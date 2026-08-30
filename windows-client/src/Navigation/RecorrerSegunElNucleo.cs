namespace U.WindowsClient.Navigation;

/// <summary>
/// RECORRER EN BATCH: varios pasos de una sola llamada, con la compuerta de vida antes de cada uno.
/// </summary>
/// <remarks>
/// ES EL PATRÓN DEL computer_batch DEL AGENT SDK, aplicado a nuestro terreno. Su contrato, medido
/// el 2026-08-24: una llamada lleva una lista de acciones; se ejecutan en secuencia; LA COMPUERTA
/// CORRE ANTES DE CADA UNA (allí: ¿la app del frente está permitida?); a la primera que no pasa, el
/// batch se detiene y el modelo recupera el control con lo que alcanzó a hacer. La velocidad no es
/// magia: es predecir N pasos y pagar UN viaje al modelo en vez de N.
///
/// NUESTRA COMPUERTA ES MEJOR QUE LA SUYA: no «¿está permitida la app?» sino «¿el elemento que toca
/// pulsar está VIVO aquí?». La bandera de vida del núcleo —que ya distingue «lo veo ahora» de «lo
/// recuerdo» (promesas 1, 6 y 11)— puesta en la puerta. Y por selector, no por coordenadas: la
/// regla de la casa, porque las coordenadas fallan en silencio y reportan éxito.
///
/// CADA PASO FABRICA UNA ARISTA. Pulsar pasa por <see cref="PulsarSegunElNucleo"/>, que verifica
/// por consecuencia y cruza el tramo — y aquí la atribución es trivial porque el que pulsó fuimos
/// NOSOTROS. Es la salida al problema de los nodos incomunicados: las aristas entre ubicaciones no
/// se deducen mirando (adivinar clics humanos falla a cada rato, «salto SIN atribuir» en los logs),
/// se GANAN ejecutando (2026-08-24, propuesto por el usuario).
///
/// LA RESPUESTA NUNCA MIENTE: si hizo 3 de 5, dice 3 de 5, dónde quedó, por qué paró y qué SÍ está
/// vivo ahí — lo que el modelo necesita para replanificar sin gastar otra llamada de
/// reconocimiento, igual que el screenshot intercalado del computer_batch.
/// </remarks>
public sealed class RecorrerSegunElNucleo
{
    /// <summary>Un paso: pulsar <paramref name="Exit"/> (etiqueta o selector), o escribir
    /// <paramref name="Texto"/> si viene con texto.</summary>
    public sealed record Paso(string Exit, string Texto = "");

    /// <summary>Qué pasó: cuántos se hicieron, de cuántos, dónde quedamos, y el relato honesto.</summary>
    public readonly record struct Resultado(int Hechos, int Total, string Donde, bool Termino, string Cuenta);

    private readonly Nucleo.Grafo _grafo;
    private readonly Func<string> _donde;
    private readonly PulsarSegunElNucleo _pulsar;
    private readonly Func<string, bool>? _escribir;
    private readonly Func<bool> _hayQueParar;

    /// <param name="escribir">Texto → ¿se pudo escribir en el campo con foco? Lo hace quien sabe de UIA.</param>
    /// <param name="hayQueParar">El freno. Se pregunta antes de CADA paso, no al empezar la tanda.</param>
    public RecorrerSegunElNucleo(Nucleo.Grafo grafo, Func<string> donde, PulsarSegunElNucleo pulsar,
        Func<string, bool>? escribir = null, Func<bool>? hayQueParar = null)
    {
        _grafo = grafo;
        _donde = donde;
        _pulsar = pulsar;
        _escribir = escribir;
        _hayQueParar = hayQueParar ?? (() => false);
    }

    /// <summary>Cuánto se espera a que un elemento aparezca vivo antes de rendirse: la pantalla
    /// nueva tarda en pintarse y en ser leída, y declarar «no está» sin esperar la lectura sería
    /// juzgar la pantalla de antes.</summary>
    public int EsperaMaximaMs { get; init; } = 1800;

    /// <summary>
    /// Qué selectores se pueden INTENTAR aunque no estén a la vista (promesa 80). El batch no
    /// sabe de mundos: quien cablea decide — hoy, las filas de árbol de SAP (sap:…#node=), cuya
    /// clave cargada se alcanza por identidad: seleccionarla la TRAE a la vista. La verificación
    /// por consecuencia sigue juzgando; sin delegado, la compuerta muerde como siempre.
    /// </summary>
    public Func<string, bool>? AccionableAunSinVerse { get; init; }

