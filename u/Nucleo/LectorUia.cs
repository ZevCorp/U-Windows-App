using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Interop.UIAutomationClient;

namespace U.Ciclo;

/// <summary>
/// LOS ACCIONABLES DE LA PANTALLA, EN UNA PETICIÓN. Del lector de main se conserva el concepto que ya
/// funcionaba —el subárbol entero en una CacheRequest, 12× más rápido que nodo a nodo (su medida del
/// 2026-09-18)— y se quita lo que lo encarecía:
///
///   · COM crudo (IUIAutomation), no System.Windows.Automation: el envoltorio gestionado no deja pedir la
///     condición del lado del proveedor, así que main traía TODO el árbol y filtraba en casa.
///   · La condición viaja: solo controles accionables y en pantalla. Menos nodos cruzan el proceso.
///   · Modo None: no se piden referencias vivas, porque se pulsa con el ratón por coordenadas.
///   · Un hilo MTA propio: UIA desde un hilo STA con bombeo de mensajes es más lento y se puede bloquear
///     contra la propia ventana de la app.
/// </summary>
public sealed class LectorUia : IDisposable
{
    private const int TipoTexto = 50020;
    /// <summary>Cuántos textos viajan a Jev como mucho, y de qué largo. La pantalla, no un documento.</summary>
    public int MaxTextos { get; init; } = 30;
    private const int PropFoco = 30008;
    private const int PropNombre = 30005, PropTipo = 30003, PropCaja = 30001, PropFuera = 30022, PropHabilitado = 30010;

    private static readonly (int Id, string Nombre)[] TiposAccionables =
    {
        (50000, "Button"), (50002, "CheckBox"), (50003, "ComboBox"), (50004, "Edit"), (50005, "Hyperlink"),
        (50007, "ListItem"), (50011, "MenuItem"), (50013, "RadioButton"), (50015, "Slider"), (50019, "TabItem"),
        (50024, "TreeItem"), (50029, "DataItem"), (50031, "SplitButton"), (50035, "HeaderItem"),
    };

    private readonly BlockingCollection<Action> _cola = new();
    private readonly Thread _hilo;
    private IUIAutomation _uia = null!;
    private IUIAutomationCacheRequest _peticion = null!;
    private IUIAutomationCondition _condicion = null!;
    private string _foco = "";

    public LectorUia()
    {
        var listo = new ManualResetEventSlim();
        _hilo = new Thread(() =>
        {
            _uia = new CUIAutomation8();
            _peticion = _uia.CreateCacheRequest();
            foreach (int p in new[] { PropNombre, PropTipo, PropCaja, PropFuera, PropHabilitado, PropFoco }) _peticion.AddProperty(p);
            _peticion.AutomationElementMode = AutomationElementMode.AutomationElementMode_None;
            _peticion.TreeScope = TreeScope.TreeScope_Element;

            // Y los textos (50020): no se ofrecen para pulsar, pero dicen lo que la pantalla muestra (promesa 443).
            var tipos = TiposAccionables.Select(t => _uia.CreatePropertyCondition(PropTipo, t.Id))
                .Append(_uia.CreatePropertyCondition(PropTipo, TipoTexto)).ToArray();
            _condicion = _uia.CreateAndCondition(
                _uia.CreateOrConditionFromArray(tipos),
                _uia.CreatePropertyCondition(PropFuera, false));
            listo.Set();
            foreach (var trabajo in _cola.GetConsumingEnumerable()) trabajo();
        })
        { IsBackground = true, Name = "u-lector-uia" };
        _hilo.SetApartmentState(ApartmentState.MTA);
        _hilo.Start();
        listo.Wait();
    }

