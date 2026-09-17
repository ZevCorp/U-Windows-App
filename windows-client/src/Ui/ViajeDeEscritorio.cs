using System.Diagnostics;
using System.Runtime.InteropServices;
using U.Graph.Surfaces;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Ui;

/// <summary>
/// EL VIAJE DEL CENTRO DE OPERACIONES A OTRO ESCRITORIO, paso a paso. Promesa 274 (spec 031).
/// </summary>
/// <remarks>
/// EJECUTA EL PLAN QUE DECIDE <see cref="ReglaDelViaje"/>, y nada que el plan no diga. Cada paso deja
/// una línea en el log con su tiempo, porque el viaje se juzga a sí mismo: mover devuelve S_OK en 5 ms
/// y el escritorio sigue siendo el mismo, así que «llevé la carita» solo es verdad cuando el registro
/// dice que el actual ES el destino (<see cref="ReglaDelViaje.Llegue"/>).
///
/// CAMBIAR DE ESCRITORIO ES EL ATAJO DEL SISTEMA (Win+Ctrl+←/→, Win+Ctrl+D para uno nuevo), como ya
/// hace <c>Gestures</c> con Win+D: no hay API pública, y la interna cambia con cada compilación de
/// Windows. Fijar la ventana durante el viaje sí usa la interna, con respaldo (<see cref="FijacionDeVentana"/>).
///
/// SI NO SE LLEGA EN EL PLAZO, SE DESHACE: las ventanas vuelven al origen y se vuelve a él. Nada queda
/// a medias, y el resultado lo dice.
/// </remarks>
public static class ViajeDeEscritorio
{
    public sealed record Resultado(bool Llego, Guid Destino, long Ms, bool Fijada, string Camino);

    [DllImport("user32.dll")] private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    private const uint KEYUP = 2, EXTENDED = 1;
    private const byte VK_LWIN = 0x5B, VK_CONTROL = 0x11, VK_LEFT = 0x25, VK_RIGHT = 0x27, VK_D = 0x44;

    /// <summary>Cuánto se espera a que el sistema diga que llegó, por cada cambio de escritorio.</summary>
    public static readonly TimeSpan PlazoPorPaso = TimeSpan.FromMilliseconds(1500);

    /// <param name="origen">Donde está el centro de operaciones.</param>
    /// <param name="destino">A dónde va; se ignora si <paramref name="nuevo"/>.</param>
    /// <param name="nuevo">Ir a un escritorio que todavía no existe.</param>
    /// <param name="ventanas">Las ventanas propias que viajan (todas las del centro de operaciones).</param>
    /// <param name="mover">Cómo se mueven: inyectado para que el hilo de la interfaz lo haga cuando toque.</param>
    public static async Task<Resultado> IrAsync(Guid origen, Guid destino, bool nuevo, IntPtr[] ventanas,
        Func<IntPtr, Guid, int> mover)
    {
        var orden = EscritorioVirtual.Orden();
        var plan = ReglaDelViaje.Plan(origen, destino, orden, nuevo);
        var reloj = Stopwatch.StartNew();
        var camino = new List<string>();
        bool fijada = false;
        LogBus.Log("viaje", $"salgo de «{EscritorioVirtual.Nombre(origen, orden)}» hacia {(nuevo ? "uno nuevo" : $"«{EscritorioVirtual.Nombre(destino, orden)}»")}: {string.Join(" → ", plan)}");

        foreach (var paso in plan)
        {
            long t0 = reloj.ElapsedMilliseconds;
            switch (paso)
            {
                case ReglaDelViaje.Fijar:
                    fijada = ventanas.Length > 0 && ventanas.All(FijacionDeVentana.Fijar);
                    camino.Add(fijada ? "fijada" : "sin fijar");
                    LogBus.Log("viaje", fijada
                        ? $"fijada en todos los escritorios ({FijacionDeVentana.UltimaRespuesta}) · {reloj.ElapsedMilliseconds - t0} ms"
                        : $"no se pudo fijar: el viaje sigue sin la ventana delante un instante — {FijacionDeVentana.UltimaRespuesta}");
                    break;

                case ReglaDelViaje.Crear:
                    Combo(VK_D, extendida: false);
                    camino.Add("creado");
                    break;

                case ReglaDelViaje.LeerNuevo:
                {
                    var antes = orden;
                    Guid leido = Guid.Empty;
                    var espera = Stopwatch.StartNew();
                    while (espera.Elapsed < PlazoPorPaso && leido == Guid.Empty)
                    {
                        await Task.Delay(60);
                        var ahora = EscritorioVirtual.Orden();
                        var nuevos = ahora.Except(antes).ToArray();
                        if (nuevos.Length > 0) { leido = nuevos[^1]; orden = ahora; }
                    }
                    if (leido == Guid.Empty)
                    {
                        LogBus.Log("viaje", $"el sistema no creó ningún escritorio en {PlazoPorPaso.TotalMilliseconds:0} ms: no se viaja");
                        Soltar(ventanas, fijada);
                        return new(false, Guid.Empty, reloj.ElapsedMilliseconds, fijada, string.Join(" · ", camino));
                    }
                    destino = leido;
                    camino.Add($"nuevo={EscritorioVirtual.Nombre(destino, orden)}");
                    LogBus.Log("viaje", $"el escritorio nuevo es «{EscritorioVirtual.Nombre(destino, orden)}» · {reloj.ElapsedMilliseconds - t0} ms");
                    break;
                }

                case ReglaDelViaje.Mover:
                    MoverTodas(ventanas, destino, mover);
                    camino.Add("movidas");
                    LogBus.Log("viaje", $"{ventanas.Length} ventana(s) movidas · {reloj.ElapsedMilliseconds - t0} ms");
                    break;

                case ReglaDelViaje.Esperar:
                {
                    bool llego = await EsperarAsync(destino, PlazoPorPaso + TimeSpan.FromMilliseconds(300 * Math.Max(1, Math.Abs(DistanciaDe(plan)))));
                    if (!llego)
                    {
                        LogBus.Log("viaje", $"no llegué a «{EscritorioVirtual.Nombre(destino, orden)}» en el plazo: el registro dice «{EscritorioVirtual.Nombre(EscritorioVirtual.Actual(), orden)}». Deshago.");
                        await DeshacerAsync(origen, destino, orden, ventanas, mover);
                        Soltar(ventanas, fijada);
                        return new(false, destino, reloj.ElapsedMilliseconds, fijada, string.Join(" · ", camino) + " · deshecho");
                    }
                    camino.Add("llegué");
                    LogBus.Log("viaje", $"llegué a «{EscritorioVirtual.Nombre(destino, orden)}» en {reloj.ElapsedMilliseconds} ms (lo dice el registro)");
                    break;
                }

                case ReglaDelViaje.SoltarFijacion:
                    Soltar(ventanas, fijada);
                    camino.Add(fijada ? "suelta" : "");
                    break;

                default:
                    int d = ReglaDelViaje.Distancia(paso);
                    if (d != 0)
                    {
                        for (int i = 0; i < Math.Abs(d); i++)
                        {
                            Combo(d > 0 ? VK_RIGHT : VK_LEFT, extendida: true);
                            await Task.Delay(140);   // el deslizamiento tiene su ritmo: dos atajos pegados se comen uno
                        }
                        camino.Add(paso);
                        LogBus.Log("viaje", $"{paso}: {Math.Abs(d)} atajo(s) · {reloj.ElapsedMilliseconds - t0} ms");
                    }
                    break;
            }
        }
        return new(true, destino, reloj.ElapsedMilliseconds, fijada, string.Join(" · ", camino));
    }

