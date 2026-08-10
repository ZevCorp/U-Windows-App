namespace U.WindowsClient.Navigation;

/// <summary>
/// BRONCE: lo observado en crudo, leído de un sitio y no reinventado en cada pantalla que lo pinta.
///
/// Existía como idea y no como dato. La vista BRONCE del explorador se lo construía ella misma con
/// un recorrido por anchura sobre las aristas, así que «bronce» significaba dos cosas según quién
/// preguntara: para el derivador era el mapa entero (con la plata que otros le habían escrito
/// encima), y para el dibujo era un cálculo suyo que nadie más veía. Dos definiciones de la etapa
/// de PARTIDA hacen imposible decir qué añadió la siguiente.
///
/// Aquí hay una sola, y su regla es una omisión: **no se mira ningún campo declarado**. Ni
/// `NivelNav`, ni `NivelFijado`, ni `PorPersona`, ni `EsCromo`, ni `KindDeclarado`. No porque
/// estorben, sino porque no son bronce: son lo que alguien afirmó DESPUÉS. Que hoy vivan dentro del
/// mismo `EdgeInfo` es una mezcla que hay que deshacer en la persistencia; mientras tanto, este
/// lector se comporta como si ya estuvieran fuera, y la promesa 13 del contrato lo vigila.
///
/// Lo que sí es bronce, y a veces cuesta distinguirlo: la etiqueta, el tipo de control, el selector,
/// el grupo que declaró la PÁGINA (los landmarks de HTML son observación, no opinión nuestra), si la
/// puerta se llegó a cruzar, y cuántas veces se vio cada cosa. Todo eso se leyó de la pantalla.
/// </summary>
public static class Bronce
{
    /// <summary>Una pantalla observada. `EsRaiz` es la primera de la app por la que se entró.</summary>
    /// <remarks>
    /// La raíz es dato de bronce —quién estaba delante la primera vez— pero hoy se guarda en
    /// `NodeInfo.Nivel == 0`, el mismo campo que la plata reescribe. Es la única lectura de este
    /// archivo que toca un campo compartido, y desaparece cuando bronce tenga su propio `EsRaiz`
    /// en disco (fase 3 del plan).
    /// </remarks>
    public sealed record Pantalla(string Id, int Visitas, bool EsRaiz);

    /// <summary>Una salida vista. `Cruzada` no es una opinión: o se abrió o no se abrió.</summary>
    public sealed record Puerta(
        string Desde, string Hacia, string Selector, string Etiqueta,
        string ControlType, string Grupo, bool Cruzada, int Veces);

    public sealed record BronceApp(
        string App,
        string Raiz,
        IReadOnlyList<Pantalla> Pantallas,
        IReadOnlyList<Puerta> Puertas,
        IReadOnlyDictionary<string, int> Distancias);

    /// <summary>
    /// El bronce de una app. Puro: no muta el mapa y no depende del orden de enumeración.
    /// </summary>
    public static BronceApp De(SurfaceMap mapa, string app)
    {
        bool DeLaApp(string id) => SurfaceMap.AppDe(id).Equals(app, StringComparison.OrdinalIgnoreCase);

        var pantallas = mapa.Nodes
            .Where(kv => DeLaApp(kv.Key))
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => new Pantalla(kv.Key, kv.Value.Visits, kv.Value.Nivel == 0))
            .ToList();

        var puertas = mapa.Edges()
            .Where(e => DeLaApp(e.From))
            .OrderBy(e => e.From, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Info.Selector, StringComparer.Ordinal)
            .ThenBy(e => e.To, StringComparer.OrdinalIgnoreCase)
            .Select(e => new Puerta(e.From, e.To, e.Info.Selector, e.Info.Label,
                e.Info.ControlType, e.Info.Nivel, e.Info.Explored, e.Info.Count))
            .ToList();

        string raiz = pantallas.FirstOrDefault(p => p.EsRaiz)?.Id ?? "";

        // LA DISTANCIA EN SALTOS, que es lo ÚNICO que bronce puede decir sobre la forma: cuántas
        // aristas hay que recorrer desde la raíz, sin distinguir un atajo de una escalera. No es
        // jerarquía y no pretende serlo — es la referencia contra la que se ve qué añadió plata.
        var distancias = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (raiz.Length > 0)
        {
            var hijos = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in puertas)
            {
                if (SurfaceMap.EsPuerta(p.Hacia) || !DeLaApp(p.Hacia)) continue;
                if (!hijos.TryGetValue(p.Desde, out var l)) hijos[p.Desde] = l = new List<string>();
                if (!l.Contains(p.Hacia, StringComparer.OrdinalIgnoreCase)) l.Add(p.Hacia);
            }
            distancias[raiz] = 0;
            var cola = new Queue<string>();
            cola.Enqueue(raiz);
            while (cola.Count > 0)
            {
                string aqui = cola.Dequeue();
                if (!hijos.TryGetValue(aqui, out var l)) continue;
                foreach (var h in l.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    if (!distancias.ContainsKey(h)) { distancias[h] = distancias[aqui] + 1; cola.Enqueue(h); }
            }
        }

        return new BronceApp(app, raiz, pantallas, puertas, distancias);
    }

    /// <summary>
    /// El bronce entero en una cadena canónica, para poder afirmar que algo NO lo cambió. Es la
    /// herramienta de la promesa «declarar no toca lo observado»: sin una huella, esa afirmación
    /// habría que comprobarla campo a campo desde el contrato, que es justo el tipo de código que
    /// falla por su cuenta y manda la investigación al sitio equivocado.
    /// </summary>
    public static string Huella(BronceApp b) =>
        $"raiz={b.Raiz}|"
        + string.Join(",", b.Pantallas.Select(p => $"{p.Id}:{p.Visitas}"))
        + "|"
        + string.Join(",", b.Puertas.Select(p =>
            $"{p.Desde}-[{p.Selector}|{p.Etiqueta}|{p.ControlType}|{p.Grupo}|{(p.Cruzada ? "x" : "o")}]->{p.Hacia}"));
}
