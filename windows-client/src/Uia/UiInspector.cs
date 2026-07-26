using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using U.Graph.Surfaces;
using U.WindowsClient.Diagnostics;
using U.WindowsClient.Ui;

namespace U.WindowsClient.Uia;

/// <summary>
/// Inspector visual de elementos UI (estilo TalkBack). Al activarse muestra recuadros sobre todos los
/// elementos accionables de la pantalla y, al hacer clic, diagnostica si el elemento cliqueado es el
/// MISMO que el asistente resolvería al replicar la acción:
///
///   - AMARILLO: cliqueaste el elemento que el asistente también tocaría (coincide).
///   - ROJO (doble): NO coincide. Marca el que cliqueaste (sólido) y el que el asistente tocaría de
///     verdad (punteado). Es el bug de ambigüedad: dos elementos con la misma etiqueta y el asistente
///     resolvería al primero.
///
/// La resolución del asistente se replica EXACTAMENTE como <see cref="UiaReader"/>: el primer elemento
/// cuya etiqueta (Name → AutomationId → HelpText) coincide, case-insensitive. Así el rojo señala el
/// mismo fallo que sufriría el agente real.
///
/// La enumeración UIA corre en un hilo de fondo (puede ser lenta) y solo los rects (structs) cruzan al
/// hilo de UI, que es quien dibuja. El overlay es click-through, así que el usuario sigue usando su app.
/// </summary>
public sealed class UiInspector : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201;

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData, flags, time; public IntPtr dwExtraInfo; }
    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto)] private static extern IntPtr GetModuleHandle(string? name);

    // Un lector por rol para no compartir el estado mutable (Elements) entre el refresco periódico y el clic.
    private readonly UiaReader _refreshReader = new();
    private readonly UiaReader _clickReader = new();
    // Mitad SAP del inspector: lee por Scripting lo que UIA no ve dentro de SAP GUI (árbol, barra, dynpro).
    private readonly SapInspectorReader _sapReader = new();
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;

    private InspectorOverlay? _overlay;
    private DispatcherTimer? _refresh;
    private int _refreshing; // 0 = libre. Compuerta anti-solapamiento; ver RefreshBoxes.
    private DispatcherTimer? _clearFlash;
    private IntPtr _hook = IntPtr.Zero;
    private HookProc? _proc; // referencia viva: si el GC lo recoge, el hook revienta.

    public bool Active { get; private set; }

    /// <summary>Alterna el inspector. Devuelve el nuevo estado (true = activo).</summary>
    public bool Toggle()
    {
        if (Active) Stop(); else Start();
        return Active;
    }

    public void Start()
    {
        if (Active) return;
        _overlay = new InspectorOverlay();
        _overlay.Show();

        _proc = HookCallback;
        _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);

        // Tick corto para que los recuadros sigan a la pantalla. Puede ser corto porque los refrescos ya
        // no se solapan (ver RefreshBoxes): si una lectura tarda más que el intervalo, los ticks de en
        // medio se descartan en vez de acumularse, así que el coste techo lo pone la lectura, no el timer.
        _refresh = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _refresh.Tick += (_, __) => RefreshBoxes();
        _refresh.Start();
        RefreshBoxes();

        Active = true;
    }

    public void Stop()
    {
        if (!Active) return;
        _refresh?.Stop(); _refresh = null;
        _clearFlash?.Stop(); _clearFlash = null;
        if (_hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
        _proc = null;
        _overlay?.Close(); _overlay = null;
        Active = false;
    }

    private void RefreshBoxes()
    {
        // UN refresco a la vez. El tick es más corto que lo que puede tardar una lectura (UIA sobre un
        // árbol grande, o el COM de SAP con la pantalla ocupada), así que sin esta compuerta los Task.Run
        // se apilaban: cada uno competía por el mismo COM de SAP y el overlay se iba quedando cada vez más
        // atrás — el síntoma era justo el contrario de lo que sugiere un timer rápido. Descartar el tick
        // mientras hay uno en vuelo mantiene el overlay pegado al último estado leído.
        if (Interlocked.Exchange(ref _refreshing, 1) == 1) return;
        Task.Run(() =>
        {
            try
            {
                _refreshReader.Read();
                var rects = _refreshReader.Elements.Select(e => e.Bounds).ToList();

                // Además de UIA, si SAP GUI está delante, léelo por Scripting y pinta sus elementos. La
                // compuerta por proceso evita fantasmas: las coordenadas SAP son absolutas de pantalla,
                // así que sin SAP en primer plano dibujaríamos cajas sobre otra app.
                var sapBoxes = new List<(System.Windows.Rect, string, bool, bool)>();
                var sapRows = new List<(System.Windows.Rect, string, bool)>();
                if (IsSapForeground(_refreshReader.ForegroundProcess))
                {
                    try
                    {
                        var els = _sapReader.ReadElements();
                        foreach (var b in _sapReader.Read(els))
                            sapBoxes.Add((b.Bounds, b.Caption, b.IsShell, b.IsMapped));
                        foreach (var r in _sapReader.ReadTreeRowBoxes(els))
                            sapRows.Add((r.Box, r.Text, r.IsFolder));
                    }
                    catch { /* COM de SAP inestable: no romper el refresco de UIA */ }
                }

                _dispatcher.BeginInvoke(new Action(() =>
                {
                    _overlay?.SetNeutral(rects);
                    _overlay?.SetSap(sapBoxes);
                    _overlay?.SetSapRows(sapRows);
                }));
            }
            catch { /* UIA puede lanzar en árboles inestables */ }
            finally { Interlocked.Exchange(ref _refreshing, 0); }
        });
    }

    /// <summary>
    /// ¿La app en primer plano es SAP GUI? El front-end de SAP GUI for Windows corre bajo
    /// <c>saplogon.exe</c> (y variantes históricas <c>sapgui</c>/<c>saplgpad</c>). Basta el prefijo
    /// "sap" para cubrirlas sin listar versiones.
    /// </summary>
    private static bool IsSapForeground(string proc) =>
        !string.IsNullOrEmpty(proc) && proc.StartsWith("sap", StringComparison.OrdinalIgnoreCase);

    private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && (int)wParam == WM_LBUTTONDOWN)
        {
            var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            int px = data.pt.X, py = data.pt.Y;
            _dispatcher.BeginInvoke(new Action(() => OnClick(px, py)));
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    private void OnClick(int px, int py)
    {
        Task.Run(() =>
        {
            try
            {
                // El _clickReader.Read() actualiza también ForegroundProcess. Si SAP está delante,
                // diagnostica por Scripting (UIA no ve nada dentro de SAP GUI); si ahí no hay nada SAP
                // en ese punto (p.ej. cliqueaste el marco de la ventana), cae al diagnóstico de UIA.
                _clickReader.Read();
                if (IsSapForeground(_clickReader.ForegroundProcess) && DiagnoseSap(px, py)) return;
                DiagnoseUia(_clickReader.Elements, px, py);
            }
            catch { }
        });
    }

    /// <summary>Diagnóstico UIA (el original): amarillo si coincide, rojo si hay ambigüedad por etiqueta.</summary>
    private void DiagnoseUia(IReadOnlyList<UiaReader.UiElement> els, int px, int py)
    {
        // Elemento cliqueado: el accionable de MENOR área que contiene el punto.
        var clicked = els
            .Where(e => !e.Bounds.IsEmpty && !double.IsInfinity(e.Bounds.Width) && e.Bounds.Contains(px, py))
            .OrderBy(e => e.Bounds.Width * e.Bounds.Height)
            .FirstOrDefault();
        if (clicked == null) return;

        // Lo que el asistente resolvería: el PRIMER elemento con la misma etiqueta (igual que UiaReader).
        var intended = els.FirstOrDefault(e =>
            string.Equals(e.Label.Trim(), clicked.Label.Trim(), StringComparison.OrdinalIgnoreCase));

        bool mismatch = intended != null && intended.Bounds != clicked.Bounds;
        InspectorDiagnostics.LogUia(px, py, clicked, intended, els, mismatch);
        Rect c = clicked.Bounds;
        Rect? it = intended?.Bounds;
        FlashOn(c, it, mismatch);
    }

    /// <summary>
    /// Diagnóstico SAP: usa el hit-test nativo (FindByPosition) como verdad de terreno de qué componente
    /// tocaste, y comprueba si el asistente, resolviendo por ETIQUETA, resolvería el MISMO (amarillo) o
    /// uno distinto con la misma etiqueta (rojo — ambigüedad, p.ej. dos favoritos con el mismo texto).
    /// A diferencia de UIA, la comparación es por <c>Id</c> de SAP (el selector real), no por bounds.
    /// Devuelve false si no hay ningún elemento SAP con caja bajo el punto (para caer a UIA).
    /// </summary>
    private bool DiagnoseSap(int px, int py)
    {
        var els = _sapReader.ReadElements();
        if (els.Count == 0) return false;

        // Verdad de terreno: hit-test nativo de SAP; si falla, la caja más pequeña que contiene el punto.
        string? hitId = _sapReader.HitTest(px, py);
        SapVisualElement? clicked =
            (hitId != null ? els.FirstOrDefault(e => e.BoundsKnown && e.Id == hitId) : null)
            ?? els.Where(e => e.BoundsKnown && Contains(e, px, py))
                  .OrderBy(e => (long)e.Width * e.Height)
                  .FirstOrDefault();
        if (clicked == null) return false;

        // Lo que el asistente resolvería si apuntara por etiqueta: el primer elemento con esa etiqueta.
        var intended = els.FirstOrDefault(e => e.BoundsKnown &&
            string.Equals(e.Label.Trim(), clicked.Label.Trim(), StringComparison.OrdinalIgnoreCase));

        // Un SHELL (árbol, grid, imagen, toolbar) no se acciona por etiqueta: se acciona por su Id, que es
        // único y que ya tenemos. Su Label es un relleno genérico —literalmente «shell»— sin ninguna
        // información, así que juzgar "¿resolvería el asistente lo mismo?" comparando etiquetas es aplicar un
        // criterio que la ejecución real no usa: los tres shells de esta pantalla comparten «shell» y el
        // veredicto salía MISMATCH en CADA clic sobre cualquiera de ellos. Peor aún, el recuadro punteado de
        // "lo que el asistente tocaría" se dibujaba sobre OTRO shell —el árbol— haciéndolo parecer culpable
        // de un problema que no tenía.
        //
        // El veredicto correcto para un shell: se conoce su Id, así que el asistente lo apunta exacto y no hay
        // ambigüedad. Para un ÁRBOL hay además una condición real —haber identificado la FILA— que se evalúa
        // más abajo, porque la fila es la unidad accionable y se acciona por clave.
        bool isShell = clicked.SubType.Length > 0;
        bool isTree = clicked.SubType.IndexOf("Tree", StringComparison.OrdinalIgnoreCase) >= 0;
        bool mismatch = !isShell && intended != null && intended.Id != clicked.Id;
        if (isShell) intended = null;

        if (!isShell) InspectorDiagnostics.LogSap(px, py, hitId, clicked, intended, els, mismatch);
        else if (!isTree)
            LogBus.Log("inspector",
                $"✓ OK @({px},{py}) [{clicked.Type}] shell subType={clicked.SubType} — se acciona por Id, " +
                "no por etiqueta, así que no hay ambigüedad posible que señalar.");

        // Clic dentro de un árbol: el shell no es la unidad accionable — la FILA sí, y se acciona por CLAVE
        // (doubleClickNode), no por etiqueta. Así que el veredicto de arriba, que compara resolución por
        // etiqueta, NO aplica aquí: los tres shells de la pantalla comparten la etiqueta genérica «shell»,
        // de modo que la comparación por etiqueta daba mismatch —rojo— en TODOS los clics de árbol aunque la
        // reproducción fuera perfecta. Se juzgaba con un criterio que la ejecución real no usa.
        //
        // Para un árbol el criterio correcto es: ¿se identificó la fila? Si sí, el asistente la reproduce con
        // su clave y no hay ambigüedad posible; si no, entonces sí hay un problema real que señalar.
        // Caja de la FILA para el destello, si se puede saber. Antes el destello enmarcaba el shell entero
        // (454×589 px) porque no había geometría por fila: iluminaba media pantalla para no decir cuál fila.
        Rect? rowBox = null;

        if (isTree)
        {
            var row = SelectedRowAfterClick(clicked.Id, out string why);
            InspectorDiagnostics.LogSapTreeNode(clicked, row, why);

            mismatch = row == null;

            if (row is { } r)
            {
                // CONTRASTE contra la realidad: se conoce la y del clic y la clave de la fila tocada, así que
                // se le pregunta a SAP por la geometría de ESA clave. Si el top que devuelve, trasladado a
                // pantalla, contiene la y del clic, entonces GetItemTop es la posición real del nodo y las
                // cajas se pueden confiar. Si no, es un valor por índice y no sirve para enmarcar.
                if (_sapReader.ItemGeometry(clicked.Id, r.Key, out int itemTop, out int itemHeight))
                {
                    double screenTop = clicked.ScreenTop + itemTop;
                    bool contains = py >= screenTop && py < screenTop + itemHeight;
                    LogBus.Log("inspector",
                        $"   ↳ CONTRASTE geometría · clic y={py} · SAP dice top={itemTop} alto={itemHeight} " +
                        $"→ en pantalla [{screenTop:0}, {screenTop + itemHeight:0}) · " +
                        $"{(contains ? "CUADRA: el top es real por nodo" : "NO CUADRA: el top no corresponde a esta fila")}");

                    // Si CUADRA no hay nada que corregir: el top crudo ya sitúa la fila. El contraste se deja
                    // como aserción viva — si algún día una pantalla o un DPI distinto dice NO CUADRA, se verá
                    // en el registro en vez de manifestarse como cajas torcidas sin explicación.
                    //
                    // Y solo si CUADRA se usa esa caja para el destello: si el contraste falla, marcar la fila
                    // señalaría el sitio equivocado, y para eso es mejor el shell entero (impreciso pero cierto).
                    if (contains)
                    {
                        // El clic también dice DÓNDE dentro de la banda está el texto, que es lo que alinea el
                        // recuadro verticalmente. Ver SapInspectorReader.ObserveRowClick.
                        _sapReader.ObserveRowClick(py, screenTop, itemHeight);
                        rowBox = new Rect(clicked.ScreenLeft + 1, screenTop,
                                          Math.Max(0, clicked.Width - 2), itemHeight);
                    }
                }
                else
                {
                    LogBus.Log("inspector", "   ↳ CONTRASTE geometría · SAP no dio geometría para la fila clicada");
                }
            }
        }

        Rect c = rowBox ?? BoxOf(clicked);
        Rect? it = (mismatch && intended != null) ? BoxOf(intended) : (Rect?)null;
        FlashOn(c, it, mismatch);
        return true;
    }

    /// <summary>
    /// La fila seleccionada del árbol, esperando a que SAP procese el clic.
    ///
    /// El hook de ratón dispara en <c>WM_LBUTTONDOWN</c>: cuando se llega aquí SAP todavía no ha atendido
    /// el clic —menos aún su viaje al servidor—, así que preguntar la selección en ese instante devuelve
    /// vacío casi siempre. Por eso el registro decía "no se pudo leer la fila bajo el clic" en TODOS los
    /// clics de árbol, que hacía parecer que los getters de selección no servían cuando el problema era
    /// llegar antes de tiempo. Se sondea hasta que SAP contesta, con techo para no bloquear el hilo de
    /// fondo si de verdad no hay fila seleccionada (clic en el fondo del árbol, p.ej.).
    /// </summary>
    private (string Key, string Text, string Via)? SelectedRowAfterClick(string treeId, out string reason)
    {
        reason = "";
        for (int i = 0; i < 12; i++)
        {
            var row = _sapReader.SelectedTreeNode(treeId, out reason);
            if (row != null) return row;
            Thread.Sleep(50);
        }

        return null;
    }

    private static bool Contains(SapVisualElement e, int px, int py) =>
        px >= e.ScreenLeft && px < e.ScreenLeft + e.Width &&
        py >= e.ScreenTop && py < e.ScreenTop + e.Height;

    private static Rect BoxOf(SapVisualElement e) =>
        new(e.ScreenLeft, e.ScreenTop, e.Width, e.Height);

    private void FlashOn(Rect clicked, Rect? intended, bool mismatch)
    {
        _dispatcher.BeginInvoke(new Action(() =>
        {
            _overlay?.Flash(clicked, intended, mismatch);
            ScheduleClear(mismatch ? 3000 : 1600);
        }));
    }

    private void ScheduleClear(int ms)
    {
        _clearFlash?.Stop();
        _clearFlash = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        _clearFlash.Tick += (_, __) =>
        {
            _clearFlash?.Stop();
            _clearFlash = null;
            _overlay?.ClearFlash();
        };
        _clearFlash.Start();
    }

    public void Dispose() => Stop();
}
