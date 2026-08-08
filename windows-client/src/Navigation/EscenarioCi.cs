using System.IO;
using System.Text.Json;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Navigation;

/// <summary>
/// Una prueba REAL convertida en escenario repetible: «mapear esta app tiene que salir al menos
/// así de bien». Es la mitad grabadora del CI local (la otra mitad es scripts\ci-local.ps1, que
/// los reproduce contra cualquier versión del núcleo).
///
/// Nace del botón de paso a paso (2026-08-08, pedido por el usuario): al terminar un mapeo con la
/// casilla «guardar esta prueba para CI» marcada, lo conseguido se congela como MÍNIMO exigible.
/// No se exige igualdad exacta a propósito: el maestro es un modelo y el terreno cambia, así que
/// pedir «exactamente 14 declarados» fallaría por ruido sin que nada se hubiera roto. Se exige el
/// 80% de lo logrado hoy — una versión del núcleo que no llega ni a eso rompió algo de verdad.
///
/// Viven junto a las versiones (<c>C:\U-versiones\escenarios\</c>) y no en el repo: son la vara de
/// medir de ESTA máquina —sus apps, su idioma, sus carpetas— y en otra máquina medirían otra cosa.
/// </summary>
public static class EscenarioCi
{
    public static string Carpeta => Path.Combine(NucleoVersiones.Raiz, "escenarios");

    /// <summary>Congelar el resultado del mapeo recién hecho como escenario exigible.</summary>
    public static void Guardar(string app, SurfaceMap mapa)
    {
        try
        {
            bool DeLaApp(string id) => SurfaceMap.AppDe(id).Equals(app, StringComparison.OrdinalIgnoreCase);
            int pantallas = mapa.Nodes.Keys.Count(DeLaApp);
            int declarados = mapa.Edges().Count(e => DeLaApp(e.From) && e.Info.NivelFijado && e.Info.NivelNav >= 0);
            int conAccion = mapa.Edges().Count(e => DeLaApp(e.From) && e.Info.Selector.Length > 0
                                                    && !SurfaceMap.EsPuerta(e.To));
            if (pantallas == 0)
            {
                LogBus.Log("ci", $"no se guarda escenario de «{app}»: el mapa no tiene ninguna pantalla suya");
                return;
            }

            var escenario = new
            {
                app,
                creada = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                nucleo = NucleoVersiones.Actual() is { } n ? $"v{n}" : "dev",
                // Lo LOGRADO hoy, de referencia; lo EXIGIBLE es el 80%, contra el ruido del maestro.
                logrado = new { pantallas, declarados, conAccion },
                minimos = new
                {
                    pantallas = Math.Max(1, (int)(pantallas * 0.8)),
                    declarados = (int)(declarados * 0.8),
                    conAccion = (int)(conAccion * 0.8),
                },
            };

            Directory.CreateDirectory(Carpeta);
            string ruta = Path.Combine(Carpeta, $"{app.Replace(".exe", "")}.json");
            File.WriteAllText(ruta, JsonSerializer.Serialize(escenario,
                new JsonSerializerOptions { WriteIndented = true }));
            LogBus.Log("ci", $"escenario guardado: «{app}» exige ≥{escenario.minimos.pantallas} pantallas, "
                + $"≥{escenario.minimos.declarados} declarados, ≥{escenario.minimos.conAccion} con acción → {ruta}");
        }
        catch (Exception e) { LogBus.Log("ci", $"no se pudo guardar el escenario de «{app}»: {e.Message}"); }
    }
}
