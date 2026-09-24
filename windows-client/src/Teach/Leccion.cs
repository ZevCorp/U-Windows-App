using U.WindowsClient.Navigation;

namespace U.WindowsClient.Teach;

/// <summary>Un clic físico que vio el vigía, en el reloj de la demo: dónde cayó, qué era, y a dónde llevó.</summary>
/// <param name="Selector">La identidad que el vigía resolvió al pulsar (SAP o UIA). Vacío si no resolvió.</param>
/// <param name="Etiqueta">El NOMBRE con el que el terreno conoce esa puerta («Pto.tbjo.clínico», «comando»).
/// Es lo que las herramientas entienden: <c>map_take exit=&lt;Etiqueta&gt;</c>.</param>
/// <param name="DeU">El clic cayó en la propia ventana de Ü: no es parte de la tarea.</param>
/// <param name="PantallaAlPulsar">La pantalla que había en el INSTANTE de pulsar. Es la llegada del clic anterior.</param>
public sealed record ClicVisto(long HoraMs, int X, int Y, string Llegada = "",
    string Selector = "", string Etiqueta = "", string Tipo = "", bool DeU = false, string PantallaAlPulsar = "");

/// <summary>Un paso que observó la superficie (SAP o UIA), en el reloj de la demo.</summary>
/// <param name="Superficie">Dónde OCURRIÓ el paso (la pantalla de antes), como lo da el grabador.</param>
public sealed record PasoVisto(long HoraMs, string Selector, string Etiqueta, string Tipo,
    string Texto, string Tecla, string Superficie);

/// <summary>Un cuadro de la demo tal como se anota en la lección.</summary>
public sealed record CuadroDeLaLeccion(long HoraMs, string Ruta, int CursorX, int CursorY, int Ancho, int Alto);

/// <summary>Un evento de la lección: lo que pasó en un instante, con sus dos cuadros y lo dicho.</summary>
/// <param name="Tipo">«clic» o «teclado».</param>
/// <param name="Etiqueta">El nombre de puerta del terreno. Es lo que hay que darle a <c>map_take</c>.</param>
/// <param name="PorTeclado">Un paso de SAP que no tuvo clic cerca: se hizo con el teclado.</param>
public sealed record EventoDeLaLeccion(int N, long HoraMs, string Tipo, int X, int Y,
    string Selector, string Etiqueta, string Texto, string Tecla, string Llegada,
    string CuadroAntes, string CuadroDespues, bool Asentado, IReadOnlyList<string> Dicho, bool PorTeclado);

/// <summary>LA LECCIÓN: una demostración escrita como línea de tiempo con un solo reloj. Spec 013.</summary>
public sealed record Leccion(string Id, string Empezo, string Termino, long DuracionMs, string Mp4,
    IReadOnlyList<EventoDeLaLeccion> Eventos, IReadOnlyList<FraseDicha> Frases,
    IReadOnlyList<CuadroDeLaLeccion> Cuadros, string Contexto = "");

/// <summary>
/// DE LO VISTO A LOS EVENTOS. Promesas 170 y 171 (spec 013). Puro: solo horas y listas.
/// </summary>
/// <remarks>
/// CADA CLIC FÍSICO ES UN EVENTO, tenga o no paso de SAP (170). El grabador de SAP emite un paso por
/// VIAJE al servidor, no por clic: 26 clics de árbol dieron 1 paso, medido dos veces (spec 009). Lo
/// que Ü hizo con las manos y no viajó no entraba en la skill. Aquí el eje es el vigía, que ve
/// todos los clics.
///
/// LA IDENTIDAD DE UN CLIC LA DA EL VIGÍA, NO SAP. Primera prueba real (2026-09-07): siete clics y
/// ninguno con selector, porque solo tomé x,y del gancho y esperé que SAP nombrara el clic. SAP
/// nombra viajes; el vigía nombra clics, en el instante de pulsar, con la MISMA etiqueta con la que
/// el terreno conoce sus puertas —que es lo único que <c>map_take</c> entiende—. El piloto tardó
/// tres minutos y casi cuatro dólares en tantear esos nombres. Ahora viajan en el evento.
///
/// UN PASO DE SAP SE CUELGA DEL ÚLTIMO CLIC ANTERIOR (171). El grabador observa el paso cuando SAP
/// CONTESTA: en la prueba real fueron 3,07 s después del clic en el campo de comando (teclear
/// «nwp1» y Enter) y 2,54 s después del clic en el árbol. Una ventana de «el más cercano en 1,5 s»
/// los dejaba a todos huérfanos. Lo que un paso cuenta es qué pasó DESPUÉS de un clic: se casa con
/// el último clic anterior dentro de <see cref="VentanaDelPasoMs"/>, y una tecla (Enter) se pliega en
/// ese mismo evento —como ya hace la promesa 132 con las skills—. Sin clic anterior, fue por teclado.
///
/// LO DICHO se reparte con el anclador de siempre (105): una frase, UN evento, el más cercano.
/// </remarks>
public static class ArmarLaLeccion
{
    /// <summary>Cuánto después de un clic puede llegar el paso de SAP que ese clic provocó.</summary>
    /// <remarks>8 s: teclear un código de transacción y pulsar Enter tardó 3,07 s en la prueba real;
    /// un viaje lento de SAP puede irse a varios segundos. Más allá de ocho ya es otra cosa.</remarks>
    public const int VentanaDelPasoMs = 8000;

