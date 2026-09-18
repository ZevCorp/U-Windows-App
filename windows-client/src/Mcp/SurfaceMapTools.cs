using U.Graph;
using U.Graph.Surfaces;
using U.WindowsClient.Diagnostics;
using U.WindowsClient.Navigation;
using U.WindowsClient.Uia;

namespace U.WindowsClient.Mcp;

/// <summary>
/// El mapa del computador, expuesto al cerebro. Cuatro herramientas: tres que MIRAN y una que
/// ACTÚA, y esa separación es el diseño, no una casualidad de implementación.
///
/// Lo que hace útil a este mapa es que se aprendió solo, viendo al usuario trabajar: el asistente
/// puede llegar a sitios que nadie le enseñó como workflow. Lo que lo hace peligroso es lo mismo —
/// nadie revisó esas rutas. De ahí las dos reglas que gobiernan todo lo de abajo:
///
///   · SOLO SE OFRECEN RUTAS COMPLETAS. Una arista sin acción no se puede recorrer, así que no
///     entra en ninguna ruta. Mejor «no sé llegar» que dejar al asistente a mitad de camino en la
///     máquina de alguien.
///   · SE VERIFICA CADA TRAMO. Tras ejecutar la acción se comprueba que la pantalla es la esperada,
///     y si no llegó se PARA y se dice dónde quedó. La medición de este mapa dio ~78% de acciones
///     correctas: sin verificación, una de cada cinco navegaciones acabaría en un sitio distinto
///     del que el modelo cree, actuando sobre lo que no reconoce.
/// </summary>
public sealed class SurfaceMapTools
{
    private readonly Func<SurfaceLocator.SurfaceLocation?> _where;
    // SoloEnFoco: esta capa verifica la ubicación antes de actuar, así que si un selector no está
    // en la ventana de delante es que no está. Sin esto, un nombre inexistente disparaba un barrido
    // por todas las ventanas del escritorio que llegó a tardar 181 s en fallar (2026-08-03).
    private readonly UiaSurface _uia = new() { Log = s => LogBus.Log("mapa-mcp", s), SoloEnFoco = true };
    private readonly UiaReader _lector = new();

    /// <summary>Lo último que se señaló, con su identidad de UIA. Ver <see cref="LoQueSenala"/> y
    /// el rescate al final de <see cref="Take"/>.</summary>
    /// <remarks>
    /// <c>Que</c> es NULLABLE desde el 2026-09-02: lo señalado dentro de SAP no tiene elemento de
    /// UIA —ahí no hay ninguno— y lo único que se usa de esta tupla es el nombre y el selector.
    /// </remarks>
    private (string Nombre, string Selector, System.Windows.Automation.AutomationElement? Que, DateTime Cuando)? _ultimoSenalado;

    /// <summary>
    /// La ruta de la foto del ÚLTIMO RECUERDO creado. Vacío si el último «esto es X» no llegó a
    /// crear uno (nada señalado, o caducado).
    ///
    /// Existe para que quien tiene la voz —<c>ConversacionEnVivo</c>— pueda mandarle esa misma
    /// imagen al modelo justo después de un <c>map_esto_es</c> que tuvo éxito: el usuario acaba de
    /// enseñar algo, y sin la foto el modelo solo tiene el texto, no lo que había alrededor cuando
    /// se dijo.
    /// </summary>
    public string UltimaFotoDeRecuerdo { get; private set; } = "";

