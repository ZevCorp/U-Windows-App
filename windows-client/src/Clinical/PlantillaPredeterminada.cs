using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using U.WindowsClient.Cuenta;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Clinical;

/// <summary>Un pin de «mi predeterminada»: qué plantilla fijó el médico, para qué especialidad y cuándo.</summary>
public sealed record Predeterminada(string Especialidad, string PlantillaId, string Actualizada);

/// <summary>Con qué se graba. Sin plantilla y sin pregunta: quien llama graba con la abierta.</summary>
public sealed record DecisionDePlantilla(PlantillaClinica? Plantilla, bool HayQuePreguntar);

/// <summary>
/// LA PLANTILLA CON LA QUE EMPIEZA LA CONSULTA: la predeterminada que elige el médico, con la misma
/// cadena que la web. Promesa 458 (spec 055); sustituye a la 440.
/// </summary>
/// <remarks>
/// LO PIDIÓ EL DUEÑO (2026-09-26): «que tanto en U como en Notes funcione con la lógica de que el
/// médico escoge la plantilla predeterminada». La regla de Jose (spec 053) caía a «la de urgencias»
/// porque nació para el hospital; ahora U y la web eligen igual.
///
/// PUERTO LÍNEA A LÍNEA de `pickPreselectedTemplate` (`lib/clinical/template-preferences.ts`). El
/// médico decide en Configuración de la web —o fijando la estrella aquí— qué pasa al empezar:
///
///   · «fixed» (el por defecto): manda su predeterminada, la más reciente si tiene varias.
///   · «last»: la última con la que de verdad grabó (en este equipo, como en la web el navegador).
///   · «manual»: ninguna; la elige cada vez.
///
/// Después, lo mismo para «fixed» y «last»: la sugerida institucional de su especialidad, y si no, la
/// primera de sus preferidas. Lo que añade Windows está en <see cref="ParaGrabar"/>: lo tocado en
/// esta consulta manda, «manual» pregunta, y si no hay NADA se graba con la abierta — grabar nunca se
/// queda sin poder arrancar.
/// </remarks>
public static class PlantillaPredeterminada
{
    /// <summary>`normalizeSpecialtyCode` de la web: «Medicina de Urgencias» → «medicina_de_urgencias».</summary>
    public static string NormalizarEspecialidad(string? valor)
    {
        string plano = ConceptosClinicos.SinTildes(valor ?? "").ToLowerInvariant();
        var sb = new StringBuilder(plano.Length);
        foreach (char c in plano)
        {
            bool vale = c is >= 'a' and <= 'z' or >= '0' and <= '9';
            if (vale) sb.Append(c);
            else if (sb.Length == 0 || sb[^1] != '_') sb.Append('_');
        }
        string fuera = sb.ToString().Trim('_');
        return fuera.Length > 80 ? fuera[..80] : fuera;
    }

    /// <summary>El id que preseleccionaría la web, o vacío.</summary>
    public static string Elegir(IReadOnlyList<PlantillaClinica> catalogo, IReadOnlyList<Predeterminada> preferencias,
        string? ultimaUsada, string? especialidad, string modo)
    {
        if (modo == "manual") return "";
        var activas = catalogo.Where(t => t.Estado != "archived").ToList();

        if (modo != "last")
        {
            // Stable, como el sort de la web: a igual fecha, el orden en que vinieron.
            foreach (var p in preferencias.OrderByDescending(p => p.Actualizada, StringComparer.Ordinal))
                if (activas.Any(t => t.Id == p.PlantillaId)) return p.PlantillaId;
        }

        if (!string.IsNullOrEmpty(ultimaUsada) && activas.Any(t => t.Id == ultimaUsada)) return ultimaUsada;

        var personales = activas.Where(t => t.Ambito == "personal").ToList();
        var institucionales = activas.Where(t => t.Ambito != "personal").ToList();
        string? buscada = string.IsNullOrEmpty(especialidad) ? null : NormalizarEspecialidad(especialidad);
        var deSuEspecialidad = buscada != null
            ? institucionales.Where(t => NormalizarEspecialidad(t.Especialidad) == buscada).ToList()
            : new List<PlantillaClinica>();
        var preferidas = buscada != null && deSuEspecialidad.Count > 0
            ? personales.Concat(deSuEspecialidad).ToList()
            : personales.Concat(institucionales).ToList();
        return preferidas.FirstOrDefault(t => t.EsLaPorDefecto)?.Id ?? preferidas.FirstOrDefault()?.Id ?? "";
    }

