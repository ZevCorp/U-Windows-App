using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace U.WindowsClient.Voice;

/// <summary>Hilo conversacional durable que une las sesiones de voz del mismo usuario.</summary>
public sealed class ConversacionPersonal
{
    private static readonly object Candado = new();
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private const int MaxTurnos = 160;
    private const int MaxTextoPorTurno = 4000;

    private readonly string _userId;
    private readonly string _archivo;

    public ConversacionPersonal(string userId, string? archivo = null)
    {
        _userId = string.IsNullOrWhiteSpace(userId) ? "anon" : userId.Trim();
        _archivo = archivo ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "U", "conversacion-personal.json");
    }

    public void Agregar(string quien, string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return;
        string rol = quien.Trim().ToLowerInvariant() is "usuario" or "asistente" ? quien.Trim().ToLowerInvariant() : "asistente";
        string limpio = texto.Trim();
        if (limpio.Length > MaxTextoPorTurno) limpio = limpio[..MaxTextoPorTurno];

        lock (Candado)
        {
            var documento = Leer();
            documento.Turnos.Add(new Turno
            {
                UserId = _userId,
                Role = rol,
                Text = limpio,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            var propios = documento.Turnos.Where(x => x.UserId == _userId).ToList();
            if (propios.Count > MaxTurnos)
            {
                var quitar = propios.Take(propios.Count - MaxTurnos).ToHashSet();
                documento.Turnos.RemoveAll(x => quitar.Contains(x));
            }
            Escribir(documento);
        }
    }

    public string Contexto(int maxTurnos = 28, int maxCaracteres = 9000)
    {
        lock (Candado)
        {
            var turnos = Leer().Turnos
                .Where(x => x.UserId == _userId)
                .OrderByDescending(x => x.CreatedAt)
                .Take(Math.Max(1, maxTurnos))
                .Reverse()
                .Select(x => $"- {x.Role}: {x.Text}")
                .ToList();
            string contexto = string.Join("\n", turnos);
            if (contexto.Length <= maxCaracteres) return contexto;
            return contexto[^maxCaracteres..];
        }
    }

    private Documento Leer()
    {
        if (!File.Exists(_archivo)) return new Documento();
        try
        {
            string json = File.ReadAllText(_archivo);
            return JsonSerializer.Deserialize<Documento>(json, Json) ?? new Documento();
        }
        catch (IOException) { return new Documento(); }
        catch (JsonException) { return new Documento(); }
    }

    private void Escribir(Documento documento)
    {
        string? carpeta = Path.GetDirectoryName(_archivo);
        if (!string.IsNullOrWhiteSpace(carpeta)) Directory.CreateDirectory(carpeta);
        string temporal = _archivo + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporal, JsonSerializer.Serialize(documento, Json));
        File.Move(temporal, _archivo, overwrite: true);
    }

    private sealed class Documento
    {
        [JsonPropertyName("turnos")] public List<Turno> Turnos { get; set; } = new();
    }

    private sealed class Turno
    {
        [JsonPropertyName("userId")] public string UserId { get; set; } = "";
        [JsonPropertyName("role")] public string Role { get; set; } = "asistente";
        [JsonPropertyName("text")] public string Text { get; set; } = "";
        [JsonPropertyName("createdAt")] public DateTimeOffset CreatedAt { get; set; }
    }
}
