using System.IO;
using System.Text.Json;

namespace U.WindowsClient.Navigation;

/// <summary>
/// DÓNDE VIVE EL REPO, según lo apuntó el registro de C:\U-versiones. Es lo único que quedó de la
/// maquinaria de versiones del núcleo viejo (v0/v1, la tira, el salto entre binarios): aquella
/// existía para poder deshacer ediciones del SurfaceMap congelado, y murió con él en la gran
/// limpieza (2026-08-30; su foto vive en la rama experimentos-viejos). El visor del 8792 sigue
/// necesitando saber dónde está el repo para servir nucleo/visor/ desde el código fuente, y esa
/// respuesta ya vivía aquí — se queda la respuesta, se va la maquinaria.
/// </summary>
public static class NucleoVersiones
{
    /// <summary>La raíz del registro. Fija a propósito: la escriben los scripts y la lee la app.</summary>
    public static string Raiz =>
        Environment.GetEnvironmentVariable("U_VERSIONES_DIR") is { Length: > 0 } v ? v : @"C:\U-versiones";

    private sealed class RegistroCrudo
    {
        public string Repo { get; set; } = "";
    }

    /// <summary>El repositorio del que sale el visor. Vacío si no consta.</summary>
    public static string Repo()
    {
        try
        {
            string registro = Path.Combine(Raiz, "versiones.json");
            // El registro es herencia del sistema de versiones; si no está, el repo también puede
            // constar directo en un repo.txt (lo deja scripts/verificar.ps1 o una mano).
            if (File.Exists(registro))
            {
                string repo = JsonSerializer.Deserialize<RegistroCrudo>(File.ReadAllText(registro))?.Repo ?? "";
                if (repo.Length > 0) return repo;
            }
            string directo = Path.Combine(Raiz, "repo.txt");
            return File.Exists(directo) ? File.ReadAllText(directo).Trim() : "";
        }
        catch { return ""; }
    }
}
