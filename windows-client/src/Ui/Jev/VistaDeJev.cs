using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Ui.Jev;

/// <summary>
/// EL COORDINADOR DE LO QUE SE VE DE JEV: crea y cierra las tres ventanas —overlay, panel y flecha—, sostiene el
/// conector y la máquina, lee la configuración y, antes de cada vuelo, le pregunta a Windows quién está delante.
/// Promesas 379 (b) y 381 (b) de la spec 049; lo que decide qué se ve es <see cref="MaquinaDeLaVista"/>, que es pura
/// y se juzga sin pantalla.
/// </summary>
/// <remarks>
/// TODO LO QUE LLEGA DE FUERA SE ENCOLA. El ciclo de Jev, las líneas de progreso y el aviso de lo pulsado
/// (<c>UiaSurface.Pulso</c>) llegan desde la tarea del tramo; la máquina y las ventanas viven en el hilo de la
/// interfaz. Cada entrada deja su trabajo en <see cref="IDespachador.Encolar"/> y vuelve: nada de aquí frena ni tumba
/// al tramo (382). La única que corre en el acto es <see cref="Sincronizar"/>, que la llama el botón de Jev, ya en el
/// hilo de la interfaz.
///
/// QUIÉN ESTÁ DELANTE SE LE PREGUNTA A WINDOWS, no se da por hecho (381). La «ventana de trabajo» es la que está en
/// el centro de lo pulsado —<c>WindowFromPoint</c>, subida a su raíz—, y está delante si es la misma raíz que la de
/// <c>GetForegroundWindow</c>. Detrás de otra, la de encima es la que está en el punto, y no hay vuelo. SIN MEDIR en
/// el PC real (D): que <c>WindowFromPoint</c> salte las ventanas nuestras que dejan pasar el ratón es lo que dice la
/// documentación del ratón, y es del nivel 4.
///
/// NADA DE JEV SE VE CON JEV APAGADO, Y LO PULSADO SOLO CUENTA EN UN TRAMO CON JEV: la mano pulsa también cuando
/// decide Luna, o el player. Cada entrada mira la máquina en el hilo de la interfaz, no al llegar: lo que se pinta lo
/// dice <see cref="MaquinaDeLaVista.QueSePinta"/> (383) y lo pulsado, <see cref="MaquinaDeLaVista.EnTramo"/> (384).
///
/// EL OVERLAY Y EL INSPECTOR NO SE ENCIENDEN A LA VEZ (371): antes de abrir los overlays se le pregunta a
/// <see cref="ExclusionConElInspector"/>, y la bandera del overlay se pone al enseñarlos y se quita al cerrarlos.
/// Hasta el 2026-09-23 solo el inspector preguntaba, y nadie ponía la bandera: la regla existía solo en el contrato.
/// </remarks>
public sealed class VistaDeJev
{
    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
    private const uint GA_ROOT = 2;
    private const int MDT_EFFECTIVE_DPI = 0;

