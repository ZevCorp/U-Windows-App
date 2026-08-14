using System.IO;
using System.Text.Json;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Voice;

/// <summary>
/// EL COLLAR, CONECTADO SIEMPRE, por encima de la conversación.
///
/// Antes el collar nacía y moría con la sesión de voz, y eso tenía una consecuencia que sólo se ve al
/// usarlo: con la voz apagada no había nadie escuchando el botón, así que el botón sólo podía APAGAR.
/// Encender con el botón exige que el enlace exista cuando no hay conversación — que es justo lo que
/// significa «permanente» (2026-08-13, pedido por el usuario tres veces antes de que yo lo hiciera).
///
/// El reparto queda así, y no se debe volver a mezclar:
///
///   · Este servicio      — el enlace: conectar, reconectar, el botón, la batería. Vive con la app.
///   · <see cref="LiveAudio"/> — la conversación: coge el audio de aquí mientras dura, y lo suelta.
///
/// Por eso <see cref="Desconectar"/> lo llama la pantalla de configuración, y NO el final de una
/// conversación: colgar no puede desemparejar.
/// </summary>
public static class CollarPermanente
{
    private static readonly object Candado = new();
    private static FuenteOmi? _fuente;
    private static CancellationTokenSource? _cts;
    private static bool _permanente;
    private static bool _reconectando;

