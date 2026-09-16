namespace U.Graph.Surfaces;

/// <summary>
/// CÓMO SE ESCRIBE EN LA VENTANA DE TRABAJO: por el patrón Value cuando hay campo, por teclado cuando
/// es una terminal, y nunca a ciegas. Promesa 235 (spec 021). Pura.
/// </summary>
/// <remarks>
/// EL 2026-09-14 ESCRIBIR FALLÓ DE DOS FORMAS EN UNA MISMA TAREA. Con `target` por nombre («Git Bash»,
/// «Preguntar a Google») el nombre se mandaba tal cual como selector y salía «no encontré el elemento «»».
/// Sin `target`, en Windows Terminal y en Git Bash, salía «NO escribo: no hay ningún campo de texto
/// abierto», porque una consola no expone ningún control con ValuePattern: no había forma de escribir
/// en una terminal, nunca. Esta clase decide por dónde va la escritura; UiaSurface pone los medios.
///
/// LA TERMINAL SE TECLEA porque es lo único que escucha: el teclado de Windows va a la ventana con el
/// foco, así que se trae al frente un instante y se devuelve el foco al terminar, igual que el clic
/// físico de la promesa 234. Sin campo y sin terminal no se escribe: escribir sobre lo seleccionado es
/// renombrar (2026-08-03), y eso se llamó éxito una vez.
/// </remarks>
public static class ComoSeEscribe
{
    public enum Via
    {
        /// <summary>Por el patrón Value del campo: sin foco, sin ratón.</summary>
        Valor,
        /// <summary>Por teclado, con la ventana delante un instante: para consolas.</summary>
        Teclado,
        /// <summary>No se escribe: no hay dónde, y a ciegas se renombra.</summary>
        SinCampo,
    }

    private static readonly string[] Procesos =
        { "windowsterminal", "openconsole", "conhost", "mintty", "cmd", "powershell", "powershell_ise", "pwsh", "wt", "alacritty", "wezterm-gui" };
    private static readonly string[] Clases =
        { "CASCADIA_HOSTING_WINDOW_CLASS", "ConsoleWindowClass", "mintty", "PseudoConsoleWindow" };

    /// <param name="proceso">Nombre del proceso, con o sin «.exe».</param>
    /// <param name="clase">Clase Win32 de la ventana, o vacío.</param>
    public static bool EsTerminal(string proceso, string clase)
    {
        string p = (proceso ?? "").Trim().ToLowerInvariant();
        if (p.EndsWith(".exe", StringComparison.Ordinal)) p = p[..^4];
        if (p.Length > 0 && Array.IndexOf(Procesos, p) >= 0) return true;
        string c = (clase ?? "").Trim();
        return c.Length > 0 && Array.Exists(Clases, k => string.Equals(k, c, StringComparison.OrdinalIgnoreCase));
    }

    public static Via Decidir(bool hayCampo, bool esTerminal)
        => hayCampo ? Via.Valor : esTerminal ? Via.Teclado : Via.SinCampo;

    /// <summary>
    /// ¿EL CAMPO SE QUEDÓ DE VERDAD CON LO QUE SE LE ESCRIBIÓ? Promesa 243 (spec 024).
    /// </summary>
    /// <remarks>
    /// EL EDITOR DE UN SITIO MODERNO ACEPTA LA ORDEN Y NO GUARDA NADA. Medido con una sonda sobre el
    /// campo real de Instagram el 2026-09-15: <c>ValuePattern.SetValue</c> no lanza, no devuelve error y
    /// el valor sigue siendo el de antes; teclear con el teclado sí entra. La razón es que ese campo es
    /// un contenteditable gobernado por JavaScript, y escribir su valor por accesibilidad no dispara los
    /// eventos de entrada que ese JavaScript escucha, así que el framework nunca se entera. Ese día la
    /// herramienta contestó dos veces «escribí «¡Ey, TGM! …» y confirmé con Enter» con la caja vacía, y
    /// la voz llegó a decir «ya quedó enviado» de un mensaje que no existía.
    ///
    /// LO QUE NO SE PUEDE LEER NO SE JUZGA. Hay controles que no devuelven su valor, y sobre ellos no hay
    /// forma de saber si cuajó: ahí se deja pasar, que es exactamente lo de siempre. Esta comprobación
    /// actúa sobre una PRUEBA de que el texto no entró, nunca sobre una sospecha — al revés, un campo
    /// perfectamente escrito acabaría tecleándose encima por no poder leerse.
    /// </remarks>
    /// <param name="pedido">Lo que se mandó escribir.</param>
    /// <param name="leido">Lo que el campo dice tener ahora, o null si no se pudo leer.</param>
    public static bool Cuajo(string pedido, string? leido)
    {
        string quiero = Aplanado(pedido);
        if (quiero.Length == 0) return true;   // escribir vacío no se puede desmentir
        if (leido == null) return true;        // ilegible: no se juzga
        return Aplanado(leido).Contains(Muestra(quiero), StringComparison.Ordinal);
    }

