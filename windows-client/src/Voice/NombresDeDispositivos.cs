using System.IO;
using System.Text.Json;
using U.Graph;

namespace U.WindowsClient.Voice;

/// <summary>
/// EL NOMBRE QUE EL MÉDICO LE PONE A CADA APARATO, en disco. Promesa 151 (spec 010).
/// </summary>
/// <remarks>
/// «Collar Omi» no distingue un collar de otro, y en un hospital los collares se prestan: el de
/// urgencias, el de consulta externa, el que se llevó alguien ayer. Sin un nombre propio, elegir el
/// aparato correcto es adivinar — y olvidarlo, peor todavía, porque no se sabe cuál se está
/// olvidando.
///
/// Calcado de <see cref="Workflows.NombresDeWorkflows"/> a propósito, hasta el mismo comportamiento
/// del vacío: son el mismo problema —lo que uno bautiza se queda como uno lo dejó— y dos formas
/// distintas de resolverlo serían dos formas distintas de fallar. Poner vacío QUITA el nombre
/// (patrón nº9: vacío no es un dato) y se vuelve al de fábrica.
///
/// Su límite honesto, el mismo que el de los workflows: es de ESTA máquina. El emparejamiento
/// también lo es, así que el nombre no sobra en ninguna parte donde el aparato exista.
/// </remarks>
public sealed class NombresDeDispositivos
{
    private readonly Dictionary<string, string> _nombres = new(StringComparer.Ordinal);
    private readonly object _candado = new();

    public static string Archivo => Path.Combine(UserPaths.Local, "U", "nombres-de-dispositivos.json");

    /// <summary>Cómo se llama un aparato al que nadie ha bautizado.</summary>
    public static string DeFabrica(string id) => id switch
    {
        DispositivosEnlazados.Collar => "Collar Omi",
        _ => "Dispositivo",
    };

    public NombresDeDispositivos()
    {
        try
        {
            if (!File.Exists(Archivo)) return;
            var leido = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Archivo));
            if (leido == null) return;
            foreach (var (id, nombre) in leido)
                if (!string.IsNullOrWhiteSpace(nombre)) _nombres[id] = nombre;
        }
        catch (Exception e)
        {
            // Un archivo corrupto no puede dejar sin selector de micrófono: se arranca sin nombres
            // —que es exactamente el estado de quien nunca puso ninguno— y se DICE por qué.
            Diagnostics.LogBus.Log("dispositivos", $"no pude leer {Archivo}: {e.Message}");
        }
    }

    /// <summary>El nombre puesto a este aparato, o null si nadie lo bautizó.</summary>
    public string? De(string id)
    {
        lock (_candado) return _nombres.TryGetValue(id ?? "", out var n) ? n : null;
    }

    /// <summary>Cómo hay que llamarlo en pantalla: el puesto si lo hay, y si no el de fábrica.</summary>
    public string ComoSeLlama(string id) => De(id) ?? DeFabrica(id);

    /// <summary>Pone (o, si viene vacío, quita) el nombre de este aparato, y lo deja en disco.</summary>
    public void Poner(string id, string? nombre)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        lock (_candado)
        {
            string n = (nombre ?? "").Trim();
            if (n.Length == 0) _nombres.Remove(id);
            else _nombres[id] = n;
            Guardar();
        }
    }

    /// <summary>
    /// El aparato se desenlazó: su nombre se va con él.
    /// </summary>
    /// <remarks>
    /// Y NO ES LO MISMO QUE DEJARLO AHÍ. Si el nombre sobreviviera al olvido, el siguiente collar
    /// que alguien enlazara heredaría el nombre del anterior sin haberlo pedido: «El de urgencias»
    /// señalando a un aparato distinto. El nombre existía justo para distinguir esos dos.
    /// </remarks>
    public void Olvidar(string id) => Poner(id, null);

    private void Guardar()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Archivo)!);
            File.WriteAllText(Archivo, JsonSerializer.Serialize(_nombres,
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                }));
        }
        catch (Exception e)
        {
            Diagnostics.LogBus.Log("dispositivos", $"no pude guardar {Archivo}: {e.Message}");
        }
    }
}
