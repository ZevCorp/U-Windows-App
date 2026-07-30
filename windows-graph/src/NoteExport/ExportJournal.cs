using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace U.Graph.NoteExport;

/// <summary>
/// Hasta dónde llegó un trabajo. El orden importa: solo <see cref="Executing"/> y posteriores
/// implican que SAP PUDO haber sido escrito.
/// </summary>
public enum ExportPhase
{
    /// <summary>Reclamado. Todavía no se ha tocado SAP. Repetirlo es seguro.</summary>
    Claimed = 0,

    /// <summary>
    /// Se empezó a ejecutar el workflow contra SAP. A partir de aquí, un corte deja el trabajo en
    /// estado INCIERTO: puede que la nota esté escrita, entera o a medias.
    /// </summary>
    Executing = 1,

    /// <summary>SAP publicó su señal y ya sabemos qué pasó. Falta reportarlo.</summary>
    Verified = 2,

    /// <summary>Resultado construido y persistido; pendiente del ack de Graph.</summary>
    Reported = 3,
}

/// <summary>Lo que se sabe de un trabajo entre reinicios.</summary>
public sealed class ExportRecord
{
    [JsonPropertyName("export_id")] public string ExportId { get; set; } = "";
    [JsonPropertyName("device")] public string Device { get; set; } = "";
    [JsonPropertyName("phase")] public ExportPhase Phase { get; set; }
    [JsonPropertyName("attempts")] public int Attempts { get; set; }
    [JsonPropertyName("started_at")] public string StartedAt { get; set; } = "";
    [JsonPropertyName("updated_at")] public string UpdatedAt { get; set; } = "";

    /// <summary>El resultado a entregar, si ya se construyó. Se guarda ANTES del primer envío.</summary>
    [JsonPropertyName("result")] public ExportResultRequest? Result { get; set; }

    /// <summary>
    /// Una corrida anterior murió con este trabajo en <see cref="ExportPhase.Executing"/>. Nadie
    /// sabe si SAP quedó escrito. Vuelve a false solo cuando una persona lo resuelve fuera de aquí.
    /// </summary>
    [JsonPropertyName("uncertain")] public bool Uncertain { get; set; }

    /// <summary>Qué se estaba haciendo cuando se cortó. Para el registro, nunca con PHI.</summary>
    [JsonPropertyName("uncertain_note")] public string UncertainNote { get; set; } = "";
}

/// <summary>
/// El diario del ejecutor: lo único que sobrevive a un cierre de la aplicación o a un reinicio de
/// Windows, y por tanto lo único que puede distinguir «este trabajo no se había empezado» de «este
/// trabajo pudo haber escrito en la historia clínica».
///
/// Por qué hace falta, en concreto: si la aplicación muere a mitad de ejecución, Graph no se entera.
/// El lease vence a los diez minutos, el trabajo vuelve a la cola y el siguiente claim lo sirve otra
/// vez — con `attempts` incrementado, que es lo único que Graph le cuenta al ejecutor. Pero
/// `attempts=2` no distingue «un reintento legítimo después de un error reportado» de «alguien ya
/// escribió esto y no llegó a decirlo». Sin el diario, la única salida sería ejecutar a ciegas y
/// arriesgar una nota duplicada en la historia de un paciente.
///
/// Un archivo por trabajo, escrito de forma atómica (temporal + move): un corte de luz a media
/// escritura deja el archivo viejo intacto, nunca un JSON truncado que al arrancar no se pueda leer.
/// </summary>
public sealed class ExportJournal
{
    private readonly string _folder;
    private readonly Action<string>? _log;
    private readonly object _gate = new();

