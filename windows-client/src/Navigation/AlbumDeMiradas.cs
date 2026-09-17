using System.IO;
using System.Text.Json;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Navigation;

/// <summary>
/// EL ÁLBUM DE MIRADAS: lo que Ü vio, guardado en casa con su ficha. Promesa 255 (spec 027).
/// </summary>
/// <remarks>
/// POR QUÉ EXISTE, dicho por el dueño el 2026-09-16: «que queden alojadas en local, en una memoria del
/// asistente, para que pueda recordar en cualquier momento acciones pasadas». La copia que viaja a OpenAI
/// dura lo que dura la conversación y se retira al cerrarla (promesa 250); la que se queda es ésta.
///
/// Y NO ES LO MISMO QUE VOLVER A MIRAR: la pantalla de ayer no se puede capturar hoy. Una foto de ahora se
/// consigue cuando se quiera; la de la ubicación por la que se pasó hace dos horas, o está guardada o no
/// existe. Eso es lo único que este álbum añade, y es justo lo que no se puede resolver de otra forma.
///
/// JPEG Y NO PNG, y se comprueba en la puerta: las 294 fotos de recuerdos que había en PNG pesaban 252 KB de
/// media, el doble que en JPEG, y para mirar no aporta nada. Si el álbum aceptara cualquier cosa, «se guarda
/// en JPEG» sería una intención y no un hecho — así que lo que no empieza por FF D8 FF no entra.
///
/// LA PODA ES EXPLÍCITA, no un efecto de guardar. Guardar y podar en el mismo gesto hace imposible saber
/// cuánto ocupa el álbum de verdad, y convierte cada foto nueva en un borrado sorpresa de otra. Se poda
/// cuando alguien lo pide —al arrancar, o cada tanto—, por edad primero y por tamaño después, y siempre lo
/// más viejo primero. Los topes los eligió el dueño: siete días o dos gigas, lo que llegue antes.
/// </remarks>
public sealed class AlbumDeMiradas
{
    /// <summary>Una mirada guardada: dónde está, cuándo fue, en qué ubicación y qué estaba pasando.</summary>
    public sealed record Ficha(string Archivo, long Cuando, string Ubicacion, string QuePasaba, long Bytes);

    /// <summary>Siete días, en milisegundos. El tope de edad que eligió el dueño.</summary>
    public const long SieteDiasMs = 7L * 24 * 60 * 60 * 1000;

    /// <summary>Dos gigas. El tope de tamaño que eligió el dueño.</summary>
    public const long DosGigas = 2L * 1024 * 1024 * 1024;

    private readonly string _carpeta;
    private readonly Func<long> _ahora;
    private readonly long _topeBytes;
    private readonly long _topeMs;
    private readonly List<Ficha> _fichas = new();
    private readonly object _candado = new();

    public AlbumDeMiradas(string carpeta, Func<long> ahora, long topeBytes, long topeMs)
    {
        _carpeta = carpeta;
        _ahora = ahora;
        _topeBytes = topeBytes;
        _topeMs = topeMs;
        Cargar();
    }

    private static AlbumDeMiradas? _suyo;

    /// <summary>
    /// EL ÁLBUM DE ESTE ORDENADOR. Uno solo, y se poda la primera vez que alguien lo pide en cada arranque.
    /// </summary>
    /// <remarks>
    /// La poda va AQUÍ y no en un arranque cableado a mano a propósito: una limpieza que hay que acordarse
    /// de llamar es una limpieza que un día no se llama, y entonces el tope de dos gigas es una intención.
    /// </remarks>
    public static AlbumDeMiradas Suyo
    {
        get
        {
            if (_suyo != null) return _suyo;
            _suyo = DelUsuario();
            int fuera = _suyo.Podar();
            if (fuera > 0) LogBus.Log("album", $"podadas {fuera} mirada(s): pasaban de siete días o de dos gigas");
            return _suyo;
        }
    }

