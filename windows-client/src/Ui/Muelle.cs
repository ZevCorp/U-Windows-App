using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Ui;

/// <summary>
/// EL MUELLE: el panel de Ü, pegado al borde derecho de la pantalla y siempre ahí.
/// </summary>
/// <remarks>
/// POR QUÉ EXISTE (spec 010, 2026-09-05, pedido por el usuario). El panel se abría con un clic en la
/// carita, y eso costaba dos cosas a la vez: el clic —el gesto más a mano que tiene la aplicación—
/// se lo gastaba en abrir una barra, y la barra no estaba en ningún sitio fijo, así que había que
/// buscar la carita para llegar a ella. Ahora el clic habla (ver <see cref="ReglaDelToque"/>) y el
/// panel vive donde siempre se le puede encontrar: en reposo asoma una pestaña diminuta contra el
/// borde, y al llevar el cursor se despliega entero.
///
/// NO SE REESCRIBIÓ EL PANEL: SE MUDÓ VIVO. Los ~35 elementos con nombre de la barra y su menú
/// —<c>DemoBtn</c>, <c>MenuPanel</c>, <c>WorkflowPick</c>, <c>BackendDot</c>, <c>TalkPanel</c>…— los
/// maneja el code-behind de <see cref="FaceWindow"/>, 4.695 líneas. Copiarlos aquí habría sido
/// reescribir la zona que el CLAUDE.md marca como el mayor riesgo de choque del repo, para un
/// resultado idéntico en pantalla. En vez de eso, <c>RootPanel</c> entero se reparenta a esta
/// ventana: los handlers que el XAML cableó (<c>Click="OnDemoPuntaAPunta"</c>) apuntan a la
/// INSTANCIA de FaceWindow, no al padre visual, así que siguen funcionando sin tocarse. Esta ventana
/// es un marco; quien manda dentro sigue siendo FaceWindow.
///
/// LA PESTAÑA ES ESTRECHA A PROPÓSITO, Y CORTA. Está topmost sobre el trabajo de alguien: cada píxel
/// que ocupe del borde derecho es un píxel de barra de desplazamiento ajena que deja de poder
/// pulsarse. Por eso mide 14 px de blanco y solo 64 de alto, centrada — no una franja de lado a
/// lado. El dibujo son 5 px; el blanco del gesto es casi el triple, que es la misma regla que ya
/// siguen las pastillas de la carita: acertarle a cinco píxeles sería puntería, no una interfaz.
///
/// EL ANCLA ES LA PESTAÑA, NO LA VENTANA. La pestaña va acoplada al borde derecho y el panel crece
/// hacia la IZQUIERDA desde ella, así que desplegarse no la mueve de sitio. Si se anclara la ventana
/// entera, abrir el menú desplazaría la pestaña y el cursor se quedaría fuera de lo que acababa de
/// tocar — que es la forma clásica de que un menú por hover parpadee.
/// </remarks>
public sealed class Muelle : Window
{
    /// <summary>Blanco del gesto de la pestaña. El dibujo son <see cref="AnchoDibujo"/>.</summary>
    private const double AnchoPestana = 14, AltoPestana = 64, AnchoDibujo = 5;

    /// <summary>
    /// Lo que se espera antes de volver a plegar tras salir el cursor.
    ///
    /// No es un adorno: el camino del ratón entre la pestaña y un botón del panel pasa por encima
    /// del borde y genera un <c>MouseLeave</c> falso. Sin este respiro, el panel se cierra en la
    /// cara de quien iba a pulsarlo.
    /// </summary>
    private const int GraciaMs = 350;

    private readonly Decorator _hueco = new();          // aquí vive el panel reparentado
    private readonly Border _dibujoPestana;
    private readonly TranslateTransform _desliz = new();
    private readonly DispatcherTimer _gracia;
    private readonly Func<bool> _hayConversacion;

    /// <summary>El centro vertical de la pestaña, que NO se mueve al desplegarse.</summary>
    private double _centro;

    public bool EstaDesplegado { get; private set; }

    /// <summary>Avisa de cada cambio de estado, para que FaceWindow sepa si el panel se ve.</summary>
    public event Action<bool>? Cambio;

