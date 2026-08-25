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

    /// <summary>
    /// DÓNDE SE ESTÁ ROMPIENDO EL TERRENO. Cuelga del pulso porque es de la misma familia que lo que
    /// ya vive aquí —cuánto cuesta cada cosa, qué se saturó— solo que en vez de contar lo que tarda,
    /// cuenta lo que sale mal. Y por el mismo motivo: para poder mirarlo desde fuera sin adivinar.
    /// Ver docs/specs/medir-el-terreno.md.
    /// </summary>
    public LoQueSeHaRoto Roto { get; } = new();

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

    /// <summary>Lo que sabemos de UNA app: cuántos saltos supimos explicar y por qué no los demás.</summary>
    public sealed class PorApp
    {
        internal int _aprendidas, _saltos, _noEranNavegacion, _cambioLaPantallaYNoElSitio;
        internal readonly ConcurrentDictionary<string, int> _rechazos = new();

        public int Aprendidas => _aprendidas;
        public int Saltos => _saltos;
        public IReadOnlyDictionary<string, int> Rechazos => _rechazos;

        /// <summary>
        /// Cambios de ventana —alt-tab, la barra de tareas— que NO eran navegación. Se cuentan
        /// aparte de <see cref="Saltos"/> a propósito: rechazarlos es el sistema portándose bien, y
        /// meterlos en el porcentaje hacía que «no había nada que mapear» se leyera como «mapeamos
        /// mal». Spotify salía con 0 de 2 explicados cuando sus dos saltos eran alt-tabs correctos.
        /// </summary>
        public int NoEranNavegacion => _noEranNavegacion;

        /// <summary>
        /// Veces que un clic cambió lo que se ve pero NO el sitio. Es el fallo que no aparecía por
        /// ningún lado: sin salto no hay rechazo que contar, así que una app donde la identidad no
        /// se mueve —Electron, una web de una sola dirección— salía con un grafo diminuto y ni un
        /// solo error. Callar un fallo entero es peor que contarlo mal.
        /// </summary>
        public int CambioLaPantallaYNoElSitio => _cambioLaPantallaYNoElSitio;
    }

    /// <summary>
    /// LAS ATRIBUCIONES, SEPARADAS POR APP. Sin este desglose no se puede contestar la única
    /// pregunta que decide la arquitectura: ¿los fallos de mapeo son propios de cada aplicación, o
    /// son los mismos en todas?
    /// </summary>
    /// <remarks>
    /// Con el total agregado, «universal» y «particular» se ven EXACTAMENTE IGUAL: cien rechazos
    /// pueden ser una causa en todas las apps o una causa distinta en cada una, y el número es el
    /// mismo. La respuesta cambia el problema de escala —hay una docena de toolkits y millones de
    /// aplicaciones—, así que merece un instrumento y no una impresión.
    ///
    /// La app es la de DONDE VENÍAMOS, no la de destino: el rechazo dice que no supimos reconocer
    /// algo en la pantalla que dejamos, y es esa pantalla la que estamos juzgando.
    /// </remarks>
    private readonly ConcurrentDictionary<string, PorApp> _porApp = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Un salto de pantalla que sí supimos explicar con un clic.</summary>
    public void Aprendida(string app)
    {
        var a = _porApp.GetOrAdd(app ?? "", _ => new PorApp());
        Interlocked.Increment(ref a._saltos);
        Interlocked.Increment(ref a._aprendidas);
    }

    /// <summary>
    /// Un salto que NO supimos explicar, con su motivo. El motivo es el dato: «no se atribuyó»
    /// sin causa obliga a volver al log, que es de donde este panel viene a sacarnos.
    /// </summary>
    public void Rechazada(string motivo, string app)
    {
        var a = _porApp.GetOrAdd(app ?? "", _ => new PorApp());
        Interlocked.Increment(ref a._saltos);
        a._rechazos.AddOrUpdate(motivo, 1, (_, n) => n + 1);
    }

    /// <summary>Un cambio de ventana que no era navegación. No cuenta como salto: no lo era.</summary>
    public void NoEraNavegacion(string app) =>
        Interlocked.Increment(ref _porApp.GetOrAdd(app ?? "", _ => new PorApp())._noEranNavegacion);

    /// <summary>Un clic cambió lo que se ve, pero el sitio siguió siendo el mismo.</summary>
    public void CambioLaPantallaYNoElSitio(string app) =>
        Interlocked.Increment(ref _porApp.GetOrAdd(app ?? "", _ => new PorApp())._cambioLaPantallaYNoElSitio);

    public IReadOnlyDictionary<string, PorApp> Apps => _porApp;

    // LOS TOTALES SE DERIVAN, no se llevan aparte. Un contador global sumando en paralelo al de
    // cada app serían dos opiniones sobre el mismo hecho, y ya sabemos cómo acaba eso.
    public int Aprendidas => _porApp.Values.Sum(a => a.Aprendidas);
    public int Saltos => _porApp.Values.Sum(a => a.Saltos);

    public IReadOnlyDictionary<string, int> Rechazos
    {
        get
        {
            var todos = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var a in _porApp.Values)
                foreach (var (motivo, veces) in a.Rechazos)
                    todos[motivo] = todos.TryGetValue(motivo, out int n) ? n + veces : veces;
            return todos;
        }
    }

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

    private readonly ConcurrentDictionary<string, int> _colgadas = new();

    /// <summary>
    /// Una vuelta que no volvió a tiempo y se dio por perdida.
    /// </summary>
    /// <remarks>
    /// SE CUENTA APARTE PORQUE LA MEDIA NO SOBREVIVE A UN COLGADO. Medido el 2026-08-13: `localizar`
    /// costaba 22 ms de verdad —160 llamadas en 40 s— y el panel decía 1.461 ms de media, porque UNA
    /// sola muestra de 1.251.056 ms (veintiún minutos) se comía el promedio. Con la media envenenada,
    /// la lectura obvia era «localizar va lento» y la verdad era «localizar va perfecto y una vez se
    /// quedó clavado». Se arreglan en sitios distintos.
    ///
    /// Y el colgado importa mucho más que la media: mientras dura, el candado de reentrada no suelta
    /// y el mapa deja de enterarse de dónde estás. Veintiún minutos ciego, sin un solo error.
    /// </remarks>
    public void Colgada(string que) => _colgadas.AddOrUpdate(que, 1, (_, n) => n + 1);

    public IReadOnlyDictionary<string, int> Colgadas => _colgadas;

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
