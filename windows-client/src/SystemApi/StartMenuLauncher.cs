using System.Diagnostics;
using System.IO;
using System.Linq;

namespace U.WindowsClient.SystemApi;

/// <summary>
/// Lanza apps por su ACCESO DIRECTO del menú Inicio. Resuelve NOMBRES VISIBLES ("Google Chrome",
/// "SAP Logon") que el shell no encuentra como comando —el ejecutable real es <c>chrome.exe</c>,
/// <c>saplogon.exe</c>—: casi toda app instalada deja un <c>.lnk</c> en el menú Inicio, y lanzarlo la
/// abre. Casar por NOMBRE normalizado del acceso directo, sin COM.
///
/// Es la lógica que ya tenía el SurfaceNavigator; se extrajo aquí para que <see cref="WindowsSystemApi"/>
/// (y por ende <c>launch_app</c> del cerebro) sea igual de robusta. Un solo lugar que mantener.
/// </summary>
public static class StartMenuLauncher
{
    /// <summary>Encuentra el .lnk y lo lanza. Devuelve false si no hay acceso directo que case.</summary>
    public static bool TryLaunch(string appName)
    {
        string? lnk = FindShortcut(appName);
        if (lnk == null) return false;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = lnk, UseShellExecute = true });
            return true;
        }
        catch { return false; }
    }

    /// <summary>Primer <c>.lnk</c> de los menús Inicio (usuario + máquina) cuyo nombre casa con la app.</summary>
    public static string? FindShortcut(string appName)
    {
        string target = Norm(appName);
        if (target.Length == 0) return null;

        foreach (string root in StartMenuRoots())
        {
            if (!Directory.Exists(root)) continue;
            IEnumerable<string> lnks;
            // IgnoreInaccessible: sin esto, una subcarpeta protegida del menú Inicio lanza
            // UnauthorizedAccessException a mitad del recorrido (es perezoso) y mata toda la búsqueda.
            try
            {
                lnks = Directory.EnumerateFiles(root, "*.lnk",
                    new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true });
            }
            catch { continue; }

            // Coincidencia exacta primero (evita que "Notepad++" gane sobre "Notepad"), luego contiene.
            string? exact = null, loose = null;
            foreach (string f in lnks)
            {
                string name = Norm(Path.GetFileNameWithoutExtension(f));
                if (name == target) { exact = f; break; }
                if (loose == null && (name.Contains(target) || target.Contains(name)) && name.Length > 0) loose = f;
            }
            if (exact != null) return exact;
            if (loose != null) return loose;
        }
        return null;
    }

    private static IEnumerable<string> StartMenuRoots()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);       // menú del usuario
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);  // menú de la máquina
    }

    /// <summary>Minúsculas y solo alfanumérico: "SAP Logon" → "saplogon", "Google Chrome" → "googlechrome".</summary>
    private static string Norm(string s) =>
        new string((s ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
}
