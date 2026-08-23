namespace U.WindowsClient.Navigation;

/// <summary>
/// PULSAR: tocar UNA cosa, comprobar qué pasó, y que el núcleo lo aprenda.
/// </summary>
/// <remarks>
/// PULSAR ES LA VERSIÓN MÍNIMA DE IR, y por eso va antes. Lo vio el usuario mirando el diseño:
/// «pulsar acaso no es la versión mínima de ir?» — y el código le da la razón, porque ir no es más
/// que preguntarle al grafo cuál es el siguiente paso y pulsarlo, en bucle. Construir ir primero
/// habría sido construir el bucle antes que el paso (2026-08-23).
///
/// Encaja además con cómo se quiere enseñar: señalar algo y pulsarlo es NAVEGAR EN CORTO, y de ahí
/// sale lo que el grafo aprende. Navegar en largo es repetir eso siguiendo lo aprendido.
///
/// LAS TRES COSAS QUE SE PUEDEN ROMPER EN SILENCIO, y que son todo lo que promete esta clase:
///
///   1. PULSAR NO ES HABER LLEGADO. Un clic es una petición; se mira la pantalla después y se
///      contesta con lo que hay, no con lo que se pretendía.
///   2. SI NO SE MOVIÓ NADA, NO SE APRENDE NADA. Acuñar un tramo que no ocurrió mete una mentira en
///      el grafo, y esa mentira la paga cada ruta que pase por ahí a partir de entonces.
///   3. SI LLEVÓ A OTRO SITIO, MANDA EL TERRENO. Se aprende a dónde llevó de verdad, no a dónde se
///      creía. El mapa se corrige solo yendo.
///
/// LO QUE NO ESTÁ AQUÍ: resolver el selector, escalar al doble clic, decidir si algo es una acción o
/// una puerta. Eso es leer y accionar la pantalla —UIA— y vive donde vive. Aquí entra «pulsa esto» y
/// sale «esto pasó», que es lo único que se puede juzgar sin una pantalla delante.
/// </remarks>
public sealed class PulsarSegunElNucleo
{
    private readonly Nucleo.Grafo _grafo;
    private readonly Func<string> _donde;
    private readonly Func<string, string, bool> _pulsar;

    /// <param name="pulsar">Selector y etiqueta → ¿se pudo tocar? Lo hace quien sabe de UIA.</param>
    public PulsarSegunElNucleo(Nucleo.Grafo grafo, Func<string> donde, Func<string, string, bool> pulsar)
    {
        _grafo = grafo;
        _donde = donde;
        _pulsar = pulsar;
    }

    /// <summary>Qué pasó al pulsar. <paramref name="Aprendido"/> = el grafo se quedó con el tramo.</summary>
    public readonly record struct Resultado(
        bool SePudo, bool CambioLaPantalla, string Desde, string Hasta, bool Aprendido, string Cuenta);

    /// <summary>
    /// Cuánto se espera a que la pantalla reaccione. No es un tiempo fijo elegido a ojo: se
    /// pregunta cada poco y se sale en cuanto cambia, así que una pantalla rápida no paga la espera
    /// de una lenta.
    /// </summary>
    public int EsperaMaximaMs { get; init; } = 1800;

    public Resultado Pulsa(string selector, string etiqueta)
    {
        string desde = _donde() ?? "";

        if (!_pulsar(selector, etiqueta))
            return new(false, false, desde, desde, false,
                $"no pude pulsar «{etiqueta}».");

        string hasta = EsperarACambiar(desde);

        // NO MOVERSE NO SIEMPRE ES UN FALLO. Un botón de acción —«Guardar», «Copiar»— hace su
        // trabajo sin cambiar de pantalla, y llamar a eso un fracaso sería reportar mal algo que
        // salió bien. Lo que NO se hace es aprender un tramo: no lo hubo.
        if (hasta.Length == 0 || hasta == desde)
            return new(true, false, desde, desde, false,
                $"pulsé «{etiqueta}» y la pantalla no cambió.");

        // EL TERRENO MANDA SOBRE EL MAPA: se aprende a dónde llevó DE VERDAD.
        bool aprendido = _grafo.Cruzar(desde, selector, hasta);

        return new(true, true, desde, hasta, aprendido,
            $"pulsé «{etiqueta}» y ahora estás en «{hasta}»."
            + (aprendido ? " Queda aprendido." : ""));
    }

    private string EsperarACambiar(string desde)
    {
        for (int ido = 0; ido < EsperaMaximaMs; ido += 120)
        {
            string ahora = _donde() ?? "";
            if (ahora.Length > 0 && ahora != desde) return ahora;
            System.Threading.Thread.Sleep(120);
        }
        return _donde() ?? "";
    }
}