    /// <summary>Dónde se recuerda la decisión. Al lado de los logs, que es donde vive lo de esta app.</summary>
    private static string Archivo => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "U", "collar.json");

    /// <summary>Audio del collar, en el mismo formato que el micrófono local.</summary>
    public static event Action<byte[]>? Capturado;

    /// <summary>Se pulsó el botón del collar. Llega SIEMPRE que haya enlace, con voz o sin ella.</summary>
    public static event Action? BotonPulsado;

    /// <summary>Cambió algo que la pantalla de configuración enseña: conectado, batería, permanente.</summary>
    public static event Action? Cambio;

    public static bool Conectado => _fuente?.Viva == true;

    /// <summary>El usuario quiere el collar enlazado. Se recuerda entre arranques.</summary>
    public static bool Permanente
    {
        get => _permanente;
        private set { _permanente = value; Guardar(); Cambio?.Invoke(); }
    }

    /// <summary>Última línea de estado, para enseñarla tal cual en la pantalla.</summary>
    public static string Estado { get; private set; } = "sin enlazar";

    /// <summary>
    /// Se llama al arrancar la app. Si el usuario dejó el enlace puesto, se reconecta solo.
    /// </summary>
    public static void Restaurar()
    {
        try
        {
            if (!File.Exists(Archivo)) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(Archivo));
            if (doc.RootElement.TryGetProperty("permanente", out var p) && p.GetBoolean())
            {
                _permanente = true;
                LogBus.Log("omi", "enlace permanente recordado de la sesión anterior: reconectando");
                _ = Task.Run(() => EncenderAsync());
            }
        }
        catch (Exception e)
        {
            // El motivo, no un catch mudo: «no hay archivo» y «el archivo está roto» se arreglan
            // de forma distinta, y confundirlos deja al usuario sin collar sin saber por qué.
            LogBus.Log("omi", $"no se pudo leer {Archivo}: {e.GetType().Name}: {e.Message}");
        }
    }

    private static void Guardar()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Archivo)!);
            File.WriteAllText(Archivo, $"{{\"permanente\":{(_permanente ? "true" : "false")}}}");
        }
        catch (Exception e) { LogBus.Log("omi", $"no se pudo guardar {Archivo}: {e.Message}"); }
    }

    /// <summary>Enciende el enlace permanente: conecta ahora y lo recuerda para la próxima.</summary>
    public static async Task<bool> EncenderAsync()
    {
        Permanente = true;
        return await ConectarAsync();
    }

    /// <summary>Apaga el enlace: suelta el collar y deja de recordarlo.</summary>
    public static void Apagar()
    {
        Permanente = false;
        Desconectar();
    }

    /// <summary>
    /// Conecta AHORA sin tocar la preferencia. Lo usa un gesto puntual («oye por el collar esta vez»);
    /// hacer el enlace permanente es una decisión distinta y se toma en la pantalla.
    /// </summary>
    public static async Task<bool> ConectarAsync()
    {
        lock (Candado)
        {
            if (_fuente != null) return true;
            if (_reconectando) return false;
            _reconectando = true;
        }

        try
        {
            var f = new FuenteOmi();
            _cts = new CancellationTokenSource();
            Estado = "buscando el collar…";
            Cambio?.Invoke();

            if (!await f.AbrirAsync(_cts.Token))
            {
                f.Dispose();
                Estado = "no se encontró el collar";
                Cambio?.Invoke();
                ProgramarReintento();
                return false;
            }

            f.Capturado += AlCapturar;
            f.BotonPulsado += AlPulsar;
            f.Perdido += AlPerder;

            lock (Candado) _fuente = f;
            Estado = "enlazado";

            // SE RECUERDA SOLO. Un collar que ya funcionó una vez es un collar conocido, y a partir
            // de ahí se comporta como cualquier aparato Bluetooth emparejado: aparece y se conecta.
            // Obligar a abrir un panel para decir «sí, este» es pedirle al usuario que repita algo
            // que el sistema ya sabe (2026-08-13, pedido por el usuario).
            if (!_permanente)
            {
                _permanente = true;
                Guardar();
                LogBus.Log("omi", "collar recordado: a partir de ahora se conecta solo al arrancar");
            }

            Cambio?.Invoke();
            return true;
        }
        finally { lock (Candado) _reconectando = false; }
    }

    public static void Desconectar()
    {
        FuenteOmi? f;
        lock (Candado) { f = _fuente; _fuente = null; }

        try { _cts?.Cancel(); _cts?.Dispose(); } catch (Exception e) { LogBus.Log("omi", $"al cancelar: {e.Message}"); }
        _cts = null;

        if (f != null)
        {
            f.Capturado -= AlCapturar;
            f.BotonPulsado -= AlPulsar;
            f.Perdido -= AlPerder;
            f.Dispose();
        }
        Estado = Permanente ? "desconectado" : "sin enlazar";
        Cambio?.Invoke();
    }

    private static void AlCapturar(byte[] trozo) => Capturado?.Invoke(trozo);

    private static void AlPulsar() => BotonPulsado?.Invoke();

    private static void AlPerder(string motivo)
    {
        LogBus.Log("omi", "enlace permanente perdido: " + motivo);
        Desconectar();
        ProgramarReintento();
    }

    private static bool _esperando;

    /// <summary>
    /// SE QUEDA ESPERANDO AL COLLAR, indefinidamente, mientras el usuario lo quiera enlazado.
    ///
    /// Esto es lo que lo hace comportarse como un aparato Bluetooth normal: enciendes el collar y se
    /// conecta, entras en alcance y se conecta, reinicias la app y se conecta. No hay un momento en
    /// el que la aplicación se rinda y haya que ir a un panel a pedirlo — el panel queda para
    /// forzarlo a mano o para desenlazar, no como paso obligatorio (2026-08-13).
    ///
    /// Un solo esperador a la vez: cada pérdida programaba el suyo y acababan solapándose, con dos
    /// rastreos BLE compitiendo por la misma radio.
    /// </summary>
    private static void ProgramarReintento()
    {
        if (!Permanente) return;
        lock (Candado) { if (_esperando) return; _esperando = true; }

        _ = Task.Run(async () =>
        {
            try
            {
                while (Permanente && _fuente == null)
                {
                    await Task.Delay(6000);
                    if (!Permanente || _fuente != null) break;
                    await ConectarAsync();
                }
            }
            finally { lock (Candado) _esperando = false; }
        });
    }

    /// <summary>
    /// Olvida el collar del todo: lo suelta y deja de esperarlo. Es lo que hay que hacer para
    /// cambiar de aparato, y por eso está en el panel aunque el enlace se ponga solo.
    /// </summary>
    public static void Olvidar()
    {
        LogBus.Log("omi", "collar olvidado por el usuario");
        Apagar();
    }

    /// <summary>Cuánto lleva el collar sin mandar audio, para quien vigile el relevo.</summary>
    public static long SinTrama => _fuente?.SinTrama ?? 0;

    /// <summary>Cuánto lleva Bluetooth diciendo que el collar no está.</summary>
    public static long MsDesconectado => _fuente?.MsDesconectado ?? 0;
}
