using System.Text.Json;
using System.Text.Json.Serialization;

namespace U.Graph.Clinical;

// Espejo del carril clínico de Graph para aparatos: /api/v1/enroll,
// /api/v1/devices/pair-code y /api/clinical/* autenticado con el token
// per-install (requireClinicalActor: el aparato actúa EN NOMBRE del médico que
// lo vinculó desde Miracle Notes).
//
// La especificación ejecutable del carril es scripts/verify-clinical-actor.js
// del repo Graph, y el simulador local para probar este cliente sin backend es
// scripts/fake-graph-clinical.js de ESTE repo. El espejo se verifica con
// scripts/verify-clinical-contract-mirror.js — un contrato copiado a mano se
// desincroniza en silencio (aprendizaje nº16: System.Text.Json ignora campos
// desconocidos sin excepción ni warning).
//
// Regla que gobierna el carril: el aparato dicta, genera y ajusta BORRADORES.
// Firmar y exportar no existen aquí — son del médico, en el portal.

/// <summary>POST /api/v1/enroll → 201. El token llega UNA vez; guárdalo ya.</summary>
public sealed class EnrollResponse
{
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("token")] public string Token { get; set; } = "";
    [JsonPropertyName("device")] public EnrolledDevice? Device { get; set; }
}

public sealed class EnrolledDevice
{
    [JsonPropertyName("device_id")] public string DeviceId { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
}

/// <summary>POST /api/v1/devices/pair-code → 201. Se muestra en pantalla para que el médico lo canjee.</summary>
public sealed class PairCodeResponse
{
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("code")] public string Code { get; set; } = "";
    [JsonPropertyName("expires_at")] public string ExpiresAt { get; set; } = "";
}

/// <summary>GET /api/clinical/templates → { templates: [...] }.</summary>
public sealed class TemplatesResponse
{
    [JsonPropertyName("templates")] public List<ClinicalTemplateInfo> Templates { get; set; } = new();
}

public sealed class TemplateResponseEnvelope
{
    [JsonPropertyName("template")] public ClinicalTemplateInfo? Template { get; set; }
}

public sealed class ClinicalTemplateInfo
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("specialty")] public string Specialty { get; set; } = "";
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("owner_user_id")] public string? OwnerUserId { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("scope")] public string Scope { get; set; } = "";
    [JsonPropertyName("is_default")] public bool IsDefault { get; set; }
    [JsonPropertyName("sections_count")] public int SectionsCount { get; set; }
    [JsonPropertyName("sections")] public JsonElement? Sections { get; set; }
    [JsonPropertyName("created_at")] public string? CreatedAt { get; set; }
    [JsonPropertyName("updated_at")] public string? UpdatedAt { get; set; }
}

/// <summary>POST /api/clinical/encounters → 201 { encounter_id, status, template }.</summary>
public sealed class CreateEncounterResponse
{
    [JsonPropertyName("encounter_id")] public string EncounterId { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    /// <summary>El template_snapshot congelado en la consulta. Se modela para no descartarlo en silencio.</summary>
    [JsonPropertyName("template")] public JsonElement? Template { get; set; }
}

/// <summary>GET /api/clinical/encounters/:id → { encounter: {...} }.</summary>
public sealed class EncounterEnvelope
{
    [JsonPropertyName("encounter")] public ClinicalEncounterInfo? Encounter { get; set; }
}

public sealed class ClinicalEncounterInfo
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("patient_id")] public string? PatientId { get; set; }
    [JsonPropertyName("doctor_id")] public string? DoctorId { get; set; }
    [JsonPropertyName("consultation_type")] public string ConsultationType { get; set; } = "";
    [JsonPropertyName("template_id")] public string? TemplateId { get; set; }
    [JsonPropertyName("template_snapshot")] public JsonElement? TemplateSnapshot { get; set; }
    /// <summary>created | transcript_ready | note_generating | note_generated | completed | failed.</summary>
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("transcript")] public string Transcript { get; set; } = "";
    /// <summary>La nota estructurada ({ summary, sections: [{key,label,content}] }) o null.</summary>
    [JsonPropertyName("note_json")] public JsonElement? NoteJson { get; set; }
    [JsonPropertyName("created_at")] public string? CreatedAt { get; set; }
    [JsonPropertyName("updated_at")] public string? UpdatedAt { get; set; }
}

/// <summary>POST /api/clinical/encounters/:id/transcript → { encounter_id, status, transcript_length }.</summary>
public sealed class TranscriptSaveResponse
{
    [JsonPropertyName("encounter_id")] public string EncounterId { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("transcript_length")] public int TranscriptLength { get; set; }
}

/// <summary>
/// generate-note y PUT /note comparten forma. <c>mirror</c> SOLO llega en el carril
/// de aparatos tras PUT /note: dice si el historial del médico quedó refrescado y,
/// si no, por qué (web_edito = el médico editó en el portal y SU versión manda).
/// </summary>
public sealed class NoteResponse
{
    [JsonPropertyName("encounter_id")] public string EncounterId { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("note_json")] public JsonElement? NoteJson { get; set; }
    [JsonPropertyName("mirror")] public MirrorOutcome? Mirror { get; set; }
}

public sealed class MirrorOutcome
{
    [JsonPropertyName("refreshed")] public bool Refreshed { get; set; }
    [JsonPropertyName("reason")] public string? Reason { get; set; }
}

/// <summary>POST /api/clinical/assistant/note-adjustment → propuesta; NO persiste (se aplica con PUT /note).</summary>
public sealed class AdjustNoteResponse
{
    [JsonPropertyName("proposed_note_json")] public JsonElement? ProposedNoteJson { get; set; }
    [JsonPropertyName("changed_sections")] public List<string> ChangedSections { get; set; } = new();
    [JsonPropertyName("explanation")] public string Explanation { get; set; } = "";
    [JsonPropertyName("requires_physician_review")] public bool RequiresPhysicianReview { get; set; }
}

/// <summary>GET /api/clinical/consultations → listado MAGRO (sin cuerpo de nota, a propósito).</summary>
public sealed class ConsultationsResponse
{
    [JsonPropertyName("consultations")] public List<ConsultationSummary> Consultations { get; set; } = new();
}

public sealed class ConsultationSummary
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("fecha")] public string Fecha { get; set; } = "";
    [JsonPropertyName("estado")] public string Estado { get; set; } = "";
    [JsonPropertyName("servicio")] public string Servicio { get; set; } = "";
    [JsonPropertyName("especialidad")] public string Especialidad { get; set; } = "";
    [JsonPropertyName("plantilla")] public string Plantilla { get; set; } = "";
    [JsonPropertyName("motivo")] public string Motivo { get; set; } = "";
}
