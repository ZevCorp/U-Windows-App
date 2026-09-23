using System.IO;
using System.Globalization;
using System.Text.Json;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Cardio;

/// <summary>
/// Dónde vive la sesión de estudios entre dos arranques de Ü, y cuándo deja de vivir (promesa 354).
/// </summary>
/// <remarks>
/// ES EL INDEXEDDB DE LA PETICIÓN, EN WINDOWS: <c>%LOCALAPPDATA%\U\cardio\</c>, con la sesión en
/// <c>sesion.bin</c> y cada foto en <c>fotos\&lt;id&gt;.bin</c>. TODO CIFRADO CON DPAPI, lo mismo que
/// protege el token de la cuenta (<see cref="Cuenta.Dpapi"/>): son datos de salud (Ley 1581) y el
/// disco de un PC de consultorio lo mira más de una persona. Lo único en claro es <c>primera.txt</c>,
/// una fecha, para poder purgar cada 10 minutos sin descifrar treinta fotos.
///
/// Las fotos van en archivos aparte para no reescribir 10 MB cada vez que se pregunta algo en el chat:
/// una foto se escribe una vez, y la sesión, que es texto, cada vez que cambia.
///
/// CADUCA A LAS <see cref="Duracion"/> DESDE LA PRIMERA FOTO, y se comprueba en TRES sitios, porque
/// cualquiera de ellos solo dejaría huecos: al cargar (Ü se reinició), al guardar (el panel escribiría
/// una sesión que ya expiró) y cada 10 minutos (<see cref="VigiaCardio"/>, con el panel cerrado).
/// </remarks>
public sealed class AlmacenCardio
{
    /// <summary>
    /// Cuánto viven las fotos. UNA constante, como pidió el dueño: cambiarla es cambiar esta línea.
    /// </summary>
    public static readonly TimeSpan DuracionPorDefecto = TimeSpan.FromHours(24);

    /// <summary>
    /// La duración en uso. <c>U_CARDIO_CADUCIDAD_MIN</c> la acorta sin recompilar, para comprobar a mano
    /// que la sesión se borra sola sin esperar un día (<c>setx U_CARDIO_CADUCIDAD_MIN 1</c>).
    /// </summary>
    public static TimeSpan Duracion
    {
        get
        {
            string? v = Environment.GetEnvironmentVariable("U_CARDIO_CADUCIDAD_MIN");
            return int.TryParse(v?.Trim(), out int min) && min > 0 ? TimeSpan.FromMinutes(min) : DuracionPorDefecto;
        }
    }

    private readonly string _carpeta;
    private readonly Func<DateTime> _reloj;
    private readonly Func<byte[], byte[]> _proteger;
    private readonly Func<byte[], byte[]> _revelar;
    private readonly object _candado = new();

    /// <summary>El de la app: DPAPI y el reloj del sistema.</summary>
    public AlmacenCardio(string carpeta, Func<DateTime> relojUtc)
        : this(carpeta, relojUtc, Cuenta.Dpapi.Proteger, Cuenta.Dpapi.Revelar) { }

    public AlmacenCardio(string carpeta, Func<DateTime> relojUtc, Func<byte[], byte[]> proteger, Func<byte[], byte[]> revelar)
    {
        _carpeta = carpeta;
        _reloj = relojUtc;
        _proteger = proteger;
        _revelar = revelar;
    }

    private static AlmacenCardio? _deLaApp;

    /// <summary>
    /// UNO para toda la app: el panel y el vigía comparten el candado, y así una purga no se cruza con
    /// un guardado a medias.
    /// </summary>
    public static AlmacenCardio DeLaApp() =>
        _deLaApp ??= new AlmacenCardio(Path.Combine(U.Graph.UserPaths.Local, "U", "cardio"), () => DateTime.UtcNow);

    public DateTime CaducaUtc(SesionCardio s) => s.PrimeraFotoUtc + Duracion;

    public bool Caducada(SesionCardio s) => s.PrimeraFotoUtc != default && _reloj() >= CaducaUtc(s);

    private string Sesion => Path.Combine(_carpeta, "sesion.bin");
    private string Primera => Path.Combine(_carpeta, "primera.txt");
    private string Fotos => Path.Combine(_carpeta, "fotos");

    /// <summary>
    /// Lo que se va a escribir, sacado de la sesión EN EL HILO QUE LA TOCA. Así escribir puede ir en
    /// segundo plano sin leer una lista que la interfaz está cambiando.
    /// </summary>
    public sealed class Paquete
    {
        internal byte[] Metadatos = Array.Empty<byte>();
        internal DateTime PrimeraFotoUtc;
        internal List<(string Id, byte[] Jpeg)> Fotos = new();
        internal bool Vacia;
    }

    public static Paquete Empaquetar(SesionCardio s) => new()
    {
        Metadatos = JsonSerializer.SerializeToUtf8Bytes(s),
        PrimeraFotoUtc = s.PrimeraFotoUtc,
        Fotos = s.Fotos.Select(f => (f.Id, f.Jpeg)).ToList(),
        Vacia = s.Fotos.Count == 0 && s.Chat.Count == 0 && string.IsNullOrEmpty(s.Resumen),
    };

