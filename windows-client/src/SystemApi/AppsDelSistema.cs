using U.WindowsClient.Diagnostics;
using U.WindowsClient.Navigation;

namespace U.WindowsClient.SystemApi;

/// <summary>
/// TODAS las aplicaciones de esta máquina, como las vería una persona en el menú Inicio.
/// </summary>
/// <remarks>
/// Se lee <c>shell:AppsFolder</c> y no la carpeta de accesos directos, y esa es toda la diferencia:
/// medido el 2026-08-23 en esta máquina, la carpeta del menú Inicio tiene <b>93</b> accesos directos
/// y ni «Microsoft To Do» ni «Claude» están entre ellos —son apps empaquetadas y no tienen .lnk—;
/// <c>shell:AppsFolder</c> tiene <b>143</b> y las dos sí. Pedir «abre microsoft to do» fallaba en
/// 16 ms, y esos 16 ms eran la pista: no es que fallara al abrir, es que ni lo intentaba.
///
/// Se lanza con <c>explorer.exe shell:AppsFolder\{AppId}</c>, que es la única forma de arrancar una
/// app empaquetada sin conocer su ejecutable — y que funciona igual para las clásicas, así que no
/// hacen falta dos caminos.
///
/// EL CATÁLOGO SE LEE UNA VEZ Y SE GUARDA. Recorrer 143 entradas por COM cuesta cientos de
/// milisegundos, y quien pide abrir algo está esperando. Se instala y se desinstala software cada
/// pocas semanas, no cada minuto; si algo no aparece, cerrar y abrir Ü lo recoge.
/// </remarks>
public static class AppsDelSistema
{
    private static IReadOnlyList<AbrirSegunElNucleo.AppDelSistema>? _cache;

    public static IReadOnlyList<AbrirSegunElNucleo.AppDelSistema> Todas()
    {
        if (_cache != null) return _cache;

        var lista = new List<AbrirSegunElNucleo.AppDelSistema>();
        try
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
            dynamic carpeta = shell.NameSpace("shell:AppsFolder");
            foreach (dynamic item in carpeta.Items())
            {
                string nombre = (string)item.Name;
                string id = (string)item.Path;
                if (nombre.Length > 0 && id.Length > 0)
                    lista.Add(new AbrirSegunElNucleo.AppDelSistema(nombre, id));
            }
            LogBus.Log("apps", $"catálogo del sistema: {lista.Count} aplicación(es)");
        }
        catch (Exception e)
        {
            // Se dice y se sigue: sin catálogo, abrir cae a la vía de siempre (el proceso). Quedarse
            // sin esto empeora una capacidad; tumbar la app la quita entera.
            LogBus.Log("apps", $"no pude leer el catálogo del sistema: {e.Message}");
        }

        _cache = lista;
        return _cache;
    }

    /// <summary>Arranca una app por su identificador del catálogo. Devuelve si se pudo pedir.</summary>
    public static bool Lanzar(string comoSeLanza)
    {
        if (string.IsNullOrWhiteSpace(comoSeLanza)) return false;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe")
            {
                Arguments = "shell:AppsFolder\\" + comoSeLanza,
                UseShellExecute = true,
            });
            LogBus.Log("apps", $"lanzada «{comoSeLanza}» por el catálogo del sistema");
            return true;
        }
        catch (Exception e)
        {
            LogBus.Log("apps", $"no pude lanzar «{comoSeLanza}»: {e.Message}");
            return false;
        }
    }
}
