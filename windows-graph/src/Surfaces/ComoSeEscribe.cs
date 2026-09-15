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

    /// <summary>El error nombra lo pedido y dónde se buscó: «no encontré el elemento «»» no decía ninguna de las dos.</summary>
    public static string NoEncontre(string campo, string ventana)
        => $"no encontré ningún campo de texto «{campo}» en la ventana «{ventana}»";
}
