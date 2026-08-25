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
            var (elegido, homonimos, muerto, aqui) = EsperarloVivo(paso.Exit);

            if (aqui.Length == 0)
                return Parcial(i, pasos.Count, "no sé dónde estoy, y sin eso no pulso nada.", conVivos: false);

            // DOS VIVOS CON EL MISMO NOMBRE: no se adivina — la misma regla que abrir (promesa 40).
            // Se dan los selectores para que quien pidió elija con conocimiento.
            if (homonimos.Count > 1)
                return Parcial(i, pasos.Count,
                    $"hay {homonimos.Count} vivos que se llaman «{paso.Exit}»: "
                    + string.Join(", ", homonimos.Select(h => $"«{h}»"))
                    + ". Dime el selector y sigo.", conVivos: false);

            if (elegido == null)
                return Parcial(i, pasos.Count,
                    muerto
                        // La promesa 15 del núcleo, hablando por el batch: «no sé llegar» y «sé pero
                        // no se ve» son respuestas distintas, y al modelo le sirven distinto.
                        ? $"«{paso.Exit}» lo conozco aquí pero AHORA no lo veo."
                        : $"«{paso.Exit}» no lo conozco en «{aqui}».",
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

    /// <summary>
    /// Busca el paso entre lo VIVO de aquí, esperando a que aparezca si hace falta. Devuelve el
    /// elegido (o null), los selectores homónimos si hay empate, si existe uno RECORDADO con ese
    /// nombre, y dónde estábamos al mirar por última vez.
    /// </summary>
    private (Nucleo.Alcanzable? Elegido, IReadOnlyList<string> Homonimos, bool Muerto, string Aqui)
        EsperarloVivo(string exit)
    {
        for (int ido = 0; ; ido += 120)
        {
            string aqui = _donde() ?? "";
            if (aqui.Length > 0)
            {
                var todos = _grafo.DesdeAqui(aqui);

                // El selector exacto manda: quien lo da ya eligió, y con homónimos es lo ÚNICO que
                // distingue. La etiqueta se compara como se habla (tildes aparte, ver Nombres).
                var porSelector = todos.FirstOrDefault(
                    a => a.Que.Selector.Equals(exit, StringComparison.Ordinal));
                if (porSelector != null)
                {
                    if (porSelector.Vivo) return (porSelector, Array.Empty<string>(), false, aqui);
                }
                else
                {
                    var conEseNombre = todos
                        .Where(a => LoNombra(a.Que.Etiqueta, exit)).ToList();

                    // LO EXACTO GANA A LO DIFUSO. Una página web observa FRAGMENTOS de texto como
                    // elementos —«,», «[1]», «El»— y por pura contención la basura «El» se tragaba
                    // el exit «El portal asociado a este artículo» (Wikipedia, 2026-08-24, en la
                    // primera prueba real del batch). Si hay un vivo que se llama EXACTAMENTE así,
                    // ese es; lo difuso queda para cuando no hay exacto — que es el caso legítimo
                    // de «Copilot» → «Copilot anclado» (promesa 43).
                    var vivosTodos = conEseNombre.Where(a => a.Vivo).ToList();
                    var exactos = vivosTodos
                        .Where(a => Nombres.Aplanar(a.Que.Etiqueta) == Nombres.Aplanar(exit)).ToList();
                    var vivos = exactos.Count > 0 ? exactos : vivosTodos;

                    if (vivos.Count == 1) return (vivos[0], Array.Empty<string>(), false, aqui);
                    if (vivos.Count > 1)
                        return (null, vivos.Select(v => v.Que.Selector).ToList(), false, aqui);
                    if (ido >= EsperaMaximaMs)
                        return (null, Array.Empty<string>(), conEseNombre.Count > 0, aqui);
                }

                if (porSelector != null && ido >= EsperaMaximaMs)
                    return (null, Array.Empty<string>(), true, aqui);   // existe, pero no vivo
            }
            else if (ido >= EsperaMaximaMs)
                return (null, Array.Empty<string>(), false, "");

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
            var nombres = _grafo.DesdeAqui(aqui)
                .Where(a => a.Vivo && a.Que.Etiqueta.Length > 0)
                .Select(a => $"«{a.Que.Etiqueta}»").Distinct().Take(15).ToList();
            if (nombres.Count > 0) vivos = " Vivo aquí: " + string.Join(", ", nombres) + ".";
        }
        return new(hechos, total, aqui, false,
            $"hice {hechos} de {total} y paré en el paso {hechos + 1}: {motivo}"
            + (aqui.Length > 0 ? $" Estás en «{aqui}»." : "") + vivos);
    }
}
