using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using U.WindowsClient.Cuenta;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Clinical;

/// <summary>
/// EL ESPEJO EN `consultations`. Lo que hace que una consulta grabada en Windows se VEA en el
/// portal, y no solo exista.
/// </summary>
/// <remarks>
/// EL HUECO QUE TAPA, descubierto el 2026-09-01 mirando dónde lista el portal sus consultas. Hay
/// DOS tablas y no una:
///
///   · `clinical_encounters` — del backend. La escribe Graph con service-role; el cliente NUNCA la
///     toca directo (regla 10 del contrato clínico). Ahí vive la transcripción y la nota.
///   · `consultations` — del portal, con RLS por organización. Es la que alimenta la lista, el
///     detalle, el dashboard, la firma y el PDF.
///
/// El portal escribe LAS DOS: termina el encounter contra Graph y después espeja la fila con
/// `upsertConsultation(encounterToConsultation(...))` (en-vivo/page.tsx:655). Windows escribía solo
/// la primera, así que la consulta existía en la base y era invisible para el médico. «Misma base
/// de datos» era cierto de la mitad de abajo y falso justo de la que se mira.
///
/// EL MAPEO ES EL DEL PORTAL, campo por campo (lib/clinical/encounter-to-consultation.ts). No es
/// copiar por copiar: si aquí se inventara otro shape, la MISMA consulta se vería distinta según
/// por dónde se mirara — y eso es peor que no verse, porque nadie lo sospecharía. En particular las
/// secciones van como <c>id/titulo/kind/texto</c> y no como <c>key/label/content</c>: el detalle
/// del portal lee los primeros, y con los segundos pintaría secciones vacías sin dar ningún error.
///
/// LO QUE NO SE MANDA, y es a propósito: <c>organization_id</c> y <c>medico_id</c>. El portal
/// tampoco los manda — los pone la base desde <c>auth.uid()</c>. Mandarlos desde el cliente sería
/// dejar que el cliente diga de quién es una consulta.
/// </remarks>
public static class EspejoDeConsulta
{
    /// <summary>
    /// La fila `consultations` de una consulta terminada, en JSON. Pura y sin red: lo que se
    /// juzga en la promesa 93 es ESTO, porque un POST con la nota vacía contesta 201 igual.
    /// </summary>
    public static string Fila(string encounterId, NotaClinica nota, string verbatim,
        string plantilla, string especialidad, DateTimeOffset cuando)
    {
        var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();

            // EL MISMO ID QUE EL ENCOUNTER. Hace el puente 1:1 e idempotente —re-guardar actualiza,
            // no duplica— y navegable: /app/consultas/<encounter_id> del portal lleva aquí.
            w.WriteString("id", encounterId);
            w.WriteNull("patient_id");
            w.WriteString("servicio", "Consulta externa");
            w.WriteString("especialidad", NombreDeEspecialidad(especialidad));
            w.WriteString("tipo", "presencial");
            // Borrador: es lo que la mete en el ciclo de revisión y firma del portal. Nacer
            // aprobada sería firmar por el médico.
            w.WriteString("estado", "borrador");
            w.WriteString("motivo", Motivo(nota));
            w.WriteString("fecha", cuando.UtcDateTime.ToString("o"));
            w.WriteNumber("duracion_min", 0);
            w.WriteString("plantilla", plantilla);
            w.WriteString("resumen", nota.Resumen);

            // note: las secciones con los nombres de campo del PORTAL.
            w.WriteStartArray("note");
            foreach (var s in nota.Secciones)
            {
                w.WriteStartObject();
                w.WriteString("id", s.Clave);
                w.WriteString("titulo", s.Titulo);
                w.WriteString("kind", "texto");
                w.WriteString("texto", s.Contenido);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            // codigos: vacío. El backend todavía no genera CIE-10/CUPS y fabricarlos aquí sería
            // inventar codificación clínica.
            w.WriteStartArray("codigos");
            w.WriteEndArray();

            // transcript: un turno sin hablante con lo dicho tal cual. Vacío es VACÍO — sin nada
            // dicho no se fabrica un turno en blanco (aprendizaje nº9).
            w.WriteStartArray("transcript");
            string limpio = (verbatim ?? "").Trim();
            if (limpio.Length > 0)
            {
                w.WriteStartObject();
                w.WriteString("t", "");
                w.WriteString("texto", limpio);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteNull("firma");
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// LO QUE SE MANDA AL PORTAL CUANDO EL MÉDICO CORRIGE UNA NOTA YA GUARDADA. Promesa 193.
    /// </summary>
    /// <remarks>
    /// SOLO VIAJA LO QUE CAMBIÓ: el id que identifica la fila, la nota, el resumen y el motivo que
    /// sale de ella. Nada más.
    ///
    /// POR QUÉ NO SE REUTILIZA <see cref="Fila"/>. El upsert es `resolution=merge-duplicates`, o
    /// sea un UPDATE cuando la fila ya está, y PostgREST escribe **solo las columnas del cuerpo**.
    /// La fila del alta lleva `estado: "borrador"` y `firma: null`, así que usarla para corregir:
    ///
    ///   · Contra una consulta `aprobada` o `exportada` la para el trigger de la base
    ///     (`CONSULTATION_IMMUTABLE`, migración 20260721000000) — pero el `PUT /note` de Graph ya
    ///     habría pasado, y la MISMA consulta quedaría distinta según por dónde se mire, sin un
    ///     error a la vista del médico. Aprendizaje nº10.
    ///   · Contra una `revisada` no hay trigger que valga: la degradaría a borrador y se llevaría
    ///     por delante el ciclo de revisión del portal, en silencio.
    ///
    /// Y TAMPOCO VAN `plantilla`, `fecha`, `especialidad` NI `tipo`, que el alta sí manda: aquí no
    /// se conocen —una consulta abierta desde la lista se leyó del backend clínico, que no sabe de
    /// esas columnas— y mandarlos a medias los sobrescribiría con lo que hubiera a mano. Una caja
    /// que miente es peor que no tener caja (aprendizaje nº4).
    ///
    /// EL `transcript` TAMPOCO SE TOCA: es la evidencia de la que se derivó la nota, y corregir la
    /// redacción no cambia lo que se dijo.
    /// </remarks>
    public static string FilaDeCorreccion(string encounterId, NotaClinica nota)
    {
        var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("id", encounterId);
            w.WriteString("resumen", nota.Resumen);
            w.WriteString("motivo", Motivo(nota));

            w.WriteStartArray("note");
            foreach (var s in nota.Secciones)
            {
                w.WriteStartObject();
                w.WriteString("id", s.Clave);
                w.WriteString("titulo", s.Titulo);
                w.WriteString("kind", "texto");
                w.WriteString("texto", s.Contenido);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// Escribe el espejo en Supabase con el JWT del médico. La RLS decide: el cliente no dice de
    /// quién es la consulta, solo la escribe con su propia sesión.
    /// </summary>
    /// <returns>Si quedó escrita. Un fallo aquí NO tumba la consulta: la nota ya está a salvo en
    /// el backend, y lo que se pierde es la visibilidad — que se dice, no se traga.</returns>
    public static async Task<bool> EscribirAsync(SesionMiracle sesion, HttpClient http, string fila,
        CancellationToken ct = default)
    {
        string token = await sesion.TokenVigenteAsync(ct);
        if (token.Length == 0)
        {
            LogBus.Log("espejo", "sin sesión: la consulta no se pudo espejar al portal");
            return false;
        }

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post,
                $"{Nube.SupabaseUrl}/rest/v1/consultations?on_conflict=id")
            { Content = new StringContent(fila, Encoding.UTF8, "application/json") };
            req.Headers.Add("apikey", Nube.ClavePublicable);
            req.Headers.Add("Authorization", $"Bearer {token}");
            // resolution=merge-duplicates → es un UPSERT por id, como el del portal: volver a
            // terminar la misma consulta actualiza la fila en vez de reventar con un duplicado.
            req.Headers.Add("Prefer", "resolution=merge-duplicates,return=minimal");

            using var res = await http.SendAsync(req, ct);
            if (res.IsSuccessStatusCode)
            {
                LogBus.Log("espejo", "consulta espejada en el portal");
                return true;
            }

            // El cuerpo de PostgREST dice qué columna o qué política falló, y sin él «no se pudo»
            // manda a mirar la red cuando el problema es la RLS (aprendizaje nº2).
            string cuerpo = await res.Content.ReadAsStringAsync(ct);
            LogBus.Log("espejo",
                $"el portal no aceptó el espejo · HTTP {(int)res.StatusCode} · {Recortar(cuerpo)}");
            return false;
        }
        catch (Exception e)
        {
            LogBus.Log("espejo", $"no se pudo espejar: {e.GetType().Name}: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Las consultas anteriores de este médico, de la misma tabla que lee el portal. La RLS ya
    /// recorta a lo suyo: aquí no se filtra por médico, porque hacerlo sería una segunda opinión
    /// sobre quién ve qué.
    /// </summary>
    public static async Task<IReadOnlyList<ConsultaVista>> UltimasAsync(SesionMiracle sesion,
        HttpClient http, int cuantas = 25, CancellationToken ct = default)
    {
        // Por el camino de la sesión (spec 055): el mismo transporte, la clave y el token del médico.
        // `http` se queda en la firma por quien llama; el que manda es el de la sesión.
        var (codigo, cuerpo) = await sesion.RestAsync(HttpMethod.Get, $"{RutaDeLaLista}&limit={cuantas}", ct: ct);
        if (codigo == 401) return Array.Empty<ConsultaVista>();
        if (codigo is < 200 or >= 300)
        {
            LogBus.Log("espejo", $"no se pudieron leer las consultas · HTTP {codigo}");
            return Array.Empty<ConsultaVista>();
        }

        using var doc = JsonDocument.Parse(cuerpo);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return Array.Empty<ConsultaVista>();
        var lista = doc.RootElement.EnumerateArray().Select(Vista).ToList();
        LogBus.Log("espejo", $"{lista.Count} consulta(s) anteriores");
        return lista;
    }

    /// <summary>
    /// Lo que pide la lista: con DE QUIÉN es cada consulta (promesa 465, spec 055). El médico busca a
    /// su paciente, no un motivo; el portal lo enseña primero y Windows también. `paciente_nombre` lo
    /// llena un trigger de la base desde la nota; `patients(nombre)` es el del paciente asociado.
    /// </summary>
    public const string RutaDeLaLista =
        "/rest/v1/consultations?select=id,fecha,motivo,estado,resumen,plantilla,paciente_nombre,paciente_documento,patients(nombre,documento)"
        + "&order=fecha.desc";

    /// <summary>Una fila de `consultations` como se pinta en la lista.</summary>
    public static ConsultaVista Vista(JsonElement c)
    {
        var asociado = c.TryGetProperty("patients", out var p) && p.ValueKind == JsonValueKind.Object ? p : default;
        string nombre = Str(asociado, "nombre") is { Length: > 0 } n ? n : Str(c, "paciente_nombre");
        string documento = Str(asociado, "documento") is { Length: > 0 } d ? d : Str(c, "paciente_documento");
        return new ConsultaVista(
            Id: Str(c, "id"),
            Fecha: DateTimeOffset.TryParse(Str(c, "fecha"), out var f) ? f : DateTimeOffset.MinValue,
            Motivo: Str(c, "motivo"),
            Estado: Str(c, "estado"),
            Resumen: Str(c, "resumen"),
            Plantilla: Str(c, "plantilla"))
        { Paciente = nombre.Trim(), Documento = documento.Trim() };
    }

    /// <summary>
    /// La fila entera de una consulta —estado, resumen y nota del portal— para abrirla (promesa 462).
    /// </summary>
    public static async Task<JsonElement> LeerFilaAsync(SesionMiracle sesion, string id, CancellationToken ct = default)
    {
        try
        {
            var (codigo, cuerpo) = await sesion.RestAsync(HttpMethod.Get,
                $"/rest/v1/consultations?select=id,estado,resumen,note,patient_id&id=eq.{Uri.EscapeDataString(id)}", ct: ct);
            if (codigo is < 200 or >= 300) return default;
            using var doc = JsonDocument.Parse(cuerpo);
            return doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0
                ? doc.RootElement[0].Clone() : default;
        }
        catch (Exception e)
        {
            LogBus.Log("espejo", $"no se pudo leer la consulta del portal: {e.GetType().Name}: {e.Message}");
            return default;
        }
    }

    /// <summary>Motivo de consulta: de la sección «motivo…» si existe, si no del resumen.</summary>
    private static string Motivo(NotaClinica nota)
    {
        var motivo = nota.Secciones.FirstOrDefault(s =>
            s.Clave.Contains("motivo", StringComparison.OrdinalIgnoreCase)
            || s.Titulo.Contains("motivo", StringComparison.OrdinalIgnoreCase));
        string texto = (motivo?.Contenido ?? "").Trim();
        if (texto.Length == 0) texto = nota.Resumen.Trim();
        return texto.Length > 140 ? texto[..139] + "…" : texto;
    }

    /// <summary>`medicina_general` → «medicina general». El portal expone el nombre legible.</summary>
    private static string NombreDeEspecialidad(string code) =>
        string.IsNullOrWhiteSpace(code) ? "" : code.Replace('_', ' ');

    private static string Str(JsonElement o, string campo) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(campo, out var v)
        && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static string Recortar(string s) => s.Length <= 300 ? s : s[..300];
}

/// <summary>Una consulta anterior, con lo justo para pintarla en una lista.</summary>
public sealed record ConsultaVista(
    string Id, DateTimeOffset Fecha, string Motivo, string Estado, string Resumen, string Plantilla)
{
    /// <summary>De quién es: el nombre del paciente, o vacío si la consulta no lo dice.</summary>
    public string Paciente { get; init; } = "";

    /// <summary>Su documento, o vacío.</summary>
    public string Documento { get; init; } = "";
}
