using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using U.Ciclo;

namespace U.Nuevo;

/// <summary>
/// LA BURBUJA: flota, está quieta hasta que se le habla, y no se roba el foco. Clic = hablarle (abre la voz);
/// clic derecho = escribirle un pedido o salir. Ctrl+Alt+Espacio también la despierta.
///
/// NO SE ACTIVA NUNCA (WS_EX_NOACTIVATE): si al pulsarla se llevara el foco, «dónde estoy» sería ella y no
/// la app de la persona. Y mientras Ü actúa se vuelve transparente al ratón, para que un clic de Ü que caiga
/// encima llegue a la app de debajo.
/// </summary>
public sealed class Burbuja : Window
{
    private readonly Ellipse _circulo;
    private readonly TextBlock _rotulo;
    private readonly TextBlock _letra;
    private readonly Asistente _ü;
    private readonly string? _claveOpenAI;
    private SesionDeVoz? _voz;
    private IntPtr _hwnd;

    public Burbuja(Asistente ü, string? claveOpenAI)
    {
        _ü = ü; _claveOpenAI = claveOpenAI;
        Title = "Ü";
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        Topmost = true; ShowInTaskbar = false; ResizeMode = ResizeMode.NoResize;
        Width = 320; Height = 96;
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - 16; Top = area.Bottom - Height - 16;

        _circulo = new Ellipse { Width = 56, Height = 56, Fill = Color("#8A8F98"), Stroke = Brushes.White, StrokeThickness = 2, Cursor = Cursors.Hand };
        _letra = new TextBlock { Text = "Ü", FontSize = 26, FontWeight = FontWeights.Bold, Foreground = Brushes.White, IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _rotulo = new TextBlock { FontSize = 12, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, MaxWidth = 250,
            Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
        var fondoRotulo = new Border { Child = _rotulo, Background = Color("#CC1E1F24"), CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 4, 8, 4),
            VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        _rotulo.TargetUpdated += (_, _) => { };
        var circulo = new Grid { Width = 56, Height = 56, VerticalAlignment = VerticalAlignment.Center };
        circulo.Children.Add(_circulo); circulo.Children.Add(_letra);
        var fila = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        fila.Children.Add(fondoRotulo); fila.Children.Add(circulo);
        Content = fila;

        // CLIC AL SOLTAR, ARRASTRE SOLO SI SE MUEVE. La primera versión llamaba a DragMove() en el MouseDown, y
        // DragMove entra en un bucle modal que espera el MouseUp: un clic rápido (el del ratón real, o el de una
        // persona decidida) quedaba atrapado ahí y la burbuja no despertaba nunca (2026-09-24, 23:16).
        Point? abajo = null;
        _circulo.MouseLeftButtonDown += (_, e) => { abajo = e.GetPosition(this); _circulo.CaptureMouse(); };
        _circulo.MouseMove += (_, e) =>
        {
            if (abajo is { } a && e.LeftButton == MouseButtonState.Pressed && (e.GetPosition(this) - a).Length > 4)
            {
                abajo = null; _circulo.ReleaseMouseCapture();
                try { DragMove(); } catch (InvalidOperationException) { }
            }
        };
        _circulo.MouseLeftButtonUp += (_, e) =>
        {
            _circulo.ReleaseMouseCapture();
            if (abajo != null) { abajo = null; _ = Despertar(); }
        };
        var menu = new ContextMenu();
        menu.Items.Add(Item("Hablarle", () => _ = Despertar()));
        menu.Items.Add(Item("Escribirle un pedido…", Escribir));
        menu.Items.Add(Item("Callar", () => _ = _voz?.CerrarAsync("se lo pidieron")));
        menu.Items.Add(Item("Abrir el log", () => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Registro.Carpeta) { UseShellExecute = true })));
        menu.Items.Add(Item("Salir", () => Application.Current.Shutdown()));
        _circulo.ContextMenu = menu;

        // El rótulo dice lo último que pasó y se apaga a los 6 s: quieta es quieta, también a la vista.
        var apagar = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        apagar.Tick += (_, _) => { apagar.Stop(); fondoRotulo.Visibility = Visibility.Collapsed; };
        Registro.Linea += l => Dispatcher.BeginInvoke(() =>
        {
            _rotulo.Text = l.Length > 140 ? l[..140] + "…" : l;
            fondoRotulo.Visibility = Visibility.Visible;
            apagar.Stop(); apagar.Start();
        });
        PonerEstado("quieta");
    }

