using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Navigation;

/// <summary>
/// DAR UN PASO HACIA UN SITIO, según el núcleo. La única implementación de «ir a», para todos los
/// que lo pidan: el visor, la ventanita HTTP y la voz.
/// </summary>
/// <remarks>
/// EXISTE PORQUE HABÍA TRES, Y CONTESTABAN DISTINTO. «Ir a una superficie» estaba resuelto en el
/// núcleo viejo (por proceso, así que fallaba en web y en SAP), en el panel de niveles (por pestaña,
/// solo si ya estaba abierta) y aquí. El modelo de voz usaba el peor de los tres y no podía llegar a
/// una página aunque el panel de al lado sí supiera (2026-08-16, lo preguntó el usuario).
///
/// Una pregunta se contesta en UN sitio: dos copias se desincronizan en silencio, y el que se queda
/// atrás es siempre el que menos se mira.
///
/// UN PASO POR LLAMADA, y quien quiera llegar que vuelva a preguntar. El núcleo contesta un solo
/// paso porque el mapa es vivo; encadenar la ruta entera de una vez sería reconstruir el plano que
/// el núcleo se niega a dar. <see cref="Hasta"/> encadena, sí, pero volviendo a preguntar cada vez
/// —que es exactamente lo que hace una persona—.
/// </remarks>
public sealed class PasoDelNucleo
{
    private readonly Nucleo.Grafo _grafo;
    private readonly Func<string> _donde;
    private readonly Func<string, string, bool> _pulsar;
    private readonly Func<string, bool> _ponerDelante;

    public PasoDelNucleo(Nucleo.Grafo grafo, Func<string> donde,
        Func<string, string, bool> pulsar, Func<string, bool> ponerDelante)
    {
        _grafo = grafo;
        _donde = donde;
        _pulsar = pulsar;
        _ponerDelante = ponerDelante;
    }

    /// <param name="Ok">Si el paso se dio (o ya estábamos allí).</param>
    /// <param name="Llegado">Si tras el paso ya estamos en el destino.</param>
    /// <param name="Paso">Qué se pulsó, para poder contarlo.</param>
    /// <param name="Porque">Vacío si fue bien; si no, QUÉ HACER, no solo qué falló.</param>
    /// <param name="YaEstaba">
    /// Si ya estábamos allí ANTES de tocar nada. Distingue «no hice falta» de «te puse delante», que
    /// se veían igual: al abrir una pestaña nueva se contestaba «ya estabas en wikipedia.org», y no
    /// era verdad — acababa de abrirla. Decir que no hiciste nada cuando sí hiciste algo es de las
    /// mentiras más caras, porque el que pregunta deja de mirar (2026-08-16).
    /// </param>
    public readonly record struct Resultado(bool Ok, bool Llegado, string Paso, string Selector,
                                            string Porque, bool YaEstaba = false);

