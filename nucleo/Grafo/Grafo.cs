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
    public void Observar(string ubicacion, IReadOnlyList<Elemento> visibles)
    {
        if (string.IsNullOrWhiteSpace(ubicacion)) return;
        lock (_llave)
        {
            // MOVERSE ES UN CAMBIO, aunque lo que se vea sea idéntico. «Dónde estoy» es un hecho
            // del grafo tanto como «qué se ve aquí»; que sea el más volátil de todos no lo hace
            // menos hecho. Sin esto, ir de A a B y volver a A no movía la versión —porque A no
            // había cambiado— así que quien pinta se saltaba la pasada y el dibujo se quedaba
            // marcando B como el sitio actual. El usuario lo vio así: «lo que estoy enfocando ya lo
            // detectó la url, pero la visualización marca un app diferente» (2026-08-12).
            bool cambio = !string.Equals(Aqui, ubicacion, StringComparison.OrdinalIgnoreCase);
            Aqui = ubicacion;
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
    public void Cruzar(string ubicacion, string selector, string destino)
    {
        if (string.IsNullOrWhiteSpace(ubicacion) || string.IsNullOrWhiteSpace(selector)) return;
        if (string.IsNullOrWhiteSpace(destino) || destino.Equals(ubicacion, StringComparison.OrdinalIgnoreCase)) return;
        lock (_llave)
        {
            string clave = ubicacion + "\n" + selector;
            if (_destinos.TryGetValue(clave, out var ya) && ya == destino) return;
            _destinos[clave] = destino;
            Version++;
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
            Aqui = ""; Version++;
        }
    }
}
