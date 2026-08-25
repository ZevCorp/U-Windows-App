namespace Nucleo;

/// <summary>Algo pulsable que se vio en una ubicación. Lo que el mapeador entrega.</summary>
/// <param name="Selector">Su identidad. Lo único que permite reconocerlo la próxima vez.</param>
/// <param name="Etiqueta">Cómo se lee en pantalla. Para las personas; no identifica.</param>
/// <param name="Tipo">Botón, pestaña, elemento de lista… tal como lo dijo el sistema operativo.</param>
public sealed record Elemento(string Selector, string Etiqueta, string Tipo);

/// <summary>
/// Algo alcanzable desde donde estás.
/// </summary>
/// <param name="Vivo">
/// ¿Está en pantalla AHORA? Lo grabado y lo vivo son cosas distintas y no se mezclan: un elemento
/// que el grafo recuerda pero que hoy no está es MEMORIA, no una promesa. Prometerlo era lo que
/// mandaba al asistente a buscar algo donde ya no existe.
/// </param>
/// <param name="Destino">
/// A dónde llevó cuando se cruzó, o vacío si nunca se cruzó. Vacío no es un fallo: es el estado
/// honesto de una puerta que nadie ha abierto todavía.
/// </param>
/// <summary>
/// UN RECUERDO: lo que una persona enseñó sobre un elemento. La foto es una RUTA, no la imagen —y
/// solo existe cuando el recuerdo se creó DE VERDAD, con significado; señalar algo sin explicarlo
/// no deja recuerdo ni foto (2026-08-24, para que una foto en disco signifique algo cada vez).
/// </summary>
public sealed record Recuerdo(string Significado, string Foto, DateTime Cuando);

public sealed record Alcanzable(Elemento Que, bool Vivo, string Destino);

/// <summary>
/// EL NÚCLEO: el grafo, y nada más.
///
/// Contesta UNA pregunta —«¿qué es alcanzable desde donde estoy?»— y guarda UNA cosa —«esto, pulsado
/// desde aquí, llevó allí»—. Todo lo demás que tenía el núcleo anterior (niveles, cromo, clases,
/// profundidades) se quitó, no por simplificar sino porque no hacía falta para contestar eso, y era
/// de donde salían los fallos: una jerarquía calculada una vez envejece, y el asistente terminaba
/// buscando cosas donde ya no estaban.
///
/// PURO Y DETERMINISTA: no lee la pantalla, no escribe en disco, no mira el reloj. Las mismas
/// observaciones en el mismo orden dan el mismo grafo — y en distinto orden, también. Eso es lo que
/// permite probarlo sin abrir una sola ventana.
/// </summary>
public sealed class Grafo
{
    // La memoria, y es toda: qué se vio en cada sitio, y a dónde llevó cada cosa.
    private readonly Dictionary<string, Dictionary<string, Elemento>> _vistos = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _destinos = new(StringComparer.Ordinal);

    /// <summary>
    /// LO QUE ALGUIEN ENSEÑÓ sobre un elemento: qué es y para qué sirve, con la foto de cuando lo
    /// dijo. Clave igual que <see cref="_destinos"/>: ubicación + selector.
    /// </summary>
    /// <remarks>
    /// VIVE AQUÍ Y NO EN UN ARCHIVO APARTE, y esa es toda la decisión. Se probó lo otro el mismo
    /// día —un JSON en disco con las mismas claves— y el usuario lo vio enseguida: dos sitios que
    /// saben de lo mismo se desincronizan sin avisar, y encima no se puede preguntar por
    /// significado y trazar el camino en la misma consulta. Que es justo lo que se quiere hacer:
    /// «llévame a donde se radican las facturas» (2026-08-23).
    ///
    /// LA FOTO NO ENTRA, solo su ruta. Un PNG de 190 KB dentro de un grafo no aporta nada y lo
    /// engorda mucho; el grafo guarda DÓNDE está, que es lo que hace falta para volver a verla.
    /// </remarks>
    private readonly Dictionary<string, Recuerdo> _recuerdos = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _vivosAhora = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _llave = new();

