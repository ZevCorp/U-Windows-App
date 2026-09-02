using System.Text.Json;

namespace U.WindowsClient.Workflows;

/// <summary>
/// CÓMO SE LLAMA UN WORKFLOW cuando el cerebro no lo bautizó. Promesa 108 (spec 007).
/// </summary>
/// <remarks>
/// Medido contra el Graph vivo el 2026-09-02: de cuatro workflows, tres se llamaban «Workflow sin
/// descripción» y el cuarto «User workflow summary:» — la primera línea del resumen del LLM, que
/// es un encabezado, no un nombre. Graph bautiza con la primera frase del summary al cerrar, y si
/// el cierre falla (504) se queda el relleno. El operador no podía distinguir uno de otro.
///
/// Lo que SÍ se sabe de cada workflow sin pedírselo a nadie: en qué app se grabó, con qué ventana
/// delante, cuándo y cuántos pasos tiene. Con eso se compone un nombre que se reconoce:
/// «Claude · 2 sep 13:42 · 6 pasos». Una descripción de verdad se respeta tal cual.
/// </remarks>
public static class NombreDeWorkflow
{
    private static readonly string[] Meses =
        { "ene", "feb", "mar", "abr", "may", "jun", "jul", "ago", "sep", "oct", "nov", "dic" };

    /// <summary>Cuánto de la ventana cabe en el nombre antes de estorbar en el carrusel.</summary>
    private const int MaxVentana = 28;

    public static string Derivar(JsonElement e)
    {
        string descripcion = (WorkflowSummary.Str(e, "description") ?? WorkflowSummary.Str(e, "title")
                              ?? WorkflowSummary.Str(e, "name") ?? "").Trim();
        if (!EsRelleno(descripcion)) return descripcion;

        string app = AppDe(WorkflowSummary.Str(e, "sourceOrigin") ?? WorkflowSummary.Str(e, "source_origin") ?? "");
        string ventana = (WorkflowSummary.Str(e, "sourceTitle") ?? WorkflowSummary.Str(e, "source_title") ?? "").Trim();
        int pasos = WorkflowSummary.Int(e, "totalSteps") ?? WorkflowSummary.Int(e, "stepCount") ?? 0;
        var creado = CreadoEn(e);

        var partes = new List<string>();
        if (app.Length > 0) partes.Add(app);
        // La ventana solo si añade algo: «Claude · Claude» no dice más que «Claude».
        if (ventana.Length > 0 && !string.Equals(ventana, app, StringComparison.OrdinalIgnoreCase))
            partes.Add(ventana.Length > MaxVentana ? ventana[..(MaxVentana - 1)].TrimEnd() + "…" : ventana);
        if (creado != default) partes.Add(FechaCorta(creado));
        partes.Add(pasos == 1 ? "1 paso" : $"{pasos} pasos");
        return string.Join(" · ", partes);
    }

    /// <summary>
    /// Lo que NO es un nombre: vacío, el relleno del grabador («Workflow sin descripción»), el de
    /// Graph («No description»), o un encabezado del LLM (termina en dos puntos, o empieza por
    /// «User workflow summary»).
    /// </summary>
    public static bool EsRelleno(string? descripcion)
    {
        string d = (descripcion ?? "").Trim();
        if (d.Length == 0) return true;
        if (d.Equals("Workflow sin descripción", StringComparison.OrdinalIgnoreCase)) return true;
        if (d.Equals("Workflow sin descripcion", StringComparison.OrdinalIgnoreCase)) return true;
        if (d.Equals("No description", StringComparison.OrdinalIgnoreCase)) return true;
        if (d.EndsWith(':')) return true;
        if (d.StartsWith("User workflow summary", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>«uia://claude.exe/…» → «Claude» · «sapgui://QAS/NWP1» → «SAP NWP1» · «https://mail.google.com» → «mail.google.com».</summary>
    public static string AppDe(string origin)
    {
        string s = (origin ?? "").Trim();
        if (s.Length == 0) return "";
        string esquema = "";
        int i = s.IndexOf("://", StringComparison.Ordinal);
        if (i >= 0) { esquema = s[..i].ToLowerInvariant(); s = s[(i + 3)..]; }
        var segmentos = s.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segmentos.Length == 0) return "";

        switch (esquema)
        {
            case "sapgui":
                return "SAP " + segmentos[^1];
            case "uia":
            {
                string proc = segmentos[0];
                if (proc.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) proc = proc[..^4];
                return proc.Length == 0 ? "" : char.ToUpperInvariant(proc[0]) + proc[1..];
            }
            default:
                return segmentos[0];
        }
    }

    /// <summary>
    /// El <c>createdAt</c> como llega de Graph: entero Neo4j <c>{low, high}</c> (lo normal), un
    /// número llano de milisegundos, o una fecha ISO. <c>default</c> si no hay nada legible.
    /// </summary>
    public static DateTimeOffset CreadoEn(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object) return default;
        if (!e.TryGetProperty("createdAt", out var v) && !e.TryGetProperty("created_at", out v)) return default;
        try
        {
            switch (v.ValueKind)
            {
                case JsonValueKind.Object when v.TryGetProperty("low", out var low) && v.TryGetProperty("high", out var high):
                {
                    // Neo4j parte el int64 en dos int32: el valor es high·2³² + low SIN signo.
                    long ms = ((long)high.GetInt32() << 32) | (uint)low.GetInt32();
                    return DateTimeOffset.FromUnixTimeMilliseconds(ms);
                }
                case JsonValueKind.Number:
                    return DateTimeOffset.FromUnixTimeMilliseconds(v.GetInt64());
                case JsonValueKind.String when DateTimeOffset.TryParse(v.GetString(), out var iso):
                    return iso;
            }
        }
        catch { }
        return default;
    }

    /// <summary>«2 sep 13:42», en hora local. Sin el año: un workflow de hace un año ya no es «el de ayer».</summary>
    public static string FechaCorta(DateTimeOffset cuando)
    {
        var local = cuando.ToLocalTime();
        return $"{local.Day} {Meses[local.Month - 1]} {local:HH:mm}";
    }
}
