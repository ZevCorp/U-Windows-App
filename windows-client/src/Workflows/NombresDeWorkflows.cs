using System.IO;
using System.Text.Json;
using U.Graph;

namespace U.WindowsClient.Workflows;

/// <summary>
/// LOS NOMBRES QUE EL OPERADOR LES PONE a sus workflows, por id, en disco. Promesa 109 (spec 007).
/// </summary>
/// <remarks>
/// Graph no tiene endpoint para renombrar (medido el 2026-09-02), y aunque lo tuviera, el nombre
/// del LLM llega roto a veces. El nombre que uno le pone a lo que acaba de enseñar tiene que
/// quedarse donde uno lo puso: <c>%LOCALAPPDATA%\U\nombres-de-workflows.json</c>, por la misma
/// puerta (<see cref="UserPaths.Local"/>) que el resto de lo local, y por eso el contrato lo juzga
/// en su propio directorio.
///
/// Poner vacío QUITA el nombre —vacío no es un nombre (patrón nº9)— y se vuelve al derivado.
/// Su límite honesto: es de esta máquina. Si el mismo operador abre Ü en otra, verá el derivado.
/// </remarks>
public sealed class NombresDeWorkflows
{
    private readonly Dictionary<string, string> _nombres = new(StringComparer.Ordinal);
    private readonly object _candado = new();

    public static string Archivo => Path.Combine(UserPaths.Local, "U", "nombres-de-workflows.json");

    public NombresDeWorkflows()
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
            // Un archivo corrupto no puede dejar sin selector: se arranca sin nombres y se dice.
            Diagnostics.LogBus.Log("workflows", $"no pude leer {Archivo}: {e.Message}");
        }
    }

    /// <summary>El nombre puesto a este id, o null si no le han puesto.</summary>
    public string? De(string id)
    {
        lock (_candado) return _nombres.TryGetValue(id ?? "", out var n) ? n : null;
    }

    /// <summary>Pone (o, si viene vacío, quita) el nombre de este id, y lo deja en disco.</summary>
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

    private void Guardar()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Archivo)!);
            File.WriteAllText(Archivo, JsonSerializer.Serialize(_nombres,
                new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        }
        catch (Exception e)
        {
            Diagnostics.LogBus.Log("workflows", $"no pude guardar {Archivo}: {e.Message}");
        }
    }
}
