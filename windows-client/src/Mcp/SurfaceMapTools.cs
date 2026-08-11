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
    private readonly SurfaceMap _map;
    private readonly Func<SurfaceLocator.SurfaceLocation?> _where;
    // SoloEnFoco: esta capa verifica la ubicación antes de actuar, así que si un selector no está
    // en la ventana de delante es que no está. Sin esto, un nombre inexistente disparaba un barrido
    // por todas las ventanas del escritorio que llegó a tardar 181 s en fallar (2026-08-03).
    private readonly UiaSurface _uia = new() { Log = s => LogBus.Log("mapa-mcp", s), SoloEnFoco = true };
    private readonly UiaReader _lector = new();

    /// <summary>La app con la que se estaba trabajando. Se usa para volver a ella si algo roba el foco.</summary>
    private string _ultimaApp = "";
    private List<string> _seleccionPrevia = new();

    /// <summary>Lo último que se intentó. Sin esto, quien deba decidir ante un diálogo no sabe
    /// para qué apareció, y «continuar o no» depende justamente de eso.</summary>
    private string _ultimaAccion = "";

    /// <summary>Ya estamos volviendo a la ubicación esperada: la vuelta no puede pedir otra vuelta.</summary>
    private bool _reanudando;

    /// <summary>Ya estamos buscando un camino alternativo: un solo reintento, no una cadena.</summary>
    private bool _reenrutando;

    /// <summary>
    /// A dónde llevaría «Atrás» AHORA MISMO. Estado efímero de la sesión, nunca una arista.
    ///
    /// El botón Atrás no describe una propiedad de la pantalla —depende de cómo se llegó— así que
    /// guardarlo en el mapa lo hace mentir en cuanto se llega por otro camino. Pero SÍ se sabe a
    /// dónde lleva en esta sesión: al sitio del que se vino. Recordarlo permite usarlo cuando
    /// conviene y descartarlo cuando el destino es otro, en vez de tener que elegir entre
    /// aprenderlo mal o no tenerlo (idea del usuario, 2026-08-02).
    /// </summary>
    private readonly Stack<string> _historial = new();

    /// <summary>Anota que se pasó de <paramref name="de"/> a <paramref name="a"/>.</summary>
    private void Anotar(string de, string a)
    {
        if (de.Length == 0 || a.Length == 0 || string.Equals(de, a, StringComparison.OrdinalIgnoreCase)) return;
        // Si volvimos justo al sitio anterior, se DESAPILA en vez de apilar: si no, el historial
        // crecería con idas y vueltas y «Atrás» acabaría prometiendo un bucle.
        if (_historial.Count > 0 && string.Equals(_historial.Peek(), a, StringComparison.OrdinalIgnoreCase))
            _historial.Pop();
        else
            _historial.Push(de);
    }

    /// <summary>A dónde lleva «Atrás» en esta sesión, o "" si no se sabe.</summary>
    private string DestinoDeAtras() => _historial.Count > 0 ? _historial.Peek() : "";

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int max);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern void keybd_event(byte key, byte scan, uint flags, IntPtr extra);
    private delegate bool EnumProc(IntPtr h, IntPtr l);

    /// <summary>Superficies del propio Windows que se ponen delante solas y tapan la app.</summary>
    private static bool EsPanelDelShell(string proc) =>
        proc.Equals("ShellExperienceHost", StringComparison.OrdinalIgnoreCase)
        || proc.Equals("SearchHost", StringComparison.OrdinalIgnoreCase)
        || proc.Equals("StartMenuExperienceHost", StringComparison.OrdinalIgnoreCase)
        || proc.Equals("TextInputHost", StringComparison.OrdinalIgnoreCase);

    /// <summary>«uia://explorer.exe/loquesea» → «explorer».</summary>
    private static string AppDe(string id)
    {
        var m = System.Text.RegularExpressions.Regex.Match(id ?? "", @"^uia://([^/]+?)\.exe/");
        return m.Success ? m.Groups[1].Value : "";
    }

    /// <summary>
    /// Devuelve el foco a la app con la que se está trabajando si algo se lo ha llevado.
    ///
    /// El centro de notificaciones, el buscador o cualquier aviso de Windows se ponen delante solos
    /// y la secuencia se rompe: el asistente pedía «Nuevo» y se le contestaba con las opciones del
    /// centro de notificaciones (2026-08-02). El recorrido automático ya se recolocaba; esta capa
    /// no, y es la que usa el asistente para hacer tareas de verdad. No se lanza nada: si la app no
    /// está viva, se dice y punto — abrir aplicaciones por iniciativa propia no es recuperarse.
    /// </summary>
    private bool AsegurarFoco(string app)
    {
        if (app.Length == 0) return true;
        // Se sale pronto solo si LAS DOS fuentes coinciden. Bastaba con el primer plano y era la
        // trampa: tras cerrarse un panel del shell el foco ya era correcto pero el localizador
        // —que sondea cada 800 ms— seguía diciendo «SearchHost», así que la ruta se calculaba desde
        // un sitio donde ya no estábamos y se respondía «no conozco ruta» (2026-08-02).
        if (Coinciden(app)) return true;

        // Los paneles del shell —centro de notificaciones, buscador, menú inicio— NO se apartan con
        // SetForegroundWindow: Windows lo bloquea mientras uno de ellos tiene el foco, así que el
        // intento fallaba en silencio y la tarea moría ahí (2026-08-02). Se DESCARTAN con Escape,
        // que es lo mismo que haría una persona, y solo después se recupera la app.
        for (int intento = 0; intento < 2 && EsPanelDelShell(AppEnFrente()); intento++)
        {
            LogBus.Log("mapa-mcp", $"«{AppEnFrente()}» tiene el foco: se descarta con Escape");
            keybd_event(0x1B, 0, 0, IntPtr.Zero);
            keybd_event(0x1B, 0, 2, IntPtr.Zero);
            System.Threading.Thread.Sleep(500);
        }
        if (EsperarCoincidencia(app)) return true;

        IntPtr elegida = IntPtr.Zero;
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h)) return true;
            var sb = new System.Text.StringBuilder(300);
            if (GetWindowText(h, sb, 300) == 0) return true;   // sin título: no es una pantalla
            GetWindowThreadProcessId(h, out uint pid);
            try
            {
                using var p = System.Diagnostics.Process.GetProcessById((int)pid);
                if (!p.ProcessName.Equals(app, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch { return true; }
            elegida = h;
            return false;
        }, IntPtr.Zero);

        if (elegida == IntPtr.Zero) return false;
        // Una sola forma de traer una ventana al frente en toda la app: ver AppAligner.TraerAlFrente.
        // Aquí había una copia sin el enganche a la cola de entrada, que falla en silencio cuando
        // quien llama no está delante — y quien llama a esto casi nunca lo está.
        AppAligner.TraerAlFrente(elegida);

        // Se espera a que lo confirmen LAS DOS fuentes: el sistema (quién está delante) y el
        // localizador, que sondea cada 800 ms. Conformarse con la primera dejaba una ventana en la
        // que el foco ya era correcto pero la superficie seguía siendo la de antes, y la ruta se
        // calculaba desde el sitio equivocado: «no conozco ruta desde SearchHost» estando ya en el
        // explorador (2026-08-02). Recuperar el foco no es haberlo notado.
        if (!EsperarCoincidencia(app)) return false;
        LogBus.Log("mapa-mcp", $"el foco se había ido; devuelto a «{app}»");
        return true;
    }

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
    private void AprenderSubida(string padre, string hijo, string tipoDePuerta)
    {
        if (!tipoDePuerta.Equals("listitem", StringComparison.OrdinalIgnoreCase)) return;
        if (padre.Length == 0 || hijo.Length == 0
            || string.Equals(padre, hijo, StringComparison.OrdinalIgnoreCase)) return;

        // ¿EXISTE el botón de subir en ESTA app? «Subir un nivel» es del explorador de archivos;
        // Configuración no lo tiene. Aprender la arista sin comprobarlo llenó su grafo de caminos
        // imposibles: las rutas se planificaban por un botón inexistente y 6 de 7 navegaciones
        // fallaban sobre un mapa que por lo demás estaba bien (2026-08-03). Una regla ganada en una
        // app no se exporta a las demás sin verificarla — es justo lo que la segunda app existe
        // para enseñarnos.
        if (!ExisteBotonSubir()) return;

        _map.LearnTraversal(hijo, padre, "uia:aid=upButton;ct=Button",
            Array.Empty<string>(), "Subir un nivel", "Button", "click");
        LogBus.Log("mapa-mcp", $"aprendida la subida: '{hijo}' → '{padre}'");
    }

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
    private string LoQueVeo()
    {
        var loc = _where();
        string aqui = loc?.Id ?? "";
        if (aqui.Length == 0) return "no sé en qué pantalla estoy";

        _lector.Read();
        var vivos = _lector.Elements
            .Where(e => e.Label.Length > 0
                     && !e.ControlType.Equals("text", StringComparison.OrdinalIgnoreCase)
                     && !e.ControlType.Equals("image", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (vivos.Count == 0) return $"en «{aqui}» no veo ningún elemento accionable ahora mismo";

        var enMapa = _map.ExitsFrom(aqui)
            .GroupBy(h => h.Info.Label, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var sb = new System.Text.StringBuilder(
            $"EN PANTALLA AHORA, en «{aqui}» ({vivos.Count} elemento(s)). "
            + "«nivel N» = lo que el mapa sabe; «fijado» = lo puso una persona; «nuevo» = el mapa aún no lo tiene.\n");
        foreach (var el in vivos.Take(60))
        {
            string estado = "nuevo";
            if (enMapa.TryGetValue(el.Label, out var h))
                estado = (h.Info.NivelNav >= 0 ? $"nivel {h.Info.NivelNav}" : "sin nivel")
                       + (h.Info.NivelFijado ? " · fijado" : "");
            sb.AppendLine($"  «{el.Label}» ({el.ControlType})  →  {estado}");
        }
        if (vivos.Count > 60) sb.AppendLine($"  …y {vivos.Count - 60} más");
        return sb.ToString();
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
    private string Mostrar(string que)
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
            elegidos = Uia.Reconocedor.Buscar(candidatos, que).Take(1).ToList();
            comoSeLlama = que;
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
                 + ". Para moverlos de nivel, map_set_level uno por uno.";
        }

        var el = elegidos[0];
        Ui.Senalador.Senalar(el.Bounds, el.Label);
        string aqui = _where()?.Id ?? "";
        var h = aqui.Length > 0
            ? _map.ExitsFrom(aqui).FirstOrDefault(x => x.Info.Label.Equals(el.Label, StringComparison.OrdinalIgnoreCase))
            : null;
        // «El mapa aún no lo tiene» se leía como una negativa, y el asistente la repetía tal cual:
        // «lo veo pero como no lo conozco no puedo marcarlo». No conocerlo nunca ha impedido nada
        // —el mapa es memoria de lo recorrido, no permiso para actuar—, así que se dice lo que de
        // verdad significa: aún sin recorrer (2026-08-05).
        string enMapa = h == null ? "aún sin recorrer, se aprende al cruzarla"
            : (h.Info.NivelNav >= 0 ? $"nivel {h.Info.NivelNav}" : "sin nivel")
              + (h.Info.NivelFijado ? " · fijado" : "");

        return $"SÍ veo «{el.Label}» ({el.ControlType}) y lo estoy señalando: recuadro encendido y "
             + $"la carita puesta a su lado. En el mapa: {enMapa}. Lo puedo pulsar ahora mismo con "
             + $"map_take exit=«{el.Label}» — que lo vea basta.";
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
    private string LoQueSenala()
    {
        try
        {
            if (!GetCursorPos(out var p)) return "no pude leer dónde está el cursor";
            var el = System.Windows.Automation.AutomationElement.FromPoint(
                new System.Windows.Point(p.X, p.Y));
            if (el == null) return "bajo el cursor no hay ningún elemento que UIA reconozca";

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

            if (nombre.Length == 0)
                return "bajo el cursor no hay nada con nombre, ni en él ni en lo que lo contiene. "
                     + "Muévelo un poco y vuelve a preguntar.";

            // Y se ilumina: si el usuario señala y el asistente dice un nombre, hay que poder
            // comprobar de un vistazo que hablan del mismo sitio.
            bool iluminado = !caja.IsEmpty && caja.Width >= 1 && caja.Height >= 1;
            if (iluminado) Ui.Senalador.Senalar(caja, nombre);

            string aqui = _where()?.Id ?? "";
            var h = aqui.Length > 0
                ? _map.ExitsFrom(aqui).FirstOrDefault(x => x.Info.Label.Equals(nombre, StringComparison.OrdinalIgnoreCase))
                : null;
            string estado = h == null ? "el mapa aún no lo tiene"
                : (h.Info.NivelNav >= 0 ? $"nivel {h.Info.NivelNav}" : "sin nivel")
                  + (h.Info.NivelFijado ? " · fijado a mano" : " · deducido");

            return $"señalas «{nombre}» ({tipo}) · {estado}"
                 + (iluminado ? " · lo estoy iluminando y me pongo a su lado" : " · no he podido iluminarlo (sin caja)")
                 + $". Para moverlo de nivel: map_set_level con exit=«{nombre}».";
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

    private string FijarNivelMirandoAntes(string app, string cuales, int nivel, bool? cromo)
    {
        var donde = _where();
        if (donde != null) ObservarAqui(donde.Id);
        return FijarNivelDeVarios(app, cuales, nivel, cromo);
    }

    private string FijarNivelDeVarios(string app, string cuales, int nivel, bool? cromo = null)
    {
        // PRIMERO ENTERO. La coma separa una lista… y también vive dentro de etiquetas reales:
        // «English 7,189,000+ articles» se partía en tres trozos y ninguno existía (2026-08-07,
        // en la primera web mapeada). Si lo pedido existe tal cual, es UNA etiqueta y no hay lista.
        string entero = cuales.Trim();
        if (entero.Length > 0)
        {
            string r0 = _map.FijarNivel(app, entero, nivel, porPersona: true, cromo);
            if (!r0.Contains("no encuentro", StringComparison.OrdinalIgnoreCase)) return r0;
        }

        var nombres = cuales.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(n => n.Trim()).Where(n => n.Length > 0)
                            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (nombres.Count == 0) return "falta `exit`: qué salida (o cuáles, separadas por comas) mover de nivel";
        if (nombres.Count == 1) return _map.FijarNivel(app, nombres[0], nivel, porPersona: true, cromo);

        var hechos = new List<string>();
        var fallados = new List<string>();
        foreach (var n in nombres)
        {
            string r = _map.FijarNivel(app, n, nivel, porPersona: true, cromo);
            if (r.Contains("no encuentro", StringComparison.OrdinalIgnoreCase)) fallados.Add(n);
            else hechos.Add(n);
        }

        var sb = new System.Text.StringBuilder();
        if (hechos.Count > 0)
            sb.Append($"Al nivel {nivel} de «{app}» han pasado {hechos.Count}: ")
              .Append(string.Join(", ", hechos.Select(h => $"«{h}»")))
              .Append(". Fijados: la deducción ya no los mueve.");
        if (fallados.Count > 0)
            sb.Append(hechos.Count > 0 ? " " : "")
              .Append($"NO encontré {fallados.Count}: ")
              .Append(string.Join(", ", fallados.Select(f => $"«{f}»")))
              .Append(" — no hay ninguna salida con ese nombre en esta app.");
        return sb.ToString();
    }

    private string OpenApp(string app)
    {
        if (app.Length == 0) return "falta `app`: qué abrir (por ejemplo «explorer» o «notepad»)";
        app = app.Replace(".exe", "", StringComparison.OrdinalIgnoreCase).Trim();
        _ultimaApp = app;

        if (!AsegurarFoco(app))
        {
            LogBus.Log("mapa-mcp", $"«{app}» no estaba delante; se abre");
            AppAligner.FocusOrLaunch(app);
            if (!AsegurarFoco(app))
                return $"no pude abrir «{app}» ni traerla al frente; ahora hay «{AppEnFrente()}»";
        }

        EsperarPantallaLista(2000);
        var loc = _where();
        string donde = loc?.Id ?? "";

        // EL PROCESO NO ES LA APLICACIÓN, y en Windows el caso que lo demuestra es el explorador:
        // explorer.exe SIEMPRE está vivo porque ES el escritorio y la barra de tareas. Enfocar el
        // proceso te deja en «program-manager» —el escritorio— y desde ahí el modelo se puso a
        // buscar accesos directos entre los iconos, que no es lo que se le pidió (2026-08-04).
        // «Abrir el explorador» significa una VENTANA de archivos, así que si no hay ninguna, se
        // abre; y si ya la había, AsegurarFoco ya nos habrá dejado dentro.
        if (EsEscritorio(donde))
        {
            LogBus.Log("mapa-mcp", $"«{donde}» es el escritorio, no una ventana de {app}: se abre una");
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(app) { UseShellExecute = true }); }
            catch (Exception e) { return $"no pude abrir una ventana de «{app}»: {e.Message}"; }

            for (int i = 0; i < 30 && EsEscritorio(donde); i++)
            {
                System.Threading.Thread.Sleep(120);
                donde = _where()?.Id ?? "";
            }
            if (EsEscritorio(donde))
                return $"abrí «{app}» pero sigo viendo el escritorio; puede que la ventana tarde en salir";
            EsperarPantallaLista(1500);
        }
        if (donde.Length > 0) { Anotar("", donde); ObservarAqui(donde); }
        return donde.Length > 0
            ? $"«{app}» está delante. Estás en «{donde}»."
            : $"«{app}» está delante, pero aún no sé identificar la pantalla.";
    }

    /// <summary>
    /// ¿Lo que hay delante NO es una ventana de la app —el escritorio, o algo sin identidad—?
    ///
    /// Qué es el escritorio lo sabe <see cref="Escritorio"/>, para toda la app. Aquí se añade el
    /// caso propio de abrir una app: una superficie vacía o «/ventana» —una ventana sin título que
    /// no identifica nada— cuenta igual, porque la decisión que se toma con esto es la misma: abrir
    /// una ventana de verdad.
    /// </summary>
    private static bool EsEscritorio(string id) =>
        id.Length == 0
        || Escritorio.EsId(id)
        || id.Contains("/ventana", StringComparison.OrdinalIgnoreCase);

    private string LearnApp(string app)
    {
        if (app.Length == 0)
            return "falta `app`: el proceso a aprender (por ejemplo «explorer» o «ApplicationFrameHost»)";

        _ultimaApp = app;
        if (!AsegurarFoco(app))
        {
            // No estaba viva: se abre. Lanzar una app es razonable cuando ALGUIEN LA PIDIÓ por su
            // nombre; lo que no vale es abrir cosas por iniciativa propia al recuperarse de un fallo.
            LogBus.Log("mapa-mcp", $"«{app}» no estaba delante; se intenta abrir");
            AppAligner.FocusOrLaunch(app);
            if (!AsegurarFoco(app))
                return $"no pude poner «{app}» delante (ahora hay «{AppEnFrente()}»); no mapeo a ciegas";
        }

        var loc = _where();
        if (loc == null || !AppDe(loc.Id).Equals(app, StringComparison.OrdinalIgnoreCase))
            return $"«{app}» está delante pero la superficie no lo confirma ({loc?.Id}); no mapeo.";

        LogBus.Log("mapa-mcp", $"→ aprender «{app}» desde '{loc.Id}'");
        var crawler = new GraphCrawler(_map, _where);
        try
        {
            string r = crawler.CrawlAsync(120, 4, System.Threading.CancellationToken.None)
                              .GetAwaiter().GetResult();
            LogBus.Log("mapa-mcp", $"← aprender «{app}»: {r}");
            return $"aprendida «{app}»: {r}";
        }
        catch (Exception e) { return $"el recorrido de «{app}» falló: {e.Message}"; }
    }

    /// <summary>¿La app de delante tiene el botón «Subir un nivel»? Solo el explorador lo tiene.</summary>
    private static bool ExisteBotonSubir()
    {
        try
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return false;
            var raiz = System.Windows.Automation.AutomationElement.FromHandle(fg);
            return raiz?.FindFirst(System.Windows.Automation.TreeScope.Descendants,
                new System.Windows.Automation.PropertyCondition(
                    System.Windows.Automation.AutomationElement.AutomationIdProperty, "upButton")) != null;
        }
        catch { return false; }
    }

    private List<string> SeleccionActual()
    {
        var sel = new List<string>();
        try
        {
            // Consulta DIRIGIDA, no una lectura del árbol entero. Saber qué hay marcado solo
            // necesita los elementos de lista seleccionados, y UIA sabe pedirlos: una condición
            // compuesta (ListItem AND IsSelected) los resuelve de una vez. Recorrer todo el lector
            // —ventanas hijas, menús, cientos de elementos, un viaje entre procesos por cada uno—
            // costaba ~3 s por llamada, y esto se consulta en cada acción (2026-08-02).
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return sel;
            var raiz = System.Windows.Automation.AutomationElement.FromHandle(fg);
            if (raiz == null) return sel;

            var cond = new System.Windows.Automation.AndCondition(
                new System.Windows.Automation.PropertyCondition(
                    System.Windows.Automation.AutomationElement.ControlTypeProperty,
                    System.Windows.Automation.ControlType.ListItem),
                new System.Windows.Automation.PropertyCondition(
                    System.Windows.Automation.SelectionItemPattern.IsSelectedProperty, true));

            foreach (System.Windows.Automation.AutomationElement el
                     in raiz.FindAll(System.Windows.Automation.TreeScope.Descendants, cond))
            {
                try
                {
                    string n = el.Current.Name?.Trim() ?? "";
                    if (n.Length > 0 && !sel.Contains(n)) sel.Add(n);
                }
                catch { }
            }
        }
        catch { }
        return sel;
    }

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
    private bool EsperarMenu(int msMax, int habiaAntes)
    {
        for (int i = 0; i < msMax / 70; i++)
        {
            if (CuantosMenus() > habiaAntes) return true;
            System.Threading.Thread.Sleep(70);
        }
        return false;
    }

    /// <summary>Cuántos elementos de menú hay ahora en la ventana de delante.</summary>
    private static int CuantosMenus()
    {
        try
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return 0;
            var raiz = System.Windows.Automation.AutomationElement.FromHandle(fg);
            if (raiz == null) return 0;
            return raiz.FindAll(System.Windows.Automation.TreeScope.Descendants,
                new System.Windows.Automation.PropertyCondition(
                    System.Windows.Automation.AutomationElement.ControlTypeProperty,
                    System.Windows.Automation.ControlType.MenuItem)).Count;
        }
        catch { return 0; }
    }

    private void ObservarMenus(string nodo)
    {
        try
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return;
            var raiz = System.Windows.Automation.AutomationElement.FromHandle(fg);
            if (raiz == null) return;

            var puertas = new List<(string, string, string, string[], string)>();
            foreach (System.Windows.Automation.AutomationElement el in raiz.FindAll(
                System.Windows.Automation.TreeScope.Descendants,
                new System.Windows.Automation.PropertyCondition(
                    System.Windows.Automation.AutomationElement.ControlTypeProperty,
                    System.Windows.Automation.ControlType.MenuItem)))
            {
                try
                {
                    var info = el.Current;
                    if (info.IsOffscreen) continue;
                    string n = info.Name?.Trim() ?? "";
                    if (n.Length == 0) continue;
                    puertas.Add((n, "MenuItem", $"uia:name={n};ct=MenuItem", Array.Empty<string>(), "menú"));
                }
                catch { }
            }
            if (puertas.Count == 0) return;
            _map.ObserveExits(nodo, puertas);
            LogBus.Log("mapa-mcp", $"menú abierto: {puertas.Count} opción(es) anotadas");
        }
        catch { }
    }

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
    private static bool PuedeAbrirMenu(string etiqueta)
    {
        var palabras = Uia.Reconocedor.Normalizar(etiqueta).Split(' ',
            StringSplitOptions.RemoveEmptyEntries);
        if (palabras.Length == 0) return false;

        bool inequivoca = palabras.Any(p => p is "nuevo" or "ordenar" or "opciones" or "compartir");
        bool ambigua = palabras.Any(p => p is "mas" or "ver");
        return inequivoca || (ambigua && palabras.Length > 1);
    }

    /// <summary>Acciones que operan sobre lo seleccionado: antes de ejecutarlas hay que saber qué es.</summary>
    private static bool OperaSobreLaSeleccion(string etiqueta) =>
        etiqueta.Contains("Cortar", StringComparison.OrdinalIgnoreCase)
        || etiqueta.Contains("Copiar", StringComparison.OrdinalIgnoreCase)
        || etiqueta.Contains("Eliminar", StringComparison.OrdinalIgnoreCase)
        || etiqueta.Contains("Cambiar nombre", StringComparison.OrdinalIgnoreCase);

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
    private string ComprobarUbicacion(string esperada)
    {
        if (esperada.Length == 0) return "";   // no lo declaró: se actúa como antes

        string app = AppDe(esperada);
        if (app.Length > 0) { _ultimaApp = app; AsegurarFoco(app); }

        // Se ESPERA a que la superficie se asiente antes de negarse. Justo después de una acción la
        // pantalla pasa por estados intermedios —al crear una carpeta el foco va un instante a la
        // ventana emergente del menú— y rechazar en el primer desacuerdo bloqueaba el paso
        // siguiente en 11 ms, con la carpeta quedándose como «Nueva carpeta» (2026-08-03). La
        // garantía no cambia: si al cabo de un segundo seguimos en otro sitio, no se actúa.
        string aqui = "";
        for (int i = 0; i < 25; i++)
        {
            aqui = _where()?.Id ?? "";
            if (string.Equals(aqui, esperada, StringComparison.OrdinalIgnoreCase)) return "";
            // UN MENÚ ABIERTO NO ES OTRO SITIO: es una capa sobre el mismo. Mientras está
            // desplegado, la superficie en foco es su ventana emergente, y tomarla por una
            // ubicación distinta hacía que el ancla rechazara elegir la opción del menú que
            // acabábamos de abrir (2026-08-03). Se acepta si pertenece a la misma app.
            if (EsCapaSobreLaPantalla(aqui, esperada)) return "";
            System.Threading.Thread.Sleep(45);   // el caso normal acierta a la primera; el resto, pronto
        }

        LogBus.Log("mapa-mcp", $"NO SE ACTÚA: se esperaba estar en '{esperada}' y estamos en '{aqui}'");

        // Si lo que bloquea es un diálogo, decir CUÁL. «No estás donde creías» obliga a investigar;
        // «hay este diálogo delante, con estas opciones» se puede resolver en el acto. Un rechazo
        // honesto que además explica la causa es la diferencia entre pararse y poder continuar.
        // UN AVISO CON UNA SOLA SALIDA NO ES UNA DECISIÓN: es un trámite. Cuando el diálogo no
        // ofrece elección, escalarlo al consciente no aporta nada y sí mata la tarea — se detectó
        // «Ubicación no disponible», se describió correctamente, y como nadie lo cerró las 27
        // acciones siguientes se rechazaron una tras otra (2026-08-03). En cuanto hay DOS opciones
        // sigue siendo del consciente, que es la regla que ya teníamos: aquí no se elige nada.
        var (_, _, opcionesAhora) = LeerInterrupcion();
        if (!_reanudando && opcionesAhora.Count > 0 && OpcionSegura(opcionesAhora).Length > 0)
        {
            _reanudando = true;
            try
            {
                LogBus.Log("mapa-mcp", "aviso de una sola salida delante: se cierra y se reanuda");
                Unblock(esperada, "");
                string tras = _where()?.Id ?? "";
                if (string.Equals(tras, esperada, StringComparison.OrdinalIgnoreCase))
                {
                    LogBus.Log("mapa-mcp", $"✓ reanudado tras el aviso: de vuelta en «{esperada}»");
                    return "";
                }
            }
            catch (Exception e) { LogBus.Log("mapa-mcp", $"al cerrar el aviso: {e.Message}"); }
            finally { _reanudando = false; }
        }

        string interrupcion = DescribirInterrupcion();
        if (interrupcion.Length > 0)
            return $"NO actúo: creías estar en «{esperada}» y lo que hay delante es otra cosa.\n{interrupcion}";

        // RESOLVER UN BLOQUEO Y REANUDAR LA TAREA SON DOS COSAS DISTINTAS, y solo estaba la primera.
        // Medido el 2026-08-03: apareció «Ubicación no disponible», se detectó, se pulsó «Aceptar»
        // correctamente… y el explorador quedó en «Notas». A partir de ahí el ancla rechazó 27
        // acciones seguidas —bien, cero daño— pero la tarea murió ahí mismo. Estar desplazado dentro
        // de la MISMA app y sin nada delante no es motivo para abandonar: es motivo para volver.
        //
        // Volver es navegación por el mapa, no la acción pedida: se usan rutas ya conocidas, se
        // COMPRUEBA la llegada, y si no se llega se rechaza igual que antes. El rechazo sigue siendo
        // la red; deja de ser lo único.
        if (!_reanudando && aqui.Length > 0 && SurfaceMap.MismaApp(aqui, esperada))
        {
            _reanudando = true;
            try
            {
                LogBus.Log("mapa-mcp", $"desplazados a «{aqui}»; se intenta volver a «{esperada}» por el mapa");
                GoTo(esperada);
                string tras = _where()?.Id ?? "";
                if (string.Equals(tras, esperada, StringComparison.OrdinalIgnoreCase))
                {
                    LogBus.Log("mapa-mcp", $"✓ reanudado: de vuelta en «{esperada}», la tarea sigue");
                    return "";
                }
                LogBus.Log("mapa-mcp", $"no se pudo volver a «{esperada}»; seguimos en «{tras}»");
            }
            catch (Exception e) { LogBus.Log("mapa-mcp", $"al intentar volver: {e.Message}"); }
            finally { _reanudando = false; }
        }

        return $"NO actúo: creías estar en «{esperada}» pero estamos en «{aqui}», y no he sabido volver. "
             + "Algo salió distinto en un paso anterior; comprueba dónde estás antes de seguir.";
    }

    /// <summary>
    /// ¿Lo que hay delante es una CAPA sobre la pantalla esperada —un menú, un desplegable— y no
    /// otro sitio? Se exige que sea de la MISMA app: una ventana emergente de otro programa sí es
    /// irse a otra parte, y ahí el ancla debe seguir negándose.
    /// </summary>
    private static bool EsCapaSobreLaPantalla(string aqui, string esperada)
    {
        if (aqui.Length == 0) return false;
        if (!AppDe(aqui).Equals(AppDe(esperada), StringComparison.OrdinalIgnoreCase)) return false;
        return aqui.Contains("ventanas-emergentes", StringComparison.OrdinalIgnoreCase)
            || aqui.Contains("popup", StringComparison.OrdinalIgnoreCase)
            || aqui.EndsWith("/ventana", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>El sistema y el localizador dicen los dos que estamos en esta app.</summary>
    private bool Coinciden(string app) =>
        AppEnFrente().Equals(app, StringComparison.OrdinalIgnoreCase)
        && AppDe(_where()?.Id ?? "").Equals(app, StringComparison.OrdinalIgnoreCase);

    private bool EsperarCoincidencia(string app)
    {
        for (int i = 0; i < 20; i++)
        {
            if (Coinciden(app)) return true;
            System.Threading.Thread.Sleep(150);
        }
        return false;
    }

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

    public SurfaceMapTools(SurfaceMap map, Func<SurfaceLocator.SurfaceLocation?> where)
    {
        _map = map;
        _where = where;
    }

    public static bool IsMapTool(string tool) => tool is
        "map_where_am_i" or "map_places" or "map_routes_from" or "map_go_to" or "map_take"
        or "map_type" or "map_unblock" or "map_run" or "map_learn_app" or "map_open_app"
        or "map_set_level" or "map_what_i_see" or "map_pointing_at" or "map_show"
        or "map_pointed_trail" or "map_exclude"
        or "map_hierarchy" or "map_feedback" or "map_unsituated" or "map_learn_back" or "map_shot"
        or "map_set_kind"
        or "file_where" or "file_list" or "file_open" or "file_find";

    public string Call(string tool, IReadOnlyDictionary<string, string> args)
    {
        string A(string k) => args.TryGetValue(k, out var v) ? v.Trim() : "";

        // Se registra CADA llamada y su respuesta. Sin esto, «el mapa no aportó nada» y «el modelo
        // ni lo intentó» se ven exactamente igual en el log — y esa ambigüedad me llevó a un
        // diagnóstico equivocado el 2026-07-31, buscando en el mapa un fallo que estaba en el
        // lanzador de apps.
        string args_ = string.Join(" ", args.Select(kv => $"{kv.Key}={kv.Value}"));
        LogBus.Log("mapa-mcp", $"→ {tool} {args_}".TrimEnd());

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
            "map_places" => Places(A("app")),
            "map_routes_from" => Routes(A("surface")),
            "map_go_to" => GoTo(A("surface")),
            "map_take" => Take(A("exit"), A("action"), A("at")),
            "map_type" => Type(A("text"), A("target"), A("at")),
            "map_unblock" => Unblock(A("at"), A("choose")),
            "map_open_app" => OpenApp(A("app")),
            "map_what_i_see" => LoQueVeo(),
            "map_pointing_at" => LoQueSenala(),
            "map_pointed_trail" => LoQueMeAcabasDeMostrar(A("seconds")),
            "map_exclude" => Excluir(A("exit")),
            "map_show" => Mostrar(A("exit")),
            // SE MIRA ANTES DE FIJAR. Fijar un nivel busca la SALIDA con ese nombre en el mapa, y
            // el mapa solo anota cuando se le pide: un elemento perfectamente visible —con su punto
            // gris encima— podía no estar registrado todavía, y la respuesta era «no lo veo en la
            // pantalla», que además es falsa. El usuario lo tenía delante (2026-08-07). Es el mismo
            // arreglo que ya necesitó el maestro: primero se anota lo que hay, luego se juzga.
            "map_set_level" => FijarNivelMirandoAntes(
                A("app").Length > 0 ? A("app") : SurfaceMap.AppDe(_where()?.Id ?? ""),
                A("exit"),
                int.TryParse(A("level"), out int niv) ? niv : -1,
                bool.TryParse(A("cromo"), out bool crm) ? crm : null),
            "map_learn_app" => LearnApp(A("app")),
            "map_run" => Run(A("steps")),
            "map_unsituated" => SinSituar(A("app").Length > 0 ? A("app") : SurfaceMap.AppDe(_where()?.Id ?? "")),
            "map_learn_back" => AprenderGestoAtras(A("app").Length > 0 ? A("app") : SurfaceMap.AppDe(_where()?.Id ?? ""), A("exit")),
            "map_shot" => Foto(),
            "map_set_kind" => Clasificar(A("app").Length > 0 ? A("app") : SurfaceMap.AppDe(_where()?.Id ?? ""), A("exit"), A("kind")),
            "map_hierarchy" => Jerarquia(A("app").Length > 0 ? A("app") : SurfaceMap.AppDe(_where()?.Id ?? "")),
            "map_feedback" => Feedback(A("app").Length > 0 ? A("app") : SurfaceMap.AppDe(_where()?.Id ?? ""), A("finding")),

            // Los verbos del explorador. Van por disco, no por pantalla: ver Explorador.cs.
            "file_where" => DondeEnDisco(),
            "file_list" => SystemApi.Explorador.Describir(SystemApi.Explorador.Expandir(A("path")), A("filter")),
            "file_open" => AbrirCarpeta(A("path")),
            "file_find" => BuscarEnDisco(A("query"), A("path")),

            _ => $"herramienta de mapa no soportada: {tool}",
        };

        LogBus.Log("mapa-mcp", "← " + (r.Length > 200 ? r[..200] + "…" : r).Replace("\n", " | "));
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

        // UN DIÁLOGO NO ES UN LUGAR: es una interrupción. Se comprueba ANTES de describir salidas
        // porque, si lo hay, todo lo demás es ruido — no hay «rutas desde aquí», hay una pregunta
        // que responder para poder seguir.
        string interrupcion = DescribirInterrupcion();
        if (interrupcion.Length > 0) return interrupcion;

        ObservarAqui(loc.Id);
        var salidas = _map.ExitsFrom(loc.Id);
        int recorribles = salidas.Count(h => h.Info.Selector.Length > 0);
        var sel = SeleccionActual();
        return $"Estás en «{loc.Id}». Desde aquí el mapa conoce {salidas.Count} salida(s), "
             + $"{recorribles} de ellas recorribles."
             + (sel.Count > 0 ? $" Seleccionado ahora mismo: {string.Join(", ", sel.Select(s => $"«{s}»"))}." : "")
             + (DestinoDeAtras().Length > 0 ? $" «Atrás» llevaría a «{DestinoDeAtras()}»." : "");
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
    private string Run(string pasosJson)
    {
        if (pasosJson.Length == 0) return "falta `steps`: la lista de pasos a ejecutar";

        List<Dictionary<string, string>> pasos;
        try
        {
            pasos = new List<Dictionary<string, string>>();
            using var doc = System.Text.Json.JsonDocument.Parse(pasosJson);
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in el.EnumerateObject())
                    d[p.Name] = p.Value.ValueKind == System.Text.Json.JsonValueKind.String
                        ? p.Value.GetString() ?? "" : p.Value.ToString();
                pasos.Add(d);
            }
        }
        catch (Exception e) { return $"no entendí `steps`: {e.Message}"; }

        var informe = new System.Text.StringBuilder();
        var reloj = System.Diagnostics.Stopwatch.StartNew();
        int hechos = 0;

        foreach (var p in pasos)
        {
            string op = p.TryGetValue("op", out var o) ? o.Trim().ToLowerInvariant() : "";
            string V(string k) => p.TryGetValue(k, out var v) ? v.Trim() : "";
            long t0 = reloj.ElapsedMilliseconds;

            string r = op switch
            {
                "go_to" => GoTo(V("surface")),
                "take" => Take(V("exit"), V("action"), V("at")),
                "type" => Type(V("text"), V("target"), V("at")),
                "unblock" => Unblock(V("at"), V("choose")),
                _ => $"paso desconocido: «{op}»",
            };
            long ms = reloj.ElapsedMilliseconds - t0;
            hechos++;

            bool mal = r.StartsWith("NO actúo", StringComparison.Ordinal)
                    || r.StartsWith("no ", StringComparison.OrdinalIgnoreCase)
                    || r.StartsWith("ATASCADO", StringComparison.Ordinal)
                    || r.Contains("pero no se llegó", StringComparison.OrdinalIgnoreCase)
                    || r.Contains("paso desconocido", StringComparison.Ordinal);

            informe.AppendLine($"  {hechos,2}. [{ms,5} ms] {op} {V("exit")}{V("surface")}{V("text")} → {Recortar(r, 90)}");
            if (mal)
            {
                reloj.Stop();
                LogBus.Log("mapa-mcp", $"PLAN detenido en el paso {hechos}/{pasos.Count}");
                return $"PLAN DETENIDO en el paso {hechos} de {pasos.Count} ({reloj.ElapsedMilliseconds} ms).\n"
                     + informe.ToString()
                     + "  No sigo tras un fallo: continuar es cómo un error se vuelve daño más adelante.";
            }
        }

        reloj.Stop();
        LogBus.Log("mapa-mcp", $"PLAN completo: {hechos} paso(s) en {reloj.ElapsedMilliseconds} ms");
        return $"PLAN COMPLETO: {hechos} paso(s) en {reloj.ElapsedMilliseconds} ms, una sola consulta.\n"
             + informe.ToString();
    }

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
    private string Unblock(string reanudarEn, string eleccion)
    {
        var (titulo, textos, opciones) = LeerInterrupcion();
        if (opciones.Count == 0)
            return "no hay nada que desbloquear: no veo ningún diálogo delante.";

        string elegida = eleccion.Length > 0
            ? opciones.FirstOrDefault(o => o.Equals(eleccion, StringComparison.OrdinalIgnoreCase)) ?? ""
            : OpcionSegura(opciones);

        if (elegida.Length == 0)
            return $"ATASCADO · decide tú, esto no lo automatizo.\n"
                 + $"  Diálogo: «{titulo}»\n"
                 + $"  Dice: {string.Join(" ", textos.Take(3))}\n"
                 + $"  Opciones: {string.Join(", ", opciones.Select(o => $"«{o}»"))}\n"
                 + (_ultimaAccion.Length > 0 ? $"  Veníamos de: {_ultimaAccion}\n" : "")
                 + "  Hay una DECISIÓN aquí, y depende de lo que estuvieras intentando: continuar o "
                 + "no es tuyo, no mío. Vuelve a llamarme con `choose` indicando la opción.";

        // El veto, ahora sobre el VERBO y no sobre el tipo de control: responder a un diálogo es
        // legítimo —para eso está—, lo que no lo es es responder «Eliminar». Aunque lo pida la
        // capa consciente: esa decisión es del usuario.
        if (SafeToClick.EsDestructivo(elegida, out string motivo))
            return $"NO pulso «{elegida}»: {motivo}. Una opción destructiva la confirma el usuario, no yo.";

        var paso = new PlanStep
        {
            StepOrder = 1, ActionType = "click",
            Selector = $"uia:name={elegida};ct=Button", Label = elegida,
        };
        if (!_uia.Execute(paso, out string error))
            return $"no pude pulsar «{elegida}» para salir del atasco: {error}";

        // ¿Se fue de verdad? Un desbloqueo que no desbloquea es peor que no intentarlo.
        bool libre = false;
        for (int i = 0; i < 20; i++)
        {
            System.Threading.Thread.Sleep(120);
            if (LeerInterrupcion().Opciones.Count == 0) { libre = true; break; }
        }
        LogBus.Log("mapa-mcp", $"DESBLOQUEO: «{titulo}» → pulsado «{elegida}» · {(libre ? "resuelto" : "sigue ahí")}");
        if (!libre)
            return $"pulsé «{elegida}» y el diálogo «{titulo}» sigue delante. No insisto sola: dime qué hacer.";

        // Reanudar por el mapa, que es de lo que se trata: salir del atasco no sirve de nada si la
        // tarea no puede continuar desde donde estaba.
        string vuelta = reanudarEn.Length > 0 ? GoTo(reanudarEn) : "";
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
    private static string OpcionSegura(List<string> opciones)
    {
        var reales = opciones.Where(o => !EsSalidaDeVentana(o)).ToList();
        return reales.Count == 1 ? reales[0] : "";
    }

    /// <summary>¿Esta «opción» es en realidad el cierre de la ventana y no una respuesta?</summary>
    private static bool EsSalidaDeVentana(string etiqueta) =>
        etiqueta.Equals("Cerrar", StringComparison.OrdinalIgnoreCase)
        || etiqueta.Equals("Close", StringComparison.OrdinalIgnoreCase)
        || etiqueta.Equals("Minimizar", StringComparison.OrdinalIgnoreCase)
        || etiqueta.Equals("Maximizar", StringComparison.OrdinalIgnoreCase);

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
        string d = Interrupcion.Describir();
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
        => Interrupcion.Leer();

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

    /// <summary>
    /// Registra las salidas de la pantalla actual, si aún no se conocen.
    ///
    /// El mapa solo se llenaba durante un recorrido automático, así que el asistente podía LLEGAR
    /// a un sitio nuevo por MCP y quedarse ciego allí: «0 salidas conocidas» estando delante de
    /// una carpeta llena de cosas (2026-08-02). Preguntar dónde estoy es el momento natural para
    /// mirar alrededor — el terreno se aprende viviendo, no solo explorando a propósito.
    /// </summary>
    /// <summary>
    /// Mira la pantalla y anota sus puertas. SIEMPRE.
    /// </summary>
    /// <remarks>
    /// Aquí había un guardia que se saltaba la relectura cuando la pantalla «ya se conocía»: seis
    /// salidas recorribles y tres acciones bastaban para darla por sabida. Ahorraba una lectura del
    /// árbol de UI y costaba dos cosas, las dos malas:
    ///
    /// · Lo que aparecía DESPUÉS no entraba nunca. Una carpeta recién creada, un botón que sale al
    ///   seleccionar algo, se quedaban fuera del mapa aunque estuvieran delante — es el «a veces
    ///   verde y a veces gris» que se venía notando en los puntos del explorador.
    /// · Y desde que el grafo se arma con lo VISIBLE, sin volver a mirar no hay forma de saber qué
    ///   dejó de estar: una puerta que ya no está seguiría ofreciéndose como si estuviera.
    ///
    /// Una pantalla no se conoce de una vez: cambia mientras se usa. Si el grafo es lo que se ve,
    /// hay que mirar (2026-08-05).
    /// </remarks>
    private void ObservarAqui(string nodo, bool forzar = false)
    {
        try { ObservarSinGuardia(nodo); }
        catch { }
    }

    /// <summary>
    /// Relee la pantalla y anota lo que haya, sin preguntarse si ya se conocía.
    ///
    /// Hace falta después de EJECUTAR una acción: un menú abierto no es una pantalla nueva —la
    /// superficie sigue siendo la misma— así que el guardia de «esto ya se conoce» impedía ver los
    /// elementos que acababan de aparecer. Se pulsaba «Nuevo», el menú se abría con «Carpeta»
    /// dentro, y el asistente seguía viendo la lista de antes (2026-08-02).
    /// </summary>
    private void ObservarSinGuardia(string nodo)
    {
        try
        {
            _lector.Read();

            // LO LEÍDO Y EL DÓNDE TIENEN QUE SER LA MISMA APP. El lector mira la ventana en primer
            // plano y el nodo viene del localizador; entre las dos cosas la ventana puede cambiar, y
            // entonces se le escriben a una pantalla las salidas de otra. Comprobado el 2026-08-04:
            // el nodo del Bloc de notas acabó con «Crear PR» y «Editado GraphExplorerWindow.cs»
            // dentro, que son de la ventana de Claude. Es el mismo veneno que las aristas entre apps
            // —una pantalla afirmando salidas que no tiene— y llevaba aquí desde el principio, solo
            // que nadie lo había mirado.
            string appLeida = _lector.ForegroundProcess;
            string appNodo = SurfaceMap.AppDe(nodo);

            // UNA WEB PERTENECE A SU DOMINIO, PERO QUIEN LA DIBUJA ES EL NAVEGADOR. Comparar
            // nombres declaraba distinta a toda página web —«se leyó chrome y el nodo es
            // web://…»— así que en una web no se podía anotar nada, y por eso señalar un elemento
            // funcionaba (lee la pantalla) pero fijarle el nivel no (busca en el mapa, que seguía
            // vacío) (2026-08-07, observado por el usuario). Para lo web, la coincidencia se
            // comprueba por IDENTIDAD: que el localizador siga diciendo que estamos en ese nodo.
            bool coincide = appNodo.Length == 0 || appLeida.Length == 0
                || appNodo.StartsWith(appLeida + ".", StringComparison.OrdinalIgnoreCase)
                || (nodo.StartsWith("web://", StringComparison.OrdinalIgnoreCase)
                    && EsNavegador(appLeida)
                    && string.Equals(_where()?.Id ?? "", nodo, StringComparison.OrdinalIgnoreCase));
            if (!coincide)
            {
                LogBus.Log("mapa-mcp", $"NO se anotan salidas: se leyó «{appLeida}» y el nodo es «{nodo}»");
                return;
            }

            var puertas = new List<(string, string, string, string[], string)>();
            foreach (var el in _lector.Elements)
            {
                // Igual que el crawler: TODO lo accionable, también los botones de ejecución.
                if (el.ControlType.Equals("text", StringComparison.OrdinalIgnoreCase)
                    || el.ControlType.Equals("image", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    var (l, t, sels) = UiaSurface.DescribeElement(el.Native);
                    var utiles = sels.Where(s => !s.Contains("path=", StringComparison.Ordinal)
                        && !System.Text.RegularExpressions.Regex.IsMatch(s, @"(name|aid)=(;|$)")).ToArray();
                    if (utiles.Length == 0) continue;
                    puertas.Add((l.Length > 0 ? l : el.Label, t.Length > 0 ? t : el.ControlType,
                                 utiles[0], utiles.Skip(1).ToArray(), U.Graph.Surfaces.UiaSurface.GrupoDe(el.Native)));
                }
                catch { }
            }
            if (puertas.Count == 0) return;

            // Cuántas salidas quedaron sin grupo, y por dónde iba el árbol en una de ellas. Sin
            // esto, «este elemento no pertenece a ningún grupo» y «no supimos ver el suyo» se leen
            // igual, que es exactamente lo que costó ver aquí (2026-08-04).
            int sinGrupo = puertas.Count(p => p.Item5.Length == 0);
            if (sinGrupo > 0)
            {
                var muestra = _lector.Elements.FirstOrDefault(e => e.Label.Length > 0);
                LogBus.Log("mapa-mcp", $"grupos: {puertas.Count - sinGrupo}/{puertas.Count} con grupo"
                    + (muestra != null ? $" · ejemplo «{muestra.Label}»: {UiaSurface.Ancestros(muestra.Native)}" : ""));
            }

            _map.ObserveExits(nodo, puertas);
            LogBus.Log("mapa-mcp", $"al llegar a '{nodo}' se anotaron {puertas.Count} salida(s)");
        }
        catch { }
    }

    /// <summary>
    /// Las superficies conocidas, agrupadas por app y ordenadas por frecuencia — lo más visitado
    /// primero, que es lo que un humano llamaría "los sitios donde trabajo".
    /// </summary>
    /// <summary>
    /// LO QUE EL MAPA NO SABE SITUAR: la lista de trabajo del arquitecto.
    ///
    /// Es la otra cara de <see cref="Jerarquia"/>. El dibujo fiel al mapa manda a una fila aparte
    /// todo lo que nadie ha situado, y ese número es la medida honesta de cuánto falta para que la
    /// estructura esté completa. Aquí se enumera, para poder atacarlo uno a uno en vez de mirar un
    /// contador (2026-08-08).
    ///
    /// Un sitio no situado no es basura por definición: puede ser una pantalla real a la que aún
    /// nadie le ha puesto nivel, o una puerta que no debió anotarse nunca. Distinguirlo es
    /// justamente el trabajo, y por eso se dan las dos cosas que permiten decidir: el grupo que
    /// declaró la página y desde dónde se ve.
    /// </summary>
    private string SinSituar(string app)
    {
        if (app.Length == 0) return "falta `app`";
        bool DeLaApp(string id) => SurfaceMap.AppDe(id).Equals(app, StringComparison.OrdinalIgnoreCase);

        var pantallas = _map.Nodes.Where(kv => DeLaApp(kv.Key) && kv.Value.Nivel < 0)
            .OrderByDescending(kv => kv.Value.Visits).ToList();
        // LO CLASIFICADO YA NO ESTÁ PENDIENTE. Aquí solo se miraba el nivel, así que una salida
        // marcada como ACCIÓN o como gesto de VOLVER seguía saliendo en la lista para siempre: el
        // agente clasificó cuarenta y la lista no bajó ni una. Rompía su criterio de terminado y,
        // peor, lo empujaba a la única salida que quedaba —ponerle nivel a cosas que no son
        // navegación—, que es justo lo contrario de lo que se le pide (2026-08-09, lo reportó él).
        //
        // Pendiente es lo que no tiene NI nivel NI clasificación. Clasificar es decidir, y una
        // decisión tomada no puede seguir contando como trabajo por hacer.
        // PENDIENTE ES LO NO DECLARADO, no lo que carece de número. Aquí se miraba NivelNav < 0, y
        // casi ninguna salida cumple eso: al observarlas se les pone un nivel por deducción. Así
        // que esta herramienta contestó «explorer.exe está ENTERA situada» mientras jerarquia
        // reportaba, en el mismo instante, CERO salidas con nivel declarado.
        //
        // Es el peor tipo de fallo que puede tener esto y lo dijo el arquitecto con precisión:
        // «corrompe el juicio, no el dato». Un agente que se fía cierra la app en BRONCE creyendo
        // que llegó a PLATA. Las dos herramientas tienen que medir lo MISMO: lo declarado
        // (2026-08-09, hallazgo nº1 de su auditoría).
        // Y EL CONTENIDO TAMPOCO ES ESTRUCTURA PENDIENTE. Sin esto la lista CRECÍA al trabajar
        // bien: cada carpeta que el arquitecto abría volcaba sus archivos a los pendientes, y en un
        // explorador el contenido es infinito. Lo midió él: pasó de 2 pendientes a 4 pantallas + 40
        // salidas «por hacer bien el trabajo de bajar en profundidad» (2026-08-10).
        //
        // Una lista de tareas que se alarga cuanto más trabajas no es una lista de tareas. Y el
        // criterio de «qué es contenido» ya existía en SafeToClick — solo faltaba usarlo aquí.
        var puertas = _map.Edges()
            .Where(e => DeLaApp(e.From) && !e.Info.NivelFijado && e.Info.Label.Length > 0
                        && !e.Info.KindDeclarado.Equals("accion", StringComparison.OrdinalIgnoreCase)
                        && !e.Info.Nivel.StartsWith("contenido", StringComparison.OrdinalIgnoreCase)
                        && !e.Info.Nivel.StartsWith("lista", StringComparison.OrdinalIgnoreCase)
                        && !_map.EsGestoDeAtras(app, e.Info.Label, e.Info.Selector))
            // POR SELECTOR, NO POR ETIQUETA. «Actualizar "Inicio" (F5)», «Actualizar "Galería"
            // (F5)», «Actualizar "Common Files" (F5)»… son UN botón cuyo nombre lleva interpolada
            // la carpeta actual, y contaban como cinco pendientes distintos; con cincuenta carpetas
            // visitadas serían cincuenta. Ya compartían uia:aid=refreshButton;ct=Button — la
            // identidad estaba ahí, solo se estaba mirando el nombre (2026-08-10, lo reportó él).
            .GroupBy(e => e.Info.Selector.Length > 0 ? e.Info.Selector : e.Info.Label,
                     StringComparer.Ordinal)
            .OrderByDescending(g => g.Count()).Take(40).ToList();

        if (pantallas.Count == 0 && puertas.Count == 0)
            return $"«{app}» está ENTERA situada: no queda nada sin nivel. Eso es la meta.";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"SIN SITUAR en «{app}» — esto es lo que falta para completar la estructura:\n");
        sb.AppendLine($"PANTALLAS sin nivel ({pantallas.Count}):");
        foreach (var (id, n) in pantallas.Take(25))
            sb.AppendLine($"  {id} · {n.Visits} visita(s)");
        sb.AppendLine($"\nSALIDAS sin nivel ({puertas.Count}) — con el grupo que declaró la página:");
        foreach (var g in puertas)
        {
            var i = g.First().Info;
            // Se agrupa por selector pero se DICE el nombre: agrupar por identidad no puede
            // convertir la respuesta en una lista de selectores ilegibles. Y cuando el nombre varía
            // entre apariciones —los «Actualizar "X"»— se avisa, porque si no, pedir la salida por
            // ese nombre solo acertaría en una de ellas.
            var nombres = g.Select(x => x.Info.Label).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            sb.AppendLine($"  «{i.Label}» ({i.ControlType})"
                + (nombres.Count > 1 ? $" · OJO: el nombre cambia según la pantalla ({nombres.Count} variantes); "
                                       + $"clasifícala por su selector {i.Selector}" : "")
                + (i.Nivel.Length > 0 ? $" · grupo: {i.Nivel}" : " · sin grupo")
                + $" · vista en {g.Count()} pantalla(s)");
        }
        return sb.ToString();
    }

    /// <summary>
    /// CLASIFICAR una salida, no borrarla. Marcarla como «accion» dice que hace algo pero no lleva
    /// a otra pantalla: guardar, ordenar, copiar, crear.
    ///
    /// Antes esto era «excluir», y el usuario lo paró a tiempo: quitar del mapa lo que no es
    /// navegación deja sin brazos al asistente que vendrá después — esos botones son justo los que
    /// necesitará para EJECUTAR (2026-08-08). Una salida puede no ser estructura y seguir siendo
    /// imprescindible. Así que se queda con todo lo suyo —selector, alternativas, dónde vive— y
    /// solo se le pone la etiqueta que dice de qué sirve.
    ///
    /// El campo <see cref="SurfaceMap.EdgeInfo.Kind"/> ya existía para esto y estaba sin usar: no
    /// hace falta estructura nueva, hacía falta que alguien lo dijera.
    /// </summary>
    /// <remarks>
    /// Clasificar es DECIDIR, y una decisión se toma una vez. Esto escribía solo en las apariciones
    /// que existían en ese instante, así que cada pantalla nueva devolvía el mismo mobiliario a la
    /// lista de pendientes: el arquitecto marcó «Nuevo» ocho veces y al entrar en OneDrive le
    /// reaparecieron 27 controles ya clasificados (2026-08-10). Ahora vive donde vive el nivel —en
    /// la enseñanza, indexada por selector— y se repone sola en cada aparición nueva.
    /// </remarks>
    private string Clasificar(string app, string salida, string clase)
    {
        if (salida.Length == 0) return "falta `exit`: qué salida quieres clasificar";
        string k = clase.Length > 0 ? clase.Trim().ToLowerInvariant() : "accion";

        string r = _map.ClasificarSalida(app, salida, k);
        LogBus.Log("mapa-mcp", $"«{salida}» clasificada como «{k}»: sigue en el mapa para "
            + "ejecutarla, deja de contar como estructura, y queda aprendida para toda la app");
        return r;
    }

    /// <summary>
    /// Enseñar cuál es el gesto de VOLVER de esta app. No es una puerta: es historial.
    ///
    /// Una arista dice «desde aquí se llega allí», y el atrás no cumple eso — te devuelve a donde
    /// vinieras, que depende del camino y no de la estructura. Sin marcarlo, cada vuelta acuña una
    /// arista falsa y el grafo acaba lleno de caminos que no existen.
    /// </summary>
    private string AprenderGestoAtras(string app, string salida)
    {
        if (app.Length == 0 || salida.Length == 0) return "faltan `app` y `exit`";
        _map.AprenderAtras(app, salida, humano: false);
        return $"«{salida}» queda marcado como el gesto de volver de «{app}»: dejará de acuñar aristas.";
    }

    /// <summary>
    /// UNA FOTO DE LA VENTANA DE DELANTE, en base64. Para que quien decide la estructura pueda
    /// MIRAR y no solo leer nombres: un panel lateral y una lista de contenido se distinguen de un
    /// vistazo y son indistinguibles en una lista de etiquetas.
    ///
    /// Se fotografía la ventana del USUARIO, no la nuestra: quien pregunta corre en otro proceso y
    /// nuestra propia capa se pondría en medio (ver AppAligner.VentanaDelUsuario).
    /// </summary>
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

    /// <summary>
    /// LA JERARQUÍA COMO EL GRAFO LA TIENE, para poder contrastarla con la real.
    ///
    /// Existe para el agente ARQUITECTO: su misión es navegar la app, entender su jerarquía
    /// mirándola, y compararla con la que el grafo está construyendo. Esa comparación necesita ver
    /// lo mismo que el mapa cree — con su procedencia: qué nivel tiene cada cosa, quién lo dijo
    /// (persona, maestro, deducción) y qué es cromo. Sin la procedencia, el agente «corregiría»
    /// niveles que una persona acaba de fijar a mano, que es exactamente lo que no debe pasar.
    /// </summary>
    private string Jerarquia(string app)
    {
        if (app.Length == 0) return "falta `app`: de qué aplicación quieres la jerarquía";
        bool DeLaApp(string id) => SurfaceMap.AppDe(id).Equals(app, StringComparison.OrdinalIgnoreCase);

        var sb = new System.Text.StringBuilder($"JERARQUÍA de «{app}» según el grafo:\n\n");

        var nodos = _map.Nodes.Where(kv => DeLaApp(kv.Key)).OrderBy(kv => kv.Value.Nivel).ToList();
        sb.AppendLine($"PANTALLAS ({nodos.Count}):");
        foreach (var (id, n) in nodos)
            sb.AppendLine($"  nivel {(n.Nivel >= 0 ? n.Nivel.ToString() : "?")} · {id} · {n.Visits} visita(s)");

        var declaradas = _map.Edges()
            .Where(e => DeLaApp(e.From) && e.Info.NivelFijado && e.Info.NivelNav >= 0)
            .GroupBy(e => e.Info.Label, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.First().Info.NivelNav).ThenBy(g => g.Key)
            .ToList();
        sb.AppendLine($"\nSALIDAS CON NIVEL DECLARADO ({declaradas.Count}):");
        foreach (var g in declaradas)
        {
            var i = g.First().Info;
            sb.AppendLine($"  nivel {i.NivelNav} · «{g.Key}»"
                + (i.EsCromo ? " · CROMO (te sigue a todas partes)" : "")
                + (i.PorPersona ? " · lo dijo UNA PERSONA (no lo muevas sin decirlo en el feedback)" : " · lo dijo el maestro")
                + $" · vista en {g.Count()} pantalla(s)");
        }

        var sinNivel = _map.Edges()
            .Where(e => DeLaApp(e.From) && !e.Info.NivelFijado && e.Info.Label.Length > 0)
            .Select(e => e.Info.Label).Distinct(StringComparer.OrdinalIgnoreCase).Take(40).ToList();
        sb.AppendLine($"\nSALIDAS SIN NIVEL DECLARADO (muestra de {sinNivel.Count}):");
        sb.AppendLine("  " + string.Join(" · ", sinNivel));

        return sb.ToString();
    }

    /// <summary>
    /// El HALLAZGO del arquitecto queda escrito donde el desarrollo lo lee. Es su entregable: los
    /// desajustes entre la jerarquía real de la app y la que el grafo construyó no se arreglan
    /// solos —a veces el fallo es del grafo, a veces del maestro, a veces de la app— y la decisión
    /// es nuestra. El agente NO edita código: deja constancia aquí y organiza el grafo con las
    /// herramientas de niveles, nada más.
    /// </summary>
    private string Feedback(string app, string finding)
    {
        if (finding.Length == 0) return "falta `finding`: el hallazgo que quieres dejar escrito";
        try
        {
            string dir = System.IO.Path.Combine(Navigation.NucleoVersiones.Raiz, "feedback-arquitecto");
            System.IO.Directory.CreateDirectory(dir);
            string ruta = System.IO.Path.Combine(dir, $"{app.Replace(".exe", "")}.md");
            System.IO.File.AppendAllText(ruta,
                $"\n## {DateTime.Now:yyyy-MM-dd HH:mm} · núcleo {(Navigation.NucleoVersiones.Actual() is { } n ? $"v{n}" : "dev")}\n\n{finding.Trim()}\n");
            LogBus.Log("arquitecto", $"hallazgo sobre «{app}» apuntado en {ruta}");
            return $"hallazgo apuntado en {ruta}. Sigue con la exploración o cierra con un resumen.";
        }
        catch (Exception e) { return $"no pude apuntar el hallazgo: {e.Message}"; }
    }

    private string Places(string app)
    {
        var nodos = _map.Nodes.AsEnumerable();
        if (app.Length > 0)
            nodos = nodos.Where(kv => kv.Key.Contains(app, StringComparison.OrdinalIgnoreCase));

        var lista = nodos.OrderByDescending(kv => kv.Value.Visits).Take(60).ToList();
        if (lista.Count == 0) return app.Length > 0
            ? $"el mapa no conoce ninguna pantalla de «{app}» todavía"
            : "el mapa está vacío: aún no se ha observado ninguna pantalla";

        var sb = new System.Text.StringBuilder($"{lista.Count} pantalla(s) conocida(s):\n");
        foreach (var kv in lista)
            sb.AppendLine($"  {kv.Key}  ({kv.Value.Visits} visita/s)");
        return sb.ToString();
    }

    /// <summary>
    /// A dónde se puede ir desde una pantalla, y con qué. Se dice EXPLÍCITAMENTE cuáles no tienen
    /// acción: que el modelo sepa que existe un camino pero que no sabemos recorrerlo es
    /// información útil —puede pedirlo al usuario o buscar otra vía—, y ocultarlo sería fingir que
    /// el mapa es más completo de lo que es.
    /// </summary>
    private string Routes(string surface)
    {
        string desde = surface.Length > 0 ? surface : (_where()?.Id ?? "");
        if (desde.Length == 0) return "no sé desde dónde: pasa `surface` o asegúrate de que hay una app en primer plano";

        var salidas = _map.ExitsFrom(desde);
        if (salidas.Count == 0) return $"el mapa no conoce ninguna salida desde «{desde}»";

        // Navegación y ejecución separadas: son preguntas distintas («¿a dónde puedo ir?» vs
        // «¿qué puedo hacer aquí?») y mezclarlas obliga al modelo a adivinar cuál es cuál.
        // EL CONTENIDO NO SE OFRECE COMO CAMINO. En «Galería» esta lista llegó a ofrecer las 1.831
        // imágenes de la carpeta como puertas cruzables, y en «Inicio» catorce archivos sueltos. Dos
        // daños, y el segundo es el grave: la estructura real —trece anclas y tres pestañas— se
        // pierde entre el relleno, y cruzar cualquiera de ellas SACA DE LA APP, porque un .d abre el
        // editor y una miniatura el visor de fotos (2026-08-10, medido por el arquitecto).
        //
        // Se cuentan aparte en vez de callarlas del todo: que ahí hay mil imágenes es un dato útil
        // —dice que esa pantalla es un CONTENEDOR— y esconderlo sería fingir que la pantalla está
        // vacía. Lo que no se hace es ofrecerlas como si fueran navegación.
        //
        // PERO UNA PUERTA CON DESTINO CONOCIDO NO ES CONTENIDO, diga lo que diga su grupo. En un
        // explorador de archivos la CARPETA es la navegación —la única fuente de niveles 3, 4, 5…—
        // y es ListItem exactamente igual que el archivo. Al filtrar por grupo se ocultaron las 15
        // carpetas de C:\ como «contenido que no se ofrece como camino», y con ellas el techo de
        // profundidad del mapa entero (2026-08-10, medido por el arquitecto: cruzó «U-versiones»
        // desde ese bloque y SÍ abría pantalla propia). Cruzada una vez, deja de ser dudosa.
        var esContenido = salidas.Where(x =>
            (x.Info.Nivel.StartsWith("contenido", StringComparison.OrdinalIgnoreCase)
             || x.Info.Nivel.StartsWith("lista", StringComparison.OrdinalIgnoreCase))
            && SurfaceMap.EsPuerta(x.To)).ToList();

        var sb = new System.Text.StringBuilder($"Desde «{desde}»:\n");
        foreach (var h in salidas.Where(x => !x.Info.Kind.Equals("accion", StringComparison.OrdinalIgnoreCase)
                                          && !esContenido.Contains(x)))
        {
            // Se distingue lo cruzado DESDE AQUÍ de lo que está disponible porque la app lo tiene en
            // todas sus pantallas. Las dos sirven para navegar; solo una se comprobó en este sitio.
            string origen = h.Info.Nivel == SurfaceMap.NivelCromo ? "  ·  del nivel (en toda la app)" : "";

            // Sin cruzar y ausente son cosas distintas, y las dos hay que decirlas. Una puerta sin
            // cruzar se puede tomar YA —se aprende al hacerlo—; una que hoy no está en pantalla, no,
            // por mucho que el mapa la recuerde (2026-08-05).
            string estado = SurfaceMap.EsPuerta(h.To) ? "  ·  sin cruzar todavía: al tomarla se aprende" : "";
            if (!_map.SigueALaVista(desde, h.Info)) estado += "  ·  NO está en pantalla ahora";

            sb.AppendLine(h.Info.Selector.Length > 0
                ? $"  → {(SurfaceMap.EsPuerta(h.To) ? "(destino por descubrir)" : h.To)}   "
                  + $"pulsando «{h.Info.Label}»  ({h.Info.Count} vez/veces){origen}{estado}"
                : $"  → {h.To}   (observado {h.Info.Count} vez/veces, pero NO se sabe con qué acción)");
        }

        // El contenido, CONTADO y no listado: dice que esta pantalla es un contenedor sin ahogar la
        // estructura. Es la forma corta de la regla que ya está escrita en la doctrina — el
        // contenido se describe y se consulta, no se enumera.
        if (esContenido.Count > 0)
            sb.AppendLine($"\n  [CONTENIDO SIN CRUZAR: {esContenido.Count} elemento(s) en el panel de esta "
                + $"pantalla (p. ej. «{esContenido[0].Info.Label}»). No se listan uno a uno: esta pantalla "
                + "es un CONTENEDOR y enumerarlos ahogaría su estructura. Para llegar a uno concreto, usa "
                + "su buscador o filtro.\n"
                + "   AVISO: entre ellos puede haber CONTENEDORES —una carpeta abre pantalla propia y es la "
                + "única fuente de profundidad de esta app—. No hay forma de saberlo sin cruzarlos: al "
                + "hacerlo con map_take el mapa lo aprende y a partir de ahí sale como camino. Cruzar uno "
                + "que NO lo sea abrirá otra aplicación.]");

        var acciones = salidas.Where(x => x.Info.Kind.Equals("accion", StringComparison.OrdinalIgnoreCase)
                                       && x.Info.Selector.Length > 0).ToList();
        if (acciones.Count > 0)
        {
            // «Disponibles» tiene que querer decir disponibles AHORA. Una acción que el mapa
            // recuerda pero que ya no está en pantalla —las cabeceras de columna al cambiar de
            // vista, los botones que solo salen con algo seleccionado— se sigue guardando, pero
            // ofrecerla sin avisar es mandar a pulsar el vacío (2026-08-05).
            var aqui = acciones.Where(a => _map.SigueALaVista(desde, a.Info)).ToList();
            var ausentes = acciones.Where(a => !_map.SigueALaVista(desde, a.Info)).ToList();

            sb.AppendLine("Acciones disponibles aquí (se toman con map_take, no navegan):");
            sb.AppendLine("  " + string.Join(", ", aqui.Select(a => $"«{a.Info.Label}»")));
            if (ausentes.Count > 0)
                sb.AppendLine($"  Conocidas pero NO en pantalla ahora ({ausentes.Count}): "
                    + string.Join(", ", ausentes.Take(15).Select(a => $"«{a.Info.Label}»")));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Recorre la ruta. Ejecuta cada tramo y COMPRUEBA la llegada antes de seguir; si un tramo no
    /// lleva a donde debía, se detiene y lo dice. Nunca improvisa: si no hay ruta completa, no se
    /// mueve — el asistente ya tiene computer-use para lo desconocido, y mezclar ambas cosas
    /// convertiría un fallo de mapa en clics a ciegas.
    /// </summary>
    private string GoTo(string destino)
    {
        if (destino.Length == 0) return "falta `surface`: a dónde hay que ir";

        // El destino dice a qué app pertenece la tarea: si el foco se ha ido, se recupera antes de
        // calcular nada. Planificar una ruta desde el centro de notificaciones no tiene sentido.
        string app = AppDe(destino);
        if (app.Length > 0)
        {
            _ultimaApp = app;
            if (!AsegurarFoco(app))
                return $"no pude poner «{app}» en primer plano (ahora hay «{AppEnFrente()}»); no me muevo a ciegas";
        }

        var actual = _where();
        if (actual == null) return "no se pudo determinar dónde estamos ahora mismo";

        // ¿El destino es justo de donde venimos? Entonces «Atrás» es el camino más corto y seguro,
        // y lo sabemos con certeza porque lo recordamos de ESTA sesión. Si el destino es otro, no
        // se toca: pulsar Atrás «a ver si suena» es como se acabó subiendo hasta Disco local (C:).
        if (string.Equals(DestinoDeAtras(), destino, StringComparison.OrdinalIgnoreCase))
        {
            var atras = new PlanStep
            {
                StepOrder = 1, ActionType = "click",
                Selector = "uia:aid=backButton;ct=Button", Label = "Atrás",
            };
            if (_uia.Execute(atras, out _) && Llego(destino, 4000))
            {
                Anotar(actual.Id, destino);
                ObservarAqui(destino);
                LogBus.Log("mapa-mcp", $"vuelta por «Atrás» (historial de sesión) → {destino}");
                return $"volví a «{destino}» con «Atrás» (era de donde venía)";
            }
            LogBus.Log("mapa-mcp", "«Atrás» no llevó a donde el historial decía; se sigue por el mapa");
        }

        var ruta = _map.Route(actual.Id, destino);

        // Sin ruta conocida, queda el ATAJO: un elemento presente en TODAS las pantallas —el panel
        // lateral, una barra de la app— que lleva al destino desde donde sea. No hace falta haber
        // recorrido nunca este camino concreto para poder usarlo.
        if (ruta == null)
        {
            var atajo = _map.AtajoHacia(destino);
            if (atajo != null)
            {
                LogBus.Log("mapa-mcp", $"sin ruta; atajo por cromo «{atajo.Info.Label}» hacia «{destino}»");
                ruta = new List<SurfaceMap.Hop> { atajo };
            }
        }
        // NO TENER RUTA NO ES NO PODER IR, si el sitio está a un clic. Planificar exige puertas ya
        // cruzadas —a través de una incógnita no se traza un camino— así que con el mapa recién
        // nacido casi todo es «no sé llegar», aunque el destino esté ahí delante, en la barra
        // lateral. Se vio el 2026-08-05: dijo que no conocía el camino a «Música», y acto seguido
        // llegó pulsándola. Salió bien porque el modelo lo razonó, y eso es suerte, no diseño.
        //
        // Así que antes de negarse, se MIRA: si hay algo delante que se llama como el destino, se
        // toma. Tomar ya comprueba a dónde se llegó y lo aprende, así que la ruta que faltaba queda
        // hecha para la próxima vez — y entonces sí habrá plan.
        if (ruta == null)
        {
            string comoSeLlama = destino.TrimEnd('/');
            int barra = comoSeLlama.LastIndexOf('/');
            if (barra >= 0) comoSeLlama = comoSeLlama[(barra + 1)..];

            if (comoSeLlama.Length > 0)
            {
                _lector.Read();
                var aLaVista = Uia.Reconocedor.Buscar(_lector.Elements, comoSeLlama.Replace('-', ' '));
                if (aLaVista.Count == 1)
                {
                    LogBus.Log("mapa-mcp", $"sin ruta a «{destino}», pero «{aLaVista[0].Label}» está a la vista: se toma");
                    string r = Take(aLaVista[0].Label, "", actual.Id);
                    var llegamos = _where()?.Id ?? "";
                    return string.Equals(llegamos, destino, StringComparison.OrdinalIgnoreCase)
                        ? $"no tenía ruta comprobada, pero «{aLaVista[0].Label}» estaba a la vista: la tomé y "
                          + $"estamos en «{destino}». Queda aprendida para la próxima."
                        : $"no tenía ruta a «{destino}» y probé con «{aLaVista[0].Label}», que estaba a la vista. "
                          + $"Resultado: {r}";
                }
            }

            return $"no conozco una ruta COMPLETA de «{actual.Id}» a «{destino}», ni veo nada delante que "
                 + "se llame así. Dime por dónde empiezo, o llévame tú a un sitio desde el que se vea.";
        }
        if (ruta.Count == 0) return $"ya estás en «{destino}»";

        LogBus.Log("mapa-mcp", $"ruta de {ruta.Count} tramo(s) hacia «{destino}»");

        for (int i = 0; i < ruta.Count; i++)
        {
            var h = ruta[i];
            var paso = new PlanStep
            {
                StepOrder = i + 1,
                // La acción con la que se APRENDIÓ la arista, no un clic por defecto: una carpeta
                // de la lista solo se abre con doble clic, y recorrerla con un clic la seleccionaría
                // sin navegar — la ruta prometería un camino que no cumple.
                ActionType = h.Info.ActionType,
                Selector = h.Info.Selector,
                Label = h.Info.Label,
            };

            if (!_uia.Execute(paso, out string error))
            {
                // Lo que no está tras esperar a que la pantalla se asiente, no está: se deja de
                // enrutar por esa puerta y se intenta OTRO camino. Antes se insistía por el mismo
                // sitio muerto hasta acabar en «Ubicación no disponible» (2026-08-03).
                bool ausente = error.Contains("no se encontró", StringComparison.OrdinalIgnoreCase);
                if (ausente) { EsperarPantallaLista(1200); ausente = !_uia.Execute(paso, out error); }
                if (ausente)
                {
                    _map.OlvidarAccion(h.From, h.To);
                    if (!_reenrutando)
                    {
                        _reenrutando = true;
                        try
                        {
                            LogBus.Log("mapa-mcp", $"«{h.Info.Label}» ya no está; se busca otro camino hacia «{destino}»");
                            return GoTo(destino);
                        }
                        finally { _reenrutando = false; }
                    }
                    return $"tramo {i + 1}/{ruta.Count}: «{h.Info.Label}» ya no existe y no hay otro camino "
                         + $"hacia «{destino}». El recorrido se detuvo en «{_where()?.Id}».";
                }
                return $"tramo {i + 1}/{ruta.Count}: no se pudo pulsar «{h.Info.Label}» ({error}). "
                     + $"El recorrido se detuvo en «{_where()?.Id}».";
            }

            // La llegada se COMPRUEBA, no se supone. Sin esto, una acción equivocada —y el mapa
            // tiene ~1 de cada 5— dejaría al modelo creyendo que está donde no está.
            if (!Llego(h.To, 4000))
                return $"tramo {i + 1}/{ruta.Count}: pulsé «{h.Info.Label}» pero no se llegó a «{h.To}». "
                     + $"Estamos en «{_where()?.Id}». La ruta del mapa no coincide con la realidad aquí.";

            LogBus.Log("mapa-mcp", $"✓ tramo {i + 1}/{ruta.Count}: «{h.Info.Label}» → {h.To}");
        }
        Anotar(actual.Id, destino);
        ObservarAqui(destino);   // llegar es mirar alrededor
        return $"llegué a «{destino}» en {ruta.Count} paso(s)";
    }

    /// <summary>
    /// Toma UNA salida de la pantalla actual, la que el modelo elija por su nombre.
    ///
    /// Es la pieza que faltaba, y la pidió el usuario con mejor criterio que el mío: yo había hecho
    /// que <see cref="GoTo"/> exigiera el id exacto del destino —«uia://explorer.exe/videos-
    /// explorador-de-archivos»— y el modelo no tenía por qué acertar esa forma. Le estaba pidiendo
    /// que hablara mi idioma. Con esto el reparto es el natural: el modelo LEE las salidas
    /// (map_routes_from), DECIDE cuál sirve, y el cliente EJECUTA y verifica. La inteligencia de la
    /// ruta es del modelo; la honestidad del paso, nuestra.
    ///
    /// Un salto cada vez, a propósito: así el modelo ve a dónde llegó antes de decidir el
    /// siguiente, en vez de encadenar a ciegas una ruta que quizá dejó de ser válida.
    /// </summary>
    /// <param name="accionPedida">
    /// «click» o «doubleclick» para forzar la forma de pulsar. Existe porque SELECCIONAR y ABRIR
    /// son cosas distintas sobre el mismo elemento: para cortar un archivo hay que seleccionarlo
    /// con un clic, mientras que la acción aprendida para un archivo es el doble clic, que lo
    /// abre en otra aplicación. Sin esto, una tarea de organizar archivos era imposible por la
    /// interfaz: todo intento de tocar un archivo lo abría (2026-08-02). Vacío = la del mapa.
    /// </param>
    /// <param name="dondeCreoEstar">
    /// La superficie donde quien pide la acción CREE estar. Si no coincide con la real, no se
    /// actúa. Es el ancla de toda la ejecución: un paso que falla en silencio deja el recorrido en
    /// otra pantalla, y las acciones siguientes se ejecutan igual de bien… sobre el sitio
    /// equivocado. Así se pegaron archivos en la carpeta de origen y se crearon carpetas anidadas
    /// (2026-08-02). Comprobar la ubicación ANTES convierte un encadenamiento optimista en uno
    /// verificado, y el fallo aparece donde se produce en vez de tres pasos después.
    /// </param>
    private string Take(string salida, string accionPedida = "", string dondeCreoEstar = "")
    {
        string desalineado = ComprobarUbicacion(dondeCreoEstar);
        if (desalineado.Length > 0) return desalineado;

        if (salida.Length == 0) return "falta `exit`: el nombre de la salida a tomar (el que aparece en map_routes_from)";

        // Si algo se llevó el foco entre dos pasos de una tarea, se vuelve a la app de antes: el
        // asistente pidió «Nuevo» y recibió las opciones del centro de notificaciones porque nadie
        // comprobaba dónde estábamos realmente (2026-08-02).
        if (_ultimaApp.Length > 0 && !AppEnFrente().Equals(_ultimaApp, StringComparison.OrdinalIgnoreCase))
            AsegurarFoco(_ultimaApp);

        var actual = _where();
        if (actual == null) return "no se pudo determinar dónde estamos ahora mismo";

        // NO RECORDARLA NO ES NO TENERLA DELANTE. Aquí se devolvía «el mapa no conoce ninguna
        // salida» y se acababa la conversación, aunque la puerta estuviera a la vista: en una
        // pantalla nueva el mapa está vacío por definición, así que la primera visita a cualquier
        // sitio era siempre un no. Se sigue: si el mapa no la tiene, se mira la pantalla.
        var opciones = _map.ExitsFrom(actual.Id).Where(h => h.Info.Selector.Length > 0).ToList();

        // Coincidencia exacta primero, y luego por contención — «videos» debe encontrar «Videos»,
        // pero si dos salidas contienen lo pedido NO se elige por el modelo: se le devuelven las
        // candidatas. Adivinar entre dos destinos es exactamente lo que no debe hacer esta capa.
        // El SELECTOR desempata. Dos elementos pueden llamarse igual —en el explorador hay dos
        // «Detalles»: el modo de vista y el panel lateral— y entonces el nombre no alcanza para
        // elegir. Decir «coincide con 2, elige por nombre exacto» dejaba al asistente sin salida,
        // porque el nombre exacto era el mismo (2026-08-02). Se acepta el selector, que sí es único.
        var porSelector = opciones.Where(h =>
            h.Info.Selector.Equals(salida, StringComparison.OrdinalIgnoreCase)
            || h.Info.Selector.Contains($"={salida};", StringComparison.OrdinalIgnoreCase)).ToList();

        var exactas = opciones.Where(h => h.Info.Label.Equals(salida, StringComparison.OrdinalIgnoreCase)).ToList();
        var candidatas = porSelector.Count > 0 ? porSelector
            : exactas.Count > 0 ? exactas
            : opciones.Where(h => h.Info.Label.Contains(salida, StringComparison.OrdinalIgnoreCase)).ToList();

        // NO CONOCERLO NO ES RAZÓN PARA NEGARSE, si lo que se pide es un SELECTOR. El mapa es una
        // memoria de lo visto, no una lista de permisos: una carpeta recién creada existe en la
        // pantalla aunque el mapa aún no la haya registrado, y negarse a entrar en ella rompía la
        // tarea justo después de crearla (2026-08-03). Se intenta, se verifica el resultado, y si
        // funciona se aprende — que es como se aprende todo lo demás.
        // LO QUE SE VE, SE PUEDE PULSAR. El mapa es memoria, no lista de permisos. Si lo pedido no
        // está registrado desde aquí, se mira la pantalla tal como está AHORA. Este era el hueco
        // entre ver y pulsar: el asistente señalaba la barra de búsqueda —que la veía— y acto
        // seguido decía que no podía pulsarla, porque señalar leía la pantalla y pulsar leía el
        // mapa. Dos sentidos distintos para la misma cosa (2026-08-05, pedido por el usuario: «que
        // pueda ver y clickear cualquier puerta que se vea en pantalla»).
        if (candidatas.Count == 0)
        {
            _lector.Read();
            var vistos = Uia.Reconocedor.Buscar(_lector.Elements, salida);

            if (vistos.Count > 1)
                return $"«{salida}» coincide con {vistos.Count} cosas que tengo a la vista: "
                     + string.Join("; ", vistos.Take(8).Select(v => $"«{v.Label}» [{Uia.Reconocedor.SelectorDe(v)}]"))
                     + ". Repite `exit` con el selector de la que quieras.";

            if (vistos.Count == 0)
            {
                // Se dice QUÉ HAY, no solo que no está lo pedido. Quien pregunta por voz dice «la
                // barra de búsqueda» y el elemento se llama «Buscar en Notas»: con la lista delante
                // el reintento es inmediato, y sin ella hay que adivinar a ciegas (2026-08-05).
                var aLaVista = _lector.Elements.Where(EsPuertaVisible)
                    .GroupBy(e => e.Label, StringComparer.OrdinalIgnoreCase).Select(g => g.Key)
                    .Take(25).ToList();
                return $"no veo nada que se llame «{salida}» en «{actual.Id}». Lo que SÍ tengo delante "
                     + $"y puedo pulsar: {string.Join(", ", aLaVista.Select(n => $"«{n}»"))}"
                     + (aLaVista.Count >= 25 ? "…" : "")
                     + ". Vuelve a pedírmelo con uno de esos nombres.";
            }

            var visto = vistos[0];
            string selectorDirecto = Uia.Reconocedor.SelectorDe(visto);
            string etiquetaDirecta = visto.Label;
            LogBus.Log("mapa-mcp", $"«{salida}» no está en el mapa, pero la veo en pantalla como "
                                 + $"«{etiquetaDirecta}» ({visto.ControlType}): se pulsa y se verifica");

            var directo = new PlanStep
            {
                StepOrder = 1, ActionType = "click",
                Selector = selectorDirecto, Label = etiquetaDirecta,
            };
            string origen = actual.Id;

            // UN CLIC SOBRE LO YA SELECCIONADO NO SELECCIONA: ABRE EL RENOMBRADO. Es la regla del
            // explorador de toda la vida (clic lento sobre lo seleccionado = renombrar), y aquí
            // mordía justo en el peor sitio: una carpeta recién creada queda seleccionada, así que
            // el clic previo abría su campo de edición y el doble clic siguiente caía dentro del
            // campo en vez de entrar en la carpeta. Se creaban las tres carpetas y no se entraba en
            // ninguna (2026-08-03). Si ya está seleccionado, el paso de seleccionar sobra.
            string nombrePedido = System.Text.RegularExpressions.Regex
                .Match(selectorDirecto, @"name=([^;]+)").Groups[1].Value;
            bool yaSeleccionado = nombrePedido.Length > 0
                && SeleccionActual().Any(s => s.Equals(nombrePedido, StringComparison.OrdinalIgnoreCase));

            // NO TODO LO QUE SE PULSA ABRE ALGO. Una barra de búsqueda, una casilla o un botón de
            // barra hacen su trabajo sin cambiar de pantalla; exigirles un cambio los daba por
            // fallados —«al pulsarla no llevó a ninguna parte», cierto y engañoso, porque no tenía
            // que llevar. Solo se sube al doble clic lo que efectivamente se abre: lo de las listas
            // y los árboles. Y si quien llama pidió una acción concreta, manda la suya.
            bool abrePorDoble = accionPedida.Length > 0
                ? accionPedida.Equals("doubleclick", StringComparison.OrdinalIgnoreCase)
                : visto.ControlType.Equals("ListItem", StringComparison.OrdinalIgnoreCase)
                  || visto.ControlType.Equals("TreeItem", StringComparison.OrdinalIgnoreCase);

            // Se actúa sobre el elemento QUE SE ACABA DE VER, no sobre su nombre: buscarlo otra vez
            // es un rodeo que puede fallar aunque siga delante, y fallaba (ver EjecutarSobre).
            string errDirecto = "";
            bool errDirectoOk = yaSeleccionado || _uia.EjecutarSobre(visto.Native, directo, out errDirecto);
            string accionHecha = "click";
            if (errDirectoOk)
            {
                string llegada = "";
                if (!yaSeleccionado)
                {
                    EsperarPantallaLista(700);
                    llegada = EsperarCambio(origen, abrePorDoble ? 1200 : 800);
                }
                else LogBus.Log("mapa-mcp", $"«{nombrePedido}» ya estaba seleccionado: se va directo al doble clic");
                if (llegada.Length == 0 && abrePorDoble)
                {
                    _uia.EjecutarSobre(visto.Native,
                        new PlanStep { StepOrder = 1, ActionType = "doubleclick", Selector = selectorDirecto, Label = etiquetaDirecta },
                        out errDirecto);
                    EsperarPantallaLista(700);
                    llegada = EsperarCambio(origen, 1500);
                    accionHecha = "doubleclick";
                }
                // Salir de la app no es cruzar una puerta de la app. Se pidió entrar en «Datos» y lo
                // que había con ese nombre era una foto: el doble clic abrió Photos.exe y esto lo
                // dio por «puerta cruzada y aprendida» (2026-08-03). Decirlo es lo útil: quien pidió
                // entrar en una carpeta necesita saber que abrió un archivo.
                if (llegada.Length > 0 && !SurfaceMap.MismaApp(origen, llegada))
                {
                    LogBus.Log("mapa-mcp", $"«{etiquetaDirecta}» no es una puerta: abrió «{llegada}», otra aplicación");
                    return $"«{etiquetaDirecta}» no lleva a ninguna parte dentro de esta app: al pulsarla se abrió "
                         + $"«{llegada}», que es otra aplicación. Lo que hay con ese nombre no es un sitio "
                         + "al que entrar, es un archivo que se abre.";
                }
                if (llegada.Length > 0)
                {
                    _ultimaApp = AppDe(llegada).Length > 0 ? AppDe(llegada) : _ultimaApp;
                    _map.LearnTraversal(origen, llegada, selectorDirecto, Array.Empty<string>(),
                                        etiquetaDirecta, visto.ControlType, accionHecha);
                    Anotar(origen, llegada);
                    ObservarAqui(llegada);
                    LogBus.Log("mapa-mcp", $"✓ «{etiquetaDirecta}» no estaba en el mapa; se cruzó y quedó aprendida → {llegada}");
                    return $"«{etiquetaDirecta}» no estaba en el mapa; la vi en pantalla, la crucé y lleva a "
                         + $"«{llegada}». Queda aprendida.";
                }

                // Pulsado y la pantalla sigue igual. Para lo que no abre nada, ESO es haberlo hecho
                // bien: la barra de búsqueda queda enfocada, la casilla marcada, el botón aplicado.
                if (!abrePorDoble)
                {
                    LogBus.Log("mapa-mcp", $"✓ pulsado «{etiquetaDirecta}» ({visto.ControlType}) visto en pantalla, sin cambio de pantalla");
                    return $"pulsé «{etiquetaDirecta}» ({visto.ControlType}). La veía en pantalla aunque el mapa "
                         + $"no la tuviera. Seguimos en «{origen}», que es lo normal en algo así: no es una "
                         + "puerta, es un control. Si querías ir a otro sitio, dime a cuál.";
                }
            }
            return $"vi «{etiquetaDirecta}» e intenté pulsarla, pero no pasó nada"
                 + (errDirecto.Length > 0 ? $" ({errDirecto})" : "") + ".";
        }

        if (candidatas.Count > 1)
            return $"«{salida}» coincide con {candidatas.Count} salidas: "
                 + string.Join("; ", candidatas.Select(h => $"«{h.Info.Label}» [{h.Info.Selector}]"))
                 + ". Repite `exit` con el SELECTOR de la que quieras (o con su AutomationId).";

        var elegida = candidatas[0];
        _ultimaApp = AppDe(actual.Id).Length > 0 ? AppDe(actual.Id) : _ultimaApp;
        // Seleccionar es TODA acción pedida que no pretende navegar: un clic sobre algo que
        // normalmente se abre con doble, y añadir a la selección. Dejar fuera «addselect» hacía
        // que sumar el segundo archivo se juzgara como puerta y se reportara «la pantalla no
        // cambió» —cierto y engañoso: no tenía que cambiar (2026-08-02).
        bool seleccionar = accionPedida.Equals("addselect", StringComparison.OrdinalIgnoreCase)
                           || (accionPedida.Equals("click", StringComparison.OrdinalIgnoreCase)
                               && elegida.Info.ActionType.Equals("doubleclick", StringComparison.OrdinalIgnoreCase));
        // Se EMPIEZA por el clic simple aunque la arista diga doble: es la acción menos agresiva y
        // la que funciona en los menús de navegación. Si no mueve nada, más abajo se sube al doble.
        string accionInicial = accionPedida.Length > 0 ? accionPedida
            : elegida.Info.ActionType.Equals("doubleclick", StringComparison.OrdinalIgnoreCase)
                ? "click" : elegida.Info.ActionType;
        var paso = new PlanStep
        {
            StepOrder = 1,
            ActionType = accionInicial,
            Selector = elegida.Info.Selector,
            Label = elegida.Info.Label,
        };

        string desde = actual.Id;
        _ultimaAccion = $"intentar «{elegida.Info.Label}» en «{desde}»";

        // La selección se lee ANTES de pulsar: «Cortar» la vacía, así que después ya no hay nada
        // que contar y no se podría decir sobre qué se actuó.
        _seleccionPrevia = OperaSobreLaSeleccion(elegida.Info.Label) ? SeleccionActual() : new List<string>();

        // Y los elementos de menú TAMBIÉN se cuentan antes: la señal de que un menú se abrió es que
        // haya MÁS que antes, no que haya alguno. Pueden quedar restos del menú anterior en el
        // árbol, y darlos por buenos hacía continuar sin que el menú estuviera abierto: el paso
        // siguiente no encontraba su opción y el grupo entero fallaba (2026-08-03). Comparar contra
        // el estado previo en vez de contra cero es lo que ya nos resolvió la identidad de pantalla.
        int menusAntes = PuedeAbrirMenu(elegida.Info.Label) ? CuantosMenus() : 0;

        if (!_uia.Execute(paso, out string error))
        {
            // NO ESTAR TODAVÍA NO ES NO ESTAR. Lo que se abre tarda en aparecer: se pulsaba «Nuevo»
            // y se preguntaba por «Carpeta» antes de que el menú existiera, así que se respondía «no
            // se encontró» y el grupo entero se caía —sin carpeta, y con el paso siguiente
            // escribiendo sobre lo que hubiera seleccionado (2026-08-03). Se espera a que la
            // pantalla se estabilice y se vuelve a mirar UNA vez: si sigue sin estar, no está.
            if (error.Contains("no se encontró", StringComparison.OrdinalIgnoreCase))
            {
                EsperarPantallaLista(1500);
                if (_uia.Execute(paso, out string error2))
                {
                    LogBus.Log("mapa-mcp", $"«{elegida.Info.Label}» no estaba aún; apareció al esperar a que la pantalla se estabilizara");
                    error = "";
                }
                else error = error2;
            }
            if (error.Length > 0)
                return $"no se pudo pulsar «{elegida.Info.Label}»: {error}";
        }

        // UN CLIC PRIMERO, EL DOBLE SOLO SI HACE FALTA. La acción de un elemento de lista depende
        // de la APP, no del tipo: en una lista de archivos el doble clic abre, pero en un menú de
        // navegación —el panel de Configuración— un solo clic navega y el segundo lo ANULA, así que
        // el recorrido pulsaba las doce secciones sin moverse de «Inicio» (2026-08-03). En vez de
        // adivinar por app, se prueba lo suave y se sube a lo fuerte solo si no pasó nada: se
        // acierta en las dos sin saber en cuál estamos.
        if (elegida.Info.ActionType.Equals("doubleclick", StringComparison.OrdinalIgnoreCase)
            && accionPedida.Length == 0 && !seleccionar
            && (EsperarPantallaLista(700) || true) && EsperarCambio(desde, 150).Length == 0)
        {
            var doble = new PlanStep
            {
                StepOrder = 1, ActionType = "doubleclick",
                Selector = elegida.Info.Selector, Label = elegida.Info.Label,
            };
            LogBus.Log("mapa-mcp", $"«{elegida.Info.Label}»: un clic no movió nada, se prueba el doble");
            _uia.Execute(doble, out _);
        }

        // Selección deliberada: no se espera ningún cambio de pantalla, y exigirlo sería reportar
        // fallo a un clic que hizo exactamente lo pedido.
        if (seleccionar)
        {
            LogBus.Log("mapa-mcp", $"✓ seleccionado «{elegida.Info.Label}» (sin abrir)");
            return $"seleccioné «{elegida.Info.Label}» sin abrirlo; ya puedes usar una acción sobre él (Cortar, Copiar, Cambiar nombre…)";
        }

        // PUERTA DE ACCIÓN: su éxito no es llegar a otra pantalla — es haber hecho algo AQUÍ.
        // Exigirle navegación reportaría fallo a un «Nuevo» que abrió su menú perfectamente. Si
        // resulta que sí navegó (un «Guardar como…» que abre diálogo), eso también se cuenta.
        if (elegida.Info.Kind.Equals("accion", StringComparison.OrdinalIgnoreCase))
        {
            // Sobre QUÉ actuó: para Cortar/Copiar/Eliminar es la única forma de comprobar que se
            // hizo sobre lo que se creía, y no sobre lo que quedó seleccionado de un paso anterior.
            string sobre = "";
            if (OperaSobreLaSeleccion(elegida.Info.Label))
            {
                var s = _seleccionPrevia;
                sobre = s.Count > 0
                    ? $" sobre {s.Count} elemento(s): {string.Join(", ", s.Select(x => $"«{x}»"))}"
                    : " sobre NADA seleccionado (probablemente no hizo nada)";
            }

            // Se espera EL EFECTO, no un tiempo. Un botón que abre menú se da por hecho cuando
            // aparecen sus opciones —suele ser <300 ms— y no cuando se agota un reloj; el resto
            // se comprueba en una ventana corta, porque una acción que navega lo hace enseguida.
            // Antes eran 800 ms fijos por acción, casi siempre esperando a nada (2026-08-03).
            // La pantalla decide cuándo se sigue, no el reloj: se espera a que se asiente y solo
            // entonces se mira si cambió de sitio. Antes eran 240-320 ms fijos, que sobraban en el
            // caso normal y se quedaban cortos cuando la app iba lenta (Fase 1 del plan, 2026-08-03).
            bool abreMenu = PuedeAbrirMenu(elegida.Info.Label);
            EsperarPantallaLista(abreMenu ? 900 : 1200);
            string tras = EsperarCambio(desde, 200);
            // ABRIR UN MENÚ Y QUE SE ABRA SON LO MISMO: si no se abrió, la acción NO se hizo. Antes
            // esto solo se anotaba en el log y se devolvía «✓ ejecuté Nuevo», así que el paso
            // siguiente pedía «Carpeta», no la encontraba, y el grupo entero moría arrastrando a los
            // demás. Es «aceptado ≠ ejecutado» en su forma más pura: el clic se aceptó y no pasó
            // nada. Se reintenta UNA vez —el primer intento tras cambiar de carpeta es el que más
            // falla, con la barra aún asentándose— y si sigue sin abrirse se dice (2026-08-03).
            if (abreMenu && !EsperarMenu(1500, menusAntes))
            {
                LogBus.Log("mapa-mcp", $"«{elegida.Info.Label}» no llegó a abrir menú (seguía habiendo {menusAntes}); se reintenta");
                EsperarPantallaLista(1200);
                if (_uia.Execute(paso, out _) && EsperarMenu(1800, menusAntes))
                    LogBus.Log("mapa-mcp", $"✓ «{elegida.Info.Label}» abrió el menú al segundo intento");
                else
                {
                    LogBus.Log("mapa-mcp", $"NO SE ABRIÓ: «{elegida.Info.Label}» no despliega su menú");
                    return $"pulsé «{elegida.Info.Label}» dos veces y su menú no llegó a abrirse. "
                         + "No sigo como si lo hubiera hecho: el paso siguiente buscaría una opción "
                         + "que no está delante. Estamos en «" + (_where()?.Id ?? desde) + "».";
                }
            }
            LogBus.Log("mapa-mcp", $"✓ acción «{elegida.Info.Label}» ejecutada" + (tras.Length > 0 ? $" → {tras}" : ""));

            // Se relee SIEMPRE: una acción suele destapar cosas nuevas —un menú, un diálogo— en la
            // misma superficie, y sin releer el asistente actuaría sobre la pantalla de antes.
            // Tras una acción se relee SOLO si pudo destapar algo nuevo: un menú, un diálogo. Un
            // «Cortar» o un «Pegar» no cambian las puertas de la pantalla, y releerla entera —con
            // su viaje UIA por cada elemento— costaba segundos por acción sin aportar nada
            // (medido el 2026-08-02: «Carpeta» llegó a agotar 150 s de espera).
            string aqui = tras.Length > 0 ? tras : desde;
            if (abreMenu) ObservarMenus(aqui);
            var nuevas = _map.ExitsFrom(aqui)
                .Where(h => h.Info.Kind.Equals("accion", StringComparison.OrdinalIgnoreCase) && h.Info.Selector.Length > 0)
                .Select(h => h.Info.Label).Distinct().Take(18).ToList();

            return (tras.Length > 0
                    ? $"ejecuté «{elegida.Info.Label}»{sobre} y la pantalla pasó a «{tras}». "
                    : $"ejecuté «{elegida.Info.Label}»{sobre} (la superficie sigue siendo «{desde}»). ")
                 + (nuevas.Count > 0 ? "Ahora hay: " + string.Join(", ", nuevas.Select(n => $"«{n}»")) : "");
        }

        // PUERTA SIN CRUZAR: no hay destino contra el que comparar, así que el éxito es que la
        // pantalla CAMBIE, y lo que se descubre se aprende. Comparar contra el marcador «?selector»
        // hacía que cruzar una puerta se reportara siempre como fallo, incluso llegando —justo lo
        // contrario de para lo que existen las puertas, que es descubrir a dónde dan (2026-08-02).
        if (SurfaceMap.EsPuerta(elegida.To))
        {
            string llegada = EsperarCambio(desde, 4000);
            if (llegada.Length == 0)
                return $"pulsé «{elegida.Info.Label}» pero la pantalla no cambió; sigue sin saberse a dónde da.";

            _map.LearnTraversal(desde, llegada, elegida.Info.Selector, elegida.Info.Alternatives,
                elegida.Info.Label, elegida.Info.ControlType, elegida.Info.ActionType);
            Anotar(desde, llegada);
            AprenderSubida(desde, llegada, elegida.Info.ControlType);
            ObservarAqui(llegada);
            LogBus.Log("mapa-mcp", $"✓ puerta «{elegida.Info.Label}» descubierta → {llegada}");
            return $"tomé «{elegida.Info.Label}»: era una puerta sin explorar y lleva a «{llegada}». Queda aprendida.";
        }

        if (!Llego(elegida.To, 4000))
        {
            // EL TERRENO MANDA SOBRE EL MAPA. Si la puerta llevó a otro sitio de la MISMA app, la
            // que estaba equivocada era la arista, no la acción: se corrige y se sigue. Sin esto
            // una arista mala se quedaba mala para siempre y arrastraba cada tarea que pasara por
            // ella — se entró en «docs6» perfectamente y se reportó fallo porque el mapa esperaba
            // «claude.exe», destino que nunca existió (2026-08-03).
            string real = _where()?.Id ?? "";
            if (real.Length > 0 && !string.Equals(real, desde, StringComparison.OrdinalIgnoreCase)
                && SurfaceMap.MismaApp(desde, real))
            {
                _map.LearnTraversal(desde, real, elegida.Info.Selector, elegida.Info.Alternatives,
                    elegida.Info.Label, elegida.Info.ControlType, elegida.Info.ActionType);
                Anotar(desde, real);
                AprenderSubida(desde, real, elegida.Info.ControlType);
                ObservarAqui(real);
                LogBus.Log("mapa-mcp", $"✓ «{elegida.Info.Label}» lleva a «{real}», no a «{elegida.To}»: mapa corregido");
                return $"tomé «{elegida.Info.Label}» y llegué a «{real}» "
                     + $"(el mapa decía «{elegida.To}»; queda corregido).";
            }
            return $"pulsé «{elegida.Info.Label}» pero no se llegó a «{elegida.To}». "
                 + $"Estamos en «{real}».";
        }

        // Llegar es mirar alrededor: si no, el asistente se planta en una pantalla nueva y no sabe
        // qué puede hacer allí. Se pegó un archivo en una carpeta recién abierta y la respuesta
        // fue «aquí no hay ninguna salida que se llame Pegar», con la barra a la vista (2026-08-02).
        Anotar(desde, elegida.To);
        AprenderSubida(desde, elegida.To, elegida.Info.ControlType);
        ObservarAqui(elegida.To);
        LogBus.Log("mapa-mcp", $"✓ salida «{elegida.Info.Label}» → {elegida.To}");
        return $"tomé «{elegida.Info.Label}» y llegué a «{elegida.To}»";
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
    private string Type(string texto, string target, string dondeCreoEstar = "")
    {
        if (texto.Length == 0) return "falta `text`: qué hay que escribir";
        string desalineado = ComprobarUbicacion(dondeCreoEstar);
        if (desalineado.Length > 0) return desalineado;

        if (_ultimaApp.Length > 0 && !AppEnFrente().Equals(_ultimaApp, StringComparison.OrdinalIgnoreCase))
            AsegurarFoco(_ultimaApp);

        string selector = target;
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
            if (selector.Length == 0) return "no hay ningún campo con el foco; pasa `target` con su selector";

            // Si ACEPTA texto o no lo dice el control, no su nombre de tipo: el buscador de YouTube
            // es un ComboBox y se rechazaba por no llamarse «Edit», aunque es justo donde se escribe
            // (2026-08-05). La regla vive en UiaSurface.AceptaTexto, que también protege el caso
            // contrario: una fila de lista jamás acepta texto, porque ahí escribir es renombrar.
            if (foco == null || !U.Graph.Surfaces.UiaSurface.AceptaTexto(foco))
            {
                LogBus.Log("mapa-mcp", $"NO SE ESCRIBE: el foco lo tiene «{selector}», que es {tipoFoco}, no un campo de texto");
                return $"NO escribo: no hay ningún campo de texto abierto. El foco lo tiene «{selector}» "
                     + $"({tipoFoco}), y escribir ahí no es escribir — es renombrar lo que esté seleccionado. "
                     + "Si querías renombrar, abre antes la edición (Cambiar nombre / F2) o pasa `target`.";
            }
        }

        var paso = new PlanStep { StepOrder = 1, ActionType = "input", Selector = selector, Value = texto };
        if (!_uia.Execute(paso, out string error))
            return $"no pude escribir en «{selector}»: {error}";

        // Enter confirma: en una edición en línea (renombrar) el texto no se aplica hasta que se
        // acepta, y dejarlo a medias deja la interfaz en un estado del que nadie se acuerda luego.
        string antes = _where()?.Id ?? "";
        keybd_event(0x0D, 0, 0, IntPtr.Zero);
        keybd_event(0x0D, 0, 2, IntPtr.Zero);
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