    /// <summary>
    /// Con qué se graba en Windows: lo tocado en ESTA consulta; si no, lo que elegiría la web; con
    /// «manual» y nada tocado, se pregunta; y sin nada, la abierta (la crea quien llama).
    /// </summary>
    public static DecisionDePlantilla ParaGrabar(IReadOnlyList<PlantillaClinica> catalogo,
        IReadOnlyList<Predeterminada> preferencias, string? ultimaUsada, string? especialidad, string modo,
        string? elegidaEnEstaConsulta)
    {
        if (!string.IsNullOrWhiteSpace(elegidaEnEstaConsulta)
            && catalogo.FirstOrDefault(t => t.Id == elegidaEnEstaConsulta) is { } tocada)
            return new DecisionDePlantilla(tocada, false);

        string id = Elegir(catalogo, preferencias, ultimaUsada, especialidad, modo);
        if (id.Length > 0 && catalogo.FirstOrDefault(t => t.Id == id) is { } elegida)
            return new DecisionDePlantilla(elegida, false);

        return new DecisionDePlantilla(null, modo == "manual");
    }
}

/// <summary>
/// LAS PREFERENCIAS DE PLANTILLA DEL MÉDICO, en las MISMAS tablas que la web: sus pines
/// (`user_template_preferences`) y su modo (`user_preferences.template_start_mode`). Promesa 459.
/// </summary>
/// <remarks>
/// FIJAR ES PEDIR QUE MANDE. En la web la estrella se guardaba y no pasaba nada si el médico estaba en
/// «la última que usé», que era el modo por defecto — arreglado allí el 2026-09-26. Aquí, fijar escribe
/// el pin Y deja el modo en «fixed», igual que ahora hace la web.
///
/// LA ÚLTIMA USADA VIVE EN ESTE EQUIPO, como en la web vive en el navegador: es una señal de uso, no
/// una decisión del médico. Su decisión es el pin, y ese sí viaja con la cuenta.
/// </remarks>
public static class PreferenciasDelMedico
{
    /// <summary>El modo que manda si el médico nunca tocó Configuración: el de la web desde el 2026-09-26.</summary>
    public const string ModoPorDefecto = "fixed";

    /// <summary>El cuerpo del upsert del modo. Sin user_id: la columna es <c>default auth.uid()</c>.</summary>
    public static string CuerpoDelModo(string modo) => new JsonObject { ["template_start_mode"] = modo }.ToJsonString();

