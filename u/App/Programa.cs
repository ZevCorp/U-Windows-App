using System.Windows;
using U.Ciclo;

namespace U.Nuevo;

/// <summary>
/// Ü DESDE CERO.
///
///   U-nuevo.exe                     la burbuja: clic o Ctrl+Alt+Espacio para hablarle
///   U-nuevo.exe --hacer "pedido"    sin voz ni burbuja: Luna planea, Jev ejecuta, y sale (para probar)
///   U-nuevo.exe --plan "p1" "p2"…   sin Luna: el plan tal cual al ejecutor (para medir el ciclo)
/// </summary>
public static class Programa
{
    [STAThread]
    public static int Main(string[] args)
    {
        var (openai, typesafe, estado) = Claves.Traer();
        Registro.Log("Ü desde cero arranca · " + estado);
        if (typesafe == null) { Registro.Log("✘ sin clave de TypeSafe no hay Jev: no arranco."); return 2; }

        using var ü = new Asistente(typesafe) { Log = Registro.Log };
        _ = Task.Run(() => Registro.Log($"Jev caliente en {ü.Calentar()} ms"));

        int i = Array.IndexOf(args, "--hacer");
        if (i >= 0 && i + 1 < args.Length)
        {
            if (openai == null) { Registro.Log("✘ sin clave de OpenAI no hay Luna"); return 2; }
            Registro.Linea += Console.WriteLine;
            using var luna = new LunaPorTexto(openai) { Log = Registro.Log };
            var r = System.Diagnostics.Stopwatch.StartNew();
            string dicho = luna.Pedir(args[i + 1], ü);
            Registro.Log($"Ü: {dicho}  ({r.ElapsedMilliseconds} ms de principio a fin)");
            return 0;
        }
        i = Array.IndexOf(args, "--plan");
        if (i >= 0)
        {
            Registro.Linea += Console.WriteLine;
            Console.WriteLine(ü.Hacer(args.Skip(i + 1).ToList()));
            return 0;
        }

        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        var burbuja = new Burbuja(ü, openai);
        app.MainWindow = burbuja;
        burbuja.Show();
        return app.Run();
    }
}
