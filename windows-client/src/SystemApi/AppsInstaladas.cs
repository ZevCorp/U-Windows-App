using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace U.WindowsClient.SystemApi;

/// <summary>Una aplicación instalada, tal como la vería alguien en el menú Inicio.</summary>
public sealed record AppInstalada(string Nombre, string Lnk);

/// <summary>
/// Qué aplicaciones hay en este equipo, con su nombre y su icono.
///
/// Se leen del menú Inicio y no del registro ni de la carpeta de programas: el menú Inicio es la
/// lista que el propio Windows considera «las apps de esta persona» —ya trae el nombre visible, ya
/// excluye lo que no se abre solo, y ya incluye las de la Store—. Cualquier otra fuente obliga a
/// reconstruir a mano lo que aquí viene hecho.
///
/// El icono se saca por Win32 y se convierte a un <see cref="BitmapSource"/> de WPF. No se usa
/// System.Drawing: este cliente es WPF puro y arrastrar esa dependencia por un icono sería pagar
/// mucho por poco.
/// </summary>
public static class AppsInstaladas
{
    /// <summary>
    /// Lo que NO es una aplicación aunque tenga acceso directo: desinstaladores, manuales, notas de
    /// la versión, enlaces a la web del fabricante. Ensucian la lista y ninguno se puede mapear.
    /// </summary>
    private static readonly string[] Ruido =
    {
        "desinstal", "uninstall", "readme", "léame", "leeme", "manual", "ayuda", "help",
        "documentación", "documentation", "release notes", "notas de la versión", "sitio web",
        "web site", "website", "licencia", "license", "actualizar", "update",
    };

    /// <summary>Todas las apps del menú Inicio, sin repetir y en orden alfabético.</summary>
    public static IReadOnlyList<AppInstalada> Todas()
    {
        var vistas = new Dictionary<string, AppInstalada>(StringComparer.OrdinalIgnoreCase);

        foreach (string raiz in StartMenuLauncher.StartMenuRoots())
        {
            if (!Directory.Exists(raiz)) continue;
            IEnumerable<string> lnks;
            try
            {
                lnks = Directory.EnumerateFiles(raiz, "*.lnk", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,   // una subcarpeta protegida no puede matar el recorrido
                });
            }
            catch { continue; }

            foreach (string lnk in lnks)
            {
                string nombre;
                try { nombre = Path.GetFileNameWithoutExtension(lnk); } catch { continue; }
                if (nombre.Length == 0) continue;
                if (Ruido.Any(r => nombre.Contains(r, StringComparison.OrdinalIgnoreCase))) continue;
                if (!vistas.ContainsKey(nombre)) vistas[nombre] = new AppInstalada(nombre, lnk);
            }
        }

        return vistas.Values.OrderBy(a => a.Nombre, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>El icono del acceso directo, ya listo para pintar. Null si no lo tiene.</summary>
    public static ImageSource? Icono(string lnk)
    {
        var info = new SHFILEINFO();
        IntPtr ok = SHGetFileInfo(lnk, 0, ref info, (uint)Marshal.SizeOf(info), SHGFI_ICON | SHGFI_LARGEICON);
        if (ok == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
        try
        {
            var img = Imaging.CreateBitmapSourceFromHIcon(
                info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            img.Freeze();   // se pinta desde el hilo de la interfaz y se guarda en una lista: sin
                            // congelar, WPF prohíbe usarlo fuera del hilo que lo creó.
            return img;
        }
        catch { return null; }
        finally { DestroyIcon(info.hIcon); }   // el icono es un recurso del sistema: se devuelve.
    }

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
        ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr hIcon);
}
