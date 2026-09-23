using System.Diagnostics;
using System.Windows;
using U.WindowsClient.Decision;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Ui.Jev;

/// <summary>
/// EL PUENTE PROVISIONAL: cronometra al decisor, arma el ciclo y devuelve la decisión intacta. Promesa 382 (spec 049).
/// </summary>
/// <remarks>
/// HASTA QUE ENTRE LA 048 (rama C, PR #117). C publica cada paso decidido como un evento tipado,
/// <c>SurfaceMapTools.AlDecidir(PasoDecidido)</c> (368); mientras no esté en <c>main</c>, la vista decora el
/// <c>Decisor</c> del mapa DESDE FUERA —<c>SurfaceMapTools.Decisor</c> es una propiedad pública— y ni
/// <c>Decision/</c> ni <c>Navigation/</c> ni <c>SurfaceMapTools</c> se tocan (spec 049 §El puente provisional).
/// Cuando C entre, el parcial se suscribe a <c>AlDecidir</c> y llama a <see cref="CicloDe"/> con los campos del
/// evento —<c>CicloDe(e.Objetivo, e.Ofrecidas, e.Decision, e.Ms.Decidir, cajas de e.Candidatas)</c>—;
/// <see cref="Envolver"/> deja de hacer falta y el modelo no cambia. Lo que el puente NO ve, y el evento sí: la
/// caja de cada candidata (365). Por eso hoy el overlay no pinta ninguna verde, y lo dice (375).
///
/// LA DECISIÓN NO SE TOCA. Sale el mismo objeto que devolvió el decisor, y una excepción del decisor sale tal cual:
/// <c>SurfaceMapTools</c> ya la recoge con su cadena entera y le devuelve el paso a Luna, y el tramo lo cuenta en su
/// línea de progreso, que llega al panel por su camino. Lo que haga la vista después —armar el ciclo, publicarlo—
/// no puede cambiar ni retrasar lo que se pulsa: si lanza, se dice en el log y la decisión sigue su camino.
///
/// ENVOLVER DOS VECES ENVUELVE UNA. <c>InterruptorDelDecisor</c> reasigna el <c>Decisor</c> en cada
/// <c>Encender</c> y <c>Apagar</c> (<c>InterruptorDelDecisor.cs:66, 81</c>), y la vista se sincroniza después de
/// los dos; sin esto cada pulsación del botón apilaría un cronómetro más y un ciclo publicado de más.
/// </remarks>
public static class ObservadorDelDecisor
{
    /// <summary>
    /// El decisor con un cronómetro alrededor: devuelve lo mismo que <paramref name="interno"/> y, además, avisa a
    /// <paramref name="alDecidir"/> con el ciclo. Si <paramref name="interno"/> ya es un envoltorio, se envuelve
    /// su decisor de dentro, no el envoltorio: el último <paramref name="alDecidir"/> manda y hay un solo cronómetro.
    /// </summary>
    public static Func<string, string, IReadOnlyList<string>, DecisionDeUnPaso> Envolver(
        Func<string, string, IReadOnlyList<string>, DecisionDeUnPaso> interno, Action<CicloDeJev> alDecidir)
    {
        ArgumentNullException.ThrowIfNull(interno);
        ArgumentNullException.ThrowIfNull(alDecidir);
        if (interno.Target is Envoltorio ya && ya.AlDecidir == alDecidir) return interno;
        return new Envoltorio(InternoDe(interno)!, alDecidir).Decidir;
    }

    /// <summary>Si <paramref name="decisor"/> es un envoltorio de este observador.</summary>
    public static bool EstaEnvuelto(Func<string, string, IReadOnlyList<string>, DecisionDeUnPaso>? decisor) =>
        decisor?.Target is Envoltorio;

    /// <summary>El decisor de dentro de un envoltorio; el mismo si no lo es. Desenvolver es quedarse con esto.</summary>
    public static Func<string, string, IReadOnlyList<string>, DecisionDeUnPaso>? InternoDe(
        Func<string, string, IReadOnlyList<string>, DecisionDeUnPaso>? decisor) =>
        decisor?.Target is Envoltorio e ? e.Interno : decisor;