    /// <summary>El de la app: en los datos del usuario, con el reloj de verdad y los topes del dueño.</summary>
    public static AlbumDeMiradas DelUsuario() => new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "U", "recuerdos", "miradas"),
        () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), DosGigas, SieteDiasMs);

    private string Indice => Path.Combine(_carpeta, "album.json");

    /// <summary>
    /// Guarda una mirada y devuelve su ficha. Null si no es un JPEG o si el disco no dejó.
    /// </summary>
    public Ficha? Guardar(byte[] jpeg, string ubicacion, string quePasaba)
    {
        if (!EsJpeg(jpeg)) return null;
        try
        {
            lock (_candado)
            {
                Directory.CreateDirectory(_carpeta);
                long cuando = _ahora();
                string ruta = Path.Combine(_carpeta, $"{cuando}-{Limpio(ubicacion)}-{_fichas.Count + 1}.jpg");
                File.WriteAllBytes(ruta, jpeg);
                var ficha = new Ficha(ruta, cuando, ubicacion ?? "", quePasaba ?? "", jpeg.LongLength);
                _fichas.Add(ficha);
                Escribir();
                return ficha;
            }
        }
        catch (Exception e)
        {
            LogBus.Log("album", $"no pude guardar la mirada de «{ubicacion}»: {e.Message}");
            return null;
        }
    }

    /// <summary>La última foto de esa ubicación, o null si nunca se miró ahí (o ya se podó).</summary>
    public Ficha? UltimaDe(string ubicacion)
    {
        string busco = (ubicacion ?? "").Trim();
        if (busco.Length == 0) return null;
        lock (_candado)
            return _fichas.Where(f => f.Ubicacion.Equals(busco, StringComparison.OrdinalIgnoreCase))
                          .OrderByDescending(f => f.Cuando)
                          .FirstOrDefault();
    }

    /// <summary>Lo que se le contesta a quien pide una mirada: la foto si la hay, y siempre el relato.</summary>
    public sealed record Recuerdo(Ficha? Ficha, string Cuenta);

    /// <summary>
    /// LA PUERTA DEL ÁLBUM. Promesa 258 (spec 027).
    /// </summary>
    /// <remarks>
    /// SIN ESTO LA MEMORIA NO EXISTÍA PARA QUIEN TENÍA QUE USARLA. El 2026-09-17 el dueño le pidió a Ü que
    /// recordara una investigación y la foto de aquel momento, y contestó que no tenía ninguna: `UltimaDe`
    /// estaba escrita y no la llamaba nadie. Construir la memoria y no darle forma de consultarla es
    /// exactamente el «código inerte» que este repo persigue, sólo que del lado que no se ve.
    ///
    /// Y DE UN SITIO QUE NO TIENE NO SE CALLA: se dice QUÉ sí recuerda. Contestar «no hay nada» sobre una
    /// memoria que sí tiene fotos es lo que hizo creer que el álbum no existía.
    /// </remarks>
    public Recuerdo LoQueRecuerdo(string ubicacion)
    {
        string pido = (ubicacion ?? "").Trim();
        var f = UltimaDe(pido) ?? PorParecido(pido);
        if (f != null)
        {
            string hace = Hace(_ahora() - f.Cuando);
            return new Recuerdo(f, $"Esto es lo que había en «{f.Ubicacion}» {hace}"
                + (f.QuePasaba.Length > 0 ? $", mientras: {f.QuePasaba}." : "."));
        }

        var tengo = Todas;
        if (tengo.Count == 0)
            return new Recuerdo(null, "Todavía no tengo ninguna foto guardada: no he pasado por ningún sitio "
                + "desde que arranqué, o las que había ya caducaron.");

        string lista = string.Join("; ", tengo.Take(12).Select(x =>
            $"«{x.Ubicacion}» ({Hace(_ahora() - x.Cuando)}{(x.QuePasaba.Length > 0 ? ", " + x.QuePasaba : "")})"));
        return new Recuerdo(null, $"No tengo ninguna foto de «{pido}». Sí recuerdo: {lista}. "
            + "Pídeme una de ésas por su nombre.");
    }

    /// <summary>Si piden «google» y lo que hay es «web://google.com», es lo mismo. El modelo habla como una persona.</summary>
    private Ficha? PorParecido(string pido)
    {
        if (pido.Length < 3) return null;
        lock (_candado)
            return _fichas.Where(f => f.Ubicacion.Contains(pido, StringComparison.OrdinalIgnoreCase)
                                   || pido.Contains(f.Ubicacion, StringComparison.OrdinalIgnoreCase))
                          .OrderByDescending(f => f.Cuando)
                          .FirstOrDefault();
    }

    private static string Hace(long ms)
    {
        if (ms < 60_000) return "hace un momento";
        long min = ms / 60_000;
        if (min < 60) return $"hace {min} minuto(s)";
        long horas = min / 60;
        return horas < 24 ? $"hace {horas} hora(s)" : $"hace {horas / 24} día(s)";
    }

    /// <summary>Lo que el álbum recuerda ahora mismo, de lo más nuevo a lo más viejo.</summary>
    public IReadOnlyList<Ficha> Todas { get { lock (_candado) return _fichas.OrderByDescending(f => f.Cuando).ToList(); } }

    /// <summary>
    /// Quita lo que sobra y devuelve cuántas se fueron: primero por edad, después por tamaño, y siempre
    /// lo más viejo primero.
    /// </summary>
    public int Podar()
    {
        lock (_candado)
        {
            long ahora = _ahora();
            var fuera = new List<Ficha>();

            // POR EDAD. Una foto de hace ocho días ya no describe ninguna pantalla que siga existiendo.
            foreach (var f in _fichas) if (ahora - f.Cuando > _topeMs) fuera.Add(f);

            // POR TAMAÑO, sobre lo que queda y de lo más viejo hacia lo más nuevo, hasta volver a caber.
            var quedan = _fichas.Except(fuera).OrderBy(f => f.Cuando).ToList();
            long pesa = quedan.Sum(f => f.Bytes);
            foreach (var f in quedan)
            {
                if (pesa <= _topeBytes) break;
                fuera.Add(f);
                pesa -= f.Bytes;
            }

            foreach (var f in fuera)
            {
                try { if (File.Exists(f.Archivo)) File.Delete(f.Archivo); }
                catch (Exception e) { LogBus.Log("album", $"no pude borrar {Path.GetFileName(f.Archivo)}: {e.Message}"); }
                _fichas.Remove(f);
            }
            if (fuera.Count > 0) Escribir();
            return fuera.Count;
        }
    }

    /// <summary>Un JPEG empieza por FF D8 FF, y eso es lo que se comprueba: no la extensión, que la pone quien quiera.</summary>
    private static bool EsJpeg(byte[]? b) => b != null && b.Length > 4 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF;

    private static string Limpio(string? ubicacion)
    {
        string s = new string((ubicacion ?? "").Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '.').ToArray());
        if (s.Length > 40) s = s[..40];
        return s.Length == 0 ? "sin-sitio" : s.ToLowerInvariant();
    }

    private void Cargar()
    {
        try
        {
            if (!File.Exists(Indice)) return;
            var leidas = JsonSerializer.Deserialize<List<Ficha>>(File.ReadAllText(Indice));
            if (leidas == null) return;
            // Una ficha cuyo archivo ya no está no se recuerda: una foto que no se puede enseñar no es un recuerdo.
            foreach (var f in leidas) if (File.Exists(f.Archivo)) _fichas.Add(f);
        }
        catch (Exception e) { LogBus.Log("album", $"no pude leer el índice del álbum: {e.Message}"); }
    }

    private void Escribir()
    {
        try
        {
            Directory.CreateDirectory(_carpeta);
            File.WriteAllText(Indice, JsonSerializer.Serialize(_fichas));
        }
        catch (Exception e) { LogBus.Log("album", $"no pude escribir el índice del álbum: {e.Message}"); }
    }
}