    public Resultado Recorre(IReadOnlyList<Paso> pasos)
    {
        if (pasos.Count == 0)
            return new(0, 0, _donde() ?? "", true, "no me diste ningún paso.");

        for (int i = 0; i < pasos.Count; i++)
        {
            // EL FRENO SE PREGUNTA ANTES DE CADA PASO, no al arrancar la tanda: una de veinte pasos
            // preguntando solo al principio correría entera con el usuario gritando que pare.
            if (_hayQueParar())
                return Parcial(i, pasos.Count, "paraste tú con Escape; no sigo.", conVivos: false);

            var paso = pasos[i];

            if (paso.Texto.Length > 0)
            {
                if (_escribir == null)
                    return Parcial(i, pasos.Count, "todavía no sé escribir dentro de un batch.", conVivos: false);
                if (!_escribir(paso.Texto))
                    return Parcial(i, pasos.Count, $"no pude escribir «{paso.Texto}».", conVivos: true);
                continue;
            }

            // LA COMPUERTA: el paso solo se pulsa si su elemento está VIVO aquí, y se le da tiempo a
            // la pantalla nueva a pintarse y a ser leída — declarar «no está» sin esperar la lectura
            // sería juzgar la pantalla de ANTES, que es justo el desfase que la compuerta evita.
            var (elegido, homonimos, motivo, aqui) = EsperarloVivo(paso.Exit);

            if (aqui.Length == 0)
                return Parcial(i, pasos.Count, "no sé dónde estoy, y sin eso no pulso nada.", conVivos: false);

            // VARIAS PUERTAS RECLAMAN LO PEDIDO: no se adivina — la misma regla que abrir (promesa
            // 40). Se dan los selectores para que quien pidió elija con conocimiento.
            if (homonimos.Count > 1)
                return Parcial(i, pasos.Count,
                    $"hay {homonimos.Count} puertas vivas para «{paso.Exit}»: "
                    + string.Join(", ", homonimos.Select(h => $"«{h}»"))
                    + ". Dime el selector y sigo.", conVivos: false);

            if (elegido == null)
                return Parcial(i, pasos.Count,
                    motivo ?? $"«{paso.Exit}» no lo conozco en «{aqui}».",
                    conVivos: true);

            // PULSAR pasa por el mismo camino de siempre: verificar por consecuencia (promesas 44 y
            // 45) y cruzar el tramo (46). El batch no inventa una segunda manera de tocar.
            var r = _pulsar.Pulsa(elegido.Que.Selector, elegido.Que.Etiqueta);
            if (!r.SePudo)
                return Parcial(i, pasos.Count, r.Cuenta, conVivos: true);

            // Que la pantalla no cambiara NO para el batch: «Guardar» o «Cortar» hacen su trabajo
            // sin ir a ninguna parte. Quien juzga si el plan sigue teniendo sentido es la compuerta
            // del paso SIGUIENTE — que mira el terreno, no la intención.
        }

        string fin = _donde() ?? "";
        return new(pasos.Count, pasos.Count, fin, true,
            $"hice los {pasos.Count} paso(s): quedaste en «{fin}».");
    }

    /// <summary>
    /// ¿Esta etiqueta nombra lo pedido? El difuso va en UNA SOLA dirección a propósito: lo pedido
    /// puede ser un trozo del nombre real —«Copilot» nombra a «Copilot anclado» (promesa 43)— pero
    /// un trozo de la página NO reclama lo pedido. Con las dos direcciones, los fragmentos de texto
    /// que las webs observan como elementos se tragaban los pasos: «que» reclamó «Paso Que No
    /// Existe» y «El» reclamó «El portal asociado a este artículo» (Wikipedia, 2026-08-24, en la
    /// primera prueba real del batch).
    /// </summary>
    private static bool LoNombra(string etiqueta, string exit)
    {
        string a = Nombres.Aplanar(etiqueta), b = Nombres.Aplanar(exit);
        return a.Length > 0 && b.Length > 0 && (a == b || a.Contains(b, StringComparison.Ordinal));
    }

    /// <summary>La cola de una ubicación —«web://x/Portal:Ajedrez» → «Portal:Ajedrez»—, que es como
    /// una persona (y un modelo) nombra el sitio.</summary>
    private static string Cola(string ubicacion)
    {
        int barra = ubicacion.LastIndexOf('/');
        return barra >= 0 && barra < ubicacion.Length - 1 ? ubicacion[(barra + 1)..] : ubicacion;
    }