    public Muelle(UIElement panel, Func<bool> hayConversacion)
    {
        _hayConversacion = hayConversacion;

        Title = "Ü";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;

        // Que quepa: si el panel crece más que la pantalla, quien cede es su propio scroll interno.
        MaxHeight = SystemParameters.WorkArea.Height - 24;

        _hueco.Child = panel;
        _hueco.Visibility = Visibility.Collapsed;
        _hueco.RenderTransform = _desliz;
        _hueco.Opacity = 0;

        _dibujoPestana = new Border
        {
            Width = AnchoDibujo,
            Height = AltoPestana - 8,
            CornerRadius = new CornerRadius(AnchoDibujo / 2),
            // Gris apagado, el MISMO de las pastillas de la carita: se ve que hay algo y no compite.
            Background = new SolidColorBrush(Color.FromArgb(0x80, 0xA8, 0xA8, 0xAE)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        // Background transparente Y NO nulo: nulo no recibe el ratón, y sin ratón no hay gesto.
        var blancoPestana = new Grid
        {
            Width = AnchoPestana,
            Height = AltoPestana,
            Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        blancoPestana.Children.Add(_dibujoPestana);

        // La pestaña a la derecha del todo y el panel llenando lo que quede a su izquierda: así el
        // panel crece hacia dentro de la pantalla y la pestaña se queda clavada al borde.
        var fila = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(blancoPestana, Dock.Right);
        fila.Children.Add(blancoPestana);
        fila.Children.Add(_hueco);
        Content = fila;

        // LA BARRA DE SCROLL, LA NUESTRA. La de Windows sobre una superficie clara del estudio se
        // ve como lo que es: un trozo de otra aplicación pegado dentro de la nuestra (lo dijo el
        // dueño el 2026-09-05 mirando el globo de conversación). Se pone en la RAÍZ y no en cada
        // ScrollViewer porque en WPF el estilo por tipo se hereda hacia abajo: uno aquí viste todo
        // lo que cuelgue, incluido lo que se añada después.
        fila.PonerLaBarraDeScroll(sobreOscuro: false);

        // Y que el texto se vea nítido: AllowsTransparency apaga el ClearType por defecto —una
        // ventana por capas no sabe qué hay debajo—, y sin volver a pedirlo las letras del panel
        // salen lavadas. Se puede porque el panel de dentro es opaco. Ver Estudio.Nitida.
        this.Nitida();

        _gracia = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(GraciaMs) };
        _gracia.Tick += (_, __) => Reconsiderar();

        MouseEnter += (_, __) => Desplegar("el cursor entró");
        MouseLeave += (_, __) => _gracia.Start();

        // Al terminar de escribir, o al colgar, el muelle tiene que poder plegarse aunque el ratón
        // lleve rato fuera: sin esto se quedaría abierto hasta el siguiente movimiento.
        LostKeyboardFocus += (_, __) => _gracia.Start();

        SizeChanged += (_, __) => Recolocar();
        Loaded += (_, __) => { _centro = CentroPorDefecto(); Recolocar(); };
    }

    private static double CentroPorDefecto()
    {
        var wa = SystemParameters.WorkArea;
        return wa.Top + wa.Height / 2;
    }

    /// <summary>
    /// Pegada al borde derecho y con la pestaña quieta. El alto lo pone el contenido, así que al
    /// desplegarse la ventana crece hacia arriba y hacia abajo por igual desde el centro guardado.
    /// </summary>
    private void Recolocar()
    {
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - ActualWidth;
        Top = Math.Clamp(_centro - ActualHeight / 2, wa.Top, Math.Max(wa.Top, wa.Bottom - ActualHeight));
    }

    /// <summary>La caja que ocupa el muelle AHORA, en coordenadas de pantalla. La usa la promesa 149
    /// para decidir si soltar la carita encima la guarda.</summary>
    public Rect Caja => new(Left, Top, ActualWidth, ActualHeight);

    private bool _guardando;

    /// <summary>
    /// El muelle tiene la carita dentro. LO DICE, y no es adorno: guardada, la carita no está en
    /// ningún sitio de la pantalla, así que si la pestaña siguiera exactamente igual no habría forma
    /// de distinguir «la guardé» de «Ü se cerró» — y de las dos se saca la misma conclusión.
    ///
    /// LA SEÑAL ES BLANCA Y NO AZUL (corregido el 2026-09-05, lo pidió el dueño). Estaba en el
    /// azul de Ü y llamaba demasiado para lo que dice: teniéndola guardada, quien lo dice de verdad
    /// es la CARA que aparece dentro del panel — el color de la pestaña era una segunda señal para
    /// el mismo hecho. En blanco sigue distinguiéndose del gris de reposo sin gritar; el filete
    /// existe para que no se pierda contra un fondo claro.
    /// </summary>
    public bool Guardando
    {
        get => _guardando;
        set
        {
            if (_guardando == value) return;
            _guardando = value;
            _dibujoPestana.Background = value
                ? Estudio.Superficie
                : new SolidColorBrush(Color.FromArgb(0x80, 0xA8, 0xA8, 0xAE));
            _dibujoPestana.BorderBrush = Estudio.Borde;
            _dibujoPestana.BorderThickness = new Thickness(value ? 1 : 0);
            LogBus.Log("muelle", value ? "la carita queda guardada aquí" : "la carita sale del muelle");
        }
    }

    public void Desplegar(string porque)
    {
        _gracia.Stop();
        if (EstaDesplegado) return;
        EstaDesplegado = true;

        _hueco.Visibility = Visibility.Visible;
        _desliz.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(24, 0, TimeSpan.FromMilliseconds(180))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        _hueco.BeginAnimation(OpacityProperty,
            new DoubleAnimation(1, TimeSpan.FromMilliseconds(150)));
        CrecerPestana(true);

        // SI SE ABRIÓ SIN QUE NADIE SE ACERCARA —porque Ü tiene algo que decir, o por un atajo—,
        // hay que armar la vigilancia igual. Si no, nada la arrancaría (solo la arranca salir el
        // cursor, y el cursor nunca entró) y el panel se quedaría abierto para siempre.
        if (!IsMouseOver) _gracia.Start();

        LogBus.Log("muelle", $"desplegado · {porque}");
        Cambio?.Invoke(true);
    }

    public void Plegar(string porque)
    {
        _gracia.Stop();
        if (!EstaDesplegado) return;
        EstaDesplegado = false;

        var irse = new DoubleAnimation(0, TimeSpan.FromMilliseconds(140))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        irse.Completed += (_, __) => { if (!EstaDesplegado) _hueco.Visibility = Visibility.Collapsed; };
        _hueco.BeginAnimation(OpacityProperty, irse);
        _desliz.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(24, TimeSpan.FromMilliseconds(140)));
        CrecerPestana(false);

        LogBus.Log("muelle", $"plegado · {porque}");
        Cambio?.Invoke(false);
    }

    /// <summary>La pestaña engorda un poco al acercarse la mano: dice «esto se toca» sin ocupar más.</summary>
    private void CrecerPestana(bool crece)
    {
        var dur = TimeSpan.FromMilliseconds(crece ? 140 : 180);
        _dibujoPestana.BeginAnimation(WidthProperty,
            new DoubleAnimation(crece ? AnchoDibujo + 2 : AnchoDibujo, dur));
    }

    /// <summary>
    /// Vuelve a preguntarle a la regla. Se llama al vencer la gracia, y se REARMA mientras la regla
    /// diga que siga abierto: cuando lo que lo mantenía abierto se acabe —cuelgas, dejas de
    /// escribir—, el muelle se pliega solo sin necesitar que el ratón vuelva a pasar por encima.
    /// </summary>
    private void Reconsiderar()
    {
        bool debe = ReglaDelMuelle.Desplegado(IsMouseOver, Seguro(_hayConversacion), IsKeyboardFocusWithin);
        if (!debe) { Plegar("el cursor se fue y no quedaba nada abierto"); return; }
        if (!_gracia.IsEnabled) _gracia.Start();
    }

    /// <summary>El estado se lo pregunta a otra ventana; si esa se está cerrando, no se cuelga el
    /// muelle por ello — pero SE DICE por qué, que es lo contrario de un catch mudo (patrón nº3).</summary>
    private bool Seguro(Func<bool> pregunta)
    {
        try { return pregunta(); }
        catch (Exception ex)
        {
            LogBus.Log("muelle", $"no se pudo saber si hay conversación abierta, se asume que no: {ex.Message}");
            return false;
        }
    }
}