    /// <summary>
    /// EL TEXTO, NO SU FORMATO (promesa 247). Un editor normaliza saltos y espacios al guardar, y exigir
    /// el texto literal leía eso como «no escribió nada»: el 2026-09-16 el Bloc de notas DECÍA tener el
    /// informe —se lee en el log— y la comprobación dio fallo porque los saltos no coincidían.
    /// </summary>
    private static string Aplanado(string? t)
    {
        if (string.IsNullOrEmpty(t)) return "";
        var sb = new System.Text.StringBuilder(t.Length);
        bool espacio = false;
        foreach (char c in t)
        {
            if (char.IsWhiteSpace(c)) { espacio = sb.Length > 0; continue; }
            if (espacio) { sb.Append(' '); espacio = false; }
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// De un texto largo basta reconocer su comienzo: un editor puede recortar, envolver o paginar el
    /// resto, y comparar el informe entero es pedirle que no toque una coma.
    /// </summary>
    private static string Muestra(string aplanado)
        => aplanado.Length <= 60 ? aplanado : aplanado[..60];

    /// <summary>Lo que se sabe después de escribir. Tres respuestas, no dos (promesa 247, spec 026).</summary>
    public enum Veredicto
    {
        /// <summary>El campo enseña el texto.</summary>
        Cuajo,
        /// <summary>El campo enseña otra cosa: el texto fue a parar a otro sitio.</summary>
        NoCuajo,
        /// <summary>El campo no cuenta lo que tiene, así que no hay forma de saberlo.</summary>
        MudoNoSeSabe,
    }

    /// <summary>
    /// QUÉ SE SABE DESPUÉS DE ESCRIBIR. Promesa 247 (spec 026).
    /// </summary>
    /// <remarks>
    /// UN CAMPO MUDO NO ES UN CAMPO QUE FALLÓ, y confundirlos costó un informe escrito CUATRO VECES el
    /// 2026-09-16. Google Docs dibuja el documento en un lienzo y no expone su contenido por
    /// accesibilidad: tras teclear responde «». La comprobación de la spec 024 leyó ese vacío como «no
    /// entró», contestó «no pude escribir», y el modelo —haciendo lo correcto con lo que se le dijo—
    /// reescribió el informe entero. Cuatro veces, dos minutos, y el documento repetido.
    ///
    /// Dar por falso lo que no se pudo comprobar es el mismo vicio que dar por cierto lo que no se
    /// comprobó, solo que al revés y más caro: el daño de la duda es un aviso; el de la duplicación es
    /// el trabajo del usuario estropeado.
    /// </remarks>
    public static Veredicto TrasEscribir(string pedido, string? antes, string? despues)
    {
        if (Aplanado(pedido).Length == 0) return Veredicto.Cuajo;
        if (despues == null) return Veredicto.MudoNoSeSabe;
        if (Cuajo(pedido, despues)) return Veredicto.Cuajo;
        // El campo no dice nada: ni lo de antes ni lo pedido. No se puede saber, y no se inventa.
        if (Aplanado(despues).Length == 0) return Veredicto.MudoNoSeSabe;
        return Veredicto.NoCuajo;
    }

    /// <summary>¿Se da por escrito? Solo el «no cuajó» es un fallo (promesa 247).</summary>
    public static bool SeDaPorEscrito(Veredicto v) => v != Veredicto.NoCuajo;

    /// <summary>El error nombra lo pedido y dónde se buscó: «no encontré el elemento «»» no decía ninguna de las dos.</summary>
    public static string NoEncontre(string campo, string ventana)
        => $"no encontré ningún campo de texto «{campo}» en la ventana «{ventana}»";
}
