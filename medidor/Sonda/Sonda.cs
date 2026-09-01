using System.Diagnostics;
using U.Graph.Surfaces;
using Medidor;

namespace Medidor.Sonda;

/// <summary>
/// LA SONDA DE FASE 0. Se corre en un PC de urgencias, CON U.exe ya corriendo, y deja un log con
/// horas que se pega en el PR (nivel 4). No mide nada del estudio: contesta las cinco preguntas que
/// deciden si la rama SAP fina del medidor es viable o si hay que caer al modo foreground-only.
///
/// Cada respuesta describe lo que se vio, nunca concluye una causa que no pueda distinguir
/// (aprendizaje nº2). Y todo con enlace tardío puro, que es lo que contesta en minutos lo que la
/// deducción no resuelve en semanas (nº13).
/// </summary>
internal static class Sonda
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var log = new List<string>();
        void Di(string s) { var l = $"[{DateTime.Now:HH:mm:ss}] {s}"; Console.WriteLine(l); log.Add(l); }

        Di("SONDA DEL MEDIDOR — fase 0. Correr con U.exe abierto y SAP GUI delante.");
        Di($"máquina={Environment.MachineName}  os={Environment.OSVersion.VersionString}");
        Di($"¿U.exe corriendo? {(Process.GetProcessesByName("U").Length > 0 ? "sí" : "NO — abrelo para la prueba de convivencia")}");

        // (1) ¿Conviven dos engines COM sobre el mismo SAP GUI?
        Di("");
        Di("(1) Attach a SAP GUI Scripting con U.exe también atachado…");
        SapGuiSurface? sap = null;
        try
        {
            sap = new SapGuiSurface();
            var disp = sap.Check();
            Di(disp.Available
                ? "    ✓ SAP disponible: el ROT wrapper dio un engine independiente. Dos procesos conviven."
                : $"    ✋ SAP no disponible ahora mismo: {disp.Reason}");
        }
        catch (Exception e)
        {
            for (var x = e; x != null; x = x.InnerException) Di($"    ✘ {x.GetType().Name}: {x.Message}");
        }

        // (2) Coste por tick de Identity(), y respeto a Busy, durante un rato de uso real.
        if (sap != null)
        {
            Di("");
            Di("(2) Identity() cada 1 s durante 60 s — usá SAP normalmente mientras corre.");
            long peor = 0, suma = 0; int n = 0, saltados = 0;
            var reloj = Stopwatch.StartNew();
            while (reloj.Elapsed < TimeSpan.FromSeconds(60))
            {
                try
                {
                    if (sap.IsBusy()) { saltados++; }
                    else
                    {
                        var t = Stopwatch.StartNew();
                        var id = sap.Identity();
                        t.Stop();
                        peor = Math.Max(peor, t.ElapsedMilliseconds); suma += t.ElapsedMilliseconds; n++;
                        if (n % 10 == 0) Di($"    tick {n}: {id.Url}  ({t.ElapsedMilliseconds} ms)");
                    }
                }
                catch (Exception e) { Di($"    ✘ tick: {e.Message}"); }
                Thread.Sleep(1000);
            }
            Di($"    media={(n > 0 ? suma / n : 0)} ms · PEOR={peor} ms · saltados por Busy={saltados}");
            Di(peor < 200
                ? "    ✓ el coste por tick es bajo: la cadencia de 1-2 s no debería degradar SAP."
                : "    ✋ un tick caro: revisar antes de subir la cadencia (aprendizaje nº8, coste por iteración primero).");
        }

        // (3) La regla de identidad del paciente sobre la pantalla actual.
        if (sap != null)
        {
            Di("");
            Di("(3) Extracción del ID de paciente sobre la pantalla actual (título SAP + regex).");
            try
            {
                var id = sap.Identity();
                var partes = Normalizador.PartesSap(id.Url);
                Di($"    superficie={id.Url}  tcode={partes?.Tcode ?? "?"}");
                Di($"    título SAP (NO se guarda, solo para calibrar la regex): «{id.Title}»");
                // La regla de ejemplo del HGM: PATNR en el título. Ajustar el patrón según lo visto.
                var patron = args.Length > 0 ? args[0] : @"[Pp]aciente\s+0*(\d{5,10})";
                var reglas = new List<ReglaDeIdentidad> { new("sonda", "*", "titulo_sap", null, patron, "digitos_sin_ceros") };
                var extraido = ReglasDeIdentidad.Extraer(reglas, partes?.Tcode ?? "", _ => null, id.Title);
                Di(extraido != null
                    ? $"    ✓ el patrón «{patron}» extrajo un ID normalizado (huella: {Huella.DeIdentificador(new byte[32], extraido.Value.IdNormalizado)[..8]}…). Ajustalo si no cuadra."
                    : $"    ✋ el patrón «{patron}» no encontró ID en este título. Probá con -patron o mirá si el ID va en un campo (fase 2).");
            }
            catch (Exception e) { Di($"    ✘ {e.Message}"); }
        }

        Di("");
        Di("(4) Ganchos globales de ratón/teclado bajo el antivirus del hospital → los prueba la App, no la sonda.");
        Di("(5) Los eventos COM StartRequest/EndRequest son fase 2; la sonda de fase 0 se queda en identidad.");

        // Dejar el log en disco, con nombre por fecha, para pegarlo en el PR.
        try
        {
            var ruta = Path.Combine(AppContext.BaseDirectory, $"sonda-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            File.WriteAllLines(ruta, log);
            Di($"log guardado en {ruta} — pegalo en el PR (con horas, no «probado»).");
        }
        catch { }

        sap?.Dispose();
        return 0;
    }
}
