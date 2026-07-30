using System.Text.Json;
using System.Text.Json.Serialization;

namespace U.Graph.NoteExport;

// Espejo del carril Operations de la cola de exportación de notas clínicas:
// POST /api/v1/operations/exports/claim y POST /api/v1/operations/exports/:id/result.
// El contrato completo vive en docs/note-export-contract.md del repo Graph, y el cliente de
// referencia (la especificación ejecutable) es scripts/simulate-operations-executor.js.
//
// La regla que gobierna todo el carril: una consulta pasa a `exportada` única y exclusivamente
// cuando el ejecutor confirma un éxito REAL. Encolar no es exportar; reclamar no es exportar;
// "no falló" no es exportar.

/// <summary>POST /api/v1/operations/exports/claim.</summary>
public sealed class ExportClaimRequest
{
    /// <summary>Identidad del ejecutor. Queda auditada en <c>claimed_by</c> y valida el lease al
    /// reportar: el mismo device que reclama tiene que reportar.</summary>
    [JsonPropertyName("device")] public string Device { get; set; } = "";
}

/// <summary>Respuesta 200 del claim. (El 204 —cola vacía— no trae cuerpo: el cliente devuelve null.)</summary>
public sealed class ExportJobResponse
{
    [JsonPropertyName("export")] public ExportJobInfo? Export { get; set; }
    [JsonPropertyName("payload")] public ExportPayload? Payload { get; set; }

    /// <summary>Plan resuelto server-side. Hoy Graph manda null y el ejecutor resuelve el suyo
    /// (GetPlanAsync con el workflow_id); si algún día llega, no rompe nada — se ignora.</summary>
    [JsonPropertyName("plan")] public JsonElement? Plan { get; set; }
}

public sealed class ExportJobInfo
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("workflow_id")] public string? WorkflowId { get; set; }
    [JsonPropertyName("attempts")] public int Attempts { get; set; }
    [JsonPropertyName("lease_expires_at")] public string LeaseExpiresAt { get; set; } = "";
}

/// <summary>
/// El snapshot congelado al crear el trabajo. Lleva el contenido clínico (es lo que hay que
/// escribir), pero <c>patient_ref</c> es un uuid — nunca nombre ni documento — y solo llegan
/// códigos con estado 'aceptado'. Nada de esto debe volver a Graph en códigos de error.
/// </summary>
public sealed class ExportPayload
{
    [JsonPropertyName("note")] public List<ExportNoteSection>? Note { get; set; }
    [JsonPropertyName("resumen")] public string? Resumen { get; set; }
    [JsonPropertyName("codigos")] public List<ExportCode>? Codigos { get; set; }
    [JsonPropertyName("firma")] public ExportSignature? Firma { get; set; }

    /// <summary>uuid del paciente en Miracle. NO es un identificador que el HIS reconozca — ver
    /// <see cref="U.Graph.PatientGuard"/> para por qué eso importa y qué se hace al respecto.</summary>
    [JsonPropertyName("patient_ref")] public string? PatientRef { get; set; }

    [JsonPropertyName("especialidad")] public string? Especialidad { get; set; }
    [JsonPropertyName("servicio")] public string? Servicio { get; set; }
    [JsonPropertyName("fecha")] public string? Fecha { get; set; }

    [JsonPropertyName("rendered_text")] public string? RenderedText { get; set; }
    [JsonPropertyName("context")] public string? Context { get; set; }
}

/// <summary>Una sección de la nota. <c>kind</c> es 'texto' (usa <c>texto</c>) o 'lista' (usa <c>items</c>).</summary>
public sealed class ExportNoteSection
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("titulo")] public string? Titulo { get; set; }
    [JsonPropertyName("kind")] public string? Kind { get; set; }
    [JsonPropertyName("texto")] public string? Texto { get; set; }
    [JsonPropertyName("items")] public List<string>? Items { get; set; }
}

/// <summary>Un código diagnóstico. Graph ya filtró: aquí solo llegan los de estado 'aceptado'.</summary>
public sealed class ExportCode
{
    [JsonPropertyName("sistema")] public string? Sistema { get; set; }
    [JsonPropertyName("codigo")] public string? Codigo { get; set; }
    [JsonPropertyName("descripcion")] public string? Descripcion { get; set; }
}

public sealed class ExportSignature
{
    [JsonPropertyName("por")] public string? Por { get; set; }
    [JsonPropertyName("fecha")] public string? Fecha { get; set; }
    [JsonPropertyName("hash")] public string? Hash { get; set; }
}

/// <summary>
/// POST /api/v1/operations/exports/:id/result.
///
/// <c>Outcome</c> es el vocabulario cerrado del contrato:
///   · <c>ok</c> — la acción de guardado se ejecutó Y se verificó la señal de éxito del HIS
///     (mensaje de la barra de estado / folio). Solo esto exporta la consulta.
///   · <c>needs_doctor</c> — faltan datos que el médico debe completar; las etiquetas van en
///     <c>UnresolvedFields</c> (una etiqueta de formulario no es PHI).
///   · <c>error</c> — falló; <c>ErrorCode</c> tipado, SIN PHI.
/// </summary>
public sealed class ExportResultRequest
{
    [JsonPropertyName("device")] public string Device { get; set; } = "";
    [JsonPropertyName("outcome")] public string Outcome { get; set; } = "";
    [JsonPropertyName("folio")] public string? Folio { get; set; }
    [JsonPropertyName("unresolved_fields")] public List<string>? UnresolvedFields { get; set; }
    [JsonPropertyName("error_code")] public string? ErrorCode { get; set; }
    [JsonPropertyName("detail_code")] public string? DetailCode { get; set; }
}

/// <summary>
/// El ack del result. <c>Idempotent</c> = Graph ya conocía este resultado (un reenvío tras un
/// timeout, por ejemplo): no re-transiciona ni duplica auditoría, y por eso reenviar es seguro.
/// </summary>
public sealed class ExportResultAck
{
    [JsonPropertyName("acknowledged")] public bool Acknowledged { get; set; }
    [JsonPropertyName("idempotent")] public bool Idempotent { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("consultation_exported")] public bool ConsultationExported { get; set; }
    [JsonPropertyName("export")] public JsonElement? Export { get; set; }
}
