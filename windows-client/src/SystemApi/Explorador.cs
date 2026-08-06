using System.IO;
using System.Runtime.InteropServices;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.SystemApi;

/// <summary>Una entrada de una carpeta, ya resuelta contra el disco.</summary>
public sealed record Entrada(string Nombre, bool EsCarpeta, long Bytes, DateTime Modificado)
{
    /// <summary>La extensión real, incluso si Windows la tiene oculta en la vista.</summary>
    public string Extension => EsCarpeta ? "" : Path.GetExtension(Nombre).TrimStart('.').ToLowerInvariant();
}

/// <summary>
/// El explorador de archivos, preguntándole al DISCO lo que hay y a la UI solo cómo se navega.
///
/// La regla ya estaba escrita en <c>docs/graphify.md</c> («para saber QUÉ hay se lee el disco; la UI
/// enseña CÓMO navegar») pero vivía dentro del crawler, en un método privado, así que ni la voz ni
/// el mapa podían usarla. Aquí se promueve a servicio, que es lo que permite lo demás.
///
/// POR QUÉ IMPORTA, con los números medidos en este repo:
///
/// - EXACTITUD. UIA no distingue carpeta de archivo en el explorador de Windows 11: <c>ItemType</c>
///   llegó vacío 30 de 30 veces, y como Windows oculta las extensiones conocidas, «Captura de
///   pantalla 1» es un .png que no lo parece — por ahí se coló un recorrido hasta dentro de Photos.
///   <c>Directory.Exists</c> contesta sin margen de error.
/// - VELOCIDAD. Leer el árbol UIA de una pantalla cuesta cientos de ms y se ha medido en ~700 ms
///   por salto; enumerar un directorio son microsegundos. Y no hay tope de 400 elementos ni
///   recorte a 40 por tipo: el disco los da todos.
/// - COMPLETITUD. UIA solo ve lo que está en pantalla (<c>IsOffscreen</c> descarta el resto). Una
///   carpeta de 300 archivos con scroll es, para UIA, los ~20 visibles. Para el disco son 300.
///
/// LO QUE NO HACE, a propósito: escribir. El explorador NO refresca su árbol UIA ante cambios
/// hechos por fuera (medido: una carpeta creada en disco con la ventana abierta seguía invisible un
/// segundo después). Mover o renombrar por <c>System.IO</c> dejaría la pantalla mintiendo y sin
/// Ctrl+Z. Leer va por disco; tocar sigue yendo por la UI, con <c>map_take</c>.
/// </summary>
public static class Explorador
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    /// <summary>Cuántas entradas se devuelven como mucho en una lista. Un directorio puede tener
    /// miles y el modelo no necesita verlos todos para decidir; el conteo real siempre se dice.</summary>
    private const int TopeListado = 200;

    /// <summary>
    /// La ruta que el explorador tiene abierta en la ventana de primer plano, preguntándole a él.
    ///
    /// Se le pregunta al shell y no se deduce del título: el título es «Descargas», no
    /// «C:\Users\…\Downloads», y hay carpetas que comparten nombre. Con <c>Shell.Application</c> la
    /// ventana dice su propia ruta.
    /// </summary>
    public static string RutaEnPrimerPlano() => RutaDe(GetForegroundWindow());

    /// <summary>La ruta abierta en una ventana concreta, o cadena vacía si esa ventana no es una
    /// carpeta del explorador.</summary>
    public static string RutaDe(IntPtr hwnd)
    {
        try
        {
            var t = Type.GetTypeFromProgID("Shell.Application");
            if (t == null) return "";
            dynamic? shell = Activator.CreateInstance(t);
            if (shell == null) return "";
            foreach (dynamic w in shell.Windows())
            {
                try
                {
                    if ((IntPtr)(long)w.HWND != hwnd) continue;
                    return (string)(w.Document.Folder.Self.Path ?? "");
                }
                catch { }
            }
        }
        catch { }
        return "";
    }

    /// <summary>¿Hay una ventana de carpeta del explorador en primer plano?</summary>
    public static bool EnElExplorador() => RutaEnPrimerPlano().Length > 0;

    /// <summary>
    /// Lo que hay dentro de una carpeta, del disco. Carpetas primero y luego archivos, cada grupo
    /// por nombre, que es el orden en que el explorador las enseña por defecto y así lo que lee el
    /// modelo se parece a lo que ve el usuario.
    /// </summary>
    /// <param name="ruta">La carpeta. Vacío = la que esté abierta en primer plano.</param>
    /// <param name="filtro">Subcadena a buscar en el nombre. Vacío = todo.</param>
    public static IReadOnlyList<Entrada> Listar(string ruta, string filtro = "")
    {
        var salida = new List<Entrada>();
        if (ruta.Length == 0) ruta = RutaEnPrimerPlano();
        if (ruta.Length == 0 || !Directory.Exists(ruta)) return salida;

        try
        {
            var dir = new DirectoryInfo(ruta);

            foreach (var d in dir.EnumerateDirectories())
            {
                if (!Coincide(d.Name, filtro)) continue;
                if (d.Attributes.HasFlag(FileAttributes.Hidden)) continue;
                salida.Add(new Entrada(d.Name, true, 0, d.LastWriteTime));
            }
            foreach (var f in dir.EnumerateFiles())
            {
                if (!Coincide(f.Name, filtro)) continue;
                if (f.Attributes.HasFlag(FileAttributes.Hidden)) continue;
                salida.Add(new Entrada(f.Name, false, f.Length, f.LastWriteTime));
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }

        return salida
            .OrderByDescending(e => e.EsCarpeta)
            .ThenBy(e => e.Nombre, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static bool Coincide(string nombre, string filtro) =>
        filtro.Length == 0 || nombre.Contains(filtro, StringComparison.CurrentCultureIgnoreCase);

    /// <summary>
    /// Busca por nombre en la carpeta y sus subcarpetas, del disco.
    ///
    /// Sustituye a escribir en la caja de búsqueda del explorador, que tarda segundos, indexa a su
    /// aire y deja la ventana en un estado del que luego hay que salir. Aquí no se toca la pantalla.
    /// </summary>
    public static IReadOnlyList<string> Buscar(string ruta, string patron, int tope = 60)
    {
        var hallazgos = new List<string>();
        if (ruta.Length == 0) ruta = RutaEnPrimerPlano();
        if (ruta.Length == 0 || patron.Length == 0 || !Directory.Exists(ruta)) return hallazgos;

        // Enumeración perezosa y con tope: una búsqueda en C:\ entera no puede colgar la voz.
        // IgnoreInaccessible evita que una sola carpeta protegida aborte todo el recorrido.
        var opciones = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            MaxRecursionDepth = 6,
        };

        try
        {
            foreach (string p in Directory.EnumerateFileSystemEntries(ruta, "*" + patron + "*", opciones))
            {
                hallazgos.Add(p);
                if (hallazgos.Count >= tope) break;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return hallazgos;
    }

    /// <summary>
    /// Lleva el explorador a una ruta EN UN SOLO SALTO, diciéndoselo al shell.
    ///
    /// Es la diferencia entre navegar y encadenar clics. Llegar a
    /// <c>C:\Users\yo\Documentos\2026\facturas</c> pulsando carpeta por carpeta son cuatro saltos de
    /// ~700 ms cada uno, cuatro lecturas del árbol y cuatro ocasiones de resolver mal un nombre
    /// repetido. Con <c>Navigate</c> es una llamada COM y no se resuelve ningún elemento: no hay
    /// nada que acertar. Si no hay ventana abierta, se abre una ya en el destino.
    /// </summary>
    /// <returns>La ruta a la que se fue, o cadena vacía si no existe.</returns>
    public static string Navegar(string ruta)
    {
        if (ruta.Length == 0) return "";
        ruta = Expandir(ruta);
        if (!Directory.Exists(ruta)) return "";

        IntPtr fg = GetForegroundWindow();
        try
        {
            var t = Type.GetTypeFromProgID("Shell.Application");
            if (t != null)
            {
                dynamic? shell = Activator.CreateInstance(t);
                if (shell != null)
                {
                    // La de primer plano primero: si el usuario está mirando una ventana, es ESA la
                    // que espera que se mueva, no otra que hubiera abierta detrás.
                    dynamic? elegida = null;
                    foreach (dynamic w in shell.Windows())
                    {
                        try
                        {
                            if (((string)(w.Document.Folder.Self.Path ?? "")).Length == 0) continue;
                            elegida ??= w;
                            if ((IntPtr)(long)w.HWND == fg) { elegida = w; break; }
                        }
                        catch { }
                    }

                    if (elegida != null)
                    {
                        elegida.Navigate(ruta);
                        LogBus.Log("explorador", $"navegado a «{ruta}» en un salto (COM)");
                        return ruta;
                    }
                }
            }
        }
        catch (Exception e) { LogBus.Log("explorador", $"Navigate falló ({e.GetType().Name}); se abre ventana nueva"); }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{ruta}\"")
            { UseShellExecute = true });
            LogBus.Log("explorador", $"abierta ventana nueva en «{ruta}»");
            return ruta;
        }
        catch (Exception e) { LogBus.Log("explorador", $"no se pudo abrir «{ruta}»: {e.Message}"); return ""; }
    }

    /// <summary>
    /// Convierte lo que diría una persona en una ruta real: «descargas», «mis documentos», «%TEMP%»,
    /// «~\notas». Sin esto el modelo tiene que saberse el perfil del usuario para navegar a ningún
    /// sitio, y lo que hace en su lugar es inventárselo.
    /// </summary>
    public static string Expandir(string entrada)
    {
        string s = Environment.ExpandEnvironmentVariables(entrada.Trim().Trim('"'));
        if (s.StartsWith("~")) s = Path.Combine(Perfil, s[1..].TrimStart('\\', '/'));
        if (Path.IsPathRooted(s)) return Path.GetFullPath(s);

        string conocida = PorNombre(s);
        if (conocida.Length > 0) return conocida;

        // Relativa: se resuelve contra donde está el explorador ahora, que es lo que quiere decir
        // alguien cuando dice «entra en facturas» sin más contexto.
        string aqui = RutaEnPrimerPlano();
        return aqui.Length > 0 ? Path.GetFullPath(Path.Combine(aqui, s)) : s;
    }

    private static string Perfil => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>Las carpetas que la gente nombra hablando. Se piden al sistema en vez de componerlas
    /// con el perfil: en un equipo con OneDrive, «Documentos» NO está bajo el perfil.</summary>
    private static string PorNombre(string nombre)
    {
        string n = nombre.Trim().ToLowerInvariant().Replace("mis ", "").Replace("mi ", "");
        var carpeta = n switch
        {
            "escritorio" or "desktop" => Environment.SpecialFolder.DesktopDirectory,
            "documentos" or "documents" => Environment.SpecialFolder.MyDocuments,
            "imágenes" or "imagenes" or "fotos" or "pictures" => Environment.SpecialFolder.MyPictures,
            "música" or "musica" or "music" => Environment.SpecialFolder.MyMusic,
            "vídeos" or "videos" => Environment.SpecialFolder.MyVideos,
            _ => (Environment.SpecialFolder?)null,
        };
        if (carpeta != null) return Environment.GetFolderPath(carpeta.Value);

        // Descargas no tiene SpecialFolder en .NET; es la única que hay que componer.
        if (n is "descargas" or "downloads") return Path.Combine(Perfil, "Downloads");
        return "";
    }

    /// <summary>
    /// El listado en el texto que lee el modelo. Carpetas marcadas como tales y archivos con su
    /// extensión REAL — que es justo lo que UIA no puede decir — y el total sin recortar, para que
    /// «hay 12» y «hay 300 y te enseño 200» no se confundan.
    /// </summary>
    public static string Describir(string ruta, string filtro = "")
    {
        if (ruta.Length == 0) ruta = RutaEnPrimerPlano();
        if (ruta.Length == 0) return "No hay ninguna carpeta del explorador en primer plano.";
        if (!Directory.Exists(ruta)) return $"La carpeta «{ruta}» no existe.";

        var todo = Listar(ruta, filtro);
        int carpetas = todo.Count(e => e.EsCarpeta);
        int archivos = todo.Count - carpetas;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{ruta} · {carpetas} carpeta(s), {archivos} archivo(s)"
            + (filtro.Length > 0 ? $" que contienen «{filtro}»" : ""));

        foreach (var e in todo.Take(TopeListado))
            sb.AppendLine(e.EsCarpeta
                ? $"  [carpeta] {e.Nombre}"
                : $"  {e.Nombre}  ({e.Extension}, {Tamano(e.Bytes)})");

        if (todo.Count > TopeListado)
            sb.AppendLine($"  … y {todo.Count - TopeListado} más (usa el filtro para acotar)");

        return sb.ToString().TrimEnd();
    }

    private static string Tamano(long b) => b switch
    {
        < 1024 => $"{b} B",
        < 1024 * 1024 => $"{b / 1024.0:F0} KB",
        < 1024L * 1024 * 1024 => $"{b / (1024.0 * 1024):F1} MB",
        _ => $"{b / (1024.0 * 1024 * 1024):F1} GB",
    };
}
