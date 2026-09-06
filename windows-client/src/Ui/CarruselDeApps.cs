using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using U.WindowsClient.Diagnostics;
using U.WindowsClient.SystemApi;

namespace U.WindowsClient.Ui;

/// <summary>
/// El carrusel de apps: «elige cuál quieres que aprenda».
///
/// Mapear una aplicación era hasta ahora algo que se pedía DESDE DENTRO de ella —ponte delante y
/// pulsa «mapear esta app»—, y eso obliga a saber de antemano qué se quiere mapear y a llegar hasta
/// allí. Aquí se ve todo lo que hay instalado de un vistazo y se elige con un doble clic, que es
/// como se abre una aplicación en cualquier sitio de Windows: el gesto ya se sabe.
///
/// Va en el CENTRO de la pantalla y con la carita encima, no en una esquina: mientras se elige, esto
/// es lo único que está pasando. Una tira en el borde diría «esto es accesorio», y no lo es —es el
/// principio de que el asistente aprenda una app entera.
/// </summary>
public sealed class CarruselDeApps : Window
{
    /// <summary>Se eligió una app para mapear. Llega el nombre visible, tal como lo ve el usuario.</summary>
    public event Action<AppInstalada>? Elegida;

    /// <summary>
    /// Dónde ha quedado el carrusel, en píxeles de pantalla. Lo publica ÉL para que la carita se
    /// ponga encima sin que nadie tenga que recalcularlo —ni volver a tocarlo si algún día cambia
    /// de tamaño—. Rect vacío = se cerró y la carita puede volver a lo suyo.
    /// </summary>
    public static event Action<Rect>? Colocado;

    private readonly WrapPanel _tira = new()
    {
        Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Center,
    };
    private readonly TextBox _filtro = new()
    {
        Width = 320, FontSize = 15, Padding = new Thickness(10, 6, 10, 6),
        Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
        Foreground = Brushes.White, BorderThickness = new Thickness(0),
        HorizontalAlignment = HorizontalAlignment.Center,
    };
    private IReadOnlyList<AppInstalada> _todas = Array.Empty<AppInstalada>();

    public CarruselDeApps()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.Height;
        Width = 980;
        WindowStartupLocation = WindowStartupLocation.Manual;

        var marco = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xE8, 0x1C, 0x1C, 0x20)),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(22, 18, 22, 22),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
        };

        var columna = new StackPanel();
        columna.Children.Add(new TextBlock
        {
            Text = "¿Qué aplicación quieres que aprenda?",
            Foreground = Brushes.White, FontSize = 17, FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 4),
        });
        columna.Children.Add(new TextBlock
        {
            Text = "Pulsa una para empezar. Esc para cerrar.",
            Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
            FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 12),
        });
        columna.Children.Add(_filtro);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = 420,
            Margin = new Thickness(0, 14, 0, 0),
            Content = _tira,
        };
        columna.Children.Add(scroll);
        marco.Child = columna;
        Content = marco;

        _filtro.TextChanged += (_, __) => Pintar(_filtro.Text);
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Loaded += (_, __) => { Centrar(); _filtro.Focus(); };
        SizeChanged += (_, __) => Centrar();
        Closed += (_, __) => Colocado?.Invoke(Rect.Empty);   // la carita vuelve a su sitio
    }

    /// <summary>Lee las apps instaladas y se muestra. La lectura va fuera del hilo de la interfaz:
    /// recorrer el menú Inicio entero tarda lo suyo y congelaría la ventana justo al abrirla.</summary>
    public async Task MostrarAsync()
    {
        Show();
        _tira.Children.Add(new TextBlock
        {
            Text = "Buscando las aplicaciones instaladas…",
            Foreground = Brushes.White, Margin = new Thickness(8),
        });

        var apps = await Task.Run(() =>
        {
            var lista = AppsInstaladas.Todas();
            // El icono también se saca aquí: son cientos de llamadas al shell y una a una en el
            // hilo de la interfaz se nota como un tirón.
            return lista.Select(a => (App: a, Icono: AppsInstaladas.Icono(a.Lnk))).ToList();
        });

        _iconos = apps;
        _todas = apps.Select(x => x.App).ToList();
        LogBus.Log("carrusel", $"{_todas.Count} aplicación(es) instaladas");
        Pintar("");
        Activate();
    }

    private List<(AppInstalada App, ImageSource? Icono)> _iconos = new();

    private void Pintar(string filtro)
    {
        _tira.Children.Clear();
        string q = Uia.Reconocedor.Normalizar(filtro);

        foreach (var (app, icono) in _iconos)
        {
            if (q.Length > 0 && !Uia.Reconocedor.Normalizar(app.Nombre).Contains(q, StringComparison.Ordinal))
                continue;
            _tira.Children.Add(Ficha(app, icono));
        }

        if (_tira.Children.Count == 0)
            _tira.Children.Add(new TextBlock
            {
                Text = $"Ninguna aplicación se llama así.",
                Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
                Margin = new Thickness(8),
            });
    }

    private UIElement Ficha(AppInstalada app, ImageSource? icono)
    {
        var caja = new StackPanel { Width = 104, Margin = new Thickness(6), Cursor = Cursors.Hand };

        if (icono != null)
            caja.Children.Add(new Image { Source = icono, Width = 48, Height = 48 });
        else
            caja.Children.Add(new Border
            {
                Width = 48, Height = 48, CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            });

        caja.Children.Add(new TextBlock
        {
            Text = app.Nombre,
            Foreground = Brushes.White, FontSize = 11, TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap, MaxHeight = 30, Margin = new Thickness(0, 6, 0, 0),
        });

        var marco = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(6),
            Background = Brushes.Transparent,
            Child = caja,
        };
        marco.MouseEnter += (_, __) => marco.Background =
            new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
        marco.MouseLeave += (_, __) => marco.Background = Brushes.Transparent;

        // UN CLIC. Estaba pedido el doble, y no llegaba nunca: en WPF, MouseLeftButtonUp informa
        // siempre de ClickCount = 1 —ese contador solo vale en el evento de BAJADA—, así que la
        // condición «si no son dos, no hagas nada» descartaba todos los clics. El doble no aporta
        // aquí ninguna protección real: esta ventana no está para otra cosa que elegir, y quien
        // abre el catálogo ya ha dicho lo que quiere (2026-08-05).
        marco.MouseLeftButtonUp += (_, __) =>
        {
            LogBus.Log("carrusel", $"elegida «{app.Nombre}» para mapear");
            Elegida?.Invoke(app);
            Close();
        };
        return marco;
    }

    /// <summary>En el centro de la pantalla, y avisando de dónde queda para que la carita se ponga
    /// encima.</summary>
    private void Centrar()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - ActualWidth) / 2;
        Top = area.Top + (area.Height - ActualHeight) / 2;
        Colocado?.Invoke(new Rect(Left, Top, ActualWidth, ActualHeight));
    }
}
