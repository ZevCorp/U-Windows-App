using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace U.WindowsClient.Voice;

/// <summary>Memoria personal de la voz, persistida localmente para no depender de una ruta remota.</summary>
public sealed class MemoriaPersonal
{
    private static readonly object Candado = new();
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string _userId;
    private readonly string _archivo;

    /// <param name="userId">Identidad estable del usuario de Windows.</param>
    /// <param name="archivo">Ruta opcional, usada por el contrato y por pruebas aisladas.</param>
    public MemoriaPersonal(string userId, string? archivo = null)
    {
        _userId = string.IsNullOrWhiteSpace(userId) ? "anon" : userId.Trim();
        _archivo = archivo ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "U", "memoria-personal.json");
    }

    public Task<Resultado> EjecutarAsync(string command, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        string text = QuitarPrefijo(command.Trim());
        if (text.Length == 0)
            return Task.FromResult(new Resultado(false, "Dime qué quieres que recuerde.", "", ""));

        lock (Candado)
        {
            var documento = Leer();
            var existente = documento.Items.FirstOrDefault(x =>
                x.UserId == _userId && string.Equals(x.Text, text, StringComparison.OrdinalIgnoreCase));
            if (existente != null)
                return Task.FromResult(new Resultado(true, existente.DueAt.HasValue
                    ? "Ya tenía ese recordatorio guardado."
                    : "Ya lo tenía guardado para nuestras próximas conversaciones.",
                    existente.Kind, existente.Id, existente.DueAt));

            DateTimeOffset? dueAt = ExtraerFecha(text, DateTimeOffset.Now);
            string kind = dueAt.HasValue ? "reminder" : "fact";

            var recuerdo = new Recuerdo
            {
                Id = $"local_{Guid.NewGuid():N}",
                UserId = _userId,
                Text = text,
                Kind = kind,
                CreatedAt = DateTimeOffset.UtcNow,
                DueAt = dueAt,
                TimeZone = TimeZoneInfo.Local.Id,
            };
            documento.Items.Add(recuerdo);
            Escribir(documento);
            string respuesta = dueAt.HasValue
                ? $"Te lo recordaré a las {dueAt.Value:HH:mm} de tu hora local."
                : "Lo recordaré para nuestras próximas conversaciones.";
            return Task.FromResult(new Resultado(true, respuesta, kind, recuerdo.Id, dueAt));
        }
    }

    public Task<string> ContextoAsync(CancellationToken ct, string query = "")
    {
        ct.ThrowIfCancellationRequested();
        lock (Candado)
        {
            var filtro = query.Trim();
            var documento = Leer();
            if (NormalizarRecordatorios(documento)) Escribir(documento);
            var recuerdos = documento.Items
                .Where(x => x.UserId == _userId)
                .Where(x => filtro.Length == 0 || x.Text.Contains(filtro, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.CreatedAt)
                .Take(20)
                .Select(x => x.DueAt.HasValue
                    ? $"- [recordatorio {x.DueAt.Value:yyyy-MM-dd HH:mm}] {x.Text}"
                    : $"- [{x.Kind}] {x.Text}")
                .ToArray();
            return Task.FromResult(string.Join("\n", recuerdos));
        }
    }

    public IReadOnlyList<Recordatorio> Pendientes(DateTimeOffset ahora)
    {
        lock (Candado)
        {
            var documento = Leer();
            bool normalizado = NormalizarRecordatorios(documento);
            if (normalizado) Escribir(documento);
            return documento.Items
                .Where(x => x.UserId == _userId && x.DueAt.HasValue && !x.Delivered && x.DueAt.Value <= ahora)
                .OrderBy(x => x.DueAt)
                .Select(x => new Recordatorio(x.Id, x.Text, x.DueAt!.Value, x.TimeZone, x.Delivered))
                .ToArray();
        }
    }

    public bool MarcarEntregado(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        lock (Candado)
        {
            var documento = Leer();
            var recuerdo = documento.Items.FirstOrDefault(x => x.UserId == _userId && x.Id == id);
            if (recuerdo == null || recuerdo.Delivered) return false;
            recuerdo.Delivered = true;
            Escribir(documento);
            return true;
        }
    }

    public readonly record struct Resultado(bool Ok, string Response, string Kind, string MemoryId,
        DateTimeOffset? ReminderDueAt = null);

    public readonly record struct Recordatorio(string Id, string Text, DateTimeOffset DueAt,
        string TimeZone, bool Delivered);

    private Documento Leer()
    {
        if (!File.Exists(_archivo)) return new Documento();
        try
        {
            string json = File.ReadAllText(_archivo);
            return JsonSerializer.Deserialize<Documento>(json, Json) ?? new Documento();
        }
        catch (IOException)
        {
            return new Documento();
        }
        catch (JsonException)
        {
            return new Documento();
        }
    }

    private void Escribir(Documento documento)
    {
        string? carpeta = Path.GetDirectoryName(_archivo);
        if (!string.IsNullOrWhiteSpace(carpeta)) Directory.CreateDirectory(carpeta);

        string temporal = _archivo + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporal, JsonSerializer.Serialize(documento, Json));
        File.Move(temporal, _archivo, overwrite: true);
    }

    private static string QuitarPrefijo(string text)
    {
        string[] prefixes =
        {
            "recuerda que ", "recuérdame ", "recuérdalo ", "acuérdate de ",
            "acuérdalo ", "acuerda que ", "no olvides "
        };
        foreach (var prefix in prefixes)
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return text[prefix.Length..].Trim();
        return text;
    }

    private static bool NormalizarRecordatorios(Documento documento)
    {
        bool cambio = false;
        foreach (var item in documento.Items.Where(x => !x.Delivered && !x.DueAt.HasValue))
        {
            var due = ExtraerFecha(item.Text, item.CreatedAt.ToLocalTime());
            if (!due.HasValue) continue;
            item.DueAt = due;
            item.Kind = "reminder";
            if (string.IsNullOrWhiteSpace(item.TimeZone)) item.TimeZone = TimeZoneInfo.Local.Id;
            cambio = true;
        }
        return cambio;
    }

    private sealed class Documento
    {
        [JsonPropertyName("items")] public List<Recuerdo> Items { get; set; } = new();
    }

    private sealed class Recuerdo
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("userId")] public string UserId { get; set; } = "";
        [JsonPropertyName("text")] public string Text { get; set; } = "";
        [JsonPropertyName("kind")] public string Kind { get; set; } = "fact";
        [JsonPropertyName("createdAt")] public DateTimeOffset CreatedAt { get; set; }
        [JsonPropertyName("dueAt")] public DateTimeOffset? DueAt { get; set; }
        [JsonPropertyName("timeZone")] public string TimeZone { get; set; } = "";
        [JsonPropertyName("delivered")] public bool Delivered { get; set; }
    }

    private static DateTimeOffset? ExtraerFecha(string text, DateTimeOffset ahora)
    {
        var en = Regex.Match(text, @"\b(?:en|dentro\s+de)\s+(?<n>\d+|cero|un|uno|una|dos|tres|cuatro|cinco|seis|siete|ocho|nueve|diez|once|doce|trece|catorce|quince|dieciséis|dieciseis|diecisiete|dieciocho|diecinueve|veinte|treinta|cuarenta|cincuenta|sesenta)\s+(?<unidad>minuto|minutos|hora|horas)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (en.Success && Numero(en.Groups["n"].Value) is int cantidad)
            return en.Groups["unidad"].Value.StartsWith("hora", StringComparison.OrdinalIgnoreCase)
                ? ahora.AddHours(cantidad)
                : ahora.AddMinutes(cantidad);

        var hora = Regex.Match(text, @"\ba\s+las\s+(?<h>\d{1,2})(?:[:.](?<m>\d{2}))?\s*(?<ampm>a\.?\s*m\.?|p\.?\s*m\.?)?(?=\s|$|[,.;])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.IgnorePatternWhitespace);
        if (!hora.Success || !int.TryParse(hora.Groups["h"].Value, out int hour)) return null;
        int minute = int.TryParse(hora.Groups["m"].Value, out int m) ? m : 0;
        if (hour > 23 || minute > 59) return null;
        string ampm = hora.Groups["ampm"].Value.Replace(".", "", StringComparison.Ordinal).ToLowerInvariant();
        if (ampm == "pm" && hour < 12) hour += 12;
        if (ampm == "am" && hour == 12) hour = 0;

        DateTime fecha = ahora.Date;
        if (text.Contains("mañana", StringComparison.OrdinalIgnoreCase)) fecha = fecha.AddDays(1);
        var local = new DateTimeOffset(fecha.AddHours(hour).AddMinutes(minute), ahora.Offset);
        return !text.Contains("mañana", StringComparison.OrdinalIgnoreCase) && local <= ahora
            ? local.AddDays(1)
            : local;
    }

    private static int? Numero(string valor)
    {
        if (int.TryParse(valor, out int numero)) return numero;
        return valor.ToLowerInvariant() switch
        {
            "cero" => 0, "un" or "uno" or "una" => 1, "dos" => 2, "tres" => 3,
            "cuatro" => 4, "cinco" => 5, "seis" => 6, "siete" => 7, "ocho" => 8,
            "nueve" => 9, "diez" => 10, "once" => 11, "doce" => 12, "trece" => 13,
            "catorce" => 14, "quince" => 15, "dieciséis" or "dieciseis" => 16,
            "diecisiete" => 17, "dieciocho" => 18, "diecinueve" => 19, "veinte" => 20,
            "treinta" => 30, "cuarenta" => 40, "cincuenta" => 50, "sesenta" => 60,
            _ => null
        };
    }
}
