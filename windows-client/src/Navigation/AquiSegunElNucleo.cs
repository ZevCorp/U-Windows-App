namespace U.WindowsClient.Navigation;

/// <summary>
/// SITUARSE: dónde estoy y qué puedo hacer desde aquí, contestado por el núcleo.
/// </summary>
/// <remarks>
/// La primera de las cinco capacidades que la voz usa de verdad, y va primero aunque no sea la más
/// pedida: las otras cuatro se apoyan en ella. Si «dónde estoy» no es fiable, señalar, pulsar e ir
/// heredan la mentira.
///
/// POR QUÉ ESTAS CINCO Y NO OTRAS. Se contaron 26 días de log real (5.030 llamadas). De las 25
/// herramientas del mapa, la voz solo ha pedido doce, y siete no las ha pedido NUNCA —clasificar,
/// jerarquía, sin-situar, hallazgos, capturas, gesto de atrás, plata—: 968 llamadas, todas de
/// automatismos explorando. Situarse fue la segunda más pedida por una persona, 105 veces
/// (2026-08-22).
///
/// QUÉ SE DEJA FUERA, Y ES EL PUNTO. La versión del mapa viejo contaba además a dónde llevaría
/// «Atrás». Aquí no: el gesto de atrás es una de esas siete capas que ninguna persona ha usado, y
/// arrastrarla al núcleo sería mudarse con las cajas sin abrir.
///
/// LA DISTINCIÓN QUE SÍ IMPORTA la da el núcleo gratis: <see cref="Nucleo.Alcanzable.Vivo"/> separa
/// lo que se ve AHORA de lo que solo se recuerda. Decir «conozco 40 salidas» cuando 38 son recuerdo
/// es prometer un terreno que no está delante, y esa promesa la paga quien intente cruzarlo.
/// </remarks>
public sealed class AquiSegunElNucleo
{
    private readonly Nucleo.Grafo _grafo;
    private readonly Func<string> _donde;

    public AquiSegunElNucleo(Nucleo.Grafo grafo, Func<string> donde)
    {
        _grafo = grafo;
        _donde = donde;
    }

    /// <summary>Lo que se contesta cuando no se sabe ni dónde estamos. Se dice, no se adivina.</summary>
    public const string NiIdea = "no sé en qué pantalla estamos ahora mismo.";

    /// <summary>
    /// Dónde estoy y qué alcanzo. En prosa, porque quien lo lee es un modelo hablando con una
    /// persona: un volcado de selectores le haría leer identificadores en voz alta.
    /// </summary>
    public string Ahora()
    {
        string aqui = _donde() ?? "";
        if (aqui.Length == 0) return NiIdea;

        var alcanzables = _grafo.DesdeAqui(aqui);
        int vivas = alcanzables.Count(a => a.Vivo);

        // UN SITIO DEL QUE NO SE HA MIRADO NADA NO TIENE CERO SALIDAS: tiene salidas desconocidas, y
        // son cosas distintas. «Aquí no hay nada» invita a rendirse; «no he mirado» invita a mirar.
        if (alcanzables.Count == 0)
            return $"Estás en «{aqui}», pero todavía no he mirado qué hay aquí.";

        string memoria = alcanzables.Count > vivas
            ? $" (y {alcanzables.Count - vivas} más que recuerdo pero ahora no veo)"
            : "";

        return $"Estás en «{aqui}». Veo {vivas} salida(s) que puedo usar ahora{memoria}.";
    }
}
