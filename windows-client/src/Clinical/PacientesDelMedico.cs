using System.Net.Http;
using System.Text;
using System.Text.Json;
using U.WindowsClient.Cuenta;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Clinical;

/// <summary>Un paciente del médico, con lo que la consulta enseña a su lado.</summary>
public sealed record Paciente(string Id, string Nombre, string Documento, string Edad, string Sexo, string Eps,
    IReadOnlyList<string> Antecedentes, IReadOnlyList<string> Alergias, IReadOnlyList<string> Medicamentos);

/// <summary>
/// LOS PACIENTES DEL MÉDICO: buscar por nombre o documento en la MISMA tabla que la web. Promesa 464.
/// </summary>
/// <remarks>
/// Se busca en la base (`ilike`) y no en una lista cargada como hace la web con sus 500 últimos:
/// Windows no carga la lista entera de pacientes al abrir, y esa es parte de por qué abre rápido.
///
/// LO ESCRITO NO PUEDE CAMBIAR LA CONSULTA. PostgREST lee comas y paréntesis dentro de `or=(…)` como
/// sintaxis: un «Pérez, Juan» crudo añadiría una condición. Se quitan esos caracteres —y el comodín
/// `*` y las comillas— antes de montar la ruta; lo que queda se escapa como dato de URL.
/// </remarks>
public static class PacientesDelMedico
{
    private const string Columnas = "id,nombre,documento,edad,sexo,eps,antecedentes,alergias,medicamentos";

    /// <summary>La ruta de la búsqueda, o nulo si no queda nada que buscar.</summary>
    public static string? RutaDeBusqueda(string? texto)
    {
        var limpio = new StringBuilder();
        foreach (char c in (texto ?? "").Trim())
            if (c is not (',' or '(' or ')' or '*' or '"' or '\\' or '.' or ':')) limpio.Append(c);
        string t = string.Join(' ', limpio.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (t.Length == 0) return null;
        string v = Uri.EscapeDataString(t);
        return $"/rest/v1/patients?select={Columnas}&or=(nombre.ilike.*{v}*,documento.ilike.*{v}*)&order=nombre.asc&limit=8";
    }

    public static async Task<IReadOnlyList<Paciente>> BuscarAsync(SesionMiracle sesion, string texto, CancellationToken ct = default)
    {
        string? ruta = RutaDeBusqueda(texto);
        if (ruta == null) return Array.Empty<Paciente>();
        try
        {
            var (http, cuerpo) = await sesion.RestAsync(HttpMethod.Get, ruta, ct: ct);
            if (http is < 200 or >= 300)
            {
                LogBus.Log("pacientes", $"no se pudo buscar · HTTP {http}");
                return Array.Empty<Paciente>();
            }
            using var doc = JsonDocument.Parse(cuerpo);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return Array.Empty<Paciente>();
            // El texto clínico no va al log: solo cuántos.
            var lista = doc.RootElement.EnumerateArray().Select(Leer).ToList();
            LogBus.Log("pacientes", $"{lista.Count} paciente(s) encontrados");
            return lista;
        }
        catch (Exception e)
        {
            LogBus.Log("pacientes", $"no se pudo buscar: {e.GetType().Name}: {e.Message}");
            return Array.Empty<Paciente>();
        }
    }

    /// <summary>Un paciente por su id (el de una consulta abierta), o nulo.</summary>
    public static async Task<Paciente?> LeerAsync(SesionMiracle sesion, string id, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        try
        {
            var (http, cuerpo) = await sesion.RestAsync(HttpMethod.Get,
                $"/rest/v1/patients?select={Columnas}&id=eq.{Uri.EscapeDataString(id)}", ct: ct);
            if (http is < 200 or >= 300) return null;
            using var doc = JsonDocument.Parse(cuerpo);
            return doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0
                ? Leer(doc.RootElement[0]) : null;
        }
        catch (Exception e)
        {
            LogBus.Log("pacientes", $"no se pudo leer el paciente: {e.GetType().Name}: {e.Message}");
            return null;
        }
    }

    private static Paciente Leer(JsonElement f) => new(Cad(f, "id"), Cad(f, "nombre"), Cad(f, "documento"),
        f.TryGetProperty("edad", out var e) && e.ValueKind == JsonValueKind.Number ? e.GetRawText() : Cad(f, "edad"),
        Cad(f, "sexo"), Cad(f, "eps"), Lista(f, "antecedentes"), Lista(f, "alergias"), Lista(f, "medicamentos"));

    private static IReadOnlyList<string> Lista(JsonElement o, string campo) =>
        o.TryGetProperty(campo, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToList()
            : Array.Empty<string>();

    private static string Cad(JsonElement o, string campo) =>
        o.TryGetProperty(campo, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
