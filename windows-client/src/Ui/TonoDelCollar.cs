using System.IO;
using System.Media;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Ui;

/// <summary>
/// LOS CUATRO SONIDOS DEL BOTÓN DEL COLLAR, sintetizados en memoria.
/// </summary>
/// <remarks>
/// SE GENERAN Y NO SE GUARDAN COMO ASSETS, y es a propósito: cuatro `.wav` en el repo son cuatro
/// binarios que nadie puede revisar en un diff, que hay que copiar al publicar, y que se pierden
/// en cuanto alguien toque el `.csproj`. Un seno de tres líneas se lee, se cambia de nota sin abrir
/// un editor de audio, y no puede faltar en tiempo de ejecución.
///
/// EL VOCABULARIO, que es lo que hay que entender antes de tocar las frecuencias:
///
///   · <b>subir</b> = abrirse = estás en vivo. La metáfora ya estaba fijada en la carita
///     («dos notas que SUBEN, escuchar = abrirse», <c>PlayChime</c>), y repetirla aquí es lo que
///     hace que la aplicación suene a una sola cosa;
///   · <b>bajar</b> = cerrarse = se acabó. El espejo exacto del anterior;
///   · <b>una sola nota corta</b> = pausa: mismo número de notas que un toque, menos energía;
///   · <b>dos notas graves</b> = no se pudo. Graves para que no se confundan con las de arriba ni
///     de lejos, que es cuando importa.
///
/// SUENA SIN BLOQUEAR. <c>SoundPlayer.Play</c> ya usa su propio hilo, y el que llama viene del
/// evento del collar: pararlo ahí retrasaría la propia acción que el sonido confirma.
/// </remarks>
public static class TonoDelCollar
{
    private const int Hz = 22050;

    /// <summary>Cada sonido, ya en formato WAV y listo para sonar. Se hacen una vez.</summary>
    private static readonly Dictionary<string, byte[]> Sonidos = new(StringComparer.Ordinal)
    {
        // En vivo: sol → do, subiendo. Cortas, porque confirman; no anuncian.
        [ReglaDelSonidoDelCollar.EnVivo] = Wav((784, 90), (0, 20), (1047, 130)),

        // Pausa: una nota sola, media y breve. Ni sube ni baja: se queda.
        [ReglaDelSonidoDelCollar.Pausado] = Wav((880, 85)),

        // Cierre: do → sol, bajando. La última más larga, para que se oiga terminada.
        [ReglaDelSonidoDelCollar.Cierre] = Wav((1047, 90), (0, 20), (784, 170)),

        // No se pudo: dos graves cortas y secas. No se parecen a nada de lo de arriba.
        [ReglaDelSonidoDelCollar.NoSePudo] = Wav((300, 70), (0, 45), (300, 70)),
    };

    private static readonly Dictionary<string, SoundPlayer> Reproductores = new(StringComparer.Ordinal);

    /// <summary>Suena el tono de ese nombre. Un nombre que no existe no suena y lo dice.</summary>
    public static void Sonar(string nombre)
    {
        if (!Sonidos.TryGetValue(nombre, out var wav))
        {
            LogBus.Log("collar", $"no hay ningún tono que se llame «{nombre}»");
            return;
        }

        SoundPlayer reproductor;
        lock (Reproductores)
        {
            if (!Reproductores.TryGetValue(nombre, out reproductor!))
            {
                reproductor = new SoundPlayer(new MemoryStream(wav));
                reproductor.Load();
                Reproductores[nombre] = reproductor;
            }
        }
        reproductor.Play();
    }

    /// <summary>
    /// Un WAV mono de 16 bits con los tramos que se le den: (frecuencia en Hz, duración en ms).
    /// Frecuencia cero es silencio, que es lo que separa dos notas y las hace contables.
    /// </summary>
    private static byte[] Wav(params (int Hercios, int Ms)[] tramos)
    {
        var muestras = new List<short>();
        foreach (var (hercios, ms) in tramos)
        {
            int n = Hz * ms / 1000;
            for (int i = 0; i < n; i++)
            {
                if (hercios == 0) { muestras.Add(0); continue; }

                // LA ENVOLVENTE NO ES ADORNO: un seno que empieza y acaba de golpe suena a «clic»
                // —el salto de amplitud es un impulso— y dos clics no se distinguen entre sí. Con
                // entrada y salida suaves, lo que se oye es la NOTA, que es lo que hay que
                // reconocer sin mirar.
                double t = (double)i / Hz;
                double borde = Math.Min(1.0, Math.Min(i, n - 1 - i) / (Hz * 0.008));
                muestras.Add((short)(Math.Sin(2 * Math.PI * hercios * t) * 8000 * borde));
            }
        }

        using var ms2 = new MemoryStream();
        using var w = new BinaryWriter(ms2);
        int datos = muestras.Count * 2;
        w.Write("RIFF"u8.ToArray());
        w.Write(36 + datos);
        w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray());
        w.Write(16);                 // tamaño del bloque fmt
        w.Write((short)1);           // PCM sin comprimir
        w.Write((short)1);           // mono
        w.Write(Hz);
        w.Write(Hz * 2);             // bytes por segundo
        w.Write((short)2);           // bytes por muestra
        w.Write((short)16);          // bits por muestra
        w.Write("data"u8.ToArray());
        w.Write(datos);
        foreach (short s in muestras) w.Write(s);
        w.Flush();
        return ms2.ToArray();
    }
}
