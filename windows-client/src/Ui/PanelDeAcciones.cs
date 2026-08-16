using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace U.WindowsClient.Ui;

/// <summary>
/// LA LISTA DE LO QUE VA PASANDO. Una columna flotante abajo a la izquierda donde cada acción
/// aparece EN CUANTO empieza y se resuelve donde estaba cuando termina.
///
/// Por qué hacía falta algo nuevo, habiendo tanto pintado ya: lo que existía o se sobrescribía o no
/// se veía. La burbuja de la carita usa <c>Narrate</c>, que REEMPLAZA, así que solo se ve el último
/// paso y los anteriores desaparecen; el chip dice «Trabajando…» sin decir en qué; el log lo tiene
/// todo pero hay que abrirlo y leerlo. Con una tarea de un solo paso da igual, pero en cuanto son
/// cuatro —«ve a descargas», «no, mejor documentos», «busca el informe»— la persona quiere ver la
/// secuencia, no el último fotograma.
///
/// TRES ESTADOS, NO DOS. Una acción que no se llegó a ejecutar tiene que dejar rastro: contar solo
/// los éxitos y los fallos fue exactamente lo que hizo que una corrida que se saltó 19 pasos
/// informara «29 de 30» (aprendizaje nº10 del CLAUDE.md). Aquí se pintan en curso, hecho, falló y
/// omitido, cada uno con su color de <see cref="UiPalette"/>.
///
/// EL TIEMPO SE ENSEÑA. Ya se medía y se tiraba a una línea de log. Puesto al lado de cada acción es
/// lo que convierte «se quedó pensando» en «esto tardó 2 400 ms y esto otro 40».
///
/// Es click-through y sin foco, como el resto de capas: se mira, no se toca.
/// </summary>
public sealed class PanelDeAcciones : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80;
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    /// <summary>Cuántas líneas se conservan a la vista. Suficiente para ver la secuencia de una
    /// conversación sin convertirse en un log: para eso ya está el log. Sube de 7 a 10 desde que
    /// aquí también va lo dicho: si la conversación y la maquinaria se reparten las mismas filas,
    /// dos herramientas seguidas te borran la pregunta que las provocó.</summary>
    private const int Memoria = 10;

    /// <summary>
    /// Cuánto aguanta en pantalla sin nada nuevo.
    ///
    /// Eran 20 s cuando esto solo enseñaba maquinaria —cuando deja de pasar algo, sobra—. Ahora
    /// también lleva la conversación, y una pausa de veinte segundos pensando qué pedir no es que
    /// sobre: es parte de hablar. Se sube a 90, que sigue siendo «se va solo» y no «hay que
    /// cerrarlo».
    /// </summary>
    private static readonly TimeSpan Caducidad = TimeSpan.FromSeconds(90);

    public enum Estado { EnCurso, Hecho, Fallo, Omitido }

    private readonly StackPanel _filas = new();
    private readonly DispatcherTimer _caducar;
    private DateTime _ultimoCambio = DateTime.UtcNow;

    /// <summary>La fila que está en curso, para resolverla en su sitio en vez de añadir otra.</summary>
    private Border? _enCurso;

    public PanelDeAcciones()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        IsHitTestVisible = false;
        Focusable = false;
        ShowActivated = false;
        Title = "Ü Acciones";

        Content = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x10, 0x10, 0x14)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x2A, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 8, 12, 8),
            Child = _filas,
        };

        SizeChanged += (_, __) => Reposition();

        _caducar = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _caducar.Tick += (_, __) =>
        {
            if (_enCurso == null && DateTime.UtcNow - _ultimoCambio > Caducidad) Limpiar();
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var h = new WindowInteropHelper(this).Handle;
        SetWindowLong(h, GWL_EXSTYLE,
            GetWindowLong(h, GWL_EXSTYLE) | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW);
    }

    /// <summary>
    /// Empieza una acción: se pinta YA, en azul de trabajo, antes de que se sepa cómo va a acabar.
    /// Ese instante es el que da la sensación de tiempo real; esperar al resultado para contar algo
    /// es justo lo que hacía que pareciera colgada.
    /// </summary>
    public void Empieza(string texto)
    {
        // Si la anterior nunca se resolvió, no se borra: se marca omitida. Una acción sin desenlace
        // que desaparece en silencio es la que hace que el recuento mienta.
        if (_enCurso != null) Resolver(Estado.Omitido, "sin respuesta");

        _enCurso = Fila(UiPalette.Trabajando, "•", texto);
        Anadir(_enCurso);
    }

    /// <summary>Cierra la que estaba en curso con su desenlace.</summary>
    public void Termina(string texto, bool ok) => Resolver(ok ? Estado.Hecho : Estado.Fallo, texto);

    /// <summary>La frase que se está diciendo ahora mismo: la tuya y la de Ü, cada una en su fila.</summary>
    private Border? _loQueDigo, _loQueDiceU;

    /// <summary>
    /// LO QUE SE OYE Y LO QUE SE CONTESTA, EN EL MISMO SITIO QUE LO QUE SE HACE.
    /// </summary>
    /// <remarks>
    /// La conversación se veía en la burbuja y la maquinaria aquí, así que para saber si te entendió
    /// había que mirar a dos sitios a la vez — y la burbuja REEMPLAZA, así que lo que dijiste hace
    /// dos frases ya no estaba. Cuando algo no funciona, la primera pregunta es «¿me oyó bien?», y
    /// no había forma de contestarla mirando (2026-08-16, lo pidió el usuario).
    ///
    /// Se ACTUALIZA LA FILA en vez de añadir una por trozo: la transcripción llega palabra a palabra
    /// y una fila por trozo convierte una frase en una columna de palabras sueltas. Lo que se busca
    /// es verla escribirse, que es lo que dice que te está oyendo AHORA.
    /// </remarks>
    public void Habla(string texto, bool esDeU)
    {
        var fila = esDeU ? _loQueDiceU : _loQueDigo;

        // Si su fila ya se fue por arriba —solo caben unas pocas— se empieza otra: actualizar una
        // fila que ya no está en pantalla es escribir donde nadie mira.
        if (fila != null && !_filas.Children.Contains(fila)) fila = null;

        if (fila == null)
        {
            fila = Fila(esDeU ? UiPalette.Vivo : UiPalette.Trabajando, esDeU ? "Ü" : "🗣", texto);
            if (esDeU) _loQueDiceU = fila; else _loQueDigo = fila;
            Anadir(fila);
            return;
        }

        if (fila.Child is StackPanel sp && sp.Children.Count == 2 && sp.Children[1] is TextBlock cuerpo)
            cuerpo.Text = texto;
        Toca();
    }

    /// <summary>Se acabó el turno: lo dicho queda fijo y la siguiente frase empieza fila nueva.</summary>
    public void CierraTurno()
    {
        _loQueDigo = null;
        _loQueDiceU = null;
    }

    private void Resolver(Estado estado, string texto)
    {
        if (_enCurso == null) { Empieza(texto); }
        var fila = _enCurso;
        if (fila == null) return;
        _enCurso = null;

        var (color, marca) = estado switch
        {
            Estado.Hecho => (UiPalette.Vivo, "✓"),
            Estado.Fallo => (UiPalette.Fallo, "✋"),
            Estado.Omitido => (UiPalette.Inactivo, "·"),
            _ => (UiPalette.Trabajando, "•"),
        };

        if (fila.Child is StackPanel sp && sp.Children.Count == 2
            && sp.Children[0] is TextBlock punto && sp.Children[1] is TextBlock cuerpo)
        {
            punto.Text = marca;
            punto.Foreground = new SolidColorBrush(color);
            cuerpo.Text = texto;
            cuerpo.Foreground = new SolidColorBrush(
                Color.FromArgb(estado == Estado.Omitido ? (byte)0x77 : (byte)0xDD, 0xFF, 0xFF, 0xFF));
        }
        Toca();
    }

    private static Border Fila(Color color, string marca, string texto)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new TextBlock
        {
            Text = marca,
            Foreground = new SolidColorBrush(color),
            FontSize = 12,
            Width = 16,
            FontFamily = new FontFamily("Segoe UI"),
        });
        sp.Children.Add(new TextBlock
        {
            Text = texto,
            Foreground = new SolidColorBrush(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF)),
            FontSize = 12,
            FontFamily = new FontFamily("Segoe UI"),
            MaxWidth = 420,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        return new Border { Padding = new Thickness(0, 2, 0, 2), Child = sp };
    }

    private void Anadir(Border fila)
    {
        _filas.Children.Add(fila);
        while (_filas.Children.Count > Memoria) _filas.Children.RemoveAt(0);
        Toca();
        if (!IsVisible) Show();
        if (!_caducar.IsEnabled) _caducar.Start();
    }

    private void Toca()
    {
        _ultimoCambio = DateTime.UtcNow;
        Reposition();
    }

    public void Limpiar()
    {
        _filas.Children.Clear();
        _enCurso = null;
        _caducar.Stop();
        Hide();
    }

    private void Reposition()
    {
        var wa = SystemParameters.WorkArea;
        // Abajo a la izquierda: la pastilla de superficie vive arriba a la derecha y el mapa justo
        // debajo, y la carita se aparca a la derecha. Esta esquina es la que queda libre.
        Left = wa.Left + 12;
        Top = wa.Bottom - ActualHeight - 12;
    }
}
