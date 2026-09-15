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
        string quiero = (pedido ?? "").Trim();
        if (quiero.Length == 0) return true;   // escribir vacío no se puede desmentir
        if (leido == null) return true;        // ilegible: no se juzga
        return leido.Trim().Contains(quiero, StringComparison.Ordinal);
    }

    /// <summary>El error nombra lo pedido y dónde se buscó: «no encontré el elemento «»» no decía ninguna de las dos.</summary>
    public static string NoEncontre(string campo, string ventana)
        => $"no encontré ningún campo de texto «{campo}» en la ventana «{ventana}»";
}