    private static int DistanciaDe(string[] plan) => plan.Select(ReglaDelViaje.Distancia).FirstOrDefault(d => d != 0);

    private static void MoverTodas(IntPtr[] ventanas, Guid destino, Func<IntPtr, Guid, int> mover)
    {
        foreach (var h in ventanas)
        {
            int hr = mover(h, destino);
            if (hr != 0) LogBus.Log("viaje", $"ventana {h} no se movió: 0x{hr:X8}");
        }
    }

    private static async Task<bool> EsperarAsync(Guid destino, TimeSpan plazo)
    {
        var espera = Stopwatch.StartNew();
        while (espera.Elapsed < plazo)
        {
            if (ReglaDelViaje.Llegue(EscritorioVirtual.Actual(), destino)) return true;
            await Task.Delay(50);
        }
        return ReglaDelViaje.Llegue(EscritorioVirtual.Actual(), destino);
    }

    private static async Task DeshacerAsync(Guid origen, Guid destino, Guid[] orden, IntPtr[] ventanas, Func<IntPtr, Guid, int> mover)
    {
        foreach (var paso in ReglaDelViaje.Deshacer(origen, destino, orden))
        {
            switch (paso)
            {
                case ReglaDelViaje.Mover: MoverTodas(ventanas, origen, mover); break;
                case ReglaDelViaje.Esperar:
                    bool volvi = await EsperarAsync(origen, PlazoPorPaso);
                    LogBus.Log("viaje", volvi ? "deshecho: de vuelta en el origen" : "deshecho a medias: el registro no dice el origen; las ventanas sí están allí");
                    break;
                default:
                    int d = ReglaDelViaje.Distancia(paso);
                    for (int i = 0; i < Math.Abs(d); i++) { Combo(d > 0 ? VK_RIGHT : VK_LEFT, extendida: true); await Task.Delay(140); }
                    break;
            }
        }
    }

    private static void Soltar(IntPtr[] ventanas, bool fijada)
    {
        if (!fijada) return;
        foreach (var h in ventanas) FijacionDeVentana.Soltar(h);
        LogBus.Log("viaje", $"fijación suelta ({FijacionDeVentana.UltimaRespuesta})");
    }

    /// <summary>Win+Ctrl+tecla, como Gestures hace Win+D: puro teclado del sistema.</summary>
    private static void Combo(byte tecla, bool extendida)
    {
        uint ext = extendida ? EXTENDED : 0;
        keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
        keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
        keybd_event(tecla, 0, ext, UIntPtr.Zero);
        keybd_event(tecla, 0, ext | KEYUP, UIntPtr.Zero);
        keybd_event(VK_CONTROL, 0, KEYUP, UIntPtr.Zero);
        keybd_event(VK_LWIN, 0, KEYUP, UIntPtr.Zero);
    }
}
