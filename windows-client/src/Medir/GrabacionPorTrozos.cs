using System.IO;
using Mapeador;
using ScreenRecorderLib;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Medir;

/// <summary>
/// GRABA LA PANTALLA EN TROZOS CORTOS, y borra los que no hicieron falta.
/// </summary>
/// <remarks>
/// Es la fase 2 de docs/specs/medir-el-terreno.md: dejar grabado el momento en que el terreno se
/// rompe, para poder mirarlo después.
///
/// TROZOS Y NO UNA GRABACIÓN LARGA, por dos motivos que apuntan al mismo sitio. Uno: un clip pasa a
/// ser «los trozos que tocan esta franja», copiados tal cual, sin ffmpeg —que este proyecto rechazó
/// a propósito, ver ScreenRecorder.cs:9—. Dos: se puede ir borrando lo limpio mientras se graba, que
/// es lo único que permite grabar un turno de doce horas sin llenar el disco.
///
/// AJUSTES BAJOS, Y NO ES TACAÑERÍA. Con los del grabador de enseñar —8 Mbps, 30 fps, pensados para
/// dos minutos— un día serían 86 GB. Grabar una interfaz no es grabar vídeo: la pantalla está quieta
/// casi todo el rato y H.264 comprime eso muy bien. A 4 fps se ve perfectamente qué pasó y en qué
/// orden, que es para lo que se graba.
///
/// MEDIDO, NO ESTIMADO (2026-08-25, tres trozos reales en esta máquina): 95 MB/hora, o sea 2,2 GB al
/// día si no se borrara nada. La estimación de la spec era 10,8 GB/día — cinco veces peor que la
/// realidad, porque suponer cuánto ocupa vídeo de una pantalla quieta se da mal. Con el barrido de
/// lo limpio, lo que hay en disco en cualquier momento son los últimos minutos más los clips
/// pendientes: del orden de decenas de MB.
///
/// LO QUE SE BORRA LO DECIDE <see cref="LosSegmentos.SePuedeBorrar"/>, que es puro y está juzgado
/// por el contrato del mapeador. Aquí solo se ejecuta esa decisión — porque una regla de borrado
/// equivocada destruye justo la prueba del fallo que veníamos a buscar, y eso no se puede dejar a
/// que alguien lo relea y le parezca bien.
/// </remarks>
public sealed class GrabacionPorTrozos : IAsyncDisposable
{
    /// <summary>Dónde viven los trozos. Fuera de la carpeta de logs: esto pesa y se borra solo.</summary>
    public static string Carpeta { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "U", "medir", "trozos");

    private readonly LoQueSeHaRoto _roto;
    private readonly object _llave = new();
    private readonly List<Segmento> _trozos = new();
    private readonly List<DateTime> _errores = new();

    private Recorder? _grabando;
    private Segmento? _enCurso;
    private System.Threading.Timer? _reloj;
    private IndicadorDeGrabacion? _indicador;

    /// <summary>Está grabando ahora mismo. La interfaz lo pinta: nadie graba a nadie sin que se vea.</summary>
    public bool Encendida { get; private set; }

    /// <summary>Cambió de estado. Para el indicador.</summary>
    public event Action<bool>? Cambio;

    public GrabacionPorTrozos(LoQueSeHaRoto roto)
    {
        _roto = roto;
        // Los errores se apuntan SOLOS, según van pasando. Preguntarle al registro cada vez que toca
        // rotar daría lo mismo mientras quepan todos, pero el registro se olvida de lo viejo y esto
        // no puede depender de cuánto le quepa: un error olvidado sería un trozo borrado por error.
        _roto.Ocurrio += Apuntar;
    }

    private void Apuntar(ErrorDelTerreno e)
    {
        lock (_llave) _errores.Add(e.Cuando);
    }

