using System.Text.Json;
using System.Text.RegularExpressions;

namespace U.WindowsClient.Clinical;

/// <summary>Un aviso sobre la nota: qué pasa, en una línea, y cómo arreglarlo.</summary>
/// <param name="Severidad">«critico», «advertencia» o «sugerencia» — las palabras del portal.</param>
public sealed record Hallazgo(string Clave, string Severidad, string Titulo, string Detalle);

/// <summary>La revisión entera de una nota.</summary>
public sealed record Revision(IReadOnlyList<Hallazgo> Hallazgos, int Criticos, int Advertencias, int Sugerencias)
{
    public static readonly Revision Vacia = new(Array.Empty<Hallazgo>(), 0, 0, 0);
}

/// <summary>Lo que el panel enseña de entrada y lo que pliega tras «Ver más».</summary>
public sealed record Reparto(IReadOnlyList<Hallazgo> Principales, IReadOnlyList<Hallazgo> Plegados);

/// <summary>
/// LOS AVISOS AL TERMINAR DE GRABAR: qué falta, qué está mal y qué se puede mejorar en la nota,
/// EXACTAMENTE como los da la web. Promesa 450 (spec 055).
/// </summary>
/// <remarks>
/// LO PIDIÓ EL DUEÑO CON ESAS PALABRAS (2026-09-25): «conservar que cada que termina de grabar una
/// consulta aparezcan los avisos de qué cosas están mal o se pueden mejorar o qué faltó, que ya
/// aparece en Miracle Notes web».
///
/// PUERTO LÍNEA A LÍNEA de `lib/clinical/note-review.ts` (`reviewGeneratedNote`, `noteReviewScore`,
/// `splitReviewFindings`, `noteReviewLabel`). Determinista, local y sin IA, por la razón que la web
/// deja escrita: el aviso que dependía de lo que el modelo quisiera emitir salía a ratos, y «un aviso
/// intermitente se ignora». Antes de esto, Windows pintaba justo ese: los `warnings` del modelo.
///
/// NO SE REESCRIBEN LAS REGLAS, se copian — textos incluidos. Un médico que ve «Falta Antecedentes»
/// en el portal y «Sección vacía: antecedentes» en Windows ve dos productos. La promesa 450 compara
/// contra la salida real de la web, carácter a carácter, en 23 notas.
///
/// Trabaja sobre el `note_json` tal como llega (<see cref="NotaClinica.Crudo"/>) y no sobre
/// <see cref="NotaClinica"/>, porque las reglas miran lo que Windows no pinta: la confianza de cada
/// sección y el cierre (plan, medicamentos, alarma).
/// </remarks>
public static class RevisionDeLaNota
{
    private const double ConfianzaMinima = 0.5;
    private const int SeccionBreve = 25;
    private const int TranscripcionSustancial = 400;