    public static IReadOnlyList<EventoDeLaLeccion> Eventos(IReadOnlyList<ClicVisto> clics,
        IReadOnlyList<PasoVisto> pasos, IReadOnlyList<FraseDicha> frases, IReadOnlyList<Cuadro> cuadros)
    {
        clics ??= Array.Empty<ClicVisto>(); pasos ??= Array.Empty<PasoVisto>();
        frases ??= Array.Empty<FraseDicha>(); cuadros ??= Array.Empty<Cuadro>();

        // 1) Cada clic que no sea sobre Ü, un evento, con la identidad que el vigía le dio.
        var eventos = clics.Where(c => !c.DeU).OrderBy(c => c.HoraMs).Select(c => new Borrador
        {
            Hora = c.HoraMs, Tipo = "clic", X = c.X, Y = c.Y, Selector = c.Selector ?? "",
            Etiqueta = c.Etiqueta ?? "", Llegada = c.Llegada ?? "",
        }).ToList();

        // 2) Cada paso, al último clic anterior dentro de la ventana; si no hay, evento propio.
        //
        // LOS PASOS POR PULSACIÓN SE PLIEGAN: la superficie UIA emite un paso por tecla («n», «nw»,
        // «nwp», «nwp1» sobre el mismo campo) y la cuarta prueba real (2026-09-07) salió con 35
        // eventos y 70 cuadros por eso. Pasos SEGUIDOS sobre el mismo selector, los dos con texto,
        // son uno: el último, que trae el texto completo.
        var plegados = new List<PasoVisto>();
        foreach (var p in pasos.OrderBy(p => p.HoraMs))
        {
            var ultimo = plegados.Count > 0 ? plegados[^1] : null;
            if (ultimo != null && ultimo.Selector.Length > 0 && ultimo.Selector == p.Selector
                && ultimo.Texto.Length > 0 && p.Texto.Length > 0)
                plegados[^1] = p;
            else plegados.Add(p);
        }
        foreach (var p in plegados)
        {
            Borrador? dueño = null;

            // POR IDENTIDAD ANTES QUE POR TIEMPO (promesa 184). Con eventos COM, SAP publica lo
            // tecleado solo cuando la pantalla viaja; si la demo acaba sin viajar, todo se descarga
            // al parar, con la hora de parar. Por cercanía iría al último clic —o a ninguno—, no al
            // campo que cada tecleo abrió. El vigía nombra el clic en un campo con el MISMO selector
            // que el grabador le pone al paso, y esa identidad es la que cuadra.
            if (p.Selector.Length > 0)
                dueño = eventos.LastOrDefault(e => e.Tipo == "clic" && e.Hora <= p.HoraMs && e.Selector == p.Selector);

            if (dueño == null)
            for (int i = eventos.Count - 1; i >= 0; i--)
            {
                var e = eventos[i];
                if (e.Tipo != "clic") continue;
                if (e.Hora > p.HoraMs) continue;
                if (p.HoraMs - e.Hora > VentanaDelPasoMs) break;
                // Un clic absorbe UN paso con selector (más su tecla). Si ya tiene uno, este paso es otro gesto.
                if (p.Selector.Length > 0 && e.SelectorDePaso) continue;
                dueño = e; break;
            }
            if (dueño == null)
            {
                eventos.Add(new Borrador
                {
                    Hora = p.HoraMs, Tipo = "teclado", Selector = p.Selector ?? "", Etiqueta = p.Etiqueta ?? "",
                    Texto = p.Texto ?? "", Tecla = p.Tecla ?? "", PorTeclado = true,
                });
                continue;
            }
            if (p.Selector.Length > 0)
            {
                // LA PUERTA QUE SAP VIO MANDA (promesa 171, 2026-09-08): el vigía nombró el clic en el
                // botón «Triage» de la barra de la rejilla como la fila «GIRALDO» —el botón vive dentro
                // del shell de la rejilla y la geometría dio la rejilla—; SAP publicó el botón. Si SAP
                // trae OTRA puerta que la adivinada, el evento se queda con la de SAP entera.
                bool otraPuerta = dueño.Selector.Length > 0 && dueño.Selector != p.Selector && (p.Etiqueta ?? "").Length > 0;
                dueño.Selector = p.Selector; dueño.SelectorDePaso = true;
                if (dueño.Etiqueta.Length == 0 || otraPuerta) dueño.Etiqueta = p.Etiqueta ?? "";
                if ((p.Texto ?? "").Length > 0) dueño.Texto = p.Texto!;
            }
            if ((p.Tecla ?? "").Length > 0 && dueño.Tecla.Length == 0) dueño.Tecla = p.Tecla!;
        }
        eventos.Sort((a, b) => a.Hora.CompareTo(b.Hora));

        // 3) Las frases, una a un solo evento, el más cercano (promesa 105 reutilizada).
        var voz = AncladorDeVoz.Ancla(frases, eventos.Select(e => e.Hora).ToList());

        // 4) Los dos cuadros de cada evento, del pasado y del asentamiento (168 y 169).
        var salida = new List<EventoDeLaLeccion>();
        for (int i = 0; i < eventos.Count; i++)
        {
            var e = eventos[i];
            var antes = CuadroDeAntes.Elegir(cuadros, e.Hora);
            var despues = CuadroDeDespues.Elegir(cuadros, e.Hora);
            string dicho = i < voz.DichoPorPaso.Count ? voz.DichoPorPaso[i] : "";
            salida.Add(new EventoDeLaLeccion(i + 1, e.Hora, e.Tipo, e.X, e.Y, e.Selector, e.Etiqueta, e.Texto, e.Tecla,
                e.Llegada, antes?.Ruta ?? "", despues?.Cuadro.Ruta ?? "", despues?.Asentado ?? false,
                dicho.Length > 0 ? new[] { dicho } : Array.Empty<string>(), e.PorTeclado));
        }
        return salida;
    }

