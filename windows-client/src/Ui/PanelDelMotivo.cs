using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using U.WindowsClient.Cardio;
using U.WindowsClient.Diagnostics;
using ParrafoDeLaHistoria = U.WindowsClient.Cardio.Parrafo;

namespace U.WindowsClient.Ui;

/// <summary>
/// «¿POR QUÉ VINO A CARDIOLOGÍA?», arriba de la Nota (spec 051). El médico suelta la historia clínica
/// —fotos de documentos y PDFs— y aquí sale la respuesta en una frase, con los párrafos que la sostienen
/// copiados del documento y con su cita.
/// </summary>
/// <remarks>
/// LO QUE SE PROMETE VIVE EN <see cref="LectorDeLaHistoria"/> y lo juzga el contrato (420-423); esto es
/// solo cómo se ve. Se construye en código, como el resto de la ventana, y con las piezas de
/// <see cref="Estudio"/> para que no parezca pegado de otra aplicación.
///
/// La historia vive en memoria y muere con la ventana: son datos de un paciente y solo sirven mientras se
/// le atiende. Nada de esto se escribe a disco.
/// </remarks>
public sealed class PanelDelMotivo : IDisposable
{
    private readonly HistoriaDeLaConsulta _historia = new();
    private readonly LectorDeLaHistoria _lector = LectorDeLaHistoria.DeLaApp();
    private readonly CancellationTokenSource _cts = new();
    private readonly List<string> _avisos = new();

    private readonly Border _tarjeta;
    private readonly StackPanel _contenido = new();
    private bool _leyendo;
    private bool _otraVez;
    private bool _arrastrando;
    private string _progreso = "";
    private string _error = "";

    public PanelDelMotivo()
    {
        _tarjeta = Estudio.Tarjeta(18);
        _tarjeta.Padding = new Thickness(20, 16, 20, 16);
        _tarjeta.Margin = new Thickness(2, 0, 2, 14);
        _tarjeta.Child = _contenido;
        Vista = Estudio.Elevar(_tarjeta);
        Pintar();
    }

    /// <summary>Lo que se mete en la Nota. Oculto hasta que llega el primer documento o un arrastre.</summary>
    public FrameworkElement Vista { get; }

    // ── soltar ────────────────────────────────────────────────────────────────────────────────────

    private static string[] Rutas(IDataObject datos) =>
        datos.GetDataPresent(DataFormats.FileDrop) ? (datos.GetData(DataFormats.FileDrop) as string[]) ?? Array.Empty<string>() : Array.Empty<string>();

    public void AlPasarPorEncima(DragEventArgs e)
    {
        bool sirve = Rutas(e.Data).Any(r => HistoriaClinica.TipoDe(r) != TipoDeDocumento.NoAdmitido);
        e.Effects = sirve ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        if (_arrastrando != sirve) { _arrastrando = sirve; Pintar(); }
    }

    public void AlSalir()
    {
        if (!_arrastrando) return;
        _arrastrando = false;
        Pintar();
    }

    public async Task SoltarAsync(IDataObject datos)
    {
        _arrastrando = false;
        var reparto = HistoriaClinica.Repartir(Rutas(datos));
        _avisos.AddRange(reparto.Avisos);
        Pintar();

        foreach (string ruta in reparto.Fotos)
        {
            string nombre = Path.GetFileName(ruta);
            // La preparación (1600 px, JPEG sobre blanco) es la de la spec 046, fuera del hilo de la
            // interfaz: una foto de 12 MP tarda lo bastante como para congelar la ventana.
            var foto = await Task.Run(() =>
            {
                try { return PreparadorDeFotos.Preparar(nombre, File.ReadAllBytes(ruta)); }
                catch (Exception e) { return new FotoPreparada { Error = $"No se pudo abrir {nombre}: {e.Message}" }; }
            });
            if (foto.Ok) _historia.Agregar(nombre, TipoDeDocumento.Foto, foto.Jpeg);
            else _avisos.Add(string.IsNullOrWhiteSpace(foto.Error) ? PreparadorDeFotos.NoSePudoLeer(nombre) : foto.Error);
        }
        foreach (string ruta in reparto.Pdfs)
        {
            string nombre = Path.GetFileName(ruta);
            try
            {
                long largo = new FileInfo(ruta).Length;
                if (largo > HistoriaClinica.TopePdfBytes)
                {
                    _avisos.Add($"«{nombre}» pesa {largo / (1024 * 1024)} MB y el tope es {HistoriaClinica.TopePdfBytes / (1024 * 1024)} MB: pártelo en varios PDF.");
                    continue;
                }
                _historia.Agregar(nombre, TipoDeDocumento.Pdf, await File.ReadAllBytesAsync(ruta));
            }
            catch (Exception e) { _avisos.Add($"No se pudo abrir {nombre}: {e.Message}"); }
        }
        Pintar();
        await LeerAsync();
    }

    /// <summary>
    /// Lee lo que falte. Si llegan documentos mientras se lee, se vuelve a pasar al terminar: solo una
    /// lectura a la vez, porque las dos escribirían en la misma historia.
    /// </summary>
    private async Task LeerAsync()
    {
        if (_leyendo) { _otraVez = true; return; }
        _leyendo = true;
        _error = "";
        try
        {
            do
            {
                _otraVez = false;
                if (!_historia.Documentos.Any(d => !d.Leido)) break;
                await _lector.LeerAsync(_historia, p => { _progreso = p; Pintar(); }, _cts.Token);
            }
            while (_otraVez);
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested) { }
        catch (Exception e)
        {
            _error = e.Message;
            LogBus.Log("historia", "la lectura se quedó a medias: " + e.GetType().Name);
        }
        finally
        {
            _leyendo = false;
            _progreso = "";
            Pintar();
        }
    }