    /// <summary>Dónde descansa la flecha respecto al ratón, en DIP (plano §Los tres modos, «siguiendo»): de ahí sale un vuelo sin flecha a la vista.</summary>
    private static readonly Vector JuntoAlRaton = new(35, 25);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT punto);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr h, uint tipo);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetCursorPos(out POINT punto);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT punto, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr hMonitor, int tipo, out uint dpiX, out uint dpiY);

    private readonly IDespachador _despachador;
    private readonly ConectorDeLaVista _conector;
    private readonly Func<Rect> _carita;

    // DEL HILO DEL TRAMO: lo que el pintor necesita para numerar y rehacer ciclos. Se cambian con Interlocked/Volatile.
    private int _paso;
    private CicloDeJev? _ultimoDecidido;
    private string _objetivo = "";

    // DEL HILO DE LA INTERFAZ: las ventanas y lo pintado.
    private PanelDeJev? _panel;
    private IReadOnlyList<OverlayDeJev> _overlays = Array.Empty<OverlayDeJev>();
    private FlechaDeJev? _flecha;
    private CosteDeJev _coste = new();
    private CajasDelOverlay _cajas = CajasDelOverlay.Vacio;
    private int _barrasPintadas;

    /// <param name="interfaz">El <see cref="Dispatcher"/> del hilo de la interfaz: el de las ventanas.</param>
    /// <param name="carita">
    /// Dónde está la carita ENTERA, en físicos —su ventana, con la barra y el menú—: lo que el panel no tapa (377).
    /// Se pregunta al encender y antes de cada recolocación, porque abrir el menú le cambia el tamaño; no se da por
    /// hecha una.
    /// </param>
    public VistaDeJev(Dispatcher interfaz, Func<Rect> carita)
    {
        ArgumentNullException.ThrowIfNull(interfaz);
        _carita = carita ?? throw new ArgumentNullException(nameof(carita));
        _despachador = new DespachadorDeWpf(interfaz);
        _conector = new ConectorDeLaVista(_despachador, Pintar, linea => LogBus.Log("jev-vista", linea));
        // EL OVERLAY LO ENCIENDE U_JEV_OVERLAY=si (379). Se lee aquí, al crear la vista: el entorno de un proceso no
        // cambia mientras corre, así que leerlo en cada encendido daría lo mismo.
        Maquina = new MaquinaDeLaVista(ConfiguracionDeLaVista.Leer(Environment.GetEnvironmentVariable));
        LogBus.Log("jev-vista", $"vista de Jev creada · {Maquina.Estado}");
    }

    /// <summary>Qué se ve de Jev ahora mismo. Vive en el hilo de la interfaz.</summary>
    public MaquinaDeLaVista Maquina { get; }

    /// <summary>Si corre un tramo con Jev: lo que <c>FaceWindow</c> le pasa a <see cref="ReglaDeQuienVuela.LaCaritaViaja"/> (384).</summary>
    public bool EnTramo => Maquina.EnTramo;

    /// <summary>
    /// Un ciclo del decisor, desde <see cref="ObservadorDelDecisor.Envolver"/> en la tarea del tramo. Numera el paso
    /// —la costura del decisor no lo sabe (hallazgo (a) de la fase 6)—, lo guarda para las líneas que vengan detrás y
    /// lo encola. Nunca lanza ni espera.
    /// </summary>
    public void Publicar(CicloDeJev ciclo)
    {
        ArgumentNullException.ThrowIfNull(ciclo);
        if (ciclo.Fase == FaseDelCiclo.Decidido)
        {
            if (ciclo.Paso == 0) ciclo = ciclo with { Paso = Interlocked.Increment(ref _paso) };
            Volatile.Write(ref _ultimoDecidido, ciclo);
        }
        _conector.Publicar(ciclo);
    }

    /// <summary>Empieza un tramo: el panel enseña el objetivo y «Mirando la pantalla».</summary>
    public void AlEmpezarTramo(string objetivo)
    {
        string limpio = string.IsNullOrWhiteSpace(objetivo) ? "" : objetivo.Trim();
        Interlocked.Exchange(ref _paso, 0);
        Volatile.Write(ref _ultimoDecidido, null);
        Volatile.Write(ref _objetivo, limpio);
        Encolar("empezar un tramo", () =>
        {
            Maquina.AlEmpezarTramo(limpio);
            _coste = new CosteDeJev();   // una tarea nueva es un coste nuevo (CosteDeJev)
        });
        _conector.Publicar(new CicloDeJev { Objetivo = limpio, Fase = FaseDelCiclo.Mirando });
    }

    /// <summary>
    /// Una línea de progreso del tramo: el último ciclo decidido con la línea puesta y, si la línea dice qué pulsó la
    /// mano, con su número (<see cref="CicloDeJev.ConLaLinea"/>, 372): la mano puede haber pulsado la segunda mejor, y
    /// solo la línea lo cuenta.
    /// </summary>
    public void Progreso(string linea)
    {
        if (string.IsNullOrWhiteSpace(linea)) return;
        _conector.Publicar(CicloDeJev.ConLaLinea(Volatile.Read(ref _ultimoDecidido), Volatile.Read(ref _objetivo), linea));
    }

    /// <summary>Termina el tramo. El panel se queda enseñando cómo acabó; la flecha se esconde sola al terminar de señalar.</summary>
    public void AlTerminarTramo() => Encolar("terminar el tramo", () => Maquina.AlTerminarTramo());

    /// <summary>
    /// La mano pulsó esta caja (<c>UiaSurface.Pulso</c>, en físicos, desde la tarea del tramo). En la interfaz: la
    /// rosa, el panel que se aparta y, si la ventana de trabajo está delante, el vuelo.
    /// </summary>
    public void AlPulsar(double x, double y, double ancho, double alto) =>
        Encolar("pintar lo pulsado", () => Pulsada(new Rect(x, y, Math.Max(0, ancho), Math.Max(0, alto))));

    /// <summary>
    /// Escape o se soltó lo señalado (383): el overlay vacío, la flecha escondida y el panel sin corrida. La última
    /// decisión y el objetivo se olvidan aquí mismo, en el hilo que avisa: la línea con que el tramo para llega
    /// después (<c>ElTramo.cs:191</c>) y no puede volver a traer las barras (hallazgo de la revisión del 2026-09-23).
    /// </summary>
    public void Suelta()
    {
        Volatile.Write(ref _ultimoDecidido, null);
        Volatile.Write(ref _objetivo, "");
        Encolar("soltar lo señalado", () =>
        {
            Maquina.Suelta();
            PintarCajas(CajasDelOverlay.Vacio);
            _flecha?.Esconder();
            PintarPanel(Reposo);
        });
    }

    /// <summary>
    /// Se enciende o se apaga Jev (el botón, después del interruptor del decisor). Encender pone la pantalla ANTES de
    /// encender la máquina —cambiarla después no recoloca el panel (hallazgo (e) de la fase 6)—, y crea el panel, los
    /// overlays si la configuración los enciende, y la flecha escondida. Apagar las cierra las tres (383). En el hilo
    /// de la interfaz.
    /// </summary>
    public void Sincronizar(bool encendido)
    {
        if (encendido == Maquina.Encendida) return;
        if (!encendido)
        {
            Maquina.Apagar();
            int overlays = _overlays.Count;
            foreach (var o in _overlays) o.Close();
            _overlays = Array.Empty<OverlayDeJev>();
            ExclusionConElInspector.OverlayActivo = false;   // el inspector ya puede encenderse (371)
            _panel?.Close(); _panel = null;
            _flecha?.Close(); _flecha = null;
            _cajas = CajasDelOverlay.Vacio;
            _barrasPintadas = 0;
            LogBus.Log("jev-vista", $"Jev apagado: cerrados el panel, la flecha y {overlays} overlay(s)");
            return;
        }

        PonerLaPantalla();
        Maquina.Encender();
        _barrasPintadas = 0;   // el panel nace vacío, y su sitio se calculó con ese alto
        _panel = new PanelDeJev();
        if (Maquina.RectDelPanel is Rect sitio) _panel.Colocar(sitio);
        else LogBus.Log("jev-vista", "el panel nace sin sitio calculado (sin área de trabajo o sin escala del monitor de la carita): se enseña donde lo ponga Windows");
        _panel.Show();

        // EL INSPECTOR ENCENDIDO IMPIDE EL OVERLAY (371): comparten ámbar, verde y rosa con otro significado. Se
        // pregunta ANTES de crear ninguna ventana, y la máquina lo sabe: con el overlay impedido no se da por visible.
        if (Maquina.OverlayVisible && !ExclusionConElInspector.PuedeEncender(ExclusionConElInspector.Cual.Overlay))
            Maquina.ImpedirElOverlay(ExclusionConElInspector.PorQueNo(ExclusionConElInspector.Cual.Overlay)
                ?? "el inspector está encendido (PuedeEncender dijo que no y PorQueNo no dio el motivo)");
        if (Maquina.OverlayVisible)
        {
            _overlays = OverlayDeJev.ParaCadaMonitor();
            foreach (var o in _overlays)
            {
                o.Caducaron += (_, _) => { _cajas = CajasDelOverlay.Vacio; Maquina.AlPintarCajas(0); };
                o.Show();
            }
            if (_overlays.Count > 0) ExclusionConElInspector.OverlayActivo = true;   // y desde aquí el inspector no se enciende
        }
        _flecha = new FlechaDeJev();
        LogBus.Log("jev-vista", $"Jev encendido · {Maquina.Estado} · panel en {(Maquina.RectDelPanel?.ToString() ?? "sin sitio")} · {_overlays.Count} overlay(s) · flecha escondida hasta el primer vuelo");
    }

    /// <summary>
    /// EL PINTOR DEL CONECTOR, en el hilo de la interfaz: pinta lo que la máquina dice que se pinta
    /// (<see cref="MaquinaDeLaVista.QueSePinta"/>, 383). El panel pinta lo que dice <see cref="EstadoDeLaDecision.De"/>
    /// y, si el ciclo es una decisión, el overlay pinta lo que dice <see cref="CajasDelOverlay.De"/>. Aquí no se
    /// decide nada: ni se ordena ni se filtra.
    /// </summary>
    private void Pintar(CicloDeJev ciclo)
    {
        var aPintar = Maquina.QueSePinta(ciclo);
        if (aPintar == null)
        {
            // LO QUE NO SE PINTA SE FACTURA IGUAL: una decisión que llega tras Escape ya se pagó (373).
            if (ciclo.Fase == FaseDelCiclo.Decidido) _coste.Acumular(ciclo.TokensFacturados);
            return;
        }
        PintarPanel(EstadoDeLaDecision.De(aPintar, _coste));
        if (aPintar.Fase == FaseDelCiclo.Decidido) PintarCajas(CajasDelOverlay.De(aPintar.Candidatas, aPintar.Pulsada));
    }

    /// <summary>
    /// El panel pinta esto y, si cambió cuántas barras enseña, su sitio se recalcula con el alto nuevo (385): la
    /// máquina lo calcula con <see cref="MedidaDelPanelDeJev.AltoDe"/> y la ventana se mueve ahí. Hasta el 2026-09-23
    /// el sitio era siempre el del panel vacío (64), y con cinco barras (198,6) el panel tapaba la carita o lo pulsado.
    /// </summary>
    private void PintarPanel(LoQuePinta loQuePinta)
    {
        if (_panel == null) return;
        _panel.Pintar(loQuePinta);
        int barras = loQuePinta.Resultados?.Barras.Count ?? 0;
        if (barras == _barrasPintadas) return;
        _barrasPintadas = barras;
        LeerLaCarita();
        Maquina.AlPintarBarras(barras);
        // EL ALTO LO DA EL CONTENIDO (SizeToContent): se mide ya, para que la ventana tenga el alto nuevo cuando se
        // mueva y el CONTRASTE del panel compare el tamaño con que se calculó el sitio con el de verdad.
        _panel.UpdateLayout();
        if (Maquina.RectDelPanel is Rect sitio) _panel.Colocar(sitio);
    }

    private void PintarCajas(CajasDelOverlay cajas)
    {
        _cajas = cajas;
        foreach (var o in _overlays) o.Pintar(cajas);
        Maquina.AlPintarCajas(cajas.Cajas.Count);
    }

    /// <summary>
    /// LO PULSADO, en el hilo de la interfaz. La rosa pasa a la candidata que calza con la caja de la mano; el panel se
    /// aparta; y la flecha vuela si Windows dice que la ventana de trabajo está delante (381). Solo en un tramo con
    /// Jev: fuera de él pulsó Luna, o el player, y la carita es la que viaja (384).
    /// </summary>
    private void Pulsada(Rect caja)
    {
        // FUERA DE UN TRAMO CON JEV, ni rosa, ni panel que se aparte, ni flecha. Hasta el 2026-09-23 se miraba
        // «encendida»: con Jev encendido y sin tramo, la carita viajaba al clic y la flecha volaba al mismo clic.
        if (!Maquina.EnTramo) return;
        if (!CajasDelOverlay.EsPintable(caja))
        {
            LogBus.Log("jev-vista", $"la mano avisó de una caja sin sitio ({caja}): ni rosa, ni vuelo");
            return;
        }
        var centro = new Point(caja.X + caja.Width / 2, caja.Y + caja.Height / 2);
        bool delante = LaDeTrabajoEstaDelante(centro, out string quien);
        LeerLaCarita();
        Maquina.AlConocerPulsada(caja, delante);
        if (Maquina.RectDelPanel is Rect sitio) _panel?.Colocar(sitio);

        var conRosa = _cajas.ConPulsada(caja);
        if (Maquina.OverlayVisible) PintarCajas(conRosa);
        string etiqueta = conRosa.Cajas.FirstOrDefault(c => c.Rosa)?.Etiqueta ?? "";

        if (_flecha == null) return;
        if (!Maquina.FlechaVisible)
        {
            _flecha.Esconder();
            LogBus.Log("jev-vista", $"la flecha no vuela a {caja}: {quien}");
            return;
        }
        if (!EscalaEn(centro, out double escala, out string sinEscala))
        {
            _flecha.Esconder();
            LogBus.Log("jev-vista", $"la flecha no vuela a {caja}: {sinEscala}");
            return;
        }
        var inicio = _flecha.Centro ?? DondeEstaElRaton(escala);
        if (inicio is not Point desde)
        {
            LogBus.Log("jev-vista", $"la flecha no vuela a {caja}: está escondida y GetCursorPos falló (error {Marshal.GetLastWin32Error()}), así que no hay de dónde salir");
            return;
        }
        var plan = PlanDeVuelo.Calcular(desde, centro, escala, appDelante: delante);
        if (plan == null) { _flecha.Esconder(); return; }
        _flecha.Volar(plan, escala, etiqueta);
    }

    /// <summary>
    /// SE LE PREGUNTA A WINDOWS, cada vez: ¿la ventana que está en el centro de lo pulsado es la de delante? Las dos
    /// subidas a su raíz, porque en el punto hay un control hijo y delante está su ventana de nivel superior.
    /// </summary>
    /// <param name="quien">Lo que contestó Windows, con los dos handles: va al log tal cual si no hay vuelo.</param>
    private static bool LaDeTrabajoEstaDelante(Point centro, out string quien)
    {
        IntPtr deDelante = GetForegroundWindow();
        IntPtr enElPunto = WindowFromPoint(new POINT { X = (int)Math.Round(centro.X), Y = (int)Math.Round(centro.Y) });
        IntPtr raizDeDelante = deDelante == IntPtr.Zero ? IntPtr.Zero : GetAncestor(deDelante, GA_ROOT);
        IntPtr raizDelPunto = enElPunto == IntPtr.Zero ? IntPtr.Zero : GetAncestor(enElPunto, GA_ROOT);
        bool si = raizDeDelante != IntPtr.Zero && raizDeDelante == raizDelPunto;
        quien = si
            ? $"la de delante (0x{raizDeDelante.ToInt64():X}) es la que está en lo pulsado"
            : raizDeDelante == IntPtr.Zero
                ? "GetForegroundWindow no devolvió ninguna ventana"
                : raizDelPunto == IntPtr.Zero
                    ? $"WindowFromPoint no devolvió ninguna ventana en ({centro.X:0}, {centro.Y:0}); delante está 0x{raizDeDelante.ToInt64():X}"
                    : $"delante está 0x{raizDeDelante.ToInt64():X} y en lo pulsado, 0x{raizDelPunto.ToInt64():X}: la ventana de trabajo está detrás";
        return si;
    }

    /// <summary>La escala del monitor de llegada, preguntada a Windows en el centro de lo pulsado.</summary>
    private static bool EscalaEn(Point punto, out double escala, out string porque)
    {
        escala = 0;
        var hMonitor = MonitorFromPoint(new POINT { X = (int)Math.Round(punto.X), Y = (int)Math.Round(punto.Y) }, MONITOR_DEFAULTTONEAREST);
        if (hMonitor == IntPtr.Zero)
        {
            porque = $"MonitorFromPoint no devolvió monitor en ({punto.X:0}, {punto.Y:0}), así que no hay escala de llegada";
            return false;
        }
        int hr = GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out uint dpi, out _);
        if (hr != 0)
        {
            porque = $"GetDpiForMonitor del monitor en ({punto.X:0}, {punto.Y:0}) devolvió 0x{hr:X8}, así que no hay escala de llegada";
            return false;
        }
        escala = dpi / 96.0;
        porque = "";
        return true;
    }

    /// <summary>Junto al ratón, donde descansaría la flecha (plano): de ahí sale un vuelo cuando no hay flecha a la vista.</summary>
    private static Point? DondeEstaElRaton(double escala) =>
        GetCursorPos(out var p) ? new Point(p.X, p.Y) + JuntoAlRaton * escala : null;

    /// <summary>
    /// La pantalla de la máquina, con el monitor de la carita: su área de trabajo y su escala, el panel vacío por esa
    /// escala, la carita y el notch. Si Windows no da el monitor, se queda sin área y el panel sin sitio, y se dice.
    /// </summary>
    private void PonerLaPantalla()
    {
        var carita = _carita();
        var ancla = new Point(carita.X + carita.Width / 2, carita.Y + carita.Height / 2);
        var hMonitor = MonitorFromPoint(new POINT { X = (int)Math.Round(ancla.X), Y = (int)Math.Round(ancla.Y) }, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (hMonitor == IntPtr.Zero || !GetMonitorInfo(hMonitor, ref info))
        {
            LogBus.Log("jev-vista", $"sin área de trabajo para el panel: GetMonitorInfo del monitor de la carita en {ancla} falló (error {Marshal.GetLastWin32Error()})");
            Maquina.RcWork = Rect.Empty;
            return;
        }
        int hr = GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out uint dpi, out _);
        if (hr != 0)
        {
            LogBus.Log("jev-vista", $"sin escala para el panel: GetDpiForMonitor del monitor de la carita devolvió 0x{hr:X8}");
            Maquina.RcWork = Rect.Empty;
            return;
        }
        double escala = dpi / 96.0;
        Maquina.RcWork = new Rect(info.rcWork.Left, info.rcWork.Top, info.rcWork.Right - info.rcWork.Left, info.rcWork.Bottom - info.rcWork.Top);
        Maquina.Escala = escala;
        // EL PANEL NACE VACÍO, y su sitio se calcula con ese alto (64). Al crecer con las barras, PintarPanel se lo
        // dice a la máquina y el sitio se recalcula con el alto nuevo (385, revisión del 2026-09-23).
        Maquina.TamanoDelPanel = new Size(MedidaDelPanelDeJev.Ancho * escala, MedidaDelPanelDeJev.AltoDe(0) * escala);
        Maquina.Carita = carita;
        Maquina.Obstaculos = PanelDeJev.Obstaculos();
    }

    /// <summary>
    /// La carita se vuelve a preguntar antes de recolocar el panel: abrir o cerrar su menú le cambia el tamaño, y el
    /// rect de cuando se encendió Jev dejaría el panel encima del menú abierto después. Si Windows no la da, se queda
    /// la de antes y se dice.
    /// </summary>
    private void LeerLaCarita()
    {
        try { Maquina.Carita = _carita(); }
        catch (Exception e)
        {
            for (var x = e; x != null; x = x.InnerException)
                LogBus.Log("jev-vista", $"✘ no pude volver a leer dónde está la carita ({x.GetType().Name}: {x.Message}); el panel se coloca con la de antes, {Maquina.Carita}");
        }
    }

    /// <summary>El panel sin corrida: sin objetivo, sin ticker y sin resultados, como nace.</summary>
    private static LoQuePinta Reposo => new("", "", 0, "", null);

    /// <summary>Deja el trabajo en el hilo de la interfaz. Si WPF ya no lo acepta —la app cerrándose—, se dice y se sigue: el tramo no se entera.</summary>
    private void Encolar(string que, Action accion)
    {
        try { _despachador.Encolar(accion); }
        catch (Exception e)
        {
            for (var x = e; x != null; x = x.InnerException)
                LogBus.Log("jev-vista", $"✘ no se pudo encolar «{que}»: {x.GetType().Name}: {x.Message}");
        }
    }
}
