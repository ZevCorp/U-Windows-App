using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using U.Graph.Surfaces;
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

        _refresh = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
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
        Task.Run(() =>
        {
            try
            {
                _refreshReader.Read();
                var rects = _refreshReader.Elements.Select(e => e.Bounds).ToList();

                // Además de UIA, si SAP GUI está delante, léelo por Scripting y pinta sus elementos. La
                // compuerta por proceso evita fantasmas: las coordenadas SAP son absolutas de pantalla,
                // así que sin SAP en primer plano dibujaríamos cajas sobre otra app.
                var sapBoxes = new List<(System.Windows.Rect, string, bool)>();
                if (IsSapForeground(_refreshReader.ForegroundProcess))
                {
                    try
                    {
                        foreach (var b in _sapReader.Read())
                            sapBoxes.Add((b.Bounds, b.Caption, b.IsShell));
                    }
                    catch { /* COM de SAP inestable: no romper el refresco de UIA */ }
                }

                _dispatcher.BeginInvoke(new Action(() =>
                {
                    _overlay?.SetNeutral(rects);
                    _overlay?.SetSap(sapBoxes);
                }));
            }
            catch { /* UIA puede lanzar en árboles inestables */ }
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

        bool mismatch = intended != null && intended.Id != clicked.Id;
        Rect c = BoxOf(clicked);
        Rect? it = (mismatch && intended != null) ? BoxOf(intended) : (Rect?)null;
        FlashOn(c, it, mismatch);
        return true;
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
