using System.Runtime.CompilerServices;
using System.Windows;
using U.WindowsClient.Decision;
using U.WindowsClient.Diagnostics;
using U.WindowsClient.Mcp;
using U.WindowsClient.Navigation;

namespace U.WindowsClient.Ui.Jev;

/// <summary>
/// LA VISTA OYE CADA PASO DECIDIDO: se suscribe al evento del mapa, traduce el paso a un ciclo y lo publica. Promesa 382
/// (spec 049).
/// </summary>
/// <remarks>
/// DESDE EL 2026-09-24 OYE EL EVENTO DE C, Y EL PUENTE PROVISIONAL SE FUE. Mientras la 048 no estaba en <c>main</c>, la
/// vista decoraba el <c>Decisor</c> del mapa desde fuera (<c>Envolver</c>) y sacaba la pulsada de la línea de progreso.
/// Al juntar A, B, C y D sobraban las dos cosas, y las dos mentían:
/// (1) el envoltorio ve la decisión ANTES del veto de A (390, <c>SurfaceMapTools.DecidirYPulsar</c>), así que con
/// «Guardar» a 0,99 publicaba Actuar=true y el panel decía «Pulsando «Guardar»» sobre algo que no se pulsa; el evento
/// <c>AlDecidir</c> (368) sale con la decisión ya vetada (ec72d96);
/// (2) la regex de la línea terminaba en «· (no )?cambió» y B (353) escribe «cambió de sitio», «dentro» y «delante»: toda
/// pulsación que cambió algo perdía su pulsada. El evento trae el paso con su número (<c>Paso.Numero</c>).
/// Y con las dos fuentes a la vez cada decisión se habría publicado dos veces, con dos números de paso.
///
/// LO QUE CUESTA, Y SE DICE: el evento sale cuando el paso TERMINÓ —decidir, pulsar y la espera—, no al decidir. El panel
/// pinta la decisión después del clic; la flecha sigue volando AL pulsar, porque ese aviso llega por
/// <c>UiaSurface.Pulso</c> (381), que no es parte del puente y se queda.
///
/// NADA DE ESTO TOCA EL DECISOR NI EL PASO. <see cref="Oir"/> no envuelve ni reasigna nada: el interruptor sigue siendo
/// el único que escribe <c>SurfaceMapTools.Decisor</c>. El evento sale en el hilo del paso; <c>Publicar</c> solo encola
/// (382), y si traducir o publicar lanza, se dice aquí con la cadena entera y el paso sigue —el mapa también lo ataja,
/// pero sin decir que era la vista—.
/// </remarks>
public static class ObservadorDelDecisor
{
    /// <summary>Los mapas que ya se oyen: oír dos veces el mismo publicaría cada paso dos veces, con dos números de paso.</summary>
    private static readonly ConditionalWeakTable<SurfaceMapTools, object> Oidos = new();

    /// <summary>
    /// La vista oye cada paso decidido de <paramref name="mapa"/>: <see cref="CicloDe(SurfaceMapTools.PasoDecidido)"/> y
    /// <paramref name="publicar"/>. Devuelve <c>true</c> si se suscribió ahora y <c>false</c> si ya lo oía: el primer
    /// <paramref name="publicar"/> es el que queda.
    /// </summary>
    public static bool Oir(SurfaceMapTools mapa, Action<CicloDeJev> publicar)
    {
        ArgumentNullException.ThrowIfNull(mapa);
        ArgumentNullException.ThrowIfNull(publicar);
        lock (Oidos)
        {
            if (Oidos.TryGetValue(mapa, out _)) return false;
            Oidos.Add(mapa, publicar);
        }
        mapa.AlDecidir += paso =>
        {
            try { publicar(CicloDe(paso)); }
            catch (Exception e)
            {
                // LA CADENA ENTERA (patrón nº3), y cuentas, NUNCA texto de pantalla (spec 049 §Con qué se juzga).
                string causa = "";
                for (var x = e; x != null; x = x.InnerException)
                    causa += $"{x.GetType().Name}: {x.Message}" + (x.InnerException != null ? " ← " : "");
                LogBus.Log("jev-vista", $"✘ la vista no pudo pintar un paso decidido de {paso.Ofrecidas.Count} ofrecida(s) y {paso.Ms.Decidir} ms: {causa}. "
                    + "El paso sigue; la vista se queda con lo anterior.");
            }
        };
        return true;
    }