    /// <summary>Un paso hacia el destino.</summary>
    public Resultado Hacia(string destino)
    {
        if (string.IsNullOrWhiteSpace(destino)) return new(false, false, "", "", "falta el destino");

        string aqui = _donde();
        bool yaEstaba = aqui.Equals(destino, StringComparison.OrdinalIgnoreCase);

        // PRIMERO, DELANTE. Para pulsar algo hay que tenerlo delante: no hay forma de navegar una
        // aplicación sin enfocarla, y fingir lo contrario sería pulsar a ciegas.
        //
        // Se pide «ponme delante de ESTA SUPERFICIE», no «tráeme este proceso»: la «app» de un id
        // web es un DOMINIO y la de SAP es un SISTEMA, y pasarlos como nombre de proceso hacía que
        // se intentara LANZAR un programa llamado «itsmiracleai.com.co» o «QAS» (2026-08-14).
        string appDestino = Nucleo.Grafo.AppDe(destino);
        if (!Nucleo.Grafo.AppDe(aqui).Equals(appDestino, StringComparison.OrdinalIgnoreCase))
        {
            if (!_ponerDelante(destino))
                return new(false, false, "", "", destino.StartsWith("web://", StringComparison.OrdinalIgnoreCase)
                    ? $"no pude abrir ni encontrar «{appDestino}» en el navegador"
                    : $"no pude ponerme delante de «{appDestino}»");
            Thread.Sleep(700);   // que la ventana se asiente antes de leer dónde estamos
            aqui = _donde();
        }

        if (aqui.Equals(destino, StringComparison.OrdinalIgnoreCase))
            return new(true, true, "", "", "", yaEstaba);

        // DOS «NO» MUY DISTINTOS. «No sé llegar» pide seguir explorando; «sé llegar pero la puerta no
        // está delante» pide esperar, desplegar el panel o volver atrás. Quien navega necesita saber
        // cuál de las dos le toca, y devolver lo mismo para ambas lo dejaba a ciegas.
        var camino = _grafo.ComoLlego(aqui, destino);
        if (camino.Paso == null)
            return new(false, false, "", "", camino.ConocidoEnMemoria
                ? $"sé llegar desde «{Corto(aqui)}», pero la puerta que hace falta no está en pantalla "
                + "ahora mismo: despliega el panel, haz scroll, o vuelve atrás"
                : $"no hay ningún camino aprendido de «{Corto(aqui)}» hasta ahí: hay que recorrerlo a "
                + "mano una vez para que el núcleo lo aprenda");

        var paso = camino.Paso;
        if (!_pulsar(paso.Que.Selector, paso.Que.Etiqueta))
            return new(false, false, paso.Que.Etiqueta, paso.Que.Selector,
                $"el mapeador no consiguió pulsar «{paso.Que.Etiqueta}»");

        // ¿NOS MOVIÓ? Un paso que no mueve no se repite. Sin esto, un camino equivocado en el grafo
        // —«pulsa Datos adjuntos para ir a Escritorio», estando ya en Datos adjuntos— hacía que el
        // mismo clic se calculara y se pulsara doce veces seguidas sin avanzar (2026-08-12).
        // Repetir algo que acaba de no funcionar no es insistir, es no estar mirando.
        string despues = aqui;
        for (int i = 0; i < 12 && despues.Equals(aqui, StringComparison.OrdinalIgnoreCase); i++)
        {
            Thread.Sleep(150);
            despues = _donde();
        }

        if (despues.Equals(aqui, StringComparison.OrdinalIgnoreCase))
            return new(false, false, paso.Que.Etiqueta, paso.Que.Selector,
                $"pulsé «{paso.Que.Etiqueta}» y no nos movió. El grafo cree que ese tramo lleva a otro "
                + "sitio, y no es cierto: hay que volver a recorrerlo para corregirlo");

        return new(true, despues.Equals(destino, StringComparison.OrdinalIgnoreCase),
                   paso.Que.Etiqueta, paso.Que.Selector, "");
    }

    /// <summary>
    /// Encadenar pasos hasta llegar, volviendo a preguntar cada vez. Devuelve una frase para quien
    /// preguntó —una persona o un modelo—, no un objeto: quien llama a esto quiere saber si llegó.
    /// </summary>
    /// <remarks>
    /// SE PARA AL PRIMER «NO». Insistir sobre un tramo que acaba de fallar es lo que dejó al
    /// navegador dando vueltas doce veces sobre el mismo clic; y el motivo del núcleo casi siempre
    /// dice qué hacer, así que repetirlo en bucle solo esconde el dato útil.
    /// </remarks>
    public string Hasta(string destino, int maxPasos = 8)
    {
        var dados = new List<string>();
        for (int i = 0; i < maxPasos; i++)
        {
            var r = Hacia(destino);
            if (r.Paso.Length > 0) dados.Add(r.Paso);
            LogBus.Log("nucleo-paso", $"hacia «{Corto(destino)}»: "
                + (r.Ok ? (r.Llegado ? "LLEGADO" : $"pulsado «{r.Paso}»") : $"NO — {r.Porque}"));

            if (!r.Ok) return dados.Count == 0
                ? r.Porque
                : $"di {dados.Count} paso(s) ({string.Join(" → ", dados)}) y ahí me paré: {r.Porque}";
            if (r.Llegado) return dados.Count == 0
                ? (r.YaEstaba ? $"ya estabas en «{Corto(destino)}»"
                              : $"te puse delante de «{Corto(destino)}»")
                : $"llegué a «{Corto(destino)}» en {dados.Count} paso(s): {string.Join(" → ", dados)}";

            Thread.Sleep(600);   // que la pantalla se asiente antes de preguntar el siguiente paso
        }
        return $"di {maxPasos} pasos y no llegué a «{Corto(destino)}»: o hay un bucle, o el grafo "
             + "aprendió mal alguno de esos tramos";
    }

    private static string Corto(string id)
    {
        int i = (id ?? "").LastIndexOf('/');
        return i > 0 ? id[(i + 1)..] : (id ?? "");
    }
}
