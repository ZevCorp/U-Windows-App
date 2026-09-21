using System.Text.Json.Serialization;
using U.WindowsClient.Backend;

namespace U.WindowsClient.Voice;

/// <summary>Puerta de voz a la memoria personal y a los recordatorios del backend.</summary>
public sealed class MemoriaPersonal
{
    private readonly BackendClient _backend;
    private readonly string _userId;
    private readonly string _timezone;

    public MemoriaPersonal(BackendClient backend, string userId)
    {
        _backend = backend;
        _userId = string.IsNullOrWhiteSpace(userId) ? "anon" : userId;
        _timezone = ZonaIanaLocal();
    }

    public async Task<Resultado> EjecutarAsync(string command, CancellationToken ct)
    {
        string text = command.Trim();
        if (text.Length == 0) return new(false, "Dime qué quieres que recuerde.", "", "");
        if (!text.StartsWith("recuerda", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("recuérdame", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("recuérdalo", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("acuérda", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("acuérdate", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("acuerda", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("no olvides", StringComparison.OrdinalIgnoreCase))
            text = "recuerda que " + text;

        var response = await _backend.PostAsync<Respuesta>("/memory", new
        {
            userId = _userId,
            command = text,
            timezone = _timezone,
            locale = "es-CO",
            clientNowUtc = DateTime.UtcNow.ToString("O"),
        }, ct);
        if (response == null) return new(false, "El backend no devolvió respuesta.", "", "");
        return new(response.Ok, response.Response ?? "No pude guardar ese recuerdo.", response.Kind ?? "", response.MemoryId ?? "");
    }

    public async Task<string> ContextoAsync(CancellationToken ct, string query = "")
    {
        string path = $"/memory?userId={Uri.EscapeDataString(_userId)}";
        if (!string.IsNullOrWhiteSpace(query)) path += $"&query={Uri.EscapeDataString(query.Trim())}";
        var snapshot = await _backend.GetAsync<Snapshot>(path, ct);
        if (snapshot?.Items == null && snapshot?.Reminders == null) return "";

        var lines = new List<string>();
        var items = (snapshot.Items ?? Array.Empty<Item>()).ToList();
        items.AddRange((snapshot.Memories ?? Array.Empty<Hit>()).Where(x => x.Item != null).Select(x => x.Item!));
        foreach (var item in items.Where(x => string.IsNullOrWhiteSpace(x.SupersededBy)).Take(20))
            lines.Add($"- [{item.Kind ?? "fact"}] {item.Text}");
        foreach (var reminder in (snapshot.Reminders ?? Array.Empty<Reminder>()).Where(x => x.Status is not "cancelled" and not "delivered").Take(10))
            lines.Add($"- [recordatorio] {reminder.Title} · {reminder.DueAt} · {reminder.Timezone}");
        return lines.Count == 0 ? "" : string.Join("\n", lines);
    }

    public readonly record struct Resultado(bool Ok, string Response, string Kind, string MemoryId);

    private sealed class Respuesta
    {
        [JsonPropertyName("ok")] public bool Ok { get; set; }
        [JsonPropertyName("response")] public string? Response { get; set; }
        [JsonPropertyName("kind")] public string? Kind { get; set; }
        [JsonPropertyName("memoryId")] public string? MemoryId { get; set; }
    }

    private sealed class Snapshot
    {
        [JsonPropertyName("items")] public Item[]? Items { get; set; }
        [JsonPropertyName("memories")] public Hit[]? Memories { get; set; }
        [JsonPropertyName("reminders")] public Reminder[]? Reminders { get; set; }
    }

    private sealed class Hit
    {
        [JsonPropertyName("item")] public Item? Item { get; set; }
    }

    private sealed class Item
    {
        [JsonPropertyName("kind")] public string? Kind { get; set; }
        [JsonPropertyName("text")] public string? Text { get; set; }
        [JsonPropertyName("supersededBy")] public string? SupersededBy { get; set; }
    }

    private sealed class Reminder
    {
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("dueAt")] public string? DueAt { get; set; }
        [JsonPropertyName("timezone")] public string? Timezone { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
    }

    private static string ZonaIanaLocal()
    {
        try
        {
            if (TimeZoneInfo.TryConvertWindowsIdToIanaId(TimeZoneInfo.Local.Id, out var iana)
                && !string.IsNullOrWhiteSpace(iana)) return iana;
            return TimeZoneInfo.Local.Id;
        }
        catch { return "UTC"; }
    }
}