    public ExportJournal(Action<string>? log = null, string? folder = null)
    {
        _log = log;
        _folder = folder ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "U", "note-exports");
    }

    private static string Now => DateTime.UtcNow.ToString("o");

    private string PathFor(string exportId)
    {
        string safe = new(exportId.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());
        return Path.Combine(_folder, (safe.Length > 0 ? safe : "sin-id") + ".json");
    }

    public ExportRecord? Find(string exportId)
    {
        lock (_gate) return Read(PathFor(exportId));
    }

    /// <summary>Abre (o recupera) el registro de un trabajo recién reclamado.</summary>
    public ExportRecord Begin(string exportId, string device, int attempts)
    {
        lock (_gate)
        {
            ExportRecord record = Read(PathFor(exportId)) ?? new ExportRecord
            {
                ExportId = exportId,
                StartedAt = Now,
            };
            record.Device = device;
            record.Attempts = attempts;
            // La fase NO se rebaja: si el registro venía de Executing, sigue diciéndolo. Rebajarla
            // aquí borraría justo la señal que impide la doble escritura.
            if (record.Phase == default && record.Result == null) record.Phase = ExportPhase.Claimed;
            Write(record);
            return record;
        }
    }

    public void Mark(string exportId, ExportPhase phase)
    {
        lock (_gate)
        {
            ExportRecord record = Read(PathFor(exportId)) ?? new ExportRecord { ExportId = exportId, StartedAt = Now };
            if (phase > record.Phase) record.Phase = phase;
            Write(record);
        }
    }

    /// <summary>
    /// Guarda el resultado ANTES de intentar entregarlo. Si la aplicación muere entre esto y el ack,
    /// al arrancar se reenvía — y reenviar es seguro porque el endpoint es idempotente.
    /// </summary>
    public void SaveResult(string exportId, ExportResultRequest result)
    {
        lock (_gate)
        {
            ExportRecord record = Read(PathFor(exportId)) ?? new ExportRecord { ExportId = exportId, StartedAt = Now };
            record.Result = result;
            record.Device = result.Device;
            record.Phase = ExportPhase.Reported;
            // El resultado ya está decidido: la incertidumbre de una corrida anterior queda resuelta.
            record.Uncertain = false;
            record.UncertainNote = "";
            Write(record);
        }
    }

    /// <summary>Trabajo cerrado con ack de Graph: fuera del diario.</summary>
    public void Complete(string exportId)
    {
        lock (_gate)
        {
            try { File.Delete(PathFor(exportId)); } catch { }
        }
    }

    /// <summary>
    /// Marca un trabajo como de desenlace desconocido. Se llama cuando la ejecución se interrumpe
    /// después de haber tocado SAP, y al arrancar sobre un registro que quedó en Executing.
    /// </summary>
    public void MarkUncertain(string exportId, string note)
    {
        lock (_gate)
        {
            ExportRecord? record = Read(PathFor(exportId));
            if (record == null) return;
            record.Uncertain = true;
            record.UncertainNote = note;
            Write(record);
        }
    }

    /// <summary>Todos los registros del diario, ordenados por antigüedad.</summary>
    public IReadOnlyList<ExportRecord> All()
    {
        lock (_gate)
        {
            var list = new List<ExportRecord>();
            try
            {
                if (!Directory.Exists(_folder)) return list;
                foreach (string file in Directory.GetFiles(_folder, "*.json"))
                {
                    ExportRecord? record = Read(file);
                    if (record != null && record.ExportId.Length > 0) list.Add(record);
                    else TryDelete(file); // ilegible: reenviar lo que no se puede leer sería inventar
                }
            }
            catch (Exception e) { _log?.Invoke($"no se pudo leer el diario de exportaciones: {e.Message}"); }
            return list.OrderBy(r => r.StartedAt, StringComparer.Ordinal).ToList();
        }
    }

    /// <summary>
    /// Cierre de arranque: todo lo que quedó a medio ejecutar se marca incierto ANTES de reclamar
    /// nada. Devuelve los resultados que esperan ack, para reenviarlos primero.
    /// </summary>
    public IReadOnlyList<ExportRecord> Recover()
    {
        var pending = new List<ExportRecord>();
        foreach (ExportRecord record in All())
        {
            if (record.Result != null)
            {
                pending.Add(record);
                continue;
            }
            if (record.Phase is ExportPhase.Executing or ExportPhase.Verified && !record.Uncertain)
            {
                MarkUncertain(record.ExportId,
                    $"la aplicación se cerró con el trabajo en fase {record.Phase}");
                _log?.Invoke($"⚠ el trabajo {record.ExportId} quedó a medio ejecutar en una corrida anterior " +
                             "(fase " + record.Phase + "): NO se volverá a ejecutar a ciegas. " +
                             "Si Graph lo vuelve a servir, se pedirá intervención.");
            }
        }
        return pending;
    }

    // ── Disco ────────────────────────────────────────────────────────────────

    private ExportRecord? Read(string file)
    {
        try
        {
            if (!File.Exists(file)) return null;
            return JsonSerializer.Deserialize<ExportRecord>(File.ReadAllText(file));
        }
        catch { return null; }
    }

    private void Write(ExportRecord record)
    {
        record.UpdatedAt = Now;
        try
        {
            Directory.CreateDirectory(_folder);
            string target = PathFor(record.ExportId);
            string temp = target + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(record));
            // Move con overwrite es atómico en NTFS: o está el archivo viejo entero, o el nuevo entero.
            File.Move(temp, target, overwrite: true);
        }
        catch (Exception e)
        {
            // Sin diario el ejecutor sigue funcionando dentro de la sesión, pero pierde la garantía
            // que justifica su existencia. Se dice con todas las letras en vez de degradar callando.
            _log?.Invoke($"✋ no se pudo escribir el diario de {record.ExportId}: {e.Message}. " +
                         "Sin diario NO hay protección contra doble escritura si la aplicación se cierra.");
        }
    }

    private static void TryDelete(string file)
    {
        try { File.Delete(file); } catch { }
    }
}