    /// <summary>El modo y los pines del médico. Si algo no se puede leer, lo de por defecto: no tumba nada.</summary>
    public static async Task<(string Modo, IReadOnlyList<Predeterminada> Pines)> LeerAsync(SesionMiracle sesion,
        CancellationToken ct = default)
    {
        string modo = ModoPorDefecto;
        var pines = new List<Predeterminada>();
        try
        {
            var (http, cuerpo) = await sesion.RestAsync(HttpMethod.Get, "/rest/v1/user_preferences?select=template_start_mode", ct: ct);
            if (http is >= 200 and < 300)
            {
                using var doc = JsonDocument.Parse(cuerpo);
                if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0
                    && doc.RootElement[0].TryGetProperty("template_start_mode", out var m) && m.ValueKind == JsonValueKind.String
                    && m.GetString() is "fixed" or "last" or "manual")
                    modo = m.GetString()!;
            }

            var (http2, cuerpo2) = await sesion.RestAsync(HttpMethod.Get,
                "/rest/v1/user_template_preferences?select=specialty_code,template_id,updated_at&order=updated_at.desc", ct: ct);
            if (http2 is >= 200 and < 300)
            {
                using var doc = JsonDocument.Parse(cuerpo2);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    foreach (var f in doc.RootElement.EnumerateArray())
                        pines.Add(new Predeterminada(Cad(f, "specialty_code"), Cad(f, "template_id"), Cad(f, "updated_at")));
            }
            LogBus.Log("plantilla", $"modo «{modo}» · {pines.Count} predeterminada(s) fijada(s)");
        }
        catch (Exception e)
        {
            LogBus.Log("plantilla", $"no se pudieron leer las preferencias: {e.GetType().Name}: {e.Message}");
        }
        return (modo, pines);
    }

    /// <summary>
    /// Fija la plantilla como predeterminada: el pin en su especialidad y el modo en «fixed».
    /// </summary>
    public static async Task<bool> FijarPredeterminadaAsync(SesionMiracle sesion, string especialidad, string plantillaId,
        CancellationToken ct = default)
    {
        try
        {
            var (http, cuerpo) = await sesion.RestAsync(HttpMethod.Post,
                "/rest/v1/user_template_preferences?on_conflict=user_id,specialty_code",
                SugeridaDelMedico.Fila(especialidad, plantillaId), "resolution=merge-duplicates,return=minimal", ct);
            if (http is < 200 or >= 300)
            {
                LogBus.Log("plantilla", $"no se pudo fijar la predeterminada · HTTP {http} · {Recorte(cuerpo)}");
                return false;
            }
            var (http2, cuerpo2) = await sesion.RestAsync(HttpMethod.Post, "/rest/v1/user_preferences?on_conflict=user_id",
                CuerpoDelModo("fixed"), "resolution=merge-duplicates,return=minimal", ct);
            if (http2 is < 200 or >= 300)
                LogBus.Log("plantilla", $"la predeterminada quedó fijada, pero el modo no pasó a «fixed» · HTTP {http2} · {Recorte(cuerpo2)}");
            else
                LogBus.Log("plantilla", "predeterminada fijada; también manda en la web");
            return true;
        }
        catch (Exception e)
        {
            LogBus.Log("plantilla", $"no se pudo fijar la predeterminada: {e.GetType().Name}: {e.Message}");
            return false;
        }
    }

    /// <summary>La última plantilla con la que este médico grabó EN ESTE EQUIPO, o vacío.</summary>
    public static string UltimaUsada(string medicoId)
    {
        try
        {
            string ruta = RutaDeLaUltima(medicoId);
            return File.Exists(ruta) ? File.ReadAllText(ruta).Trim() : "";
        }
        catch (Exception e)
        {
            LogBus.Log("plantilla", $"no se pudo leer la última plantilla: {e.GetType().Name}: {e.Message}");
            return "";
        }
    }

    /// <summary>Anota con qué se grabó (solo al EMPEZAR a grabar, no al mirar el selector).</summary>
    public static void RecordarUltima(string medicoId, string plantillaId)
    {
        if (string.IsNullOrWhiteSpace(medicoId) || string.IsNullOrWhiteSpace(plantillaId)) return;
        try
        {
            string ruta = RutaDeLaUltima(medicoId);
            Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
            File.WriteAllText(ruta, plantillaId);
        }
        catch (Exception e)
        {
            LogBus.Log("plantilla", $"no se pudo recordar la última plantilla: {e.GetType().Name}: {e.Message}");
        }
    }

    private static string RutaDeLaUltima(string medicoId) =>
        Path.Combine(U.Graph.UserPaths.Roaming, "U", "plantillas", $"ultima-{PlantillaPredeterminada.NormalizarEspecialidad(medicoId)}.txt");

    private static string Cad(JsonElement o, string campo) =>
        o.TryGetProperty(campo, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static string Recorte(string s) => s.Length <= 200 ? s : s[..200];
}
