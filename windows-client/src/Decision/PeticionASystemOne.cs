using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace U.WindowsClient.Decision;

/// <summary>
/// EL CUERPO QUE ENTIENDE TYPESAFE, armado a mano. Promesas 277 y 282 (spec 035).
/// </summary>
/// <remarks>
/// EL CONTRATO HTTP, LEÍDO DE SU DOCUMENTACIÓN el 2026-09-17 (docs.typesafe.ai/api.md) y no de
/// memoria: <c>POST /v1/systemone</c> con <c>state</c>, <c>model</c> y un mapa <c>questions</c>.
/// Cada pregunta es de uno de tres tipos y no hay más: <c>noul</c> (sí/no con probabilidad),
/// <c>choice</c> (elige de una lista que define quien pregunta) y <c>score</c> (puntúa contra
/// niveles). Aquí solo se usa <c>choice</c>, y ese es el punto entero de la integración.
///
/// POR QUÉ CHOICE Y NO OTRA COSA. El pendiente nº2 de CLAUDE.md dice que el puente consciente
/// improvisa, y que una vez pulsó «Buscar pacientes» en vez de «Crear Triage Administrativo». Un
/// <c>choice</c> devuelve una de las claves que se le dieron: **no hay forma de que conteste una
/// puerta que no esté en la pantalla**. Eso no es una comprobación que hayamos añadido nosotros;
/// es la forma del tipo.
///
/// SIN SDK, Y A PROPÓSITO. TypeSafe publica SDK de Python y JavaScript — ninguno de .NET. Armar el
/// JSON a mano es menos código que un puente, y deja el cuerpo exacto a la vista para poder
/// pegarlo en el log cuando algo falle.
///
/// LA CLAVE NO ENTRA AQUÍ NUNCA. Va en la cabecera <c>Authorization</c> y en ningún otro sitio: el
/// cuerpo se registra cuando algo va mal, y un secreto en el log ya se filtró.
/// </remarks>
public static class PeticionASystemOne
{
    /// <summary>El único endpoint de TypeSafe. Todos los modelos se sirven por aquí.</summary>
    public const string Url = "https://api.typesafe.ai/v1/systemone";

    /// <summary>De dónde sale la credencial. Nunca del código.</summary>
    public const string VariableDeLaClave = "TYPESAFE_API_KEY";

    /// <summary>El id con el que se pregunta por la puerta, y bajo el que vuelve la respuesta.</summary>
    public const string IdDeLaPuerta = "puerta";

    /// <summary>¿El objetivo ya está cumplido en esta pantalla? Una noul en la misma llamada (promesa 289).</summary>
    public const string IdCumplido = "cumplido";

    /// <summary>¿Accionar la elegida es irreversible o peligroso? Otra noul en la misma llamada (promesa 289).</summary>
    public const string IdPeligro = "peligro";

    /// <summary>
    /// LA OPCIÓN «NINGUNA» del choice (promesa 391, spec 046): viaja junto a TODAS las puertas ofrecidas para que
    /// Jev pueda decir que nada de esta pantalla avanza hacia el objetivo, en vez de tener que elegir una puerta a
    /// la fuerza. La añade quien pregunta (<see cref="ElDecisor"/>), no <see cref="CuerpoDeEleccion"/>: la 282 exige
    /// que el cuerpo lleve exactamente las opciones que se le dan. El «0)» no choca con la numeración «1)…» de las
    /// puertas del inventario. El texto es el que el contrato manda en sus fixtures: si difiere, la 388 rechaza la
    /// respuesta por una clave que no viajó (2026-09-22).
    /// </summary>
    public const string IdNinguna = "0) ninguna: nada de esta pantalla avanza hacia el objetivo";