    /// <summary>
    /// LA TRADUCCIÓN ENTERA de lo que ve la costura del decisor a lo que pinta la vista. Sus argumentos son los
    /// campos del evento de la 048 (<c>PasoDecidido</c>: <c>Objetivo</c>, <c>Ofrecidas</c>, <c>Decision</c>,
    /// <c>Ms.Decidir</c> y la <c>Caja</c> de cada candidata), para que el puente de hoy y el evento de mañana
    /// pinten por el mismo camino (aprendizaje nº16).
    /// </summary>
    /// <remarks>
    /// <see cref="DecisionDeUnPaso"/> Y <see cref="DecisionDeJev"/> NO SIGNIFICAN LO MISMO en dos campos, y aquí se
    /// traduce, no se copia (hallazgo de la fase 2 de la 049). (1) <c>Puerta</c> viene VACÍA cuando no se actúa
    /// (<c>DecisionDeUnPaso.No</c>), y el panel necesita la elegida también entonces para decir «"Grabar" no se
    /// deshace»: es la primera de la distribución, que llega de mayor a menor (288). (2) <c>Cumplido</c> vale 0
    /// cuando no se preguntó, y solo se pregunta con distribución: sin ella es <c>null</c> y el medidor enseña «—»
    /// (373), no un 0 que parece medido. Con distribución se da por preguntado, y es la única lectura posible: un
    /// transporte viejo que no trae la noul deja 0 igual (<c>ElDecisor.cs:183</c>), y ahí el medidor dirá 0.00 sin
    /// que nadie lo haya medido; ninguna comprobación lo distingue (dicho en la spec).
    /// <c>Ausente</c> no viaja en <see cref="DecisionDeUnPaso"/> y va <c>null</c>
    /// siempre. Y la pulsada y los tokens tampoco pasan por aquí: la mano aún no pulsó, y nadie lee
    /// <c>input_tokens</c> todavía (350 de A).
    /// </remarks>
    /// <param name="objetivo">Lo que el tramo quiere conseguir.</param>
    /// <param name="ofrecidas">Las ids tal como se le ofrecieron al decisor, en su orden.</param>
    /// <param name="decision">Lo que devolvió el decisor.</param>
    /// <param name="msDecidir">Lo que tardó en decidir.</param>
    /// <param name="cajas">
    /// La caja de cada ofrecida, en PARALELO y en el mismo orden (285), o <c>null</c> si no se sabe —el puente de
    /// hoy—. <see cref="Rect.Empty"/> es «sin caja leída» (terreno, dynpro: 365), y esa candidata se ofrece pero no
    /// se pinta.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Si hay cajas y no son tantas como las ofrecidas: emparejarlas a ojo pintaría la caja de una sobre el id de
    /// otra, que es la caja que miente (aprendizaje nº4).
    /// </exception>
    public static CicloDeJev CicloDe(string objetivo, IReadOnlyList<string> ofrecidas, DecisionDeUnPaso decision, long msDecidir, IReadOnlyList<Rect>? cajas)
    {
        ArgumentNullException.ThrowIfNull(ofrecidas);
        ArgumentNullException.ThrowIfNull(decision);
        if (cajas != null && cajas.Count != ofrecidas.Count)
            throw new ArgumentException(
                $"llegaron {cajas.Count} caja(s) para {ofrecidas.Count} ofrecida(s): van en paralelo, una por id y en su orden, y no se emparejan a ojo",
                nameof(cajas));

        var candidatas = new List<CandidataDeJev>(ofrecidas.Count);
        for (int i = 0; i < ofrecidas.Count; i++)
        {
            var (etiqueta, tipo) = EtiquetaYTipo(ofrecidas[i]);
            Rect? caja = cajas != null && !cajas[i].IsEmpty ? cajas[i] : null;
            candidatas.Add(new CandidataDeJev { Id = ofrecidas[i], Etiqueta = etiqueta, Tipo = tipo, Caja = caja, EsLeida = caja != null });
        }

        bool conDistribucion = decision.Alternativas.Count > 0;
        return new CicloDeJev
        {
            Objetivo = objetivo ?? "",
            Candidatas = candidatas,
            Decision = new DecisionDeJev
            {
                Actuar = decision.Actuar,
                // La primera ES la elegida: ElDecisor ordena la distribución de mayor a menor (ElDecisor.cs:182).
                Puerta = decision.Actuar || !conDistribucion ? decision.Puerta : decision.Alternativas[0].Puerta,
                Confianza = decision.Confianza,
                Alternativas = decision.Alternativas,
                Cumplido = conDistribucion ? decision.Cumplido : null,
                Ausente = null,
                Peligro = decision.Peligro,
                Porque = decision.Porque,
            },
            MsDecidir = msDecidir,
            Fase = FaseDelCiclo.Decidido,
        };
    }