    /// <summary>
    /// EL PASO DECIDIDO, COMO CICLO: lo que el evento de la 048 publicó, tal como lo pinta la vista. La decisión, ya
    /// vetada (390); la pulsada, la del paso; y la caja de cada candidata, solo si sigue siendo de lo que se ve.
    /// </summary>
    /// <remarks>
    /// LA PULSADA ES <c>Paso.Numero</c>, y solo si la mano actuó y TERMINÓ: «no terminó» es que no se pulsó (la elegida no
    /// estaba y no hubo segunda, o el tope), y decir «pulsé» sería mentira. Es el número que la mano sacó del id
    /// (<c>id.Substring(0, id.IndexOf(')'))</c>), el mismo que <see cref="CandidataDeJev.NumeroDelId"/>.
    ///
    /// LAS CAJAS, SOLO SI EL PASO NO CAMBIÓ LO QUE SE VE (<c>Paso.QueCambio == Nada</c>). El evento sale cuando el paso
    /// acabó, espera incluida: si la mano cambió de sitio, dentro o delante, las cajas leídas son de la pantalla de antes, y
    /// pintarlas sobre la nueva es la caja que miente (patrón nº8). Van sin caja —se ofrecen, se cuentan y no se pintan:
    /// 375—, igual que las del terreno. Si llegaran menos cajas que ofrecidas (el paso no llegó a leer), tampoco: no se
    /// emparejan a ojo.
    ///
    /// SIN DECISIÓN (decisor apagado, pantalla sin nombre, nada accionable, el decisor lanzó) el ciclo sale igual, sin
    /// decisión y sin pulsada: un paso no ejecutado deja rastro (patrón nº10). Así el número de paso de la vista es el del
    /// tramo. Y lleva el porqué del paso: con <c>map_decidir</c> no viene ninguna línea detrás que lo diga.
    ///
    /// «NO SE PULSÓ» SE SABE AQUÍ, Y SOLO AQUÍ (<see cref="CicloDeJev.NoSePulso"/>, revisión de la fusión, 2026-09-24): el
    /// evento sale con el paso terminado, así que un paso sin «actuó y terminó» es uno en que la mano no pulsó nada. Sin esto
    /// el panel leía la pulsada en <c>null</c> como «todavía no se sabe» y decía «Pulsando» con la elegida resaltada.
    /// </remarks>
    public static CicloDeJev CicloDe(SurfaceMapTools.PasoDecidido paso)
    {
        ArgumentNullException.ThrowIfNull(paso);
        var ofrecidas = paso.Ofrecidas ?? Array.Empty<string>();
        var candidatas = paso.Candidatas ?? Array.Empty<SurfaceMapTools.Candidata>();
        bool mismaPantalla = paso.Paso.QueCambio == HuellaDeLoQueSeVe.QueCambio.Nada;
        IReadOnlyList<Rect>? cajas = mismaPantalla && candidatas.Count == ofrecidas.Count
            ? candidatas.Select(c => c.Caja).ToList()
            : null;
        // VACÍO NO ES AUSENTE (patrón nº9): un número en blanco es que no se sabe cuál fue.
        string? pulsada = paso.Paso.Actuo && paso.Paso.Termino && !string.IsNullOrWhiteSpace(paso.Paso.Numero)
            ? paso.Paso.Numero
            : null;
        bool noSePulso = !(paso.Paso.Actuo && paso.Paso.Termino);
        string porQue = noSePulso ? (paso.Paso.Porque ?? "").Trim() : "";

        if (paso.Decision != null)
            return CicloDe(paso.Objetivo, ofrecidas, paso.Decision, paso.Ms.Decidir, cajas)
                with { Pulsada = pulsada, NoSePulso = noSePulso, PorQueNoSePulso = porQue };
        return new CicloDeJev
        {
            Objetivo = paso.Objetivo ?? "",
            Candidatas = Candidatas(ofrecidas, cajas),
            MsDecidir = paso.Ms.Decidir,
            Fase = FaseDelCiclo.Decidido,
            NoSePulso = noSePulso,
            PorQueNoSePulso = porQue,
        };
    }