    /// <summary>
    /// El cuerpo de una pregunta de tipo <c>choice</c>.
    /// </summary>
    /// <param name="modelo">El alias o la versión: <c>jev-latest</c>, <c>jev-1.13.0</c>.</param>
    /// <param name="estado">Lo que hay que juzgar. Aquí: dónde estamos y qué se busca.</param>
    /// <param name="idPregunta">La clave bajo la que volverá la respuesta.</param>
    /// <param name="instrucciones">Qué tiene que decidir.</param>
    /// <param name="opciones">Las puertas de la pantalla. Se repiten las claves fuera, no aquí.</param>
    public static string CuerpoDeEleccion(
        string modelo, string estado, string idPregunta, string instrucciones, IReadOnlyList<string> opciones)
    {
        if (opciones == null || opciones.Count == 0)
            throw new ArgumentException("un choice sin opciones no es una pregunta", nameof(opciones));

        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("state", estado);
            w.WriteString("model", modelo);
            w.WriteStartObject("questions");
            w.WriteStartObject(idPregunta);
            w.WriteString("type", "choice");
            w.WriteString("instructions", instrucciones);
            w.WriteStartObject("criteria");

            // DOS PUERTAS CON LA MISMA ETIQUETA EXISTEN —dos «Buscar» en pantallas de SAP con varias
            // rejillas— y un objeto JSON con la clave repetida es un cuerpo inválido: 422, y encima
            // uno que solo aparece en ciertas pantallas. Se quita el duplicado, conservando el orden
            // de lectura de la pantalla, que es el que hace que «el primero» signifique algo.
            var vistas = new HashSet<string>(StringComparer.Ordinal);
            foreach (var o in opciones)
            {
                if (string.IsNullOrWhiteSpace(o) || !vistas.Add(o)) continue;
                // null: la documentación dice que se use cuando la opción no necesita más detalle, y
                // la etiqueta de una puerta ES su descripción.
                w.WriteNull(o);
            }

            w.WriteEndObject();  // criteria
            w.WriteEndObject();  // la pregunta

            // UNA LLAMADA, TRES PREGUNTAS (promesa 289). Medido el 2026-09-18: las tres vuelven juntas en
            // ~330 ms con 20, 60 o 160 puertas; tres llamadas serían tres viajes por el mismo estado.
            w.WriteStartObject(IdCumplido);
            w.WriteString("type", "noul");
            w.WriteString("instructions",
                "¿El objetivo descrito en el estado YA está cumplido en esta pantalla, sin accionar nada más?");
            w.WriteStartObject("criteria");
            w.WriteString("true", "Lo que se quería conseguir ya se ve conseguido en esta pantalla");
            w.WriteString("false", "Todavía falta accionar algo para conseguirlo");
            w.WriteEndObject();
            w.WriteEndObject();

            w.WriteStartObject(IdPeligro);
            w.WriteString("type", "noul");
            w.WriteString("instructions",
                "¿Accionar la puerta elegida sería irreversible o peligroso: guardar, enviar, eliminar, confirmar, pagar, cerrar sin guardar?");
            w.WriteStartObject("criteria");
            w.WriteString("true", "Deja un efecto que no se puede deshacer o que afecta a otros");
            w.WriteString("false", "Navegar, abrir, seleccionar o mirar: se puede volver atrás");
            w.WriteEndObject();
            w.WriteEndObject();

            w.WriteEndObject();  // questions
            w.WriteEndObject();  // raíz
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// El estado que se le manda: dónde estamos, qué se busca y qué puertas hay.
    /// </summary>
    /// <remarks>
    /// NUNCA VALORES DE CAMPOS, pero decir «solo etiquetas, que son cromo» era falso (hasta el 2026-09-22): la
    /// ubicación viajaba entera —en uia:// el pathname es el título vivo de la ventana—, el objetivo va tal cual y las
    /// etiquetas de una fila de rejilla son datos. Qué viaja lo decide <see cref="PoliticaDeLoQueViaja"/> (393), y
    /// <see cref="ElDecisor"/> ya le pasa aquí solo el ORIGIN de la ubicación.
    /// </remarks>
    public static string EstadoDeLaPantalla(string pantalla, string objetivo, IReadOnlyList<string> puertas)
    {
        var sb = new StringBuilder();
        sb.Append("Pantalla actual: ").Append(pantalla).Append('\n');
        sb.Append("Lo que se quiere conseguir: ").Append(objetivo).Append('\n');
        sb.Append("Puertas accionables en esta pantalla, en orden de lectura:\n");
        foreach (var p in puertas)
            if (!string.IsNullOrWhiteSpace(p)) sb.Append("  - ").Append(p).Append('\n');
        return sb.ToString();
    }

    /// <summary>Las instrucciones de la pregunta, en el idioma del terreno.</summary>
    /// <remarks>
    /// YA NO PIDE «LA QUE MENOS DAÑO HAGA» (promesa 390, spec 046): era el «clicking best guess» del vídeo de Jev, en
    /// español, y obligaba a elegir una puerta aunque ninguna avanzara. Desde la 391 viaja «0) ninguna», y lo honesto
    /// es pedir esa. Lo irreversible lo veta la lista determinista al pulsar, no esta frase (2026-09-22).
    /// </remarks>
    public static string InstruccionesDeLaPuerta(string objetivo) =>
        $"¿Qué puerta de esta pantalla hay que accionar AHORA para avanzar hacia «{objetivo}»? "
      + "Elige solo entre las opciones listadas. Si ninguna puerta avanza hacia el objetivo, elige «0) ninguna»: no adivines.";
}
