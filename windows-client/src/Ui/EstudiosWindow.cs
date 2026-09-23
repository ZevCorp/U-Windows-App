using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using U.WindowsClient.Cardio;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Ui;

/// <summary>
/// EL PANEL DE ESTUDIOS: fotos de cardiología → resumen → preguntas. Lo abre la píldora «Subir» del
/// óvalo (spec 046).
/// </summary>
/// <remarks>
/// UNA VENTANA PROPIA Y NO DENTRO DEL ÓVALO: el óvalo mide 150 DIP de ancho y es su propia ventana
/// transparente; una grilla de fotos y un resumen no caben ahí. Nace pegada a su izquierda, sin
/// marco, con el mismo manual que la ventana de la Nota: blanco, esquinas muy redondeadas, y la sombra
/// —no el color— separa lo elevado (<see cref="Estudio"/>).
///
/// ESTA VENTANA NO DECIDE NADA. Qué se manda al modelo, qué se descarta, qué dice el botón y cuándo
/// caduca todo viven en <c>U.WindowsClient.Cardio</c>, donde el contrato lo juzga sin pantalla
/// (promesas 346-353). Aquí solo se pinta y se reciben las fotos.
///
/// ESC Y ✕ LA ESCONDEN, NO LA CIERRAN: una lectura en curso sigue en marcha y el médico la encuentra
/// terminada al volver. Lo que sobrevive a reiniciar Ü es lo guardado en disco, no esta instancia.
/// </remarks>
public sealed class EstudiosWindow : Window
{
    private static EstudiosWindow? _unica;

    /// <summary>La del panel. Una sola: dos paneles sobre la misma sesión se pisarían el guardado.</summary>
    public static EstudiosWindow Unica => _unica ??= new EstudiosWindow();

    /// <summary>Lo que se acepta. HEIC entra para poder nombrarlo si WIC no lo sabe leer.</summary>
    private static readonly string[] Extensiones =
        { ".jpg", ".jpeg", ".jfif", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".heic", ".heif" };

    private const string Filtro = "Imágenes|*.jpg;*.jpeg;*.jfif;*.png;*.bmp;*.gif;*.tif;*.tiff;*.webp;*.heic;*.heif";

    private const double AnchoTarjeta = 440;
    private const double AltoTarjeta = 660;
    private const double Lado = 86;

    private readonly AlmacenCardio _almacen = AlmacenCardio.DeLaApp();
    private readonly ClienteCardio _cliente = ClienteCardio.DeLaApp();
    private SesionCardio _sesion;
    private readonly Dictionary<string, BitmapSource> _miniaturas = new();

    /// <summary>Las escrituras a disco, en fila: la última foto de la sesión es la que tiene que quedar.</summary>
    private Task _escritura = Task.CompletedTask;
    private CancellationTokenSource _trabajo = new();
    private bool _generando;
    /// <summary>Cuántas tandas de fotos se están preparando. Un contador: dos pegados seguidos no se pisan.</summary>
    private int _preparando;
    private string _preguntaEnCurso = "";
    private bool _omitidasAbiertas;
    private readonly Thickness _holgura = Estudio.HolguraDe(Estudio.Sombra3);

    private readonly Rectangle _bordeDeLaZona;
    private readonly TextBlock _textoElegir;
    private readonly TextBlock _pista;
    private readonly StackPanel _errores = new();
    private readonly WrapPanel _grilla = new() { Margin = new Thickness(0, 12, 0, 0) };
    private readonly Button _generar;
    private readonly TextBlock _progreso;
    private readonly TextBlock _error;
    private readonly Button _omitidasBoton;
    private readonly StackPanel _omitidasLista = new() { Margin = new Thickness(18, 2, 0, 0) };
    private readonly Border _tarjetaResumen;
    private readonly StackPanel _resumen = new();
    private readonly StackPanel _seccionChat = new() { Margin = new Thickness(0, 18, 0, 0) };
    private readonly StackPanel _chat = new();
    private readonly TextBlock _errorChat;
    private readonly TextBox _pregunta;
    private readonly TextBlock _fantasmaDeLaPregunta;
    private readonly Button _enviar;
    private readonly TextBlock _caducidad;
    private readonly ScrollViewer _scroll;
    private readonly DispatcherTimer _relojDelPie = new() { Interval = TimeSpan.FromSeconds(30) };