    public void Guardar(SesionCardio s) => Escribir(Empaquetar(s));

    public void Escribir(Paquete p)
    {
        lock (_candado)
        {
            // Vacía o ya caducada: no se escribe, se borra. Un guardado que llega tarde —el panel
            // guardando justo después de la purga— resucitaría una sesión expirada.
            if (p.Vacia || (p.PrimeraFotoUtc != default && _reloj() >= p.PrimeraFotoUtc + Duracion))
            {
                BorrarSinCandado(p.Vacia ? "la sesión quedó vacía" : "caducada al guardar");
                return;
            }

            Directory.CreateDirectory(Fotos);
            var vivas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (id, jpeg) in p.Fotos)
            {
                string archivo = Path.Combine(Fotos, Seguro(id) + ".bin");
                vivas.Add(archivo);
                // Una foto no cambia: si ya está escrita, no se vuelve a cifrar ni a escribir.
                if (!File.Exists(archivo)) EscribirAtomico(archivo, _proteger(jpeg));
            }
            foreach (string sobra in Directory.GetFiles(Fotos).Where(f => !vivas.Contains(f)))
                File.Delete(sobra);

            File.WriteAllText(Primera, p.PrimeraFotoUtc.ToString("o", CultureInfo.InvariantCulture));
            EscribirAtomico(Sesion, _proteger(p.Metadatos));
        }
    }

    /// <summary>La sesión guardada, o null si no hay, no se puede leer o ya caducó (y entonces se borra).</summary>
    public SesionCardio? Cargar()
    {
        lock (_candado)
        {
            if (!File.Exists(Sesion))
            {
                // Fotos sin sesión que las nombre son restos de un guardado roto: no tienen dueño.
                if (Directory.Exists(_carpeta)) BorrarSinCandado("había restos sin sesión");
                return null;
            }

            SesionCardio? s;
            try
            {
                s = JsonSerializer.Deserialize<SesionCardio>(_revelar(File.ReadAllBytes(Sesion)));
            }
            catch (Exception e)
            {
                // Otra cuenta de Windows, otro equipo, o un archivo roto: no se puede leer y no se va a
                // poder. Se dice el tipo —no el contenido— y se borra, que es lo que pasaría al caducar.
                BorrarSinCandado($"no se pudo leer ({e.GetType().Name}: {e.Message})");
                return null;
            }
            if (s == null) { BorrarSinCandado("la sesión estaba vacía"); return null; }
            if (Caducada(s)) { BorrarSinCandado("caducada al cargar"); return null; }

            foreach (var foto in s.Fotos.ToList())
            {
                string archivo = Path.Combine(Fotos, Seguro(foto.Id) + ".bin");
                try { foto.Jpeg = _revelar(File.ReadAllBytes(archivo)); }
                catch (Exception e)
                {
                    LogBus.Log("cardio", $"una foto guardada no se pudo leer y se quita ({e.GetType().Name})");
                    s.Fotos.Remove(foto);
                }
            }
            return s;
        }
    }

    /// <summary>Si lo guardado ya caducó, lo borra. Devuelve si borró algo.</summary>
    public bool PurgarSiCaduco()
    {
        lock (_candado)
        {
            if (!Directory.Exists(_carpeta)) return false;
            DateTime primera = default;
            bool leida = File.Exists(Primera)
                && DateTime.TryParse(File.ReadAllText(Primera), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out primera);
            // Sin fecha legible no hay forma de saber cuánto le queda: se trata como caducada. Con datos
            // de salud, la duda se resuelve borrando.
            if (!leida || (primera != default && _reloj() >= primera + Duracion))
            {
                BorrarSinCandado(leida ? "caducada" : "sin fecha de la primera foto");
                return true;
            }
            return false;
        }
    }

    public void Borrar(string porque = "lo pidió el médico")
    {
        lock (_candado) BorrarSinCandado(porque);
    }

    private void BorrarSinCandado(string porque)
    {
        if (!Directory.Exists(_carpeta)) return;
        for (int intento = 1; ; intento++)
        {
            try
            {
                Directory.Delete(_carpeta, recursive: true);
                LogBus.Log("cardio", $"sesión de estudios borrada: {porque}");
                return;
            }
            catch (IOException) when (intento < 3)
            {
                // Un antivirus o el indexador pueden tener un archivo abierto un instante.
                Thread.Sleep(150);
            }
        }
    }

    private static void EscribirAtomico(string archivo, byte[] datos)
    {
        string temporal = archivo + ".tmp";
        File.WriteAllBytes(temporal, datos);
        File.Move(temporal, archivo, overwrite: true);
    }

    /// <summary>Los ids los genera el panel, pero un id es un nombre de archivo y no se le deja subir de carpeta.</summary>
    private static string Seguro(string id) =>
        new string(id.Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_').ToArray()) is { Length: > 0 } s ? s : "sin-id";
}
