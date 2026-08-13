using System.Collections.Concurrent;

namespace Mapeador;

/// <summary>
/// LO QUE EL MAPEADOR SABE DE SÍ MISMO. Contadores que él mismo lleva mientras trabaja.
///
/// Existe porque el mapeador es un PROCESO, no un dato: a diferencia del núcleo —que tiene su grafo
/// en Neo4j y se puede mirar— aquí no había nada que mirar salvo el log, y leer el log es
/// arqueología. Los tres fallos del 2026-08-12 se encontraron así, línea a línea.
///
/// LA REGLA QUE HACE QUE ESTO SIRVA: el panel no calcula NADA. Pinta estos números tal cual. Si la
/// pantalla dedujera algo por su cuenta tendríamos dos opiniones sobre el mismo hecho, que es
/// exactamente lo que llevamos la semana entera pagando — y lo que mató al pintor anterior.
///
/// Y por eso los contadores viven DONDE OCURRE EL TRABAJO y no en un observador aparte: un
/// cronómetro externo mide también su propio coste. Dos veces esta semana el instrumento engañó al
/// que medía —el `docker exec` que costaba 1,7 s, y comparar Gmail con el explorador— y las dos
/// veces la conclusión estuvo a punto de ser la contraria de la verdad.
/// </summary>
public sealed class PulsoDelMapeador
{
    /// <summary>El pulso del proceso vivo. Uno solo: el mapeador es uno.</summary>
    public static readonly PulsoDelMapeador Actual = new();

    private PulsoDelMapeador() { }

    public DateTime Desde { get; } = DateTime.Now;

    // ── SATURACIÓN: nada debe apilarse ───────────────────────────────────────

    /// <summary>
    /// LOS DOS RELOJES DEL MAPEADOR, cada uno con su candado. Saber dónde estoy es barato y va
    /// rápido; leer la pantalla es caro y va lento. Tienen candados separados porque si compartieran
    /// uno, la vuelta cara bloquearía a la barata y perderíamos justo lo que hace que el sitio
    /// actual esté al día.
    ///
    /// EL CONTADOR DE DESCARTES VIVE DENTRO DEL CANDADO, no aquí al lado. Tenerlo en los dos sitios
    /// era tener dos opiniones sobre el mismo hecho, que es exactamente la avería que este panel
    /// existe para no repetir.
    /// </summary>
    public VueltaUnica Ubicacion { get; } = new("ubicación");

    /// <inheritdoc cref="Ubicacion"/>
    public VueltaUnica Pantalla { get; } = new("pantalla");

    public int DescartadasUbicacion => Ubicacion.Descartadas;
    public int DescartadasPantalla => Pantalla.Descartadas;

    // ── ATRIBUCIONES: toda transición se explica, o se dice por qué no ───────
    private int _aprendidas, _saltos;
    private readonly ConcurrentDictionary<string, int> _rechazos = new();

    /// <summary>Un salto de pantalla que sí supimos explicar con un clic.</summary>
    public void Aprendida()
    {
        Interlocked.Increment(ref _saltos);
        Interlocked.Increment(ref _aprendidas);
    }

    /// <summary>
    /// Un salto que NO supimos explicar, con su motivo. El motivo es el dato: «no se atribuyó»
    /// sin causa obliga a volver al log, que es de donde este panel viene a sacarnos.
    /// </summary>
    public void Rechazada(string motivo)
    {
        Interlocked.Increment(ref _saltos);
        _rechazos.AddOrUpdate(motivo, 1, (_, n) => n + 1);
    }

    public int Aprendidas => _aprendidas;
    public int Saltos => _saltos;
    public IReadOnlyDictionary<string, int> Rechazos => _rechazos;

    // ── COSTES: cuánto tarda cada cosa, cronometrado por quien la hace ───────
    private readonly ConcurrentDictionary<string, (long Veces, long TotalMs, long PeorMs)> _tiempos = new();

    /// <summary>
    /// Apunta cuánto costó una operación. Se guardan las tres cifras que hacen falta para
    /// entender: cuántas veces, el promedio y LA PEOR — porque un promedio bueno con una peor
    /// terrible es justo el perfil que se siente lento y no lo parece en la media.
    /// </summary>
    public void Costo(string que, long ms) =>
        _tiempos.AddOrUpdate(que, (1, ms, ms),
            (_, v) => (v.Veces + 1, v.TotalMs + ms, Math.Max(v.PeorMs, ms)));

    public IReadOnlyDictionary<string, (long Veces, long TotalMs, long PeorMs)> Tiempos => _tiempos;

    // ── EL EMBUDO: de lo que se ve a lo que llega al núcleo ──────────────────
    private long _leidos, _entregados;
    private readonly ConcurrentDictionary<string, int> _filtrados = new();

    /// <summary>Cuántos elementos se leyeron de la pantalla y cuántos llegaron al núcleo.</summary>
    public void Embudo(int leidos, int entregados)
    {
        Interlocked.Add(ref _leidos, leidos);
        Interlocked.Add(ref _entregados, entregados);
    }

    /// <summary>Por qué se descartó un elemento. Sin esto, el filtro es invisible — y fue un
    /// filtro invisible el que rompió la atribución de «Lote A1-2» sin que nadie lo viera.</summary>
    public void Filtrado(string motivo, int cuantos) =>
        _filtrados.AddOrUpdate(motivo, cuantos, (_, n) => n + cuantos);

    public long Leidos => _leidos;
    public long Entregados => _entregados;
    public IReadOnlyDictionary<string, int> Filtrados => _filtrados;
}
