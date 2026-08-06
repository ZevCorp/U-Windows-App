using System.Net.Http;
using System.Text;
using System.Text.Json;
using U.WindowsClient.Capture;
using U.WindowsClient.Diagnostics;
using U.WindowsClient.Uia;

namespace U.WindowsClient.Navigation;

/// <summary>
/// Le enseña a Ü la jerarquía de una aplicación: qué es navegación permanente y qué cuelga de qué.
///
/// El sistema deduce el nivel contando —una salida que aparece en dos pantallas se considera de la
/// app— y eso acierta después de pasear un rato, no al llegar. Un modelo que MIRA la pantalla lo
/// sabe de un vistazo: distingue un panel lateral de una lista de archivos porque entiende para qué
/// sirve cada cosa, no porque la haya contado.
///
/// EL NÚMERO ES EL PUENTE. Un modelo de visión puede decir «el menú de la izquierda» y eso no
/// identifica nada: describir no es señalar. Con un número pintado sobre cada puerta, su respuesta
/// —«el 3, el 7 y el 12»— se traduce al elemento exacto del árbol UIA, que es lo único accionable.
/// La imagen le da el sentido; el número, la identidad.
///
/// Y NO PULSA NADA. Enseña: mira y nombra. Cruzar las puertas y comprobar a dónde llevan sigue
/// siendo cosa nuestra, por identidad y verificando la consecuencia. Es deliberado: un modelo que
/// acciona por píxeles falla en silencio y reporta éxito, y eso es justo lo que este sistema lleva
/// meses evitando.
/// </summary>
public sealed class MaestroDeApps
{
    private const string Modelo = "gemini-3.6-flash";

    private readonly SurfaceMap _mapa;
    public MaestroDeApps(SurfaceMap mapa) => _mapa = mapa;

    /// <summary>Lo que el maestro dictaminó, ya aplicado al mapa.</summary>
    public sealed record Leccion(int Nivel1, int Nivel2, string Resumen);

    private static string Clave() =>
        Environment.GetEnvironmentVariable("GEMINI_API_KEY")?.Trim() ?? "";

    /// <summary>
    /// Mira la pantalla y fija los niveles de la app. Devuelve null si no se pudo preguntar.
    /// </summary>
    /// <param name="app">A qué aplicación pertenece lo que se ve («explorer.exe»).</param>
    /// <param name="superficie">Dónde estamos, para que pueda situar el segundo nivel.</param>
    /// <param name="numeradas">Número pintado en pantalla → elemento real.</param>
    /// <param name="ventana">
    /// La ventana EXACTA de la que salieron los puntos numerados. Se pasa en vez de volver a
    /// buscarla porque entre leer los puntos y sacar la foto pasan segundos, y en esos segundos el
    /// foco puede irse a otra app: se llegó a leer los puntos del explorador y aplicar la lección a
    /// claude.exe, mezclando tres momentos distintos en una sola enseñanza (2026-08-06). La app, los
    /// puntos y la foto tienen que venir de la misma ventana y del mismo instante.
    /// </param>
    public async Task<Leccion?> EnsenarAsync(string app, string superficie,
        IReadOnlyDictionary<int, UiaReader.UiElement> numeradas, CancellationToken ct,
        IntPtr ventana)
    {
        string clave = Clave();
        if (clave.Length == 0) { LogBus.Log("maestro", "sin GEMINI_API_KEY: no se puede enseñar"); return null; }
        if (numeradas.Count == 0) { LogBus.Log("maestro", "no hay puertas numeradas que enseñar"); return null; }

        string inventario = string.Join("\n", numeradas.OrderBy(kv => kv.Key)
            .Select(kv => $"{kv.Key}. «{kv.Value.Label}» ({kv.Value.ControlType})"));

        // La foto es DE LA APP, no del escritorio: ver también el editor, el navegador y nuestra
        // propia interfaz convierte «¿qué es aquí navegación permanente?» en una pregunta sin «aquí».
        //
        // Y la ventana se toma de LO QUE ESTÁ DELANTE, no buscándola por el nombre de la app. Ese
        // nombre no siempre es el dueño de la ventana, y buscarlo así fallaba en dos casos muy
        // normales: en el navegador la app es el DOMINIO —«canva.com», que no es ningún proceso— y
        // el Panel de control vive dentro de explorer.exe. Las dos veces se dijo «no encuentro la
        // ventana» con la ventana delante (2026-08-05). Lo que hay delante es lo que se enseña.
        // LA MISMA VENTANA DE LA QUE SALIERON LOS PUNTOS. Si entre medias el foco se fue a otra
        // app, enseñar sería mezclar: puntos de una, foto de otra, y la lección aplicada a una
        // tercera. Antes que enseñar mal, no enseñar.
        if (ventana == IntPtr.Zero || AppAligner.VentanaDelUsuario() != ventana)
        {
            LogBus.Log("maestro", $"la ventana cambió mientras se preparaba la lección de «{app}»: no se enseña");
            return null;
        }

        string? foto = Screenshotter.CaptureVentanaBase64Png(ventana);
        if (foto == null)
        {
            LogBus.Log("maestro", $"no pude fotografiar la ventana de «{app}»: no se enseña a ciegas");
            return null;
        }

        string respuesta;
        try { respuesta = await PreguntarAsync(clave, Instruccion(app, superficie, inventario), foto, ct); }
        catch (Exception e) { LogBus.Log("maestro", $"no se pudo preguntar: {e.Message}"); return null; }

        return Aplicar(app, respuesta, numeradas);
    }

