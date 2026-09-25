using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using U.WindowsClient.Decision;

// LA SONDA DEL CICLO (spec 052, fase 0). Mide el ciclo de main TAL CUAL corre hoy:
//
//   dónde estoy  → map_where_am_i  (MCP de U.exe)
//   qué veo      → map_what_i_see  (MCP: el mismo PuertasDeAhora que usa map_decidir)
//   decide Jev   → ElDecisor.Elegir("jev", …) con ClienteTypeSafe — el mismo código de la app
//   pulsa        → map_take        (MCP: la mano entera, con sus esperas)
//
// Y al lado, las primitivas en crudo, para saber cuánto de cada fase es trabajo y cuánto es harness:
// GetForegroundWindow, UiaReader.Read en proceso, SetCursorPos+SendInput.
//
//   sonda-del-ciclo <salida.jsonl>
//
// Requiere U.exe de main corriendo (8790) y TYPESAFE_API_KEY en el entorno. Sin clave, no decide nadie
// y lo dice: un ciclo medido sin decisor no es el ciclo.

internal static class Programa
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private static int _id;
    private static StreamWriter _out = null!;

    private sealed record Tarea(string Nombre, string AbrirApp, string Objetivo, int MaxPasos);

    // Tareas reversibles y sin datos: abrir, navegar, mirar. Ninguna escribe ni borra nada.
    private static readonly Tarea[] Tareas =
    {
        new("bloc-menu-archivo", "notepad", "abrir el menú Archivo del Bloc de notas", 3),
        new("config-bluetooth", "ms-settings:", "ir a la sección Bluetooth y dispositivos de Configuración", 4),
        new("explorador-documentos", "explorer", "abrir la carpeta Documentos en el Explorador de archivos", 4),
        new("calc-7x8", "calc", "calcular 7 por 8 pulsando los botones de la calculadora: siete, multiplicar, ocho, igual", 6),
        new("config-sistema-pantalla", "ms-settings:", "ir a Sistema y después a Pantalla en Configuración", 5),
    };

    private static int Main(string[] args)
    {
        string salida = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "u-medicion", "ciclo.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(salida)!);
        _out = new StreamWriter(salida, append: true) { AutoFlush = true };

        string? clave = Environment.GetEnvironmentVariable("TYPESAFE_API_KEY");
        if (string.IsNullOrWhiteSpace(clave)) { Console.WriteLine("✘ sin TYPESAFE_API_KEY: no mido un ciclo sin decisor."); return 2; }
        using var jev = new ClienteTypeSafe(clave, 5000, m => Console.WriteLine("    jev: " + m));

        try { Mcp("map_where_am_i", new()); }
        catch (Exception e)
        {
            for (var x = e; x != null; x = x.InnerException) Console.WriteLine($"   ✘ {x.GetType().Name}: {x.Message}");
            Console.WriteLine("✘ no pude hablar con U.exe en 8790: ¿está corriendo?");
            return 3;
        }

        Primitivas();

        foreach (var t in Tareas)
        {
            Console.WriteLine($"\n=== {t.Nombre}: {t.Objetivo}");
            var (abrir, msAbrir) = Mcp("map_open_app", new() { ["app"] = t.AbrirApp });
            Console.WriteLine($"  abrir {msAbrir} ms · {Corta(abrir)}");
            Anota(new { tarea = t.Nombre, fase = "abrir_app", ms = msAbrir, texto = Corta(abrir, 300) });
            Thread.Sleep(1500);

            for (int paso = 1; paso <= t.MaxPasos; paso++)
            {
                var ciclo = Stopwatch.StartNew();
                var (donde, msDonde) = Mcp("map_where_am_i", new());
                var (veo, msVeo) = Mcp("map_what_i_see", new());
                string aqui = Regex.Match(veo, "en «([^»]*)»").Groups[1].Value;
                var puertas = Regex.Matches(veo, @"^\s+«(.*)» \(([^)]*)\)\s*$", RegexOptions.Multiline)
                    .Select(m => (Etiqueta: m.Groups[1].Value, Tipo: m.Groups[2].Value)).ToList();
                var ids = puertas.Select((p, i) => $"{i + 1}) {p.Etiqueta} ({p.Tipo})").ToList();

                var relojJev = Stopwatch.StartNew();
                DecisionDeUnPaso d;
                try { d = ElDecisor.Elegir("jev", aqui, t.Objetivo, ids, 0.70, jev.Pregunta); }
                catch (Exception e) { d = null!; Console.WriteLine($"  ✘ el decisor lanzó {e.GetType().Name}: {e.Message}"); }
                relojJev.Stop();

                string etiqueta = "";
                long msPulsar = 0; string pulsado = "";
                if (d != null && d.Actuar)
                {
                    int n = ids.IndexOf(d.Puerta);
                    etiqueta = n >= 0 ? puertas[n].Etiqueta : d.Puerta;
                    (pulsado, msPulsar) = Mcp("map_take", new() { ["exit"] = etiqueta });
                }
                ciclo.Stop();

                Console.WriteLine($"  paso {paso}: dónde {msDonde} · veo {msVeo} ({puertas.Count}) · jev {relojJev.ElapsedMilliseconds} · pulsar {msPulsar} · TOTAL {ciclo.ElapsedMilliseconds} ms"
                    + $"\n     → {(d?.Actuar == true ? "«" + etiqueta + "» conf " + d.Confianza.ToString("0.00") : "no acciona: " + d?.Porque)}"
                    + (pulsado.Length > 0 ? "\n     ← " + Corta(pulsado) : ""));
                Anota(new
                {
                    tarea = t.Nombre, paso, aqui, puertas = puertas.Count,
                    ms_donde = msDonde, ms_veo = msVeo, ms_jev = relojJev.ElapsedMilliseconds, ms_pulsar = msPulsar,
                    ms_total = ciclo.ElapsedMilliseconds,
                    actua = d?.Actuar ?? false, elegida = etiqueta, conf = d?.Confianza ?? 0, porque = d?.Porque ?? "",
                    resultado = Corta(pulsado, 400),
                });
                if (d == null || !d.Actuar) break;
            }
        }
        Console.WriteLine($"\nlisto: {salida}");
        return 0;
    }

    // ── Las primitivas en crudo ─────────────────────────────────────────────────────────────────

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder s, int max);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern uint SendInput(uint n, INPUT[] i, int size);
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public MOUSEINPUT mi; }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra; public uint pad1, pad2; }

    private static void Primitivas()
    {
        Console.WriteLine("=== primitivas en crudo (20 repeticiones)");
        var fg = new List<double>(); var uia = new List<double>(); var mouse = new List<double>(); int nUia = 0;
        var lector = new U.WindowsClient.Uia.UiaReader();
        for (int i = 0; i < 20; i++)
        {
            var r = Stopwatch.StartNew();
            var h = GetForegroundWindow(); GetWindowThreadProcessId(h, out uint pid);
            var sb = new StringBuilder(256); GetWindowText(h, sb, 256);
            string proc = ""; try { proc = Process.GetProcessById((int)pid).ProcessName; } catch { }
            fg.Add(r.Elapsed.TotalMilliseconds);

            r.Restart(); lector.Read(h); nUia = lector.Elements.Count; uia.Add(r.Elapsed.TotalMilliseconds);

            // Mover el ratón real UN píxel y volver: el coste de la mano física, sin pulsar nada.
            GetCursorPos(out var p);
            r.Restart();
            SetCursorPos(p.X + 1, p.Y);
            var mv = new INPUT[] { new() { type = 0, mi = new MOUSEINPUT { dx = 0, dy = 0, dwFlags = 0x0001 } } };
            SendInput(1, mv, Marshal.SizeOf<INPUT>());
            SetCursorPos(p.X, p.Y);
            mouse.Add(r.Elapsed.TotalMilliseconds);
        }
        Console.WriteLine($"  foreground+pid+título+proceso: med {Med(fg):0.00} ms");
        Console.WriteLine($"  UiaReader.Read en proceso    : med {Med(uia):0.0} ms ({nUia} elementos)");
        Console.WriteLine($"  SetCursorPos+SendInput       : med {Med(mouse):0.000} ms");
        Anota(new { fase = "primitivas", ms_foreground = Med(fg), ms_uia_en_proceso = Med(uia), elementos = nUia, ms_raton = Med(mouse) });
    }

    private static double Med(List<double> v) { var s = v.OrderBy(x => x).ToList(); return s[s.Count / 2]; }

    // ── MCP ──────────────────────────────────────────────────────────────────────────────────────

    private static (string Texto, long Ms) Mcp(string herramienta, Dictionary<string, string> args)
    {
        string cuerpo = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0", id = ++_id, method = "tools/call",
            @params = new { name = herramienta, arguments = args },
        });
        var reloj = Stopwatch.StartNew();
        using var res = Http.PostAsync("http://127.0.0.1:8790/mcp/", new StringContent(cuerpo, Encoding.UTF8, "application/json")).Result;
        string json = res.Content.ReadAsStringAsync().Result;
        reloj.Stop();
        using var doc = JsonDocument.Parse(json);
        string texto = doc.RootElement.TryGetProperty("result", out var r) && r.TryGetProperty("content", out var c) && c.GetArrayLength() > 0
            ? c[0].GetProperty("text").GetString() ?? ""
            : json;
        return (texto, reloj.ElapsedMilliseconds);
    }

    private static void Anota(object o) => _out.WriteLine(JsonSerializer.Serialize(o));
    private static string Corta(string s, int n = 160) { s = s.Replace("\n", " ⏎ "); return s.Length > n ? s[..n] + "…" : s; }
}