    /// <summary>La app con la que se estaba trabajando. Se usa para volver a ella si algo roba el foco.</summary>

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int max);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern void keybd_event(byte key, byte scan, uint flags, IntPtr extra);
    private delegate bool EnumProc(IntPtr h, IntPtr l);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumProc callback, IntPtr extra);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr h);

    /// <summary>Superficies del propio Windows que se ponen delante solas y tapan la app.</summary>
    /// <summary>«uia://explorer.exe/loquesea» → «explorer».</summary>
    /// <summary>
    /// Devuelve el foco a la app con la que se está trabajando si algo se lo ha llevado.
    ///
    /// El centro de notificaciones, el buscador o cualquier aviso de Windows se ponen delante solos
    /// y la secuencia se rompe: el asistente pedía «Nuevo» y se le contestaba con las opciones del
    /// centro de notificaciones (2026-08-02). El recorrido automático ya se recolocaba; esta capa
    /// no, y es la que usa el asistente para hacer tareas de verdad. No se lanza nada: si la app no
    /// está viva, se dice y punto — abrir aplicaciones por iniciativa propia no es recuperarse.
    /// </summary>
    /// <summary>
    /// Qué hay seleccionado ahora mismo en la pantalla.
    ///
    /// Existe porque una acción como «Cortar» opera sobre LO SELECCIONADO, y el asistente no tenía
    /// forma de saber qué era. En una tarea de organizar archivos, un paso previo falló, la
    /// selección se quedó en una carpeta recién creada, y el siguiente «Cortar» la cortó a ella:
    /// se intentó pegar la carpeta dentro de sí misma (2026-08-02). Nadie mintió —cada paso
    /// reportó su fallo— pero el que actuaba no sabía sobre qué actuaba. Decirlo convierte un
    /// encadenamiento a ciegas en algo comprobable antes de tocar nada.
    /// </summary>
    /// <summary>
    /// Al bajar a una carpeta, aprende también la SUBIDA a su padre.
    ///
    /// «Subir» (aid=upButton) es estructural: desde una carpeta siempre lleva a la que la contiene,
    /// se haya llegado como se haya llegado. Eso lo hace una arista legítima, al contrario que
    /// «Atrás», que depende del historial y por eso NO se aprende. Sin esto el grafo bajaba y no
    /// subía: al pedir volver de «facturas» a su padre la respuesta era «no conozco una ruta
    /// COMPLETA», y una tarea que creaba carpetas hermanas acababa creándolas anidadas — Windows
    /// mismo lo paró con «la carpeta de destino es una subcarpeta de la de origen» (2026-08-02).
    ///
    /// Solo cuando la bajada fue por un elemento de LISTA —una carpeta de contenido—: pulsar algo
    /// del panel lateral no es descender, y su padre no es de donde veníamos.
    /// </summary>
    /// <summary>
    /// Aprende una app ENTERA: la trae al frente ella sola (o la abre) y la recorre.
    ///
    /// Antes, mapear dependía de quién tuviera el foco al pulsar el botón, y eso es frágil hasta el
    /// absurdo: cualquier ventana que se pusiera delante en ese instante —incluida la de quien
    /// lanzaba la prueba— hacía que se mapeara la app equivocada (2026-08-03). Decir QUÉ app se
    /// quiere aprender y que el sistema se encargue del resto es la forma correcta: la intención
    /// la pone quien pide, no el azar del escritorio.
    /// </summary>
    /// <summary>
    /// Abre una aplicación (o la trae al frente si ya estaba) y dice dónde quedamos.
    ///
    /// Faltaba, y se notó en cuanto la voz tuvo manos: al pedirle «abre el explorador de archivos»,
    /// el modelo no tenía con qué, así que buscó en el mapa algo que sonara a explorador y acabó
    /// pulsando un icono de la barra de tareas aprendido desde OTRA app — que ni existía allí
    /// (2026-08-04). Aprender una app entera con map_learn_app tampoco servía: eso mapea, tarda, y
    /// no es lo que se pidió. Abrir es un gesto propio y merecía su primitiva.
    /// </summary>
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out System.Drawing.Point p);

    /// <summary>
    /// LO QUE HAY EN PANTALLA AHORA, leído en vivo, y qué sabe el mapa de cada cosa.
    ///
    /// Hasta ahora el asistente solo podía consultar su MEMORIA: todas las herramientas respondían
    /// desde el grafo guardado. Eso deja un hueco que ya nos mordió —una carpeta recién creada es
    /// invisible para quien solo recuerda, y el modelo pedía entrar en algo que «no existe» con la
    /// carpeta delante (2026-08-03)— y además impide lo que el usuario quiere hacer ahora: señalar
    /// cosas de la interfaz viva para colocarlas en un nivel.
    ///
    /// Se marca CADA elemento con lo que el mapa sabe de él, porque mezclar «lo que veo» con «lo que
    /// recuerdo» sin distinguirlos sería peor que no tener esto: el modelo no podría saber si algo
    /// es terreno conocido o una novedad.
    /// </summary>
    /// <summary>
    /// LAS PUERTAS DEL TERRENO QUE UIA NO NOMBRÓ. Promesa 183. Pura: dos listas, una respuesta.
    /// </summary>
    /// <remarks>
    /// UIA ve el Pane opaco de SAP —botones y campos— pero NUNCA las filas de una rejilla ni las de
    /// un árbol. El terreno sí las conoce (las lee por Scripting). Se añaden las que UIA no nombró
    /// ya, por etiqueta, sin duplicar: el piloto tiene que ver «GIRALDO» para poder pedirlo por su
    /// nombre, y map_take ya sabe seleccionarlo (2026-09-07, era la puerta invisible).
    /// </remarks>
    public static IReadOnlyList<(string Selector, string Etiqueta, string Tipo)> FundirPuertas(
        IEnumerable<string> loQueYaNombraUia,
        IReadOnlyList<(string Selector, string Etiqueta, string Tipo)> delTerreno)
    {
        var yaEstan = new HashSet<string>(loQueYaNombraUia ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var salida = new List<(string, string, string)>();
        foreach (var p in delTerreno ?? Array.Empty<(string, string, string)>())
            if (!string.IsNullOrWhiteSpace(p.Etiqueta) && yaEstan.Add(p.Etiqueta))
                salida.Add(p);
        return salida;
    }

    /// <summary>
    /// LAS PUERTAS DE AHORA, en orden de lectura: lo que ve UIA, las puertas vivas del terreno y los
    /// campos del dynpro, fundidos sin duplicar. UNA SOLA LISTA para <c>map_what_i_see</c> y para
    /// <c>map_decidir</c> (promesa 285): dos caminos para enumerar el mismo terreno se desincronizan
    /// en silencio, y el decisor acabaría eligiendo entre puertas que la voz no lista, o al revés.
    /// </summary>
    /// <returns>Dónde, las puertas que se cuentan (con su tope) y cuántas hay en total.</returns>
    private (string Aqui, IReadOnlyList<(string Selector, string Etiqueta, string Tipo)> Puertas, int Total) PuertasDeAhora()
    {
        var loc = _where();
        string aqui = loc?.Id ?? "";
        if (aqui.Length == 0) return ("", Array.Empty<(string, string, string)>(), 0);
        if (Puertas != null) { var inyectadas = Puertas(aqui); return (aqui, inyectadas, inyectadas.Count); }

        _lector.Read();
        var vivos = _lector.Elements
            .Where(e => e.Label.Length > 0
                     && !e.ControlType.Equals("text", StringComparison.OrdinalIgnoreCase)
                     && !e.ControlType.Equals("image", StringComparison.OrdinalIgnoreCase))
            .ToList();
        // LAS PUERTAS DEL TERRENO, no solo lo que ve UIA (promesa 183). UIA ve el Pane opaco de SAP:
        // botones y campos, pero NUNCA las filas de una rejilla ni las de un árbol. El piloto planeaba
        // a ciegas sobre la lista de pacientes porque «GIRALDO» no salía aquí, aunque map_take SÍ sabía
        // seleccionarlo. Se añaden las puertas vivas que el terreno conoce en esta pantalla, sin
        // duplicar las que UIA ya nombró.
        // Y LOS CAMPOS DEL DYNPRO por su etiqueta (promesa 188): UIA los lista por su nombre técnico
        // («Y0000000-ZTXTTASIS»); la persona y el piloto los llaman «Presión Arterial».
        var terreno = PuertasVivas?.Invoke(aqui) ?? Array.Empty<(string, string, string)>();
        var candidatos = ConLaEtiquetaQueSeLee(terreno, CamposDeSapComoPuertas());
        var delTerreno = FundirPuertas(vivos.Select(v => v.Label), candidatos);
        // EL TOPE ERA 40 PUERTAS DE SAP y el triage tiene 39 campos más 21 botones (2026-09-08): los
        // signos vitales quedaban fuera de la lista y el piloto no podía nombrarlos. Un formulario
        // entero cabe en 160; lo que pase de ahí se dice.
        // CON SU SELECTOR (promesa 287): es lo que la mano resuelve antes que el nombre, y lo que hace que
        // dos «Detalles» no choquen. El selector no viaja a Jev: Jev decide por lo que una persona lee.
        var lista = vivos.Take(60).Select(v => (Selector: Uia.Reconocedor.SelectorDe(v), Etiqueta: v.Label, Tipo: v.ControlType))
            .Concat(delTerreno.Take(160).Select(p => (Selector: p.Selector, Etiqueta: p.Etiqueta, Tipo: p.Tipo)))
            .ToList();
        return (aqui, lista, vivos.Count + delTerreno.Count);
    }

    private string LoQueVeo()
    {
        var (aqui, puertas, total) = PuertasDeAhora();
        if (aqui.Length == 0) return "no sé en qué pantalla estoy";
        // EL «NO VEO NADA» VA DESPUÉS DE MIRAR EN LOS TRES SITIOS (2026-09-08): con UIA en blanco
        // —SAP recién delante, el lector aún sin leer— se contestaba «no veo ningún elemento» sin
        // consultar el terreno ni el dynpro, que sí tenían 39 campos que contar.
        if (total == 0) return $"en «{aqui}» no veo ningún elemento accionable ahora mismo";

        var sb = new System.Text.StringBuilder(
            $"EN PANTALLA AHORA, en «{aqui}» ({total} elemento(s)):" + "\n");
        foreach (var (_, etiqueta, tipo) in puertas)
            sb.AppendLine($"  «{etiqueta}» ({tipo})");
        if (total > 220) sb.AppendLine($"  …y {total - 220} más");
        return sb.ToString();
    }

    /// <summary>
    /// EL DECISOR ELIGE LA PUERTA Y LA PULSA POR EL MISMO CAMINO QUE map_take. Promesas 284-286 (spec 035).
    /// </summary>
    /// <remarks>
    /// ES UNA HERRAMIENTA APARTE Y NO UN CAMBIO A map_take, a propósito: con el decisor apagado
    /// —<see cref="Decisor"/> nulo— esto contesta «todavía no sé decidir» sin leer la pantalla, y el
    /// catálogo de la voz ni la ofrece. Así el camino que usa el hospital hoy queda byte a byte igual.
    ///
    /// LAS PUERTAS SALEN DE <see cref="PuertasDeAhora"/>, la misma función que map_what_i_see, y la
    /// elegida entra por <see cref="Take"/>: misma coreografía, mismos vetos, mismo juez de llegada,
    /// misma <see cref="Mano"/> para el tope de intentos. Esta pieza no pulsa nada por su cuenta.
    ///
    /// CUANDO EL DECISOR NO ACTÚA, EL CONTROL VUELVE CON EL INVENTARIO. La respuesta empieza por
    /// «no se acciona» y el despacho le pega lo que hay delante (263), para que Luna elija ella como
    /// hasta hoy. Y la mano NO cuenta un intento: no se pulsó nada, y contarlo frenaría el «pruebo
    /// otro» del tope de la 204 —el mismo argumento que la lista de homónimos (207).
    /// </remarks>
    private string Decidir(string objetivo, string decir, string recuerdo)
    {
        if (objetivo.Length == 0) return "falta `objetivo`: qué se quiere conseguir en esta pantalla, para que el decisor elija la puerta";
        if (Decisor == null)
            return "todavía no sé decidir: el decisor está apagado (U_DECISOR ausente o en «luna»), así que decide Luna. "
                 + "Elige tú la puerta con map_take.";
        return UnPasoDecidido(objetivo, decir, recuerdo).Cuenta;
    }

    /// <summary>
    /// UN PASO DECIDIDO: leer las puertas, que el decisor elija, y pulsar por selector —con la segunda mejor si la
    /// primera no está—. Es el cuerpo de map_decidir, y el paso que repite el tramo (spec 037). Devuelve qué pasó
    /// como datos, y la cuenta con las mismas palabras de siempre.
    /// </summary>
    private Navigation.ElTramo.Paso UnPasoDecidido(string objetivo, string decir, string recuerdo)
    {
        Navigation.ElTramo.Paso Sin(string cuenta, string porque, double conf = 0, bool cumplido = false)
        {
            _ultimaMano = new Mano(false, false, Intento: false);
            return new Navigation.ElTramo.Paso(false, false, false, "", "", "", conf, cuenta, porque, cumplido);
        }
        if (Decisor == null) return Sin("todavía no sé decidir: el decisor está apagado.", "el decisor está apagado");

        var (aqui, puertas, total) = PuertasDeAhora();
        if (aqui.Length == 0) return Sin("no sé en qué pantalla estoy, así que no hay nada entre lo que decidir.", "no sé en qué pantalla estoy");
        if (total == 0) return Sin($"en «{aqui}» no veo ningún elemento accionable ahora mismo: nada entre lo que decidir.", "no veo ningún elemento accionable");
        // PUERTAS ÚNICAS Y NUMERADAS (promesa 287): «2) Detalles (RadioButton)». Con etiquetas a secas, en
        // openai.com Jev eligió bien tres veces y las tres se perdieron en «hay 2 puertas vivas para…»
        // (2026-09-18, 03:33-03:34): la etiqueta no es única; el id sí, y detrás lleva su selector.
        var ids = new List<string>(puertas.Count);
        var selectorDe = new Dictionary<string, (string Selector, string Etiqueta)>(StringComparer.Ordinal);
        for (int i = 0; i < puertas.Count; i++)
        {
            string id = $"{i + 1}) {puertas[i].Etiqueta} ({puertas[i].Tipo})";
            ids.Add(id);
            selectorDe[id] = (puertas[i].Selector, puertas[i].Etiqueta);
        }
        var etiquetas = ids;

        var reloj = System.Diagnostics.Stopwatch.StartNew();
        Decision.DecisionDeUnPaso d;
        try { d = Decisor(aqui, objetivo, etiquetas); }
        catch (Exception e)
        {
            // LA CADENA ENTERA (patrón nº3). ElDecisor promete no lanzar (280); esto es la costura, y lo
            // que promete otro se comprueba: un decisor inyectado distinto sí podría.
            string causa = "";
            for (var x = e; x != null; x = x.InnerException)
                causa += $"{x.GetType().Name}: {x.Message}" + (x.InnerException != null ? " ← " : "");
            LogBus.Log("decisor", $"✘ en «{aqui}» el decisor lanzó: {causa}");
            return Sin($"no se acciona: el decisor falló ({causa}). Decide Luna.", $"el decisor falló ({causa})");
        }
        reloj.Stop();

        // SE REGISTRA CADA DECISIÓN CON SU CONFIANZA, también las descartadas: el umbral se ajusta con
        // datos del terreno, y los datos son estas líneas.
        LogBus.Log("decisor", $"«{aqui}» · {etiquetas.Count} puerta(s) · {reloj.ElapsedMilliseconds} ms → "
            + (d.Actuar ? $"ACCIONA «{d.Puerta}»" : "no acciona")
            + $" conf={d.Confianza.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)} · {d.Porque}");

        if (!d.Actuar)
            return Sin($"no se acciona: {d.Porque}", d.Porque, d.Confianza,
                cumplido: d.Cumplido >= Decision.ElDecisor.CumplidoMinimo || d.Porque.Contains("cumplido", StringComparison.OrdinalIgnoreCase));

        // LA ELEGIDA, Y COMO MUCHO LA SEGUNDA MEJOR (promesa 288): si la primera no está viva al ir a pulsarla,
        // se prueba la siguiente por probabilidad si llega al mínimo. Sin otra llamada a Jev: las
        // probabilidades ya vinieron. La tercera no se prueba: sería adivinar.
        var candidatos = new List<(string Id, double Prob)> { (d.Puerta, d.Confianza) };
        var segunda = d.Alternativas
            .Where(a => a.Puerta != d.Puerta && a.Probabilidad >= Decision.ElDecisor.SegundaMejorMinima && selectorDe.ContainsKey(a.Puerta))
            .OrderByDescending(a => a.Probabilidad)
            .FirstOrDefault();
        if (segunda.Puerta != null) candidatos.Add((segunda.Puerta, segunda.Probabilidad));

        var relato = new System.Text.StringBuilder();
        for (int k = 0; k < candidatos.Count; k++)
        {
            var (id, prob) = candidatos[k];
            if (!selectorDe.TryGetValue(id, out var puerta))
                return Sin($"no se acciona: el decisor contestó «{id}», que no es ninguna de las {ids.Count} puertas ofrecidas. Decide Luna.",
                    $"contestó «{id}», que no se ofreció", d.Confianza);
            string numero = id.Substring(0, id.IndexOf(')'));
            string cuenta = Take(puerta.Selector, "", decir, recuerdo);
            var mano = _ultimaMano;
            string medida = k == 0
                ? $"con confianza {prob.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}"
                : $"con probabilidad {prob.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}";

            // «NO ESTÁ» ES LO ÚNICO QUE DISPARA LA SEGUNDA: la mano no terminó, lo intentó, y no fue una lista de
            // homónimos (eso es otra clase de respuesta: falta elegir cuál de las iguales, no otra puerta).
            bool noEstaba = _ultimaMano is { Termino: false, Intento: true } m && (m.Candidatos == null || m.Candidatos.Count == 0)
                         && !cuenta.Contains("puertas vivas para", StringComparison.Ordinal);
            if (noEstaba && k + 1 < candidatos.Count)
            {
                relato.Append($"«{puerta.Etiqueta}» ({numero}) no estaba: {cuenta}; probé la segunda: ");
                continue;
            }
            if (noEstaba)
                relato.Append($"«{puerta.Etiqueta}» ({numero}) no estaba: {cuenta}");
            else
                relato.Append($"elegida «{puerta.Etiqueta}» ({numero}) {medida}: {cuenta}");
            bool termino = mano?.Termino == true;
            bool cambio = mano?.Logro == true;
            return new Navigation.ElTramo.Paso(true, termino, cambio, puerta.Selector, puerta.Etiqueta, numero, prob, relato.ToString(), d.Porque, false);
        }
        return Sin(relato.ToString(), "no quedó ninguna candidata", d.Confianza);
    }

    /// <summary>
    /// ¿Veo esto? Si sí, lo SEÑALA: enciende el recuadro y lleva la carita a su lado.
    ///
    /// «¿Ves el botón Nuevo?» tenía una respuesta insuficiente: decir que sí. Quien pregunta no está
    /// pidiendo un sí — está pidiendo comprobar que los dos miran lo mismo, y para eso hay que
    /// apuntar (2026-08-05, pedido por el usuario). Señalar convierte una afirmación en algo
    /// verificable de un vistazo: si el recuadro cae sobre otra cosa, se ve al instante.
    ///
    /// Se busca en lo que hay AHORA en pantalla, no en el mapa: la pregunta es «¿lo ves?», no
    /// «¿te acuerdas de él?».
    /// </summary>
    /// <summary>
    /// Todo lo que se llama así, SIN agrupar y en el orden en que se lee la pantalla: de arriba
    /// abajo y de izquierda a derecha. Ese orden es el que hace que «el primero» y «el segundo»
    /// signifiquen lo mismo para quien mira y para quien señala.
    /// </summary>
    private static List<UiaReader.UiElement> Homonimos(
        IReadOnlyList<UiaReader.UiElement> aLaVista, string que)
    {
        string q = que.Trim();
        return aLaVista
            .Where(e => e.Label.Trim().Equals(q, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.Bounds.Top).ThenBy(e => e.Bounds.Left)
            .Take(12)
            .ToList();
    }

    private string Mostrar(string que, int cual = 0)
    {
        if (que.Length == 0) return "falta `exit`: qué elemento hay que señalar";

        // MIRAR NO MUEVE NADA. Aquí había un AsegurarFoco para que una ventana intrusa no falseara
        // la respuesta, y fue un remedio peor que la enfermedad: `_ultimaApp` guarda la última app
        // sobre la que se ACTUÓ, así que preguntar «¿ves esto?» estando en Chrome arrancaba el foco
        // al explorador —el usuario lo vio pasar cada vez (2026-08-05)—. Y el problema que
        // pretendía resolver era de mi banco de pruebas, no de nadie usando esto.
        //
        // Ver es pasivo por definición: se lee lo que hay delante, y si no se puede leer se dice.
        // Un observador que reordena la pantalla para verla mejor ha dejado de observar.
        // NO NOS MIRAMOS A NOSOTROS. Si lo que hay delante es una ventana de Ü —la barra, la capa
        // del grafo— leerla y contestar «veo ▾ y 🤖» es describirse a sí mismo creyendo que describe
        // la pantalla del usuario (2026-08-05, salió en la primera prueba de las zonas). Es la misma
        // regla que ya rige en el mapa y en el detector de diálogos, que aquí faltaba.
        // Ya NO se rechaza por tener nuestra propia ventana delante: pulsar la carita para hablar
        // nos pone delante, así que rechazarlo era negarse justo cuando se pregunta. El lector mira
        // la ventana del usuario —la de debajo de la nuestra—, que es a la que se refiere quien
        // pregunta. Además el asistente contaba aquel «mi propia interfaz» como «Claude se puso por
        // medio», culpando a una app que ni siquiera estaba (2026-08-05).
        _lector.Read();
        var candidatos = _lector.Elements.Where(e => e.Label.Length > 0).ToList();
        string donde = _where()?.Id ?? "(pantalla desconocida)";

        // NO HABER MIRADO NO ES NO HABERLO VISTO. Si la lectura vino vacía, decir «no lo veo» sería
        // afirmar algo que no se ha comprobado — el mismo error que perseguimos en las acciones,
        // trasladado a la vista (2026-08-05).
        if (candidatos.Count == 0)
        {
            Ui.Senalador.Soltar();
            return $"no he podido leer la pantalla ({donde}): no me devuelve ningún elemento. "
                 + "No es que «{que}» no esté — es que no llegué a mirar.";
        }

        // ¿Se pregunta por UNA cosa, por VARIAS, o por una ZONA? «¿Ves los elementos de la columna
        // derecha?» no se responde buscando un nombre: se responde mirando dónde está cada cosa. Y
        // es la forma natural de preguntar cuando se señala en voz alta, porque quien mira una
        // pantalla piensa en zonas antes que en nombres (2026-08-05, pedido por el usuario).
        var zona = ZonaPedida(que);
        List<UiaReader.UiElement> elegidos;
        string comoSeLlama;

        if (zona != null)
        {
            // Solo lo PULSABLE, y sin repetir nombre. Sin filtrar salían 256 «elementos» de una
            // columna que tiene doce: contenedores anidados, textos de estado, cada fila contada
            // varias veces. Señalar 256 cosas no es señalar (2026-08-05). Lo que el usuario llama
            // «los elementos de esa columna» son sus puertas, no cada nodo del árbol UIA.
            elegidos = candidatos
                .Where(e => zona(e.Bounds) && EsPuertaVisible(e))
                .GroupBy(e => e.Label, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(e => e.Bounds.Top).ThenBy(e => e.Bounds.Left)
                .Take(30)
                .ToList();
            comoSeLlama = que;
        }
        else if (que.Contains(',') || que.Contains(';'))
        {
            var nombres = que.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                             .Select(n => n.Trim()).Where(n => n.Length > 0).ToList();
            elegidos = nombres
                .Select(n => Uia.Reconocedor.Buscar(candidatos, n).FirstOrDefault())
                .Where(e => e != null).Select(e => e!).ToList();
            comoSeLlama = string.Join(", ", nombres);
        }
        else
        {
            // El MISMO reconocedor que usa map_take para pulsar. Cuando señalar y pulsar buscaban
            // cada uno a su manera, pasaba lo del 2026-08-05: veía la barra de búsqueda y decía que
            // no podía pulsarla. Si lo señalo, lo puedo pulsar — y al revés.
            // SI EL NOMBRE COINCIDE CON VARIOS, NO SE ELIGE UNO A DEDO. Aquí había un `.Take(1)`:
            // señalaba el primero del árbol y se quedaba tan ancho, así que cuando Ü preguntaba
            // «¿cuál de los dos «Code»?» no tenía forma de enseñar CUÁL era cada uno — que es
            // justo el momento en el que señalar sirve para algo (2026-08-16, pedido por el usuario:
            // «que sea como este, y va y lo señala, o este, y va y señala el otro»).
            //
            // Con `cual` se señala uno concreto —el 1.º, el 2.º…— y sin él se señalan todos, que es
            // la respuesta honesta a un nombre que nombra a varios.
            // SEÑALAR NECESITA VER LOS HOMÓNIMOS; PULSAR NO. `Reconocedor.Buscar` agrupa por nombre
            // y devuelve uno —«el mismo nombre, una vez»—, que es lo correcto para pulsar: no vas a
            // pulsar dos cosas. Pero para PREGUNTAR «¿cuál de los dos?» hay que poder enseñar los
            // dos, y ahí ese acierto se convierte en el impedimento. Se buscan aquí, sin agrupar, y
            // no se toca el reconocedor: lo usa `map_take`, y cambiarlo movería el suelo de la
            // navegación entera para arreglar una forma de señalar (2026-08-16).
            var homonimos = Homonimos(candidatos, que);
            elegidos = cual >= 1 && cual <= homonimos.Count
                ? new List<UiaReader.UiElement> { homonimos[cual - 1] }
                : homonimos.Count > 0 ? homonimos : Uia.Reconocedor.Buscar(candidatos, que).ToList();
            comoSeLlama = cual >= 1 && cual <= homonimos.Count
                ? $"{que} ({cual} de {homonimos.Count})" : que;
        }

        if (elegidos.Count == 0)
        {
            Ui.Senalador.Soltar();
            var parecidos = candidatos.Take(12).Select(c => $"«{c.Label}»");
            return $"NO veo «{que}» en «{donde}» ({candidatos.Count} elementos a la vista). "
                 + $"Lo que sí veo: {string.Join(", ", parecidos)}…";
        }

        if (elegidos.Count > 1)
        {
            Ui.Senalador.SenalarVarias(elegidos.Select(e => (e.Bounds, e.Label)).ToList());
            var nombres = elegidos.Take(20).Select(e => $"«{e.Label}»");
            return $"SÍ, veo {elegidos.Count} y los estoy señalando todos en «{donde}»: "
                 + string.Join(", ", nombres) + (elegidos.Count > 20 ? "…" : "")
                 + ".";
        }

        var el = elegidos[0];
        Ui.Senalador.Senalar(el.Bounds, el.Label);

        // SE DICE CUÁL DE ELLOS. Con tres «Descargas» en pantalla, «lo estoy señalando» no distingue
        // nada: ni quien mira el panel ni el propio modelo sabrían a cuál se refiere en el turno
        // siguiente. Al preguntar «¿este o este?» hay que poder nombrar el que se está enseñando.
        string posicion = comoSeLlama.Contains(" (") && comoSeLlama.EndsWith(")")
            ? " " + comoSeLlama[comoSeLlama.IndexOf(" (", StringComparison.Ordinal)..].Trim()
            : "";
        // CON HOMÓNIMOS SE DA SU SELECTOR, NO SU ETIQUETA (promesa 203, el segundo sitio de la clase).
        // Hasta el 2026-09-10 se sugería «map_take exit=«Etiqueta»», que vuelve a crear la ambigüedad que
        // se acababa de resolver señalando: con tres «Descargas» delante, la etiqueta nombra a las tres.
        // Solo si ese selector es ÚNICO: dos homónimos del mismo tipo comparten «uia:name=X;ct=Y», y
        // sugerir ese selector diciendo «(2 de 3)» sería prometer uno concreto y pulsar cualquiera.
        string selectorDe = Uia.Reconocedor.SelectorDe(el);
        bool selectorUnico = candidatos.Count(c => Uia.Reconocedor.SelectorDe(c) == selectorDe) == 1;
        string paraPulsar = posicion.Length > 0 && selectorUnico ? selectorDe : el.Label;
        return $"SÍ veo «{el.Label}»{posicion} ({el.ControlType}) y lo estoy señalando: recuadro encendido "
             + $"y la carita puesta a su lado. Lo puedo pulsar ahora mismo con "
             + $"map_take exit=«{paraPulsar}» — que lo vea basta.";
    }


    /// <summary>Lo que una persona llamaría «un elemento» de la pantalla: algo que se puede pulsar
    /// y que ocupa un sitio razonable. No un contenedor ni una etiqueta suelta.</summary>
    private static bool EsPuertaVisible(UiaReader.UiElement e) =>
        e.Bounds.Width >= 12 && e.Bounds.Height >= 12
        && e.Bounds.Width < 900                       // un contenedor ancho no es un elemento
        && e.ControlType.ToLowerInvariant() is "button" or "listitem" or "treeitem" or "tabitem"
            or "menuitem" or "hyperlink" or "checkbox" or "radiobutton" or "splitbutton" or "combobox";

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    /// <summary>
    /// Traduce «la columna derecha», «el panel de la izquierda», «la barra de arriba» a una región
    /// de la ventana. Devuelve null si lo pedido no suena a zona.
    ///
    /// Los tercios y quintos no son arbitrarios: un panel lateral ocupa alrededor de un tercio y una
    /// barra de herramientas bastante menos de un quinto del alto. No hace falta afinar más, porque
    /// esto sirve para SEÑALAR —el usuario ve al instante si se pasó o se quedó corto— y no para
    /// decidir nada por su cuenta.
    /// </summary>
    private Func<System.Windows.Rect, bool>? ZonaPedida(string texto)
    {
        string t = texto.ToLowerInvariant();
        bool zonaSuena = t.Contains("columna") || t.Contains("panel") || t.Contains("barra")
                      || t.Contains("lateral") || t.Contains("todos") || t.Contains("elementos de");
        if (!zonaSuena) return null;

        IntPtr h = GetForegroundWindow();
        if (h == IntPtr.Zero || !GetWindowRect(h, out RECT w)) return null;
        double x0 = w.Left, y0 = w.Top, ancho = w.Right - w.Left, alto = w.Bottom - w.Top;
        if (ancho <= 0 || alto <= 0) return null;

        if (t.Contains("derech")) return r => r.X >= x0 + ancho * 2 / 3;
        if (t.Contains("izquierd")) return r => r.X <= x0 + ancho / 3;
        if (t.Contains("arriba") || t.Contains("superior") || t.Contains("herramientas"))
            return r => r.Y <= y0 + alto / 5;
        if (t.Contains("abajo") || t.Contains("inferior")) return r => r.Y >= y0 + alto * 4 / 5;
        if (t.Contains("centro") || t.Contains("contenido"))
            return r => r.X > x0 + ancho / 3 && r.X < x0 + ancho * 2 / 3;
        return null;
    }

    /// <summary>
    /// El elemento que hay BAJO EL CURSOR, ahora mismo.
    ///
    /// Es la forma barata y exacta de resolver «esto que estoy señalando»: la alternativa era
    /// mandarle vídeo de la pantalla al modelo y confiar en que acertara mirando píxeles, cuando el
    /// sistema ya puede preguntarle a Windows qué hay en ese punto y obtener el nombre exacto
    /// (2026-08-04, a propuesta del usuario de señalar con el ratón).
    /// </summary>
    /// <summary>
    /// Si lo que hay bajo el cursor es una ventana NUESTRA, devuelve lo que hay debajo de ella.
    /// Null si debajo tampoco hay nada que no seamos nosotros.
    /// </summary>
    /// <remarks>
    /// Se recorren las ventanas de arriba abajo en el orden en que las pinta Windows y se coge la
    /// primera visible que contenga el punto y NO sea de este proceso. Es lo mismo que hace una
    /// persona al apartar un papel para leer el de abajo.
    ///
    /// Se decide por PROCESO y no por título: llamarse «Ü» es una casualidad que cualquier app puede
    /// repetir —y entonces la estaríamos saltando sin motivo—; ser nuestro proceso no lo es.
    /// </remarks>
    private static System.Windows.Automation.AutomationElement? SaltarNuestrasVentanas(
        System.Windows.Automation.AutomationElement el, System.Drawing.Point p)
    {
        int mio = Environment.ProcessId;
        try { if (el.Current.ProcessId != mio) return el; } catch { return el; }

        var punto = new System.Windows.Point(p.X, p.Y);
        System.Windows.Automation.AutomationElement? debajo = null;

        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h)) return true;
            GetWindowThreadProcessId(h, out uint pid);
            if ((int)pid == mio) return true;                     // seguimos siendo nosotros
            if (!GetWindowRect(h, out var r)) return true;
            if (p.X < r.Left || p.X > r.Right || p.Y < r.Top || p.Y > r.Bottom) return true;

            try
            {
                var v = System.Windows.Automation.AutomationElement.FromHandle(h);
                if (v == null) return true;
                // Dentro de esa ventana, lo más pequeño con nombre que contenga el punto: la misma
                // regla que se usa para todo lo demás (ver Navigation.LoQueSenalas.Elegir).
                var candidatos = new List<Navigation.LoQueSenalas.Candidato>();
                foreach (System.Windows.Automation.AutomationElement d in v.FindAll(
                    System.Windows.Automation.TreeScope.Descendants,
                    System.Windows.Automation.Condition.TrueCondition))
                {
                    try
                    {
                        candidatos.Add(new Navigation.LoQueSenalas.Candidato(
                            (d.Current.Name ?? "").Trim(),
                            d.Current.ControlType.ProgrammaticName.Replace("ControlType.", ""),
                            d.Current.BoundingRectangle));
                    }
                    catch { }
                }
                if (Navigation.LoQueSenalas.Elegir(candidatos, punto) is { } c)
                {
                    debajo = v.FindAll(System.Windows.Automation.TreeScope.Descendants,
                        new System.Windows.Automation.PropertyCondition(
                            System.Windows.Automation.AutomationElement.NameProperty, c.Nombre))
                        .Cast<System.Windows.Automation.AutomationElement>().FirstOrDefault() ?? v;
                    return false;   // encontrado: se deja de buscar
                }
            }
            catch { }
            return true;
        }, IntPtr.Zero);

        if (debajo != null) LogBus.Log("mapa-mcp", "señalar: la carita estaba encima; miro lo que hay debajo");
        return debajo;
    }

    /// <summary>
    /// Ilumina en pantalla las candidatas de un nombre ambiguo, para que se pueda elegir mirando.
    /// </summary>
    /// <remarks>
    /// Se buscan por SELECTOR y no por etiqueta: si hay dos que se llaman igual —que es justo el
    /// caso— buscar por nombre volvería a devolver las dos y no se sabría cuál es cuál.
    ///
    /// Si alguna no se puede localizar en pantalla no se dice nada y se enseñan las demás: el aviso
    /// útil es la respuesta que ya se está devolviendo, y un «no pude iluminar la segunda» sobra
    /// cuando la persona tiene la primera delante.
    /// </remarks>
    /// <summary>
    /// Guarda la ventana que se está mirando, junto al recuerdo que se acaba de crear. Devuelve la
    /// ruta, o vacío.
    /// </summary>
    /// <remarks>
    /// Se guarda la VENTANA y no solo el recuadro del elemento: «aquí va el número de factura» se
    /// entiende viendo el formulario entero, no un botón recortado. El contexto es la mitad del
    /// recuerdo.
    ///
    /// Y se guarda con la fecha en el nombre para poder ver DESPUÉS si lo recordado sigue teniendo
    /// sentido cuando la pantalla cambie — que es la única forma de saber si un recuerdo envejeció
    /// mal en vez de enterarse el día que falla.
    /// </remarks>
    private static string GuardarFotoDeLoSenalado(string nombre)
    {
        try
        {
            return Navigation.FotosDeLosRecuerdos.Guardar(nombre,
                Capture.Screenshotter.CaptureVentanaJpeg(AppAligner.VentanaDelUsuario()));
        }
        catch (Exception e) { LogBus.Log("recuerdo", $"no pude guardar la foto: {e.Message}"); return ""; }
    }
    /// <summary>
    /// «ESTO ES X» / «recuerda que aquí va el número de factura»: crea un RECUERDO, sobre lo último
    /// señalado o sobre un elemento nombrado (<paramref name="sobre"/>).
    /// </summary>
    /// <remarks>
    /// DOS FORMAS DE DECIR SOBRE QUÉ, porque enseñar ocurre de dos maneras y solo se cubría una:
    ///
    ///   · SEÑALANDO, que es el gesto natural —se apunta y se explica— y no obliga a decir en voz
    ///     alta «Número de factura de la cabecera», que es justo lo que se evita apuntando.
    ///   · NOMBRÁNDOLO (`sobre`), para lo que se enseña SIN la mano encima: «recuerda que para
    ///     iniciar sesión se hace clic en Acceder al sistema» se dice mirando la pantalla, no
    ///     necesariamente con el cursor sobre el botón. Exigir el gesto dejaba esa frase sin
    ///     ningún sitio donde caer, y el modelo contestaba «lo tengo en mente» sin guardar nada
    ///     (2026-08-24, visto en el log: dos lecciones perdidas en un minuto).
    ///
    /// El cursor MANDA cuando hay gesto reciente: si se acaba de señalar algo, eso gana sobre el
    /// nombre, porque apuntar es más exacto que describir y con dos elementos homónimos el nombre
    /// no distingue.
    ///
    /// Se exige haber señalado hace poco (LoQueSenalas.LoSenaladoCaduca): un significado dicho diez
    /// minutos después de apuntar se colgaría de lo que fuera que se mirara entonces.
    ///
    /// LA FOTO SE TOMA SOLO CUANDO EL RECUERDO YA EXISTE (2026-08-24, pedido por el usuario). Antes
    /// se tomaba una en CADA señalado, se convirtiera o no en un recuerdo — la mayoría de los
    /// señalados son solo mirar, y cada uno dejaba un PNG que nadie iba a volver a ver. Y la primera
    /// versión de este arreglo todavía fallaba: tomaba la foto ANTES de saber si `Ensenar` iba a
    /// aceptar, así que un intento fallido —«no tenía anotado»— igual dejaba el PNG en disco.
    /// Reproducido con la sonda de desarrollo el 2026-08-24: tres intentos fallidos, tres archivos.
    /// Por eso son DOS llamadas a `Ensenar`: la primera, sin foto, es la que de verdad decide si hay
    /// recuerdo; solo si esa contesta que sí se toma la foto y se vuelve a llamar para colgarla.
    /// </remarks>
    private string EstoEs(string significado, string sobre)
    {
        if (significado.Length == 0)
            return "falta `significado`: qué es o para qué sirve lo que se está enseñando.";
        if (Ensenar == null) return "todavía no sé guardar lo que me enseñas.";

        string donde = _where()?.Id ?? "";
        bool hayGesto = _ultimoSenalado is { } s
                     && Navigation.LoQueSenalas.SigueValiendo(s.Cuando, DateTime.UtcNow);

        string selector, nombre, tipo;
        if (hayGesto)
        {
            var ult = _ultimoSenalado!.Value;
            selector = ult.Selector;
            nombre = ult.Nombre;
            tipo = "";
        }
        else if (sobre.Length > 0 && BuscarEnPantalla(sobre) is { } visto)
        {
            selector = Uia.Reconocedor.SelectorDe(visto);
            nombre = visto.Label;
            tipo = visto.ControlType;
        }
        // DENTRO DE SAP, WINDOWS NO VE NADA (promesa 117). El lector de arriba es UIA, y en una
        // sesión de SAP se queda en un Pane opaco: enseñar un campo del triage por su nombre era
        // imposible salvo con el cursor encima. El terreno SÍ los tiene, con su identidad
        // «sap:wnd[0]/usr/...», así que cuando Windows no encuentra lo que se nombra se le pregunta
        // al grafo por lo que hay VIVO aquí. Un empate no se adivina: lo dice ElCampoQueNombras.
        else if (sobre.Length > 0
                 && LoQueSeNombra(sobre, PuertasVivas?.Invoke(donde), CamposDeSap?.Invoke()) is { } nombrado)
        {
            // …o un campo del dynpro por su etiqueta (promesa 188): «Presión Arterial» ya se cuelga.
            selector = nombrado.Selector;
            nombre = nombrado.Etiqueta;
            tipo = nombrado.Tipo;
        }
        else if (sobre.Length > 0)
        {
            return $"no veo nada que se llame «{sobre}» en esta pantalla, así que no sé a qué "
                 + "colgarle eso. Señálamelo con el cursor y te escucho, o dime el nombre tal "
                 + "como se lee.";
        }
        // LO QUE SE ACABA DE HACER TAMBIÉN SE PUEDE ENSEÑAR. «Recuérdalo, después de escribir NWP1
        // siempre hay que hacer scroll hasta el fondo» se dice JUSTO DESPUÉS del scroll, sin señalar
        // nada: la lección es sobre la acción, y el sujeto es el panel que se acaba de desplazar.
        // Sin esto, esa frase no tenía dónde caer y se perdía — pasó tres veces seguidas
        // (2026-08-24). Va la última porque el gesto y el nombre son más explícitos: solo se recurre
        // a la última acción cuando no hay nada mejor.
        else if (Uia.Desplazamiento.UltimoDesplazado.Selector.Length > 0)
        {
            var d = Uia.Desplazamiento.UltimoDesplazado;
            selector = d.Selector; nombre = d.Etiqueta; tipo = d.Tipo;
            LogBus.Log("recuerdo", $"sin gesto ni nombre: lo cuelgo de lo último desplazado, «{nombre}»");
        }
        else
        {
            return "no me has señalado nada ni me has dicho de qué hablas. Ponme el cursor encima, "
                 + "o pásame `sobre` con el nombre del elemento tal como se lee en pantalla.";
        }

        // SE PRESENTA ANTES DE ENSEÑAR. El grafo solo acepta un recuerdo sobre algo que haya visto
        // en esta pantalla —y hace bien: una frase sin sujeto no se puede volver a encontrar—, pero
        // su lista son las PUERTAS, lo que se puede pulsar. Un panel que se desplaza no es una
        // puerta, así que señalarlo, verlo, iluminarlo… y aun así oír «no tenía anotado eso aquí»
        // (2026-08-24, dos rechazos seguidos con el elemento delante y encendido en pantalla).
        //
        // Presentarlo no afloja la regla: la cumple. Lo estamos viendo AQUÍ, ahora mismo — eso es
        // exactamente lo que la regla pide, solo que quien lo vio fue el cursor y no el mapeador.
        Presentar?.Invoke(donde, selector, nombre, tipo);

        // REESCRIBIR UN RECUERDO SE DICE. Volver a enseñar algo es legítimo —se explica mejor, se
        // corrige— pero pisar en silencio lo que alguien enseñó no: cuando pasó de verdad, el
        // recuerdo del usuario quedó sustituido por un resumen que el propio modelo acababa de
        // recitar, y no había forma de enterarse (2026-08-24). Queda en el log lo de antes y lo de
        // ahora, que es lo único que permite recuperarlo si el cambio no era el que se quería.
        string previo = RecuerdosAqui?.Invoke(donde)
            .FirstOrDefault(r => r.Selector.Equals(selector, StringComparison.OrdinalIgnoreCase))
            .Significado ?? "";
        if (previo.Length > 0 && !previo.Equals(significado, StringComparison.Ordinal))
            LogBus.Log("recuerdo", $"REESCRITO «{nombre}»: antes «{previo}» → ahora «{significado}»");

        if (!Ensenar(donde, selector, significado, ""))
            return $"no tenía anotado «{nombre}» en esta pantalla; señálalo otra vez y te escucho.";

        // AHORA SÍ HAY RECUERDO: la foto se toma y se cuelga con una segunda llamada. Si esta
        // segunda fallara —no debería, el elemento seguía ahí hace un instante— el recuerdo ya
        // creado se queda sin foto, que es mejor que un recuerdo a medias por una foto que falló.
        string foto = GuardarFotoDeLoSenalado(nombre);
        if (foto.Length > 0) Ensenar(donde, selector, significado, foto);

        UltimaFotoDeRecuerdo = foto;
        LogBus.Log("recuerdo", $"«{nombre}» en «{donde}» → {significado}"
            + (foto.Length > 0 ? $" · foto {System.IO.Path.GetFileName(foto)}" : " · sin foto"));
        return $"nuevo recuerdo: «{nombre}» es {significado}. Lo recordaré cuando vuelva aquí.";
    }

    /// <summary>
    /// «¿QUÉ SABES DE ESTA PANTALLA?»: enseña los recuerdos de aquí, UNO A UNO y señalándolos.
    /// </summary>
    /// <remarks>
    /// Contarlos de corrido —«aquí me enseñaste A, B y C»— es lo que ya hacía map_where_am_i, y se
    /// queda corto por lo mismo que «sí, lo veo» se queda corto sin señalar: quien pregunta qué
    /// sabes de una pantalla está comprobando que lo aprendido corresponde con lo que él tiene
    /// delante, y eso solo se sabe VIÉNDOLO marcado (2026-08-24, pedido por el usuario tras oír los
    /// dos recuerdos recitados sin que se encendiera nada).
    ///
    /// UNO POR LLAMADA, Y ESA ES TODA LA COREOGRAFÍA. Iluminarlos todos a la vez y soltar la lista
    /// de un tirón desincroniza lo que se oye de lo que se ve: para cuando la voz llega al tercero,
    /// los tres llevan encendidos diez segundos. Devolviendo uno cada vez, la voz habla ENTRE
    /// llamadas y el recuadro va donde va la frase, sin necesidad de temporizadores ni de adivinar
    /// cuánto tarda en decirse cada cosa.
    ///
    /// EL QUE YA NO ESTÁ TAMBIÉN SE CUENTA. Un recuerdo cuyo elemento no aparece en pantalla se dice
    /// igual, avisando de que no se pudo señalar: callarlo sería esconder justo lo que hay que
    /// revisar —una pantalla que cambió— y dejar al usuario creyendo que ese recuerdo se perdió.
    /// </remarks>
    private string Recuerdos(string cualPedido)
    {
        string donde = _where()?.Id ?? "";
        if (donde.Length == 0) return "no sé dónde estoy, así que no sé qué recuerdos son de aquí.";

        var todos = RecuerdosAqui?.Invoke(donde) ?? Array.Empty<(string, string, string)>();
        if (todos.Count == 0)
        {
            Ui.Senalador.Soltar();
            TurnoDeContar.Reiniciar();
            SiguienteRecuerdoPendiente = 0;
            return $"todavía no me has enseñado nada en «{donde}». Señálame algo y dime qué es.";
        }

        // PEDIR EL PRIMERO EMPIEZA UNA TANDA NUEVA. Sin esto, la segunda vez que alguien pregunta
        // «¿qué recuerdas de aquí?» arrastraría la cuenta de la vez anterior y el primero se
        // encontraría con que «ya se contó».
        if (cualPedido.Trim().Length == 0) TurnoDeContar.Reiniciar();

        // Sin número: se empieza por el primero. Con número: ese. Fuera de rango se dice, no se
        // recorta en silencio a otro — contar un recuerdo distinto del que se pidió es peor que decir
        // que ese no existe.
        int cual = int.TryParse(cualPedido.Trim(), out int n) ? n : 1;
        if (cual < 1 || cual > todos.Count)
            return $"aquí tengo {todos.Count} recuerdo(s), del 1 al {todos.Count}: no hay un número {cual}.";

        // UNO NO SE CUENTA HASTA HABER CONTADO EL ANTERIOR. Ver ElTurnoDeContar: encadenar las
        // llamadas sin hablar deja el recuadro sobre el último mientras la voz cuenta todos, que es
        // exactamente lo contrario de señalar de lo que se habla.
        if (!TurnoDeContar.PuedeContar(cual))
        {
            // DOS MOTIVOS DISTINTOS PARA ESPERAR, y conviene decir cuál: uno se arregla hablando y
            // el otro se arregla solo, esperando a que la persona termine. El segundo se da cuando
            // se está corrigiendo una tarjeta abierta desde el panel mientras la voz narra.
            if (Ui.TarjetasDeRecuerdo.EscribiendoAlguna)
            {
                LogBus.Log("recuerdo", $"pidió el {cual} mientras se corrige a mano: espera");
                return "[interno] está corrigiendo un recuerdo a mano. No pases al siguiente ni "
                     + "digas nada de esto; espera a que termine.";
            }

            LogBus.Log("recuerdo", $"pidió el {cual} sin haber contado el anterior: se le hace esperar");
            return $"[interno] todavía no has contado el {cual - 1} en voz. Cuéntalo y vuelve a "
                 + $"pedir el {cual}. No leas esto en voz alta.";
        }

        var r = todos[cual - 1];
        TurnoDeContar.SeConto(cual);
        SiguienteRecuerdoPendiente = cual < todos.Count ? cual + 1 : 0;
        bool marcado = IluminarUno(r.Selector, r.Etiqueta);

        LogBus.Log("recuerdo", $"contando {cual}/{todos.Count} en «{donde}»: «{r.Etiqueta}»"
            + (marcado ? " · iluminado" : " · NO está en pantalla"));

        // LO QUE VUELVE DE AQUÍ SE ACABA DICIENDO EN VOZ ALTA, así que solo puede llevar lo que
        // valga la pena decir. Llevaba «Pídeme el 2 cuando termines de contar este» —una frase
        // dirigida al modelo— y el modelo se la leía al usuario tal cual: «ahora vuelve a pedirme el
        // recuerdo 2 cuando…». Desde fuera parecía que el sistema le pedía a la persona que hiciera
        // el trabajo (2026-08-24, visto en pantalla por el usuario).
        //
        // Y LO QUE SE LE PIDA AQUÍ ES LO ÚNICO QUE VA A HACER. La primera versión de esta nota decía
        // «quedan 1; sigue con el 2» y el modelo hizo exactamente eso: pidió el 2 un segundo después
        // SIN haber contado el 1. La nota le dijo que avanzara, no que contara — y avanzó. Aquí solo
        // se le pide CONTAR; avanzar lo empuja el continuador cuando ya ha hablado, que es quien
        // sabe si habló.
        return $"recuerdo {cual} de {todos.Count} — «{r.Etiqueta}»: {r.Significado}"
             + (marcado ? "" : " [interno] ese elemento ya no está en pantalla; dilo al contarlo.")
             + " [interno] cuéntalo en voz AHORA, con tus palabras. No pidas otro todavía.";
    }

    /// <summary>
    /// La caja que UIA le da a un elemento POR SU NOMBRE, o null si no lo ve. Promesa 144.
    /// </summary>
    /// <remarks>
    /// APLANANDO ANTES DE COMPARAR, y no es cosmético: en la sonda del 2026-09-03, de doce botones
    /// de barra casaron ocho a la primera. Los dos lados escriben el mismo rótulo de forma distinta,
    /// y comparar en crudo da falso EN SILENCIO — el aprendizaje nº16 de este repo.
    ///
    /// Lo exacto primero. Sin coincidencia, NADA: estirar esto a «contiene» encontraría «Triage» en
    /// «Triage del paciente» y dibujaría la caja del vecino, que es peor que no dibujar (nº4).
    /// </remarks>
    private System.Windows.Rect? CajaPorNombreEnUia(string nombre)
    {
        string busco = Navigation.Nombres.Aplanar(nombre);
        if (busco.Length == 0) return null;
        foreach (var e in _lector.Elements)
            if (Navigation.Nombres.Aplanar(e.Label) == busco && e.Bounds.Width > 0)
                return e.Bounds;
        return null;
    }

    /// <summary>
    /// TODOS los recuerdos de esta pantalla que se puedan localizar, con su caja. Para verlos de un
    /// vistazo desde el panel, sin pedírselo a la voz.
    /// </summary>
    /// <remarks>
    /// Los que ya no están en pantalla se quedan fuera y no se dicen: aquí no hay una voz que pueda
    /// explicar «este lo recuerdo pero no lo veo», y un cartel flotando sobre nada sería peor que su
    /// ausencia. Quien quiera esa distinción la tiene contándolos con map_recuerdos, que sí la dice.
    /// </remarks>
    public IReadOnlyList<(System.Windows.Rect Caja, string Selector, string Etiqueta, string Significado)> RecuerdosEnPantalla()
    {
        var salida = new List<(System.Windows.Rect, string, string, string)>();
        string donde = _where()?.Id ?? "";
        if (donde.Length == 0) return salida;

        var todos = RecuerdosAqui?.Invoke(donde) ?? Array.Empty<(string, string, string)>();
        if (todos.Count == 0) return salida;

        try
        {
            // CADA CAJA LA DA SU MUNDO (promesa 119). Buscarlas siempre en el lector de UIA dejaba
            // los recuerdos de SAP sin encender —«0 de 6 localizados» sobre el triage el
            // 2026-09-02—, porque dentro de una sesión de SAP UIA no ve ni un campo.
            //
            // A SAP SE LE PREGUNTA UNA VEZ, y solo si hay algo suyo que localizar: la lectura del
            // dynpro es cara y esto corre en el hilo de la interfaz.
            IReadOnlyList<(string Selector, string Etiqueta, string Tipo, System.Windows.Rect Caja)>? cajasSap = null;
            bool haySap = todos.Any(r => U.Graph.Surfaces.SapSelector.Owns(r.Selector));
            var geometria = new Navigation.GeometriaPorMundo(uia: CajaPorUia, sap: s =>
            {
                if (!haySap || CajasEnSap == null) return null;
                cajasSap ??= CajasEnSap() ?? Array.Empty<(string, string, string, System.Windows.Rect)>();
                return Navigation.LaCajaDeUnaIdentidadDeSap.De(s, cajasSap, CajaPorNombreEnUia);
            });
            _lector.Read();
            foreach (var r in todos)
            {
                var caja = geometria.Caja(r.Selector);
                if (caja == null && !U.Graph.Surfaces.SapSelector.Owns(r.Selector))
                {
                    // Respaldo por etiqueta, solo en el mundo de UIA: en SAP la identidad es exacta
                    // y dos campos pueden compartir etiqueta.
                    var porNombre = _lector.Elements.FirstOrDefault(
                        e => e.Label.Equals(r.Etiqueta, StringComparison.OrdinalIgnoreCase));
                    if (porNombre != null) caja = porNombre.Bounds;
                }
                if (caja is { } c) salida.Add((c, r.Selector, r.Etiqueta, r.Significado));
            }
        }
        catch (Exception e) { LogBus.Log("recuerdo", $"no pude localizar los recuerdos: {e.Message}"); }

        LogBus.Log("recuerdo", $"vista de recuerdos en «{donde}»: {salida.Count} de {todos.Count} localizados");
        return salida;
    }

    /// <summary>
    /// «map_skills»: qué tareas me han enseñado, cuáles están listas para usar, y QUÉ DATOS necesita
    /// cada una. Promesas 106, 127 y 193.
    /// </summary>
    /// <remarks>
    /// EL CATÁLOGO DICE SI ESTÁ COMPROBADA, y eso no es un adorno: comprobar es obligatorio
    /// (decisión del dueño, 2026-09-03), así que anunciar una skill sin decir que está pendiente
    /// sería ofrecerle al cerebro algo que después le van a negar. Se anuncia el estado, no solo
    /// el nombre.
    ///
    /// Y DICE QUÉ DATOS NECESITA (promesa 193, 2026-09-10): el piloto elige la skill para un
    /// encargo de la nota por su propio criterio; sin los huecos sabría qué hace pero no qué
    /// pedirle a la nota, y map_skill_run llegaría sin datos.
    /// </remarks>
    private string Skills() => AnuncioDeLasSkills(Navigation.SkillEnsenada.Catalogo(CarpetaDeSkills));

    /// <summary>De dónde se leen las skills: la carpeta del usuario, salvo que una prueba ponga la suya.</summary>
    public string CarpetaDeSkills { get; set; } = Navigation.SkillEnsenada.CarpetaPorDefecto;

    /// <summary>El texto del catálogo, puro: una línea por skill con su estado y sus datos.</summary>
    public static string AnuncioDeLasSkills(IReadOnlyList<Navigation.SkillAnunciada> catalogo)
    {
        catalogo ??= Array.Empty<Navigation.SkillAnunciada>();
        if (catalogo.Count == 0)
            return "no me han enseñado ninguna tarea todavía: pulsa Enseñar, hazla una vez hablando, "
                 + "y después «Comprobar aprendizaje».";
        var lineas = catalogo.Select(c =>
            $"· «{c.Nombre}»{(c.Description.Length > 0 ? " — " + c.Description : "")}"
            + (c.Comprobada ? " · lista" : " · PENDIENTE de comprobar, no se puede ejecutar")
            + (c.Huecos.Count > 0 ? $" · necesita: {string.Join(", ", c.Huecos)}" : " · no necesita datos"));
        var salida = new List<string> { $"tareas que me has enseñado ({catalogo.Count}):" };
        salida.AddRange(lineas);
        salida.Add("Para correr una: map_skill_run con su nombre y `datos` = {\"<dato>\":\"<valor>\"} usando los nombres "
                 + "de «necesita» tal cual. Lo que no pases queda en blanco y se te dice.");
        return string.Join(Environment.NewLine, salida);
    }

    /// <summary>
    /// «map_skill_run»: reproducir una tarea enseñada. Promesas 122, 123, 126, 127, 196 y 197.
    /// </summary>
    /// <remarks>
    /// NO HAY SEGUNDO EJECUTOR: la skill se TRADUCE a pasos del batch y los corre el mismo
    /// recorredor que todo lo demás — misma compuerta de vida, misma verificación por consecuencia,
    /// misma cuenta honesta, mismo freno. Dos ejecutores del mismo hecho acabarían contradiciéndose
    /// sin avisar, que es la razón por la que el player viejo se congela.
    ///
    /// LA COMPUERTA DE LA COMPROBACIÓN VA PRIMERO, antes de tocar nada: una skill sin repasar no se
    /// ejecuta, y el «no» dice qué falta.
    ///
    /// Y CON LA COREOGRAFÍA DEL PLAN cuando la app señala al actuar (promesa 197, 2026-09-10): cada
    /// paso de uno en uno por <see cref="DarUnPasoConCoreografia"/> —la carita al lado del campo,
    /// el nombre del dato dicho en voz, y solo entonces el toque—. El dueño pidió UNA experiencia;
    /// un batch ciego era la otra. Sin la app señalando (la voz en vivo pidiendo una skill), sigue
    /// siendo el batch de siempre.
    /// </remarks>
    private string CorrerSkill(string nombre, string datosJson)
    {
        if (nombre.Length == 0)
            return "falta `nombre`: cuál de las tareas enseñadas hay que hacer. Pídelas con map_skills.";
        if (RecorrerPorElNucleo == null) return "todavía no sé recorrer en batch.";
        var catalogo = Navigation.SkillEnsenada.Catalogo(CarpetaDeSkills);
        var anunciada = catalogo.FirstOrDefault(c => Navigation.Nombres.Aplanar(c.Nombre) == Navigation.Nombres.Aplanar(nombre))
                     ?? catalogo.FirstOrDefault(c => Navigation.Nombres.Aplanar(c.Nombre)
                            .Contains(Navigation.Nombres.Aplanar(nombre), StringComparison.Ordinal));
        if (anunciada == null)
            return $"no tengo ninguna tarea que se llame «{nombre}». Las que sí: "
                 + (catalogo.Count > 0 ? string.Join(", ", catalogo.Select(c => $"«{c.Nombre}»")) : "ninguna todavía.");
        var skill = Navigation.SkillEnsenada.Cargar(anunciada.Archivo);
        if (skill == null) return $"«{anunciada.Nombre}» está en disco pero no se deja leer: {anunciada.Archivo}";
        var veredicto = skill.PuedeCorrer();
        if (!veredicto.Puede) return veredicto.Motivo;
        var datos = LeerDatos(datosJson);
        var pasos = Navigation.InstanciarSkill.Pasos(skill, datos);
        if (pasos.Count == 0)
            return $"«{skill.Nombre}» no dejó ningún paso que correr con estos datos: "
                 + (skill.Huecos.Count > 0
                    ? $"necesita {string.Join(", ", skill.Huecos.Select(h => $"«{h.Significado}»"))}."
                    : "la skill está vacía.");
        var sobrantes = Navigation.InstanciarSkill.Sobrantes(skill, datos);
        // LO QUE QUEDA EN BLANCO SE DICE (promesa 196): no se pregunta ni se inventa, pero un campo
        // vacío sin rastro parecería un envío completo, y eso es lo peor que puede pasar.
        var enBlanco = Navigation.InstanciarSkill.SinDato(skill, datos);
        LogBus.Log("skill", $"corriendo «{skill.Nombre}»: {pasos.Count} paso(s) de {skill.Pasos.Count} "
            + $"· {datos.Count} dato(s)"
            + (enBlanco.Count > 0 ? $" · en blanco: {string.Join(", ", enBlanco)}" : "")
            + (sobrantes.Count > 0 ? $" · sin hueco: {string.Join(", ", sobrantes)}" : "")
            + (DarUnPasoConCoreografia != null && SenalarAlActuar ? " · con coreografía" : " · en batch"));
        var nombreDelDato = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in skill.Huecos) if (h.Campo.Length > 0) nombreDelDato[h.Campo] = h.Significado;
        var res = DarUnPasoConCoreografia != null && SenalarAlActuar
            ? RecorrerSkill(pasos, p => DarUnPasoConCoreografia(p.Exit, p, "",
                p.Texto.Length > 0 && nombreDelDato.TryGetValue(p.Exit, out var dato) ? dato : ""))
            : RecorrerPorElNucleo(pasos);
        string cuenta = res.Cuenta;
        LogBus.Log("skill", "← " + cuenta);
        return $"«{skill.Nombre}»: {cuenta}"
             + (enBlanco.Count > 0 ? $" Quedaron EN BLANCO por falta de dato: {string.Join(", ", enBlanco)}." : "")
             + (sobrantes.Count > 0
                ? $" No supe dónde va: {string.Join(", ", sobrantes)} — enséñamelo y lo recuerdo."
                : "");
    }

    /// <summary>
    /// UNA SKILL DE UNO EN UNO (promesa 197): cada paso por la coreografía; el primero que no se da
    /// para el resto, y el total es el plan, nunca lo ejecutado (patrón nº10).
    /// </summary>
    public static Navigation.RecorrerSegunElNucleo.Resultado RecorrerSkill(
        IReadOnlyList<Navigation.RecorrerSegunElNucleo.Paso> pasos,
        Func<Navigation.RecorrerSegunElNucleo.Paso, Navigation.RecorrerSegunElNucleo.Resultado> darUnPaso)
    {
        pasos ??= Array.Empty<Navigation.RecorrerSegunElNucleo.Paso>();
        int hechos = 0; string donde = "";
        for (int i = 0; i < pasos.Count; i++)
        {
            var r = darUnPaso(pasos[i]);
            if (r.Donde.Length > 0) donde = r.Donde;
            if (r.Hechos < 1)
            {
                string que = pasos[i].Exit.Length > 0 ? pasos[i].Exit : pasos[i].Tecla.Length > 0 ? "tecla " + pasos[i].Tecla : $"paso {i + 1}";
                return new(hechos, pasos.Count, donde, false,
                    $"hice {hechos} de {pasos.Count} y paré en el paso {i + 1} «{que}»: {r.Cuenta}");
            }
            hechos++;
        }
        return new(hechos, pasos.Count, donde, true,
            $"hice los {pasos.Count} paso(s)" + (donde.Length > 0 ? $": quedaste en «{donde}»" : ""));
    }


    /// <summary>Los datos de esta corrida: `{"peso":"68"}`. Vacío si no vienen o no se entienden.</summary>
    private static Dictionary<string, string> LeerDatos(string json)
    {
        var datos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json)) return datos;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return datos;
            foreach (var p in doc.RootElement.EnumerateObject())
                datos[p.Name] = p.Value.ValueKind == System.Text.Json.JsonValueKind.String
                    ? p.Value.GetString() ?? "" : p.Value.ToString();
        }
        catch (Exception e) { LogBus.Log("skill", $"no entendí `datos` como JSON: {e.Message}"); }
        return datos;
    }

    /// <summary>
    /// «map_batch»: la lista de pasos llega como JSON y se le entrega al recorredor. Aquí solo se
    /// TRADUCE — la compuerta, el freno y el relato viven en Navigation/RecorrerSegunElNucleo,
    /// donde se pueden juzgar sin pantalla.
    /// </summary>
    private string Batch(string pasosJson)
    {
        if (RecorrerPorElNucleo == null) return "todavía no sé recorrer en batch.";
        if (string.IsNullOrWhiteSpace(pasosJson))
            return "falta `pasos`: una lista JSON de pasos, p. ej. "
                 + "[{\"exit\":\"Descargas\"},{\"exit\":\"Facturas\"},{\"text\":\"informe\"}].";

        var pasos = new List<Navigation.RecorrerSegunElNucleo.Paso>();
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(pasosJson);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array)
                return "«pasos» tiene que ser una LISTA de pasos, no un objeto suelto.";
            foreach (var p in doc.RootElement.EnumerateArray())
            {
                string exit = p.TryGetProperty("exit", out var e) ? e.GetString() ?? "" : "";
                string text = p.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
                if (exit.Length == 0 && text.Length == 0)
                    return $"el paso {pasos.Count + 1} no trae ni `exit` ni `text`: no sé qué hacer con él.";
                pasos.Add(new Navigation.RecorrerSegunElNucleo.Paso(exit, text));
            }
        }
        catch (Exception ex) { return $"no entendí `pasos` como JSON: {ex.Message}"; }

        if (pasos.Count == 0) return "la lista de pasos vino vacía.";

        LogBus.Log("batch", $"recorrido de {pasos.Count} paso(s): "
            + string.Join(" → ", pasos.Select(p => p.Texto.Length > 0 ? $"escribir «{p.Texto}»" : $"«{p.Exit}»")));
        string cuenta = RecorrerPorElNucleo(pasos).Cuenta;
        LogBus.Log("batch", "← " + cuenta);
        return cuenta;
    }

    /// <summary>Enciende UN elemento por su selector. False si ya no está en pantalla.</summary>
    /// <remarks>
    /// SOLO EL RECUADRO, SIN EL TEXTO. Se probó enseñando también la nota mientras se narra y
    /// estorbaba: la voz ya está diciendo lo mismo, así que el texto encima es una segunda copia
    /// tapando la pantalla (2026-08-24, dicho por el usuario: «ahora que muestra el texto es
    /// incómodo»). El texto se lee cuando se pide con los ojos —el botón del panel—, no cuando se
    /// está escuchando.
    /// </remarks>
    /// <summary>
    /// Enciende el recuadro de un elemento y lleva la carita a su lado. Devuelve si se pudo.
    /// </summary>
    /// <remarks>
    /// LO USA LA COMPROBACIÓN: mientras Ü cuenta qué entendió de un elemento, el humano tiene que
    /// poder ver DE CUÁL habla — «que vaya y lo señale, acercando la carita al lado de ese
    /// elemento» (el dueño, 2026-09-03). Sin eso la narración es correcta y no se puede seguir.
    /// </remarks>
    public bool SenalarElemento(string selector, string etiqueta) => IluminarUno(selector, etiqueta);

    private bool IluminarUno(string selector, string etiqueta)
    {
        try
        {
            // POR EL MUNDO DEL QUE ES (promesa 119). Este es el camino de «cuéntame los recuerdos de
            // aquí», que los señala uno a uno: buscándolos siempre en UIA, los de SAP no se
            // encendían ninguno — el mismo agujero que la vista de golpe.
            if (U.Graph.Surfaces.SapSelector.Owns(selector))
            {
                // POR EL ÚNICO SITIO QUE HAY (promesa 145). Aquí vivía una segunda copia del mapeo
                // selector→caja, y dejó de coincidir con la otra en cuanto los botones de barra
                // empezaron a entrar sin caja: ésta los habría iluminado en la esquina.
                var enSap = Navigation.LaCajaDeUnaIdentidadDeSap.De(selector,
                    CajasEnSap?.Invoke() ?? Array.Empty<(string, string, string, System.Windows.Rect)>(),
                    CajaPorNombreEnUia);
                if (enSap is not { } caja) return false;
                _lector.Read();   // el buscador por rótulo del puente necesita la pantalla leída
                Ui.Senalador.Senalar(caja, etiqueta);
                return true;
            }

            var cronoLectura = System.Diagnostics.Stopwatch.StartNew();
            _lector.Read();
            // CUÁNTO CUESTA SEÑALAR (spec 030, nivel 4): esta lectura va ANTES de la compuerta y del clic, y en una
            // página recién cargada de Chrome puede ser lo que la persona vive como «demora entre pulsando y el clic».
            LogBus.Log("mano", $"señalar «{etiqueta}»: leí la ventana en {cronoLectura.ElapsedMilliseconds} ms ({_lector.Elements.Count} elemento(s))");
            var visto = _lector.Elements.FirstOrDefault(
                            e => Uia.Reconocedor.SelectorDe(e).Equals(selector, StringComparison.OrdinalIgnoreCase))
                        // Por etiqueta como último recurso: un selector puede envejecer —cambia una
                        // ruta, se renombra un automation id— y el elemento seguir ahí con su nombre.
                        ?? _lector.Elements.FirstOrDefault(
                            e => e.Label.Equals(etiqueta, StringComparison.OrdinalIgnoreCase));
            if (visto == null)
            {
                // UN CAMPO DE SAP NOMBRADO POR SU ETIQUETA (promesa 188): «Presión Arterial» no es un
                // selector ni lo ve UIA, pero el dynpro lo conoce. Se resuelve a su selector sap: y se
                // señala por la caja que SAP declara, que es la rama de arriba.
                string donde = _where()?.Id ?? "";
                if (donde.StartsWith("sapgui://", StringComparison.OrdinalIgnoreCase)
                    && LoQueSeNombra(etiqueta.Length > 0 ? etiqueta : selector, PuertasVivas?.Invoke(donde), CamposDeSap?.Invoke()) is { } enSap
                    && U.Graph.Surfaces.SapSelector.Owns(enSap.Selector))
                    return IluminarUno(enSap.Selector, etiqueta.Length > 0 ? etiqueta : enSap.Etiqueta);
                return false;
            }
            Ui.Senalador.Senalar(visto.Bounds, etiqueta);
            return true;
        }
        catch (Exception e) { LogBus.Log("recuerdo", $"no pude iluminar «{etiqueta}»: {e.Message}"); }
        return false;
    }

    /// <summary>
    /// El elemento que se llama así en la pantalla de AHORA, o null. Exacto primero, y si no,
    /// el que lo contenga — quien enseña dice «Acceder al sistema» y el botón puede llamarse
    /// «Acceder al sistema (Enter)».
    /// </summary>
    private UiaReader.UiElement? BuscarEnPantalla(string nombre)
    {
        try
        {
            _lector.Read();
            var puertas = _lector.Elements.Where(e => e.Label.Length > 0 && EsPuertaVisible(e)).ToList();
            return puertas.FirstOrDefault(e => e.Label.Equals(nombre, StringComparison.OrdinalIgnoreCase))
                ?? puertas.FirstOrDefault(e => e.Label.Contains(nombre, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception e) { LogBus.Log("recuerdo", $"no pude buscar «{nombre}»: {e.Message}"); return null; }
    }
    private string LoQueSenala()
    {
        try
        {
            if (!GetCursorPos(out var p)) return "no pude leer dónde está el cursor";

            // DENTRO DE SAP MANDA SAP (promesa 118). Preguntarle a UIA aquí devuelve el Pane opaco
            // que lo contiene todo: señalando la casilla de la presión arterial contestaba «Gos
            // Container» (2026-09-02, visto por el dueño). Y si SAP no reconoce el punto NO se cae a
            // UIA: se dice que no se sabe, que es justo lo que evita volver al panel.
            var senaladoSap = new Navigation.LoSenaladoPorMundo(
                () => _where()?.Id ?? "", (x, y) => SenaladoEnSap?.Invoke(x, y));
            var bajoElCursor = senaladoSap.ElPunto(p.X, p.Y);
            if (bajoElCursor.MandaSap)
            {
                if (bajoElCursor.Que is not { } enSap)
                    return "estás sobre SAP pero ahí no hay ningún campo que SAP reconozca; "
                         + "apunta a un campo, un botón o una fila.";

                // SE ENCIENDE DE VERDAD, y luego se dice. El 2026-09-02 esta rama contestaba «lo
                // estoy iluminando» y salía sin llamar al señalador: la respuesta era cierta en el
                // texto y falsa en la pantalla, que es la peor clase de mentira de este repo.
                bool encendido = !enSap.Caja.IsEmpty && enSap.Caja.Width >= 1 && enSap.Caja.Height >= 1;
                if (encendido) Ui.Senalador.Senalar(enSap.Caja, enSap.Etiqueta);

                _ultimoSenalado = (enSap.Etiqueta, enSap.Selector, null, DateTime.UtcNow);
                Senalar?.Invoke(new Navigation.LoQueSenalas.Senalado(enSap.Etiqueta, enSap.Tipo, encendido));
                LogBus.Log("recuerdo", $"señalado en SAP: «{enSap.Etiqueta}» ({enSap.Tipo}) · "
                    + $"{enSap.Selector} · {(encendido ? $"iluminado en {enSap.Caja}" : "SIN caja: no se pudo iluminar")}");
                return encendido
                    ? $"señalas «{enSap.Etiqueta}» ({enSap.Tipo}). Lo estoy iluminando."
                    : $"señalas «{enSap.Etiqueta}» ({enSap.Tipo}), pero SAP no da su posición y no puedo marcarlo.";
            }

            var el = System.Windows.Automation.AutomationElement.FromPoint(
                new System.Windows.Point(p.X, p.Y));
            if (el == null) return "bajo el cursor no hay ningún elemento que UIA reconozca";

            // Ü NO SE SEÑALA A SÍ MISMA. La carita flota por encima de todo y, peor, SE MUEVE al
            // lado de lo que ilumina — así que en cuanto señalas algo una vez, se coloca justo ahí y
            // tapa lo que quieras señalar después. Barriendo la barra de tareas el 2026-08-23:
            // x=660, 740, 820 y 1000 contestaban «señalas «Ü» (Window)» en vez del botón de debajo.
            //
            // Se salta preguntando de quién es la ventana, no por su nombre: llamarse «Ü» es una
            // casualidad que cualquier app puede repetir; ser NUESTRO proceso no. Y se mira lo que
            // hay debajo con WindowFromPoint saltando nuestras ventanas, que es exactamente lo que
            // haría alguien apartando un papel para leer el de abajo.
            el = SaltarNuestrasVentanas(el, p);
            if (el == null) return "bajo el cursor solo está la propia Ü; muévela o aparta el cursor.";

            var (etiqueta, tipo, sels) = UiaSurface.DescribeElement(el);
            string nombre = etiqueta.Length > 0 ? etiqueta : (el.Current.Name ?? "").Trim();

            // EL PUNTO MANDA, PERO LA PUERTA ES LO ÚTIL. Bajo el cursor puede haber un trozo interno
            // —el texto de un botón, la celda de una fila— y ese trozo no es lo que el usuario está
            // señalando: señala la PUERTA que lo contiene. Se busca entre lo que hay en pantalla la
            // puerta que cubre ese punto, y se prefiere la más pequeña, que es la más específica
            // (2026-08-05, pedido por el usuario: primero el cursor, luego buscarlo entre las
            // puertas).
            var punto = new System.Windows.Point(p.X, p.Y);
            _lector.Read();
            var puerta = _lector.Elements
                .Where(e => e.Label.Length > 0 && EsPuertaVisible(e) && e.Bounds.Contains(punto))
                .OrderBy(e => e.Bounds.Width * e.Bounds.Height)
                .FirstOrDefault();

            // La caja que se va a iluminar. Se prefiere la de la puerta —es la que el usuario
            // reconoce como «el elemento»— pero si no la hay, vale la del propio elemento bajo el
            // cursor: LO TENEMOS DELANTE, con su rectángulo. Exigir la puerta dejaba sin iluminar
            // —y sin mover la carita— todo lo que estuviera fuera de la ventana leída: la barra de
            // tareas, otra ventana, un menú (2026-08-05, «no se movió la carita al lado»).
            System.Windows.Rect caja = System.Windows.Rect.Empty;
            try { caja = el.Current.BoundingRectangle; } catch { }

            if (puerta != null) { nombre = puerta.Label; tipo = puerta.ControlType; caja = puerta.Bounds; }

            // Si el punto cayó en un trozo sin nombre —un Group, un panel interno— se sube por el
            // árbol hasta encontrar algo que sí lo tenga. Un contenedor anónimo no es una respuesta:
            // el usuario está señalando ALGO, y ese algo tiene nombre un poco más arriba.
            if (nombre.Length == 0)
            {
                var subiendo = System.Windows.Automation.TreeWalker.ControlViewWalker.GetParent(el);
                for (int i = 0; i < 5 && subiendo != null && nombre.Length == 0; i++)
                {
                    try
                    {
                        string n = (subiendo.Current.Name ?? "").Trim();
                        if (n.Length > 0)
                        {
                            nombre = n;
                            tipo = subiendo.Current.ControlType.ProgrammaticName.Replace("ControlType.", "");
                            try { caja = subiendo.Current.BoundingRectangle; } catch { }
                            break;
                        }
                    }
                    catch { }
                    subiendo = System.Windows.Automation.TreeWalker.ControlViewWalker.GetParent(subiendo);
                }
            }

            // Y SI ARRIBA NO HAY NOMBRE, SE MIRA ABAJO. La barra de tareas de Windows 11 es XAML:
            // `FromPoint` cae en un Pane sin nombre cuyo padre tampoco lo tiene, así que subir por
            // el árbol no encuentra nada y señalar contestaba «no hay nada con nombre» sobre una
            // barra llena de botones (2026-08-22, reproducido). El nombre estaba en los
            // DESCENDIENTES: ese Pane tenía 29, y dos contenían el punto —«Aplicaciones en
            // ejecución» (26.400 px²) y «Vista de tareas» (3.300 px²)—. El pequeño era el bueno.
            //
            // Quién gana lo decide Navigation/LoQueSenalas.Elegir, que es donde se puede juzgar sin
            // pantalla: el más pequeño con nombre que contiene el punto.
            if (nombre.Length == 0)
            {
                var candidatos = new List<Navigation.LoQueSenalas.Candidato>();
                try
                {
                    foreach (System.Windows.Automation.AutomationElement d in el.FindAll(
                        System.Windows.Automation.TreeScope.Descendants,
                        System.Windows.Automation.Condition.TrueCondition))
                    {
                        try
                        {
                            candidatos.Add(new Navigation.LoQueSenalas.Candidato(
                                (d.Current.Name ?? "").Trim(),
                                d.Current.ControlType.ProgrammaticName.Replace("ControlType.", ""),
                                d.Current.BoundingRectangle));
                        }
                        catch { }
                    }
                }
                catch (Exception e) { LogBus.Log("mapa-mcp", $"señalar: no pude mirar dentro: {e.Message}"); }

                var elegido = Navigation.LoQueSenalas.Elegir(candidatos, punto);
                if (elegido is { } c)
                {
                    nombre = c.Nombre; tipo = c.Tipo; caja = c.Caja;
                    LogBus.Log("mapa-mcp", $"señalar: el nombre estaba dentro, no arriba: «{nombre}»");
                }
            }

            if (nombre.Length == 0)
                return "bajo el cursor no hay nada con nombre, ni en él ni en lo que lo contiene. "
                     + "Muévelo un poco y vuelve a preguntar.";

            // Y se ilumina: si el usuario señala y el asistente dice un nombre, hay que poder
            // comprobar de un vistazo que hablan del mismo sitio.
            bool iluminado = !caja.IsEmpty && caja.Width >= 1 && caja.Height >= 1;
            if (iluminado) Ui.Senalador.Senalar(caja, nombre);

            // SE GUARDA LO SEÑALADO, CON SU IDENTIDAD. Señalar ya sabe encontrar cosas que el mapa
            // de esta pantalla NO tiene —un botón de la barra de tareas, otra ventana— pero pulsar
            // solo buscaba en la pantalla de delante, así que «¿ves esto? ábrelo» no funcionaba:
            // «no veo nada que se llame «Copilot anclado» en «web://copilot.microsoft.com»»
            // (2026-08-23, con el usuario señalando el icono de la barra).
            //
            // Se guarda el ELEMENTO, no su caja: pulsar por coordenadas acierta hasta que algo se
            // mueve, y entonces falla en silencio diciendo que funcionó.
            // LA IDENTIDAD TIENE QUE SER LA DE LO SEÑALADO, no la de por dónde se llegó. `sels` se
            // saca del elemento que hay bajo el punto, y cuando el nombre aparece MÁS ABAJO —la barra
            // de tareas, un contenedor XAML— ese selector es el del PADRE: se guardaba
            // «uia:path=;ct=Pane», que no vuelve a encontrar nada. Y un recuerdo se cuelga de esa
            // identidad, así que un selector malo no es un detalle: es un recuerdo perdido
            // (2026-08-23, visto en ensenanzas.json).
            //
            // NO SE TOMA FOTO NI SE ESCRIBE NADA EN EL GRAFO TODAVÍA (2026-08-24, pedido por el
            // usuario). Señalar no es enseñar: la mayoría de los señalados son solo mirar, y una
            // foto que nadie va a volver a ver es trabajo tirado. La foto se toma en map_esto_es,
            // justo cuando se sabe que hace falta de verdad — ver GuardarFotoDeLoSenalado.
            string suyo = sels.FirstOrDefault() ?? "";
            bool sirve = suyo.Length > 0 && !suyo.Contains("path=;") && !suyo.StartsWith("uia:path=;");
            _ultimoSenalado = (nombre, sirve ? suyo : $"uia:name={nombre};ct={tipo}", el, DateTime.UtcNow);
            LogBus.Log("mapa-mcp", $"señalado «{nombre}»");

            // LA RESPUESTA LA COMPONE EL NÚCLEO. Leer la pantalla —todo lo de arriba— es trabajo de
            // UIA y se queda aquí; decidir QUÉ se contesta sobre lo señalado es lo único que puede
            // equivocarse en silencio, y eso vive en Navigation/LoQueSenalas, donde se puede juzgar
            // sin un cursor delante.
            //
            // Lo que se decía antes era «nivel 3 · fijado a mano», y terminaba sugiriendo
            // map_set_level. O sea: la herramienta más usada del sistema (168 veces en 26 días)
            // anunciaba otra en CADA respuesta — y de ahí salían los 63 usos de aquella. Era
            // evidencia inducida. Lo que de verdad decide la frase siguiente de la conversación no
            // es un nivel: es si eso se puede pulsar ahora (2026-08-22).
            if (Senalar != null)
                return Senalar(new Navigation.LoQueSenalas.Senalado(nombre, tipo, iluminado));

            string estado = "aún sin recorrer: se aprende al cruzarlo";

            return $"señalas «{nombre}» ({tipo}) · {estado}"
                 + (iluminado ? " · lo estoy iluminando y me pongo a su lado" : " · no he podido iluminarlo (sin caja)")
                 + $". Lo puedo pulsar con map_take.";
        }
        catch (Exception e) { return $"no pude leer lo que hay bajo el cursor: {e.Message}"; }
    }

    /// <summary>
    /// «Ilumina todo ESTO que te estoy mostrando»: lo que el cursor ha tocado hace nada.
    ///
    /// Es la misma técnica que ya acertaba con un elemento —mirar qué hay bajo el cursor— repetida
    /// en el tiempo, que es exactamente como una persona enseña varias cosas: pasando la mano por
    /// encima. Antes esto se intentaba adivinando ZONAS de la pantalla por coordenadas, y pedir «la
    /// columna izquierda» devolvía la barra de título: una banda de píxeles no sabe qué agrupa
    /// (2026-08-05, propuesto por el usuario al ver que señalar de uno en uno sí funcionaba).
    /// </summary>
    private string LoQueMeAcabasDeMostrar(string segundosPedidos)
    {
        double segundos = double.TryParse(segundosPedidos, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out double s) && s > 0 ? s : 10;

        var marcas = Ui.RastroDelCursor.Ultimas(segundos);
        if (marcas.Count == 0)
        {
            Ui.Senalador.Soltar();
            return $"no has pasado el ratón por encima de nada en los últimos {segundos:0} segundos. "
                 + "Pásalo por lo que quieras enseñarme y dímelo otra vez.";
        }

        // Lo que se ATRAVIESA no es lo que se enseña. Al ir de un icono a otro el cursor cruza por
        // encima de cosas de paso, y meterlas convertiría «estos cuatro» en «estos once». Se filtra
        // igual que en el resto: solo lo que una persona llamaría un elemento.
        var buenas = marcas
            .Where(m => m.Caja.Width >= 12 && m.Caja.Height >= 12)
            .GroupBy(m => m.Nombre, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .OrderBy(m => m.Caja.Top).ThenBy(m => m.Caja.Left)
            .Take(25)
            .ToList();

        if (buenas.Count == 0)
        {
            Ui.Senalador.Soltar();
            return "por donde pasaste no había nada que pueda señalar.";
        }

        Ui.Senalador.SenalarVarias(buenas.Select(m => (m.Caja, m.Nombre)).ToList());

        string lista = string.Join(", ", buenas.Select(m => $"«{m.Nombre}» ({m.Tipo})"));
        return $"SÍ: por ahí pasaste {buenas.Count} cosa(s) y las estoy iluminando todas: {lista}. "
             + "Si sobra alguna o falta otra, vuelve a pasar el ratón y dímelo.";
    }

    /// <summary>
    /// «Excepto este»: quita uno de lo que ya está marcado y DEJA EL RESTO encendido.
    ///
    /// Sin esto, la única forma de responder a «excepto este» era señalar algo — y lo que se
    /// señalaba era justo el que se quería excluir, así que quedaba encendido él solo: exactamente
    /// lo contrario de lo pedido (2026-08-05). Corregir una selección es parte de hacerla; si cada
    /// frase empieza de cero, no se puede afinar nada.
    /// </summary>
    private string Excluir(string cual)
    {
        var marcadas = Ui.Senalador.Marcadas;
        if (marcadas.Count == 0)
            return "ahora mismo no tengo nada marcado, así que no hay de dónde quitar. "
                 + "Dime primero qué ilumino.";

        // Cuál quitar: el que se nombre, o —si no se nombra— el que esté bajo el cursor, porque
        // «excepto ESTE» se dice señalando.
        var fuera = new List<(System.Windows.Rect Caja, string Que)>();

        if (cual.Length > 0)
        {
            foreach (var n in cual.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                  .Select(x => x.Trim()).Where(x => x.Length > 0))
            {
                string norm = Uia.Reconocedor.Normalizar(n);
                fuera.AddRange(marcadas.Where(m =>
                    Uia.Reconocedor.Normalizar(m.Que) == norm
                    || Uia.Reconocedor.Normalizar(m.Que).Contains(norm, StringComparison.Ordinal)));
            }
        }

        if (fuera.Count == 0 && GetCursorPos(out var p))
        {
            var punto = new System.Windows.Point(p.X, p.Y);
            // La MÁS PEQUEÑA que contenga el cursor: si hay cajas anidadas, la de dentro es la que
            // se está señalando.
            var bajoElCursor = marcadas.Where(m => m.Caja.Contains(punto))
                                       .OrderBy(m => m.Caja.Width * m.Caja.Height)
                                       .Cast<(System.Windows.Rect Caja, string Que)?>()
                                       .FirstOrDefault();
            if (bajoElCursor != null) fuera.Add(bajoElCursor.Value);
        }

        if (fuera.Count == 0)
            return cual.Length > 0
                ? $"«{cual}» no está entre los que tengo marcados: {Marcados(marcadas)}. "
                  + "Dime cuál de esos quito."
                : "el cursor no está encima de ninguno de los que tengo marcados. Ponlo sobre el que "
                  + $"quieras quitar, o dime su nombre. Marcados: {Marcados(marcadas)}.";

        var quedan = marcadas.Where(m => !fuera.Any(f =>
            f.Caja == m.Caja && f.Que.Equals(m.Que, StringComparison.OrdinalIgnoreCase))).ToList();

        if (quedan.Count == 0)
        {
            Ui.Senalador.Soltar();
            return $"quitando {Marcados(fuera)} no queda ninguno marcado, así que lo he apagado todo.";
        }

        Ui.Senalador.SenalarVarias(quedan);
        return $"quitado {Marcados(fuera)}. Siguen marcados los otros {quedan.Count}: {Marcados(quedan)}.";
    }

    private static string Marcados(IReadOnlyList<(System.Windows.Rect Caja, string Que)> xs) =>
        string.Join(", ", xs.Take(25).Select(x => $"«{x.Que}»")) + (xs.Count > 25 ? "…" : "");

    /// <summary>
    /// Mueve de nivel UNO o VARIOS, separados por comas.
    /// </summary>
    /// <remarks>
    /// Corregir la jerarquía es una tarea de lista —«todos estos son del nivel principal»— y hacerlo
    /// de uno en uno son veinte llamadas y veinte confirmaciones habladas. El modelo lo intentó por
    /// su cuenta: mandó los diecinueve de la barra lateral separados por comas y la herramienta
    /// buscó una salida llamada «Inicio,Galería,OneDrive - Personal,…», que por supuesto no existe.
    /// Contestó que no podía, y luego los fue haciendo de uno en uno; las dos cosas eran verdad y
    /// por eso la respuesta pareció contradecirse (2026-08-05).
    ///
    /// Se informa de cada uno por separado: fijar quince y fallar en uno no es ni éxito ni fracaso,
    /// y quien pregunta necesita saber exactamente cuál se quedó fuera.
    /// </remarks>
    /// <summary>
    /// Anota lo que hay en pantalla y DESPUÉS fija el nivel.
    /// </summary>
    /// <remarks>
    /// Los puntos y el mapa son dos lectores distintos: el punto se dibuja leyendo la pantalla en
    /// vivo, y el mapa solo anota cuando alguna herramienta se lo pide. De ahí el punto GRIS —visible
    /// pero no registrado— y de ahí que fijarle el nivel respondiera «no lo veo en la pantalla»
    /// teniéndolo delante. Ver y recordar no son lo mismo, pero para el usuario tienen que serlo.
    /// </remarks>
    /// <summary>Procesos que dibujan páginas web. Una superficie web:// es legítima si delante hay
    /// uno de estos: su app es el dominio, no el proceso. La lista vive en un solo sitio
    /// (<see cref="Uia.PestanasAbiertas.EsNavegador"/>): había tres copias y a «vivaldi» solo lo
    /// conocía una, así que la misma ventana era web para el localizador y no para el mapa.</summary>
    private static bool EsNavegador(string proc) => Uia.PestanasAbiertas.EsNavegador(proc);


    private string OpenApp(string app, string instancia)
    {
        if (app.Length == 0) return "falta `app`: qué abrir (por ejemplo «explorer» o «notepad»)";
        // ABRIR LO DECIDE EL MAPEADOR y lo ejecuta el núcleo (AbrirSegunElNucleo): programa,
        // pestaña del navegador o SAP — sin adivinar jamás qué lanzar. Y con conciencia de lo que
        // ya hay abierto (promesa 232): «instancia» lo decide el modelo.
        return AbrirPorElNucleo != null ? AbrirPorElNucleo(app, instancia) : "todavía no sé abrir: el núcleo no está conectado.";
    }

    /// <summary>
    /// ¿Lo que hay delante NO es una ventana de la app —el escritorio, o algo sin identidad—?
    ///
    /// Qué es el escritorio lo sabe <see cref="Escritorio"/>, para toda la app. Aquí se añade el
    /// caso propio de abrir una app: una superficie vacía o «/ventana» —una ventana sin título que
    /// no identifica nada— cuenta igual, porque la decisión que se toma con esto es la misma: abrir
    /// una ventana de verdad.
    /// </summary>

    /// <summary>¿La app de delante tiene el botón «Subir un nivel»? Solo el explorador lo tiene.</summary>

    /// <summary>
    /// ¿Esta acción puede DESTAPAR elementos nuevos (un menú, un desplegable, un diálogo)?
    /// Solo entonces vale la pena releer la pantalla: lo demás no cambia las puertas y releer
    /// cuesta un recorrido completo del árbol de UI.
    /// </summary>
    /// <summary>
    /// Registra SOLO los elementos de menú visibles. Consulta dirigida, no una relectura completa.
    ///
    /// Tras pulsar «Nuevo» lo único que interesa es lo que acaba de aparecer —«Carpeta», «Acceso
    /// directo»…—, no las 300 puertas que la pantalla ya tenía. Releerlo todo costaba ~7 s por
    /// acción de menú; pedirle a UIA los MenuItem de una vez lo resuelve en una fracción.
    /// </summary>
    /// <summary>
    /// Espera a que HAYA menú abierto, y vuelve en cuanto lo hay. Devuelve false si no apareció.
    ///
    /// Es la diferencia entre esperar el efecto y esperar el reloj: un menú se abre en unos
    /// 200 ms, así que un plazo fijo de 800 ms desperdiciaba medio segundo cada vez y aun así se
    /// quedaba corto en un equipo cargado. Preguntar por lo que se espera sirve para las dos cosas.
    /// </summary>
    /// <summary>Cuántos elementos de menú hay ahora en la ventana de delante.</summary>

    /// <summary>
    /// ¿Es de los que al pulsarlos despliegan un menú? Se espera el menú y, si no sale, no se sigue.
    /// </summary>
    /// <remarks>
    /// Se miraba por TROZO de texto, y un trozo no distingue una cosa de otra: «Ver» casa con
    /// «Volver», y «Más» casa igual con «Más opciones» —que sí abre menú— que con el «Más» de
    /// sumar. Pidiéndole una suma a la Calculadora, el sistema pulsó «Más» dos veces esperando un
    /// menú que nunca iba a salir y detuvo la operación a medias (2026-08-05).
    ///
    /// Ahora se compara por PALABRA COMPLETA, y las palabras que solas no significan menú —«más»,
    /// «ver»— solo cuentan cuando acompañan a algo: «Más opciones», «Ver más». Una etiqueta de una
    /// sola palabra ambigua es un botón normal, que es lo que casi siempre es.
    /// </remarks>
    /// <summary>Acciones que operan sobre lo seleccionado: antes de ejecutarlas hay que saber qué es.</summary>
    /// <summary>
    /// ¿Estoy donde quien me pide la acción cree que estoy? Devuelve "" si sí (o si no lo dijo),
    /// y el motivo del desacuerdo si no.
    ///
    /// Es el ancla de la ejecución. Sin ella, un paso que falla deja el recorrido en otra pantalla
    /// y los siguientes se ejecutan perfectamente… en el sitio equivocado: así se pegaron archivos
    /// dentro de su propia carpeta de origen y se crearon carpetas anidadas (2026-08-02). Se
    /// recupera el foco primero, porque «no estoy donde creía» y «algo me tapó» son cosas distintas
    /// y solo la segunda tiene arreglo automático.
    /// </summary>
    /// <summary>
    /// Cuántas letras hay que cambiar para pasar de una a otra. Se corta pronto: solo interesa
    /// saber si es «lo mismo mal escrito», y para eso no hace falta medir distancias grandes.
    /// </summary>

    /// <summary>
    /// ¿Lo que hay delante es una CAPA sobre la pantalla esperada —un menú, un desplegable— y no
    /// otro sitio? Se exige que sea de la MISMA app: una ventana emergente de otro programa sí es
    /// irse a otra parte, y ahí el ancla debe seguir negándose.
    /// </summary>
    /// <summary>El sistema y el localizador dicen los dos que estamos en esta app.</summary>
    /// <summary>
    /// ¿Está delante la app que pido? Se exigen LAS DOS FUENTES —el proceso en primer plano y la
    /// superficie que ve el localizador— porque conformarse con la primera dejaba calcular rutas
    /// desde un sitio donde ya no estábamos: tras cerrarse un panel del shell el foco ya era
    /// correcto pero el localizador seguía diciendo «SearchHost» (2026-08-02).
    /// </summary>
    /// <remarks>
    /// UN NAVEGADOR MOSTRANDO UNA PÁGINA SE LLAMA COMO LA PÁGINA, y eso rompía la segunda fuente
    /// para siempre: con YouTube delante la superficie es «web://youtube.com», cuya app es
    /// «youtube.com» y nunca «chrome». Así que `map_open_app chrome` no podía tener éxito jamás
    /// estando Chrome delante — esperaba tres segundos, relanzaba Chrome, volvía a esperar, y a los
    /// 16,7 s contestaba «no pude traer chrome al frente; ahora hay chrome» (2026-08-16, en el log
    /// del usuario). Y mientras tanto peleaba por el foco con las demás llamadas, que es lo que hizo
    /// aparecer el explorador en medio de la tarea.
    ///
    /// La garantía no se afloja: para una app nativa siguen exigiéndose las dos. Lo que se añade es
    /// que una superficie web EN el navegador pedido cuenta como estar en ese navegador, que es lo
    /// que cualquiera diría mirando la pantalla.
    /// </remarks>

    private static string AppEnFrente()
    {
        try
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
            using var p = System.Diagnostics.Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch { return ""; }
    }

    /// <summary>
    /// LA BOCA DE LAS HERRAMIENTAS DEL TERRENO. Desde la gran limpieza (2026-08-30) todo lo que
    /// navega, pulsa, escribe o mira va por el NÚCLEO —los delegados—; el mapa por niveles que
    /// vivía aquí murió (su foto vive en la rama experimentos-viejos). Queda además del despacho:
    /// señalar/iluminar (UIA puro), los recuerdos, los diálogos y el disco.
    /// </summary>
    public SurfaceMapTools(Func<SurfaceLocator.SurfaceLocation?> where)
    {
        _where = where;
    }

    /// <summary>
    /// Dónde estamos, sin leer la pantalla: solo la identidad. Es lo que necesita la ficha de una mirada
    /// para saber DÓNDE se tomó (promesa 255), y cuesta lo que cueste situarse —que desde la spec 025 se
    /// recuerda 400 ms—, no lo que cuesta enumerar todo lo que hay delante.
    /// </summary>
    public string DondeEstoyAhora { get { try { return _where()?.Id ?? ""; } catch { return ""; } } }

    /// <summary>
    /// EL NÚCLEO NUEVO, para las superficies que este mapa no sabe alcanzar. Si nadie lo conecta,
    /// todo sigue exactamente como estaba.
    /// </summary>
    /// <remarks>
    /// «Ir a una superficie» estaba resuelto en tres sitios y los tres contestaban distinto: aquí
    /// por PROCESO —así que fallaba en cualquier web y en SAP—, en el panel de niveles por pestaña
    /// pero solo si ya estaba abierta, y en el núcleo nuevo entero. El modelo de voz usaba este, o
    /// sea el peor: no podía llegar a una página aunque el panel de al lado sí supiera (2026-08-16,
    /// lo preguntó el usuario).
    ///
    /// SE REPARTE POR TIPO DE SUPERFICIE Y NO POR «A VER SI SUENA». La navegación del explorador de
    /// archivos por voz es rápida y funciona, y no se toca: `uia://` sigue entero por aquí. Lo que
    /// pasa al núcleo nuevo es solo lo que aquí nunca funcionó. Una escalera —probar uno y si falla
    /// el otro— sería peor que cualquiera de los dos: impide saber cuál hizo el trabajo, que es
    /// justo lo que el propio interruptor SOLO GRAFO existe para poder medir.
    /// </remarks>
    public Func<string, string>? PorElNucleo { get; set; }

    /// <summary>
    /// DE DÓNDE SALE LO QUE HAY DELANTE cuando un acto lo cuenta (promesa 263). Nulo en la app, que es leer la
    /// pantalla como hace map_what_i_see; el contrato lo cambia por un inventario fijo para juzgar el despacho
    /// sin tocar UIA.
    /// </summary>
    public Func<string>? InventarioParaLosActos { get; set; }

    /// <summary>
    /// DE DÓNDE SALEN LAS PUERTAS DE AHORA (promesa 285). Nulo en la app: UIA + terreno + dynpro, como
    /// siempre. El contrato lo cambia por una lista fija para juzgar que map_decidir y map_what_i_see
    /// ven lo mismo, sin tocar la pantalla.
    /// </summary>
    public Func<string, IReadOnlyList<(string Selector, string Etiqueta, string Tipo)>>? Puertas { get; set; }

    /// <summary>
    /// QUIÉN ELIGE LA PUERTA cuando el cerebro pide <c>map_decidir</c> (spec 035): pantalla, objetivo y
    /// las etiquetas de las puertas de ahora → una decisión. NULO = decide Luna, y la herramienta no
    /// existe para ella. Lo enchufa la ventana según <see cref="Decision.ConfiguracionDelDecisor"/>.
    /// </summary>
    public Func<string, string, IReadOnlyList<string>, Decision.DecisionDeUnPaso>? Decisor { get; set; }

    // ── El tramo (spec 037) ───────────────────────────────────────────────────────────────────────

    /// <summary>¿Hay que parar? Por defecto, el freno de Escape. El contrato lo cambia por el suyo.</summary>
    public Func<bool>? HayQueParar { get; set; }

    /// <summary>Cada paso del tramo, en una línea: para el notch. Nulo = solo al log.</summary>
    public Action<string>? Progreso { get; set; }

    /// <summary>La cuenta del tramo al parar, como mensaje a la sesión de voz (295). Nulo = sin voz.</summary>
    public Action<string>? AvisarALaVoz { get; set; }

    /// <summary>Pedir el freno de verdad (map_alto). Por defecto, el de Escape; el contrato lo cambia por un no-op.</summary>
    public Action<string>? PedirFreno { get; set; }

    /// <summary>Marcar que empieza y termina una tarea para el freno (Freno.Empezar/Termine). Nulos = nada.</summary>
    public Action<string>? AlEmpezarTramo { get; set; }
    public Action? AlTerminarTramo { get; set; }

    private Navigation.ElTramo? _tramo;

    /// <summary>Para los jueces: espera a que el tramo en marcha termine.</summary>
    public bool EsperarTramo(int ms) => _tramo?.Esperar(ms) ?? true;

    private Navigation.ElTramo ElTramo() => _tramo ??= new Navigation.ElTramo(new Navigation.ElTramo.Manos(
        Donde: () => { try { return _where()?.Id ?? ""; } catch { return ""; } },
        Paso: objetivo => UnPasoDecidido(objetivo, "", ""),
        HayQueParar: () => HayQueParar?.Invoke() ?? Actions.Freno.Pidieron,
        Progreso: l => Progreso?.Invoke(l),
        Inventario: () => InventarioParaLosActos?.Invoke() ?? LoQueVeo(),
        AvisarALaVoz: AvisarALaVoz == null ? null : (Action<string>)(c => AvisarALaVoz?.Invoke(c)),
        Log: l => LogBus.Log("tramo", l),
        AlEmpezar: t => AlEmpezarTramo?.Invoke(t),
        AlTerminar: () => AlTerminarTramo?.Invoke()));

    /// <summary>«map_tramo»: contesta al instante y el bucle corre por detrás (291).</summary>
    private string Tramo(string objetivo, string tope, string decir)
    {
        if (objetivo.Length == 0) return "falta `objetivo`: qué se quiere conseguir, para que el tramo sepa hacia dónde ir";
        if (Decisor == null)
            return "todavía no sé recorrer un tramo: el decisor está apagado (U_DECISOR ausente o en «luna», o el botón Jev apagado). "
                 + "Avanza tú paso a paso con map_take.";
        int.TryParse(tope, out int n);
        string r = ElTramo().Arrancar(objetivo, n);
        LogBus.Log("tramo", $"→ {r}");
        return r;
    }

    /// <summary>«map_alto»: para el tramo en el paso en curso, y pone el mismo freno que Escape (293).</summary>
    private string Alto()
    {
        var t = _tramo;
        if (t == null || !t.EnMarcha) return "no hay ningún tramo en marcha que parar.";
        string r = t.Parar("lo pidió la voz (map_alto)");
        try { PedirFreno?.Invoke("lo pidió la voz (map_alto)"); } catch (Exception e) { LogBus.Log("tramo", $"no pude pedir el freno: {e.Message}"); }
        LogBus.Log("tramo", $"ALTO: {r}");
        return r;
    }

    /// <summary>«map_tramo_estado»: la cuenta, en marcha o terminada (294).</summary>
    private string EstadoDelTramo() => _tramo?.Estado ?? "no hay ningún tramo en marcha ni terminado.";

    /// <summary>
    /// SITUARSE, contestado por el núcleo. Lo enchufa la ventana cuando el mapa vivo existe; si
    /// vale null se contesta como siempre. Ver <see cref="Navigation.AquiSegunElNucleo"/>.
    /// </summary>
    public Func<string>? Situarse { get; set; }

    /// <summary>
    /// LA VENTANA EN LA QUE Ü TRABAJA (spec 020, promesa 233), para juzgar las interrupciones ahí y
    /// no en la que la persona mira. Sin cableado, o cero, se mira la de delante como siempre.
    /// </summary>
    public Func<IntPtr>? VentanaDeTrabajo { get; set; }

    /// <summary>
    /// QUIÉN LEE EL DIÁLOGO que se cruzó, con sus botones (promesa 236). Inyectable para juzgarlo sin
    /// pantalla; sin cableado, lo lee <see cref="Interrupcion.LeerDialogo"/> en la ventana de trabajo.
    /// </summary>
    public Func<Navigation.Desbloqueo.Dialogo?>? LeerDialogo { get; set; }

    private Navigation.Desbloqueo.Dialogo? DialogoDelante()
        => LeerDialogo != null ? LeerDialogo() : Interrupcion.LeerDialogo(VentanaDeTrabajo?.Invoke() ?? IntPtr.Zero);

    /// <summary>
    /// SEÑALAR, contestado por el núcleo. Recibe lo que UIA ya resolvió bajo el cursor y devuelve la
    /// frase. Ver <see cref="Navigation.LoQueSenalas"/>.
    /// </summary>
    public Func<Navigation.LoQueSenalas.Senalado, string>? Senalar { get; set; }

    /// <summary>
    /// ABRIR, contestado por el núcleo. La vía —programa, pestaña o SAP— la decide el
    /// mapeador; traer al frente de verdad sigue siendo Win32 y se queda aquí.
    /// Ver <see cref="Navigation.AbrirSegunElNucleo"/>.
    /// </summary>
    public Func<string, string, string>? AbrirPorElNucleo { get; set; }

    /// <summary>
    /// PULSAR, contestado por el núcleo: se toca, se comprueba qué pasó y el grafo lo aprende.
    /// Resolver el selector y escalar al doble clic siguen siendo de UIA.
    /// Ver <see cref="Navigation.PulsarSegunElNucleo"/>.
    /// </summary>
    public Func<string, string, string>? PulsarPorElNucleo { get; set; }

    /// <summary>
    /// RECORRER EN BATCH: N pasos por llamada con la compuerta de vida antes de cada uno.
    /// Ver <see cref="Navigation.RecorrerSegunElNucleo"/> y docs/plan-batch-sobre-nodos-vivos.md.
    /// </summary>
    /// <remarks>
    /// DEVUELVE EL RESULTADO ENTERO, no solo su prosa, y ese cambio es del 2026-09-03: quien
    /// comprueba una skill necesita SABER cuántos pasos se anduvieron para decidir si el camino
    /// quedó certificado (promesa 131). Sacar ese número de la frase sería parsear prosa, y la
    /// frase existe para una persona, no para un `if`.
    /// </remarks>
    public Func<IReadOnlyList<Navigation.RecorrerSegunElNucleo.Paso>,
        Navigation.RecorrerSegunElNucleo.Resultado>? RecorrerPorElNucleo { get; set; }

    /// <summary>
    /// DAR UN PASO CON SU COREOGRAFÍA (promesa 191): señalar, decir, escribir el recuerdo, mostrar la
    /// tarjeta, actuar, cerrar. (qué señalar, el paso para el ejecutor, el recuerdo, qué decir). Es la
    /// misma función que recorre el plan; las manos del piloto pasan por aquí para que la
    /// experiencia sea una sola.
    /// </summary>
    public Func<string, Navigation.RecorrerSegunElNucleo.Paso, string, string,
        Navigation.RecorrerSegunElNucleo.Resultado>? DarUnPasoConCoreografia { get; set; }

    /// <summary>El terreno por delante (puerta, niveles) → la cuenta. T3: la consulta de la profundidad.</summary>
    public Func<string, string, string>? TerrenoPorElNucleo { get; set; }

    /// <summary>
    /// ENSEÑAR: «esto es X». (ubicación, selector, significado, ruta de la foto) → si se guardó.
    ///
    /// Va al grafo, no a un archivo al lado. El significado es del elemento igual que su etiqueta;
    /// guardarlo aparte con las mismas claves eran dos sitios que sabían de lo mismo, y dos sitios
    /// se desincronizan sin avisar. Además así «llévame a donde se radican las facturas» es UNA
    /// consulta: se busca por significado y desde ese elemento ya se sabe el camino (2026-08-23).
    /// </summary>
    public Func<string, string, string, string, bool>? Ensenar { get; set; }

    /// <summary>
    /// «ESTO EXISTE AQUÍ»: (ubicación, selector, etiqueta, tipo). Le cuenta al grafo un elemento que
    /// tenemos delante y que su lista de puertas no tiene —un panel que se desplaza, un contenedor—
    /// para poder colgarle un recuerdo.
    ///
    /// Entra como MEMORIA y no como vivo: que lo veamos bajo el cursor no lo convierte en algo que
    /// se pueda pulsar, y decir lo contrario mandaría al asistente a accionar un panel.
    /// </summary>
    public Action<string, string, string, string>? Presentar { get; set; }

    // ── Lo que el piloto de la spec 013 necesita de la ventana ──────────────────────────
    /// <summary>Decir algo con la voz de Ü. Devuelve qué pasó («dicho» o el motivo).</summary>
    public Func<string, string>? Decir { get; set; }
    /// <summary>Preguntarle a la persona y ESPERAR su respuesta hablada (con techo). Devuelve lo que dijo.</summary>
    public Func<string, string>? Preguntar { get; set; }
    /// <summary>El piloto declara que hizo el evento N de la lección; la app juzga y contesta.</summary>
    public Func<int, string>? Llegue { get; set; }
    /// <summary>Empaquetar la skill con lo verificado hasta ahora.</summary>
    public Func<string, string, string>? GuardarSkill { get; set; }
    /// <summary>Recorrer el plan del piloto: por cada paso, voz, recuerdo, el paso por el batch y el juez.</summary>
    public Func<string, string>? Plan { get; set; }

    /// <summary>
    /// Quién decide si ya se puede pasar al siguiente recuerdo. Lo alimenta la voz —es la única que
    /// sabe si Ü habló— y lo consulta <see cref="Recuerdos"/>. Ver <see cref="Navigation.ElTurnoDeContar"/>.
    /// </summary>
    public Navigation.ElTurnoDeContar TurnoDeContar { get; } = new();

    /// <summary>
    /// Cuál toca contar después, o 0 si no queda ninguno. Lo lee la voz para RETOMAR sola.
    /// </summary>
    /// <remarks>
    /// Hace falta porque hablar CIERRA el turno: se cuenta el primero, se dice en voz, el turno
    /// termina — y ahí ya no hay nada que despierte al modelo para pedir el segundo. Se probó el
    /// 2026-08-24: contó el 1 perfectamente y se quedó ahí, con el otro recuerdo sin contar y sin
    /// que nadie se enterara de que faltaba. La regla de uno-en-uno impedía atropellarlos; esto es
    /// lo que hace que además LLEGUEN todos.
    /// </remarks>
    public int SiguienteRecuerdoPendiente { get; private set; }

    /// <summary>
    /// CORREGIR A MANO un recuerdo de esta pantalla, desde su tarjeta. Devuelve si se guardó.
    /// </summary>
    /// <remarks>
    /// Es la misma puerta que usa la voz —<c>Ensenar</c>— y por eso pasa por las mismas reglas: si
    /// el elemento ya no se conoce aquí, se rechaza igual. Escribir a mano no es un atajo para
    /// meter en el grafo algo que la voz no habría podido meter.
    ///
    /// LA FOTO NO SE TOCA: la de un recuerdo es la de cuando se enseñó, y corregir la frase no
    /// cambia lo que había en pantalla aquel día.
    /// </remarks>
    public bool CorregirRecuerdo(string selector, string significado)
    {
        string donde = _where()?.Id ?? "";
        if (donde.Length == 0 || Ensenar == null) return false;

        var previo = RecuerdosAqui?.Invoke(donde)
            .FirstOrDefault(r => r.Selector.Equals(selector, StringComparison.OrdinalIgnoreCase));
        string foto = "";   // se conserva sola: Ensenar no la borra si no llega una nueva

        bool ok = Ensenar(donde, selector, significado, foto);
        LogBus.Log("recuerdo", ok
            ? $"corregido a mano «{previo?.Etiqueta ?? selector}» en «{donde}» → {significado}"
            : $"NO pude corregir «{selector}» en «{donde}»: el grafo no lo tiene aquí");
        return ok;
    }

    /// <summary>Lo enseñado en una pantalla: (etiqueta, significado). Para contarlo al llegar.</summary>
    /// <remarks>
    /// LLEVA EL SELECTOR, no solo la etiqueta: es lo único con lo que se puede volver a encontrar el
    /// elemento EN PANTALLA para iluminarlo. Con dos cosas llamadas igual —que es justo el caso que
    /// obliga a tener selectores— la etiqueta señalaría las dos.
    /// </remarks>
    public Func<string, IReadOnlyList<(string Selector, string Etiqueta, string Significado)>>? RecuerdosAqui { get; set; }

    /// <summary>
    /// Lo que el TERRENO ve vivo en una ubicación: (selector, etiqueta, tipo). Es lo que permite
    /// enseñar dentro de SAP, donde el lector de UIA no ve nada (promesa 117).
    /// </summary>
    public Func<string, IReadOnlyList<(string Selector, string Etiqueta, string Tipo)>>? PuertasVivas { get; set; }

    /// <summary>
    /// LOS CAMPOS DEL DYNPRO DE AHORA, leídos por SAP (etiqueta, selector, tipo). Vacío fuera de SAP.
    /// Es lo que hace que un campo se pueda NOMBRAR para colgarle un recuerdo, señalarlo o verlo en la
    /// lista, con el mismo criterio que para escribir en él (promesa 188).
    /// </summary>
    public Func<IReadOnlyList<DetectedField>>? CamposDeSap { get; set; }

    /// <summary>
    /// LO QUE LA PERSONA NOMBRA DENTRO DE SAP, resuelto en UN solo sitio (promesa 188): primero las
    /// puertas del terreno —tienen historia y tipo—, después los campos del dynpro por su etiqueta o
    /// su nombre técnico, con el mismo resolutor que usa la mano para escribir
    /// (<see cref="SapGuiSurface.ElCampoQueSeLlama"/>). Con dos iguales, nadie. Sin nada, null.
    /// </summary>
    /// <remarks>
    /// POR QUÉ (2026-09-08, novena prueba): el piloto pidió un recuerdo en los 14 campos del triage y
    /// la app rechazó 11 con «no veo nada que se llame Presión Arterial»: colgar un recuerdo y señalar
    /// buscaban entre lo de UIA y las puertas del terreno, y un campo del dynpro no está en ninguna de
    /// las dos. Escribir SÍ lo encontraba. Dos criterios para nombrar la misma cosa son un bug
    /// esperando su turno; este es el único desde hoy.
    /// </remarks>
    public static (string Selector, string Etiqueta, string Tipo)? LoQueSeNombra(string nombre,
        IReadOnlyList<(string Selector, string Etiqueta, string Tipo)>? delTerreno,
        IReadOnlyList<DetectedField>? camposDeSap)
    {
        if (string.IsNullOrWhiteSpace(nombre)) return null;
        if (delTerreno != null && delTerreno.Count > 0)
        {
            // Por su selector exacto también: el plan a veces trae el selector en vez del nombre.
            foreach (var p in delTerreno)
                if (p.Selector.Length > 0 && p.Selector.Equals(nombre.Trim(), StringComparison.Ordinal)) return p;
            if (Navigation.ElCampoQueNombras.Resolver(nombre, delTerreno) is { } enElTerreno) return enElTerreno;
        }
        var campo = SapGuiSurface.ElCampoQueSeLlama(camposDeSap ?? Array.Empty<DetectedField>(), nombre);
        return campo == null ? null : (campo.Selector, campo.Label, campo.ControlType);
    }

    /// <summary>
    /// LAS PUERTAS DEL TERRENO CON LA ETIQUETA QUE SE LEE. El terreno conoce un campo del dynpro por su
    /// nombre técnico («Y0000000-ZTXTTASIS»); el dynpro lo conoce por lo que la persona lee («Presión
    /// Arterial»). Mismo selector, mismo campo: se queda la puerta del terreno —con su tipo y su
    /// historia— y la etiqueta que se lee. Los campos que el terreno no conoce se añaden.
    /// </summary>
    /// <remarks>
    /// MEDIDO (2026-09-08): con la fusión por selector «a secas», map_what_i_see listaba 24 campos del
    /// triage por su nombre técnico y ninguno por su etiqueta, y el piloto planea con las etiquetas
    /// que la lección trae. Nombrar y listar tienen que hablar el mismo idioma.
    /// </remarks>
    public static IReadOnlyList<(string Selector, string Etiqueta, string Tipo)> ConLaEtiquetaQueSeLee(
        IReadOnlyList<(string Selector, string Etiqueta, string Tipo)>? delTerreno,
        IReadOnlyList<(string Selector, string Etiqueta, string Tipo)>? camposDeSap)
    {
        var terreno = delTerreno ?? Array.Empty<(string, string, string)>();
        var campos = camposDeSap ?? Array.Empty<(string, string, string)>();
        var etiquetaPorSelector = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in campos)
            if (c.Etiqueta.Length > 0) etiquetaPorSelector[U.Graph.Surfaces.SapSelector.Normalize(c.Selector)] = c.Etiqueta;

        var salida = new List<(string, string, string)>();
        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in terreno)
        {
            string clave = U.Graph.Surfaces.SapSelector.Normalize(p.Selector);
            vistos.Add(clave);
            salida.Add(etiquetaPorSelector.TryGetValue(clave, out var leida) ? (p.Selector, leida, p.Tipo) : p);
        }
        foreach (var c in campos)
            if (!vistos.Contains(U.Graph.Surfaces.SapSelector.Normalize(c.Selector))) salida.Add(c);
        return salida;
    }

    /// <summary>Los campos del dynpro como puertas, para fundirlos con lo del terreno.</summary>
    private IReadOnlyList<(string Selector, string Etiqueta, string Tipo)> CamposDeSapComoPuertas()
    {
        IReadOnlyList<DetectedField> campos;
        try { campos = CamposDeSap?.Invoke() ?? Array.Empty<DetectedField>(); } catch { return Array.Empty<(string, string, string)>(); }
        return campos.Where(c => c.Label.Length > 0).Select(c => (c.Selector, c.Label, c.ControlType)).ToList();
    }

    /// <summary>
    /// DÓNDE ESTÁ TODO LO DE SAP en pantalla, de una sola lectura. Es la mitad SAP de
    /// <see cref="Navigation.GeometriaPorMundo"/> (promesa 119).
    /// </summary>
    /// <remarks>
    /// DEVUELVE LA PANTALLA ENTERA Y NO UN ELEMENTO, y eso es el arreglo de un cuelgue real: con una
    /// función por selector, seis recuerdos eran seis recorridos COM completos del dynpro, en el
    /// hilo de la interfaz — y como la vista se refresca en CADA cambio de pantalla, se apilaban
    /// varias veces por segundo hasta dejar la app clavada (2026-09-02, lo vio el dueño sobre el
    /// triage). Una lectura por refresco, y el emparejado se hace en memoria.
    /// </remarks>
    public Func<IReadOnlyList<(string Selector, string Etiqueta, string Tipo, System.Windows.Rect Caja)>>? CajasEnSap { get; set; }

    /// <summary>
    /// QUÉ HAY bajo un punto de la pantalla, según SAP. Es la mitad SAP de
    /// <see cref="Navigation.LoSenaladoPorMundo"/> (promesa 118).
    /// </summary>
    public Func<int, int, (string Selector, string Etiqueta, string Tipo, System.Windows.Rect Caja)?>? SenaladoEnSap { get; set; }

    /// <summary>La caja de un elemento de UIA por su selector, leyendo la pantalla ya cargada.</summary>
    private System.Windows.Rect? CajaPorUia(string selector)
    {
        var visto = _lector.Elements.FirstOrDefault(
            e => Uia.Reconocedor.SelectorDe(e).Equals(selector, StringComparison.OrdinalIgnoreCase));
        return visto?.Bounds;
    }

    public static bool IsMapTool(string tool) => tool is
        "map_where_am_i" or "map_go_to" or "map_take" or "map_type" or "map_unblock" or "map_decidir"
        or "map_tramo" or "map_alto" or "map_tramo_estado"
        or "map_open_app" or "map_what_i_see" or "map_pointing_at" or "map_show"
        or "map_pointed_trail" or "map_exclude" or "map_shot" or "map_scroll"
        or "map_esto_es" or "map_recuerdos" or "map_batch" or "map_ahead"
        or "map_skills" or "map_skill_run"
        or "file_where" or "file_list" or "file_open" or "file_find"
        // LAS DEL PILOTO (spec 013): hablar con la voz de Ü, preguntarle a la persona, declarar
        // una llegada para que la app la juzgue, y guardar la skill de lo verificado.
        or "voz_decir" or "voz_preguntar" or "leccion_llegue" or "leccion_guardar_skill" or "leccion_plan";

    public string Call(string tool, IReadOnlyDictionary<string, string> args)
    {
        string A(string k) => args.TryGetValue(k, out var v) ? v.Trim() : "";
        _ultimaMano = null;   // cada llamada dice SU resultado, no el de la anterior (spec 017)

        // Se registra CADA llamada y su respuesta. Sin esto, «el mapa no aportó nada» y «el modelo
        // ni lo intentó» se ven exactamente igual en el log — y esa ambigüedad me llevó a un
        // diagnóstico equivocado el 2026-07-31, buscando en el mapa un fallo que estaba en el
        // lanzador de apps.
        string args_ = string.Join(" ", args.Select(kv => $"{kv.Key}={kv.Value}"));
        LogBus.Log("mapa-mcp", $"→ {tool} {args_}".TrimEnd());
        var reloj = System.Diagnostics.Stopwatch.StartNew();

        // Si se pasa a hacer otra cosa, ya no se está mirando lo de antes: se suelta. Señalar es un
        // gesto que acompaña a una frase, no un estado en el que quedarse.
        if (!tool.Equals("map_show", StringComparison.OrdinalIgnoreCase)
            && !tool.Equals("map_pointing_at", StringComparison.OrdinalIgnoreCase)
            && !tool.Equals("map_pointed_trail", StringComparison.OrdinalIgnoreCase)
            // Excluir RETOCA lo marcado; si se soltara antes, se quedaría sin nada que retocar.
            && !tool.Equals("map_exclude", StringComparison.OrdinalIgnoreCase))
            Ui.Senalador.Soltar();

        string r = tool switch
        {
            "map_where_am_i" => WhereAmI(),
            "map_go_to" => GoTo(A("surface")),
            "map_take" => Take(A("exit"), A("which"), A("decir"), A("recuerdo")),
            "map_decidir" => Decidir(A("objetivo"), A("decir"), A("recuerdo")),
            "map_tramo" => Tramo(A("objetivo"), A("tope"), A("decir")),
            "map_alto" => Alto(),
            "map_tramo_estado" => EstadoDelTramo(),
            "map_type" => Type(A("text"), A("target"), A("decir"), A("recuerdo")),
            "map_unblock" => Desbloquear(A("at"), A("choose")),
            "map_open_app" => OpenApp(A("app"), A("instancia")),
            "map_what_i_see" => LoQueVeo(),
            "map_pointing_at" => LoQueSenala(),
            "map_pointed_trail" => LoQueMeAcabasDeMostrar(A("seconds")),
            "map_exclude" => Excluir(A("exit")),
            "map_show" => Mostrar(A("exit"), int.TryParse(A("which"), out int cual) ? cual : 0),
            "map_shot" => Foto(),
            "map_skills" => Skills(),
            "map_skill_run" => CorrerSkill(A("nombre"), A("datos")),
            // DESPLAZAR ES ACCIONAR, no mirar: va con el resto de manos. Faltaba entero — el modelo
            // contestaba «no puedo scrolear directamente» porque era verdad (2026-08-16).
            "map_esto_es" => EstoEs(A("significado"), A("sobre")),
            "map_recuerdos" => Recuerdos(A("cual")),
            "map_batch" => Batch(A("pasos")),
            "map_ahead" => TerrenoPorElNucleo == null
                ? "todavía no sé mirar el terreno por delante."
                : TerrenoPorElNucleo(A("exit"), A("levels")),
            "map_scroll" => Uia.Desplazamiento.Mover(Uia.Desplazamiento.Leer(A("direction"))),

            // Los verbos del explorador. Van por disco, no por pantalla: ver Explorador.cs.
            "file_where" => DondeEnDisco(),
            "file_list" => SystemApi.Explorador.Describir(SystemApi.Explorador.Expandir(A("path")), A("filter")),
            "file_open" => AbrirCarpeta(A("path")),
            "file_find" => BuscarEnDisco(A("query"), A("path")),

            // EL PILOTO HABLA Y PREGUNTA CON LA VOZ DE Ü, y declara llegadas que juzga la APP (spec
            // 012, promesas 174 y 175). Son delegados porque quien tiene la voz y el juez es la
            // ventana; aquí solo se despacha.
            "voz_decir" => Decir == null ? "todavía no tengo voz con la que decirlo." : Decir(A("texto")),
            "voz_preguntar" => Preguntar == null ? "todavía no tengo cómo preguntarle a la persona." : Preguntar(A("texto")),
            "leccion_llegue" => Llegue == null ? "no hay ninguna comprobación en curso que juzgar."
                : Llegue(int.TryParse(A("n"), out int nEvento) ? nEvento : -1),
            "leccion_guardar_skill" => GuardarSkill == null ? "no hay ninguna comprobación en curso de la que sacar una skill."
                : GuardarSkill(A("nombre"), A("descripcion")),
            // EL PLAN (promesa 179): el piloto entrega lo que entendió en el idioma del ejecutor y la
            // app lo recorre —voz, recuerdo, paso por el batch, juez— parando donde no pueda.
            "leccion_plan" => Plan == null ? "no hay ninguna comprobación en curso que planear." : Plan(A("pasos")),

            _ => $"herramienta de mapa no soportada: {tool}",
        };

        // CADA HERRAMIENTA CON SU RELOJ, Y AQUÍ PORQUE AQUÍ PASAN TODOS. El tiempo ya se medía en la
        // voz —el «✓ … (817 ms)» del panel— pero solo ahí: la sonda de desarrollo y el bucle del
        // agente llaman directo a este método, así que sus llamadas no se cronometraban y una
        // medición hecha con la sonda salía vacía (2026-08-16, me pasó al intentar comprobarlo).
        //
        // Va al MISMO pulso donde ya viven «localizar», «leer la pantalla» y «proyectar», con el
        // mismo trato —veces, media y LA PEOR—, porque compiten por los mismos milisegundos y dos
        // tablas de tiempos obligarían a mirar en dos sitios para compararlos.
        //
        // El prefijo agrupa sin mezclar: lo que cuesta atender una orden no es lo que el mapeador
        // hace por su cuenta, y confundirlos es la misma trampa que comparar Gmail con el explorador.
        // UN ACTO CUENTA LO QUE DEJÓ DELANTE (promesa 263, spec 029). Aquí, en el despacho, y no en cada
        // herramienta: había seis sitios y la clase de error se arregla una vez donde pasan todos. Lo que
        // compra: la mitad de los actos iban seguidos de un «¿y ahora qué hay?» que ya no hace falta. Va
        // ANTES de parar el reloj, para que el coste de leer la pantalla cuente como parte del acto.
        if (ComoSeContesta.LlevaInventario(tool, r))
        {
            try { r = ComoSeContesta.Pegar(r, InventarioParaLosActos?.Invoke() ?? LoQueVeo()); }
            catch (Exception e) { LogBus.Log("mapa-mcp", $"no pude añadir lo que hay delante: {e.Message}"); }
        }

        reloj.Stop();
        // DÓNDE QUEDAMOS, para la próxima. Lo que el modelo sabe de la pantalla es lo que esta
        // llamada le acaba de contar; comparar contra esto es comparar contra su último vistazo.
        Mapeador.PulsoDelMapeador.Actual.Costo("voz: " + tool, reloj.ElapsedMilliseconds);
        LogBus.Log("mapa-mcp", $"← ({reloj.ElapsedMilliseconds} ms) "
            + (r.Length > 200 ? r[..200] + "…" : r).Replace("\n", " | "));
        return r;
    }

    /// <summary>
    /// Dónde está el explorador, con la ruta REAL y no el título de la ventana.
    ///
    /// El título dice «Descargas»; la ruta dice «C:\Users\quien\Downloads». Cuando el usuario
    /// habla, la diferencia importa: hay una carpeta «Facturas» en tres sitios distintos y el
    /// título no las distingue.
    /// </summary>
    private static string DondeEnDisco()
    {
        string ruta = SystemApi.Explorador.RutaEnPrimerPlano();
        if (ruta.Length == 0)
            return "No hay ninguna carpeta del explorador en primer plano. "
                 + "Usa map_open_app con «explorer», o file_open con una ruta.";

        var todo = SystemApi.Explorador.Listar(ruta);
        int carpetas = todo.Count(e => e.EsCarpeta);
        return $"{ruta} · {carpetas} carpeta(s) y {todo.Count - carpetas} archivo(s) dentro. "
             + $"Carpeta padre: {System.IO.Path.GetDirectoryName(ruta) ?? "(ninguna, es una raíz)"}";
    }

    /// <summary>
    /// Navega a una carpeta en un salto. Es lo que sustituye a encadenar clics carpeta por carpeta.
    /// </summary>
    private static string AbrirCarpeta(string path)
    {
        if (path.Trim().Length == 0) return "Falta la ruta o el nombre de la carpeta.";

        string destino = SystemApi.Explorador.Expandir(path);
        string ido = SystemApi.Explorador.Navegar(destino);
        if (ido.Length == 0)
            return $"No existe la carpeta «{destino}»"
                 + (destino.Equals(path, StringComparison.OrdinalIgnoreCase) ? "." : $" (interpretado desde «{path}»).")
                 + " Mira con file_list qué hay donde estás antes de inventar el nombre.";

        // El explorador tarda un instante en repintar; sin esto, un file_list inmediatamente después
        // podría leerse antes de que la ventana muestre el destino. El disco ya es correcto — esto
        // es solo para que lo que se dice y lo que se ve coincidan.
        System.Threading.Thread.Sleep(150);
        return $"Abierta «{ido}».\n" + SystemApi.Explorador.Describir(ido);
    }

    /// <summary>
    /// Busca por nombre en el disco, sin tocar la caja de búsqueda del explorador.
    /// </summary>
    private static string BuscarEnDisco(string query, string path)
    {
        if (query.Trim().Length == 0) return "Falta qué buscar.";

        string raiz = path.Trim().Length > 0
            ? SystemApi.Explorador.Expandir(path)
            : SystemApi.Explorador.RutaEnPrimerPlano();
        if (raiz.Length == 0) return "No sé dónde buscar: no hay carpeta abierta y no me diste ruta.";

        var hallazgos = SystemApi.Explorador.Buscar(raiz, query.Trim());
        if (hallazgos.Count == 0) return $"Nada que contenga «{query}» dentro de {raiz}.";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{hallazgos.Count} resultado(s) para «{query}» en {raiz}:");
        foreach (string p in hallazgos)
            sb.AppendLine($"  {(System.IO.Directory.Exists(p) ? "[carpeta] " : "")}{p}");
        return sb.ToString().TrimEnd();
    }

    private string WhereAmI()
    {
        var loc = _where();
        if (loc == null) return "no se pudo determinar la superficie actual";

        // UN DIÁLOGO NO ES UN LUGAR: es una interrupción, y se comprueba antes que nada.
        string interrupcion = DescribirInterrupcion();
        if (interrupcion.Length > 0) return interrupcion;

        if (Situarse == null) return $"Estás en «{loc.Id}».";

        // SITUARSE LO CONTESTA EL NÚCLEO (AquiSegunElNucleo): separa lo que se ve AHORA de lo que
        // solo se recuerda. Lo enseñado aquí se AVISA sin recitarse — contarlo es de
        // map_recuerdos, que lo señala uno a uno (2026-08-24).
        string donde = Situarse();
        var sabidas = RecuerdosAqui?.Invoke(loc.Id)
                      ?? (IReadOnlyList<(string Selector, string Etiqueta, string Significado)>)
                         Array.Empty<(string, string, string)>();
        if (sabidas.Count > 0)
            donde += $" Y aquí me has enseñado {sabidas.Count} cosa(s): "
                   + string.Join(", ", sabidas.Select(e => $"«{e.Etiqueta}»"))
                   + ". Para contarlas usa map_recuerdos, que las señala una a una.";
        return donde;
    }

    /// <summary>
    /// EJECUCIÓN DE BAJA FRECUENCIA: un plan entero en UNA llamada.
    ///
    /// Es la otra mitad de la arquitectura de dos frecuencias. Quien decide —el modelo— emite el
    /// plan una vez; el ejecutor lo recorre verificando cada paso, sin volver a preguntar. La
    /// diferencia con ir paso a paso no es el trabajo, que es el mismo: es cuántas veces se
    /// consulta a quien decide. Veintiuna consultas para organizar unos archivos era el coste de
    /// no tener esta pieza, no del sistema (2026-08-03).
    ///
    /// Se PARA en el primer paso que no confirme lo esperado, y dice dónde. Seguir tras un fallo
    /// es exactamente cómo un error se convierte en daño tres pasos después: ya nos pasó, y el
    /// ancla de ubicación existe por eso. Aquí se aplica a la secuencia entera.
    ///
    /// Formato: [{"op":"go_to","surface":"..."},
    ///           {"op":"take","exit":"...","at":"...","action":"click|addselect|doubleclick"},
    ///           {"op":"type","text":"...","at":"..."}]
    /// </summary>
    private static string Recortar(string s, int max)
    {
        s = (s ?? "").Replace("\n", " · ");
        return s.Length > max ? s[..max] + "…" : s;
    }

    /// <summary>
    /// EL DESBLOQUEADOR. Detecta que la ejecución está atascada, sale del atasco y reanuda por el
    /// mapa, dejando constancia de lo ocurrido.
    ///
    /// Nace de una crítica acertada: estábamos arreglando los colapsos uno a uno —un diálogo, otro
    /// diálogo, otro más— en vez de tener un sistema que los resuelva. Los bloqueos comparten forma
    /// (algo se cruza, hay que responderle, y después hay que volver a donde íbamos), así que
    /// merecen un mecanismo, no un parche por cada caso.
    ///
    /// La política es DELIBERADAMENTE conservadora, y es lo importante de este diseño:
    ///   • una sola opción → es informativo, se acepta y ya está;
    ///   • hay una opción que NO compromete nada (Cancelar, No, Cerrar) → esa;
    ///   • cualquier otra cosa → NO se adivina. Se describe y decide la capa consciente.
    /// Elegir mal aquí es destructivo —el aviso de cambiar la extensión de un archivo tiene un «Sí»
    /// que lo corrompe—, así que ante la duda se prefiere no avanzar antes que avanzar mal.
    /// </summary>
    /// <param name="reanudarEn">A dónde volver una vez resuelto, para continuar la tarea.</param>
    /// <param name="eleccion">Opción impuesta por la capa consciente cuando la política no decide.</param>
    public string Desbloquear(string reanudarEn, string eleccion)
    {
        // EL DIÁLOGO CON SUS BOTONES (promesa 236): lo que se pulsa es el botón que se leyó, no un nombre.
        var dialogo = DialogoDelante();
        if (dialogo == null || dialogo.Opciones.Count == 0)
            return "no hay nada que desbloquear: no veo ningún diálogo delante.";
        string titulo = dialogo.Titulo;
        var textos = dialogo.Textos.ToList();
        var opciones = dialogo.Opciones.ToList();

        string elegida = eleccion.Length > 0
            ? opciones.FirstOrDefault(o => o.Equals(eleccion, StringComparison.OrdinalIgnoreCase)) ?? ""
            : OpcionSegura(opciones);

        if (elegida.Length == 0)
            return $"ATASCADO · decide tú, esto no lo automatizo.\n"
                 + $"  Diálogo: «{titulo}»\n"
                 + $"  Dice: {string.Join(" ", textos.Take(3))}\n"
                 + $"  Opciones: {string.Join(", ", opciones.Select(o => $"«{o}»"))}\n"
                 + "  Hay una DECISIÓN aquí, y depende de lo que estuvieras intentando: continuar o "
                 + "no es tuyo, no mío. Vuelve a llamarme con `choose` indicando la opción.";

        // El veto, ahora sobre el VERBO y no sobre el tipo de control: responder a un diálogo es
        // legítimo —para eso está—, lo que no lo es es responder «Eliminar». Aunque lo pida la
        // capa consciente: esa decisión es del usuario.
        if (SafeToClick.EsDestructivo(elegida, out string motivo))
            return $"NO pulso «{elegida}»: {motivo}. Una opción destructiva la confirma el usuario, no yo.";

        // NI SIQUIERA SI ME LO PIDEN. `choose` es una instrucción explícita y por eso se respeta casi
        // siempre —responder diálogos es para lo que existe esta herramienta—, pero abrir una sesión
        // de cuenta o autorizar un pago no es responder un diálogo: es comprometer al usuario con un
        // tercero. Eso lo pulsa él, delante de la pantalla. Mismo criterio que el veto de lo
        // destructivo, que también ignora a la capa consciente a propósito.
        if (EsCuentaOPago(elegida))
            return $"NO pulso «{elegida}»: abre una sesión de cuenta o un pago, y eso compromete al "
                 + "usuario con un tercero. Tiene que pulsarlo él. Puedo cerrar el diálogo si quieres "
                 + "seguir sin eso.";

        // SE PULSA EL BOTÓN DEL DIÁLOGO, y ninguno otro. Hasta el 2026-09-14 se construía
        // «uia:name=Cerrar;ct=Button» y se resolvía en toda la ventana: el primer «Cerrar» de Chrome es
        // el de la barra de título, y se cerró el navegador dos veces creyendo cerrar una barra.
        if (!dialogo.Pulsar(elegida))
            return $"no pude pulsar «{elegida}» del diálogo «{titulo}»: su botón no admite ningún patrón, y no pulso por nombre en la ventana";

        // ¿Se fue de verdad? Un desbloqueo que no desbloquea es peor que no intentarlo.
        bool libre = false;
        for (int i = 0; i < 20; i++)
        {
            System.Threading.Thread.Sleep(120);
            var sigue = DialogoDelante();
            if (sigue == null || sigue.Opciones.Count == 0) { libre = true; break; }
        }
        LogBus.Log("mapa-mcp", $"DESBLOQUEO: «{titulo}» → pulsado «{elegida}» · {(libre ? "resuelto" : "sigue ahí")}");
        if (!libre)
            return $"pulsé «{elegida}» y el diálogo «{titulo}» sigue delante. No insisto sola: dime qué hacer.";

        // Reanudar por el mapa, que es de lo que se trata: salir del atasco no sirve de nada si la
        // tarea no puede continuar desde donde estaba.
        // …salvo que `at` sea el título del diálogo: eso no es un sitio, y el modelo lo manda así a veces
        // («at=Barra de información», 19:42:29). Reanudar hacia ahí solo hace ruido.
        bool reanudar = reanudarEn.Length > 0 && !string.Equals(reanudarEn.Trim(), titulo.Trim(), StringComparison.OrdinalIgnoreCase);
        string vuelta = reanudar ? GoTo(reanudarEn) : "";
        return $"DESBLOQUEADO. Era «{titulo}» ({string.Join(" ", textos.Take(1))}); pulsé «{elegida}»."
             + (vuelta.Length > 0 ? $"\n  Reanudación: {vuelta}" : "")
             + "\n  INCIDENTE registrado — si este diálogo se repite, es una regla que falta.";
    }

    /// <summary>
    /// La opción que se puede tomar SIN decidir nada, o "" si hay que preguntar al consciente.
    ///
    /// Solo hay un caso: el aviso informativo, el que tiene una única salida. Ahí no se elige nada
    /// —se acusa recibo— y automatizarlo no arriesga.
    ///
    /// En cuanto hay dos opciones hay una DECISIÓN, y no es de esta capa. La versión anterior
    /// prefería siempre la que «no compromete» (Cancelar/No/Cerrar), y eso parecía prudente y era
    /// falso: si la tarea quería continuar de verdad, cancelar por regla la rompe igual, solo que
    /// en silencio y con aire de cautela. Si continuar o no depende de lo que se estuviera
    /// intentando, y eso solo lo sabe quien tiene la intención (2026-08-03, corregido por el
    /// usuario). Se prefiere preguntar a acertar por casualidad.
    /// </summary>
    /// <remarks>
    /// La X del título NO es una opción: es la salida. Contarla como tal hacía que un aviso con un
    /// solo botón —«Ubicación no disponible», con «Aceptar» y la X— pareciera una decisión de dos
    /// caminos, así que se escalaba al consciente, nadie lo cerraba y el diálogo se quedaba delante
    /// envenenando todas las corridas siguientes (2026-08-03, visto en pantalla). Las opciones son
    /// lo que el aviso PROPONE, no las formas de deshacerse de él.
    /// </remarks>
    /// <summary>
    /// La opción que se puede pulsar SIN preguntarle a nadie.
    ///
    /// QUITAR LA FORMA DE DECLINAR CONVIERTE UNA DECISIÓN EN UN SÍ FORZADO. Esto filtraba «Cerrar»
    /// por considerarlo cromo de ventana y, si quedaba UNA sola opción, la pulsaba automáticamente
    /// dando por hecho que «una opción no es una elección». El 2026-08-10 Windows ofreció
    /// «Iniciar sesión» y «Cerrar» para una copia de seguridad con cuenta Microsoft: se filtró
    /// «Cerrar», quedó una, y el sistema **inició sesión solo** — y lo reportó como DESBLOQUEADO.
    /// Se pidió declinar y aceptó.
    ///
    /// El error de fondo: en un diálogo así la elección no es «cuál de las respuestas», es
    /// **aceptar o irse**, y «Cerrar» ES la respuesta de irse. Cualquier consentimiento, login o
    /// upsell con la forma [Acción] + [Cerrar] se auto-aceptaba.
    ///
    /// Ahora manda otra regla, y es la de siempre para un agente sin supervisión: **si hay manera de
    /// declinar, esa es la segura.** Y si la única respuesta que queda compromete algo —iniciar
    /// sesión, aceptar, permitir, activar, comprar— no se pulsa: se devuelve la decisión, que es de
    /// quien va a vivir con ella.
    /// </summary>
    private static string OpcionSegura(List<string> opciones)
    {
        // 1. DECLINAR SIEMPRE GANA. Irse nunca compromete nada; quedarse puede.
        string? declinar = opciones.FirstOrDefault(EsDeclinar);
        if (declinar != null) return declinar;

        // 2. Sin salida ofrecida: solo se automatiza si lo que queda no compromete nada.
        var reales = opciones.Where(o => !EsSalidaDeVentana(o)).ToList();
        if (reales.Count == 1 && !EsCompromiso(reales[0])) return reales[0];

        return "";   // hay una decisión: la toma el usuario, con `choose`
    }

    /// <summary>
    /// ¿Esta opción es «irse sin hacer nada»? Se compara por PREFIJO porque Windows etiqueta los
    /// botones de marco con el nombre de la ventana detrás: «Cerrar Copias de seguridad de Windows».
    /// Con <c>Equals</c> ese no casaba con nada y el diálogo entero quedaba sin salida reconocible.
    /// </summary>
    private static bool EsDeclinar(string etiqueta)
    {
        string e = (etiqueta ?? "").Trim();
        string[] formas =
        {
            "cerrar", "close", "cancelar", "cancel", "no, gracias", "no thanks", "ahora no",
            "not now", "más tarde", "mas tarde", "later", "omitir", "skip", "rechazar", "decline",
            "descartar", "dismiss",
        };
        return formas.Any(f => e.StartsWith(f, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// ¿Esta opción COMPROMETE algo — una cuenta, un permiso, un pago, un cambio de configuración?
    ///
    /// No es la lista de lo destructivo (eso ya lo veta <c>SafeToClick.EsDestructivo</c>): es la de
    /// lo que ata al usuario a algo. Nada de esto se pulsa solo, ni siquiera cuando es la única
    /// opción que queda — sobre todo entonces, porque «solo queda una» es justo como se disfraza un
    /// diálogo que no acepta un no.
    /// </summary>
    private static bool EsCompromiso(string etiqueta)
    {
        string e = (etiqueta ?? "").Trim();
        string[] verbos =
        {
            "iniciar sesión", "iniciar sesion", "sign in", "log in", "acceder", "entrar",
            "crear cuenta", "registrar", "sign up", "suscrib", "subscribe", "comprar", "buy",
            "pagar", "pay", "aceptar", "accept", "acepto", "estoy de acuerdo", "agree",
            "permitir", "allow", "conceder", "grant", "activar", "enable", "habilitar",
            "continuar", "continue", "siguiente", "next", "sí", "si", "yes", "ok", "aceptar y",
        };
        return verbos.Any(v => e.StartsWith(v, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// El subconjunto de <see cref="EsCompromiso"/> que NO se pulsa ni con <c>choose</c> explícito:
    /// abrir sesión en una cuenta, crearla, o autorizar un pago. No es responder un diálogo, es atar
    /// al usuario con un tercero — y eso lo hace él, delante de la pantalla.
    /// </summary>
    private static bool EsCuentaOPago(string etiqueta)
    {
        string e = (etiqueta ?? "").Trim();
        string[] verbos =
        {
            "iniciar sesión", "iniciar sesion", "sign in", "log in", "acceder con", "entrar con",
            "crear cuenta", "crear una cuenta", "registrar", "sign up", "create account",
            "suscrib", "subscribe", "comprar", "buy", "pagar", "pay", "añadir tarjeta", "add card",
        };
        return verbos.Any(v => e.StartsWith(v, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>¿Esta «opción» es en realidad el cierre de la ventana y no una respuesta?</summary>
    private static bool EsSalidaDeVentana(string etiqueta) =>
        EsDeclinar(etiqueta)
        || etiqueta.StartsWith("Minimizar", StringComparison.OrdinalIgnoreCase)
        || etiqueta.StartsWith("Maximizar", StringComparison.OrdinalIgnoreCase)
        || etiqueta.StartsWith("Minimize", StringComparison.OrdinalIgnoreCase)
        || etiqueta.StartsWith("Maximize", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Si delante hay un DIÁLOGO, lo describe como lo que es: una interrupción con una pregunta y
    /// unas opciones. Devuelve "" si no lo hay.
    ///
    /// El sistema los trataba como nodos del grafo —«estás en uia://explorer.exe/ubicación-no-
    /// disponible, 15 salidas»— y eso es un error de modelado con consecuencias: quien tenga que
    /// resolverlo, persona o modelo, recibía ruido en vez del dato que necesita. Un diálogo no es
    /// un sitio al que se llega: es algo que se cruza en el camino, dice POR QUÉ, y ofrece unas
    /// opciones entre las que hay que elegir. Es la misma corrección que ya hicimos con los menús
    /// («no es otro sitio, es una capa»), aplicada a las interrupciones (2026-08-03).
    ///
    /// Solo describe. No decide: elegir entre «Aceptar», «Omitir» o «Sí» es criterio —el aviso de
    /// cambiar la extensión de un archivo tiene un «Sí» que lo corrompe— y esa decisión es de la
    /// capa consciente, con el veto de SafeToClick encima.
    /// </summary>
    private string DescribirInterrupcion()
    {
        string d = Interrupcion.Describir(VentanaDeTrabajo?.Invoke() ?? IntPtr.Zero);
        if (d.Length > 0) LogBus.Log("mapa-mcp", "INTERRUPCIÓN detectada");
        return d;
    }

    /// <summary>
    /// Lee el diálogo que haya delante: título, lo que dice y entre qué se puede elegir.
    /// Opciones vacías = no hay diálogo.
    ///
    /// Se reconoce por su FORMA —pocos botones de respuesta más texto que explica— y no por el
    /// título, que cambia con el idioma y con cada versión de Windows. El explorador normal, con
    /// 18 botones, no se confunde (comprobado el 2026-08-03).
    /// </summary>
    private (string Titulo, List<string> Textos, List<string> Opciones) LeerInterrupcion()
        => Interrupcion.Leer(VentanaDeTrabajo?.Invoke() ?? IntPtr.Zero);

    private (string Titulo, List<string> Textos, List<string> Opciones) LeerInterrupcionVieja()
    {
        var textos = new List<string>();
        var opciones = new List<string>();
        string titulo = "";
        try
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return (titulo, textos, opciones);
            var v = System.Windows.Automation.AutomationElement.FromHandle(fg);
            if (v == null) return (titulo, textos, opciones);

            foreach (System.Windows.Automation.AutomationElement b in v.FindAll(
                System.Windows.Automation.TreeScope.Descendants,
                new System.Windows.Automation.PropertyCondition(
                    System.Windows.Automation.AutomationElement.ControlTypeProperty,
                    System.Windows.Automation.ControlType.Button)))
            {
                try { string n = b.Current.Name?.Trim() ?? ""; if (n.Length > 0 && !opciones.Contains(n)) opciones.Add(n); }
                catch { }
            }
            // Muchos botones = es una app, no un diálogo. Ninguno = no hay nada que responder.
            if (opciones.Count == 0 || opciones.Count > 8) { opciones.Clear(); return (titulo, textos, opciones); }

            foreach (System.Windows.Automation.AutomationElement t in v.FindAll(
                System.Windows.Automation.TreeScope.Descendants,
                new System.Windows.Automation.PropertyCondition(
                    System.Windows.Automation.AutomationElement.ControlTypeProperty,
                    System.Windows.Automation.ControlType.Text)))
            {
                try { string n = t.Current.Name?.Trim() ?? ""; if (n.Length > 12 && !textos.Contains(n)) textos.Add(n); }
                catch { }
            }
            if (textos.Count == 0) { opciones.Clear(); return (titulo, textos, opciones); }

            try { titulo = v.Current.Name?.Trim() ?? ""; } catch { }
        }
        catch { opciones.Clear(); }
        return (titulo, textos, opciones);
    }

    private string Foto()
    {
        try
        {
            var ventana = AppAligner.VentanaDelUsuario();
            string? b64 = Capture.Screenshotter.CaptureVentanaBase64Png(ventana);
            if (string.IsNullOrEmpty(b64)) return "no pude capturar la ventana";
            LogBus.Log("mapa-mcp", $"foto de la ventana del usuario: {b64.Length} car. base64");
            return "data:image/png;base64," + b64;
        }
        catch (Exception e) { return $"no pude capturar: {e.Message}"; }
    }

    private string GoTo(string destino)
    {
        if (destino.Length == 0) return "falta `surface`: a dónde hay que ir";
        // TODO VA POR EL NÚCLEO desde la gran limpieza (2026-08-30): PasoDelNucleo es «la única
        // implementación de ir-a», y el grafo aprende cada tramo que camina.
        return PorElNucleo != null ? PorElNucleo(destino) : "todavía no sé navegar: el núcleo no está conectado.";
    }

    /// <summary>
    /// Mientras está puesto, cada cosa que se va a pulsar se SEÑALA antes: se enciende su recuadro y
    /// la carita se pone a su lado.
    /// </summary>
    /// <remarks>
    /// LO PIDIÓ EL DUEÑO EL 2026-09-03, viendo la primera comprobación que narraba bien: «cuando
    /// hable sobre algún elemento, que vaya y lo señale, acercando la carita al lado de ese
    /// elemento». Sin eso la narración es correcta y aun así no se puede seguir — se oye de qué
    /// habla y no se ve cuál es.
    ///
    /// SOLO DURANTE LA COMPROBACIÓN, y por eso es una bandera y no el comportamiento normal: en una
    /// ejecución de verdad lo que se quiere es que la tarea salga, no un recuadro parpadeando en
    /// cada clic.
    /// </remarks>
    public bool SenalarAlActuar { get; set; }

    private string Take(string salida, string cual = "", string decir = "", string recuerdo = "")
    {
        if (salida.Length == 0) return "falta `exit`: qué puerta tomar (su nombre tal como se ve, o su selector)";
        if (RecorrerPorElNucleo == null) return "todavía no sé pulsar: el núcleo no está conectado.";
        // CUÁL DE VARIOS (promesa 203): si el batch contestó con una lista numerada, `which` elige en ESE
        // orden. Hasta el 2026-09-10 map_take no tenía forma de decir cuál, y un homónimo acababa en
        // «dime el selector». Los antiguos `action` y `at` no llegaban aquí desde e3c3ad8: el gesto lo
        // aprende la arista (spec 003), y ofrecerlos era la ilusión de controlarlo (promesa 206).
        int.TryParse(cual, out int n);
        var paso = new Navigation.RecorrerSegunElNucleo.Paso(salida) { Cual = n, AntesDePulsar = _antesDePulsar };
        // LA MISMA COREOGRAFÍA QUE EL PLAN (promesa 191): al comprobar, o cuando el piloto trae algo que
        // decir o un recuerdo, la mano señala, dice, cuelga y muestra, y solo después pulsa.
        var r = DarUnPasoConCoreografia != null && (SenalarAlActuar || decir.Length > 0 || recuerdo.Length > 0)
            ? DarUnPasoConCoreografia(salida, paso, recuerdo, decir)
            : RecorrerPorElNucleo(new[] { paso });
        return Anotar(r, escribe: false);
    }

    /// <summary>
    /// Cómo salió la última acción de la mano, ESTRUCTURADO: si la tanda terminó y si se logró. Pulsar
    /// se logró si la pantalla cambió; escribir, si se escribió. Lo lee el tope de intentos de la voz
    /// (promesa 204) en vez de interpretar la prosa de la respuesta (aprendizaje nº2).
    /// </summary>
    /// <remarks>
    /// Por HILO y no por instancia: la voz lo lee justo después de su propia llamada, en el mismo hilo,
    /// y una llamada del servidor MCP que entrara a la vez desde otro hilo no le pisa el dato.
    /// Solo lo dan map_take y map_type; el resto de herramientas devuelven texto y aquí queda null.
    /// </remarks>
    public readonly record struct Mano(bool Termino, bool Logro, bool Intento = true)
    {
        /// <summary>Si la tanda contestó con una lista de homónimos: sus selectores, en el orden de su número.</summary>
        public IReadOnlyList<string>? Candidatos { get; init; }

        /// <summary>El selector que el ejecutor pulsó de verdad, si pulsó algo.</summary>
        public string? Pulsado { get; init; }
    }

    [ThreadStatic] private static Mano? _ultimaMano;

    // LA CONSULTA AL TOPE ANTES DE PULSAR (promesa 204). La pone la voz en SU hilo justo antes de llamar
    // y la quita al volver; viaja dentro del paso hasta el ejecutor, que la hace con el selector que va
    // a pulsar. Por hilo, como la mano: una llamada del servidor MCP desde otro hilo no la hereda.
    [ThreadStatic] private static Func<string, string?>? _antesDePulsar;

    public Func<string, string?>? AntesDePulsarEnEsteHilo
    {
        get => _antesDePulsar;
        set => _antesDePulsar = value;
    }

    public Mano? UltimaMano => _ultimaMano;

    private static string Anotar(Navigation.RecorrerSegunElNucleo.Resultado r, bool escribe)
    {
        // La lista numerada de homónimos NO es un intento: no se pulsó nada, y contarla como fallo
        // frenaba el «pruebo este otro botón» que pide el audio (crítico de la rama, 2026-09-11).
        _ultimaMano = new Mano(r.Termino, r.Termino && (escribe || r.Cambio), Intento: !r.Ambiguo) { Candidatos = r.Candidatos, Pulsado = r.Pulsado };
        return r.Cuenta;
    }

    /// <summary>
    /// Escribe texto en un campo. Sin `target`, en el que tenga el foco del teclado.
    ///
    /// Es la primitiva que faltaba para que una tarea completa se pueda hacer por la interfaz:
    /// renombrar exige escribir, y sin ella el asistente podía crear una carpeta pero no ponerle
    /// nombre. Se escribe por VALOR cuando el control lo admite —determinista, sin depender de la
    /// distribución del teclado ni de que ninguna tecla se quede hundida— y solo si no lo admite
    /// se recurre a teclear, con Enter al final para confirmar la edición en línea.
    /// </summary>
    private string Type(string texto, string target, string decir = "", string recuerdo = "")
    {
        if (texto.Length == 0) return "falta `text`: qué hay que escribir";

        // EN SAP SE ESCRIBE CON LA MANO DE SAP (2026-09-07). Esta herramienta escribía SIEMPRE por
        // UIA, que dentro de SAP GUI ve un Pane opaco: «no se encontró el elemento» para cualquier
        // campo del dynpro, y un Enter de regalo que en SAP es un viaje. Va por el mismo ejecutor
        // que el plan (EscribirPorMundo → la mano de SAP), que entiende el selector de la lección,
        // la etiqueta que la persona ve y el nombre técnico del campo (promesa 185).
        if ((_where()?.Id ?? "").StartsWith("sapgui://", StringComparison.OrdinalIgnoreCase) && RecorrerPorElNucleo != null)
        {
            // Y con la coreografía del plan cuando toca (promesa 191): se señala el campo, se dice, y luego se escribe.
            if (DarUnPasoConCoreografia != null && (SenalarAlActuar || decir.Length > 0 || recuerdo.Length > 0))
                return Anotar(DarUnPasoConCoreografia(target, new Navigation.RecorrerSegunElNucleo.Paso(target, texto), recuerdo, decir), escribe: true);
            return Anotar(RecorrerPorElNucleo(new[] { new Navigation.RecorrerSegunElNucleo.Paso(target, texto) }), escribe: true);
        }

        // ESCRIBIR VA A LA VENTANA DE TRABAJO (promesa 235), como pulsar. Un `target` por nombre se resuelve
        // a un campo de esa ventana; una terminal se teclea; sin `target` y con la ventana de trabajo
        // detrás, se busca el único campo a la vista. Lo de siempre —el campo con el foco— queda para
        // cuando la ventana de trabajo es la de delante o no hay ninguna.
        IntPtr ventana = VentanaDeTrabajo?.Invoke() ?? IntPtr.Zero;
        bool trabajoDetras = ventana != IntPtr.Zero && !UiaSurface.EstaDelante(ventana);
        bool terminal = ventana != IntPtr.Zero && UiaSurface.EsTerminal(ventana);
        string tituloVentana = ventana != IntPtr.Zero ? UiaSurface.TituloDe(ventana) : "";
        string selector = target;
        if (target.Length > 0 && !target.StartsWith("uia:", StringComparison.OrdinalIgnoreCase) && ventana != IntPtr.Zero)
        {
            var campo = _uia.CampoDeTexto(ventana, target);
            if (campo != null) selector = UiaSurface.SelectorDe(campo);
            else if (terminal) selector = "";   // en una consola «el campo» es la consola: se teclea
            else
            {
                _ultimaMano = new Mano(false, false);
                return ComoSeEscribe.NoEncontre(target, tituloVentana);
            }
        }
        if (selector.Length == 0 && terminal)
        {
            if (!_uia.TeclearEnLaVentana(ventana, texto, enter: true, out string errTeclado))
            {
                _ultimaMano = new Mano(false, false);
                return $"no pude teclear en «{tituloVentana}»: {errTeclado}";
            }
            _ultimaMano = new Mano(true, true);
            LogBus.Log("mapa-mcp", $"✓ tecleado «{texto}» en la terminal «{tituloVentana}»");
            return $"tecleé «{texto}» en la terminal «{tituloVentana}» y confirmé con Enter";
        }
        if (selector.Length == 0 && trabajoDetras)
        {
            var campos = _uia.CamposDeTexto(ventana);
            if (campos.Count == 1) selector = campos[0].Selector;
            else
            {
                _ultimaMano = new Mano(false, false);
                return campos.Count == 0
                    ? $"NO escribo: en «{tituloVentana}» no veo ningún campo de texto, y la persona tiene el foco en otra ventana. Pasa `target` con el nombre del campo."
                    : $"en «{tituloVentana}» hay {campos.Count} campos de texto: {string.Join(", ", campos.Select(c => $"«{c.Nombre}»"))}. Dime cuál con `target`.";
            }
        }
        if (selector.Length == 0)
        {
            // Se ESPERA a que aparezca un campo editable. Una edición en línea —el nombre de una
            // carpeta recién creada— tarda un instante en aparecer, y desde que las acciones son
            // rápidas se llegaba aquí antes que ella: se respondía «no hay ningún campo con el
            // foco» y la carpeta se quedaba como «Nueva carpeta» (2026-08-03).
            for (int i = 0; i < 30; i++)
            {
                try
                {
                    var f = System.Windows.Automation.AutomationElement.FocusedElement;
                    if (f != null && f.Current.ControlType == System.Windows.Automation.ControlType.Edit) break;
                }
                catch { }
                System.Threading.Thread.Sleep(50);   // se sondea fino: se sale en cuanto aparece
            }

            // El campo con el foco: es donde una persona escribiría sin pensarlo. Pero SOLO si de
            // verdad es un campo. Sin esta comprobación, cuando la edición en línea no llegaba a
            // abrirse se escribía sobre lo que estuviera seleccionado —un archivo del usuario— y
            // el explorador lo interpretaba como renombrar: «logo-empresa.png» se convirtió en
            // «Datos.png» y la respuesta fue «✓ escrito» (2026-08-03). Escribir a ciegas sobre la
            // selección no es escribir: es renombrar lo que haya delante y llamarlo éxito.
            string tipoFoco = "";
            System.Windows.Automation.AutomationElement? foco = null;
            try
            {
                foco = System.Windows.Automation.AutomationElement.FocusedElement;
                string aid = foco?.Current.AutomationId ?? "";
                string nombre = foco?.Current.Name ?? "";
                tipoFoco = (foco?.Current.ControlType.ProgrammaticName ?? "").Replace("ControlType.", "");
                selector = aid.Length > 0 && !aid.All(char.IsDigit) ? $"uia:aid={aid};ct={tipoFoco}"
                         : nombre.Length > 0 ? $"uia:name={nombre};ct={tipoFoco}"
                         : "";
            }
            catch { }
            // Intentó escribir y no había dónde: también es un intento que no se logró, y un «no hay
            // campo» repetido tiene que frenarse como cualquier otro (crítico final, 2026-09-11).
            if (selector.Length == 0)
            {
                _ultimaMano = new Mano(false, false);
                return "no hay ningún campo con el foco; pasa `target` con su selector";
            }

            // Si ACEPTA texto o no lo dice el control, no su nombre de tipo: el buscador de YouTube
            // es un ComboBox y se rechazaba por no llamarse «Edit», aunque es justo donde se escribe
            // (2026-08-05). La regla vive en UiaSurface.AceptaTexto, que también protege el caso
            // contrario: una fila de lista jamás acepta texto, porque ahí escribir es renombrar.
            if (foco == null || !U.Graph.Surfaces.UiaSurface.AceptaTexto(foco))
            {
                LogBus.Log("mapa-mcp", $"NO SE ESCRIBE: el foco lo tiene «{selector}», que es {tipoFoco}, no un campo de texto");
                _ultimaMano = new Mano(false, false);
                return $"NO escribo: no hay ningún campo de texto abierto. El foco lo tiene «{selector}» "
                     + $"({tipoFoco}), y escribir ahí no es escribir — es renombrar lo que esté seleccionado. "
                     + "Si querías renombrar, abre antes la edición (Cambiar nombre / F2) o pasa `target`.";
            }
        }

        var paso = new PlanStep { StepOrder = 1, ActionType = "input", Selector = selector, Value = texto, Label = target.Length > 0 ? target : selector };
        bool escrito = ventana != IntPtr.Zero ? _uia.Execute(paso, ventana, out string error) : _uia.Execute(paso, out error);
        if (!escrito)
        {
            _ultimaMano = new Mano(false, false);   // intentó escribir y no pudo: cuenta para el tope (207)
            return $"no pude escribir en «{paso.Label}»: {error}";
        }
        // ESCRIBIR POR UIA TAMBIÉN DICE SI SE LOGRÓ. Hasta el 2026-09-11 solo lo decía la rama que va por
        // el ejecutor (SAP): por UIA la mano quedaba vacía, y fuera de SAP el tope no frenaba nada.
        _ultimaMano = new Mano(true, true);

        // Enter confirma: en una edición en línea (renombrar) el texto no se aplica hasta que se
        // acepta, y dejarlo a medias deja la interfaz en un estado del que nadie se acuerda luego.
        string antes = _where()?.Id ?? "";
        if (trabajoDetras)
        {
            // El Enter va a la ventana con el foco, que es la de la persona: se manda a la de trabajo con el
            // enganche que la trae un instante y devuelve el foco (235).
            if (!_uia.TeclearEnLaVentana(ventana, "", enter: true, out string errEnter))
                LogBus.Log("mapa-mcp", $"escrito «{texto}» pero sin Enter: {errEnter}");
        }
        else
        {
            keybd_event(0x0D, 0, 0, IntPtr.Zero);
            keybd_event(0x0D, 0, 2, IntPtr.Zero);
        }
        EsperarPantallaLista(900);

        // EL ENTER PUEDE HABERNOS METIDO DENTRO. Al renombrar una carpeta recién creada queda
        // seleccionada, y el Enter que confirma el nombre también la ABRE: la tarea seguía creyendo
        // estar en la carpeta padre y el ancla rechazaba los pasos siguientes uno tras otro
        // (2026-08-03). Escribir un nombre no debería cambiar de sitio; si cambió, se deshace.
        string ahora = _where()?.Id ?? "";
        if (antes.Length > 0 && ahora.Length > 0
            && !string.Equals(antes, ahora, StringComparison.OrdinalIgnoreCase))
        {
            LogBus.Log("mapa-mcp", $"el Enter abrió «{ahora}»; se vuelve a «{antes}»");
            var atras = new PlanStep
            {
                StepOrder = 1, ActionType = "click",
                Selector = "uia:aid=backButton;ct=Button", Label = "Atrás",
            };
            _uia.Execute(atras, out _);
            Llego(antes, 2000);
        }

        LogBus.Log("mapa-mcp", $"✓ escrito «{texto}» en {selector}");
        return $"escribí «{texto}» y confirmé con Enter";
    }

    /// <summary>
    /// Espera a que la pantalla esté LISTA: que deje de cambiar. Devuelve en cuanto lo está.
    ///
    /// Es la idea de <c>SurfaceReadiness</c> —que ya usa el reproductor de workflows— traída a esta
    /// capa en su forma mínima. La diferencia con dormir un tiempo fijo es doble: se sigue en
    /// cuanto se puede, en vez de esperar el peor caso, y no se actúa antes de tiempo cuando la app
    /// tarda más de lo previsto. Un plazo fijo se equivoca en las dos direcciones a la vez.
    ///
    /// La señal es el número de elementos accionables: mientras la pantalla se pinta, sube; cuando
    /// se repite dos lecturas seguidas, está lista. El techo es una red contra pantallas que nunca
    /// se asientan (una lista que se refresca sola), no el mecanismo de espera.
    /// </summary>
    private bool EsperarPantallaLista(int msMax = 2500)
    {
        int anterior = -1;
        for (int i = 0; i < msMax / 90; i++)
        {
            int ahora = CuantosAccionables();
            if (ahora > 0 && ahora == anterior) return true;
            anterior = ahora;
            System.Threading.Thread.Sleep(90);
        }
        return false;
    }

    /// <summary>Cuántos elementos accionables hay ahora. Consulta dirigida: nada de leer el árbol.</summary>
    private static int CuantosAccionables()
    {
        try
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return 0;
            var raiz = System.Windows.Automation.AutomationElement.FromHandle(fg);
            if (raiz == null) return 0;
            int n = 0;
            foreach (var ct in new[] { System.Windows.Automation.ControlType.Button,
                                       System.Windows.Automation.ControlType.ListItem,
                                       System.Windows.Automation.ControlType.MenuItem })
            {
                n += raiz.FindAll(System.Windows.Automation.TreeScope.Descendants,
                    new System.Windows.Automation.PropertyCondition(
                        System.Windows.Automation.AutomationElement.ControlTypeProperty, ct)).Count;
            }
            return n;
        }
        catch { return 0; }
    }

    /// <summary>Espera a que la superficie DEJE de ser la de partida y devuelve la nueva, o "".</summary>
    private string EsperarCambio(string desde, int msMax)
    {
        for (int i = 0; i < msMax / 80; i++)
        {
            System.Threading.Thread.Sleep(80);
            string ahora = _where()?.Id ?? "";
            if (ahora.Length > 0 && !string.Equals(ahora, desde, StringComparison.OrdinalIgnoreCase)
                && !ahora.EndsWith("/ventana", StringComparison.OrdinalIgnoreCase))
                return ahora;
        }
        return "";
    }

    /// <summary>Espera a que la superficie sea la esperada. La UI tarda; la paciencia va aquí.</summary>
    private bool Llego(string esperada, int msMax)
    {
        var hasta = DateTime.UtcNow.AddMilliseconds(msMax);
        while (DateTime.UtcNow < hasta)
        {
            string ahora = _where()?.Id ?? "";
            if (ahora.Length > 0 && SurfacePlace.Same(ahora, esperada)) return true;
            System.Threading.Thread.Sleep(200);
        }
        return false;
    }
}
