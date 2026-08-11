using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Navigation;

/// <summary>
/// LO QUE LA PÁGINA DECLARA, PUESTO EN EL GRAFO: los landmarks de HTML convertidos en niveles.
///
/// La estructura de una web no hay que deducirla ni preguntársela a un modelo: HTML tiene un
/// estándar para declararla desde hace años —<c>&lt;header&gt;</c>, <c>&lt;nav&gt;</c>,
/// <c>&lt;main&gt;</c>— y el navegador lo expone por UIA. Medido sobre GitHub el 2026-08-08:
/// «cabecera:Global navigation menu» con la navegación del sitio, «navegación:Repository» con
/// Code/Issues/Pull requests, «navegación:Breadcrumbs» con la cadena de padres, y «contenido» con
/// las 74 puertas que no son estructura. Todo en UNA lectura, sin cruzar nada.
///
/// Aquí solo se traduce a niveles. Entra por la MISMA puerta que usa el maestro
/// (<see cref="SurfaceMap.FijarNivel"/> con <c>porPersona: false</c>), así que hereda todo lo que
/// ya funciona: sobrevive al borrado del grafo, se repone sobre las puertas que renacen, y se
/// puede olvidar por aplicación. Cero cambios en el núcleo.
///
/// EL ORDEN DE AUTORIDAD NO CAMBIA. Lo que declaró una persona no se toca — se comprueba antes y
/// se salta. La página es una fuente MÁS fiable que nuestra estadística y menos que su dueño.
/// </summary>
public static class JerarquiaWeb
{
    /// <summary>
    /// Traduce a niveles los grupos que la página declaró. Devuelve cuántas salidas quedaron
    /// situadas por esta vía.
    /// </summary>
    public static int Aplicar(SurfaceMap mapa, string app)
    {
        // Solo webs: en escritorio no hay landmarks y el grupo viene de contenedores UIA, que no
        // dicen nada sobre permanencia — un ToolBar puede ser de la app o de una sola pantalla.
        if (app.Length == 0 || app.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return 0;

        // Lo dicho por una persona manda, y por eso se mira ANTES: FijarNivel sobreescribe el nivel
        // aunque conserve el sello humano, así que sin esta comprobación un landmark podría mover
        // lo que el dueño acaba de fijar a mano.
        var deMano = new HashSet<string>(
            mapa.CorreccionesDe(app).Select(c => c.Etiqueta), StringComparer.OrdinalIgnoreCase);

        // Una etiqueta puede aparecer en varias pantallas; se decide una vez por etiqueta.
        var porEtiqueta = mapa.Edges()
            .Where(e => SurfaceMap.AppDe(e.From).Equals(app, StringComparison.OrdinalIgnoreCase)
                        && e.Info.Label.Length > 0 && e.Info.Nivel.Length > 0)
            .GroupBy(e => e.Info.Label, StringComparer.OrdinalIgnoreCase);

        int puestas = 0;
        foreach (var g in porEtiqueta)
        {
            string etiqueta = g.Key;
            if (deMano.Contains(etiqueta)) continue;

            var (nivel, cromo) = NivelDelGrupo(g.First().Info.Nivel);
            if (nivel < 0) continue;

            // Si ya está donde toca, no se toca: FijarNivel guarda en disco en cada llamada, y
            // reescribir lo mismo en cada observación sería escribir por escribir.
            if (g.All(e => e.Info.NivelFijado && e.Info.NivelNav == nivel && e.Info.EsCromo == cromo)) continue;

            mapa.FijarNivel(app, etiqueta, nivel, porPersona: false, cromo: cromo);
            puestas++;
        }

        if (puestas > 0)
        {
            LogBus.Log("jerarquía", $"«{app}»: {puestas} salida(s) situadas por lo que declara la página");
            // Declarar cromo cambia qué aristas son ATAJOS, y los atajos no cuentan para la
            // profundidad: hay que rehacer el cálculo o la jerarquía se queda con la de antes.
            mapa.RecalcularProfundidades(app);
        }
        return puestas;
    }

    /// <summary>
    /// Qué nivel implica cada landmark. Devuelve -1 cuando el grupo no dice nada de estructura.
    ///
    /// · cabecera / pie → NIVEL 1 y CROMO: el &lt;header&gt; y el &lt;footer&gt; son el marco del
    ///   sitio, están en todas sus páginas. Eso es exactamente nuestra definición de permanencia.
    /// · navegación → NIVEL 2: un &lt;nav&gt; que no es la cabecera suele ser el menú de UNA
    ///   sección (en GitHub, el Code/Issues/Actions de un repo). Vive dentro, no encima.
    /// · ruta (breadcrumbs) → NADA. Es una navegación, sí, pero lo que aporta es la cadena de
    ///   padres, no un nivel propio: sus enlaces son atajos a sitios que ya tienen el suyo.
    /// · contenido / lateral → NADA. Lo que hay dentro de &lt;main&gt; es contenido por
    ///   declaración del sitio, y el contenido no es estructura por muy grande que se vea.
    /// </summary>
    private static (int Nivel, bool Cromo) NivelDelGrupo(string grupo)
    {
        string g = grupo.Trim().ToLowerInvariant();
        if (g.StartsWith("cabecera")) return (1, true);
        if (g.StartsWith("pie")) return (1, true);

        // EL NOMBRE DEL LANDMARK TAMBIÉN HABLA, y hace falta mirarlo: el navegador da a casi todo
        // el tipo «navegación» y la distinción viaja en el NOMBRE que el sitio le puso. Medido:
        // «navegación:Breadcrumbs» y «navegación:Footer» acababan los dos en nivel 2, y ninguno de
        // los dos lo es (2026-08-08).
        //
        // Las migas de pan no tienen nivel PROPIO: son atajos a sitios que ya tienen el suyo, y
        // ponerlas en el 2 inventaría una sección que no existe. El pie sí es permanente —está en
        // todas las páginas del sitio— así que es cromo de nivel 1, como la cabecera.
        if (g.Contains("breadcrumb") || g.Contains("ruta")) return (-1, false);
        if (g.Contains("footer") || g.Contains("pie de")) return (1, true);

        if (g.StartsWith("navegación")) return (2, false);
        return (-1, false);
    }
}