    /// <summary>
    /// LA TRADUCCIÓN ENTERA de una decisión a lo que pinta la vista. Sus argumentos son los campos del evento de la 048
    /// (<c>PasoDecidido</c>: <c>Objetivo</c>, <c>Ofrecidas</c>, <c>Decision</c>, <c>Ms.Decidir</c> y la <c>Caja</c> de
    /// cada candidata); <see cref="CicloDe(SurfaceMapTools.PasoDecidido)"/> pasa por aquí (aprendizaje nº16).
    /// </summary>
    /// <remarks>
    /// <see cref="DecisionDeUnPaso"/> Y <see cref="DecisionDeJev"/> NO SIGNIFICAN LO MISMO en dos campos, y aquí se
    /// traduce, no se copia (hallazgo de la fase 2 de la 049). (1) <c>Puerta</c> viene VACÍA cuando no se actúa
    /// (<c>DecisionDeUnPaso.No</c>, y también la vetada: <c>ConVeto</c>), y el panel necesita la elegida también entonces
    /// para decir «"Grabar" no se deshace»: es la primera de la distribución, que llega de mayor a menor (288). (2)
    /// <c>Cumplido</c> vale 0 cuando no se preguntó, y solo se pregunta con distribución: sin ella es <c>null</c> y el
    /// medidor enseña «—» (373), no un 0 que parece medido. Con distribución se da por preguntado, y es la única lectura
    /// posible: un transporte viejo que no trae la noul deja 0 igual (<c>ElDecisor.cs:183</c>), y ahí el medidor dirá 0.00
    /// sin que nadie lo haya medido; ninguna comprobación lo distingue (dicho en la spec).
    /// <c>Ausente</c> no viaja en <see cref="DecisionDeUnPaso"/> y va <c>null</c> siempre. El <c>Veto</c> (390) se copia
    /// tal cual: es lo que distingue una vetada de cualquier otro «no», y el panel la dice «vetada» por él (374).
    /// </remarks>
    /// <param name="objetivo">Lo que el tramo quiere conseguir.</param>
    /// <param name="ofrecidas">Las ids tal como se le ofrecieron al decisor, en su orden.</param>
    /// <param name="decision">Lo que devolvió el decisor, ya con el veto.</param>
    /// <param name="msDecidir">Lo que tardó en decidir.</param>
    /// <param name="cajas">
    /// La caja de cada ofrecida, en PARALELO y en el mismo orden (285), o <c>null</c> si no se sabe. <see cref="Rect.Empty"/>
    /// es «sin caja leída» (terreno, dynpro: 365), y esa candidata se ofrece pero no se pinta.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Si hay cajas y no son tantas como las ofrecidas: emparejarlas a ojo pintaría la caja de una sobre el id de otra,
    /// que es la caja que miente (aprendizaje nº4).
    /// </exception>
    public static CicloDeJev CicloDe(string objetivo, IReadOnlyList<string> ofrecidas, DecisionDeUnPaso decision, long msDecidir, IReadOnlyList<Rect>? cajas)
    {
        ArgumentNullException.ThrowIfNull(ofrecidas);
        ArgumentNullException.ThrowIfNull(decision);
        if (cajas != null && cajas.Count != ofrecidas.Count)
            throw new ArgumentException(
                $"llegaron {cajas.Count} caja(s) para {ofrecidas.Count} ofrecida(s): van en paralelo, una por id y en su orden, y no se emparejan a ojo",
                nameof(cajas));

        bool conDistribucion = decision.Alternativas.Count > 0;
        return new CicloDeJev
        {
            Objetivo = objetivo ?? "",
            Candidatas = Candidatas(ofrecidas, cajas),
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
                Veto = decision.Veto ?? "",
            },
            MsDecidir = msDecidir,
            Fase = FaseDelCiclo.Decidido,
        };
    }

    /// <summary>Las candidatas de las ids ofrecidas, con su caja si la hay; <paramref name="cajas"/> ya viene emparejada.</summary>
    private static List<CandidataDeJev> Candidatas(IReadOnlyList<string> ofrecidas, IReadOnlyList<Rect>? cajas)
    {
        var candidatas = new List<CandidataDeJev>(ofrecidas.Count);
        for (int i = 0; i < ofrecidas.Count; i++)
        {
            var (etiqueta, tipo) = EtiquetaYTipo(ofrecidas[i]);
            Rect? caja = cajas != null && !cajas[i].IsEmpty ? cajas[i] : null;
            candidatas.Add(new CandidataDeJev { Id = ofrecidas[i], Etiqueta = etiqueta, Tipo = tipo, Caja = caja, EsLeida = caja != null });
        }
        return candidatas;
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
}
