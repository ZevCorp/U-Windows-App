namespace U.WindowsClient.Navigation;

/// <summary>
/// LO QUE SE ACABA DE MIRAR NO SE VUELVE A MIRAR, durante un instante. Promesa 246 (spec 025).
/// </summary>
/// <remarks>
/// CONTESTAR «DÓNDE ESTÁS» COSTABA MÁS QUE LEER LA PANTALLA ENTERA. Medido el 2026-09-15, ocho veces
/// seguidas: <c>map_where_am_i</c> 2.771 ms de mediana contra los 823 ms de <c>map_what_i_see</c>, que
/// lee todos los elementos. No tiene defensa, y salió de las specs 020 y 022: cada pregunta volvía a
/// identificar en vivo la ventana de trabajo, y esa identificación vale lo que valga la ventana.
///
/// La pieza es la misma idea que <see cref="Ui.LaBarraDeTareas"/> ya usa con su caducidad de cuatro
/// segundos, y el mismo argumento: la respuesta cambia como mucho cada varios segundos, y se pregunta
/// varias veces por segundo.
///
/// CORTA A PROPÓSITO, y con olvido a mano. Una memoria larga se convierte en una mentira en cuanto la
/// pantalla cambia; por eso la caducidad se mide en centenares de milisegundos y por eso quien acaba
/// de accionar algo puede tirar lo recordado en vez de esperar a que caduque.
/// </remarks>
/// <typeparam name="T">Lo que se recuerda.</typeparam>
public sealed class MemoriaCorta<T>
{
    private readonly int _caducidadMs;
    private readonly Func<long> _reloj;
    private T? _recordado;
    private long _cuando = long.MinValue;
    private bool _hay;

    /// <param name="caducidadMs">Cuánto vale lo recordado.</param>
    /// <param name="reloj">De dónde sale la hora. Por defecto, la del sistema.</param>
    public MemoriaCorta(int caducidadMs, Func<long>? reloj = null)
    {
        _caducidadMs = caducidadMs;
        _reloj = reloj ?? (() => Environment.TickCount64);
    }

    /// <summary>Lo recordado si aún vale; si no, se va a la fuente y se recuerda lo que traiga.</summary>
    public T Pide(Func<T> fuente)
    {
        long ahora = _reloj();
        if (_hay && ahora - _cuando < _caducidadMs) return _recordado!;
        _recordado = fuente();
        _cuando = ahora;
        _hay = true;
        return _recordado;
    }

    /// <summary>Tirar lo recordado: algo acaba de cambiar y ya no vale.</summary>
    public void Olvida()
    {
        _hay = false;
        _recordado = default;
    }
}