    /// <summary>
    /// Los accionables de una ventana y de sus ventanas hermanas del mismo proceso (los menús de Windows 11
    /// y los desplegables son ventanas aparte: sin ellas, abrir «Archivo» no enseñaría sus opciones).
    /// </summary>
    public Lectura Leer(IntPtr ventana)
    {
        var tcs = new TaskCompletionSource<Lectura>();
        _cola.Add(() =>
        {
            try
            {
                var crudos = LeerEnElHilo(ventana);
                var textos = crudos.Where(c => c.Tipo == "Text" && !c.FueraDePantalla && c.Caja.Ancho > 0)
                    .Select(c => (c.Nombre ?? "").Trim()).Where(t => t.Length > 0)
                    .Select(t => t.Length > 80 ? t[..80] + "…" : t).Distinct().Take(MaxTextos).ToList();
                tcs.SetResult(new Lectura(Accionables.Numerar(crudos.Where(c => c.Tipo != "Text")), textos) { Foco = _foco });
            }
            catch (Exception e) { tcs.SetException(e); }
        });
        return tcs.Task.GetAwaiter().GetResult();
    }

    private List<Crudo> LeerEnElHilo(IntPtr ventana)
    {
        var crudos = new List<Crudo>();
        _foco = "";
        if (ventana == IntPtr.Zero) return crudos;
        // Las emergentes primero: un menú abierto tapa la ventana, y lo de arriba es lo que se pulsa.
        foreach (var h in Emergentes(ventana)) LeerUna(h, crudos);
        LeerUna(ventana, crudos);
        return crudos;
    }

    private void LeerUna(IntPtr h, List<Crudo> crudos)
    {
        IUIAutomationElement raiz;
        try { raiz = _uia.ElementFromHandle(h); }
        catch (COMException) { return; }   // la ventana murió entre enumerarla y leerla
        var todos = raiz.FindAllBuildCache(TreeScope.TreeScope_Descendants, _condicion, _peticion);
        if (todos == null) return;
        for (int i = 0; i < todos.Length; i++)
        {
            var e = todos.GetElement(i);
            try
            {
                var r = e.CachedBoundingRectangle;
                int tipo = e.CachedControlType;
                if (_foco.Length == 0 && e.GetCachedPropertyValue(PropFoco) is bool conFoco && conFoco) _foco = (e.CachedName ?? "").Trim();
                crudos.Add(new Crudo(
                    e.CachedName ?? "",
                    NombreDelTipo(tipo),
                    new Caja(r.left, r.top, r.right - r.left, r.bottom - r.top),
                    e.CachedIsEnabled != 0,
                    e.CachedIsOffscreen != 0));
            }
            catch (COMException) { /* un nodo que se fue a mitad de lectura no tumba la lectura */ }
        }
    }

    private static string NombreDelTipo(int id)
    {
        if (id == TipoTexto) return "Text";
        foreach (var t in TiposAccionables) if (t.Id == id) return t.Nombre;
        return id.ToString();
    }

    // ── Las ventanas emergentes del mismo proceso ────────────────────────────────────────────────

    private delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int L, T, R, B; }

    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int max);

    /// <summary>
    /// Las ventanas visibles POR ENCIMA de la de delante en el orden Z (EnumWindows va de arriba abajo), y de
    /// ellas solo las que le pertenecen (promesa 444): la regla está en <see cref="Emergentes.Elegir"/>.
    /// </summary>
    private static IReadOnlyList<IntPtr> Emergentes(IntPtr ventana)
    {
        var encima = new List<(IntPtr, IntPtr, string, bool)>();
        GetWindowThreadProcessId(ventana, out uint pid);
        EnumWindows((h, _) =>
        {
            if (h == ventana) return false;
            if (!IsWindowVisible(h)) return true;
            if (!GetWindowRect(h, out var r) || r.R - r.L <= 1 || r.B - r.T <= 1) return true;
            GetWindowThreadProcessId(h, out uint p);
            var sb = new System.Text.StringBuilder(64);
            GetClassName(h, sb, sb.Capacity);
            encima.Add((h, GetWindow(h, 4 /* GW_OWNER */), sb.ToString(), p == pid));
            return encima.Count < 40;
        }, IntPtr.Zero);
        return U.Ciclo.Emergentes.Elegir(ventana, encima);
    }

    public void Dispose() => _cola.CompleteAdding();
}