    private sealed class Borrador
    {
        public long Hora; public string Tipo = "clic"; public int X, Y;
        public string Selector = "", Etiqueta = "", Texto = "", Tecla = "", Llegada = "";
        public bool PorTeclado, SelectorDePaso;
    }

    /// <summary>
    /// A DÓNDE LLEVÓ CADA CLIC: lo que el TERRENO aprendió. Promesa 178 (spec 013).
    /// </summary>
    /// <remarks>
    /// UNA SOLA FUENTE, Y NO ES LA LECCIÓN. El terreno vivo (<c>MapaVivo</c>) aprende desde agosto
    /// que «desde esta pantalla, esta puerta lleva a aquella»: sondea la ubicación, y cuando cambia
    /// dentro de la misma app se lo atribuye al último clic del vigía (promesas 46 y 77). Esa arista
    /// es la que <c>map_batch</c> consulta al verificar cada paso. En las demos del 2026-09-07 el
    /// terreno la aprendió BIEN («Favoritos/IS-H: Pto.tbjo.clínico» lleva de SESSION_MANAGER a
    /// NWP1/0100) mientras la lección, calculándola por su cuenta —con reloj primero, con «la
    /// pantalla del clic siguiente» después—, la grabó MAL dos veces. El dueño lo dijo: «que tengamos
    /// una solución sólida estándar en vez de múltiples soluciones a lo mismo». Esta es la estándar.
    ///
    /// LO QUE EL TERRENO NO APRENDIÓ QUEDA VACÍO, y se dice: un clic sin arista es o un clic que no
    /// navegó, o una transición que el terreno no supo atribuir (su ventana de 6 s). Inventar una
    /// llegada ahí es exactamente lo que hizo que el juez castigara al piloto por llegar bien.
    /// Solo el ÚLTIMO clic tiene respaldo: donde acabó la demo, que es un hecho leído al parar.
    ///
    /// <paramref name="terreno"/>: (pantalla al pulsar, selector, etiqueta) → destino, o vacío.
    /// </remarks>
    public static IReadOnlyList<ClicVisto> Llegadas(IReadOnlyList<ClicVisto> clics, string dondeTermino,
        Func<string, string, string, string>? terreno)
    {
        var lista = (clics ?? Array.Empty<ClicVisto>()).OrderBy(c => c.HoraMs).ToList();
        var deLaTarea = lista.Where(c => !c.DeU).ToList();
        var llegada = new Dictionary<ClicVisto, string>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < deLaTarea.Count; i++)
        {
            var c = deLaTarea[i];
            string l = "";
            try { l = (terreno?.Invoke(c.PantallaAlPulsar ?? "", c.Selector ?? "", c.Etiqueta ?? "") ?? "").Trim(); } catch { }
            // SAP VISTO POR UIA NO ES UNA LLEGADA (promesa 178, 2026-09-08): si el clic se dio DENTRO
            // de una sesión de SAP y el terreno dice que llevó a «uia://saplogon.exe/…», eso es la
            // misma ventana identificada por el ojo equivocado mientras SAP no contestaba. Un evento
            // con esa llegada cuenta como navegante y no puede aterrizar jamás.
            if (Mundos.EsSap(c.PantallaAlPulsar ?? "") && Mundos.EsSapVistoPorUia(l)) l = "";
            if (l.Length == 0 && i == deLaTarea.Count - 1) l = (dondeTermino ?? "").Trim();
            llegada[c] = l;
        }
        return lista.Select(c => llegada.TryGetValue(c, out var l) ? c with { Llegada = l } : c).ToList();
    }

    /// <summary>
    /// LOS CLICS CON LA IDENTIDAD QUE SAP VIO, para preguntarle al terreno por las llegadas con la
    /// puerta correcta. Promesa 178, extendida el 2026-09-08.
    /// </summary>
    /// <remarks>
    /// POR QUÉ: el terreno había aprendido que el botón «Triage» lleva al formulario (189), y la
    /// lección salió con esa llegada vacía: las llegadas se buscaban con la identidad del vigía —la
    /// fila «GIRALDO», adivinada por geometría— y solo después <see cref="Eventos"/> la sustituía
    /// por la de SAP. Se reusa <see cref="Eventos"/> a propósito: es la ÚNICA regla de qué paso cuelga
    /// de qué clic, y aquí solo se lee el resultado por la hora del clic.
    /// </remarks>
    public static IReadOnlyList<ClicVisto> ConLaIdentidadDeSap(IReadOnlyList<ClicVisto> clics, IReadOnlyList<PasoVisto> pasos)
    {
        clics ??= Array.Empty<ClicVisto>();
        if (pasos == null || pasos.Count == 0) return clics.ToList();
        var eventos = Eventos(clics, pasos, Array.Empty<FraseDicha>(), Array.Empty<Cuadro>());
        var porHora = new Dictionary<long, EventoDeLaLeccion>();
        foreach (var e in eventos) if (e.Tipo == "clic" && !porHora.ContainsKey(e.HoraMs)) porHora[e.HoraMs] = e;
        return clics.Select(c =>
            !c.DeU && porHora.TryGetValue(c.HoraMs, out var e) && e.Selector.Length > 0
                && (e.Selector != (c.Selector ?? "") || e.Etiqueta != (c.Etiqueta ?? ""))
                ? c with { Selector = e.Selector, Etiqueta = e.Etiqueta }
                : c).ToList();
    }

    /// <summary>Lo dicho que no le quedó cerca a ningún evento: el contexto de la lección.</summary>
    public static string Contexto(IReadOnlyList<FraseDicha> frases, IReadOnlyList<EventoDeLaLeccion> eventos)
        => AncladorDeVoz.Ancla(frases ?? Array.Empty<FraseDicha>(), (eventos ?? Array.Empty<EventoDeLaLeccion>()).Select(e => e.HoraMs).ToList()).Contexto;
}

