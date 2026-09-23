using System.Text.Json.Serialization;

namespace U.WindowsClient.Cardio;

/// <summary>
/// Lo que se sabe de una foto después de pasarla por el modelo. Tres estados y no dos (promesa 346):
/// una foto de la que no volvió nada NO es «omitida» —eso afirmaría que se miró y no era de
/// cardiología— sino «sin leer», y el siguiente «Generar» la vuelve a pedir.
/// </summary>
public enum EstadoDeFoto { SinLeer, Cardiologia, Omitida }

public sealed class ValorCardio
{
    public string Nombre { get; set; } = "";
    public string Valor { get; set; } = "";
    public string Unidad { get; set; } = "";
}

/// <summary>
/// La lectura de una foto. De una <see cref="EstadoDeFoto.Omitida"/> solo se rellena <see cref="Motivo"/>:
/// el resto lo vacía <see cref="LecturaCardio.Emparejar"/> aunque el modelo lo devuelva (promesa 347).
/// </summary>
public sealed class ResultadoFoto
{
    public string Id { get; set; } = "";
    public EstadoDeFoto Estado { get; set; }
    public string Tipo { get; set; } = "";
    public string Fecha { get; set; } = "";
    public List<string> Hallazgos { get; set; } = new();
    public List<ValorCardio> Valores { get; set; } = new();
    public List<string> Alertas { get; set; } = new();
    /// <summary>«alta», «media», «baja», o vacío si el modelo no lo dijo. No se inventa.</summary>
    public string Confianza { get; set; } = "";
    /// <summary>Por qué se omitió, o por qué quedó sin leer. Solo para la interfaz: no viaja al modelo.</summary>
    public string Motivo { get; set; } = "";
}

public sealed class FotoCardio
{
    public string Id { get; set; } = "";
    public string Nombre { get; set; } = "";

    /// <summary>
    /// La foto YA PREPARADA (1600 px, JPEG): la original no se guarda en ningún sitio. Fuera del JSON
    /// de la sesión porque cada foto se guarda cifrada en su propio archivo (ver <see cref="AlmacenCardio"/>).
    /// </summary>
    [JsonIgnore]
    public byte[] Jpeg { get; set; } = Array.Empty<byte>();

    public ResultadoFoto? Resultado { get; set; }
}

public sealed class VueltaDeChat
{
    public string Pregunta { get; set; } = "";
    public string Respuesta { get; set; } = "";
}

/// <summary>
/// Una sesión de estudios: las fotos, lo leído, el resumen y el chat. Vive en memoria y en disco
/// cifrado, y caduca entera a las 24 h desde <see cref="PrimeraFotoUtc"/> (promesa 350).
/// </summary>
public sealed class SesionCardio
{
    /// <summary>
    /// Cuándo entró la PRIMERA foto. Cargar más fotos no lo mueve: si cada foto nueva alargara la
    /// vida de la sesión, una sesión usada a diario no caducaría nunca.
    /// </summary>
    public DateTime PrimeraFotoUtc { get; set; }

    public List<FotoCardio> Fotos { get; set; } = new();
    public string Resumen { get; set; } = "";

    /// <summary>Las fotos que había cuando se hizo el resumen. Si cambian, el resumen ya no las describe.</summary>
    public List<string> FotosDelResumen { get; set; } = new();

    public List<VueltaDeChat> Chat { get; set; } = new();

    public void Agregar(FotoCardio foto, DateTime ahoraUtc)
    {
        if (PrimeraFotoUtc == default) PrimeraFotoUtc = ahoraUtc;
        Fotos.Add(foto);
    }

    public bool Quitar(string id) => Fotos.RemoveAll(f => f.Id == id) > 0;
}
