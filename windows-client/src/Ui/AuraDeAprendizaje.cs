using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Ui;

/// <summary>En qué estado está el aura. Lo decide <see cref="ReglaDelAura.Decidir"/>, no la UI.</summary>
public enum FaseDelAura
{
    /// <summary>No se enseña nada: sin aura.</summary>
    Apagada,
    /// <summary>Se pulsó Enseñar y corre la cuenta atrás: todavía NO graba. Tenue.</summary>
    Preparando,
    /// <summary>Grabando: encendida y respirando.</summary>
    Aprendiendo,
}

/// <summary>
/// LA REGLA del aura de aprendizaje, separada del dibujo para que el contrato la juzgue sin
/// pantalla (promesa 107, spec 006). El overlay (<see cref="AuraDeAprendizaje"/>) no decide nada:
/// pregunta aquí y pinta muestreando <see cref="Opacidad"/>, así lo juzgado y lo pintado no
/// pueden discrepar (aprendizaje nº16: comparar por el mismo camino).
/// </summary>
public static class ReglaDelAura
{
    /// <summary>Cuánto entra el aura desde el borde, en DIPs. A partir de aquí, NADA.</summary>
    public const double Grosor = 96;

    /// <summary>
    /// Sin enseñar: apagada. Enseñando pero aún sin grabar (la cuenta atrás de 3 s): preparando.
    /// Grabando: aprendiendo. Y terminado pero todavía cerrando (<paramref name="ensenando"/>
    /// falso con <paramref name="grabando"/> aún verdadero): APAGADA — el cierre sube el video y
    /// no aprende nada de lo que hagas ahora; un aura encendida diría lo contrario.
    /// </summary>
    public static FaseDelAura Decidir(bool ensenando, bool grabando)
    {
        if (!ensenando) return FaseDelAura.Apagada;
        return grabando ? FaseDelAura.Aprendiendo : FaseDelAura.Preparando;
    }

    /// <summary>
    /// Opacidad del aura a <paramref name="distanciaAlBorde"/> DIPs del borde, para un aura de
    /// <paramref name="grosor"/> DIPs. 0,85 en el borde mismo, CERO desde el grosor hacia dentro,
    /// y entre medias una curva cuadrática: concentra la luz pegada al borde y muere suave, en
    /// vez de una franja que se lea como un marco.
    /// </summary>
    public static double Opacidad(double distanciaAlBorde, double grosor)
    {
        if (grosor <= 0 || distanciaAlBorde >= grosor) return 0;
        double resto = 1 - Math.Max(0, distanciaAlBorde) / grosor;   // 1 en el borde, 0 en el interior
        return 0.85 * resto * resto;
    }
}