    /// <summary>Cuántas veces ha cambiado. Sirve para no republicar lo que no se movió.</summary>
    public int Version { get; private set; }

    /// <summary>Dónde se está ahora, según la última observación. Vacío si nunca se observó nada.</summary>
    public string Aqui { get; private set; } = "";

    /// <summary>
    /// «Estoy aquí y esto es lo que veo». La única entrada del mapeador al núcleo.
    ///
    /// OBSERVAR NO BORRA. Lo que se vio antes en esta ubicación y hoy no está sigue en el grafo,
    /// marcado como no vivo: el mapa es memoria además de espejo, y olvidar en cuanto algo se
    /// oculta —un menú cerrado, una lista con scroll— haría que el grafo se vaciara solo.
    /// </summary>
    /// <summary>
    /// «Estoy aquí», sin decir nada de lo que se ve. Es la mitad BARATA de observar.
    /// </summary>
    /// <remarks>
    /// SON DOS HECHOS CON VELOCIDADES DISTINTAS, y juntarlos costaba caro. Saber dónde estoy vale
    /// 32 ms; leer la pantalla entera, 400. Al llegar los dos por la misma puerta, lo barato
    /// heredaba la lentitud de lo caro: el cambio de sitio se enteraba con más de un segundo de
    /// retraso, y si se navegaba rápido una ubicación intermedia no llegaba a verse nunca — el
    /// camino se grababa como A→C cuando en realidad fue A→B→C (2026-08-12, medido).
    ///
    /// Separarlas permite preguntar «¿dónde estoy?» diez veces por segundo sin pagar la lectura.
    /// </remarks>
    public void Estoy(string ubicacion)
    {
        if (string.IsNullOrWhiteSpace(ubicacion)) return;
        lock (_llave)
        {
            if (string.Equals(Aqui, ubicacion, StringComparison.OrdinalIgnoreCase)) return;
            Aqui = ubicacion;
            // Se apunta la ubicación aunque todavía no se sepa qué hay: existir es un hecho, y
            // tener elementos es otro. Si no, un sitio por el que se pasó rápido no existiría.
            if (!_vistos.ContainsKey(ubicacion))
                _vistos[ubicacion] = new Dictionary<string, Elemento>(StringComparer.Ordinal);
            Version++;
        }
    }

    public void Observar(string ubicacion, IReadOnlyList<Elemento> visibles)
    {
        if (string.IsNullOrWhiteSpace(ubicacion)) return;
        lock (_llave)
        {
            // OBSERVAR NO DICE DÓNDE ESTOY. Dice qué hay EN UN SITIO —el que le pasan— y nada más.
            // Quien decide dónde estamos es `Estoy`, y solo él.
            //
            // Las dos cosas estuvieron juntas y costó caro: leer la pantalla tarda ~400 ms, así que
            // quien observaba fijaba la ubicación con un valor de hace 400 ms y REBOBINABA el sitio
            // actual al anterior. Con la ubicación mirándose cada 120 ms, el lento pisaba
            // constantemente al rápido y el grafo se quedaba clavado en la app de antes — el
            // usuario lo vio como «vaya donde vaya, se queda en claude.exe» (2026-08-12).
            //
            // Que `Observar` reciba la ubicación como parámetro ya lo decía: quien llama sabe de
            // dónde leyó. Deducir de ahí que además ESTAMOS allí era una suposición de más.
            bool cambio = false;
            if (!_vistos.TryGetValue(ubicacion, out var aqui))
                _vistos[ubicacion] = aqui = new Dictionary<string, Elemento>(StringComparer.Ordinal);

            var vivos = new HashSet<string>(StringComparer.Ordinal);
            foreach (var e in visibles)
            {
                if (string.IsNullOrWhiteSpace(e.Selector)) continue;
                vivos.Add(e.Selector);
                if (!aqui.TryGetValue(e.Selector, out var ya) || ya != e) { aqui[e.Selector] = e; cambio = true; }
            }

            // Lo vivo cambia aunque no se haya visto nada nuevo —abrir un menú, hacer scroll— y ese
            // cambio ES lo que se quiere ver moverse: entra en la versión.
            if (!_vivosAhora.TryGetValue(ubicacion, out var antes) || !antes.SetEquals(vivos)) cambio = true;
            _vivosAhora[ubicacion] = vivos;

            if (cambio) Version++;
        }
    }

