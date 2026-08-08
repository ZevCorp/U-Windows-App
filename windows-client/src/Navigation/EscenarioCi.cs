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

    /// <summary>Lo exigible de un escenario: qué app y qué mínimos tiene que sostener el núcleo.</summary>
    public sealed record Escenario(string App, int Pantallas, int Declarados, int ConAccion, string Creada);

    /// <summary>Los escenarios grabados, por orden alfabético para que el informe sea estable.</summary>
    public static IReadOnlyList<Escenario> Todos()
    {
        var r = new List<Escenario>();
        try
        {
            if (!Directory.Exists(Carpeta)) return r;
            foreach (var f in Directory.GetFiles(Carpeta, "*.json").OrderBy(x => x))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(f));
                var raiz = doc.RootElement;
                var min = raiz.GetProperty("minimos");
                r.Add(new Escenario(
                    raiz.GetProperty("app").GetString() ?? "",
                    min.GetProperty("pantallas").GetInt32(),
                    min.GetProperty("declarados").GetInt32(),
                    min.GetProperty("conAccion").GetInt32(),
                    raiz.TryGetProperty("creada", out var c) ? c.GetString() ?? "" : ""));
            }
        }
        catch (Exception e) { LogBus.Log("ci", $"no se pudieron leer los escenarios: {e.Message}"); }
        return r;
    }

    /// <summary>
    /// ¿El mapa que hay AHORA está a la altura de lo que este escenario exige?
    ///
    /// Se juzga el mapa vivo y no un archivo, porque la prueba es sobre el NÚCLEO QUE ESTÁ
    /// CORRIENDO: quien mapea es este proceso, así que lo que quedó en su mapa es exactamente lo
    /// que ese núcleo sabe hacer. Se cuenta igual que al grabar — misma pregunta, misma cuenta— o
    /// la comparación no significaría nada.
    /// </summary>
    public static (bool Ok, string Detalle) Juzgar(Escenario e, SurfaceMap mapa)
    {
        var (pantallas, declarados, conAccion) = Contar(e.App, mapa);
        bool ok = pantallas >= e.Pantallas && declarados >= e.Declarados && conAccion >= e.ConAccion;
        return (ok, $"pantallas {pantallas}/{e.Pantallas} · declarados {declarados}/{e.Declarados} "
                  + $"· con acción {conAccion}/{e.ConAccion}");
    }

    /// <summary>Las tres cuentas que definen «salió bien». Una sola función: grabar y juzgar tienen
    /// que medir lo mismo, y dos copias de una cuenta acaban midiendo cosas distintas.</summary>
    private static (int Pantallas, int Declarados, int ConAccion) Contar(string app, SurfaceMap mapa)
    {
        bool DeLaApp(string id) => SurfaceMap.AppDe(id).Equals(app, StringComparison.OrdinalIgnoreCase);
        return (
            mapa.Nodes.Keys.Count(DeLaApp),
            mapa.Edges().Count(x => DeLaApp(x.From) && x.Info.NivelFijado && x.Info.NivelNav >= 0),
            mapa.Edges().Count(x => DeLaApp(x.From) && x.Info.Selector.Length > 0 && !SurfaceMap.EsPuerta(x.To)));
    }

    /// <summary>Olvidar un escenario. Devuelve false si no había ninguno con ese nombre.</summary>
    public static bool Olvidar(string app)
    {
        try
        {
            string ruta = Path.Combine(Carpeta, $"{app.Replace(".exe", "")}.json");
            if (!File.Exists(ruta)) return false;
            File.Delete(ruta);
            LogBus.Log("ci", $"escenario de «{app}» olvidado");
            return true;
        }
        catch (Exception e) { LogBus.Log("ci", $"no se pudo olvidar «{app}»: {e.Message}"); return false; }
    }

    /// <summary>Congelar el resultado del mapeo recién hecho como escenario exigible.</summary>
    public static void Guardar(string app, SurfaceMap mapa)
    {
        try
        {
            var (pantallas, declarados, conAccion) = Contar(app, mapa);
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
