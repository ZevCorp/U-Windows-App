using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Path = System.Windows.Shapes.Path;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Ui.Jev;

/// <summary>
/// LA FLECHA QUE VUELA A LO PULSADO. Una ventana pequeña, centrada en la flecha, que se mueve fotograma a fotograma
/// por el camino de <see cref="PlanDeVuelo"/> y con su duración: no calcula ninguna curva ni ningún tiempo suyo.
/// Promesa 381 (b) de la spec 049; el camino, la duración y el «no hay vuelo con la app detrás» son puros y se juzgan
/// sin pantalla (381 (a)). Quién está delante lo pregunta <see cref="VistaDeJev"/> antes de pedir el vuelo.
/// </summary>
/// <remarks>
/// UNA VENTANA PROPIA Y PEQUEÑA, NO EL OVERLAY (plano §La flecha, «Qué ventana»): repintar una ventana en capas de
/// pantalla completa a 60 fotogramas por segundo para mover 44 DIP es lo caro. Esta mide 128 de alto y
/// <c>2·max(63, 10 + píldora + 22)</c> de ancho —126 volando, 544 como mucho con la píldora de 240—, y se mueve con
/// <c>SetWindowPos</c> en físicos sin tocar su sitio en Z, que es del grupo (378: la flecha, arriba del todo).
///
/// NO TOMA EL RATÓN NI EL FOCO, Y NO SALE EN LAS CAPTURAS: las mismas banderas que el overlay (plano). Se posa a 42·s
/// de lo pulsado y señala allí 3 s; una flecha que se comiera el clic siguiente estaría estorbando a la mano.
///
/// DOS MODOS DE LOS TRES DEL PLANO: volando y señalando. «Siguiendo» —junto al ratón en reposo— no está en la máquina
/// de la vista (spec 049, hallazgo (d) de la fase 6), así que al terminar de señalar la flecha se esconde en vez de
/// volver al ratón. Tampoco están el asentamiento de 0,22 s ni el spinner de «pensando»: decoración sin medir.
///
/// EL RELOJ ES EL DE WPF: <see cref="CompositionTarget.Rendering"/>, una vez por fotograma pintado, con
/// <c>p = transcurrido / Duracion</c>. <see cref="PlanDeVuelo.PosicionEn"/> ya se queda en el final si el último
/// fotograma llega tarde y p pasa de 1.
/// </remarks>
public sealed class FlechaDeJev : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WM_WINDOWPOSCHANGING = 0x0046;
    private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;

    /// <summary>La silueta Lucide <c>mouse-pointer-2</c> en su caja de 24 (plano §La flecha, <c>OverlayWindow.swift:59-85</c>). A 0° apunta arriba a la izquierda.</summary>
    private const string Silueta =
        "M4.037,4.688 Q3.90,3.90 4.688,4.037 L20.688,10.537 Q21.42,10.84 20.625,11.484 L14.501,13.064 " +
        "Q13.43,13.34 13.063,14.499 L11.484,20.625 Q11.17,21.42 10.537,20.688 Z";

    /// <summary>El marco de la flecha, en DIP; la silueta se escala de 24 a esto con <c>Geometry.Transform</c> para que el trazo no crezca con ella.</summary>
    private const double Marco = 44;

    /// <summary>Alto de la ventana, en DIP: <c>2·(31 + 32)</c> redondeado (plano): la flecha girada ocupa hasta 62 y su halo de 16, otros ~32.</summary>
    private const double Alto = 128;

    /// <summary>La mitad del ancho sin píldora, en DIP: la misma cuenta que el alto.</summary>
    private const double MedioAnchoMinimo = 63;

    /// <summary>Dónde va la píldora respecto al centro de la flecha, en DIP (<c>OverlayWindow.swift:241-249</c>), y cuánto brilla por fuera.</summary>
    private const double PildoraX = 10, PildoraY = 18, BrilloDeLaPildora = 22, PildoraMaxima = 240;

    /// <summary>Lo que el rumbo le suma al ángulo del camino para que la silueta, que a 0° apunta arriba a la izquierda, apunte hacia donde va (plano §El vuelo).</summary>
    private const double GiroDeLaSilueta = 137;

    /// <summary>Cuánto se acerca el giro a su objetivo en cada fotograma, por el camino corto (plano).</summary>
    private const double SuavizadoDelGiro = 0.11;

    /// <summary>Cuánto señala antes de esconderse (plano: «3 s y vuelve»).</summary>
    private static readonly TimeSpan Senalando = TimeSpan.FromSeconds(3);

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPOS { public IntPtr hwnd, hwndInsertAfter; public int x, y, cx, cy; public uint flags; }

    [DllImport("user32.dll", SetLastError = true)] private static extern int GetWindowLong(IntPtr h, int indice);
    [DllImport("user32.dll", SetLastError = true)] private static extern int SetWindowLong(IntPtr h, int indice, int valor);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowDisplayAffinity(IntPtr h, uint afinidad);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr h, IntPtr tras, int x, int y, int cx, int cy, uint flags);

    private readonly Canvas _lienzo = new();
    private readonly Grid _flecha = new() { Width = Marco, Height = Marco };
    private readonly RotateTransform _giro = new();
    private readonly ScaleTransform _pulso = new(1, 1);
    private readonly Border _pildora;
    private readonly TextBlock _textoDeLaPildora;
    private readonly DispatcherTimer _relojDeSenalar;
    private readonly Stopwatch _cronometro = new();

    private IntPtr _handle;
    private PlanDeVuelo? _plan;
    private double _escala = 1;
    private double _medioAncho = MedioAnchoMinimo;
    private Point _anterior;
    private string _etiqueta = "";
    private int _fotogramas;
    private bool _falloDicho;

    /// <summary>El rect que la ventana tiene que tener, en físicos. <c>null</c> mientras no haya vuelo.</summary>
    private Rect? _rectAplicado;

    /// <summary>La flecha, escondida: aparece al volar y se esconde sola al terminar de señalar.</summary>
    public FlechaDeJev()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        IsHitTestVisible = false;
        Focusable = false;
        Title = "Jev · flecha";
        Width = 2 * MedioAnchoMinimo;
        Height = Alto;

        var azul = Pincel(PaletaDeJev.AzulDelCursor);
        var silueta = Geometry.Parse(Silueta).CloneCurrentValue();
        silueta.Transform = new ScaleTransform(Marco / 24, Marco / 24);
        silueta.Freeze();
        // DE ABAJO ARRIBA (OverlayWindow.swift:522-539): dos halos azules desenfocados, el relleno blanco y el trazo.
        // El Radius de BlurEffect no es la misma magnitud que el blur de SwiftUI: 16 y 6 son el punto de partida del
        // plano, SIN MEDIR contra el vídeo.
        _flecha.Children.Add(new Path { Data = silueta, Fill = azul, Opacity = 0.32, Effect = new BlurEffect { Radius = 16 } });
        _flecha.Children.Add(new Path { Data = silueta, Fill = azul, Opacity = 0.38, Effect = new BlurEffect { Radius = 6 } });
        _flecha.Children.Add(new Path
        {
            Data = silueta, Fill = Brushes.White, Stroke = azul, StrokeThickness = 4.2,
            StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        });
        _flecha.RenderTransformOrigin = new Point(0.5, 0.5);
        _flecha.RenderTransform = new TransformGroup { Children = { _pulso, _giro } };
        _lienzo.Children.Add(_flecha);

        _textoDeLaPildora = new TextBlock
        {
            FontFamily = new FontFamily("Segoe UI"), FontSize = 11, FontWeight = FontWeights.Normal, Foreground = Brushes.White,
            TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = PildoraMaxima - 16,
        };
        _pildora = new Border
        {
            CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 4, 8, 4), Background = azul, MaxWidth = PildoraMaxima,
            Child = _textoDeLaPildora, Visibility = Visibility.Collapsed,
        };
        _lienzo.Children.Add(_pildora);
        Content = _lienzo;

        _relojDeSenalar = new DispatcherTimer { Interval = Senalando };
        _relojDeSenalar.Tick += (_, _) => { _relojDeSenalar.Stop(); Esconder("terminó de señalar"); };

        SiempreDelante.EntraAlGrupo(this, Capa.Flecha);
    }

    /// <summary>Dónde está la flecha ahora, en físicos; <c>null</c> si está escondida. De ahí sale el siguiente vuelo.</summary>
    public Point? Centro => IsVisible && _rectAplicado is Rect r ? new Point(r.X + r.Width / 2, r.Y + r.Height / 2) : null;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _handle = new WindowInteropHelper(this).Handle;

        // LAS BANDERAS DEL OVERLAY, y se COMPRUEBAN después (aprendizaje nº19): una flecha que tomara el ratón se
        // comería el clic que caiga donde señala. La máscara se nombra una vez, en la línea que la toma.
        uint mascara = EstilosDeVentana.ExtendidosDelOverlay;
        int antes = GetWindowLong(_handle, GWL_EXSTYLE);
        SetWindowLong(_handle, GWL_EXSTYLE, antes | unchecked((int)mascara));
        uint quedo = unchecked((uint)GetWindowLong(_handle, GWL_EXSTYLE));
        if ((quedo & mascara) != mascara)
            LogBus.Log("jev-flecha", $"la flecha quedó con estilos 0x{quedo:X8} y le faltan 0x{mascara & ~quedo:X8}: puede tomar el ratón o el foco");
        if (!SetWindowDisplayAffinity(_handle, EstilosDeVentana.Afinidad))
            LogBus.Log("jev-flecha", $"no se pudo excluir de la captura la flecha (SetWindowDisplayAffinity, error {Marshal.GetLastWin32Error()}): saldrá en capturas y grabaciones");

        HwndSource.FromHwnd(_handle)?.AddHook(MantenerElRect);
    }

    /// <summary>
    /// Vuela por <paramref name="plan"/> y se posa señalando. Un vuelo nuevo sustituye al que hubiera en el aire: se
    /// vuela directamente al nuevo (plano, <c>OverlayWindow.swift:639-654</c>). En el hilo de la interfaz: quien pide el
    /// vuelo desde otro hilo pasa por el despachador de la vista (382).
    /// </summary>
    /// <param name="plan">El de <see cref="PlanDeVuelo.Calcular"/>, que ya contestó si hay vuelo: aquí no se decide nada.</param>
    /// <param name="escala">La del monitor de llegada, la misma con la que se calculó el plan: da el tamaño en físicos.</param>
    /// <param name="etiqueta">Lo que dice la píldora al posarse; vacía, sin píldora: no se sabe qué candidata se pulsó.</param>
    public void Volar(PlanDeVuelo plan, double escala, string? etiqueta)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!double.IsFinite(escala) || escala <= 0)
            throw new ArgumentOutOfRangeException(nameof(escala), escala, "la escala del monitor de llegada es un número positivo (DPI / 96)");

        Parar();
        _plan = plan;
        _escala = escala;
        _etiqueta = string.IsNullOrWhiteSpace(etiqueta) ? "" : etiqueta.Trim();
        _anterior = plan.Inicio;
        _giro.Angle = Grados(plan.CentroPulsado - plan.Inicio) + GiroDeLaSilueta;
        _fotogramas = 0;
        _falloDicho = false;
        _pildora.Visibility = Visibility.Collapsed;
        Dimensionar(anchoDeLaPildora: 0);
        Mover(plan.Inicio);
        if (!IsVisible) Show();

        _cronometro.Restart();
        CompositionTarget.Rendering += AlFotograma;
        LogBus.Log("jev-flecha", $"vuela de {Punto(plan.Inicio)} a {Punto(plan.Fin)} (lo pulsado en {Punto(plan.CentroPulsado)}) en {plan.Duracion.TotalSeconds:0.00} s ×{escala:0.##}");
    }

    /// <summary>Escape, soltar lo señalado o apagar Jev (383): la flecha se esconde en el acto, esté volando o señalando.</summary>
    public void Esconder() => Esconder(null);

    private void Esconder(string? porque)
    {
        bool estaba = IsVisible;
        Parar();
        _plan = null;
        _rectAplicado = null;
        if (estaba) Hide();
        // Sola —terminó de señalar, o un fotograma lanzó— se dice; por Escape, soltar o apagar ya lo dice quien llama.
        if (porque != null) LogBus.Log("jev-flecha", $"escondida sola: {porque}");
    }

    protected override void OnClosed(EventArgs e)
    {
        Parar();
        base.OnClosed(e);
    }

    private void Parar()
    {
        CompositionTarget.Rendering -= AlFotograma;
        _cronometro.Stop();
        _relojDeSenalar.Stop();
        _pulso.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _pulso.BeginAnimation(ScaleTransform.ScaleYProperty, null);
    }

    /// <summary>
    /// UN FOTOGRAMA DEL VUELO: dónde va, hacia dónde mira y cuánto late (plano §El vuelo). Lo único que se calcula
    /// aquí es el giro y el latido, que son decoración; el sitio es <see cref="PlanDeVuelo.PosicionEn"/> y el tiempo,
    /// su <see cref="PlanDeVuelo.Duracion"/>.
    /// </summary>
    private void AlFotograma(object? remitente, EventArgs e)
    {
        // Una excepción que saliera de aquí iría al manejador global del Dispatcher en cada fotograma: se dice una vez,
        // con la cadena entera, y la flecha se esconde en vez de quedarse congelada a mitad de camino.
        try
        {
            if (_plan is not PlanDeVuelo plan) { CompositionTarget.Rendering -= AlFotograma; return; }
            _fotogramas++;
            double p = _cronometro.Elapsed.TotalSeconds / plan.Duracion.TotalSeconds;
            var pos = plan.PosicionEn(p);

            double aterrizaje = Grados(plan.CentroPulsado - plan.Fin) + GiroDeLaSilueta;
            var avance = pos - _anterior;
            double rumbo = avance.Length > 0.5 ? Grados(avance) + GiroDeLaSilueta : _giro.Angle;
            double objetivo = p < 0.66 ? rumbo : Interpolar(rumbo, aterrizaje, Smoothstep((p - 0.66) / 0.34));
            _giro.Angle = Interpolar(_giro.Angle, objetivo, SuavizadoDelGiro);
            double latido = 1 + 0.045 * Math.Sin(Math.Clamp(p, 0, 1) * Math.PI);
            _pulso.ScaleX = _pulso.ScaleY = latido;
            _anterior = pos;
            Mover(pos);

            if (p >= 1) Posarse(plan, aterrizaje);
        }
        catch (Exception ex)
        {
            for (var x = ex; x != null; x = x.InnerException)
                LogBus.Log("jev-flecha", $"✘ el fotograma {_fotogramas} del vuelo lanzó {x.GetType().Name}: {x.Message}");
            Esconder("un fotograma del vuelo lanzó una excepción");
        }
    }

    /// <summary>Llegó: queda quieta a 42·s de lo pulsado, mirándolo, late 1,035 cada 1,4 s y enseña la píldora 3 s.</summary>
    private void Posarse(PlanDeVuelo plan, double aterrizaje)
    {
        CompositionTarget.Rendering -= AlFotograma;
        _cronometro.Stop();
        _giro.Angle = aterrizaje;
        Mover(plan.Fin);
        var late = new DoubleAnimation(1, 1.035, new Duration(TimeSpan.FromSeconds(1.4)))
        {
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        _pulso.BeginAnimation(ScaleTransform.ScaleXProperty, late);
        _pulso.BeginAnimation(ScaleTransform.ScaleYProperty, late);

        if (_etiqueta.Length > 0)
        {
            _textoDeLaPildora.Text = _etiqueta;
            _pildora.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double ancho = Math.Min(_pildora.DesiredSize.Width, PildoraMaxima);
            // A LA DERECHA DE LA FLECHA, O A LA IZQUIERDA SI LLEGÓ POR LA IZQUIERDA de lo pulsado (plano).
            bool izquierda = plan.Fin.X < plan.CentroPulsado.X;
            Dimensionar(ancho);
            Canvas.SetLeft(_pildora, izquierda ? _medioAncho - PildoraX - ancho : _medioAncho + PildoraX);
            Canvas.SetTop(_pildora, Alto / 2 + PildoraY - _pildora.DesiredSize.Height / 2);
            _pildora.Visibility = Visibility.Visible;
            Mover(plan.Fin);
        }
        LogBus.Log("jev-flecha", $"posada en {Punto(plan.Fin)} tras {_fotogramas} fotograma(s) en {_cronometro.Elapsed.TotalSeconds:0.00} s (el plan decía {plan.Duracion.TotalSeconds:0.00} s){(_etiqueta.Length > 0 ? $", píldora «{_etiqueta}»" : ", sin píldora: no se sabe qué candidata se pulsó")}");
        _relojDeSenalar.Start();
    }

    /// <summary>El ancho de la ventana para una píldora de este ancho (0, sin píldora), y la flecha en su centro.</summary>
    private void Dimensionar(double anchoDeLaPildora)
    {
        _medioAncho = Math.Max(MedioAnchoMinimo, anchoDeLaPildora > 0 ? PildoraX + anchoDeLaPildora + BrilloDeLaPildora : 0);
        Width = 2 * _medioAncho;
        _lienzo.Width = 2 * _medioAncho;
        _lienzo.Height = Alto;
        Canvas.SetLeft(_flecha, _medioAncho - Marco / 2);
        Canvas.SetTop(_flecha, Alto / 2 - Marco / 2);
    }

    /// <summary>
    /// Centra la ventana en <paramref name="centro"/> (físicos), con su tamaño en DIP por la escala de llegada. Sin
    /// tocar el orden en Z (378). Un fallo se dice una vez por vuelo, no uno por fotograma.
    /// </summary>
    private void Mover(Point centro)
    {
        var rect = new Rect(centro.X - _medioAncho * _escala, centro.Y - Alto / 2 * _escala, 2 * _medioAncho * _escala, Alto * _escala);
        _rectAplicado = rect;
        if (_handle == IntPtr.Zero) return;   // sin HWND todavía: lo aplica MantenerElRect al mostrarla
        if (!SetWindowPos(_handle, IntPtr.Zero, (int)Math.Round(rect.X), (int)Math.Round(rect.Y),
                (int)Math.Round(rect.Width), (int)Math.Round(rect.Height), SWP_NOZORDER | SWP_NOACTIVATE) && !_falloDicho)
        {
            _falloDicho = true;
            LogBus.Log("jev-flecha", $"SetWindowPos a {rect} falló en el fotograma {_fotogramas} (error {Marshal.GetLastWin32Error()}): la flecha no está donde dice el plan");
        }
    }

    /// <summary>
    /// EL RECT NO SE MUEVE, LO MUEVA QUIEN LO MUEVA: al mostrarla WPF la pondría en su <c>Left/Top</c> y al cruzar a un
    /// monitor de otra escala, en el rect que sugiere Windows. Cada <c>WM_WINDOWPOSCHANGING</c> con posición o tamaño
    /// se reescribe con el aplicado; las del grupo en Z (<c>SWP_NOMOVE | SWP_NOSIZE</c>) pasan intactas.
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

    private static double Grados(Vector v) => Math.Atan2(v.Y, v.X) * 180 / Math.PI;

    private static double Smoothstep(double x) { double t = Math.Clamp(x, 0, 1); return t * t * (3 - 2 * t); }

    /// <summary>De <paramref name="a"/> hacia <paramref name="b"/>, en grados y por el camino corto.</summary>
    private static double Interpolar(double a, double b, double t)
    {
        double delta = ((b - a) % 360 + 540) % 360 - 180;
        return a + delta * t;
    }

    private static string Punto(Point p) => $"({p.X:0}, {p.Y:0})";

    private static SolidColorBrush Pincel(uint argb)
    {
        var p = new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
        p.Freeze();
        return p;
    }
}
