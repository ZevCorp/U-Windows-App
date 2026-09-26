using System.Text.Json;

namespace U.WindowsClient.Clinical;

/// <summary>
/// UNA CONSULTA ABIERTA DESDE LA LISTA ENSEÑA LO QUE ENSEÑA EL PORTAL. Promesa 462 (spec 055).
/// </summary>
/// <remarks>
/// LAS DOS COPIAS DE UNA NOTA. Graph guarda `note_json` (con las claves del snapshot, lo único que
/// acepta `PUT /note`); el portal guarda su espejo en `consultations.note`. Las correcciones hechas en
/// el DETALLE de la web van solo al espejo (`providers.tsx`, `updateNote`), así que abrir en Windows la
/// nota de Graph enseñaba una versión vieja — y corregirla ahí pisaba lo que el médico había arreglado
/// en la web.
///
/// Por eso la nota se sigue leyendo de Graph (por las claves) y encima se ponen los textos del portal,
/// sección por sección y en el resumen. Lo que el portal no trae no se toca: vacío no es ausente
/// (patrón nº9). Las listas del portal (`kind: "lista"`, con `items`) se leen línea a línea.
/// </remarks>
public static class NotaDelPortal
{
    public static NotaClinica Fusionar(NotaClinica deGraph, JsonElement fila)
    {
        var nota = deGraph;
        if (fila.ValueKind != JsonValueKind.Object) return nota;

        if (fila.TryGetProperty("note", out var secciones) && secciones.ValueKind == JsonValueKind.Array)
        {
            foreach (var s in secciones.EnumerateArray())
            {
                string clave = Cad(s, "id");
                if (clave.Length == 0 || !nota.Secciones.Any(x => x.Clave == clave)) continue;
                string? texto = Texto(s);
                if (texto == null) continue;
                if (nota.Secciones.First(x => x.Clave == clave).Contenido != texto) nota = nota.ConSeccion(clave, texto);
            }
        }

        string resumen = Cad(fila, "resumen");
        if (resumen.Trim().Length > 0 && resumen != nota.Resumen) nota = nota.ConResumen(resumen);
        return nota;
    }

    /// <summary>El texto de una sección del portal: `texto`, o sus `items` uno por línea. Nulo si no trae ninguno.</summary>
    private static string? Texto(JsonElement s)
    {
        if (s.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array && items.GetArrayLength() > 0)
            return string.Join("\n", items.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString() ?? ""));
        return s.TryGetProperty("texto", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? "" : null;
    }

    private static string Cad(JsonElement o, string campo) =>
        o.TryGetProperty(campo, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