    // Contenido que ocupa el campo sin documentar nada. Se exige que sea el contenido COMPLETO.
    private static readonly Regex Relleno = new(
        @"^(?:[-—.\s]*|n\/?a|no aplica|sin (?:informaci[oó]n|datos)(?:\s+documentad[ao]s?)?|no (?:se )?document(?:a|ó|o)|no disponible|pendiente)\.?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex Espacios = new(@"\s+");
    private static readonly Regex DeMuestra = new("patolog|citolog|histolog|laboratorio|bacteriolog");
    private static readonly Regex Alergia = new("alergi|al[eé]rgic", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Orden de LECTURA para quien está a punto de cerrar la nota. No es la severidad: una indicación
    /// sin dosis es riesgo directo para el paciente aunque «solo» sea advertencia.
    /// </summary>
    private static readonly Dictionary<string, int> PrioridadDeLectura = new(StringComparer.Ordinal)
    {
        ["medicacion-incompleta"] = 2,
        ["alergias-no-documentadas"] = 3,
        ["cierre-incompleto"] = 4,
        ["sin-resumen"] = 5,
        ["secciones-vacias"] = 6,
        ["confianza-baja"] = 7,
        ["secciones-breves"] = 8,
        ["sin-signos-vitales"] = 9,
    };

    /// <summary>
    /// Revisa la nota contra su plantilla congelada y lo que se habló.
    /// </summary>
    /// <param name="nota">El `note_json`. Nulo o ausente → sin hallazgos.</param>
    /// <param name="plantilla">El `template_snapshot` del encounter (de ahí salen las obligatorias).</param>
    /// <param name="transcripcion">Lo que se dijo; solo se mide y se busca en ello.</param>
    public static Revision Revisar(JsonElement nota, JsonElement plantilla, string? transcripcion)
    {
        if (nota.ValueKind != JsonValueKind.Object) return Revision.Vacia;

        var hallazgos = new List<Hallazgo>();
        var secciones = Arreglo(nota, "sections");
        string transcrito = (transcripcion ?? "").Trim();
        var cierre = Cierre(nota);
        var dePlantilla = plantilla.ValueKind == JsonValueKind.Object ? Arreglo(plantilla, "sections") : new List<JsonElement>();

        // ── secciones: obligatorias vs. opcionales ───────────────────────────
        // Mismo orden de inserción que el Set de la web: primero las de la plantilla, luego las del backend.
        var obligatorias = new List<string>();
        foreach (var s in dePlantilla)
            if (s.TryGetProperty("required", out var r) && r.ValueKind == JsonValueKind.True) Agregar(obligatorias, Cad(s, "key"));
        foreach (var k in Arreglo(nota, "missing_required_sections"))
            if (k.ValueKind == JsonValueKind.String) Agregar(obligatorias, k.GetString() ?? "");

        var faltantes = new List<(string Clave, string Etiqueta, bool Obligatoria)>();
        var breves = new List<string>();
        var dudosas = new List<string>();

        foreach (var s in secciones)
        {
            string clave = Cad(s, "key");
            string etiqueta = Recortado(CadONulo(s, "label")) is { Length: > 0 } l ? l : clave;
            string? contenidoCrudo = CadONulo(s, "content");
            if (Vacio(contenidoCrudo))
            {
                faltantes.Add((clave, etiqueta, obligatorias.Contains(clave)));
                continue;
            }
            string contenido = contenidoCrudo!.Trim();
            if (contenido.Length < SeccionBreve && !PareceDato(contenido)) breves.Add(etiqueta);
            if (s.TryGetProperty("confidence", out var c) && c.ValueKind == JsonValueKind.Number && c.GetDouble() < ConfianzaMinima)
                dudosas.Add(etiqueta);
        }

        var presentes = new HashSet<string>(secciones.Select(s => Cad(s, "key")), StringComparer.Ordinal);
        foreach (var clave in obligatorias)
        {
            if (presentes.Contains(clave)) continue;
            string etiqueta = dePlantilla.FirstOrDefault(s => Cad(s, "key") == clave) is { ValueKind: JsonValueKind.Object } p
                && Recortado(CadONulo(p, "label")) is { Length: > 0 } l ? l : clave;
            faltantes.Add((clave, etiqueta, true));
        }

        string? nombre = plantilla.ValueKind == JsonValueKind.Object ? Recortado(CadONulo(plantilla, "name")) : null;
        string laPlantilla = !string.IsNullOrEmpty(nombre) ? $"«{nombre}»" : "La plantilla";

        foreach (var falta in faltantes.Where(f => f.Obligatoria))
            hallazgos.Add(new Hallazgo($"falta-{falta.Clave}", "critico", $"Falta {falta.Etiqueta}",
                $"{laPlantilla} marca esta sección como obligatoria y quedó sin información. Complétala antes de cerrar la nota."));

        var opcionalesVacias = faltantes.Where(f => !f.Obligatoria).ToList();
        if (opcionalesVacias.Count > 0)
            hallazgos.Add(new Hallazgo("secciones-vacias", "advertencia",
                Plural(opcionalesVacias.Count, "sección sin información", "secciones sin información"),
                $"{laPlantilla} las incluye y quedaron vacías: {Unir(opcionalesVacias.Select(f => f.Etiqueta).ToList())}. Si no aplican, déjalo escrito."));

        if (Vacio(CadONulo(nota, "summary")))
            hallazgos.Add(new Hallazgo("sin-resumen", "advertencia", "Sin resumen clínico",
                "El resumen es lo primero que lee quien continúa el caso. Escríbelo o pídeselo al asistente."));

        // ── confianza y profundidad ──────────────────────────────────────────
        if (dudosas.Count > 0)
            hallazgos.Add(new Hallazgo("confianza-baja", "advertencia",
                Plural(dudosas.Count, "sección con baja confianza", "secciones con baja confianza"),
                $"La IA no quedó segura de lo que entendió en: {Unir(dudosas)}. Contrástalas con la transcripción."));

        if (breves.Count > 0 && transcrito.Length >= TranscripcionSustancial)
            hallazgos.Add(new Hallazgo("secciones-breves", "sugerencia",
                Plural(breves.Count, "sección muy breve", "secciones muy breves"),
                $"Frente a lo que se habló, quedó muy poco en: {Unir(breves)}. Amplía el detalle que respalde el diagnóstico."));

        // ── cierre: plan, seguimiento, alarma, recomendaciones ──────────────
        bool deMuestra = plantilla.ValueKind == JsonValueKind.Object
            && DeMuestra.IsMatch((CadONulo(plantilla, "specialty") ?? "").ToLowerInvariant());
        var meds = cierre.Medicamentos;

        if (!deMuestra)
        {
            bool planVacio = meds.Count == 0 && cierre.NoFarmacologico == 0 && cierre.Seguimiento == 0;
            var cierreFaltante = new List<string>();
            if (planVacio) cierreFaltante.Add("plan terapéutico");
            else if (cierre.Seguimiento == 0) cierreFaltante.Add("control o seguimiento");
            if (cierre.Alarma == 0) cierreFaltante.Add("signos de alarma");
            if (cierre.Recomendaciones == 0) cierreFaltante.Add("recomendaciones al paciente");

            if (cierreFaltante.Count > 0)
                hallazgos.Add(new Hallazgo("cierre-incompleto", planVacio ? "advertencia" : "sugerencia",
                    "Falta el cierre de la consulta",
                    $"No quedó registrado: {Unir(cierreFaltante)}. Es lo que el paciente se lleva y lo que respalda la atención si el cuadro empeora."));

            var incompletos = meds
                .Where(m => Vacio(CadONulo(m, "dose")) || Vacio(CadONulo(m, "frequency")))
                .Select(m => Recortado(CadONulo(m, "name")) is { Length: > 0 } n ? n : "medicamento sin nombre")
                .ToList();
            if (incompletos.Count > 0)
                hallazgos.Add(new Hallazgo("medicacion-incompleta", "advertencia",
                    Plural(incompletos.Count, "medicamento sin dosis o frecuencia", "medicamentos sin dosis o frecuencia"),
                    $"Completa dosis y frecuencia de: {Unir(incompletos)}. Sin eso la indicación no se puede cumplir."));
        }

        // ── lo que se suele olvidar preguntar ───────────────────────────────
        var comoSecciones = secciones.Select(s => new SeccionDeNota(Cad(s, "key"), Cad(s, "label"), CadONulo(s, "content") ?? "")).ToList();
        bool hayVitales = ConceptosClinicos.Extraer(comoSecciones).Keys.Any(k => k.StartsWith("vital.", StringComparison.Ordinal));
        if (!deMuestra && !hayVitales && transcrito.Length >= TranscripcionSustancial)
            hallazgos.Add(new Hallazgo("sin-signos-vitales", "sugerencia", "No quedaron signos vitales en la nota",
                "Si se tomaron, díctalos (TA, FC, FR, temperatura, saturación, peso o talla): también son los que alimentan el agente de escritorio."));

        if (!deMuestra && meds.Count > 0)
        {
            string textoNota = string.Join("\n", secciones.Select(s => CadONulo(s, "content") ?? ""));
            if (!Alergia.IsMatch($"{textoNota}\n{CadONulo(nota, "summary") ?? ""}\n{transcrito}"))
                hallazgos.Add(new Hallazgo("alergias-no-documentadas", "advertencia", "Se prescribió sin documentar alergias",
                    "No aparece ninguna mención a alergias en la consulta. Déjalo escrito aunque sea para negarlas."));
        }

        // ── lo que sí mandó el backend: contexto, al fondo ──────────────────
        int i = 0;
        foreach (var w in Arreglo(nota, "warnings"))
        {
            string aviso = w.ValueKind == JsonValueKind.String ? (w.GetString() ?? "").Trim() : "";
            if (aviso.Length == 0) continue;
            hallazgos.Add(new Hallazgo($"generacion-{i++}", "sugerencia", "Aviso de la generación", aviso));
        }

        // OrderBy es ESTABLE, como el sort de JavaScript desde ES2019: a igualdad, el orden de llegada.
        var ordenados = hallazgos
            .OrderBy(h => Prioridad(h.Clave))
            .ThenBy(h => Rango(h.Severidad))
            .ToList();

        return new Revision(ordenados,
            ordenados.Count(h => h.Severidad == "critico"),
            ordenados.Count(h => h.Severidad == "advertencia"),
            ordenados.Count(h => h.Severidad == "sugerencia"));
    }

    /// <summary>0-100 con la escala del historial: crítico 30, advertencia 12, sugerencia 5.</summary>
    public static int Puntaje(Revision revision)
    {
        int penalizacion = revision.Hallazgos.Sum(h => Penalizacion(h.Severidad));
        return Math.Max(0, Math.Min(100, 100 - penalizacion));
    }

    /// <summary>
    /// Arriba lo poco que hay que corregir; el resto a un clic. Un crítico NUNCA se pliega.
    /// </summary>
    public static Reparto Repartir(Revision revision, int visibles = 3)
    {
        var criticos = revision.Hallazgos.Where(h => h.Severidad == "critico").ToList();
        var resto = revision.Hallazgos.Where(h => h.Severidad != "critico").ToList();
        int cupo = Math.Max(0, visibles - criticos.Count);
        return new Reparto(criticos.Concat(resto.Take(cupo)).ToList(), resto.Skip(cupo).ToList());
    }

    /// <summary>«Todo en orden» | «1 crítico · 2 advertencias».</summary>
    public static string Etiqueta(Revision revision)
    {
        var partes = new List<string>();
        if (revision.Criticos > 0) partes.Add(Plural(revision.Criticos, "crítico", "críticos"));
        if (revision.Advertencias > 0) partes.Add(Plural(revision.Advertencias, "advertencia", "advertencias"));
        if (revision.Sugerencias > 0) partes.Add(Plural(revision.Sugerencias, "sugerencia", "sugerencias"));
        return partes.Count > 0 ? string.Join(" · ", partes) : "Todo en orden";
    }

    // ── piezas ───────────────────────────────────────────────────────────────

    private sealed record ElCierre(List<JsonElement> Medicamentos, int NoFarmacologico, int Seguimiento, int Recomendaciones, int Alarma);

    /// <summary>`normalizeDischarge`: una nota vieja sin cierre es un cierre vacío, no un error.</summary>
    private static ElCierre Cierre(JsonElement nota)
    {
        var d = nota.TryGetProperty("discharge", out var x) && x.ValueKind == JsonValueKind.Object ? x : default;
        var plan = d.ValueKind == JsonValueKind.Object && d.TryGetProperty("plan", out var p) && p.ValueKind == JsonValueKind.Object ? p : default;
        return new ElCierre(
            plan.ValueKind == JsonValueKind.Object ? Arreglo(plan, "medications") : new List<JsonElement>(),
            plan.ValueKind == JsonValueKind.Object ? Arreglo(plan, "non_pharmacological").Count : 0,
            plan.ValueKind == JsonValueKind.Object ? Arreglo(plan, "follow_up").Count : 0,
            d.ValueKind == JsonValueKind.Object ? Arreglo(d, "recommendations").Count : 0,
            d.ValueKind == JsonValueKind.Object ? Arreglo(d, "alarm_signs").Count : 0);
    }

    /// <summary>`vacio()` de la web: vacío, o relleno que ocupa el campo sin documentar nada.</summary>
    public static bool Vacio(string? valor)
    {
        string t = (valor ?? "").Trim();
        return t.Length == 0 || Relleno.IsMatch(t);
    }

    /// <summary>Un valor suelto («26-2513», una cédula) no es prosa truncada.</summary>
    private static bool PareceDato(string contenido) =>
        Espacios.Split(contenido.Trim()).Length < 3;

    private static string Plural(int n, string singular, string plural) => $"{n} {(n == 1 ? singular : plural)}";

    private static string Unir(IReadOnlyList<string> etiquetas, int max = 4) =>
        etiquetas.Count <= max
            ? string.Join(", ", etiquetas)
            : $"{string.Join(", ", etiquetas.Take(max))} y {etiquetas.Count - max} más";

    private static int Prioridad(string clave)
    {
        if (clave.StartsWith("falta-", StringComparison.Ordinal)) return 1;
        if (clave.StartsWith("generacion-", StringComparison.Ordinal)) return 90;
        return PrioridadDeLectura.TryGetValue(clave, out int p) ? p : 50;
    }

    private static int Rango(string severidad) => severidad switch { "critico" => 0, "advertencia" => 1, _ => 2 };

    private static int Penalizacion(string severidad) => severidad switch { "critico" => 30, "advertencia" => 12, _ => 5 };

    private static void Agregar(List<string> conjunto, string clave)
    {
        if (!conjunto.Contains(clave)) conjunto.Add(clave);
    }

    private static List<JsonElement> Arreglo(JsonElement o, string campo) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(campo, out var a) && a.ValueKind == JsonValueKind.Array
            ? a.EnumerateArray().ToList() : new List<JsonElement>();

    private static string Cad(JsonElement o, string campo) => CadONulo(o, campo) ?? "";

    private static string? CadONulo(JsonElement o, string campo) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(campo, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    private static string? Recortado(string? s) => s?.Trim();
}
