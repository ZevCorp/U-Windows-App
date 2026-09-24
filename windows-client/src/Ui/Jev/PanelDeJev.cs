using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Ui.Jev;

/// <summary>
/// EL PANEL DE JEV: paso, detectados, ms, coste, los dos medidores y las barras. Una ventana que dibuja exactamente
/// lo que devuelve <see cref="EstadoDeLaDecision.De"/> y nada más: ni ordena, ni filtra, ni busca por etiqueta.
/// Promesas 376 (el XAML), 377 (el notch como obstáculo) y 385 (nunca toma el ratón ni el foco) de la spec 049.
/// </summary>
/// <remarks>
/// EL XAML ES TEXTO, Y ES EL MISMO QUE MIDE EL CONTRATO. <see cref="Xaml"/> se parsea aquí para el contenido de la
/// ventana y en el arnés para medirlo sin pantalla (376): 64 sin resultados y 198,6 con cinco barras. Por eso no es
/// un <c>.xaml</c> compilado a BAML —el arnés no podría leerlo— sino una cadena con los colores de
/// <see cref="PaletaDeJev"/> y las medidas de <see cref="MedidaDelPanelDeJev"/> dentro, sin copiar ningún número.
///
/// NUNCA TOMA EL RATÓN NI EL FOCO (385): no tiene campo de texto (spec 049 §Lo que NO entra), así que la máscara
/// extendida es la misma en reposo y en corrida, y se comprueba después de ponerla (aprendizaje nº19). El objetivo
/// llega por la voz y se enseña en un <c>TextBlock</c>, no en el <c>TextBox</c> del plano.
///
/// NO DECIDE DÓNDE VA. El sitio lo calcula <see cref="MaquinaDeLaVista"/> con <see cref="DondeVaElPanel"/> (377) y
/// esta ventana solo lo aplica con <c>SetWindowPos</c> en físicos, sin tocar su tamaño: el alto lo da el contenido
/// (<c>SizeToContent</c>) y ese alto es el de <see cref="MedidaDelPanelDeJev.AltoDe"/>, que es lo que la 376
/// garantiza. Si el rect pedido y la ventana no miden lo mismo se dice en el log, porque entonces el sitio se calculó
/// para otro tamaño. Lo que el panel esquiva del notch sale de <see cref="Obstaculos"/>.
/// </remarks>
public sealed class PanelDeJev : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WM_WINDOWPOSCHANGING = 0x0046;
    private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;
    private const uint MONITOR_DEFAULTTOPRIMARY = 0x00000001;
    private const int MDT_EFFECTIVE_DPI = 0;

    /// <summary>Cuánto pueden discrepar, en físicos, el rect pedido y la ventana antes de decirlo: el redondeo de <c>Nitida()</c>.</summary>
    private const double Tolerancia = 1.5;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPOS { public IntPtr hwnd, hwndInsertAfter; public int x, y, cx, cy; public uint flags; }

    [DllImport("user32.dll", SetLastError = true)] private static extern int GetWindowLong(IntPtr h, int indice);
    [DllImport("user32.dll", SetLastError = true)] private static extern int SetWindowLong(IntPtr h, int indice, int valor);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowDisplayAffinity(IntPtr h, uint afinidad);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr h, IntPtr tras, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT punto, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr hMonitor, int tipo, out uint dpiX, out uint dpiY);

    private readonly FrameworkElement _raiz;
    private IntPtr _handle;

    /// <summary>El rect que se pidió, en físicos. <c>null</c> hasta el primer <see cref="Colocar"/>.</summary>
    private Rect? _pedido;

    /// <summary>
    /// El panel, sin mostrar y sin sitio: lo muestra, lo esconde y lo coloca quien lo maneja. Nace en reposo —sin
    /// ticker ni resultados— hasta el primer <see cref="Pintar(LoQuePinta)"/>.
    /// </summary>
    public PanelDeJev()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        ResizeMode = ResizeMode.NoResize;
        Width = MedidaDelPanelDeJev.Ancho;
        SizeToContent = SizeToContent.Height;
        Title = "Jev · panel";
        // Nítida se puede porque el fondo es opaco (PaletaDeJev.FondoDelPanel): con AllowsTransparency el ClearType
        // solo vuelve sobre fondo opaco (Estudio.cs).
        this.Nitida();

        _raiz = (FrameworkElement)XamlReader.Parse(Xaml);
        Content = _raiz;
        SizeChanged += (_, _) => Contrastar("al cambiar de tamaño");

        SiempreDelante.EntraAlGrupo(this, Capa.PanelDeJev);
    }

    /// <summary>
    /// El XAML del panel, como texto: la disposición del vídeo, fila a fila (plano §XAML, fila a fila). El borde va
    /// en una capa aparte y sin hijos porque en WPF <c>BorderThickness</c> ocupa sitio y le quitaba 1,6 a
    /// «Resultados» (medido: 312,4 en vez de 314). Cinco filas de barra ya declaradas: <see cref="Pintar(FrameworkElement, LoQuePinta)"/>
    /// enseña las que el modelo trae y pliega las demás.
    /// </summary>
    public static string Xaml { get; } = ArmarXaml();

    private static string ArmarXaml()
    {
        const double m = MedidaDelPanelDeJev.PaddingHorizontal;
        string filas = string.Concat(Enumerable.Range(0, EstadoDeLaDecision.BarrasComoMucho).Select(FilaDeBarra));
        return $$"""
            <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                  xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Width="{{N(MedidaDelPanelDeJev.Ancho)}}">
              <Border CornerRadius="10" Background="{{Hex(PaletaDeJev.FondoDelPanel)}}" BorderThickness="0"
                      Padding="{{N(m)}},{{N(MedidaDelPanelDeJev.PaddingArriba)}},{{N(m)}},{{N(MedidaDelPanelDeJev.PaddingAbajo)}}"
                      MinHeight="{{N(MedidaDelPanelDeJev.AltoMinimo)}}">
                <StackPanel>
                  <Grid Height="{{N(MedidaDelPanelDeJev.AltoDeLaEntrada)}}">
                    <Grid.ColumnDefinitions><ColumnDefinition Width="16"/><ColumnDefinition Width="9"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
                    <TextBlock Grid.Column="0" Text="⌘" FontFamily="Segoe UI Symbol" FontSize="11" Foreground="{{Hex(PaletaDeJev.TextoTerciario)}}"
                               HorizontalAlignment="Center" VerticalAlignment="Center"/>
                    <TextBlock Grid.Column="2" x:Name="Objetivo" FontFamily="Segoe UI Variable Text, Segoe UI" FontSize="13"
                               Foreground="{{Hex(PaletaDeJev.TextoPrimario)}}" TextTrimming="CharacterEllipsis" VerticalAlignment="Center"/>
                  </Grid>
                  <Grid x:Name="Ticker" Height="{{N(MedidaDelPanelDeJev.AltoDelTicker)}}" Margin="25,{{N(MedidaDelPanelDeJev.HuecoEntreFilas)}},2,0" Opacity="0">
                    <Grid.ColumnDefinitions><ColumnDefinition Width="4"/><ColumnDefinition Width="7"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
                    <Ellipse Grid.Column="0" x:Name="Punto" Width="4" Height="4" Fill="{{Hex(PaletaDeJev.TextoSecundario)}}" Opacity="{{N(EstadoDeLaDecision.PuntoSigue)}}"/>
                    <TextBlock Grid.Column="2" x:Name="TextoDelTicker" FontSize="11" Foreground="{{Hex(PaletaDeJev.TextoSecundario)}}"
                               TextTrimming="CharacterEllipsis" VerticalAlignment="Center" LineStackingStrategy="BlockLineHeight" LineHeight="14"/>
                  </Grid>
                  <StackPanel x:Name="Resultados" Margin="0,{{N(MedidaDelPanelDeJev.HuecoEntreFilas)}},0,0" Width="{{N(MedidaDelPanelDeJev.AnchoDeResultados)}}" Visibility="Collapsed">
                    <Rectangle Height="{{N(MedidaDelPanelDeJev.AltoDelDivisor)}}" Fill="{{Hex(Divisor)}}"/>
                    <DockPanel Height="{{N(MedidaDelPanelDeJev.AltoDeLaCabecera)}}" Margin="0,{{N(MedidaDelPanelDeJev.EspacioDeResultados)}},0,0"
                               TextBlock.FontFamily="Consolas" TextBlock.FontSize="10" TextBlock.LineStackingStrategy="BlockLineHeight" TextBlock.LineHeight="11">
                      <TextBlock DockPanel.Dock="Left" x:Name="Paso" Foreground="{{Hex(PaletaDeJev.TextoSecundario)}}"/>
                      <TextBlock DockPanel.Dock="Left" x:Name="Detectados" Foreground="{{Hex(PaletaDeJev.TextoTerciario)}}" Margin="6,0,0,0"/>
                      <TextBlock DockPanel.Dock="Right" x:Name="Coste" Foreground="{{Hex(PaletaDeJev.TextoTerciario)}}" Margin="6,0,0,0"/>
                      <TextBlock DockPanel.Dock="Right" x:Name="Ms" Foreground="{{Hex(PaletaDeJev.TextoTerciario)}}"/>
                      <Border/>
                    </DockPanel>
                    <StackPanel Orientation="Horizontal" Height="{{N(MedidaDelPanelDeJev.AltoDeLosMedidores)}}" Margin="0,{{N(MedidaDelPanelDeJev.EspacioDeResultados)}},0,0"
                                TextBlock.LineStackingStrategy="BlockLineHeight" TextBlock.LineHeight="11">
            {{Medidor("Cumplido", "cumplido", PaletaDeJev.Cumplido, 0)}}
            {{Medidor("Ausente", "ausente", PaletaDeJev.Ausente, 12)}}
                    </StackPanel>
            {{filas}}
                  </StackPanel>
                </StackPanel>
              </Border>
              <Border CornerRadius="10" BorderBrush="{{Hex(PaletaDeJev.BordeDelPanel)}}" BorderThickness="0.8" IsHitTestVisible="False"/>
            </Grid>
            """;
    }

    /// <summary>
    /// El divisor que abre los resultados: el gris del borde a la mitad de alfa (<c>#80373B39</c> del plano). No es un
    /// token de <see cref="PaletaDeJev"/> —la 371 fija dieciséis— sino el borde más tenue, y se deriva de él.
    /// </summary>
    private const uint Divisor = (PaletaDeJev.BordeDelPanel & 0x00FFFFFF) | 0x80000000;

    /// <summary>Un medidor: título, pista de 64×4 con su relleno, y el valor. El nombre es el sufijo de sus <c>x:Name</c>.</summary>
    private static string Medidor(string nombre, string titulo, uint tinte, double hueco) => $$"""
                      <TextBlock Margin="{{N(hueco)}},0,0,0" Text="{{titulo}}" FontSize="9" Foreground="{{Hex(PaletaDeJev.TextoTerciario)}}" VerticalAlignment="Center"/>
                      <Grid Width="{{N(AnchoDeLaPistaDelMedidor)}}" Height="4" Margin="5,0,0,0" VerticalAlignment="Center">
                        <Border CornerRadius="2" Background="{{Hex(PaletaDeJev.Pista)}}"/>
                        <Border x:Name="Relleno{{nombre}}" CornerRadius="2" Background="{{Hex(tinte)}}" HorizontalAlignment="Left" Width="0"/>
                      </Grid>
                      <TextBlock x:Name="Valor{{nombre}}" Margin="5,0,0,0" FontFamily="Consolas" FontSize="10" Foreground="{{Hex(PaletaDeJev.TextoSecundario)}}" VerticalAlignment="Center"/>
        """;

    /// <summary>Una fila de barra, plegada hasta que el modelo la traiga: etiqueta en 118, pista de 150×5, valor.</summary>
    private static string FilaDeBarra(int i) => $$"""
                    <Grid x:Name="Barra{{i}}" Height="{{N(MedidaDelPanelDeJev.AltoDeUnaBarra)}}" Margin="0,{{N(MedidaDelPanelDeJev.EspacioDeResultados)}},0,0" Visibility="Collapsed">
                      <Grid.ColumnDefinitions><ColumnDefinition Width="118"/><ColumnDefinition Width="6"/><ColumnDefinition Width="{{N(AnchoDeLaPistaDeBarra)}}"/><ColumnDefinition Width="6"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
                      <TextBlock Grid.Column="0" x:Name="Etiqueta{{i}}" FontSize="10" TextTrimming="CharacterEllipsis" VerticalAlignment="Center"/>
                      <Border Grid.Column="2" Height="5" CornerRadius="2.5" Background="{{Hex(PaletaDeJev.Pista)}}" VerticalAlignment="Center"/>
                      <Border Grid.Column="2" x:Name="Relleno{{i}}" Height="5" CornerRadius="2.5" HorizontalAlignment="Left" VerticalAlignment="Center" Width="0"/>
                      <TextBlock Grid.Column="4" x:Name="Valor{{i}}" FontFamily="Consolas" FontSize="10" VerticalAlignment="Center"/>
                    </Grid>

        """;

    private const double AnchoDeLaPistaDeBarra = 150, AnchoDeLaPistaDelMedidor = 64;

    /// <summary>
    /// Pinta en <paramref name="raiz"/> —el árbol de <see cref="Xaml"/>— exactamente lo que dice el modelo: textos tal
    /// cual, el ticker visible solo si trae texto, los resultados plegados si no hay distribución (373) y una fila por
    /// barra, en el orden en que llegan. Es estático para que el arnés lo llame sobre el XAML medido (376); la ventana
    /// lo llama sobre el suyo.
    /// </summary>
    /// <exception cref="InvalidOperationException">Si al árbol le falta un elemento con nombre: no es el XAML del panel.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Con más barras que filas. El modelo da cinco como mucho (372); una sexta no tiene fila, y callarla haría que
    /// el panel enseñara menos de lo que decidió Jev sin decirlo.
    /// </exception>
    public static void Pintar(FrameworkElement raiz, LoQuePinta loQuePinta)
    {
        ArgumentNullException.ThrowIfNull(raiz);
        ArgumentNullException.ThrowIfNull(loQuePinta);

        Hijo<TextBlock>(raiz, "Objetivo").Text = loQuePinta.Objetivo;
        // TICKER VACÍO = NO SE VE (LoQuePinta.Ticker). Con opacidad y no plegado: su fila ocupa lo mismo con y sin
        // texto, así el alto sin resultados no depende de si el tramo ya dijo algo.
        Hijo<FrameworkElement>(raiz, "Ticker").Opacity = string.IsNullOrEmpty(loQuePinta.Ticker) ? 0 : 1;
        Hijo<TextBlock>(raiz, "TextoDelTicker").Text = loQuePinta.Ticker;
        Hijo<Ellipse>(raiz, "Punto").Opacity = loQuePinta.Punto;

        var resultados = loQuePinta.Resultados;
        Hijo<FrameworkElement>(raiz, "Resultados").Visibility = resultados == null ? Visibility.Collapsed : Visibility.Visible;
        if (resultados == null) return;

        Hijo<TextBlock>(raiz, "Paso").Text = resultados.Cabecera.Paso;
        Hijo<TextBlock>(raiz, "Detectados").Text = resultados.Cabecera.Detectados;
        Hijo<TextBlock>(raiz, "Ms").Text = resultados.Cabecera.Ms;
        Hijo<TextBlock>(raiz, "Coste").Text = loQuePinta.Coste;
        PintarMedidor(raiz, "Cumplido", resultados.Medidores.Cumplido);
        PintarMedidor(raiz, "Ausente", resultados.Medidores.Ausente);

        var barras = resultados.Barras;
        if (barras.Count > EstadoDeLaDecision.BarrasComoMucho)
            throw new ArgumentOutOfRangeException(nameof(loQuePinta), barras.Count,
                $"el panel tiene {EstadoDeLaDecision.BarrasComoMucho} filas de barra y el modelo trae {barras.Count}");
        for (int i = 0; i < EstadoDeLaDecision.BarrasComoMucho; i++)
        {
            var fila = Hijo<FrameworkElement>(raiz, $"Barra{i}");
            if (i >= barras.Count) { fila.Visibility = Visibility.Collapsed; continue; }
            var barra = barras[i];
            fila.Visibility = Visibility.Visible;
            var etiqueta = Hijo<TextBlock>(raiz, $"Etiqueta{i}");
            etiqueta.Text = barra.Etiqueta;
            etiqueta.FontWeight = barra.Resaltada ? FontWeights.SemiBold : FontWeights.Normal;
            etiqueta.Foreground = Pincel(barra.Resaltada ? PaletaDeJev.TextoPrimario : PaletaDeJev.TextoSecundario);
            var relleno = Hijo<Border>(raiz, $"Relleno{i}");
            relleno.Width = Relleno(AnchoDeLaPistaDeBarra, barra.Probabilidad);
            relleno.Background = Pincel(barra.Resaltada ? PaletaDeJev.BarraElegida : PaletaDeJev.BarraNoElegida);
            var valor = Hijo<TextBlock>(raiz, $"Valor{i}");
            valor.Text = barra.Valor;
            valor.Foreground = Pincel(barra.Resaltada ? PaletaDeJev.TextoPrimario : PaletaDeJev.TextoTerciario);
        }
    }

    /// <summary>Pinta este ciclo en la ventana. En el hilo de la interfaz: quien publica desde otro pasa por el despachador (382).</summary>
    public void Pintar(LoQuePinta loQuePinta) => Pintar(_raiz, loQuePinta);

    /// <summary>
    /// Pone el panel en <paramref name="rect"/> (físicos, el <see cref="MaquinaDeLaVista.RectDelPanel"/>): mueve la
    /// ventana y no la redimensiona, porque su alto lo da el contenido. Sin HWND todavía, se guarda y se aplica al
    /// nacer. No toca su sitio en Z, que es del grupo (378).
    /// </summary>
    /// <exception cref="ArgumentException">Con un rect vacío: no hay sitio que aplicar, y ponerlo en (0, 0) sería inventarlo.</exception>
    public void Colocar(Rect rect)
    {
        if (rect.IsEmpty || !double.IsFinite(rect.X) || !double.IsFinite(rect.Y))
            throw new ArgumentException($"el panel de Jev se coloca en un rect con sitio, y llegó {rect}", nameof(rect));
        _pedido = rect;
        if (_handle != IntPtr.Zero) Aplicar(rect, "al colocarlo");
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _handle = new WindowInteropHelper(this).Handle;

        // NUNCA TOMA EL RATÓN NI EL FOCO (385), y se COMPRUEBA después (aprendizaje nº19): un SetWindowLong que no
        // agarra dejaría un panel que se come los clics de la app de trabajo. La máscara se nombra UNA vez, en la
        // línea que la toma: la 385 la lee por fuente, y un nombre repetido la dejaría verde sin aplicarla.
        uint mascara = EstilosDeVentana.ExtendidosDelPanel;
        int antes = GetWindowLong(_handle, GWL_EXSTYLE);
        SetWindowLong(_handle, GWL_EXSTYLE, antes | unchecked((int)mascara));
        uint quedo = unchecked((uint)GetWindowLong(_handle, GWL_EXSTYLE));
        if ((quedo & mascara) != mascara)
            LogBus.Log("jev-panel", $"el panel quedó con estilos 0x{quedo:X8} y le faltan 0x{mascara & ~quedo:X8}: puede tomar el ratón o el foco");
        // FUERA DE LA CAPTURA, como el overlay y la flecha (385, revisión del 2026-09-23). El panel es opaco, va siempre
        // encima y cae dentro del área de trabajo: sin esto, la foto que Luna manda al modelo, los fotogramas de voz y
        // la captura del puente consciente (todas CopyFromScreen) llevaban 340×199 de panel tapando el formulario.
        if (!SetWindowDisplayAffinity(_handle, EstilosDeVentana.Afinidad))
            LogBus.Log("jev-panel", $"no se pudo excluir de la captura el panel (SetWindowDisplayAffinity, error {Marshal.GetLastWin32Error()}): saldrá en las fotos que se mandan al modelo y en las grabaciones");

        HwndSource.FromHwnd(_handle)?.AddHook(MantenerElSitio);
        if (_pedido is Rect pedido) Aplicar(pedido, "al nacer");
    }

    /// <summary>
    /// CAMBIÓ EL DPI: WPF pone la ventana en el rect que sugiere Windows y rehace su tamaño con la escala nueva. Se
    /// vuelve a aplicar el sitio CALCULADO (<see cref="ReglaDeDpi.RectTrasCambio"/>), y el tamaño en físicos ya es
    /// otro: quien calculó el sitio lo hizo con la escala vieja, y el contraste lo dice.
    /// </summary>
    protected override void OnDpiChanged(DpiScale viejo, DpiScale nuevo)
    {
        base.OnDpiChanged(viejo, nuevo);
        if (_handle == IntPtr.Zero || _pedido is not Rect pedido) return;
        Aplicar(ReglaDeDpi.RectTrasCambio(pedido, RectDeLaVentana()), $"tras cambiar la escala de {viejo.PixelsPerDip:0.##} a {nuevo.PixelsPerDip:0.##}");
    }

    private void Aplicar(Rect rect, string cuando)
    {
        if (!SetWindowPos(_handle, IntPtr.Zero, (int)Math.Round(rect.X), (int)Math.Round(rect.Y), 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE))
            LogBus.Log("jev-panel", $"SetWindowPos {cuando} a {rect.TopLeft} falló (error {Marshal.GetLastWin32Error()}): el panel se queda donde estaba");
        Contrastar(cuando);
    }

    /// <summary>
    /// CONTRASTE del tamaño: el sitio se calculó con un tamaño (<see cref="MaquinaDeLaVista.TamanoDelPanel"/>) y la
    /// ventana mide el suyo. Si discrepan más que el redondeo, el panel puede tapar lo que se calculó que esquivaba, y
    /// se dice con los dos números; no se corrige aquí, porque recalcular el sitio es de quien lo calcula.
    /// </summary>
    private void Contrastar(string cuando)
    {
        if (_handle == IntPtr.Zero || _pedido is not Rect pedido || !IsVisible) return;
        var ventana = RectDeLaVentana();
        if (ventana.IsEmpty)
        {
            LogBus.Log("jev-panel", $"CONTRASTE panel {cuando}: GetWindowRect falló (error {Marshal.GetLastWin32Error()}); no sé cuánto mide");
            return;
        }
        if (Math.Abs(ventana.Width - pedido.Width) > Tolerancia || Math.Abs(ventana.Height - pedido.Height) > Tolerancia)
            LogBus.Log("jev-panel", $"CONTRASTE panel {cuando}: el sitio se pidió para {pedido.Width:0.#}×{pedido.Height:0.#} y la ventana mide {ventana.Width:0}×{ventana.Height:0} físicos en {ventana.TopLeft}");
    }

    private Rect RectDeLaVentana() =>
        GetWindowRect(_handle, out var r) ? new Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top) : Rect.Empty;

    /// <summary>
    /// EL SITIO NO SE MUEVE, LO MUEVA QUIEN LO MUEVA: al mostrarla WPF la pondría donde Windows quiera y al cambiar el
    /// DPI en el sugerido. Cada <c>WM_WINDOWPOSCHANGING</c> que traiga posición se reescribe con la pedida; el tamaño
    /// se deja pasar, que es el del contenido, y las llamadas del grupo en Z (<c>SWP_NOMOVE</c>) pasan intactas.
    /// </summary>
    private IntPtr MantenerElSitio(IntPtr h, int mensaje, IntPtr wParam, IntPtr lParam, ref bool manejado)
    {
        if (mensaje != WM_WINDOWPOSCHANGING || _pedido is not Rect r) return IntPtr.Zero;
        var pos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
        if ((pos.flags & SWP_NOMOVE) != 0) return IntPtr.Zero;
        pos.x = (int)Math.Round(r.X);
        pos.y = (int)Math.Round(r.Y);
        Marshal.StructureToPtr(pos, lParam, false);
        return IntPtr.Zero;
    }

    /// <summary>
    /// LO QUE EL PANEL NO CRUZA si alguna esquina cabe (377): el notch, en físicos, calculado por el mismo camino por el
    /// que se pone —<see cref="LaBarraDeTareas.Mirar"/> y <see cref="DondeVaElPanel.NotchEnFisicos"/> con el primario,
    /// que es donde vive—. Es lo que la vista le da a <see cref="MaquinaDeLaVista.Obstaculos"/>. Si Windows no da el
    /// primario o su escala, la lista sale vacía y se dice con el paso que falló: el panel podría cruzar el notch.
    /// </summary>
    public static IReadOnlyList<Rect> Obstaculos()
    {
        var hPrimario = MonitorFromPoint(new POINT { X = 0, Y = 0 }, MONITOR_DEFAULTTOPRIMARY);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (hPrimario == IntPtr.Zero || !GetMonitorInfo(hPrimario, ref info))
        {
            LogBus.Log("jev-panel", $"sin obstáculos para el panel: GetMonitorInfo del primario falló (hMonitor 0x{hPrimario.ToInt64():X}, error {Marshal.GetLastWin32Error()}); el panel puede cruzar el notch");
            return Array.Empty<Rect>();
        }
        int hr = GetDpiForMonitor(hPrimario, MDT_EFFECTIVE_DPI, out uint dpiX, out _);
        if (hr != 0)
        {
            LogBus.Log("jev-panel", $"sin obstáculos para el panel: GetDpiForMonitor del primario devolvió 0x{hr:X8}; el panel puede cruzar el notch");
            return Array.Empty<Rect>();
        }
        var rc = new Rect(info.rcMonitor.Left, info.rcMonitor.Top, info.rcMonitor.Right - info.rcMonitor.Left, info.rcMonitor.Bottom - info.rcMonitor.Top);
        var (libre, _) = LaBarraDeTareas.Mirar();
        return new[] { DondeVaElPanel.NotchEnFisicos(libre, Pantallas.DelMonitor(rc, dpiX / 96.0)) };
    }

    private static void PintarMedidor(FrameworkElement raiz, string nombre, MedidorDeJev medidor)
    {
        Hijo<Border>(raiz, $"Relleno{nombre}").Width = Relleno(AnchoDeLaPistaDelMedidor, medidor.Relleno);
        Hijo<TextBlock>(raiz, $"Valor{nombre}").Text = medidor.Texto;
    }

    /// <summary>
    /// Cuánto de una pista se rellena: <c>max(2, ancho·v)</c> como el plano, para que un valor pequeño se vea, y 0
    /// con 0. El modelo da 0 cuando el medidor no vino («—», <see cref="MedidorDeJev"/>): dos DIP de relleno ahí
    /// enseñarían un dato que no llegó (aprendizaje nº4). Una barra de probabilidad 0,00 tampoco se rellena.
    /// </summary>
    private static double Relleno(double ancho, double v) => v > 0 ? Math.Min(ancho, Math.Max(2, ancho * v)) : 0;

    private static T Hijo<T>(FrameworkElement raiz, string nombre) where T : class =>
        raiz.FindName(nombre) as T
        ?? throw new InvalidOperationException($"el árbol que se pinta no tiene «{nombre}» como {typeof(T).Name}: no es el XAML del panel de Jev");

    private static SolidColorBrush Pincel(uint argb)
    {
        var p = new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
        p.Freeze();
        return p;
    }

    private static string Hex(uint argb) => "#" + argb.ToString("X8", System.Globalization.CultureInfo.InvariantCulture);

    private static string N(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
}