    /// <summary>
    /// ¿Esta etiqueta PARECE una puerta? Una página web observa fragmentos de texto como elementos
    /// —«,», «[1]», «, dos», «_r_1fi7_», párrafos enteros— y tratarlos como puertas rompía las dos
    /// mitades del batch a la vez: reclamaban pasos por contención, y llenaban la lista de «vivo
    /// aquí» de escombros con los que no se puede replanificar (Wikipedia, 2026-08-25, medido en la
    /// primera corrida del piloto). Lo EXACTO no pasa por aquí: si alguien pide «[1]» con todas sus
    /// letras, se le da — el filtro es para lo difuso y para las listas, donde adivinar cuesta.
    /// </summary>
    private static bool ParecePuerta(string etiqueta)
    {
        string t = etiqueta.Trim();
        if (t.Length == 0 || t.Length > 80) return false;   // un párrafo no es una puerta
        return char.IsLetterOrDigit(t[0]);                  // «, dos», «[1]», «_r_1fi7_», «(beta)»…
    }

    /// <summary>
    /// LA ESCALERA DE RESOLUCIÓN, esperando a que la pantalla se pinte y se lea. En orden:
    ///
    ///   1. SELECTOR exacto — quien lo da ya eligió, y con homónimos es lo único que distingue.
    ///   2. ETIQUETA viva — exacto primero, difuso después y solo entre lo que parece puerta.
    ///   3. DESTINO de una arista viva — exacto por la cola primero, difuso después.
    ///
    /// El peldaño 3 es la lección de la primera corrida real (2026-08-25): el modelo pide a dónde
    /// QUIERE IR —«Portal:Ajedrez»— y la puerta se llama «El portal asociado a este artículo». El
    /// terreno tenía la arista aprendida y aun así costó 24 viajes, porque nadie la consultaba.
    /// Pedir el destino vale tanto como pedir la puerta: es exactamente para esto que el batch
    /// fabrica aristas (promesa 58) — un grafo que gana aristas que luego nadie usa es un mapa de
    /// adorno.
    ///
    /// Devuelve el elegido, o los homónimos si hay empate, o el MOTIVO ya redactado del fallo —
    /// que distingue «lo conozco pero no lo veo», «sé llegar por X pero X no está viva» y «no lo
    /// conozco», porque al modelo le sirven distinto (promesa 15 del núcleo, hablando por el batch).
    /// </summary>
    private (Nucleo.Alcanzable? Elegido, IReadOnlyList<string> Homonimos, string? Motivo, string Aqui)
        EsperarloVivo(string exit)
    {
        var nada = Array.Empty<string>();
        for (int ido = 0; ; ido += 120)
        {
            string aqui = _donde() ?? "";
            if (aqui.Length > 0)
            {
                var todos = _grafo.DesdeAqui(aqui);

                // 1. El selector exacto manda.
                var porSelector = todos.FirstOrDefault(
                    a => a.Que.Selector.Equals(exit, StringComparison.Ordinal));
                if (porSelector is { Vivo: true }) return (porSelector, nada, null, aqui);

                if (porSelector == null)
                {
                    var vivas = todos.Where(a => a.Vivo).ToList();

                    // 2. ETIQUETA: lo exacto gana a lo difuso. «El» no puede tragarse «El portal
                    // asociado a este artículo», pero «Copilot» sí nombra a «Copilot anclado»
                    // (promesa 43) — por eso el difuso existe, filtrado a lo que parece puerta.
                    var exactas = vivas.Where(
                        a => Nombres.Aplanar(a.Que.Etiqueta) == Nombres.Aplanar(exit)).ToList();
                    var candidatas = exactas.Count > 0 ? exactas
                        : vivas.Where(a => ParecePuerta(a.Que.Etiqueta) && LoNombra(a.Que.Etiqueta, exit)).ToList();

                    if (candidatas.Count == 1) return (candidatas[0], nada, null, aqui);
                    if (candidatas.Count > 1)
                        return (null, candidatas.Select(v => v.Que.Selector).ToList(), null, aqui);

                    // 3. DESTINO: la arista aprendida contesta por la puerta.
                    var conDestino = vivas.Where(a => a.Destino.Length > 0).ToList();
                    var destExactas = conDestino.Where(
                        a => Nombres.Aplanar(Cola(a.Destino)) == Nombres.Aplanar(exit)).ToList();
                    var destinos = destExactas.Count > 0 ? destExactas
                        : conDestino.Where(a => Nombres.Aplanar(Cola(a.Destino))
                            .Contains(Nombres.Aplanar(exit), StringComparison.Ordinal)).ToList();

                    if (destinos.Count == 1) return (destinos[0], nada, null, aqui);
                    if (destinos.Count > 1)
                        return (null, destinos.Select(v => v.Que.Selector).ToList(), null, aqui);
                }

                if (ido >= EsperaMaximaMs)
                {
                    // LA FILA DESPLAZADA SE INTENTA (promesa 80): conocida y accionable por
                    // identidad —lo dice el delegado, no el batch—, se pulsa aunque no se vea;
                    // la consecuencia juzga. El atasco real: tras un relogin el árbol de NWP1
                    // quedó arriba y «Triage» —cruzada, con destino— quedó fuera de la vista
                    // (2026-08-30, la corrida completa del piloto).
                    if (AccionableAunSinVerse != null)
                    {
                        var desplazada = todos.FirstOrDefault(a => !a.Vivo
                            && AccionableAunSinVerse(a.Que.Selector)
                            && (a.Que.Selector.Equals(exit, StringComparison.Ordinal)
                                || Nombres.Aplanar(a.Que.Etiqueta) == Nombres.Aplanar(exit)
                                || (a.Destino.Length > 0
                                    && Nombres.Aplanar(Cola(a.Destino)) == Nombres.Aplanar(exit))));
                        if (desplazada != null) return (desplazada, nada, null, aqui);
                    }

                    // Se acabó la espera: se redacta el motivo MÁS útil que el terreno permita.
                    if (porSelector != null)
                        return (null, nada, $"«{exit}» lo conozco aquí pero AHORA no lo veo.", aqui);

                    if (todos.Any(a => !a.Vivo && (Nombres.Aplanar(a.Que.Etiqueta) == Nombres.Aplanar(exit)
                            || (ParecePuerta(a.Que.Etiqueta) && LoNombra(a.Que.Etiqueta, exit)))))
                        return (null, nada, $"«{exit}» lo conozco aquí pero AHORA no lo veo.", aqui);

                    // La puerta muerta con destino conocido se dice CON su nombre: es justo lo que
                    // el siguiente intento necesita para no adivinar.
                    var muertaConDestino = todos.FirstOrDefault(a => !a.Vivo && a.Destino.Length > 0
                        && Nombres.Aplanar(Cola(a.Destino)).Contains(Nombres.Aplanar(exit), StringComparison.Ordinal));
                    if (muertaConDestino != null)
                        return (null, nada,
                            $"sé llegar a «{exit}» por «{muertaConDestino.Que.Etiqueta}», pero esa "
                            + "puerta ahora no está viva.", aqui);

                    return (null, nada, $"«{exit}» no lo conozco en «{aqui}».", aqui);
                }
            }
            else if (ido >= EsperaMaximaMs)
                return (null, nada, null, "");

            System.Threading.Thread.Sleep(120);
        }
    }