    public void Encender()
    {
        lock (_llave)
        {
            if (Encendida) return;
            Encendida = true;
        }
        Directory.CreateDirectory(Carpeta);
        LogBus.Log("medir", $"grabación encendida · trozos de {LosSegmentos.Dura.TotalSeconds:F0} s en {Carpeta}");

        // EL PUNTO ROJO SE ENCIENDE CON LA GRABACIÓN Y NO ANTES NI DESPUÉS. Van juntos a propósito:
        // si el indicador se pudiera encender por su cuenta, podría mentir en las dos direcciones —
        // y de las dos, la que importa es grabar sin que se vea.
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            _indicador ??= new IndicadorDeGrabacion();
            _indicador.Show();
        });
        Cambio?.Invoke(true);

        Rotar();
        _reloj = new System.Threading.Timer(_ => Rotar(), null, LosSegmentos.Dura, LosSegmentos.Dura);
    }

    public void Apagar()
    {
        lock (_llave)
        {
            if (!Encendida) return;
            Encendida = false;
        }
        try { _reloj?.Dispose(); } catch { }
        _reloj = null;
        CerrarElDeAhora();
        System.Windows.Application.Current?.Dispatcher.Invoke(() => _indicador?.Hide());
        LogBus.Log("medir", "grabación apagada");
        Cambio?.Invoke(false);
    }

    /// <summary>
    /// Cierra el trozo de ahora, abre el siguiente y barre lo que ya no hace falta.
    /// </summary>
    private void Rotar()
    {
        if (!Encendida) return;
        try
        {
            CerrarElDeAhora();
            Abrir();
            Barrer();
        }
        catch (Exception e) { LogBus.Log("medir", $"al rotar el trozo: {e.Message}"); }
    }

    private void Abrir()
    {
        var empieza = DateTime.UtcNow;
        string archivo = Path.Combine(Carpeta, $"{empieza:yyyyMMdd-HHmmss}.mp4");

        var opciones = new RecorderOptions
        {
            SourceOptions = new SourceOptions { RecordingSources = { new DisplayRecordingSource() } },
            VideoEncoderOptions = new VideoEncoderOptions
            {
                Encoder = new H264VideoEncoder { EncoderProfile = H264Profile.Main },

                // LOS NÚMEROS QUE HACEN VIABLE GRABAR UN TURNO ENTERO. Ver el remark de arriba: con
                // los del grabador de enseñar esto serían decenas de GB al día.
                Bitrate = 1_000_000,
                Framerate = 4,
                IsMp4FastStartEnabled = true,
            },

            // SIN AUDIO, y a propósito. Aquí se graba para ver qué pasó en la PANTALLA cuando el
            // grafo se rompió, y el sonido no lo cuenta. Y son urgencias: lo que se oye en esa
            // consulta no es nuestro y no hay ninguna razón para llevárnoslo.
            AudioOptions = new AudioOptions { IsAudioEnabled = false },
        };

        _grabando = Recorder.CreateRecorder(opciones);
        _grabando.OnRecordingFailed += (_, a) => LogBus.Log("medir", $"la grabación falló: {a.Error}");
        _grabando.Record(archivo);

        lock (_llave)
        {
            _enCurso = new Segmento(empieza, archivo);
            _trozos.Add(_enCurso);
        }
    }

    private void CerrarElDeAhora()
    {
        var g = _grabando;
        _grabando = null;
        if (g == null) return;
        try { g.Stop(); } catch (Exception e) { LogBus.Log("medir", $"al cerrar el trozo: {e.Message}"); }
        lock (_llave) _enCurso = null;
    }

    /// <summary>
    /// Borra lo que ya no puede hacer falta. NO decide aquí qué se puede borrar — eso es
    /// <see cref="LosSegmentos.SePuedeBorrar"/>, que está juzgado por el contrato.
    /// </summary>
    private void Barrer()
    {
        List<Segmento> candidatos;
        DateTime[] errores;
        lock (_llave)
        {
            var ahora = DateTime.UtcNow;
            errores = _errores.ToArray();
            candidatos = _trozos
                .Where(t => t != _enCurso && LosSegmentos.SePuedeBorrar(t, ahora, errores))
                .ToList();
            foreach (var t in candidatos) _trozos.Remove(t);
        }

        int fuera = 0;
        foreach (var t in candidatos)
        {
            try { if (File.Exists(t.Archivo)) { File.Delete(t.Archivo); fuera++; } }
            catch (Exception e) { LogBus.Log("medir", $"no pude borrar {Path.GetFileName(t.Archivo)}: {e.Message}"); }
        }
        if (fuera > 0) LogBus.Log("medir", $"barridos {fuera} trozo(s) sin errores");
    }

    /// <summary>Los trozos que hay ahora mismo. Para el visor y para las pruebas.</summary>
    public IReadOnlyList<Segmento> Trozos
    {
        get { lock (_llave) return _trozos.ToList(); }
    }

    public ValueTask DisposeAsync()
    {
        _roto.Ocurrio -= Apuntar;
        Apagar();
        return ValueTask.CompletedTask;
    }
}
