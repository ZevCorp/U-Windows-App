namespace U.WindowsClient.Navigation;

/// <summary>
/// EL COMPÁS DE UNA ESPERA: cuánto llevas esperando DE VERDAD y si se acabó el presupuesto.
/// Promesa 245 (spec 025).
/// </summary>
/// <remarks>
/// LAS ESPERAS SE CONTABAN EN MILISEGUNDOS FICTICIOS. Los cuatro bucles que aguardan a que la
/// pantalla reaccione estaban escritos así:
///
/// <code>
/// for (int ido = 0; ido &lt;= EsperaMaximaMs; ido += 120)   // «como mucho 1,8 s»
/// {
///     donde = _donde();          // …pero esto cuesta 2,8 s de verdad
///     Thread.Sleep(120);
/// }
/// </code>
///
/// El presupuesto suma 120 por vuelta, pero cada vuelta paga ADEMÁS lo que cueste el sondeo. Con un
/// «dónde estoy» de 2,8 s —medido el 2026-09-15 en la máquina del dueño— una espera de «1,8 segundos»
/// duraba más de treinta: quince vueltas de 2,8 s para agotar un presupuesto que se creía de 1,8. En
/// el log se vio un map_take de 28,8 s para contestar «lo conozco aquí pero AHORA no lo veo», y varios
/// map_go_to de 32 y 38 s para decir «no hay ningún camino aprendido».
///
/// Y LO PEOR ES CÓMO ESCALA: cuanto más pesada la pantalla, más cuesta el sondeo y más se infla la
/// espera. Justo en las pantallas lentas, donde ya se iba lento, la espera se multiplica.
///
/// El reloj se inyecta para poder juzgar esto sin esperar de verdad: el contrato mueve las manecillas
/// a mano y comprueba que el presupuesto se agota con el tiempo y no con el número de preguntas.
/// </remarks>
public sealed class Compas
{
    private readonly int _topeMs;
    private readonly Func<long> _reloj;
    private readonly long _inicio;

    /// <param name="topeMs">El presupuesto, en milisegundos de reloj de pared.</param>
    /// <param name="reloj">De dónde sale la hora. Por defecto, la del sistema.</param>
    public Compas(int topeMs, Func<long>? reloj = null)
    {
        _topeMs = topeMs;
        _reloj = reloj ?? (() => Environment.TickCount64);
        _inicio = _reloj();
    }

    /// <summary>Cuánto se lleva esperando, de verdad.</summary>
    public long Transcurrido => _reloj() - _inicio;

    /// <summary>¿Se acabó el presupuesto?</summary>
    public bool SeAcabo => Transcurrido >= _topeMs;

    /// <summary>Lo que queda, nunca negativo: para no dormir más de lo que sobra.</summary>
    public int Queda => (int)Math.Max(0, _topeMs - Transcurrido);

    /// <summary>
    /// Duerme el paso que toque, sin pasarse del presupuesto. Devuelve false si ya no hay que esperar
    /// más, para que el bucle salga sin dar otra vuelta de cortesía.
    /// </summary>
    public bool Respira(int pasoMs)
    {
        int cuanto = Math.Min(pasoMs, Queda);
        if (cuanto <= 0) return false;
        System.Threading.Thread.Sleep(cuanto);
        return !SeAcabo;
    }
}