    /// <summary>
    /// El relato de una tanda que no terminó: cuánto se hizo, por qué se paró, dónde quedamos y —si
    /// aporta— qué SÍ está vivo aquí. Es lo que el modelo necesita para replanificar sin gastar otra
    /// llamada de reconocimiento: el equivalente del screenshot intercalado del computer_batch.
    /// </summary>
    private Resultado Parcial(int hechos, int total, string motivo, bool conVivos)
    {
        string aqui = _donde() ?? "";
        string vivos = "";
        if (conVivos && aqui.Length > 0)
        {
            // PUERTAS, NO ESCOMBROS. En la web lo vivo incluye fragmentos de texto —«,», «[1]»,
            // párrafos— y una lista con eso no sirve para replanificar: el modelo gastaba otra
            // llamada de reconocimiento, que es lo que esta lista existe para evitar (2026-08-25).
            // Primero las que ya tienen arista aprendida: son las que se sabe a dónde llevan.
            var nombres = _grafo.DesdeAqui(aqui)
                .Where(a => a.Vivo && ParecePuerta(a.Que.Etiqueta))
                .OrderByDescending(a => a.Destino.Length > 0)
                .ThenBy(a => a.Que.Etiqueta.Length)
                .Select(a => $"«{a.Que.Etiqueta}»").Distinct().Take(15).ToList();
            if (nombres.Count > 0) vivos = " Vivo aquí: " + string.Join(", ", nombres) + ".";
        }
        return new(hechos, total, aqui, false,
            $"hice {hechos} de {total} y paré en el paso {hechos + 1}: {motivo}"
            + (aqui.Length > 0 ? $" Estás en «{aqui}»." : "") + vivos);
    }
}