/// <summary>
/// ¿SE ENTREGA ESTA LECCIÓN? Promesa 172 (spec 013). Entera o nada, y con el motivo.
/// </summary>
/// <remarks>
/// UNA LECCIÓN A MEDIAS ES PEOR QUE NINGUNA: sin cuadros el piloto «vería» una demo que no puede ver
/// y describiría lo que imagina; sin mp4 el humano no puede revisar qué enseñó. Se dice qué falta,
/// porque «no se entregó» sin motivo es lo que hace que la próxima persona lo desactive.
/// </remarks>
public static class LaEntregaDeLaLeccion
{
    public readonly record struct Veredicto(bool Entregable, string Motivo);

    public static Veredicto Juzgar(bool tieneMp4, int cuadros, int eventos)
    {
        var faltas = new List<string>();
        if (!tieneMp4) faltas.Add("no hay mp4 de la demo");
        if (cuadros <= 0) faltas.Add("la cámara no dejó ningún cuadro");
        if (eventos <= 0) faltas.Add("no hubo ni un clic ni un paso que enseñar");
        return faltas.Count == 0
            ? new(true, $"lección completa: {eventos} evento(s), {cuadros} cuadro(s) y el video")
            : new(false, "la lección no se entrega: " + string.Join("; ", faltas));
    }
}

