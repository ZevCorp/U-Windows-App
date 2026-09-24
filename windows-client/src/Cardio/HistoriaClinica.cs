namespace U.WindowsClient.Cardio;

/// <summary>Cómo se lee un documento soltado en la Nota (promesa 420).</summary>
public enum TipoDeDocumento { Foto, Pdf, NoAdmitido }

/// <summary>Lo soltado, repartido: lo que se lee como foto, lo que se lee como PDF, y lo que se nombra.</summary>
public sealed class Reparto
{
    public List<string> Fotos { get; } = new();
    public List<string> Pdfs { get; } = new();
    /// <summary>Un aviso por archivo no admitido, con su nombre y qué hacer.</summary>
    public List<string> Avisos { get; } = new();
}

/// <summary>
/// QUÉ SE ADMITE DE LA HISTORIA CLÍNICA SOLTADA EN LA NOTA. Promesa 420 (spec 051). Pura.
/// </summary>
/// <remarks>
/// Fotos tomadas a documentos y PDFs, que es lo que un médico tiene a mano. Lo demás —Word, Excel— se
/// nombra y se pide exportarlo, sin tumbar a los que sí se pueden leer: soltar diez documentos y que uno
/// raro los pare a todos sería peor que no admitirlo.
/// </remarks>
public static class HistoriaClinica
{
    private static readonly string[] DeFoto =
        { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".heic", ".heif" };

    /// <summary>
    /// Tope de un PDF. La Responses API admite archivos más grandes, pero 20 MB ya son cientos de páginas
    /// escaneadas: más que eso casi siempre es una exportación sin comprimir, y se pide partirla.
    /// </summary>
    public const long TopePdfBytes = 20L * 1024 * 1024;

    public static TipoDeDocumento TipoDe(string ruta)
    {
        string ext = System.IO.Path.GetExtension(ruta ?? "");
        if (string.Equals(ext, ".pdf", StringComparison.OrdinalIgnoreCase)) return TipoDeDocumento.Pdf;
        return Array.Exists(DeFoto, e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase))
            ? TipoDeDocumento.Foto
            : TipoDeDocumento.NoAdmitido;
    }

    public static string NoAdmitido(string nombre) =>
        $"«{nombre}» no se puede leer aquí: suéltalo como PDF o como foto.";

    public static Reparto Repartir(IEnumerable<string> rutas)
    {
        var r = new Reparto();
        foreach (string ruta in rutas ?? Array.Empty<string>())
        {
            switch (TipoDe(ruta))
            {
                case TipoDeDocumento.Foto: r.Fotos.Add(ruta); break;
                case TipoDeDocumento.Pdf: r.Pdfs.Add(ruta); break;
                default: r.Avisos.Add(NoAdmitido(System.IO.Path.GetFileName(ruta))); break;
            }
        }
        return r;
    }
}

/// <summary>Un párrafo transcrito, con id estable «Dn-pP-k»: documento, página, orden en la página.</summary>
public sealed class Parrafo
{
    public string Id { get; set; } = "";
    public string DocumentoId { get; set; } = "";
    /// <summary>El nombre del archivo, que es lo que el médico reconoce.</summary>
    public string Documento { get; set; } = "";
    public int Pagina { get; set; }
    /// <summary>El texto tal como lo transcribió el modelo. Es lo que se enseña al citar: no se reescribe.</summary>
    public string Texto { get; set; } = "";
}

public sealed class DocumentoDeLaHistoria
{
    public string Id { get; set; } = "";
    public string Nombre { get; set; } = "";
    public TipoDeDocumento Tipo { get; set; }

    /// <summary>La foto ya preparada (JPEG) o el PDF tal cual. Solo en memoria: no se escribe a disco.</summary>
    public byte[] Contenido { get; set; } = Array.Empty<byte>();

    public bool Leido { get; set; }
    public List<Parrafo> Parrafos { get; set; } = new();

    /// <summary>Por qué quedó sin leer. Vacío si se leyó.</summary>
    public string Motivo { get; set; } = "";
}

/// <summary>
/// La respuesta a «¿por qué vino a cardiología?» (promesa 422): una frase, y los párrafos que la
/// sostienen copiados del documento. Sin ninguna cita, el titular dice que los documentos no lo dicen.
/// </summary>
public sealed class MotivoDeCardiologia
{
    public const string SinMotivo = "Los documentos no dicen por qué vino a cardiología";

    public string Frase { get; set; } = "";
    public List<Parrafo> Citas { get; set; } = new();

    public bool Dicho => Frase.Length > 0 && Citas.Count > 0;
    public string Titular => Dicho ? Frase : SinMotivo;
}

/// <summary>
/// La historia clínica de ESTA consulta. Vive en memoria y muere con la ventana: son datos de un
/// paciente y solo sirven mientras se le atiende (spec 051).
/// </summary>
public sealed class HistoriaDeLaConsulta
{
    private int _siguiente;

    public List<DocumentoDeLaHistoria> Documentos { get; } = new();
    public MotivoDeCardiologia? Motivo { get; set; }

    /// <summary>Añade un documento. Su id es «D» y su orden de llegada, y no cambia aunque se añadan más.</summary>
    public DocumentoDeLaHistoria Agregar(string nombre, TipoDeDocumento tipo, byte[] contenido)
    {
        var d = new DocumentoDeLaHistoria
        {
            Id = "D" + (++_siguiente),
            Nombre = nombre ?? "",
            Tipo = tipo,
            Contenido = contenido ?? Array.Empty<byte>(),
        };
        Documentos.Add(d);
        return d;
    }

    public IEnumerable<Parrafo> Parrafos => Documentos.Where(d => d.Leido).SelectMany(d => d.Parrafos);
}