    /// <summary>
    /// «Pulsé esto desde aquí y acabé allí». El único hecho que el grafo guarda sobre navegación.
    ///
    /// Se guarda por (ubicación, selector) y no solo por selector: el mismo botón puede llevar a
    /// sitios distintos según desde dónde se pulse —un «Atrás», una miga de pan—, y meterlos en la
    /// misma casilla haría que el grafo prometiera un destino que depende de por dónde viniste.
    /// </summary>
    /// <returns>
    /// Falso si NO se guardó, y el motivo importa: un destino cuyo selector no se vio nunca en esa
    /// ubicación es un hecho sobre un elemento que el grafo no conoce, y guardarlo lo dejaría
    /// invisible para siempre — nadie que pregunte «qué alcanzo desde aquí» lo vería, porque esa
    /// respuesta se arma con los elementos observados.
    ///
    /// Pasó de verdad y por eso se rechaza en vez de tragarlo: el vigilante de clics escribía
    /// «uia:aid=navCatalogo;ct=Button» y el observador «uia:name=Catálogo;ct=Button» — dos
    /// vocabularios de identidad para la misma cosa. De diez caminos aprendidos llegaron tres, y
    /// los siete perdidos no dejaron rastro (2026-08-12, medido). Un rechazo ruidoso habría
    /// enseñado el problema el primer día.
    /// </returns>
    public bool Cruzar(string ubicacion, string selector, string destino)
    {
        if (string.IsNullOrWhiteSpace(ubicacion) || string.IsNullOrWhiteSpace(selector)) return false;
        if (string.IsNullOrWhiteSpace(destino) || destino.Equals(ubicacion, StringComparison.OrdinalIgnoreCase)) return false;
        lock (_llave)
        {
            if (!_vistos.TryGetValue(ubicacion, out var aqui) || !aqui.ContainsKey(selector)) return false;

            string clave = ubicacion + "\n" + selector;
            if (_destinos.TryGetValue(clave, out var ya) && ya == destino) return true;
            _destinos[clave] = destino;
            Version++;
            return true;
        }
    }