/// <summary>
/// EL AURA DE APRENDIZAJE: un degradado en el color de Ü pegado a los cuatro bordes del monitor que
/// se graba, click-through, que respira mientras Ü aprende. Existe porque mientras se enseña el
/// operador está mirando SAP y la carita queda plegada en una esquina: lo único que decía «te
/// estoy grabando» era un botón de 24 px en rojo —y ese rojo también significa «fallo»
/// (UiPalette). Una demo con pasos de más porque nadie se acordó de que grababa es una demo mala
/// (2026-09-02, pedido por el usuario).
/// </summary>
/// <remarks>
/// Tres decisiones que no son de estilo:
///
/// · <b>Cubre SOLO el monitor principal</b>, porque es el único que graba <c>ScreenRecorder</c>
///   (<c>SourceOptions.MainMonitor</c>). El aura marca lo que entra al video; pintarla en un
///   monitor que no se graba diría lo contrario de lo que pasa.
/// · <b>No sale en el mp4</b> (<c>WDA_EXCLUDEFROMCAPTURE</c>): el aura es para el humano, no para
///   el video que va al LLM ni para los pantallazos por paso. Si el sistema no lo soporta, se dice
///   en el registro y se sigue: un aura grabada es un defecto cosmético, no un motivo para no avisar.
/// · <b>No se activa jamás</b> (<c>ShowActivated=false</c> + <c>WS_EX_NOACTIVATE</c>): la valla de
///   foco de <c>WorkflowTeachSession</c> exige que el foco esté FUERA de Ü, y una ventana nuestra
///   que robara el foco al aparecer haría fallar la enseñanza justo al arrancar.
/// </remarks>
public sealed class AuraDeAprendizaje : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80,
        WS_EX_NOACTIVATE = 0x08000000;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x11;
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    /// <summary>El azul de Ü (<see cref="UiPalette.Trabajando"/>): aprender es trabajar. Y no es
    /// rojo a propósito: así «grabando» y «fallo» dejan de compartir color en la pantalla.</summary>
    private static readonly Color Azul = UiPalette.Trabajando;
    /// <summary>El mismo azul, un punto más claro, para el borde mismo: el brillo viene de fuera.</summary>
    private static readonly Color AzulClaro = Color.FromRgb(0x4C, 0x8D, 0xFF);

    private readonly Grid _capa = new();
    private readonly TextBlock _texto = new()
    {
        Foreground = Brushes.White,
        FontFamily = new FontFamily("Segoe UI"),
        FontSize = 13,
        FontWeight = FontWeights.SemiBold,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly Border _chip;
    private FaseDelAura _fase = FaseDelAura.Apagada;
    private int _pasos;

    public AuraDeAprendizaje()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        IsHitTestVisible = false;
        Focusable = false;
        ShowActivated = false;
        Left = 0; Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;

        double g = ReglaDelAura.Grosor;
        _capa.Children.Add(Borde(new Point(0, 0), new Point(0, 1), VerticalAlignment.Top, HorizontalAlignment.Stretch, alto: g));
        _capa.Children.Add(Borde(new Point(0, 1), new Point(0, 0), VerticalAlignment.Bottom, HorizontalAlignment.Stretch, alto: g));
        _capa.Children.Add(Borde(new Point(0, 0), new Point(1, 0), VerticalAlignment.Stretch, HorizontalAlignment.Left, ancho: g));
        _capa.Children.Add(Borde(new Point(1, 0), new Point(0, 0), VerticalAlignment.Stretch, HorizontalAlignment.Right, ancho: g));

        // La píldora de arriba dice CON PALABRAS lo que el aura dice con color: quien no conozca
        // la convención la aprende la primera vez, y quien la conozca no la necesita leer.
        var punto = new Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush(AzulClaro), Margin = new Thickness(0, 0, 8, 0) };
        var fila = new StackPanel { Orientation = Orientation.Horizontal };
        fila.Children.Add(punto);
        fila.Children.Add(_texto);
        _chip = new Border
        {
            Child = fila,
            Background = new SolidColorBrush(Color.FromArgb(0xD0, 0x0F, 0x15, 0x24)), // la tinta del estudio, translúcida
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(12, 6, 14, 6),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 14, 0, 0),
        };

        var raiz = new Grid();
        raiz.Children.Add(_capa);
        raiz.Children.Add(_chip);
        Content = raiz;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var h = new WindowInteropHelper(this).Handle;
        SetWindowLong(h, GWL_EXSTYLE, GetWindowLong(h, GWL_EXSTYLE)
            | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        if (!SetWindowDisplayAffinity(h, WDA_EXCLUDEFROMCAPTURE))
            LogBus.Log("aura", $"no se pudo excluir el aura de la captura (error {Marshal.GetLastWin32Error()}): saldrá en el video");
    }

    /// <summary>
    /// Un lado del aura: un rectángulo pegado al borde con un degradado que va del azul claro en
    /// el borde a NADA a <see cref="ReglaDelAura.Grosor"/> DIPs, muestreando la misma
    /// <see cref="ReglaDelAura.Opacidad"/> que juzga el contrato. Doce paradas bastan: la curva es
    /// cuadrática y WPF interpola linealmente entre ellas sin que se note.
    /// </summary>
    private static System.Windows.Shapes.Rectangle Borde(Point desde, Point hasta,
        VerticalAlignment v, HorizontalAlignment hz, double ancho = double.NaN, double alto = double.NaN)
    {
        const int Paradas = 12;
        var pincel = new LinearGradientBrush { StartPoint = desde, EndPoint = hasta };
        for (int k = 0; k <= Paradas; k++)
        {
            double t = (double)k / Paradas;
            double a = ReglaDelAura.Opacidad(t * ReglaDelAura.Grosor, ReglaDelAura.Grosor);
            var c = UiPalette.Blend(AzulClaro, Azul, t);
            pincel.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Math.Round(a * 255), c.R, c.G, c.B), t));
        }
        pincel.Freeze();
        return new System.Windows.Shapes.Rectangle
        {
            Fill = pincel,
            Width = ancho,
            Height = alto,
            VerticalAlignment = v,
            HorizontalAlignment = hz,
            IsHitTestVisible = false,
        };
    }

    /// <summary>Lleva el aura a la fase que dice la regla. Idempotente: repetir la fase no reinicia nada.</summary>
    public void Mostrar(FaseDelAura fase)
    {
        if (fase == _fase) return;
        _fase = fase;
        _capa.BeginAnimation(OpacityProperty, null);
        switch (fase)
        {
            case FaseDelAura.Apagada:
                Hide();
                return;
            case FaseDelAura.Preparando:
                // Tenue y quieta: avisa de que viene, sin decir que ya graba.
                _capa.Opacity = 0.35;
                break;
            case FaseDelAura.Aprendiendo:
                // Respira: un aura fija se vuelve invisible a los treinta segundos, y este es el
                // estado que existe para no olvidarse.
                _capa.Opacity = 1;
                _capa.BeginAnimation(OpacityProperty, new DoubleAnimation(0.55, 1.0, TimeSpan.FromMilliseconds(1600))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                });
                break;
        }
        RefrescarTexto();
        if (!IsVisible) Show();
    }

    /// <summary>Cuántos pasos lleva la demo: se dice en la píldora, para que se vea que Ü los está recibiendo.</summary>
    public void Pasos(int enviados)
    {
        _pasos = enviados;
        RefrescarTexto();
    }

    private void RefrescarTexto()
    {
        _texto.Text = _fase switch
        {
            FaseDelAura.Preparando => "Ü va a aprender · cambia a la app que vas a enseñar",
            FaseDelAura.Aprendiendo => _pasos > 0
                ? $"Ü está aprendiendo · {_pasos} paso{(_pasos == 1 ? "" : "s")}"
                : "Ü está aprendiendo",
            _ => "",
        };
    }
}
