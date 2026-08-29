namespace U.WindowsClient.Navigation;

/// <summary>
/// EL TERRENO POR DELANTE, EN DATOS: el mismo árbol que <see cref="TerrenoPorDelante"/> cuenta en
/// prosa al modelo, servido como estructura para que el visor lo PINTE — vivo en trazo lleno,
/// recordado punteado, la distinción que el núcleo ya hace y el dibujo solo repite (promesa 75).
/// </summary>
/// <remarks>
/// SON DOS BOCAS DE LA MISMA FUENTE, no dos opiniones: las dos leen `Grafo.DesdeAqui` y las dos
/// respetan las mismas prohibiciones (no inventar destino, cortar ciclos, tope de niveles). La
/// prosa además RESUELVE la puerta pedida y recorta a 12 —es para planificar—; esto entrega la
/// pantalla entera —es para mirar—. Si algún día contestaran distinto sobre el mismo hecho, gana
/// el grafo: los dos son solo maneras de leerlo.
///
/// El visor lo pide por el 8792 (`/terreno`), la lectura sin proyección de por medio — no por
/// Neo4j, que para esta pregunta sería un espejo con retardo.
/// </remarks>
public static class TerrenoParaElVisor
{
    /// <summary>Una puerta tal como el visor la pinta. Destino vacío = por descubrir.</summary>
    public sealed record PuertaVista(string Etiqueta, string Selector, string Tipo, bool Vivo, string Destino);

    /// <summary>Una pantalla del árbol: sus puertas, y detrás de cada cruzada, la siguiente.</summary>
    public sealed record PantallaVista(
        string Id, string Corto,
        IReadOnlyList<PuertaVista> Puertas,
        IReadOnlyList<PantallaVista> Dentro,
        int PuertasOcultas);

    /// <summary>Cuántas puertas por pantalla entrega, con las vivas y cruzadas primero.</summary>
    private const int PuertasPorPantalla = 30;

    public static PantallaVista Arbol(Nucleo.Grafo grafo, string desde, int niveles)
    {
        niveles = Math.Clamp(niveles, 1, 3);
        return Pantalla(grafo, desde ?? "", esAqui: true, niveles,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    private static PantallaVista Pantalla(Nucleo.Grafo grafo, string id, bool esAqui, int nivelesQueQuedan, HashSet<string> vistas)
    {
        vistas.Add(id);
        var alli = grafo.DesdeAqui(id);

        // Vivas primero (son las accionables YA), luego cruzadas (las que saben a dónde llevan).
        var orden = alli.OrderByDescending(a => a.Vivo)
                        .ThenByDescending(a => a.Destino.Length > 0)
                        .ThenBy(a => a.Que.Etiqueta.Length).ToList();

        // VIVO SOLO DONDE ESTAMOS. La bandera del grafo dice «estaba en la última observación de
        // ESA pantalla»; para cualquier pantalla que no es la actual eso es memoria con fecha, y
        // pintarla de vivo prometería pantalla donde no la hay — la mentira que la promesa 75
        // prohíbe. El árbol la apaga fuera de aquí; el batch la re-verificará en vivo al llegar.
        var puertas = orden.Take(PuertasPorPantalla)
            .Select(a => new PuertaVista(a.Que.Etiqueta, a.Que.Selector, a.Que.Tipo, esAqui && a.Vivo, a.Destino))
            .ToList();

        var dentro = new List<PantallaVista>();
        if (nivelesQueQuedan >= 1)
            foreach (var a in orden.Where(a => a.Destino.Length > 0))
            {
                if (vistas.Contains(a.Destino)) continue;
                dentro.Add(Pantalla(grafo, a.Destino, esAqui: false, nivelesQueQuedan - 1, vistas));
            }

        return new PantallaVista(id, Corto(id), puertas, dentro,
            Math.Max(0, alli.Count - puertas.Count));
    }

    private static string Corto(string id)
    {
        id ??= "";
        // La misma regla que la prosa: en SAP la cola sola miente (todo acaba en «0100»), así que
        // el nombre corto lleva la transacción.
        if (id.StartsWith("sapgui://", StringComparison.OrdinalIgnoreCase))
        {
            var partes = id[9..].Split('/');
            if (partes.Length >= 2)
            {
                string cola = partes[^1];
                return partes[1] + (cola.Length > 0 && !cola.Equals(partes[1], StringComparison.OrdinalIgnoreCase)
                    ? "·" + cola : "");
            }
        }
        int i = id.LastIndexOf('/');
        return i > 0 ? id[(i + 1)..] : id;
    }
}

/// <summary>
/// EL RASTRO DE LOS BATCHES: lo que cada tanda contestó, para volver a mirarlo (promesa 76).
/// </summary>
/// <remarks>
/// Hasta ahora el relato de cada batch vivía solo en la respuesta MCP y en el log — el visor no
/// tenía de dónde pintarlo. Un anillo corto: lo último manda, lo viejo se cae, y no crece sin
/// tope — un visor que pagina historia es un archivo, no un pulso.
/// </remarks>
public sealed class RastroDeBatches
{
    public sealed record Corrida(DateTime Cuando, string Cuenta);

    private readonly object _llave = new();
    private readonly LinkedList<Corrida> _anillo = new();
    private readonly int _tope;

    public RastroDeBatches(int tope = 20) => _tope = Math.Max(1, tope);

    public void Agrega(string cuenta)
    {
        lock (_llave)
        {
            _anillo.AddFirst(new Corrida(DateTime.Now, cuenta ?? ""));
            while (_anillo.Count > _tope) _anillo.RemoveLast();
        }
    }

    /// <summary>Las corridas, la más reciente primero.</summary>
    public IReadOnlyList<Corrida> Ultimas()
    {
        lock (_llave) return _anillo.ToList();
    }
}