    private EstudiosWindow()
    {
        _sesion = CargarLoGuardado();

        Title = "Estudios · Ü";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        SizeToContent = SizeToContent.WidthAndHeight;
        ShowInTaskbar = true;
        AllowDrop = true;
        this.PonerLaBarraDeScroll();

        var tarjeta = new Border
        {
            Width = AnchoTarjeta,
            Height = AltoTarjeta,
            CornerRadius = new CornerRadius(30),
            Background = Estudio.Fondo,
            BorderBrush = Estudio.Borde,
            BorderThickness = new Thickness(1),
        };

        var raiz = new Grid { Margin = new Thickness(22, 18, 14, 16) };
        raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        raiz.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // ── la cabecera ──────────────────────────────────────────────────────
        var cabecera = new DockPanel { Margin = new Thickness(2, 0, 8, 12), Background = Brushes.Transparent };
        var cerrar = BotonDeMarco("", "Cerrar (Esc)");
        cerrar.Click += (_, _) => Ocultar();
        DockPanel.SetDock(cerrar, Dock.Right);
        cabecera.Children.Add(cerrar);
        var titulos = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titulos.Children.Add(new TextBlock
        {
            Text = "Estudios",
            Foreground = Estudio.Tinta,
            FontSize = 15.5,
            FontWeight = FontWeights.SemiBold,
        });
        titulos.Children.Add(new TextBlock
        {
            Text = "Fotos de cardiología → resumen y preguntas",
            Foreground = Estudio.TintaMedia,
            FontSize = 11.5,
            Margin = new Thickness(0, 1, 0, 0),
        });
        cabecera.Children.Add(titulos);
        // Sin barra de título, arrastrar es cosa nuestra: por la cabecera, que no tiene nada que pulsar.
        cabecera.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        Grid.SetRow(cabecera, 0);
        raiz.Children.Add(cabecera);

        // ── el cuerpo, con scroll ────────────────────────────────────────────
        var cuerpo = new StackPanel { Margin = new Thickness(0, 0, 8, 8) };

        // La zona de carga: el botón grande, la pista de arrastrar y pegar, y un borde punteado que se
        // enciende cuando algo pasa por encima.
        _bordeDeLaZona = new Rectangle
        {
            RadiusX = 20,
            RadiusY = 20,
            Stroke = Estudio.BordeDeLaBarra,
            StrokeThickness = 1.3,
            StrokeDashArray = new DoubleCollection { 4, 3 },
        };
        _textoElegir = new TextBlock { Text = "Elegir fotos", FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Estudio.Tinta };
        var dentroDeElegir = new StackPanel { Orientation = Orientation.Horizontal };
        dentroDeElegir.Children.Add(new TextBlock
        {
            Text = "",   // Upload, de Segoe MDL2: el mismo glifo que la píldora del óvalo
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 14,
            Foreground = Estudio.Acento,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 9, 0),
        });
        dentroDeElegir.Children.Add(_textoElegir);
        var elegir = new Button
        {
            Content = dentroDeElegir,
            Height = 44,
            Padding = new Thickness(26, 0, 26, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = Estudio.Superficie,
            BorderBrush = Estudio.Borde,
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Template = Estudio.Pastilla(22),
        };
        elegir.ConRelieve(Estudio.Sombra2);
        elegir.Click += (_, _) => ElegirFotos();
        _pista = new TextBlock
        {
            Text = "o arrástralas aquí · Ctrl+V para pegar capturas o archivos",
            Foreground = Estudio.TintaTenue,
            FontSize = 11.5,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 10, 0, 0),
        };
        var dentroDeLaZona = new StackPanel { Margin = new Thickness(16, 18, 16, 16) };
        dentroDeLaZona.Children.Add(elegir);
        dentroDeLaZona.Children.Add(_pista);
        var zona = new Grid();
        zona.Children.Add(_bordeDeLaZona);
        zona.Children.Add(dentroDeLaZona);
        cuerpo.Children.Add(zona);

        cuerpo.Children.Add(_errores);
        cuerpo.Children.Add(_grilla);

        _generar = new Button
        {
            Height = 46,
            Margin = new Thickness(0, 16, 0, 0),
            Background = Estudio.Superficie,
            BorderBrush = Estudio.Borde,
            BorderThickness = new Thickness(1),
            Foreground = Estudio.Tinta,
            FontSize = 14.5,
            FontWeight = FontWeights.SemiBold,
            Cursor = Cursors.Hand,
            Template = Estudio.Pastilla(23),
        };
        _generar.ConRelieve(Estudio.Sombra2);
        _generar.Click += async (_, _) => await GenerarAsync();
        cuerpo.Children.Add(_generar);