    // ── pintar ────────────────────────────────────────────────────────────────────────────────────

    private void Pintar()
    {
        bool hayAlgo = _historia.Documentos.Count > 0 || _avisos.Count > 0;
        Vista.Visibility = hayAlgo || _arrastrando ? Visibility.Visible : Visibility.Collapsed;
        _tarjeta.BorderBrush = _arrastrando ? Estudio.Acento : Estudio.Borde;
        _tarjeta.Background = _arrastrando ? Estudio.AcentoSuave : Estudio.Superficie;
        _contenido.Children.Clear();

        var rotulo = Estudio.Rotulo("¿Por qué vino a cardiología?");
        rotulo.HorizontalAlignment = HorizontalAlignment.Center;
        _contenido.Children.Add(rotulo);

        var motivo = _historia.Motivo;
        string titular = _arrastrando && _historia.Documentos.Count == 0
            ? "Suelta aquí la historia clínica: fotos o PDF."
            : motivo != null && !(_leyendo && !motivo.Dicho) ? motivo.Titular
            : _leyendo ? (_progreso.Length > 0 ? _progreso : "Leyendo…")
            : _historia.Documentos.Count == 0 ? "Suelta aquí la historia clínica: fotos o PDF." : "Todavía no se ha leído.";
        _contenido.Children.Add(new TextBlock
        {
            Text = titular,
            Foreground = motivo?.Dicho == true ? Estudio.Tinta : Estudio.TintaMedia,
            FontSize = 16.5,
            FontWeight = FontWeights.SemiBold,
            LineHeight = 24,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 4),
        });

        // Con un motivo ya a la vista y documentos nuevos entrando, el progreso va debajo, sin tapar el titular.
        if (_leyendo && motivo?.Dicho == true && _progreso.Length > 0)
            _contenido.Children.Add(Linea(_progreso, Estudio.TintaTenue, centrada: true));

        if (motivo?.Dicho == true)
            foreach (var cita in motivo.Citas) _contenido.Children.Add(Cita(cita));

        if (_historia.Documentos.Count > 0) _contenido.Children.Add(Documentos());

        foreach (string aviso in _avisos) _contenido.Children.Add(Linea(aviso, Estudio.Alerta));
        // Por qué no se leyó, A LA VISTA: este cliente no enseña texto al pasar el ratón (promesa del panel de estudios).
        if (!_leyendo)
            foreach (var d in _historia.Documentos.Where(d => !d.Leido && d.Motivo.Length > 0))
                _contenido.Children.Add(Linea($"«{d.Nombre}» no se leyó: {d.Motivo}.", Estudio.Espera));
        if (_error.Length > 0) _contenido.Children.Add(Linea(_error, Estudio.Alerta));

        if (!_leyendo && _historia.Documentos.Any(d => !d.Leido))
        {
            var reintentar = new Button
            {
                Content = "Volver a leer lo que faltó",
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 10, 0, 0),
                Padding = new Thickness(14, 5, 14, 5),
                Background = Estudio.SuperficieSuave,
                Foreground = Estudio.Tinta,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Template = Estudio.Pastilla(14),
            };
            reintentar.Click += async (_, __) => await LeerAsync();
            _contenido.Children.Add(reintentar);
        }
    }

    /// <summary>
    /// Un párrafo citado: de qué documento y página sale, y el texto TAL CUAL se transcribió. Se enseña
    /// entero porque es lo que el médico va a leer para fiarse de la frase de arriba.
    /// </summary>
    private static UIElement Cita(ParrafoDeLaHistoria p)
    {
        var pila = new StackPanel();
        pila.Children.Add(new TextBlock
        {
            Text = p.Pagina > 1 || p.Documento.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
                ? $"{p.Documento} · pág. {p.Pagina}"
                : p.Documento,
            Foreground = Estudio.TintaTenue,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 3),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        pila.Children.Add(Estudio.Parrafo(p.Texto, 13));
        return new Border
        {
            BorderBrush = Estudio.Acento,
            BorderThickness = new Thickness(3, 0, 0, 0),
            Background = Estudio.SuperficieDeLaBarra,
            CornerRadius = new CornerRadius(0, 8, 8, 0),
            Padding = new Thickness(12, 8, 12, 9),
            Margin = new Thickness(0, 10, 0, 0),
            Child = pila,
        };
    }

    /// <summary>Los documentos soltados, en una línea: cuáles se leyeron y cuáles no, sin abrir nada.</summary>
    private UIElement Documentos()
    {
        var fila = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) };
        foreach (var d in _historia.Documentos)
        {
            string estado = d.Leido ? "" : _leyendo ? " · leyendo" : " · sin leer";
            var ficha = new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = d.Leido ? Estudio.SuperficieSuave : Estudio.EsperaSuave,
                Padding = new Thickness(9, 3, 9, 3),
                Margin = new Thickness(3),
                Child = new TextBlock
                {
                    Text = d.Nombre + estado,
                    Foreground = d.Leido ? Estudio.TintaMedia : Estudio.Espera,
                    FontSize = 11,
                    MaxWidth = 220,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                },
            };
            fila.Children.Add(ficha);
        }
        return fila;
    }

    private static TextBlock Linea(string texto, Brush color, bool centrada = false) => new()
    {
        Text = texto,
        Foreground = color,
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
        TextAlignment = centrada ? TextAlignment.Center : TextAlignment.Left,
        Margin = new Thickness(0, 8, 0, 0),
    };

    public void Dispose()
    {
        try { _cts.Cancel(); } catch (ObjectDisposedException) { }
        _cts.Dispose();
    }
}