    /// <summary>
    /// Lo que se le pide. Se insiste en dos cosas porque son las que se tuercen: que responda con
    /// NÚMEROS —un nombre puede repetirse en la pantalla, un número no— y que ante la duda deje algo
    /// fuera. Un elemento de más en el nivel principal se propaga a toda la app y hay que
    /// desandarlo; uno de menos se añade señalándolo, que es un gesto.
    /// </summary>
    private static string Instruccion(string app, string superficie, string inventario) => $$"""
        Estás mirando la aplicación «{{app}}». Ahora mismo la pantalla es «{{superficie}}».

        En la captura, cada elemento accionable lleva encima un punto con un NÚMERO. Esta es la
        lista de esos números:

        {{inventario}}

        Tu tarea es explicar la JERARQUÍA DE NAVEGACIÓN de esta aplicación:

        · PRIMER NIVEL: el mobiliario fijo de navegación, lo que está SIEMPRE a la vista dentro de
          esta app estés en la pantalla que estés — el panel lateral del explorador, las pestañas de
          un navegador, la barra de secciones de una app de ajustes. No son acciones («Copiar»,
          «Eliminar», «Nuevo») ni contenido (archivos, correos, filas de una lista): son los sitios
          a los que siempre se puede ir.

        · SEGUNDO NIVEL: si en esta pantalla ves elementos que pertenecen a UNO de los de primer
          nivel —porque estamos dentro de él—, dilo colgándolos de su número. Ejemplo: si estamos
          dentro de «Notas» y ves sus subcarpetas, esas van en el segundo nivel bajo el número de
          «Notas».

        Responde SOLO con este JSON, usando los números de la lista:

        {"nivel1": [1, 5, 9],
         "nivel2": {"5": [12, 13]},
         "explicacion": "una frase corta, en español, de qué es cada zona"}

        CASI TODA APLICACIÓN TIENE NAVEGACIÓN PERMANENTE, y suele estar en el mismo sitio: una
        columna a la izquierda, una fila de pestañas arriba, o una barra de secciones. Si crees que
        no la hay, vuelve a mirar el lado izquierdo y la parte de arriba antes de decirlo — devolver
        la lista vacía es casi siempre no haber mirado bien, no una app sin navegación.

        Reglas:
        - Solo números que estén en la lista. Nada inventado.
        - Si dudas de UNO concreto, déjalo fuera y sigue con los demás. Lo que NO vale es dejarlos
          todos fuera por no estar seguro de alguno: quedarse corto en uno se arregla señalándolo,
          quedarse en cero deja la app sin aprender.
        - Los números están pintados sobre cada elemento, en su esquina superior izquierda. Si
          alguno no se lee bien, guíate por la posición: el que está encima de la carpeta que ves.
        """;

