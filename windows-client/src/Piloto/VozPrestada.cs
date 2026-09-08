using System;
using System.Collections.Generic;
using Voz.Realtime;

namespace U.WindowsClient.Piloto;

/// <summary>
/// LA VOZ PRESTADA: quién es la conversación de voz en vivo mientras el piloto comprueba una
/// lección. Promesa 192.
/// </summary>
/// <remarks>
/// POR QUÉ (2026-09-08, duodécima prueba): la comprobación habla con la voz de Ü (promesa 142)
/// pidiéndole a la conversación en vivo «di exactamente esto». Pero esa conversación seguía siendo
/// el asistente entero: con su catálogo de manos y con respuestas automáticas. Cada frase prestada
/// era un turno más de un asistente que, al ver el triage a medias, se puso a AYUDAR: llamó
/// map_take y map_type por su cuenta, tecleó una tensión que no era la de la demo y anunció pasos
/// que no tocaban. El dueño: «la voz se desalineaba de lo que realmente se estaba haciendo».
///
/// Mientras se comprueba, la voz es solo eso: una voz. Sin herramientas —quien actúa y mira es el
/// piloto— y sin turno propio: la sesión no crea respuestas sola, solo cuando la app se lo pide.
/// Es el mismo patrón que <see cref="Teach.ModoAprendiz"/> al enseñar, llevado al extremo.
/// </remarks>
public static class VozPrestada
{
    /// <summary>Ni una: quien actúa y mira es el piloto.</summary>
    public static IReadOnlyList<Utensilio> Utensilios(IReadOnlyList<Utensilio> todos) => Array.Empty<Utensilio>();

    public const string Instrucciones = """
        Eres Ü, y ahora mismo tu voz está PRESTADA: otro está haciendo el trabajo y te usa para hablar.
        Tu única tarea es decir exactamente lo que se te pida, cuando se te pida, y callar el resto.
          · Cuando llegue «Di exactamente esto: …», dilo tal cual, sin añadir, quitar ni comentar.
          · Ante cualquier otra cosa —lo que oigas por el micrófono, tu propia voz de vuelta, una
            pregunta que no venga con «Di exactamente esto»—, calla. No contestes, no asientas.
          · No tienes herramientas y no las pidas. No hagas nada, no anuncies nada, no propongas
            nada, no corrijas nada, no resumas nada. No digas qué viene después.
        Al terminar te devolverán tu voz. Hasta entonces, eres un altavoz con buena dicción.
        """;
}