    /// <summary>
    /// Qué es alcanzable desde una ubicación: lo vivo primero, y detrás lo que solo es memoria.
    /// </summary>
    public IReadOnlyList<Alcanzable> DesdeAqui(string ubicacion)
    {
        lock (_llave)
        {
            if (!_vistos.TryGetValue(ubicacion, out var aqui)) return Array.Empty<Alcanzable>();
            _vivosAhora.TryGetValue(ubicacion, out var vivos);
            return aqui.Values
                .Select(e => new Alcanzable(
                    e,
                    vivos?.Contains(e.Selector) == true,
                    _destinos.TryGetValue(ubicacion + "\n" + e.Selector, out var d) ? d : ""))
                .OrderByDescending(a => a.Vivo)
                .ThenBy(a => a.Que.Etiqueta, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
    }

    /// <summary>Todas las ubicaciones conocidas. El orden es estable para que dos volcados se puedan comparar.</summary>
    public IReadOnlyList<string> Ubicaciones()
    {
        lock (_llave) return _vistos.Keys.OrderBy(x => x, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// «Esto se supo antes». Mete en el grafo lo que se recuerda de una ubicación SIN decir que
    /// esté en pantalla: es como vuelve la memoria al arrancar.
    /// </summary>
    /// <remarks>
    /// NO ES OBSERVAR, Y LA DIFERENCIA ES TODA LA HONESTIDAD DEL MODELO. Observar significa «lo
    /// estoy viendo ahora», y usarlo para restaurar marcaría vivo todo un mapa de pantallas que no
    /// están delante — el asistente creería que puede pulsar cualquier cosa de cualquier sitio.
    /// Lo que vuelve del disco es memoria, y entra como memoria.
    ///
    /// Tampoco toca <see cref="Aqui"/>: recordar dónde estuviste no es estar allí.
    /// </remarks>
    public void Recordar(string ubicacion, IReadOnlyList<Elemento> elementos)
    {
        if (string.IsNullOrWhiteSpace(ubicacion)) return;
        lock (_llave)
        {
            if (!_vistos.TryGetValue(ubicacion, out var aqui))
                _vistos[ubicacion] = aqui = new Dictionary<string, Elemento>(StringComparer.Ordinal);
            foreach (var e in elementos)
            {
                if (string.IsNullOrWhiteSpace(e.Selector)) continue;
                if (!aqui.ContainsKey(e.Selector)) { aqui[e.Selector] = e; Version++; }
            }
        }
    }

    /// <summary>
    /// EL SIGUIENTE PASO hacia un destino: qué hay que pulsar AHORA, aquí. Vacío si no se sabe
    /// llegar.
    /// </summary>
    /// <remarks>
    /// NO DEVUELVE UNA RUTA, Y ESO ES EL DISEÑO ENTERO. Una ruta completa es una promesa sobre el
    /// futuro —«luego pulsa esto, y después esto»— y el mapa es vivo: para cuando se llegue al
    /// tercer tramo, la pantalla puede haber cambiado. Se contesta un solo paso y se vuelve a
    /// preguntar al llegar. Eso es «caminar mirando» en vez de «seguir un plano», que es la idea
    /// que el usuario trajo el 2026-08-12 y de la que sale todo este núcleo.
    ///
    /// EL PRIMER PASO TIENE QUE ESTAR VIVO. Se puede buscar el camino a través de lo recordado
    /// —para eso se recuerda— pero lo que se va a pulsar AHORA tiene que estar en pantalla ahora.
    /// Devolver algo que el mapa recuerda y la pantalla ya no tiene es mandar a pulsar el vacío,
    /// que es exactamente el fallo que este modelo vino a quitar.
    /// </remarks>
    public Alcanzable? SiguientePaso(string desde, string hasta) => ComoLlego(desde, hasta).Paso;

    /// <summary>El siguiente paso y, si no lo hay, SI AL MENOS SE CONOCE EL CAMINO.</summary>
    /// <param name="Paso">Qué pulsar ahora, o nulo.</param>
    /// <param name="ConocidoEnMemoria">Si el grafo sabe llegar aunque el paso no esté en pantalla.</param>
    public readonly record struct Camino(Alcanzable? Paso, bool ConocidoEnMemoria);

    /// <summary>
    /// Cómo llegar de un sitio a otro, y por qué no se puede cuando no se puede.
    /// </summary>
    /// <remarks>
    /// DOS «NO» MUY DISTINTOS, Y ANTES SALÍAN COMO UNO. «No sé llegar» y «sé llegar pero la puerta
    /// no está delante ahora mismo» piden cosas opuestas de quien pregunta: la primera, seguir
    /// explorando; la segunda, esperar, desplegar el panel o volver atrás. Devolver nulo para las
    /// dos dejaba al que navega sin saber cuál de las dos le tocaba (2026-08-13, el usuario hizo
    /// clic en un nodo y solo obtuvo «no sé llegar desde aquí, o el paso no está en pantalla»).
    ///
    /// Y SE PRUEBAN LAS OTRAS RUTAS. La versión anterior tomaba el camino más corto y, si su primer
    /// paso no estaba vivo, se rendía —aunque hubiera otra ruta más larga cuya puerta SÍ estuviera
    /// delante—. Eso no es ser prudente, es dejar de mirar: el mapa es vivo justamente para poder
    /// preferir lo que se ve.
    ///
    /// SOLO EL PRIMER PASO TIENE QUE ESTAR VIVO. Los demás se vuelven a decidir al llegar, que es lo
    /// que significa un mapa vivo; exigir que la ruta entera esté visible desde aquí sería pedirle
    /// al núcleo una promesa sobre pantallas que todavía no se han visto.
    /// </remarks>
    public Camino ComoLlego(string desde, string hasta)
    {
        if (string.IsNullOrWhiteSpace(desde) || string.IsNullOrWhiteSpace(hasta)) return new(null, false);
        if (desde.Equals(hasta, StringComparison.OrdinalIgnoreCase)) return new(null, false);

        lock (_llave)
        {
            var porLoVivo = Buscar(desde, hasta, soloPuertasVivas: true);
            if (porLoVivo != null) return new(porLoVivo, true);
            return new(null, Buscar(desde, hasta, soloPuertasVivas: false) != null);
        }
    }

    /// <summary>
    /// Anchura desde donde estamos: el camino más corto en número de clics, que es la única medida
    /// que le importa a quien lo va a recorrer. Devuelve el PRIMER paso de ese camino.
    /// </summary>
    private Alcanzable? Buscar(string desde, string hasta, bool soloPuertasVivas)
    {
        var visto = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { desde };
        var cola = new Queue<(string Donde, Alcanzable Primero)>();

        // La condición de «vivo» se aplica SOLO aquí, en las puertas de donde estamos: son las
        // únicas que se pueden pulsar ahora. Descartarlas ya, en vez de al final, es lo que permite
        // que el recorrido encuentre otra ruta cuando la más corta no está a la vista.
        foreach (var a in Salidas(desde))
        {
            if (a.Destino.Length == 0) continue;
            if (soloPuertasVivas && !a.Vivo) continue;
            if (a.Destino.Equals(hasta, StringComparison.OrdinalIgnoreCase)) return a;
            if (visto.Add(a.Destino)) cola.Enqueue((a.Destino, a));
        }

        while (cola.Count > 0)
        {
            var (donde, primero) = cola.Dequeue();
            foreach (var a in Salidas(donde))
            {
                if (a.Destino.Length == 0) continue;
                if (a.Destino.Equals(hasta, StringComparison.OrdinalIgnoreCase)) return primero;
                if (visto.Add(a.Destino)) cola.Enqueue((a.Destino, primero));
            }
        }
        return null;
    }

    /// <summary>Las salidas de una ubicación, ya resueltas. Sin tomar el candado: quien llama lo tiene.</summary>
    private List<Alcanzable> Salidas(string ubicacion)
    {
        if (!_vistos.TryGetValue(ubicacion, out var aqui)) return new List<Alcanzable>();
        _vivosAhora.TryGetValue(ubicacion, out var vivos);
        return aqui.Values
            .Select(e => new Alcanzable(e, vivos?.Contains(e.Selector) == true,
                _destinos.TryGetValue(ubicacion + "\n" + e.Selector, out var d) ? d : ""))
            .ToList();
    }

    /// <summary>
    /// «Esto es X»: crea un RECUERDO con lo que alguien enseñó sobre un elemento de esta ubicación.
    /// </summary>
    /// <remarks>
    /// Se exige que el elemento SE HAYA VISTO aquí, igual que <see cref="Cruzar"/> exige que la
    /// puerta exista antes de acuñar por dónde lleva. Guardar el significado de algo que nadie ha
    /// mirado sería una frase sin sujeto: después nadie sabría a qué se refería.
    /// </remarks>
    public bool Ensenar(string ubicacion, string selector, string significado, string foto = "")
    {
        if (string.IsNullOrWhiteSpace(ubicacion) || string.IsNullOrWhiteSpace(selector)) return false;
        if (string.IsNullOrWhiteSpace(significado)) return false;
        lock (_llave)
        {
            if (!_vistos.TryGetValue(ubicacion, out var aqui) || !aqui.ContainsKey(selector)) return false;

            string clave = ubicacion + "\n" + selector;
            // La foto vieja se conserva si no llega una nueva: volver a explicar algo no borra la
            // imagen de cuando se explicó la primera vez.
            string laFoto = foto.Length > 0 ? foto
                : _recuerdos.TryGetValue(clave, out var ya) ? ya.Foto : "";
            _recuerdos[clave] = new Recuerdo(significado.Trim(), laFoto, DateTime.UtcNow);
            Version++;
            return true;
        }
    }

    /// <summary>Los recuerdos de una ubicación, para poder contarlos al llegar.</summary>
    public IReadOnlyList<(Elemento Que, Recuerdo Eso)> RecuerdosDe(string ubicacion)
    {
        lock (_llave)
        {
            if (!_vistos.TryGetValue(ubicacion, out var aqui)) return Array.Empty<(Elemento, Recuerdo)>();
            var salida = new List<(Elemento, Recuerdo)>();
            foreach (var e in aqui.Values)
                if (_recuerdos.TryGetValue(ubicacion + "\n" + e.Selector, out var ens))
                    salida.Add((e, ens));

            // EN UN ORDEN QUE NO CAMBIE. Salían en el orden del diccionario, o sea en el orden en que
            // se fueron viendo los elementos: la misma pantalla numeraba sus recuerdos distinto en
            // cada arranque, y basta que entre un elemento nuevo para que la lista se reordene sola.
            // Se cuentan de uno en uno —«recuerdo 1 de 2», «ahora el 2»— así que «el 2» tiene que
            // seguir siendo el mismo entre una llamada y la siguiente; si no, se señala uno mientras
            // se habla de otro, que es exactamente lo que contarlos de uno en uno vino a evitar.
            //
            // Por SELECTOR y no por etiqueta: dos cosas pueden llamarse igual —es la razón de que
            // haya selectores— y entonces el desempate volvería a depender del azar.
            salida.Sort((a, b) => string.CompareOrdinal(a.Item1.Selector, b.Item1.Selector));
            return salida;
        }
    }

    /// <summary>El recuerdo sobre UN elemento, o null.</summary>
    public Recuerdo? RecuerdoSobre(string ubicacion, string selector)
    {
        lock (_llave)
            return _recuerdos.TryGetValue(ubicacion + "\n" + selector, out var e) ? e : null;
    }

    /// <summary>
    /// De qué app es una ubicación. Vive aquí porque la identidad es asunto del grafo: quien
    /// decide qué cuenta como «el mismo sitio» tiene que decidir también qué cuenta como «la misma
    /// app», o acabarían siendo dos criterios que se separan en silencio.
    ///
    /// El formato es «esquema://app/lo-que-sea», el mismo que ya usaba el mapeador: se respeta
    /// porque cambiarlo obligaría a tocar el mapeador, y el mapeador no es lo que estamos
    /// rehaciendo.
    /// </summary>
    public static string AppDe(string ubicacion)
    {
        if (string.IsNullOrEmpty(ubicacion)) return "";
        int i = ubicacion.IndexOf("//", StringComparison.Ordinal);
        if (i < 0) return "";
        int j = ubicacion.IndexOf('/', i + 2);
        return j < 0 ? ubicacion[(i + 2)..] : ubicacion[(i + 2)..j];
    }

    /// <summary>Empezar de cero. Una prueba del núcleo empieza siempre con el grafo vacío.</summary>
    public void Olvidar()
    {
        lock (_llave)
        {
            _vistos.Clear(); _destinos.Clear(); _vivosAhora.Clear();
            _recuerdos.Clear();
            Aqui = ""; Version++;
        }
    }
}