    /// <summary>
    /// La etiqueta y el tipo de un id, POR EL CAMINO QUE LO FORMÓ: <c>$"{n}) {etiqueta} ({tipo})"</c>
    /// (<c>SurfaceMapTools.cs:270</c>). La primera «)» cierra el número —como en <see cref="CandidataDeJev.NumeroDelId"/>
    /// y en la mano— y el ÚLTIMO paréntesis es el tipo, así que una etiqueta con los suyos («Guardar (F5)») los
    /// conserva. Un id sin esa forma se enseña entero y sin tipo: inventarle uno sería peor que no tenerlo.
    /// </summary>
    private static (string Etiqueta, string Tipo) EtiquetaYTipo(string id)
    {
        int cierre = id.IndexOf(')');
        if (cierre < 0) return (id, "");
        string resto = id.Substring(cierre + 1);
        if (resto.StartsWith(' ')) resto = resto.Substring(1);   // UN espacio: con etiqueta vacía, «5)  (Button)»
        int abre = resto.LastIndexOf(" (", StringComparison.Ordinal);
        if (abre < 0 || !resto.EndsWith(')')) return (resto, "");
        return (resto.Substring(0, abre), resto.Substring(abre + 2, resto.Length - abre - 3));
    }

    /// <summary>El envoltorio: lo que el <c>Target</c> del delegado devuelto lleva dentro, y por eso se reconoce.</summary>
    private sealed class Envoltorio
    {
        public Func<string, string, IReadOnlyList<string>, DecisionDeUnPaso> Interno { get; }
        public Action<CicloDeJev> AlDecidir { get; }

        public Envoltorio(Func<string, string, IReadOnlyList<string>, DecisionDeUnPaso> interno, Action<CicloDeJev> alDecidir)
        {
            Interno = interno;
            AlDecidir = alDecidir;
        }

        public DecisionDeUnPaso Decidir(string pantalla, string objetivo, IReadOnlyList<string> ofrecidas)
        {
            var reloj = Stopwatch.StartNew();
            var decision = Interno(pantalla, objetivo, ofrecidas);   // si lanza, sale tal cual: no hay catch aquí
            reloj.Stop();
            try { AlDecidir(CicloDe(objetivo, ofrecidas, decision, reloj.ElapsedMilliseconds, cajas: null)); }
            catch (Exception e)
            {
                // LA CADENA ENTERA (patrón nº3), y el paso que falló. Pintar no es decidir: la decisión ya está y
                // sale igual; lo único que se pierde es el ciclo de la vista, y eso se dice.
                string causa = "";
                for (var x = e; x != null; x = x.InnerException)
                    causa += $"{x.GetType().Name}: {x.Message}" + (x.InnerException != null ? " ← " : "");
                // Cuentas y tiempos, NUNCA texto de pantalla (spec 049 §Con qué se juzga): ni la pantalla ni las ids.
                LogBus.Log("jev-vista", $"✘ el observador no pudo publicar una decisión de {ofrecidas.Count} ofrecida(s) y {reloj.ElapsedMilliseconds} ms: {causa}. "
                    + "La decisión sale intacta; la vista se queda con lo anterior.");
            }
            return decision;
        }
    }
}