    private static async Task<string> PreguntarAsync(string clave, string instruccion, string fotoBase64, CancellationToken ct)
    {
        var cuerpo = new
        {
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new object[]
                    {
                        new { text = instruccion },
                        new { inline_data = new { mime_type = "image/png", data = fotoBase64 } },
                    },
                },
            },
            // Se pide JSON de verdad y no «un JSON dentro de un texto»: parsear prosa es adivinar.
            generationConfig = new { responseMimeType = "application/json", temperature = 0.0 },
        };

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        string url = $"https://generativelanguage.googleapis.com/v1beta/models/{Modelo}:generateContent?key={Uri.EscapeDataString(clave)}";
        string json = JsonSerializer.Serialize(cuerpo);

        // «AHORA MISMO NO» NO ES «NO». Un 503 o un 429 dicen que el servicio está ocupado, no que la
        // pregunta esté mal, y sin reintentar se perdía la lección entera por un momento malo — que
        // es justo lo que pasó al probar con Chrome (2026-08-05). Se espera un poco más cada vez;
        // los errores de verdad —una clave mala, una petición inválida— no se reintentan, porque
        // repetirlos no los arregla.
        string texto = "";
        int codigo = 0;
        for (int intento = 1; intento <= 4; intento++)
        {
            using var contenido = new StringContent(json, Encoding.UTF8, "application/json");
            HttpResponseMessage r;
            try { r = await http.PostAsync(url, contenido, ct); }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                // LA RED TAMBIÉN DICE «AHORA NO». Se reintentaba por código HTTP, pero un fallo de
                // red no llega como código: llega como excepción y se llevaba la lección entera a la
                // primera. Pasó de verdad —el DNS de la máquina devolvía solo IPv6 y las conexiones
                // se quedaban colgadas— y desde fuera se veía igual que si el maestro no supiera
                // responder (2026-08-06).
                if (intento == 4) throw new InvalidOperationException($"no se pudo llegar al servicio: {e.Message}");
                int esperaRed = 1000 * (1 << (intento - 1));
                LogBus.Log("maestro", $"la red falló ({e.GetType().Name}); reintento {intento + 1} de 4 en {esperaRed / 1000}s");
                await Task.Delay(esperaRed, ct);
                continue;
            }
            texto = await r.Content.ReadAsStringAsync(ct);
            codigo = (int)r.StatusCode;
            if (r.IsSuccessStatusCode) break;

            bool vuelveAIntentarse = codigo is 429 or 500 or 502 or 503 or 504;
            if (!vuelveAIntentarse || intento == 4)
                throw new InvalidOperationException($"{codigo}: {Recorta(texto)}");

            int esperaMs = 1000 * (1 << (intento - 1));   // 1s, 2s, 4s
            LogBus.Log("maestro", $"el servicio dijo {codigo}; reintento {intento + 1} de 4 en {esperaMs / 1000}s");
            await Task.Delay(esperaMs, ct);
        }

        using var doc = JsonDocument.Parse(texto);
        return doc.RootElement.GetProperty("candidates")[0]
                  .GetProperty("content").GetProperty("parts")[0]
                  .GetProperty("text").GetString() ?? "";
    }

    private static string Recorta(string s) => s.Length <= 300 ? s : s[..300] + "…";

    /// <summary>
    /// Traduce los números a elementos y los fija en el mapa.
    ///
    /// Se fija como puesto A MANO —igual que si lo hubiera dicho el usuario— porque eso es lo que
    /// es: alguien que MIRÓ la pantalla y lo dijo, no un contador que llegó a dos. Y así la
    /// deducción automática no lo mueve después.
    /// </summary>
    private Leccion? Aplicar(string app, string json, IReadOnlyDictionary<int, UiaReader.UiElement> numeradas)
    {
        List<int> nivel1;
        Dictionary<string, List<int>> nivel2;
        string explicacion;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var raiz = doc.RootElement;
            nivel1 = raiz.TryGetProperty("nivel1", out var n1)
                ? n1.EnumerateArray().Where(x => x.TryGetInt32(out _)).Select(x => x.GetInt32()).ToList()
                : new List<int>();
            nivel2 = new Dictionary<string, List<int>>();
            if (raiz.TryGetProperty("nivel2", out var n2) && n2.ValueKind == JsonValueKind.Object)
                foreach (var p in n2.EnumerateObject())
                    nivel2[p.Name] = p.Value.EnumerateArray()
                        .Where(x => x.TryGetInt32(out _)).Select(x => x.GetInt32()).ToList();
            explicacion = raiz.TryGetProperty("explicacion", out var ex) ? ex.GetString() ?? "" : "";
        }
        catch (Exception e)
        {
            LogBus.Log("maestro", $"no entendí la respuesta: {e.Message} · {Recorta(json)}");
            return null;
        }

        int puestos1 = 0, puestos2 = 0;
        var elegidos = new List<string>();   // para poder JUZGAR la lección, no solo contarla
        foreach (int n in nivel1.Distinct())
        {
            // UN NÚMERO QUE NO EXISTE NO SE APLICA. Si el modelo se inventa uno, aquí se cae solo:
            // el puente son los números que nosotros pintamos, no los que él imagine.
            if (!numeradas.TryGetValue(n, out var el)) { LogBus.Log("maestro", $"número {n} no existe: se ignora"); continue; }
            string r = _mapa.FijarNivel(app, el.Label, 1);
            if (!r.Contains("no encuentro", StringComparison.OrdinalIgnoreCase)) { puestos1++; elegidos.Add(el.Label); }
            else LogBus.Log("maestro", $"«{el.Label}» (nº {n}) no está como salida en el mapa: no se fija");
        }
        if (elegidos.Count > 0)
            LogBus.Log("maestro", "primer nivel: " + string.Join(", ", elegidos.Select(x => $"«{x}»")));

        foreach (var (padre, hijos) in nivel2)
            foreach (int n in hijos.Distinct())
            {
                if (!numeradas.TryGetValue(n, out var el)) continue;
                string r = _mapa.FijarNivel(app, el.Label, 2);
                if (!r.Contains("no encuentro", StringComparison.OrdinalIgnoreCase)) puestos2++;
            }

        string resumen = puestos1 == 0
            ? "el maestro no reconoció navegación permanente en esta pantalla"
            : $"{puestos1} en el primer nivel"
              + (puestos2 > 0 ? $" y {puestos2} en el segundo" : "")
              + (explicacion.Length > 0 ? $". {explicacion}" : "");
        LogBus.Log("maestro", $"lección aplicada a «{app}»: {resumen}");
        return new Leccion(puestos1, puestos2, resumen);
    }
}
