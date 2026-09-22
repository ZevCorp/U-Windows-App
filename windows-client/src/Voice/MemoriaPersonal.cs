using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

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
                return Task.FromResult(new Resultado(true, "Ya lo tenía guardado para nuestras próximas conversaciones.", "remember", existente.Id));

            var recuerdo = new Recuerdo
            {
                Id = $"local_{Guid.NewGuid():N}",
                UserId = _userId,
                Text = text,
                Kind = "fact",
                CreatedAt = DateTimeOffset.UtcNow,
            };
            documento.Items.Add(recuerdo);
            Escribir(documento);
            return Task.FromResult(new Resultado(true, "Lo recordaré para nuestras próximas conversaciones.", "remember", recuerdo.Id));
        }
    }

    public Task<string> ContextoAsync(CancellationToken ct, string query = "")
    {
        ct.ThrowIfCancellationRequested();
        lock (Candado)
        {
            var filtro = query.Trim();
            var recuerdos = Leer().Items
                .Where(x => x.UserId == _userId)
                .Where(x => filtro.Length == 0 || x.Text.Contains(filtro, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.CreatedAt)
                .Take(20)
                .Select(x => $"- [{x.Kind}] {x.Text}")
                .ToArray();
            return Task.FromResult(string.Join("\n", recuerdos));
        }
    }

    public readonly record struct Resultado(bool Ok, string Response, string Kind, string MemoryId);

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
    }
}