        _progreso = new TextBlock
        {
            Foreground = Estudio.TintaMedia,
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0),
        };
        cuerpo.Children.Add(_progreso);
        _error = new TextBlock
        {
            Foreground = Estudio.Alerta,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 6, 2, 0),
        };
        cuerpo.Children.Add(_error);

        _omitidasBoton = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 12, 0, 0),
            Padding = new Thickness(8, 4, 10, 4),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Estudio.TintaMedia,
            FontSize = 12,
            Cursor = Cursors.Hand,
            Template = Estudio.Pastilla(Estudio.RadioChico),
        };
        _omitidasBoton.MouseEnter += (_, _) => _omitidasBoton.Background = Estudio.SuperficieSuave;
        _omitidasBoton.MouseLeave += (_, _) => _omitidasBoton.Background = Brushes.Transparent;
        _omitidasBoton.Click += (_, _) => { _omitidasAbiertas = !_omitidasAbiertas; Pintar(); };
        cuerpo.Children.Add(_omitidasBoton);
        cuerpo.Children.Add(_omitidasLista);

        // El resumen, en su propia tarjeta blanca con filete: es lo que el médico va a leer.
        var dentroDelResumen = new StackPanel();
        dentroDelResumen.Children.Add(Estudio.Rotulo("Resumen"));
        dentroDelResumen.Children.Add(_resumen);
        dentroDelResumen.Children.Add(new TextBlock
        {
            Text = "Apoyo a la lectura. Verifica contra el estudio original.",
            Foreground = Estudio.TintaTenue,
            FontSize = 11,
            FontStyle = FontStyles.Italic,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 0),
        });
        _tarjetaResumen = new Border
        {
            CornerRadius = new CornerRadius(Estudio.RadioPanel),
            Background = Estudio.Superficie,
            BorderBrush = Estudio.Borde,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(18, 16, 18, 16),
            Margin = new Thickness(0, 16, 0, 0),
            Child = dentroDelResumen,
        };
        cuerpo.Children.Add(_tarjetaResumen);

        // El chat: las vueltas, y el campo abajo.
        _seccionChat.Children.Add(Estudio.Rotulo("Preguntas"));
        _seccionChat.Children.Add(_chat);
        _errorChat = new TextBlock { Foreground = Estudio.Alerta, FontSize = 12, TextWrapping = TextWrapping.Wrap };
        _seccionChat.Children.Add(_errorChat);
        _pregunta = new TextBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Estudio.Tinta,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = false,
            MaxHeight = 90,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        _fantasmaDeLaPregunta = new TextBlock
        {
            Text = "Pregúntale a U sobre estas fotos…",
            Foreground = Estudio.TintaTenue,
            FontSize = 13,
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(3, 0, 0, 0),
        };
        _pregunta.TextChanged += (_, _) => PintarElCampo();
        _pregunta.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; await PreguntarAsync(); }
        };
        _enviar = new Button
        {
            Content = new TextBlock
            {
                Text = "",   // Send
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 13,
                Foreground = Brushes.White,
            },
            Width = 34,
            Height = 34,
            Background = Estudio.Acento,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Template = Estudio.Pastilla(17),
            ToolTip = "Preguntar (Enter)",
        };
        _enviar.ConRelieve();
        _enviar.Click += async (_, _) => await PreguntarAsync();
        var fila = new Grid();
        fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        fila.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var campo = new Grid { VerticalAlignment = VerticalAlignment.Center };
        campo.Children.Add(_fantasmaDeLaPregunta);
        campo.Children.Add(_pregunta);
        fila.Children.Add(campo);
        Grid.SetColumn(_enviar, 1);
        fila.Children.Add(_enviar);
        _seccionChat.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(22),
            Background = Estudio.SuperficieSuave,
            Padding = new Thickness(16, 5, 5, 5),
            Margin = new Thickness(0, 10, 0, 0),
            Child = fila,
        });
        cuerpo.Children.Add(_seccionChat);

        _scroll = new ScrollViewer
        {
            Content = cuerpo,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        Grid.SetRow(_scroll, 1);
        raiz.Children.Add(_scroll);

        // ── el pie ───────────────────────────────────────────────────────────
        var pie = new DockPanel { Margin = new Thickness(2, 10, 8, 0) };
        var borrar = new Button
        {
            Content = "Borrar ahora",
            Padding = new Thickness(12, 5, 12, 5),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Estudio.Alerta,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Cursor = Cursors.Hand,
            Template = Estudio.Pastilla(Estudio.RadioChico),
            ToolTip = "Borra de este equipo las fotos, lo leído, el resumen y el chat",
        };
        borrar.MouseEnter += (_, _) => borrar.Background = Estudio.AlertaSuave;
        borrar.MouseLeave += (_, _) => borrar.Background = Brushes.Transparent;
        borrar.Click += (_, _) => BorrarTodo("lo pidió el médico (Borrar ahora)");
        DockPanel.SetDock(borrar, Dock.Right);
        pie.Children.Add(borrar);
        _caducidad = new TextBlock { Foreground = Estudio.TintaTenue, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };
        pie.Children.Add(_caducidad);
        Grid.SetRow(pie, 2);
        raiz.Children.Add(pie);

        tarjeta.Child = raiz;
        Content = Estudio.Elevar(tarjeta, Estudio.Sombra3);
        this.Nitida();

        // ── el teclado, el arrastre y el portapapeles ────────────────────────
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; Ocultar(); }
            else if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control && PegarFotos()) e.Handled = true;
        };
        DragEnter += (_, e) => AlPasarPorEncima(e);
        DragOver += (_, e) => AlPasarPorEncima(e);
        DragLeave += (_, _) => _bordeDeLaZona.Stroke = Estudio.BordeDeLaBarra;
        Drop += async (_, e) =>
        {
            _bordeDeLaZona.Stroke = Estudio.BordeDeLaBarra;
            await SoltarAsync(e.Data);
        };

        _relojDelPie.Tick += (_, _) => { VigiaCardio.Revisar("con el panel abierto"); PintarPie(); };
        IsVisibleChanged += (_, _) => { if (IsVisible) _relojDelPie.Start(); else _relojDelPie.Stop(); };
        VigiaCardio.SeBorro += AlCaducar;
        Closed += (_, _) =>
        {
            VigiaCardio.SeBorro -= AlCaducar;
            _trabajo.Cancel();
            _relojDelPie.Stop();
            if (ReferenceEquals(_unica, this)) _unica = null;
        };

        Pintar();
    }

    public bool TieneFotos => _sesion.Fotos.Count > 0;

    // ── abrir, colocar, esconder ──────────────────────────────────────────────────────────────────

    /// <summary>Se muestra pegada a la izquierda del óvalo (o a su derecha, si a la izquierda no cabe).</summary>
    public void MostrarJuntoA(FrameworkElement ancla)
    {
        // Al abrir el panel también se mira la caducidad: el vigía pasa cada 10 minutos, y el médico
        // podría abrir justo en medio.
        VigiaCardio.Revisar("al abrir el panel");
        if (!IsVisible) Show();
        UpdateLayout();
        Colocar(ancla);
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        PintarPie();
    }

    private void Colocar(FrameworkElement ancla)
    {
        var area = SystemParameters.WorkArea;
        double w = ActualWidth, h = ActualHeight;
        var fuente = PresentationSource.FromVisual(ancla);
        if (fuente?.CompositionTarget == null || !ancla.IsVisible)
        {
            Left = area.Right - w - 200;
            Top = area.Bottom - h;
            return;
        }
        // PointToScreen da píxeles FÍSICOS; la ventana se coloca en DIPs. Con escalado al 150 % la
        // diferencia es de un tercio de pantalla.
        var aDips = fuente.CompositionTarget.TransformFromDevice;
        var arriba = aDips.Transform(ancla.PointToScreen(new Point(0, 0)));
        var abajo = aDips.Transform(ancla.PointToScreen(new Point(ancla.ActualWidth, ancla.ActualHeight)));

        const double Hueco = 12;
        // La holgura es el aire transparente que la sombra necesita: se descuenta para que el hueco
        // se mida entre lo que se VE, no entre los bordes invisibles de las dos ventanas.
        double izquierda = arriba.X - Hueco - (w - _holgura.Right);
        if (izquierda + _holgura.Left < area.Left) izquierda = abajo.X + Hueco - _holgura.Left;
        double arribaDelPanel = abajo.Y - h + _holgura.Bottom;
        Left = Math.Max(area.Left - _holgura.Left, Math.Min(izquierda, area.Right - w + _holgura.Right));
        Top = Math.Max(area.Top - _holgura.Top, Math.Min(arribaDelPanel, area.Bottom - h + _holgura.Bottom));
    }

    private void Ocultar() => Hide();

    // ── recibir fotos ─────────────────────────────────────────────────────────────────────────────

    /// <summary>El explorador de Windows, con selección múltiple y solo imágenes.</summary>
    public void ElegirFotos()
    {
        var dialogo = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Elegir fotos de estudios",
            Filter = Filtro,
            Multiselect = true,
            CheckFileExists = true,
        };
        if (dialogo.ShowDialog(this) == true) _ = AgregarArchivosAsync(dialogo.FileNames);
    }

    private static bool EsImagen(string ruta) =>
        Extensiones.Contains(System.IO.Path.GetExtension(ruta), StringComparer.OrdinalIgnoreCase);

    private void AlPasarPorEncima(DragEventArgs e)
    {
        bool sirve = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? ((e.Data.GetData(DataFormats.FileDrop) as string[]) ?? Array.Empty<string>()).Any(EsImagen)
            : e.Data.GetDataPresent(DataFormats.Bitmap) || e.Data.GetDataPresent("PNG");
        e.Effects = sirve ? DragDropEffects.Copy : DragDropEffects.None;
        _bordeDeLaZona.Stroke = sirve ? Estudio.Acento : Estudio.BordeDeLaBarra;
        e.Handled = true;
    }

    private async Task SoltarAsync(IDataObject datos)
    {
        try
        {
            if (datos.GetDataPresent(DataFormats.FileDrop))
            {
                var rutas = ((datos.GetData(DataFormats.FileDrop) as string[]) ?? Array.Empty<string>()).Where(EsImagen).ToList();
                await AgregarArchivosAsync(rutas);
                return;
            }
            var imagen = ImagenDe(datos);
            if (imagen != null) await AgregarImagenAsync(imagen, "arrastrada");
        }
        catch (Exception e)
        {
            LogBus.Log("cardio", $"no pude recibir lo arrastrado: {e.GetType().Name}: {e.Message}");
            MostrarErrores(new[] { "No se pudo recibir lo que arrastraste. Prueba con el botón «Elegir fotos»." });
        }
    }

    /// <summary>
    /// Ctrl+V. SOLO SE QUEDA CON EL PEGADO SI TRAE IMÁGENES: con texto en el portapapeles no hace nada y
    /// el campo de pregunta pega como siempre. Primero archivos copiados en el Explorador; si no hay,
    /// una imagen copiada (captura, WhatsApp Web, historia clínica).
    /// </summary>
    private bool PegarFotos()
    {
        try
        {
            if (Clipboard.ContainsFileDropList())
            {
                var rutas = Clipboard.GetFileDropList().Cast<string>().Where(EsImagen).ToList();
                if (rutas.Count == 0) return false;
                _ = AgregarArchivosAsync(rutas);
                return true;
            }
            var imagen = ImagenDe(Clipboard.GetDataObject());
            if (imagen == null) return false;
            _ = AgregarImagenAsync(imagen, "pegada");
            return true;
        }
        catch (Exception e)
        {
            // El portapapeles de Windows lo puede tener abierto otra aplicación (CLIPBRD_E_CANT_OPEN).
            LogBus.Log("cardio", $"no pude leer el portapapeles: {e.GetType().Name}: {e.Message}");
            MostrarErrores(new[] { "No se pudo leer el portapapeles. Vuelve a copiar la imagen y pega otra vez." });
            return true;
        }
    }

    /// <summary>
    /// Una imagen de un portapapeles o de un arrastre. «PNG» PRIMERO: es lo que ponen los navegadores y
    /// la herramienta de recortes, y conserva la transparencia de verdad. El mapa de bits genérico
    /// (CF_DIB) llega a veces con el canal alfa a cero en TODOS los píxeles —no significa nada, es
    /// relleno— y pintado sobre blanco saldría una foto en blanco; por eso se le quita el alfa.
    /// </summary>
    private static BitmapSource? ImagenDe(IDataObject? datos)
    {
        if (datos == null) return null;
        if (datos.GetDataPresent("PNG") && datos.GetData("PNG") is MemoryStream png)
        {
            var cuadro = BitmapDecoder.Create(png, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            cuadro.Freeze();
            return cuadro;
        }
        if (datos.GetDataPresent(DataFormats.Bitmap) && datos.GetData(DataFormats.Bitmap) is BitmapSource mapa)
        {
            // Se COPIA a un mapa propio: el del portapapeles es un InteropBitmap atado a un HBITMAP que
            // no siempre se deja congelar, y sin congelar no puede cruzar al hilo que lo prepara.
            var opaco = new FormatConvertedBitmap(mapa, PixelFormats.Bgr32, null, 0);
            int paso = opaco.PixelWidth * 4;
            var px = new byte[paso * opaco.PixelHeight];
            opaco.CopyPixels(px, paso, 0);
            var copia = BitmapSource.Create(opaco.PixelWidth, opaco.PixelHeight, 96, 96, PixelFormats.Bgr32, null, px, paso);
            copia.Freeze();
            return copia;
        }
        return null;
    }

    private Task AgregarArchivosAsync(IReadOnlyList<string> rutas) =>
        AgregarAsync(rutas.Select(ruta =>
        {
            string nombre = System.IO.Path.GetFileName(ruta);
            return (nombre, (Func<FotoPreparada>)(() =>
            {
                byte[] bytes;
                try { bytes = File.ReadAllBytes(ruta); }
                catch (Exception e) { return new FotoPreparada { Error = $"No se pudo abrir {nombre}: {e.Message}" }; }
                return PreparadorDeFotos.Preparar(nombre, bytes);
            }));
        }).ToList());

    private Task AgregarImagenAsync(BitmapSource imagen, string como)
    {
        string nombre = $"Imagen {como} {DateTime.Now:HH.mm.ss}.jpg";
        return AgregarAsync(new List<(string, Func<FotoPreparada>)> { (nombre, () => PreparadorDeFotos.Preparar(nombre, imagen)) });
    }

    /// <summary>
    /// Prepara las fotos de una en una, fuera del hilo de la interfaz, y las va enseñando según entran.
    /// Una que no se pueda leer se nombra y NO detiene a las demás.
    /// </summary>
    private async Task AgregarAsync(List<(string Nombre, Func<FotoPreparada> Preparar)> trabajos)
    {
        if (trabajos.Count == 0) return;
        var sesion = _sesion;
        _preparando++;
        _errores.Children.Clear();
        var fallos = new List<string>();
        int hechas = 0;
        foreach (var (nombre, preparar) in trabajos)
        {
            _progreso.Text = trabajos.Count == 1 ? "Preparando la foto…" : $"Preparando fotos {hechas + 1} de {trabajos.Count}…";
            var (preparada, miniatura) = await Task.Run(() =>
            {
                FotoPreparada p;
                try { p = preparar(); }
                catch (Exception e)
                {
                    // Preparar ya se queda con sus fallos; esto es la red por si algo se escapó, para que una
                    // foto rara no deje el panel para siempre en «Preparando…».
                    LogBus.Log("cardio", $"preparar una foto lanzó: {e.GetType().Name}: {e.Message}");
                    p = new FotoPreparada { Error = PreparadorDeFotos.NoSePudoLeer(nombre) };
                }
                BitmapSource? m = null;
                // Sin miniatura la foto sigue valiendo: la celda sale gris, y se reintenta al pintar.
                try { if (p.Ok) m = PreparadorDeFotos.Miniatura(p.Jpeg); }
                catch (Exception e) { LogBus.Log("cardio", $"miniatura: {e.GetType().Name}: {e.Message}"); }
                return (p, m);
            });
            hechas++;
            // «Borrar ahora» a mitad: lo que llega tarde ya no es de ninguna sesión.
            if (!ReferenceEquals(sesion, _sesion)) break;
            if (!preparada.Ok) { fallos.Add(preparada.Error); continue; }
            var foto = new FotoCardio { Id = Guid.NewGuid().ToString("N")[..12], Nombre = nombre, Jpeg = preparada.Jpeg };
            if (miniatura != null) _miniaturas[foto.Id] = miniatura;
            sesion.Agregar(foto, DateTime.UtcNow);
            PintarGrilla();
        }
        _preparando--;
        _progreso.Text = "";
        LogBus.Log("cardio", $"fotos cargadas: {hechas - fallos.Count} de {trabajos.Count}"
            + (fallos.Count > 0 ? $" ({fallos.Count} no se pudieron leer)" : ""));
        if (ReferenceEquals(sesion, _sesion)) Guardar();
        MostrarErrores(fallos);
        Pintar();
    }

    private void Quitar(string id)
    {
        _sesion.Quitar(id);
        _miniaturas.Remove(id);
        // Sin fotos no queda nada que resumir ni que preguntar: la sesión entera se va, y con ella la
        // cuenta de las 24 h. Quedarse con un chat sobre fotos que ya no están sería lo raro.
        if (_sesion.Fotos.Count == 0) { BorrarTodo("se quitaron todas las fotos"); return; }
        Guardar();
        Pintar();
    }

    // ── leer, resumir, preguntar ──────────────────────────────────────────────────────────────────

    private async Task GenerarAsync()
    {
        if (_generando || _preparando > 0) return;
        _generando = true;
        _error.Text = "";
        var sesion = _sesion;
        var trabajo = _trabajo;
        Pintar();
        try
        {
            await _cliente.GenerarAsync(sesion, linea => _progreso.Text = linea, trabajo.Token);
        }
        catch (OperationCanceledException) when (trabajo.IsCancellationRequested) { }
        catch (Exception e)
        {
            // El mensaje ya dice QUÉ lote y POR QUÉ (promesa 353); aquí solo se añade qué hacer.
            _error.Text = e.Message + " Lo ya leído se conserva: pulsa el botón para reintentar lo que faltó.";
            LogBus.Log("cardio", "generar falló: " + e.Message);
        }
        finally
        {
            _generando = false;
            _progreso.Text = "";
            if (ReferenceEquals(sesion, _sesion)) Guardar();
            Pintar();
        }
    }

    private async Task PreguntarAsync()
    {
        string pregunta = _pregunta.Text.Trim();
        if (pregunta.Length == 0 || _preguntaEnCurso.Length > 0 || string.IsNullOrEmpty(_sesion.Resumen)) return;
        _preguntaEnCurso = pregunta;
        _errorChat.Text = "";
        _pregunta.Text = "";
        var sesion = _sesion;
        var trabajo = _trabajo;
        PintarChat();
        _scroll.ScrollToEnd();
        try
        {
            await _cliente.PreguntarAsync(sesion, pregunta, trabajo.Token);
            if (ReferenceEquals(sesion, _sesion)) Guardar();
        }
        catch (OperationCanceledException) when (trabajo.IsCancellationRequested) { }
        catch (Exception e)
        {
            _errorChat.Text = e.Message;
            // La pregunta vuelve al campo: reintentar es pulsar Enter, no volver a escribirla.
            if (_pregunta.Text.Length == 0) _pregunta.Text = pregunta;
            LogBus.Log("cardio", "preguntar falló: " + e.Message);
        }
        finally
        {
            _preguntaEnCurso = "";
            PintarChat();
            _scroll.ScrollToEnd();
        }
    }

    // ── guardar y borrar ──────────────────────────────────────────────────────────────────────────

    private SesionCardio CargarLoGuardado()
    {
        try { return _almacen.Cargar() ?? new SesionCardio(); }
        catch (Exception e)
        {
            LogBus.Log("cardio", $"no pude cargar la sesión guardada: {e.GetType().Name}: {e.Message}");
            return new SesionCardio();
        }
    }

    /// <summary>
    /// Se empaqueta AQUÍ, en el hilo que toca la sesión, y se escribe en segundo plano y en fila: dos
    /// guardados seguidos no se adelantan uno al otro.
    /// </summary>
    private void Guardar()
    {
        var paquete = AlmacenCardio.Empaquetar(_sesion);
        _escritura = _escritura.ContinueWith(_ =>
        {
            try { _almacen.Escribir(paquete); }
            catch (Exception e) { LogBus.Log("cardio", $"no pude guardar la sesión: {e.GetType().Name}: {e.Message}"); }
        }, TaskScheduler.Default);
    }

    private void BorrarTodo(string porque)
    {
        _trabajo.Cancel();
        _trabajo = new CancellationTokenSource();
        _sesion = new SesionCardio();
        _miniaturas.Clear();
        _error.Text = "";
        _errorChat.Text = "";
        _errores.Children.Clear();
        _pregunta.Text = "";
        _escritura = _escritura.ContinueWith(_ =>
        {
            try { _almacen.Borrar(porque); }
            catch (Exception e) { LogBus.Log("cardio", $"no pude borrar la sesión: {e.GetType().Name}: {e.Message}"); }
        }, TaskScheduler.Default);
        Pintar();
    }

    /// <summary>El vigía la borró del disco: la de la memoria también se va, aunque el panel esté abierto.</summary>
    private void AlCaducar() => Dispatcher.BeginInvoke(() =>
    {
        _trabajo.Cancel();
        _trabajo = new CancellationTokenSource();
        _sesion = new SesionCardio();
        _miniaturas.Clear();
        _error.Text = "";
        _errorChat.Text = "";
        MostrarErrores(new[] { "Las fotos cumplieron su plazo y se borraron solas." });
        Pintar();
    });

    // ── pintar ────────────────────────────────────────────────────────────────────────────────────

    private void Pintar()
    {
        bool hayFotos = _sesion.Fotos.Count > 0;
        _textoElegir.Text = hayFotos ? "Agregar fotos" : "Elegir fotos";
        PintarGrilla();

        bool hayResumen = !string.IsNullOrEmpty(_sesion.Resumen);
        bool cambios = ReglaCardio.HayCambios(_sesion);
        _generar.Content = ReglaCardio.EtiquetaDelBoton(_sesion.Fotos.Count, hayResumen, cambios);
        _generar.IsEnabled = !_generando && _preparando == 0 && ReglaCardio.BotonHabilitado(_sesion.Fotos.Count, hayResumen, cambios);
        _generar.Visibility = hayFotos ? Visibility.Visible : Visibility.Collapsed;

        var omitidas = _sesion.Fotos.Where(f => f.Resultado?.Estado == EstadoDeFoto.Omitida).ToList();
        _omitidasBoton.Visibility = omitidas.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _omitidasBoton.Content = (_omitidasAbiertas ? "▾ " : "▸ ")
            + (omitidas.Count == 1 ? "1 foto no cardiológica omitida" : $"{omitidas.Count} fotos no cardiológicas omitidas");
        _omitidasLista.Children.Clear();
        _omitidasLista.Visibility = _omitidasAbiertas && omitidas.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var f in omitidas)
        {
            var linea = new TextBlock { FontSize = 12, Foreground = Estudio.TintaMedia, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) };
            linea.Inlines.Add(new Run(f.Nombre) { Foreground = Estudio.Tinta });
            linea.Inlines.Add(new Run(" — " + f.Resultado!.Motivo));
            _omitidasLista.Children.Add(linea);
        }

        _resumen.Children.Clear();
        _tarjetaResumen.Visibility = hayResumen ? Visibility.Visible : Visibility.Collapsed;
        if (hayResumen) _resumen.Children.Add(Markdown(_sesion.Resumen));

        PintarChat();
        PintarPie();
        if (IsActive && !IsKeyboardFocusWithin) Focus();
    }

    private void PintarGrilla()
    {
        _grilla.Children.Clear();
        _grilla.Visibility = _sesion.Fotos.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var foto in _sesion.Fotos) _grilla.Children.Add(Miniatura(foto));
    }

    private FrameworkElement Miniatura(FotoCardio foto)
    {
        if (!_miniaturas.TryGetValue(foto.Id, out var imagen))
        {
            try { imagen = _miniaturas[foto.Id] = PreparadorDeFotos.Miniatura(foto.Jpeg); }
            catch (Exception e) { LogBus.Log("cardio", $"miniatura ilegible: {e.GetType().Name}"); }
        }

        var celda = new Grid { Width = Lado, Height = Lado, Margin = new Thickness(0, 0, 8, 8) };
        celda.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(14),
            BorderBrush = Estudio.Borde,
            BorderThickness = new Thickness(1),
            Background = imagen != null
                ? new ImageBrush(imagen) { Stretch = Stretch.UniformToFill }
                : Estudio.SuperficieSuave,
        });

        string? etiqueta = foto.Resultado?.Estado switch
        {
            EstadoDeFoto.Cardiologia => "♥ cardiología",
            EstadoDeFoto.Omitida => "omitida",
            EstadoDeFoto.SinLeer => "sin leer",
            _ => null,
        };
        if (etiqueta != null)
        {
            celda.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(8),
                Background = Estudio.Superficie,
                BorderBrush = Estudio.Borde,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 1, 6, 2),
                Margin = new Thickness(5),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                Child = new TextBlock
                {
                    Text = etiqueta,
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = foto.Resultado!.Estado switch
                    {
                        EstadoDeFoto.Cardiologia => Estudio.Alerta,
                        EstadoDeFoto.SinLeer => Estudio.Espera,
                        _ => Estudio.TintaMedia,
                    },
                },
            });
            if (foto.Resultado.Estado == EstadoDeFoto.Omitida) celda.Opacity = 0.6;
        }

        var quitar = new Button
        {
            Content = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 8, Foreground = Estudio.Tinta },
            Width = 22,
            Height = 22,
            Margin = new Thickness(0, 5, 5, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Background = Estudio.Superficie,
            BorderBrush = Estudio.Borde,
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Template = Estudio.Pastilla(11),
            ToolTip = "Quitar esta foto",
            // Sin foco propio: al quitar la foto, el botón desaparece, y si se llevaba el foco con él
            // la ventana dejaba de oír Esc y Ctrl+V hasta el siguiente clic.
            Focusable = false,
        };
        quitar.ConRelieve();
        quitar.Click += (_, _) => Quitar(foto.Id);
        celda.Children.Add(quitar);

        celda.ToolTip = foto.Resultado?.Estado == EstadoDeFoto.Cardiologia
            ? $"{foto.Nombre}\n{foto.Resultado.Tipo}{(foto.Resultado.Fecha.Length > 0 ? " · " + foto.Resultado.Fecha : "")}"
            : foto.Resultado != null ? $"{foto.Nombre}\n{foto.Resultado.Motivo}" : foto.Nombre;
        return celda;
    }

    private void PintarChat()
    {
        bool hayResumen = !string.IsNullOrEmpty(_sesion.Resumen);
        _seccionChat.Visibility = hayResumen ? Visibility.Visible : Visibility.Collapsed;
        _chat.Children.Clear();
        foreach (var v in _sesion.Chat)
        {
            _chat.Children.Add(Burbuja(v.Pregunta));
            _chat.Children.Add(Respuesta(Markdown(v.Respuesta, 12.5)));
        }
        if (_preguntaEnCurso.Length > 0)
        {
            _chat.Children.Add(Burbuja(_preguntaEnCurso));
            _chat.Children.Add(Respuesta(new TextBlock { Text = "U está leyendo…", Foreground = Estudio.TintaTenue, FontSize = 12.5, FontStyle = FontStyles.Italic }));
        }
        _pregunta.IsEnabled = _preguntaEnCurso.Length == 0;
        _enviar.IsEnabled = _preguntaEnCurso.Length == 0;
        PintarElCampo();
    }

    private void PintarElCampo() =>
        _fantasmaDeLaPregunta.Visibility = _pregunta.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private static Border Burbuja(string texto) => new()
    {
        CornerRadius = new CornerRadius(16, 16, 4, 16),
        Background = Estudio.AcentoSuave,
        Padding = new Thickness(12, 8, 12, 8),
        Margin = new Thickness(60, 10, 0, 4),
        HorizontalAlignment = HorizontalAlignment.Right,
        Child = new TextBlock { Text = texto, Foreground = Estudio.Tinta, FontSize = 12.5, TextWrapping = TextWrapping.Wrap },
    };

    private static Border Respuesta(UIElement contenido) => new()
    {
        Padding = new Thickness(2, 4, 24, 4),
        Child = contenido,
    };

    private void PintarPie()
    {
        _caducidad.Text = _sesion.PrimeraFotoUtc == default
            ? $"Las fotos se borran solas a las {FormatoDeDuracion(AlmacenCardio.Duracion)}"
            : ReglaCardio.TextoDeCaducidad(_almacen.CaducaUtc(_sesion) - DateTime.UtcNow);
    }

    private static string FormatoDeDuracion(TimeSpan d) =>
        d.TotalHours >= 1 ? $"{d.TotalHours:0} h" : $"{d.TotalMinutes:0} min";

    private void MostrarErrores(IEnumerable<string> errores)
    {
        _errores.Children.Clear();
        foreach (string e in errores)
        {
            _errores.Children.Add(new TextBlock
            {
                Text = e,
                Foreground = Estudio.Alerta,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, 8, 2, 0),
            });
        }
    }

    /// <summary>
    /// El markdown del modelo, pintado con Runs: títulos, viñetas y negritas. No hay nada que
    /// interprete marcado, así que lo que no sea eso se ve literal (promesa 351).
    /// </summary>
    private static UIElement Markdown(string md, double tamano = 13)
    {
        var panel = new StackPanel();
        bool primero = true;
        foreach (var bloque in MarkdownSimple.Bloques(md))
        {
            var texto = new TextBlock
            {
                Foreground = Estudio.Tinta,
                FontSize = tamano,
                LineHeight = tamano * 1.45,
                TextWrapping = TextWrapping.Wrap,
            };
            foreach (var trozo in bloque.Trozos)
            {
                var run = new Run(trozo.Texto);
                if (trozo.Negrita) run.FontWeight = FontWeights.SemiBold;
                texto.Inlines.Add(run);
            }

            switch (bloque.Tipo)
            {
                case TipoDeBloque.Titulo:
                    texto.FontSize = tamano + 1;
                    texto.FontWeight = FontWeights.SemiBold;
                    texto.Margin = new Thickness(0, primero ? 0 : 12, 0, 3);
                    panel.Children.Add(texto);
                    break;
                case TipoDeBloque.Vineta:
                    var fila = new Grid { Margin = new Thickness(2, 1, 0, 1) };
                    fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
                    fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    fila.Children.Add(new TextBlock { Text = "•", Foreground = Estudio.TintaMedia, FontSize = tamano, LineHeight = tamano * 1.45 });
                    Grid.SetColumn(texto, 1);
                    fila.Children.Add(texto);
                    panel.Children.Add(fila);
                    break;
                default:
                    texto.Margin = new Thickness(0, 2, 0, 4);
                    panel.Children.Add(texto);
                    break;
            }
            primero = false;
        }
        return panel;
    }

    private static Button BotonDeMarco(string glifo, string queHace)
    {
        var b = new Button
        {
            Content = new TextBlock
            {
                Text = glifo,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 9.5,
                Foreground = Estudio.TintaMedia,
            },
            Width = 30,
            Height = 30,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Template = Estudio.Pastilla(15),
            ToolTip = queHace,
        };
        System.Windows.Automation.AutomationProperties.SetName(b, queHace);
        b.MouseEnter += (_, _) => b.Background = Estudio.SuperficieSuave;
        b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
        return b;
    }
}
