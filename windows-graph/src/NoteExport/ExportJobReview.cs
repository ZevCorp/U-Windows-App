namespace U.Graph.NoteExport;

/// <summary>El veredicto previo a tocar SAP, con lo que una persona debería saber antes de aprobar.</summary>
public sealed record JobReview(ExportDecision Decision, IReadOnlyList<string> Warnings)
{
    public bool CanProceed => Decision.Verdict == ExportVerdict.Proceed;
}

/// <summary>
/// Revisa un trabajo reclamado ANTES de tocar SAP: que el payload sirva para escribir algo, que no
/// sea la repetición de una ejecución que quedó a medias, y qué avisos merece ver quien aprueba.
///
/// Pura y sin dependencias de Windows, COM ni red: es la parte del ejecutor que se puede probar
/// entera. El orden de las comprobaciones es deliberado — primero lo que impide ejecutar con
/// seguridad, después lo que solo merece un aviso.
/// </summary>
public static class ExportJobReview
{
    /// <summary>Margen de lease por debajo del cual avisamos de que puede no dar tiempo.</summary>
    public const int LeaseWarningSeconds = 60;

    public static JobReview Review(ExportJobResponse? job, ExportRecord? record, DateTimeOffset now)
    {
        var warnings = new List<string>();
        ExportJobInfo? export = job?.Export;

        if (export == null || string.IsNullOrWhiteSpace(export.Id))
        {
            return new JobReview(
                ExportDecision.Fail("TRABAJO_SIN_ID", "Graph devolvió un trabajo sin identificador."),
                warnings);
        }

        // ── 1. Una ejecución anterior quedó en estado desconocido ────────────────
        // Esta es la comprobación que impide el peor desenlace de todo el ejecutor: escribir dos
        // veces la misma nota en la historia de un paciente. Si una corrida previa tocó SAP y no
        // llegó a saber cómo terminó, NADIE puede afirmar desde aquí si la nota está o no está.
        // Ejecutar «por si acaso» es apostar; se para y lo resuelve una persona mirando SAP.
        if (record is { Uncertain: true })
        {
            return new JobReview(
                ExportDecision.NeedsDoctor(
                    $"Una ejecución anterior de este trabajo quedó sin desenlace conocido ({record.UncertainNote}). " +
                    "No se vuelve a ejecutar: hay que comprobar en el sistema hospitalario si la nota ya quedó " +
                    "registrada antes de reintentar.",
                    "Verificar si la nota ya se registró"),
                warnings);
        }

        // ── 2. El trabajo tiene que ser ejecutable ───────────────────────────────
        if (string.IsNullOrWhiteSpace(export.WorkflowId))
        {
            return new JobReview(
                ExportDecision.Fail("SIN_WORKFLOW",
                    "El trabajo no trae workflow de automatización. Falta configurar " +
                    "GRAPH_NOTE_EXPORT_WORKFLOW_ID en Graph."),
                warnings);
        }

        ExportPayload? payload = job!.Payload;
        if (payload == null)
        {
            return new JobReview(
                ExportDecision.Fail("PAYLOAD_AUSENTE", "El trabajo llegó sin contenido clínico."),
                warnings);
        }

        // Un payload sin nada que escribir no puede terminar en «exportada»: escribir cero campos y
        // reportar éxito sería la definición exacta de un éxito falso. (Pasa de verdad: el payload se
        // purga a las 72 h del estado terminal, así que un trabajo viejo puede llegar vacío.)
        bool hasText = !string.IsNullOrWhiteSpace(payload.RenderedText) ||
                       !string.IsNullOrWhiteSpace(payload.Context);
        bool hasSections = payload.Note is { Count: > 0 };
        if (!hasText && !hasSections)
        {
            return new JobReview(
                ExportDecision.Fail("PAYLOAD_VACIO",
                    "El trabajo no trae texto clínico que escribir (¿se purgó el contenido por antigüedad?)."),
                warnings);
        }

        // La firma es lo que hace legítima a esta nota. Graph ya re-verificó su hash contra la fila de
        // la base antes de encolar —este cliente no puede repetir esa verificación, no tiene la fila—,
        // pero que llegue SIN firma significa que algo se saltó ese control aguas arriba.
        if (string.IsNullOrWhiteSpace(payload.Firma?.Hash))
        {
            return new JobReview(
                ExportDecision.Fail("SIN_FIRMA",
                    "El contenido llega sin hash de firma. No se escribe en una historia clínica algo " +
                    "cuya firma no consta."),
                warnings);
        }

        // ── 3. Avisos: no impiden ejecutar, pero quien aprueba debe verlos ───────

        // `attempts` llega YA incrementado: la primera entrega de un trabajo vale 1. Un valor mayor
        // significa que hubo un claim anterior, y desde aquí NO se puede distinguir cuál de los dos
        // casos fue: un reintento legítimo tras un error ya reportado (inofensivo), o un intento que
        // escribió en SAP y murió sin reportar (peligroso). Si el diario local lo explica, se calla;
        // si no —otro equipo, o disco limpio—, se avisa y decide la persona.
        if (export.Attempts > 1 && record == null)
        {
            warnings.Add($"Es el intento nº {export.Attempts} de este trabajo y este equipo no tiene " +
                         "registro de los anteriores. Puede que otro equipo ya lo haya escrito: " +
                         "comprueba en el sistema hospitalario antes de aprobar.");
        }

        // El lease lo sella el reloj de Postgres y aquí se compara con el del PC, que puede ir
        // desfasado. Por eso es un AVISO y nunca un bloqueo: parar una exportación buena por un reloj
        // mal puesto sería peor que el problema que evita. Si de verdad venció, el reporte recibirá
        // un 409 y eso sí es una respuesta fiable del servidor.
        if (DateTimeOffset.TryParse(export.LeaseExpiresAt, out DateTimeOffset lease))
        {
            double left = (lease - now).TotalSeconds;
            if (left <= 0)
            {
                warnings.Add("El plazo (lease) de este trabajo ya figura vencido según el reloj de este " +
                             "equipo. Graph puede rechazar el resultado y volver a servir el trabajo.");
            }
            else if (left < LeaseWarningSeconds)
            {
                warnings.Add($"Quedan unos {(int)left} s de plazo para este trabajo; si la escritura " +
                             "tarda más, Graph rechazará el resultado.");
            }
        }

        return new JobReview(ExportDecision.Proceed(), warnings);
    }

