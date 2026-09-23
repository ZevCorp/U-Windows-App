using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace U.WindowsClient.Cardio;

public sealed class FotoPreparada
{
    public bool Ok { get; init; }
    public byte[] Jpeg { get; init; } = Array.Empty<byte>();
    public int Ancho { get; init; }
    public int Alto { get; init; }
    /// <summary>Qué decirle al médico si no se pudo. Vacío si salió bien.</summary>
    public string Error { get; init; } = "";
}

/// <summary>
/// Deja una foto lista para el modelo: lado mayor 1600 px, JPEG al 82 % y lo transparente en BLANCO
/// (promesa 349). Es el canvas de la petición original, hecho con WIC, que ya viene con Windows.
/// </summary>
/// <remarks>
/// POR QUÉ 1600: es donde un ECG fotografiado sigue leyéndose —las cifras de un informe y la cuadrícula
/// del trazado— y tres fotos caben en ~1,5 MB de base64. Lo que se GUARDA es esta versión: la original
/// no se copia a ningún sitio.
///
/// POR QUÉ EL BLANCO: una captura de pantalla con transparencia, pasada a JPEG sin fondo, sale NEGRA
/// donde era transparente, y un informe con texto negro sobre transparente se vuelve ilegible.
///
/// SE PUEDE LLAMAR DESDE CUALQUIER HILO: todo lo que crea son imágenes de WIC, y la que entra desde el
/// portapapeles llega congelada.
/// </remarks>
public static class PreparadorDeFotos
{
    public const int LadoMayor = 1600;
    public const int CalidadJpeg = 82;

    /// <summary>A qué tamaño se queda. NUNCA se agranda: inventar píxeles no añade nada que leer.</summary>
    public static (int Ancho, int Alto) Medida(int ancho, int alto)
    {
        if (ancho <= 0 || alto <= 0) return (Math.Max(1, ancho), Math.Max(1, alto));
        int mayor = Math.Max(ancho, alto);
        if (mayor <= LadoMayor) return (ancho, alto);
        double escala = (double)LadoMayor / mayor;
        return (Math.Max(1, (int)Math.Round(ancho * escala)), Math.Max(1, (int)Math.Round(alto * escala)));
    }

    public static string NoSePudoLeer(string nombre) => $"No se pudo leer {nombre}. Expórtala como JPG";

    /// <summary>Desde los bytes de un archivo. Lo que WIC no sepa leer (HEIC sin su extensión) se nombra.</summary>
    public static FotoPreparada Preparar(string nombre, byte[] original)
    {
        try
        {
            // Primero solo la cabecera: con eso basta para saber el tamaño y la orientación, y así una foto
            // de 48 MP se decodifica YA reducida en vez de ocupar 200 MB para tirarlos después.
            var decodificador = BitmapDecoder.Create(new MemoryStream(original),
                BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
            var cuadro = decodificador.Frames[0];
            int giro = Giro(cuadro.Metadata as BitmapMetadata);
            var (ancho, _) = Medida(cuadro.PixelWidth, cuadro.PixelHeight);

            var imagen = new BitmapImage();
            imagen.BeginInit();
            imagen.StreamSource = new MemoryStream(original);
            imagen.CacheOption = BitmapCacheOption.OnLoad;
            imagen.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            if (ancho < cuadro.PixelWidth) imagen.DecodePixelWidth = ancho;
            imagen.EndInit();
            imagen.Freeze();

            BitmapSource fuente = imagen;
            if (giro != 0)
            {
                var girada = new TransformedBitmap(imagen, new RotateTransform(giro));
                girada.Freeze();
                fuente = girada;
            }
            return Preparar(nombre, fuente);
        }
        // TODO LO QUE FALLE AL LEER UNA FOTO se queda en ESA foto: la petición es explícita en que un
        // HEIC no rompa el flujo, y la lista de lo que WIC puede lanzar no está documentada entera.
        // No es un catch mudo: el motivo va al log, y la foto se nombra en el panel.
        catch (Exception e)
        {
            Diagnostics.LogBus.Log("cardio", $"no se pudo leer una foto ({Path.GetExtension(nombre)}): {e.GetType().Name}: {e.Message}");
            return new FotoPreparada { Ok = false, Error = NoSePudoLeer(nombre) };
        }
    }

    /// <summary>Desde una imagen ya decodificada (el portapapeles).</summary>
    public static FotoPreparada Preparar(string nombre, BitmapSource fuente)
    {
        try
        {
            var (ancho, alto) = Medida(fuente.PixelWidth, fuente.PixelHeight);
            BitmapSource escalada = fuente;
            if (ancho != fuente.PixelWidth || alto != fuente.PixelHeight)
            {
                escalada = new TransformedBitmap(fuente,
                    new ScaleTransform((double)ancho / fuente.PixelWidth, (double)alto / fuente.PixelHeight));
            }

            // SIN PREMULTIPLICAR para poder mezclar con el blanco a mano: color·α + blanco·(1−α).
            var bgra = new FormatConvertedBitmap(escalada, PixelFormats.Bgra32, null, 0);
            int w = bgra.PixelWidth, h = bgra.PixelHeight;
            var px = new byte[w * h * 4];
            bgra.CopyPixels(px, w * 4, 0);
            var bgr = new byte[w * h * 3];
            for (int i = 0, j = 0; i < px.Length; i += 4, j += 3)
            {
                int a = px[i + 3], blanco = 255 * (255 - a);
                bgr[j] = (byte)((px[i] * a + blanco + 127) / 255);
                bgr[j + 1] = (byte)((px[i + 1] * a + blanco + 127) / 255);
                bgr[j + 2] = (byte)((px[i + 2] * a + blanco + 127) / 255);
            }
            var opaca = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr24, null, bgr, w * 3);

            var jpeg = new JpegBitmapEncoder { QualityLevel = CalidadJpeg };
            jpeg.Frames.Add(BitmapFrame.Create(opaca));
            using var salida = new MemoryStream();
            jpeg.Save(salida);
            return new FotoPreparada { Ok = true, Jpeg = salida.ToArray(), Ancho = w, Alto = h };
        }
        catch (Exception e)
        {
            Diagnostics.LogBus.Log("cardio", $"no se pudo preparar una foto: {e.GetType().Name}: {e.Message}");
            return new FotoPreparada { Ok = false, Error = NoSePudoLeer(nombre) };
        }
    }

    /// <summary>Una miniatura para la grilla, desde el JPEG ya preparado.</summary>
    public static BitmapSource Miniatura(byte[] jpeg, int ancho = 200)
    {
        var imagen = new BitmapImage();
        imagen.BeginInit();
        imagen.StreamSource = new MemoryStream(jpeg);
        imagen.CacheOption = BitmapCacheOption.OnLoad;
        imagen.DecodePixelWidth = ancho;
        imagen.EndInit();
        imagen.Freeze();
        return imagen;
    }

    /// <summary>
    /// Cuánto girar según la orientación EXIF. Las fotos del móvil vienen «de lado» con una etiqueta
    /// que dice cómo ponerlas derechas, y WIC no la aplica solo: un ECG girado 90° se lee mucho peor.
    /// Los espejos (2, 4, 5, 7) no los produce una cámara de móvil y no se tratan.
    /// </summary>
    private static int Giro(BitmapMetadata? metadatos)
    {
        try
        {
            object? valor = metadatos?.GetQuery("System.Photo.Orientation");
            return Convert.ToInt32(valor) switch { 3 => 180, 6 => 90, 8 => 270, _ => 0 };
        }
        catch (Exception) { return 0; }   // sin metadatos legibles se deja como viene: es lo que haría el visor
    }
}
