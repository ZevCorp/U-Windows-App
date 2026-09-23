namespace U.WindowsClient.Navigation;

/// <summary>
/// EL TECHO DE UNA ESPERA SALE DE LA ESPERA NORMAL MEDIDA (spec 047, promesa 359): el triple de la mediana de lo que
/// tardaron en asentarse o cambiar las últimas esperas que terminaron por condición, nunca menos que el mínimo.
/// </summary>
/// <remarks>
/// HASTA EL 22-09 EL TECHO ERA UN NÚMERO A OJO: 1.800 ms, «lo que dura una espera que no cambia». Con la regla de la 351 una
/// pantalla que se asienta ya no lo paga, y el techo deja de ser la duración normal para pasar a ser una RED: solo lo agota
/// lo que no para de moverse, lo que nadie mira, la puerta que el terreno sabe que navega (248) y lo que no se pudo mirar.
/// Una red se mide contra lo normal. Lo normal medido hasta hoy: 13 de 13 clics que no navegaban esperaron 1.797-1.825 ms
/// (log del 21-09, M), y la mediana del cambio de sitio de los que sí navegan fue de 405-510 ms (18-09, M).
///
/// LAS QUE LLEGARON AL TECHO NO LO ALIMENTAN: lo que duran ES el techo, y meterlas lo haría crecer a partir de sí mismo.
///
/// EL MÍNIMO NO ES DE AQUÍ: lo pone quien espera (<c>PulsarSegunElNucleo.EsperaMaximaMs</c>), y sigue en 1.800 hasta que
/// el nivel 4 de la fase 0 mida (la spec propone el p95 del «ms hasta cambio de sitio» + 300). Así el techo medido solo
/// puede SUBIR sobre el de hoy, nunca bajar de él.
///
/// UN SOLO TECHO PARA TODAS LAS APPS de un pulsar, y se dice (D): una app lenta sube el techo de las rápidas. Separarlo por
/// app no lo pide ninguna promesa; lo decidirá la medida del nivel 4.
/// </remarks>
public sealed class TechoDeLaEspera
{
    /// <summary>Cuántas esperas se recuerdan: las últimas 20. Una sesión cambia de app y de pantalla, y un techo con toda la historia no seguiría a la de ahora.</summary>
    public const int Recordadas = 20;

    private readonly Queue<int> _ultimas = new();
    // Pulsa se llama desde el hilo del servidor MCP y desde el del tramo, sobre el MISMO pulsar: dos hilos.
    private readonly object _cerrojo = new();

    /// <summary>
    /// Anota lo que tardó una espera. <paramref name="porCondicion"/> = terminó porque se asentó o cambió; una que llegó al
    /// techo se entrega igual y aquí se descarta, para que lo decida un solo sitio.
    /// </summary>
    public void Registra(int ms, bool porCondicion)
    {
        // VACÍO NO ES AUSENTE (patrón nº9): un «aún no» de −1 no es una espera de −1 ms.
        if (!porCondicion || ms < 0) return;
        lock (_cerrojo)
        {
            _ultimas.Enqueue(ms);
            while (_ultimas.Count > Recordadas) _ultimas.Dequeue();
        }
    }

    /// <summary>La mediana de las anotadas; −1 = sin medida (no «0 ms»: una espera de 0 ms es un dato, y la falta de datos no).</summary>
    public int Mediana => MedianaDe(Foto());

    /// <summary>El techo: el triple de la mediana, nunca menos que <paramref name="minimoMs"/>; sin medida, el mínimo.</summary>
    public int Techo(int minimoMs) => Calcula(minimoMs).Ms;

    /// <summary>
    /// El techo y de dónde sale, sacados de UNA misma foto de lo anotado: dos lecturas separadas podrían ver entre medias la
    /// espera de otro hilo, y la línea del log diría un techo distinto del que se esperó.
    /// </summary>
    public (int Ms, string DeDondeSale) Calcula(int minimoMs)
    {
        var a = Foto();
        int m = MedianaDe(a);
        if (m < 0) return (minimoMs, $"{minimoMs} ms, el mínimo: aún no hay ninguna espera que terminara por condición");
        long triple = 3L * m;
        return triple > minimoMs
            ? ((int)Math.Min(int.MaxValue, triple), $"{triple} ms = 3 × mediana {m} ms de {a.Length} espera(s) que terminaron por condición (mínimo {minimoMs})")
            : (minimoMs, $"{minimoMs} ms, el mínimo: 3 × mediana {m} ms de {a.Length} espera(s) = {triple}, no llega");
    }

    private int[] Foto()
    {
        lock (_cerrojo) return _ultimas.ToArray();
    }

    private static int MedianaDe(int[] a)
    {
        if (a.Length == 0) return -1;
        Array.Sort(a);
        int mitad = a.Length / 2;
        return a.Length % 2 == 1 ? a[mitad] : (int)(((long)a[mitad - 1] + a[mitad]) / 2);
    }
}
