using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Ui.Jev;

/// <summary>
/// LAS CAJAS DE JEV, EN UN MONITOR. Una ventana por monitor que dibuja exactamente lo que devuelve
/// <see cref="CajasDelOverlay"/> y nada más: ni ordena, ni filtra, ni busca por etiqueta. Promesas 375 (b), 379 (b) y
/// 380 (b) de la spec 049; lo que decide qué se pinta es puro y se juzga sin pantalla (375 (a)).
/// </summary>
/// <remarks>
/// UNA POR MONITOR, CON SU <c>rcMonitor</c> ENTERO EN FÍSICOS (379). Las otras ventanas de pantalla completa de Ü
/// —<c>AuraDeAprendizaje.cs:123-124</c>, <c>HighlightOverlay.cs:52-53</c>, <c>InspectorOverlay.cs:88-89</c>— se
/// miden con <c>SystemParameters.PrimaryScreenWidth</c> y dejan sin cubrir los demás monitores (3 sitios contados el
/// 2026-09-23; la spec decía 2). Esta no repite el patrón: nace de <see cref="ParaCadaMonitor"/>, que enumera los
/// monitores, y se pone con <c>SetWindowPos</c> en físicos, nunca con <c>Left/Top</c> en DIP, que son ambiguos al
/// cruzar entre monitores de escala distinta.
///
/// NO TOMA EL RATÓN NI EL FOCO, Y NO SALE EN LAS CAPTURAS (379): aplica la máscara extendida y la afinidad de
/// <see cref="EstilosDeVentana"/>. Si la exclusión de la captura falla se dice en el log y se sigue: Ü no
/// decide mirando píxeles, así que un overlay grabado es un defecto a la vista, no una decisión torcida.
///
/// LAS CAJAS CADUCAN AL CAMBIAR LA VENTANA DE DELANTE (375): <c>SetWinEventHook(EVENT_SYSTEM_FOREGROUND)</c> y, desde
/// ahí, <see cref="CajasDelOverlay.Caducar"/>. Lo que se pintó era de lo que había delante; si delante hay otra
/// cosa, las cajas señalarían encima de algo que nadie ofreció (aprendizaje nº4). Al revés que TipTour, que deja
/// cajas de la terminal sobre Music (plano, t=73).
///
/// TODO VA POR <see cref="Pantallas"/> (380): cada caja llega en físicos del escritorio virtual y se lleva a la unidad
/// de ESTE monitor con <see cref="Pantallas.DelMonitor"/>, que resta su origen. Al cambiar el DPI se vuelve a aplicar
/// el rect calculado (<see cref="ReglaDeDpi.RectTrasCambio"/>) y no el que sugiere Windows.
///
/// DOS CAPAS Y NO CUATRO: el plano (§Cómo se pinta) lleva cuatro, pero la 1 y la 3 son las del burbuja en reposo, que
/// la spec deja fuera (§Lo que NO entra). Quedan el pulso —los halos que respiran, con UNA animación de opacidad— y
/// las cajas, que solo se repintan cuando llega una lista nueva.
/// </remarks>
public sealed class OverlayDeJev : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WM_WINDOWPOSCHANGING = 0x0046;
    private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000, WINEVENT_SKIPOWNPROCESS = 0x0002;
    private const int MDT_EFFECTIVE_DPI = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPOS { public IntPtr hwnd, hwndInsertAfter; public int x, y, cx, cy; public uint flags; }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr rect, IntPtr data);
    private delegate void WinEventProc(IntPtr hook, uint evento, IntPtr hwnd, int idObject, int idChild, uint hilo, uint ms);

    [DllImport("user32.dll", SetLastError = true)] private static extern int GetWindowLong(IntPtr h, int indice);
    [DllImport("user32.dll", SetLastError = true)] private static extern int SetWindowLong(IntPtr h, int indice, int valor);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowDisplayAffinity(IntPtr h, uint afinidad);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr h, IntPtr tras, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr recorte, MonitorEnumProc proc, IntPtr datos);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr hMonitor, int tipo, out uint dpiX, out uint dpiY);
    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr modulo, WinEventProc proc, uint proceso, uint hilo, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);

    /// <summary>El <c>rcMonitor</c> de este monitor, en físicos: lo que la ventana cubre, y de donde no se mueve.</summary>
    private readonly Rect _rcMonitor;
    private readonly Lienzo _lienzo = new();

    /// <summary>La unidad de este monitor. Cambia con el DPI (<see cref="OnDpiChanged"/>), por eso no es de solo lectura.</summary>
    private ConversorDeMonitor _conversor;

    /// <summary>El rect que la ventana tiene que tener, en físicos. <c>null</c> hasta que exista el HWND.</summary>
    private Rect? _rectAplicado;

    private IntPtr _handle, _gancho;

    /// <summary>
    /// El delegado del gancho, sujeto en un campo: Windows solo guarda el puntero, y si el recolector se llevara el
    /// delegado el siguiente cambio de primer plano llamaría a memoria liberada.
    /// </summary>
    private WinEventProc? _alCambiarDePrimerPlano;

    /// <summary>
    /// La ventana de un monitor. No se muestra sola: la muestra y la esconde quien la maneja.
    /// </summary>
    /// <param name="rcMonitor">El <c>rcMonitor</c> de <c>GetMonitorInfo</c>, en físicos.</param>
    /// <param name="escala">La del monitor, <c>GetDpiForMonitor</c> / 96.</param>
    /// <exception cref="ArgumentOutOfRangeException">Con una escala que no es un número positivo (lo dice <see cref="Pantallas.DelMonitor"/>).</exception>
    /// <exception cref="ArgumentException">
    /// Con un <paramref name="rcMonitor"/> vacío o sin área: <see cref="ReglaDeDpi.RectTrasCambio"/> lo devolvería tal
    /// cual y la ventana se pondría en <c>Rect.Empty</c> (spec 049, hallazgo de la fase 4, (b)). No nace.
    /// </exception>
    public OverlayDeJev(Rect rcMonitor, double escala)
    {
        if (!CajasDelOverlay.EsPintable(rcMonitor))
            throw new ArgumentException($"el rcMonitor de un overlay tiene que tener área, y llegó {rcMonitor}", nameof(rcMonitor));
        _rcMonitor = rcMonitor;
        _conversor = Pantallas.DelMonitor(rcMonitor, escala);

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        IsHitTestVisible = false;
        Focusable = false;
        Title = "Jev · overlay";
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.Grayscale);
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        Content = _lienzo;

        SiempreDelante.EntraAlGrupo(this, Capa.Overlays);
    }

    /// <summary>Lo que se está pintando ahora: la última lista que llegó, o la vacía si caducó.</summary>
    public CajasDelOverlay Cajas { get; private set; } = CajasDelOverlay.Vacio;

    /// <summary>
    /// Las cajas caducaron porque cambió la ventana de delante. Quien maneja el overlay lo oye para que su cuenta de
    /// cajas visibles no se quede con las de antes (<see cref="MaquinaDeLaVista.AlPintarCajas"/>).
    /// </summary>
    public event EventHandler? Caducaron;

    /// <summary>
    /// Un overlay por monitor, sin mostrar. Un monitor del que Windows no da el rect o la escala se queda sin overlay
    /// y se dice en el log con el paso que falló; los demás siguen. La lista de rects la decide
    /// <see cref="EstilosDeVentana.UnaPorMonitor"/>, que es la regla pura (379).
    /// </summary>
    public static IReadOnlyList<OverlayDeJev> ParaCadaMonitor()
    {
        var rcs = new List<Rect>();
        var escalas = new Dictionary<Rect, double>();
        int vistos = 0;
        bool enumerado = EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMonitor, _, _, _) =>
        {
            vistos++;
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(hMonitor, ref info))
            {
                LogBus.Log("jev-overlay", $"el monitor {vistos} se queda sin overlay: GetMonitorInfo falló (error {Marshal.GetLastWin32Error()})");
                return true;
            }
            var rc = new Rect(info.rcMonitor.Left, info.rcMonitor.Top,
                info.rcMonitor.Right - info.rcMonitor.Left, info.rcMonitor.Bottom - info.rcMonitor.Top);
            int hr = GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out uint dpiX, out _);
            if (hr != 0)
            {
                LogBus.Log("jev-overlay", $"el monitor {vistos} ({rc}) se queda sin overlay: GetDpiForMonitor devolvió 0x{hr:X8}");
                return true;
            }
            rcs.Add(rc);
            escalas[rc] = dpiX / 96.0;
            return true;
        }, IntPtr.Zero);
        if (!enumerado)
            LogBus.Log("jev-overlay", $"EnumDisplayMonitors devolvió false después de {vistos} monitor(es): puede haber monitores sin overlay");

        var overlays = new List<OverlayDeJev>();
        foreach (var rc in EstilosDeVentana.UnaPorMonitor(rcs))
        {
            if (!escalas.TryGetValue(rc, out double escala))
            {
                LogBus.Log("jev-overlay", $"el rect {rc} no es de ningún monitor enumerado: no se le pone overlay");
                continue;
            }
            // ArgumentOutOfRangeException (escala) es un ArgumentException (rect sin área): los dos, con su tipo.
            try { overlays.Add(new OverlayDeJev(rc, escala)); }
            catch (ArgumentException e) { LogBus.Log("jev-overlay", $"el monitor {rc} se queda sin overlay: {e.GetType().Name}: {e.Message}"); }
        }
        LogBus.Log("jev-overlay", $"{overlays.Count} overlay(s) para {vistos} monitor(es): [{string.Join(" · ", overlays.Select(o => $"{o._rcMonitor} ×{o._conversor.Escala:0.##}"))}]");
        return overlays;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _handle = new WindowInteropHelper(this).Handle;

        // LAS MÁSCARAS (379), sobre las que la ventana ya tenga, y se COMPRUEBAN después (aprendizaje nº19): un
        // SetWindowLong que no agarra dejaría un overlay que se come los clics de la app de trabajo.
        // La máscara se nombra UNA vez, en la línea que la toma: la 379 lo lee por fuente, y un nombre repetido en
        // comentarios o en la comprobación la dejaría en verde aunque la ventana usara una máscara escrita a mano.
        uint mascara = EstilosDeVentana.ExtendidosDelOverlay;
        int antes = GetWindowLong(_handle, GWL_EXSTYLE);
        SetWindowLong(_handle, GWL_EXSTYLE, antes | unchecked((int)mascara));
        uint quedo = unchecked((uint)GetWindowLong(_handle, GWL_EXSTYLE));
        if ((quedo & mascara) != mascara)
            LogBus.Log("jev-overlay", $"el overlay de {_rcMonitor} quedó con estilos 0x{quedo:X8} y le faltan 0x{mascara & ~quedo:X8}: puede tomar el ratón o el foco");
        if (!SetWindowDisplayAffinity(_handle, EstilosDeVentana.Afinidad))
            LogBus.Log("jev-overlay", $"no se pudo excluir de la captura el overlay de {_rcMonitor} (SetWindowDisplayAffinity, error {Marshal.GetLastWin32Error()}): saldrá en capturas y grabaciones");

        HwndSource.FromHwnd(_handle)?.AddHook(MantenerElRect);
        Aplicar(_rcMonitor, "al nacer");

        _alCambiarDePrimerPlano = AlCambiarDePrimerPlano;
        _gancho = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _alCambiarDePrimerPlano,
            0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        if (_gancho == IntPtr.Zero)
            LogBus.Log("jev-overlay", $"SetWinEventHook(EVENT_SYSTEM_FOREGROUND) devolvió 0 en el overlay de {_rcMonitor}: sus cajas NO caducarán al cambiar la ventana de delante");
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_gancho != IntPtr.Zero && !UnhookWinEvent(_gancho))
            LogBus.Log("jev-overlay", $"UnhookWinEvent devolvió false al cerrar el overlay de {_rcMonitor}");
        _gancho = IntPtr.Zero;
        _lienzo.Parar();
        base.OnClosed(e);
    }

    /// <summary>
    /// Pinta exactamente estas cajas. Se llama en el hilo de la interfaz: quien publica desde otro hilo pasa por el
    /// despachador de la vista, que es el único sitio de <c>Ui/Jev</c> que encola en WPF (382).
    /// </summary>
    public void Pintar(CajasDelOverlay cajas)
    {
        ArgumentNullException.ThrowIfNull(cajas);
        Cajas = cajas;
        Repintar();
    }

    private void Repintar() =>
        _lienzo.Pintar(Cajas.Cajas, _conversor, _conversor.AUnidad(_rcMonitor.BottomRight).X);

    /// <summary>Pone la ventana en <paramref name="rect"/> (físicos) sin tocar su sitio en Z, que es del grupo (378).</summary>
    private void Aplicar(Rect rect, string cuando)
    {
        _rectAplicado = rect;
        if (!SetWindowPos(_handle, IntPtr.Zero, (int)Math.Round(rect.X), (int)Math.Round(rect.Y),
                (int)Math.Round(rect.Width), (int)Math.Round(rect.Height), SWP_NOZORDER | SWP_NOACTIVATE))
            LogBus.Log("jev-overlay", $"SetWindowPos {cuando} a {rect} falló (error {Marshal.GetLastWin32Error()}): el overlay no cubre su monitor y sus cajas caerán desplazadas");
    }

    /// <summary>
    /// EL RECT NO SE MUEVE, LO MUEVA QUIEN LO MUEVA. WPF pone la ventana con sus <c>Left/Top/Width/Height</c> al
    /// mostrarla y con el rect que sugiere Windows al cambiar el DPI, y <see cref="OnDpiChanged"/> no recibe ese
    /// sugerido ni dice si WPF lo aplica antes o después. En vez de apostar por el orden, cada <c>WM_WINDOWPOSCHANGING</c>
    /// que traiga posición o tamaño se reescribe con el rect aplicado. Las llamadas que solo cambian el orden en Z
    /// (las del grupo, con <c>SWP_NOMOVE | SWP_NOSIZE</c>) pasan intactas.
    /// </summary>
    private IntPtr MantenerElRect(IntPtr h, int mensaje, IntPtr wParam, IntPtr lParam, ref bool manejado)
    {
        if (mensaje != WM_WINDOWPOSCHANGING || _rectAplicado is not Rect r) return IntPtr.Zero;
        var pos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
        if ((pos.flags & SWP_NOMOVE) == 0) { pos.x = (int)Math.Round(r.X); pos.y = (int)Math.Round(r.Y); }
        if ((pos.flags & SWP_NOSIZE) == 0) { pos.cx = (int)Math.Round(r.Width); pos.cy = (int)Math.Round(r.Height); }
        Marshal.StructureToPtr(pos, lParam, false);
        return IntPtr.Zero;
    }

    /// <summary>
    /// CAMBIÓ EL DPI (380): la unidad del monitor es otra, así que el conversor se rehace con la escala nueva por
    /// <see cref="Pantallas.DelMonitor"/>, se vuelve a aplicar el rect CALCULADO —<see cref="ReglaDeDpi.RectTrasCambio"/>
    /// con el que la ventana tiene en este momento, que es lo más cerca del sugerido que WPF deja ver aquí— y las
    /// cajas se repintan en la unidad nueva.
    /// </summary>
    protected override void OnDpiChanged(DpiScale viejo, DpiScale nuevo)
    {
        base.OnDpiChanged(viejo, nuevo);
        try { _conversor = Pantallas.DelMonitor(_rcMonitor, nuevo.PixelsPerDip); }
        catch (ArgumentOutOfRangeException e)
        {
            // Sin conversor bueno las cajas caerían en la unidad vieja: desplazadas, que es la caja que miente
            // (aprendizaje nº4). Se vacía el overlay y se dice por qué.
            LogBus.Log("jev-overlay", $"el overlay de {_rcMonitor} se vacía al cambiar el DPI: {e.GetType().Name}: {e.Message}");
            Cajas = CajasDelOverlay.Vacio;
        }
        if (_handle != IntPtr.Zero)
        {
            var ahora = GetWindowRect(_handle, out var w) ? new Rect(w.Left, w.Top, w.Right - w.Left, w.Bottom - w.Top) : Rect.Empty;
            Aplicar(ReglaDeDpi.RectTrasCambio(_rcMonitor, ahora), $"tras cambiar la escala de {viejo.PixelsPerDip:0.##} a {nuevo.PixelsPerDip:0.##}");
        }
        Repintar();
    }

    /// <summary>
    /// Cambió la ventana de delante (de otro proceso: <c>WINEVENT_SKIPOWNPROCESS</c>, porque la carita o el panel no
    /// cambian lo que hay en la app de trabajo). Llega en ESTE hilo —un gancho <c>WINEVENT_OUTOFCONTEXT</c> se entrega
    /// al hilo que lo puso, que es el de la interfaz y bombea mensajes—, así que no hay nada que despachar.
    /// </summary>
    private void AlCambiarDePrimerPlano(IntPtr gancho, uint evento, IntPtr hwnd, int idObject, int idChild, uint hilo, uint ms)
    {
        // Una excepción que saliera de aquí cruzaría a código nativo y tumbaría Ü: se queda en el log, entera.
        try
        {
            if (ReferenceEquals(Cajas, CajasDelOverlay.Vacio)) return;
            int habia = Cajas.Cajas.Count;
            Pintar(Cajas.Caducar());
            LogBus.Log("jev-overlay", $"cajas caducadas en el overlay de {_rcMonitor}: cambió la ventana de delante (hwnd 0x{hwnd.ToInt64():X}); había {habia} pintada(s)");
            Caducaron?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception e)
        {
            for (var x = e; x != null; x = x.InnerException)
                LogBus.Log("jev-overlay", $"al caducar las cajas del overlay de {_rcMonitor}: {x.GetType().Name}: {x.Message}");
        }
    }

    /// <summary>
    /// Las dos capas, de abajo arriba: lo que respira y las cajas. Un árbol de <c>Shape</c>s serían cientos de
    /// elementos (plano §Cómo se pinta); dos <c>DrawingVisual</c> son dos.
    /// </summary>
    private sealed class Lienzo : FrameworkElement
    {
        /// <summary>El pulso de TipTour, <c>(sin(t·2,2)+1)/2</c>: de valle a pico en 1,428 s con forma de <c>SineEase</c> (plano).</summary>
        private static readonly Duration MedioPulso = new(TimeSpan.FromSeconds(1.428));
        private static readonly Typeface Normal = new(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        private static readonly Typeface Negrita = new(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

        private readonly VisualCollection _capas;
        private readonly DrawingVisual _pulso = new(), _cajas = new();

        /// <summary>
        /// El grupo cuya opacidad respira: UNA animación para todos los halos, porque dos arrancadas en momentos
        /// distintos respirarían desfasadas. Sus hijos se sustituyen al repintar; él no.
        /// </summary>
        private readonly DrawingGroup _queRespira = new();
        private bool _respirando;

        public Lienzo()
        {
            IsHitTestVisible = false;
            _capas = new VisualCollection(this) { _pulso, _cajas };
            using var dc = _pulso.RenderOpen();
            dc.DrawDrawing(_queRespira);
        }

        protected override int VisualChildrenCount => _capas.Count;

        protected override Visual GetVisualChild(int indice) => _capas[indice];

        /// <summary>Pinta las cajas tal cual llegan, en su orden; la última queda encima.</summary>
        public void Pintar(IReadOnlyList<CajaDeJev> cajas, ConversorDeMonitor conversor, double anchoPantalla)
        {
            var halos = new DrawingGroup();
            using (var dc = _cajas.RenderOpen())
            using (var dh = halos.Open())
                foreach (var caja in cajas)
                    Caja(dc, dh, caja, conversor, anchoPantalla);
            _queRespira.Children.Clear();
            _queRespira.Children.Add(halos);
            // SIN CAJAS NO RESPIRA NADA: una ventana en capas de pantalla completa con una animación en marcha se
            // recompone en cada fotograma aunque no haya nada que ver (plano §Lo que hay que cuidar, 1; sin medir).
            Respirar(cajas.Count > 0);
        }

        public void Parar() => Respirar(false);

        private void Respirar(bool si)
        {
            if (si == _respirando) return;
            _respirando = si;
            _queRespira.BeginAnimation(DrawingGroup.OpacityProperty, si
                ? new DoubleAnimation(0, 1, MedioPulso)
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                }
                : null);
        }

        /// <summary>
        /// Una caja, paso a paso (plano §Una caja): halos, relleno y trazo, corchetes, etiqueta y punto. La rosa es
        /// lo pulsado (<see cref="PaletaDeJev.Elegida"/>, que conserva su nombre del plano); el resto, candidatas.
        /// </summary>
        private static void Caja(DrawingContext dc, DrawingContext dh, CajaDeJev c, ConversorDeMonitor conversor, double anchoPantalla)
        {
            var r = new Rect(conversor.AUnidad(c.Caja.TopLeft), conversor.AUnidad(c.Caja.BottomRight));
            uint color = c.Rosa ? PaletaDeJev.Elegida : PaletaDeJev.Candidata;
            double grosor = c.Rosa ? 2.0 : 0.9, halo = c.Rosa ? 0.18 : 0.055, relleno = c.Rosa ? 0.08 : 0.018;
            const double radio = 6, respira = 0.045;

            // Los halos se parten: lo fijo aquí y lo que respira (+0,045) en la capa del pulso.
            dc.DrawRoundedRectangle(null, Pluma(ConAlfa(color, halo), grosor + 3.8), r, radio, radio);
            dc.DrawRoundedRectangle(null, Pluma(ConAlfa(color, halo + 0.06), grosor + 1.6), r, radio, radio);
            dh.DrawRoundedRectangle(null, Pluma(ConAlfa(color, respira), grosor + 3.8), r, radio, radio);
            dh.DrawRoundedRectangle(null, Pluma(ConAlfa(color, respira), grosor + 1.6), r, radio, radio);
            // El trazo neto lleva el alfa del token: 0,58 la candidata, 0,9 la pulsada.
            dc.DrawRoundedRectangle(Pincel(ConAlfa(color, relleno)), Pluma(Tal(color), grosor), r, radio, radio);
            Corchetes(dc, r, Pluma(Tal(color), grosor + 0.25));
            Etiqueta(dc, c, r, color, anchoPantalla, conversor.Escala);

            // EL PUNTO DICE «LEÍDA» (aprendizaje nº4): sin él la geometría es estimada. Lo pulsado no lo lleva.
            if (c.EsLeida && !c.Rosa)
            {
                const double d = 3.0;
                dc.DrawEllipse(Pincel(ConAlfa(color, 0.86)), null, new Point(r.Right - d - 2, r.Top + 2), d / 2, d / 2);
            }
        }

        private static void Corchetes(DrawingContext dc, Rect r, Pen pluma)
        {
            double largo = Math.Clamp(0.28 * Math.Min(r.Width, r.Height), 8, 18);
            foreach (var (x, y, dx, dy) in new[] { (r.Left, r.Top, 1, 1), (r.Right, r.Top, -1, 1), (r.Left, r.Bottom, 1, -1), (r.Right, r.Bottom, -1, -1) })
            {
                dc.DrawLine(pluma, new Point(x, y), new Point(x + dx * largo, y));
                dc.DrawLine(pluma, new Point(x, y), new Point(x, y + dy * largo));
            }
        }

        /// <summary>
        /// La etiqueta sola, en su cápsula encima de la caja. La caja sola de la mano llega sin etiqueta
        /// (<c>UiaSurface.Pulso</c> no la trae) y no se le inventa una. Una candidata pequeña no la lleva; lo pulsado sí.
        /// El ancho se mide con <c>FormattedText</c> sobre la fuente real, no con la fórmula del original (plano, paso 4).
        /// </summary>
        private static void Etiqueta(DrawingContext dc, CajaDeJev c, Rect r, uint color, double anchoPantalla, double pixelsPorDip)
        {
            if (string.IsNullOrWhiteSpace(c.Etiqueta)) return;
            if (!c.Rosa && (r.Width < 42 || r.Height < 12)) return;

            var texto = new FormattedText(c.Etiqueta, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                c.Rosa ? Negrita : Normal, c.Rosa ? 10.5 : 8.3, c.Rosa ? Brushes.White : Pincel(ConAlfa(color, 0.9)),
                null, TextFormattingMode.Display, pixelsPorDip);
            double alto = c.Rosa ? Math.Max(14.5, texto.Height + 3) : 14.5;
            double ancho = Math.Min(texto.WidthIncludingTrailingWhitespace + 14, Math.Max(58, anchoPantalla - r.Left - 8));
            double x = Math.Max(0, Math.Min(r.Left, anchoPantalla - ancho));
            double y = Math.Max(2, r.Top - alto - 4);
            var capsula = new Rect(x, y, ancho, alto);

            dc.DrawRoundedRectangle(Pincel(Tal(c.Rosa ? PaletaDeJev.FondoDeEtiquetaElegida : PaletaDeJev.FondoDeEtiqueta)),
                Pluma(ConAlfa(color, c.Rosa ? 0.76 : 0.28), 0.55), capsula, 5, 5);
            dc.PushClip(new RectangleGeometry(capsula, 5, 5));
            dc.DrawRectangle(Pincel(ConAlfa(color, c.Rosa ? 0.85 : 0.55)), null, new Rect(x, y, 4, alto));
            dc.Pop();

            // Lo que no cabe se corta con «…» en una línea: una etiqueta que se sale de su cápsula pisa la caja de al lado.
            texto.MaxTextWidth = Math.Max(1, ancho - 10);
            texto.MaxLineCount = 1;
            texto.Trimming = TextTrimming.CharacterEllipsis;
            dc.DrawText(texto, new Point(x + ancho / 2 + 3 - texto.Width / 2, y + (alto - texto.Height) / 2));
        }

        private static Color Tal(uint argb) => Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

        private static Color ConAlfa(uint argb, double alfa) =>
            Color.FromArgb((byte)Math.Round(Math.Clamp(alfa, 0, 1) * 255), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

        private static Brush Pincel(Color color)
        {
            var pincel = new SolidColorBrush(color);
            pincel.Freeze();
            return pincel;
        }

        private static Pen Pluma(Color color, double grosor)
        {
            var pluma = new Pen(Pincel(color), grosor);
            pluma.Freeze();
            return pluma;
        }
    }
}
