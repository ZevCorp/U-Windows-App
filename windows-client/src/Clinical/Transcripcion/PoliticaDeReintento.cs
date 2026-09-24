using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Clinical.Transcripcion;

/// <summary>
/// CUÁNTAS VECES SE VUELVE A INTENTAR ANTES DE RENDIRSE, y cuánto se espera entre una y otra.
/// </summary>
/// <remarks>
/// NACE DE UNA MEDICIÓN, no de una buena práctica (2026-09-01). En la máquina del usuario, en tres
/// segundos seguidos: el DNS de <c>stt-rt.soniox.com</c> contestó «Host desconocido», el TCP a :443
/// no abrió, y el WebSocket al MISMO host abrió a la primera. Ese equipo tiene el DNS fallando de
/// forma intermitente —le había pasado antes con <c>vercel.app</c>— y el dictado, que se rendía al
/// primer fallo, se llevaba la consulta entera por delante.
///
/// LOS NÚMEROS SON LOS DEL PORTAL, no unos inventados: cuatro intentos y esperas de 300, 1000, 3000
/// y 5000 ms (<c>lib/stt/useDictation.ts:40</c>). Se portó el motor de transcripción de la web y se
/// dejó atrás justo esto; traerlo no es añadir una idea, es terminar el port.
///
/// LA ESPERA CRECE A PROPÓSITO. Reintentar de golpe contra un servicio que se está cayendo es
/// martillearlo, y contra un DNS que parpadea es no darle tiempo a volver. La primera espera es
/// corta porque el caso típico —el que se midió— se arregla solo en centésimas.
///
/// SE INYECTA LA ESPERA para poder juzgar esto sin dormir nueve segundos: una prueba lenta se acaba
/// saltando, y un juez que no se corre no juzga nada.
/// </remarks>
public sealed class PoliticaDeReintento
{
    /// <summary>Lo que se espera antes de cada reintento. Su longitud es el presupuesto.</summary>
    public static IReadOnlyList<int> DemorasMs { get; } = new[] { 300, 1000, 3000, 5000 };

    private readonly Func<int, CancellationToken, Task> _esperar;

    public PoliticaDeReintento(Func<int, CancellationToken, Task>? esperar = null)
        => _esperar = esperar ?? ((ms, ct) => Task.Delay(ms, ct));

    /// <summary>Cuántas veces se llegó a intentar. Lo que se dice al rendirse.</summary>
    public int Intentos { get; private set; }

    /// <summary>
    /// Corre el intento hasta que salga o hasta agotar el presupuesto. Devuelve si salió.
    /// </summary>
    public async Task<bool> HastaQueSalgaAsync(Func<CancellationToken, Task<bool>> intento,
        CancellationToken ct = default)
    {
        Intentos = 0;
        // Uno de partida más uno por cada demora: 1 + 4 = 5 oportunidades en total.
        int tope = DemorasMs.Count + 1;

        for (int i = 0; i < tope; i++)
        {
            ct.ThrowIfCancellationRequested();
            Intentos++;

            bool salio;
            try { salio = await intento(ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                // Que el intento reviente cuenta como que no salió: por dentro esto son fallos de
                // red, y una excepción de red no es distinta de un `false` para quien reintenta.
                // Al panel el tipo; el mensaje, en el log local (spec 051, P15).
                LogBus.Publico("reintento", $"intento {Intentos} lanzó {e.GetType().Name}");
                LogBus.Log("reintento", $"el motivo del intento {Intentos}: {e.Message}");
                salio = false;
            }

            if (salio)
            {
                if (Intentos > 1) LogBus.Log("reintento", $"salió al intento {Intentos}");
                return true;
            }

            // No se espera después del último: sería hacer esperar a alguien para nada.
            if (i < DemorasMs.Count) await _esperar(DemorasMs[i], ct);
        }

        LogBus.Publico("reintento", $"se agotaron los {Intentos} intentos");
        return false;
    }
}
