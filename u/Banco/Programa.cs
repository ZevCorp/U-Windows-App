using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using U.Ciclo;

// EL BANCO: el ciclo nuevo sobre el escritorio real. Las mismas cinco tareas con las que se midió main
// (sondas/DelCiclo), para que la comparación sea de igual a igual.
//
//   banco-u [salida.jsonl] [--solo nombre]
//
// Escape detiene todo. Mueve el ratón DE VERDAD: no tocar el equipo mientras corre.

Raton.AsegurarDpi();
string salida = args.FirstOrDefault(a => !a.StartsWith("--")) ?? Path.Combine(Path.GetTempPath(), "u-medicion", "banco-u.jsonl");
string? solo = args.SkipWhile(a => a != "--solo").Skip(1).FirstOrDefault();
Directory.CreateDirectory(Path.GetDirectoryName(salida)!);
using var log = new StreamWriter(salida, append: true) { AutoFlush = true };
void Anota(object o) => log.WriteLine(JsonSerializer.Serialize(o));

var (_, typesafe, estado) = Claves.Traer();
Console.WriteLine(estado);
if (typesafe == null) { Console.WriteLine("✘ sin clave de TypeSafe no hay Jev: no mido un ciclo sin decisor."); return 2; }

using var jev = new ClienteJev(typesafe) { Umbral = Jev.UmbralPorDefecto };
using var lector = new LectorUia();
Console.WriteLine($"Jev calentado en {jev.Calentar()} ms · segunda {jev.Calentar()} ms");

// Las primitivas, sobre lo que haya delante.
{
    var d = new List<double>(); var v = new List<double>(); int n = 0;
    for (int i = 0; i < 20; i++)
    {
        var r = Stopwatch.StartNew(); var u = Donde.Leer(); d.Add(r.Elapsed.TotalMilliseconds);
        r.Restart(); n = lector.Leer(u.Ventana).Accionables.Count; v.Add(r.Elapsed.TotalMilliseconds);
    }
    Console.WriteLine($"primitivas: dónde {Med(d):0.00} ms · ver {Med(v):0.0} ms ({n} accionables)");
    Anota(new { fase = "primitivas", ms_donde = Med(d), ms_ver = Med(v), accionables = n });
}

var tareas = new (string Nombre, string App, string Objetivo, int Max)[]
{
    ("bloc-menu-archivo", "notepad", "abrir el menú Archivo del Bloc de notas", 3),
    ("config-bluetooth", "configuracion", "ir a la sección Bluetooth y dispositivos de Configuración", 4),
    ("explorador-documentos", "explorador", "abrir la carpeta Documentos en el Explorador de archivos", 4),
    ("calc-7x8", "calculadora", "calcular 7 por 8 pulsando los botones de la calculadora: siete, multiplicar, ocho, igual", 6),
    ("config-sistema-pantalla", "configuracion", "ir a Sistema y después a Pantalla en Configuración", 5),
};

var todas = new List<Vuelta>();
foreach (var t in tareas)
{
    if (solo != null && t.Nombre != solo) continue;
    if (Raton.EscapePulsado()) { Console.WriteLine("Escape: paro el banco."); break; }
    Console.WriteLine($"\n=== {t.Nombre}: {t.Objetivo}");
    var (llego, msAbrir) = Apps.Abrir(t.App);
    Console.WriteLine($"  abrir {msAbrir} ms · {(llego ? "delante" : "NO llegó delante")} · {Donde.Leer().Pantalla}");
    Anota(new { tarea = t.Nombre, fase = "abrir_app", ms = msAbrir, llego });
    Thread.Sleep(700);   // que la app termine de pintarse: abrir no es parte del ciclo

    var motor = new Motor(
        Donde.Ahora,
        () => lector.Leer(Donde.Ahora()?.Ventana ?? IntPtr.Zero),
        (Contexto c) => jev.Decidir(c),
        Raton.Clic,
        Raton.EscapePulsado)
    {
        AlTerminarVuelta = v =>
        {
            Console.WriteLine($"  paso {v.Paso}: {v.Tiempos.Linea()}\n     {v.Accionables} accionables · {(v.Elegida.Length > 0 ? "pulsé " + v.Elegida : "")} → {v.Resultado}");
            Anota(new
            {
                tarea = t.Nombre, paso = v.Paso, pantalla = v.Pantalla, accionables = v.Accionables, elegida = v.Elegida, resultado = v.Resultado,
                ms_donde = v.Tiempos.Donde, ms_ver = v.Tiempos.Ver, ms_decidir = v.Tiempos.Decidir, ms_pulsar = v.Tiempos.Pulsar,
                ms_asentar = v.Tiempos.Asentar, ms_total = v.Tiempos.Total, fuera = v.Tiempos.FueraDePresupuesto,
            });
        },
    };
    var rec = motor.Objetivo(t.Objetivo, t.Max);
    todas.AddRange(rec.Vueltas);
    Console.WriteLine($"  ⇒ {(rec.Cumplido ? "CUMPLIDO" : "sin cumplir")}: {rec.PorQueParo}");
    Anota(new { tarea = t.Nombre, fase = "fin", cumplido = rec.Cumplido, porque = rec.PorQueParo });
}

var conPulso = todas.Where(v => v.Tiempos.Pulsar > 0).ToList();
if (todas.Count > 0)
{
    string F(double x) => x.ToString("0", CultureInfo.InvariantCulture);
    Console.WriteLine($"\nRESUMEN ({todas.Count} vueltas, {conPulso.Count} con clic) — medianas:");
    Console.WriteLine($"  dónde {Med(todas.Select(v => v.Tiempos.Donde)):0.00} · ver {F(Med(todas.Select(v => v.Tiempos.Ver)))} · decidir {F(Med(todas.Select(v => v.Tiempos.Decidir)))}"
        + (conPulso.Count > 0 ? $" · pulsar {Med(conPulso.Select(v => v.Tiempos.Pulsar)):0.00} · asentar {F(Med(conPulso.Select(v => v.Tiempos.Asentar)))} · total con clic {F(Med(conPulso.Select(v => v.Tiempos.Total)))} (máx {F(conPulso.Max(v => v.Tiempos.Total))})" : ""));
    Console.WriteLine($"  fuera de presupuesto: {todas.Count(v => v.Tiempos.FueraDePresupuesto)} de {todas.Count}");
}
return 0;

static double Med(IEnumerable<double> xs) { var s = xs.OrderBy(x => x).ToList(); return s.Count == 0 ? 0 : s[s.Count / 2]; }