    /// <summary>
    /// Lo que se le enseña a quien aprueba: metadatos de la consulta, nunca la nota entera. El texto
    /// clínico se muestra aparte y bajo su propia decisión de la interfaz — aquí solo va lo que
    /// permite reconocer DE QUÉ consulta se trata.
    /// </summary>
    public static IReadOnlyList<(string Label, string Value)> Summarize(ExportPayload payload)
    {
        var rows = new List<(string, string)>();
        void Add(string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) rows.Add((label, value!.Trim()));
        }

        Add("Fecha de la consulta", payload.Fecha);
        Add("Servicio", payload.Servicio);
        Add("Especialidad", payload.Especialidad);
        Add("Firmada por", payload.Firma?.Por);
        Add("Fecha de firma", payload.Firma?.Fecha);
        // El uuid del paciente no dice quién es sin la base de datos de Miracle, pero permite
        // correlacionar este trabajo con la consulta del portal si hay que auditarlo.
        if (!string.IsNullOrWhiteSpace(payload.PatientRef))
            rows.Add(("Referencia de paciente", Shorten(payload.PatientRef!)));
        if (payload.Codigos is { Count: > 0 })
            rows.Add(("Códigos", string.Join(", ", payload.Codigos.Select(c => c.Codigo).Where(c => !string.IsNullOrWhiteSpace(c)))));

        return rows;
    }

    private static string Shorten(string value) =>
        value.Length <= 12 ? value : value[..8] + "…" + value[^4..];
}