/// <summary>
/// EL CUADRO DE UN MOMENTO CUALQUIERA. Promesa 177 (spec 013): el piloto puede pedir la pantalla de
/// un segundo exacto de la demo, aunque ahí no hubiera clic.
/// </summary>
/// <remarks>
/// Pedido del dueño (2026-09-06): «yo puedo solo señalar algo y decir "esta es la opción que
/// usarías"». Eso no deja evento; deja una frase con hora. El piloto lee la frase, pide el cuadro
/// de esa hora y ve el anillo del ratón sobre lo señalado. El más cercano, sin inventar: si no hay
/// cuadros, null.
/// </remarks>
public static class CuadroDelMomento
{
    public static CuadroDeLaLeccion? Elegir(IReadOnlyList<CuadroDeLaLeccion> cuadros, long horaMs)
    {
        if (cuadros == null || cuadros.Count == 0) return null;
        CuadroDeLaLeccion? mejor = null; long dist = long.MaxValue;
        foreach (var c in cuadros)
        {
            long d = Math.Abs(c.HoraMs - horaMs);
            if (d < dist) { dist = d; mejor = c; }
        }
        return mejor;
    }
}

/// <summary>De qué mundo es una URL de pantalla, en UN sitio. Promesas 178 y 189.</summary>
public static class Mundos
{
    public static bool EsSap(string url) => (url ?? "").StartsWith("sapgui://", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// La ventana de SAP GUI vista por UIA («uia://saplogon.exe/…»): el ojo equivocado sobre una
    /// sesión, que aparece cuando SAP no contesta al scripting. No es un destino al que se llegue.
    /// </summary>
    public static bool EsSapVistoPorUia(string url) =>
        (url ?? "").StartsWith("uia://saplogon.exe/", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// ¿Es una sesión de SAP, en cualquiera de sus DOS identidades? <c>sapgui://</c> cuando el Scripting contesta, y
    /// «uia://sap….exe/…» cuando no —SAP Busy, <c>Identity()</c> sin contestar— y <c>SurfaceLocator</c> cae al esquema
    /// uia://. Es la misma sesión y, desde UIA, el mismo Pane opaco.
    /// </summary>
    /// <remarks>
    /// NACE AL JUNTAR A Y B (2026-09-24), por el aprendizaje nº16. La 046 lo descubrió para la política de lo que viaja
    /// (393) y lo resolvió dentro de ella; la 047 excluía SAP de la espera por huella con <see cref="EsSap"/>, que solo ve
    /// sapgui://. Juntas, «uia://saplogon.exe/…» salía «asentada» en el primer respiro con SAP aún ocupado: la carrera del
    /// Busy. El proceso se reconoce con el criterio que acuñó esa identidad (<c>SurfaceLocator.IsSap</c>: empieza por
    /// «sap»), no con una lista de versiones: <see cref="EsSapVistoPorUia"/> solo conoce saplogon.exe, y se queda como
    /// está porque <c>Leccion</c> la usa para otra cosa (tirar una llegada vista por el ojo equivocado).
    /// </remarks>
    public static bool EsSesionDeSap(string url)
    {
        string u = url ?? "";
        if (EsSap(u)) return true;
        if (!u.StartsWith("uia://", StringComparison.OrdinalIgnoreCase)) return false;
        string origin = U.Graph.SurfacePlace.OriginOf(u);
        return origin.Length > "uia://".Length && Uia.SurfaceLocator.IsSap(origin["uia://".Length..]);
    }
}
