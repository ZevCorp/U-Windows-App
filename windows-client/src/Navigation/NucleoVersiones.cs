using System.IO;
using System.Text.Json;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Navigation;

/// <summary>
/// LAS VERSIONES DEL NÚCLEO: qué versiones del grafo existen, cuál está corriendo, cuál se está
/// editando, y cómo saltar de una a otra. Una pregunta, un sitio.
///
/// Por qué existe (2026-08-08, pedido por el usuario): el candado hace que editar el núcleo sea
/// una decisión consciente, pero una decisión consciente también puede romper algo. La salida no
/// es prohibir más: es que romper no cueste nada. La v0 es el núcleo congelado; se edita sobre la
/// versión elegida, y volver a la que funcionaba es UN clic en la tira de la izquierda.
///
/// Cómo funciona el salto: cada versión está PRECOMPILADA en su directorio
/// (<c>C:\U-versiones\vN\bin</c>, lo hace <c>scripts\version-nucleo.ps1</c>). Cambiar no compila
/// nada: arranca ese binario —que hereda el entorno entero, datos incluidos— y cierra este. Se
/// descartó el intercambio en caliente dentro del proceso a propósito: el estado estático y los
/// eventos de WPF colgados del objeto viejo lo hacen frágil, y un sistema de seguridad no puede
/// estar construido sobre algo frágil.
///
/// El registro (<c>versiones.json</c>) vive JUNTO A LOS BINARIOS y no en el repo: es la única
/// copia, la leen el runtime y los scripts, y dos copias de una misma verdad acaban siempre
/// desincronizadas. Las instantáneas del código (<c>versiones/nucleo/vN.cs</c>) sí van al repo,
/// porque son código.
/// </summary>
public static class NucleoVersiones
{
    /// <summary>La raíz de todo lo versionado. Fija a propósito: la tira, los scripts y el guardián
    /// del candado tienen que coincidir sin pasarse rutas entre ellos.</summary>
    public static string Raiz =>
        Environment.GetEnvironmentVariable("U_VERSIONES_DIR") is { Length: > 0 } v ? v : @"C:\U-versiones";

    private static string Registro => Path.Combine(Raiz, "versiones.json");

    public sealed record Version(int N, string Creada, string Nota)
    {
        public string Bin => Path.Combine(Raiz, $"v{N}", "bin");
        public string Exe => Path.Combine(Bin, "U.exe");
        public bool Construida => File.Exists(Exe);
    }

    private sealed class RegistroCrudo
    {
        public int EnEdicion { get; set; } = -1;
        public List<Version> Versiones { get; set; } = new();

        /// <summary>Dónde vive el repo. Lo apunta el script en cada guardado, porque la app corre
        /// desde otro directorio y sin esto no sabría a qué script llamar para crear una versión.</summary>
        public string Repo { get; set; } = "";
    }

    /// <summary>El repositorio del que salen las versiones, según lo apuntó el script.</summary>
    public static string Repo()
    {
        try
        {
            if (!File.Exists(Registro)) return "";
            return JsonSerializer.Deserialize<RegistroCrudo>(File.ReadAllText(Registro))?.Repo ?? "";
        }
        catch { return ""; }
    }