    private static MenuItem Item(string t, Action a) { var m = new MenuItem { Header = t }; m.Click += (_, _) => a(); return m; }
    private static SolidColorBrush Color(string hex) => new((System.Windows.Media.Color)ColorConverter.ConvertFromString(hex));

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        SetWindowLong(_hwnd, -20, GetWindowLong(_hwnd, -20) | 0x08000000 /* NOACTIVATE */ | 0x00000080 /* TOOLWINDOW */);
        HwndSource.FromHwnd(_hwnd)?.AddHook(Gancho);
        // EL ATAJO SE DICE SI NO SE PUDO PONER. Ctrl+Alt+Espacio lo ocupa la app de escritorio de Claude en esta
        // máquina, y un RegisterHotKey fallido sin log dejaba el atajo muerto sin que nadie lo supiera (23:16).
        var atajos = new (uint Mods, uint Vk, string Nombre)[]
        {
            (0x0002 | 0x0001, 0x20, "Ctrl+Alt+Espacio"), (0x0002 | 0x0004, 0x20, "Ctrl+Mayús+Espacio"), (0x0002 | 0x0001, 0x59, "Ctrl+Alt+Y"),
        };
        string puesto = "";
        foreach (var (mods, vk, nombre) in atajos)
            if (RegisterHotKey(_hwnd, 1, mods, vk)) { puesto = nombre; break; }
            else Registro.Log($"burbuja: el atajo {nombre} está ocupado (error {Marshal.GetLastWin32Error()})");
        Registro.Log(puesto.Length > 0 ? $"burbuja: para hablarle, clic o {puesto}" : "burbuja: ningún atajo libre; para hablarle, clic en la burbuja");
    }

    private IntPtr Gancho(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool manejado)
    {
        if (msg == 0x0312 /* WM_HOTKEY */) { _ = Despertar(); manejado = true; }
        return IntPtr.Zero;
    }

    public void PonerEstado(string estado)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _circulo.Fill = Color(estado switch
            {
                "escuchando" => "#2F80ED",
                "pensando" => "#8E44AD",
                "actuando" => "#27AE60",
                "error" => "#D64545",
                _ => "#8A8F98",
            });
            // Transparente al ratón mientras actúa: un clic de Ü que caiga encima llega a la app de debajo.
            int ex = GetWindowLong(_hwnd, -20);
            SetWindowLong(_hwnd, -20, estado == "actuando" ? ex | 0x20 : ex & ~0x20);
        });
    }

    private async Task Despertar()
    {
        if (_voz is { Abierta: true }) return;
        if (_claveOpenAI == null) { Registro.Log("✘ sin clave de OpenAI no hay voz: usa «Escribirle un pedido…»"); PonerEstado("error"); return; }
        _voz = new SesionDeVoz(_claveOpenAI, _ü);
        _voz.Estado += PonerEstado;
        _voz.Estado += s => { if (s == "cerrada") PonerEstado("quieta"); };
        string oido = "", dicho = "";
        _voz.DiceUsuario += t => { oido += t; if (t.Contains('.') || t.Contains('?') || oido.Length > 120) { Registro.Log("🗣 " + oido.Trim()); oido = ""; } };
        _voz.DiceU += t => { dicho += t; if (t.Contains('.') || t.Contains('?') || dicho.Length > 120) { Registro.Log("Ü: " + dicho.Trim()); dicho = ""; } };
        PonerEstado("pensando");
        try { await _voz.AbrirAsync(); }
        catch (Exception e) { Registro.Log($"✘ no pude abrir la voz: {e.GetType().Name}: {e.Message}"); PonerEstado("error"); }
    }

    private void Escribir()
    {
        var caja = new TextBox { Width = 380, Margin = new Thickness(8) };
        var w = new Window { Title = "Pídele algo a Ü", Content = caja, SizeToContent = SizeToContent.WidthAndHeight, Topmost = true, WindowStartupLocation = WindowStartupLocation.CenterScreen };
        caja.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            string pedido = caja.Text.Trim();
            w.Close();
            if (pedido.Length == 0) return;
            Registro.Log("✍ " + pedido);
            if (_voz is { Abierta: true }) { _ = _voz.EscribirAsync(pedido); return; }
            if (_claveOpenAI == null) { Registro.Log("✘ sin clave de OpenAI no hay Luna"); return; }
            // Sin voz abierta, por texto: la ventana del pedido se cerró y el foco vuelve a la app de la persona.
            _ = Task.Run(() =>
            {
                PonerEstado("actuando");
                using var luna = new LunaPorTexto(_claveOpenAI) { Log = Registro.Log };
                string r = luna.Pedir(pedido, _ü);
                Registro.Log("Ü: " + r);
                PonerEstado("quieta");
            });
        };
        w.Show();
        caja.Focus();
    }

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h, int i, int v);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
}
