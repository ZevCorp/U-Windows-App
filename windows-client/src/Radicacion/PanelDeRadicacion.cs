using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Radicacion;

/// <summary>
/// El chat flotante de radicación. Ventana aparte, sin .xaml —igual que PanelDelCollar.cs— porque
/// no vale la pena tocar el build por una ventana de este tamaño.
///
/// DEMO, código aparte a propósito: nada de este archivo ni de la carpeta Radicacion/ es tocado por
/// ni toca SurfaceMap, el núcleo congelado, ni ninguna promesa de Contrato.cs.
///
/// Flujo (pedido explícitamente por el cliente): adjuntar documento → el LLM lo lee y propone una
/// clasificación → LA FUNCIONARIA REVISA Y CONFIRMA → solo ahí se asigna el número (provisional) y
/// se manda el correo. Nada se envía sin ese clic de confirmación.
/// </summary>
public sealed class PanelDeRadicacion : Window
{
    private readonly StackPanel _mensajes = new() { Margin = new Thickness(12) };
    private readonly TextBox _entrada = new()
    {
        FontSize = 13,
        Padding = new Thickness(8),
        Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x30)),
        Foreground = Brushes.White,
        BorderThickness = new Thickness(0),
    };
    private readonly Button _adjuntar = new() { Content = "📎 Adjuntar documento", Height = 30, FontSize = 12, Margin = new Thickness(0, 0, 6, 0) };
    private readonly TextBlock _estado = new() { FontSize = 11, Opacity = 0.7, Margin = new Thickness(12, 0, 12, 6), TextWrapping = TextWrapping.Wrap };

    private readonly List<AreaDestino> _areas = AreasConfig.Cargar();

    public PanelDeRadicacion()
    {
        Title = "Radicación — demo";
        Width = 460;
        Height = 640;
        MinWidth = 380;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x22));
        Foreground = Brushes.White;
        AllowDrop = true;

        var raiz = new Grid();
        raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        raiz.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var titulo = new TextBlock
        {
            Text = "🧾 Radicación por chat",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(14, 12, 14, 4),
        };
        Grid.SetRow(titulo, 0);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _mensajes };
        Grid.SetRow(scroll, 1);

        Grid.SetRow(_estado, 2);

        var filaEntrada = new DockPanel { Margin = new Thickness(12, 0, 12, 12) };
        _adjuntar.Click += (_, __) => AdjuntarPorDialogo();
        DockPanel.SetDock(_adjuntar, Dock.Left);
        filaEntrada.Children.Add(_adjuntar);
        var enviar = new Button { Content = "Enviar", Width = 70, Height = 30, FontSize = 12, Margin = new Thickness(6, 0, 0, 0) };
        DockPanel.SetDock(enviar, Dock.Right);
        enviar.Click += (_, __) => EnviarTextoLibre();
        filaEntrada.Children.Add(enviar);
        _entrada.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) EnviarTextoLibre(); };
        filaEntrada.Children.Add(_entrada);
        Grid.SetRow(filaEntrada, 3);

        raiz.Children.Add(titulo);
        raiz.Children.Add(scroll);
        raiz.Children.Add(_estado);
        raiz.Children.Add(filaEntrada);
        Content = raiz;

        Drop += (_, e) =>
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] archivos && archivos.Length > 0)
                EncolarDocumentos(archivos);
        };

        BurbujaAsistente(
            "Adjunta o arrastra uno o varios PDF/fotos/escaneos y los reviso uno por uno: tipo, remitente, " +
            "datos, anexos y a qué área correspondería. Tú confirmas antes de que se radique o se avise a nadie.");

        if (ClasificadorDocumento.ClaveGemini().Length == 0 && ClasificadorDocumento.ClaveOpenAi().Length == 0)
            _estado.Text = "⚠ Falta GEMINI_API_KEY u OPENAI_API_KEY en el entorno — no se podrá clasificar todavía.";
    }

    private void EnviarTextoLibre()
    {
        string texto = _entrada.Text.Trim();
        if (texto.Length == 0) return;
        BurbujaUsuario(texto);
        _entrada.Clear();
        BurbujaAsistente("Por ahora solo proceso documentos adjuntos — usa \"📎 Adjuntar documento\" o arrastra el archivo aquí.");
    }

    private void AdjuntarPorDialogo()
    {
        var dialogo = new OpenFileDialog
        {
            Title = "Adjuntar documento(s) a radicar",
            Filter = "Documentos|*.pdf;*.png;*.jpg;*.jpeg",
            Multiselect = true,
        };
        if (dialogo.ShowDialog(this) == true)
            EncolarDocumentos(dialogo.FileNames);
    }

    /// <summary>Uno o varios archivos a la vez (diálogo con Multiselect, o arrastrar varios de golpe)
    /// se procesan EN COLA, uno detrás de otro — en paralelo competirían por el mismo _procesando y
    /// por el límite de tasa del LLM, y las tarjetas de revisión saldrían en un orden que no coincide
    /// con el de los archivos.</summary>
    private readonly Queue<string> _cola = new();
    private bool _procesandoCola;

    private void EncolarDocumentos(IEnumerable<string> rutas)
    {
        foreach (var r in rutas) _cola.Enqueue(r);
        _ = ProcesarColaAsync();
    }

    private async Task ProcesarColaAsync()
    {
        if (_procesandoCola) return; // ya hay un ciclo consumiendo la cola; este documento se suma a ella
        _procesandoCola = true;
        _adjuntar.IsEnabled = false;
        try
        {
            while (_cola.Count > 0)
                await ProcesarDocumentoAsync(_cola.Dequeue());
        }
        finally
        {
            _procesandoCola = false;
            _adjuntar.IsEnabled = true;
        }
    }

    private async Task ProcesarDocumentoAsync(string ruta)
    {
        string mime = System.IO.Path.GetExtension(ruta).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => "",
        };
        if (mime.Length == 0)
        {
            BurbujaAsistente($"«{System.IO.Path.GetFileName(ruta)}» no es un PDF ni una imagen (PNG/JPG) — no lo puedo leer.");
            return;
        }

        BurbujaUsuario($"📎 {System.IO.Path.GetFileName(ruta)}");
        var analizando = BurbujaAsistente("Analizando el documento…");

        try
        {
            byte[] bytes = await File.ReadAllBytesAsync(ruta);
            var doc = await ClasificadorDocumento.ClasificarAsync(bytes, mime, _areas, default);
            _mensajes.Children.Remove(analizando);
            AgregarTarjetaRevision(ruta, doc);
        }
        catch (Exception e)
        {
            _mensajes.Children.Remove(analizando);
            LogBus.Log("radicacion", $"fallo clasificando {ruta}: {e.Message}");
            BurbujaAsistente($"No pude analizar «{System.IO.Path.GetFileName(ruta)}»: {e.Message}");
        }
    }

    private void AgregarTarjetaRevision(string archivoOriginal, DocumentoClasificado doc)
    {
        var tarjeta = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x30)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 4, 40, 10),
        };
        var pila = new StackPanel();

        void Campo(string etiqueta, string valor)
        {
            if (string.IsNullOrWhiteSpace(valor)) return;
            pila.Children.Add(new TextBlock
            {
                Text = $"{etiqueta}: {valor}",
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 2),
            });
        }

        pila.Children.Add(new TextBlock { Text = doc.Resumen, FontSize = 13, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        Campo("Tipo de documento", doc.TipoDocumento);
        Campo("Remitente", doc.Remitente);
        Campo("Cédula/NIT", doc.CedulaONit);
        Campo("Afiliado/IPS/Empleador", doc.AfiliadoIpsEmpleador);
        Campo("Fecha del documento", doc.FechaDocumento);
        Campo("Asunto", doc.Asunto);
        Campo("Datos de contacto", doc.DatosContacto);
        Campo("Número referido", doc.NumeroReferido);

        if (!doc.AnexosCompletos && doc.AnexosFaltantes.Count > 0)
            pila.Children.Add(new TextBlock
            {
                Text = $"⚠ Faltan anexos: {string.Join(", ", doc.AnexosFaltantes)}",
                FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0xA0, 0x30)),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0),
            });
        if (doc.Advertencias.Count > 0)
            pila.Children.Add(new TextBlock
            {
                Text = $"⚠ {string.Join(" · ", doc.Advertencias)}",
                FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0x60, 0x60)),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0),
            });

        pila.Children.Add(new TextBlock { Text = "Área responsable:", FontSize = 12, Margin = new Thickness(0, 10, 0, 2) });
        var combo = new ComboBox { ItemsSource = _areas, DisplayMemberPath = "Nombre", SelectedValuePath = "Clave", SelectedValue = doc.AreaSugerida, Height = 26, FontSize = 12 };
        pila.Children.Add(combo);

        var filaBotones = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var porCorreo = new Button { Content = "📧 Enviar por correo", Height = 28, FontSize = 12, Padding = new Thickness(8, 0, 8, 0) };
        var porWeb = new Button { Content = "🖥 Diligenciar en la web", Height = 28, FontSize = 12, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(8, 0, 8, 0) };
        var descartar = new Button { Content = "Descartar", Height = 28, FontSize = 12, Margin = new Thickness(8, 0, 0, 0) };
        filaBotones.Children.Add(porCorreo);
        filaBotones.Children.Add(porWeb);
        filaBotones.Children.Add(descartar);
        pila.Children.Add(filaBotones);

        // El número provisional se genera UNA vez para esta tarjeta, sin importar cuál de los dos
        // botones se pulse primero — si se prueban los dos (correo y web) para el mismo documento en
        // la demo, deben compartir radicado en vez de generar dos números para el mismo papel.
        Radicado? radicadoActual = null;
        Radicado ObtenerORegistrar()
        {
            if (radicadoActual != null) return radicadoActual;
            var area = _areas.FirstOrDefault(a => a.Clave == (string)combo.SelectedValue) ?? _areas.First();
            radicadoActual = new Radicado
            {
                Numero = RadicadorLocal.GenerarNumeroProvisional(),
                FechaHoraUtc = DateTime.UtcNow,
                ArchivoOriginal = archivoOriginal,
                Documento = doc,
                AreaClave = area.Clave,
                AreaNombre = area.Nombre,
                CorreoDestinoFicticio = area.CorreoFicticio,
            };
            RadicadorLocal.Registrar(radicadoActual);
            combo.IsEnabled = false; // el área queda fija en cuanto hay número: cambiarla después dejaría el registro apuntando a otra parte
            return radicadoActual;
        }

        descartar.Click += (_, __) =>
        {
            _mensajes.Children.Remove(tarjeta);
            BurbujaAsistente("Documento descartado — no se radicó ni se avisó a ningún área.");
        };

        porCorreo.Click += async (_, __) =>
        {
            porCorreo.IsEnabled = false;
            var radicado = ObtenerORegistrar();
            var area = _areas.First(a => a.Clave == radicado.AreaClave);
            var pendiente = BurbujaAsistente($"Enviando el correo de {radicado.Numero}…");
            var (enviado, motivo) = await EnviadorCorreo.EnviarAsync(area, radicado, default);
            radicado.CorreoEnviado = enviado;
            radicado.Estado = EstadoRadicado.Confirmado;
            RadicadorLocal.Registrar(radicado);
            _mensajes.Children.Remove(pendiente);
            BurbujaAsistente(enviado
                ? $"✓ Radicado {radicado.Numero} — correo enviado (destino real simulado: {area.CorreoFicticio})."
                : $"Radicado {radicado.Numero} registrado, pero el correo NO se pudo enviar: {motivo}");
            porCorreo.IsEnabled = true;
        };

        porWeb.Click += async (_, __) =>
        {
            porWeb.IsEnabled = false;
            var radicado = ObtenerORegistrar();
            var area = _areas.First(a => a.Clave == radicado.AreaClave);
            var pendiente = BurbujaAsistente($"Abriendo la web y diligenciando {radicado.Numero}…");
            bool ok = await RellenadorWeb.DiligenciarAsync(radicado, area, Owner);
            _mensajes.Children.Remove(pendiente);
            BurbujaAsistente(ok
                ? $"✓ Formulario diligenciado en la web para {radicado.Numero} (área: {area.Nombre})."
                : $"El formulario quedó incompleto para {radicado.Numero} — revisa el log (algún campo no se encontró en la página).");
            porWeb.IsEnabled = true;
        };

        tarjeta.Child = pila;
        _mensajes.Children.Add(tarjeta);
        DesplazarAlFinal();
    }

    private Border BurbujaAsistente(string texto) => Burbuja(texto, propia: false);
    private Border BurbujaUsuario(string texto) => Burbuja(texto, propia: true);

    private Border Burbuja(string texto, bool propia)
    {
        var burbuja = new Border
        {
            Background = new SolidColorBrush(propia ? Color.FromRgb(0x3A, 0x5F, 0x8F) : Color.FromRgb(0x2A, 0x2A, 0x30)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 7, 10, 7),
            Margin = propia ? new Thickness(40, 4, 0, 4) : new Thickness(0, 4, 40, 4),
            HorizontalAlignment = propia ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Child = new TextBlock { Text = texto, FontSize = 13, TextWrapping = TextWrapping.Wrap },
        };
        _mensajes.Children.Add(burbuja);
        DesplazarAlFinal();
        return burbuja;
    }

    private void DesplazarAlFinal() =>
        Dispatcher.BeginInvoke(() => (_mensajes.Parent as ScrollViewer)?.ScrollToEnd());
}
