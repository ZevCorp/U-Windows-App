using System.Text.Json;
using System.Text.Json.Nodes;

namespace U.WindowsClient.Clinical;

/// <summary>
/// Qué hacer con lo que el médico dijo a una sección.
/// </summary>
/// <param name="TextoLocal">Si no es nulo: el nuevo contenido, ya aplicado aquí, SIN red (un literal).</param>
/// <param name="Instruccion">Lo que viaja a <c>note-adjustment</c> si hay que preguntarle al asistente.</param>
/// <param name="Seccion">La clave de la sección, o nulo para la nota entera (el resumen no tiene clave).</param>
/// <param name="Tipo">«literal», «dictation» o «rewrite».</param>
public sealed record PedidoDeVoz(string? TextoLocal, string Instruccion, string? Seccion, string Tipo);

/// <summary>Lo que volvió de un ajuste: la nota propuesta, qué secciones cambió y qué se le dice al médico.</summary>
public sealed record Propuesta(NotaClinica Nota, IReadOnlyList<string> Cambiadas, string Explicacion);

/// <summary>
/// AJUSTAR LA NOTA —escribiendo o en voz alta— como la web. Promesas 453 y 454 (spec 055).
/// </summary>
/// <remarks>
/// LA PROPUESTA NO SE GUARDA SOLA. El backend calcula el ajuste sobre la última nota GUARDADA y
/// devuelve una propuesta; la web la deja «sin guardar» hasta que el médico guarda. Windows hace lo
/// mismo con un gesto menos: las secciones cambiadas se marcan y una sola banda ofrece Guardar
/// (Ctrl+S) o Descartar. Lo que no puede pasar es que la IA escriba en la historia clínica sin que
/// nadie lo apruebe.
///
/// Y SE MIRA QUÉ CAMBIÓ DE VERDAD (`changed_sections`), como en la web: una instrucción que el modelo
/// se negó a aplicar se anunciaba igual que una aplicada, y el micrófono parecía roto.
/// </remarks>
public static class AjusteDeLaNota
{
    /// <summary>
    /// Decide qué hacer con lo dicho a una sección. Nulo si no se dijo nada utilizable.
    /// </summary>
    /// <param name="clave">La clave de la sección; vacía para el resumen.</param>
    /// <param name="titulo">Cómo se llama la sección en pantalla.</param>
    /// <param name="contenidoActual">Lo que tiene ahora (para aplicar un literal).</param>
    public static PedidoDeVoz? PorVoz(string dicho, string clave, string titulo, string contenidoActual)
    {
        var intencion = InstruccionDeVoz.Interpretar(dicho);
        if (intencion == null) return null;

        if (intencion.Modo == "literal")
            return new PedidoDeVoz(InstruccionDeVoz.AplicarLiteral(contenidoActual, intencion.Texto), "",
                clave.Length > 0 ? clave : null, "literal");

        // El dictado exige una sección concreta: sin ella (el resumen) viaja como instrucción y el
        // modelo decide. Es exactamente la regla de la web (`esDictado`).
        bool esDictado = intencion.Modo == "dictado" && clave.Length > 0;
        if (esDictado) return new PedidoDeVoz(null, intencion.Texto, clave, "dictation");

        string pedido = intencion.Modo == "dictado" ? $"agrega que {intencion.Texto}" : intencion.Texto;
        return new PedidoDeVoz(null,
            $"En la sección \"{titulo}\", aplica esta instrucción dictada por el médico: \"{pedido}\". "
            + "Modifica únicamente lo necesario para cumplirla y conserva el resto de la nota.",
            clave.Length > 0 ? clave : null, "rewrite");
    }

    /// <summary>
    /// El cuerpo de <c>POST /api/clinical/assistant/note-adjustment</c>. Sin sección = la nota entera.
    /// </summary>
    public static string Cuerpo(string encounterId, string instruccion, string? seccion, string tipo)
    {
        var o = new JsonObject
        {
            ["encounter_id"] = encounterId,
            ["instruction"] = instruccion.Length > 2000 ? instruccion[..2000] : instruccion,
        };
        if (!string.IsNullOrWhiteSpace(seccion)) o["section_key"] = seccion;
        o["instruction_kind"] = tipo;
        return o.ToJsonString();
    }

    /// <summary>
    /// Aplica la respuesta del asistente sobre la nota que se ve. Si no cambió ninguna sección, la nota
    /// queda como estaba y se dice por qué — con la salida que da la web: dictarlo como literal.
    /// </summary>
    public static Propuesta Aplicar(NotaClinica actual, JsonElement respuesta)
    {
        var cambiadas = respuesta.ValueKind == JsonValueKind.Object
            && respuesta.TryGetProperty("changed_sections", out var c) && c.ValueKind == JsonValueKind.Array
                ? c.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString() ?? "")
                    .Where(x => x.Length > 0).ToList()
                : new List<string>();
        string explicacion = respuesta.ValueKind == JsonValueKind.Object
            && respuesta.TryGetProperty("explanation", out var e) && e.ValueKind == JsonValueKind.String
                ? (e.GetString() ?? "").Trim() : "";

        if (cambiadas.Count == 0)
            return new Propuesta(actual, cambiadas,
                $"{(explicacion.Length > 0 ? explicacion : "No se aplicó ningún cambio.")} "
                + "Si quieres que quede escrito tal cual, díctalo empezando por «quiero que diga».");

        var propuesta = respuesta.TryGetProperty("proposed_note_json", out var n) && n.ValueKind == JsonValueKind.Object
            ? NotaClinica.Leer(n) : actual;
        return new Propuesta(propuesta, cambiadas, explicacion.Length > 0 ? explicacion : "Ajuste aplicado.");
    }

    /// <summary>
    /// La propuesta de un literal: la misma nota con UNA sección (o el resumen) cambiada, aplicada
    /// aquí. Mismo camino que una propuesta del asistente para que se acepte con el mismo gesto.
    /// </summary>
    public static Propuesta Literal(NotaClinica actual, string clave, string titulo, string nuevo) =>
        new(clave.Length > 0 ? actual.ConSeccion(clave, nuevo) : actual.ConResumen(nuevo),
            new[] { clave.Length > 0 ? clave : "summary" }, $"Se escribió tal cual en «{titulo}».");
}