    /// <summary>
    /// Crear una versión nueva desde lo que hay en edición, y dejarla en edición.
    ///
    /// Lo hace el MISMO script que se usa a mano (<c>version-nucleo.ps1 -Crear</c>) y no una copia
    /// del procedimiento aquí dentro: crear una versión es instantánea + compilación + contrato, y
    /// dos implementaciones de eso acabarían divergiendo justo el día que importe. Corre aparte y
    /// tarda su minuto —compila de verdad—, así que se avisa por <paramref name="cuandoTermine"/>.
    /// </summary>
    public static void Crear(string nota, Action<bool, string> cuandoTermine)
    {
        string repo = Repo();
        if (repo.Length == 0 || !Directory.Exists(repo))
        {
            cuandoTermine(false, "no consta dónde está el repo: corre una vez scripts\\version-nucleo.ps1 a mano");
            return;
        }
        string script = Path.Combine(repo, "scripts", "version-nucleo.ps1");
        if (!File.Exists(script)) { cuandoTermine(false, $"no encuentro {script}"); return; }

        Task.Run(() =>
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("powershell")
                {
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -Crear -Nota \"{nota.Replace("\"", "'")}\"",
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    WorkingDirectory = repo,
                };
                using var p = System.Diagnostics.Process.Start(psi)!;
                string salida = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit();
                LogBus.Log("versiones", $"crear versión (código {p.ExitCode}): {salida.Trim()}");
                cuandoTermine(p.ExitCode == 0, p.ExitCode == 0
                    ? "versión nueva creada y compilada"
                    : "no se pudo crear la versión; mira el log de «versiones»");
            }
            catch (Exception e)
            {
                LogBus.Log("versiones", $"crear versión falló: {e.Message}");
                cuandoTermine(false, e.Message);
            }
        });
    }

    /// <summary>Las versiones que constan, con o sin binario (se dice cuál es cuál).</summary>
    public static IReadOnlyList<Version> Todas()
    {
        try
        {
            if (!File.Exists(Registro)) return Array.Empty<Version>();
            var r = JsonSerializer.Deserialize<RegistroCrudo>(File.ReadAllText(Registro));
            return r?.Versiones.OrderBy(v => v.N).ToList() ?? (IReadOnlyList<Version>)Array.Empty<Version>();
        }
        catch (Exception e)
        {
            LogBus.Log("versiones", $"no se pudo leer el registro: {e.Message}");
            return Array.Empty<Version>();
        }
    }

    /// <summary>Qué versión está marcada PARA EDITAR. -1 si ninguna. La marca el usuario con el
    /// script, nunca el agente: es la única cuyo código se puede tocar (con contraseña).</summary>
    public static int EnEdicion()
    {
        try
        {
            if (!File.Exists(Registro)) return -1;
            return JsonSerializer.Deserialize<RegistroCrudo>(File.ReadAllText(Registro))?.EnEdicion ?? -1;
        }
        catch { return -1; }
    }

    /// <summary>
    /// Qué versión ES este proceso. La dice el <c>version.txt</c> que el build dejó junto al exe;
    /// sin él —un build de desarrollo, el estable— se es «dev»: una versión sin número no finge
    /// tener uno.
    /// </summary>
    public static int? Actual()
    {
        try
        {
            string marca = Path.Combine(AppContext.BaseDirectory, "version.txt");
            if (File.Exists(marca) && int.TryParse(File.ReadAllText(marca).Trim(), out int n)) return n;
        }
        catch { }
        return null;
    }

    /// <summary>La última huella del registro, para que la tira sepa si hay que redibujar sin
    /// releer el archivo en cada latido.</summary>
    public static DateTime UltimoCambio()
    {
        try { return File.Exists(Registro) ? File.GetLastWriteTimeUtc(Registro) : DateTime.MinValue; }
        catch { return DateTime.MinValue; }
    }

    /// <summary>
    /// Saltar a otra versión: arrancar su binario y apagar este proceso. El hijo hereda el entorno
    /// entero (U_DATA_DIR, claves, la sonda MCP), así que ve el MISMO terreno y las mismas
    /// enseñanzas: lo que cambia es el núcleo, no el mundo.
    /// </summary>
    public static bool SaltarA(Version v)
    {
        if (!v.Construida)
        {
            LogBus.Log("versiones", $"v{v.N} no tiene binario en {v.Bin}: constrúyela con scripts\\version-nucleo.ps1");
            return false;
        }
        try
        {
            // UseShellExecute=false para que herede el entorno de ESTE proceso, no el del shell.
            var psi = new System.Diagnostics.ProcessStartInfo(v.Exe)
            { UseShellExecute = false, WorkingDirectory = v.Bin };
            System.Diagnostics.Process.Start(psi);
            LogBus.Log("versiones", $"saltando a v{v.N}: lanzada desde {v.Exe}; este proceso se apaga");
            System.Windows.Application.Current.Shutdown();
            return true;
        }
        catch (Exception e)
        {
            LogBus.Log("versiones", $"no se pudo saltar a v{v.N}: {e.Message}");
            return false;
        }
    }
}
