using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using U.Graph.Surfaces;
using U.WindowsClient.Diagnostics;
using U.WindowsClient.Mcp;
using U.WindowsClient.Ui.Jev;

namespace U.WindowsClient.Ui;

/// <summary>
/// LO QUE SE VE DE JEV, ENCHUFADO A LA CARITA. Spec 049, fase 9: el cableado de las promesas 381 a 384 (sus partes
/// (b)); lo que decide qué se ve es <see cref="MaquinaDeLaVista"/>, que se juzga sin pantalla.
/// </summary>
/// <remarks>
/// Archivo parcial aparte a propósito, como <c>FaceWindow.Escritorio.cs</c>: <c>FaceWindow.xaml.cs</c> es la zona de
/// choque alta del repo (lo tocan los tres). Allí quedan CUATRO líneas de Jev, contadas en el commit: nacer
/// (<see cref="NacerLaVistaDeJev"/>, en el arranque), sincronizar con el botón (<see cref="SincronizarLaVistaDeJev"/>,
/// en <c>PintarBotonJev</c>) y las dos de la regla de quién vuela (384). Todo lo demás vive aquí.
///
/// LA VISTA OYE EL EVENTO DE C (spec 048, 368) desde el 2026-09-24, al juntar A, B, C y D: <see cref="NacerLaVistaDeJev"/>
/// se suscribe una vez a <c>SurfaceMapTools.AlDecidir</c> con <see cref="ObservadorDelDecisor.Oir"/>, y el <c>Decisor</c>
/// ya no se envuelve —el puente provisional de la 049 se fue: veía la decisión antes del veto de la 390, y con los dos a la
/// vez cada paso se habría pintado dos veces—. El interruptor sigue siendo el único que escribe el <c>Decisor</c>. Lo
/// pulsado sigue llegando además por <c>UiaSurface.Pulso</c>, que no es del puente: es el único aviso que llega AL
/// pulsar, y la flecha vuela con él (381); el evento sale cuando el paso terminó, espera incluida.
/// </remarks>
public partial class FaceWindow
{
    [StructLayout(LayoutKind.Sequential)]
    private struct RectDeVentana { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr h, out RectDeVentana rect);

    /// <summary>La vista de Jev; <c>null</c> hasta <see cref="NacerLaVistaDeJev"/>. La lee la regla de quién vuela (384).</summary>
    private VistaDeJev? _vistaDeJev;

    /// <summary>El mapa que la vista oye; al encender Jev se mira si su <c>Decisor</c> está puesto, para decirlo si no.</summary>
    private SurfaceMapTools? _mapaDeJev;

    /// <summary>
    /// Nace la vista de Jev y se engancha a lo que la alimenta. Se llama UNA vez, en el arranque, con los avisos del
    /// tramo del mapa YA puestos, y antes del primer <c>PintarBotonJev</c>. No crea ninguna ventana: eso lo hace
    /// encender Jev (<see cref="SincronizarLaVistaDeJev"/>).
    /// </summary>
    private void NacerLaVistaDeJev(SurfaceMapTools mapa)
    {
        if (_vistaDeJev != null)
        {
            LogBus.Log("jev-vista", "NacerLaVistaDeJev se llamó otra vez: sigue la vista que ya había, sin engancharla dos veces");
            return;
        }
        var vista = new VistaDeJev(Dispatcher, DondeEstaLaCarita);
        _vistaDeJev = vista;
        _mapaDeJev = mapa;

        // LOS AVISOS DEL TRAMO SE ENCADENAN, no se sustituyen: primero lo que ya había (el freno, el notch) y después
        // la vista, que solo encola y vuelve (382). ElTramo lee estas propiedades en cada aviso (SurfaceMapTools:
        // `AlEmpezarTramo?.Invoke(t)`), así que encadenar aquí es lo mismo que haberlo escrito en su línea — y deja
        // tres líneas menos en el archivo grande. Lo que se pague: quien reasigne uno de los tres DESPUÉS de esta
        // llamada deja a la vista sin él, y EnTramo se quedaría en falso (la carita viajaría: 384).
        var empezar = mapa.AlEmpezarTramo;
        var terminar = mapa.AlTerminarTramo;
        var progreso = mapa.Progreso;
        var sinPoner = new List<string>();
        if (empezar == null) sinPoner.Add(nameof(mapa.AlEmpezarTramo));
        if (terminar == null) sinPoner.Add(nameof(mapa.AlTerminarTramo));
        if (progreso == null) sinPoner.Add(nameof(mapa.Progreso));
        if (sinPoner.Count > 0)
            LogBus.Log("jev-vista", $"al nacer la vista, el mapa no tenía puesto {string.Join(", ", sinPoner)}: la vista los recibe igual, sin nada delante");
        mapa.AlEmpezarTramo = tarea => { empezar?.Invoke(tarea); vista.AlEmpezarTramo(tarea); };
        mapa.AlTerminarTramo = () => { terminar?.Invoke(); vista.AlTerminarTramo(); };
        mapa.Progreso = linea => { progreso?.Invoke(linea); vista.Progreso(linea); };

        // CADA PASO DECIDIDO, POR EL EVENTO DE C (368), ya con el veto (390): la decisión, lo ofrecido con su caja y lo que
        // hizo la mano. Una suscripción por mapa y para toda la vida de Ü: con Jev apagado la máquina no pinta nada (383),
        // así que no hace falta quitarla al apagar.
        if (!ObservadorDelDecisor.Oir(mapa, vista.Publicar))
            LogBus.Log("jev-vista", "el mapa ya tenía a la vista oyendo AlDecidir: no se suscribe otra vez (cada paso se pintaría dos veces)");

        // LA MANO PULSÓ, en físicos y desde la tarea del tramo: la rosa, el panel que se aparta y la flecha (381). La
        // vista mira si Jev está encendido: la mano también pulsa cuando decide Luna.
        Action<double, double, double, double> alPulsar;
        UiaSurface.Pulso += alPulsar = (x, y, ancho, alto) => vista.AlPulsar(x, y, ancho, alto);

        // ESCAPE Y SOLTAR LO SEÑALADO (383): overlay vacío, flecha escondida y panel sin corrida. Escape siempre, haya
        // tramo o no (Freno.cs:82). Sin -= al cerrar, como las otras suscripciones a estos dos en FaceWindow.xaml.cs.
        Senalador.Suelta += () => vista.Suelta();
        Actions.Freno.SePulso += () => vista.Suelta();

        // CERRAR Ü CIERRA LAS TRES (383). Fuera del arranque la app sale con la última ventana (OnLastWindowClose,
        // App.xaml.cs:111): tres ventanas de Jev abiertas la dejarían viva sin carita.
        Closed += (_, _) =>
        {
            UiaSurface.Pulso -= alPulsar;
            try { vista.Sincronizar(false); }
            catch (Exception e)
            {
                for (var x = e; x != null; x = x.InnerException)
                    LogBus.Log("jev-vista", $"✘ al cerrarse Ü, cerrar las ventanas de Jev lanzó {x.GetType().Name}: {x.Message}");
            }
        };
    }

    /// <summary>
    /// EL BOTÓN DE JEV LLEGA A LA VISTA. Lo llama <c>PintarBotonJev</c>, que corre DESPUÉS del interruptor —en el
    /// arranque y en cada pulsación—. Encendido: se abren las ventanas; apagado: se cierran las tres (383). El
    /// <c>Decisor</c> no se toca: hasta el 2026-09-24 aquí se envolvía y desenvolvía (el puente provisional de la 049), y
    /// desde que la vista oye el evento de C lo escribe solo el interruptor.
    /// </summary>
    private void SincronizarLaVistaDeJev(bool encendido)
    {
        var vista = _vistaDeJev;
        var mapa = _mapaDeJev;
        if (vista == null || mapa == null)
        {
            LogBus.Log("jev-vista", $"el botón de Jev ({(encendido ? "on" : "off")}) llegó antes de que naciera la vista: no hay ventanas que abrir ni cerrar");
            return;
        }
        // SE LEE, NO SE ESCRIBE: con Jev encendido y el Decisor en null no habrá pasos decididos, y el panel se quedaría
        // mudo sin que nada lo dijera.
        if (encendido && mapa.Decisor == null)
            LogBus.Log("jev-vista", "Jev encendido con el Decisor del mapa en null: no habrá pasos decididos, y el panel no recibirá decisiones");

        // ABRIR LAS VENTANAS PUEDE LANZAR (la carita sin sitio, WPF cerrándose), y esto corre en el arranque y en el
        // clic del botón: lo que falle se dice entero y la carita sigue. Si lanza el ancla, la máquina no llega a
        // encenderse —VistaDeJev pone la pantalla antes de Encender—; si lanza una ventana, la máquina puede quedar
        // encendida con ventanas de menos, y la línea de abajo es la que lo dice.
        try { vista.Sincronizar(encendido); }
        catch (Exception e)
        {
            for (var x = e; x != null; x = x.InnerException)
                LogBus.Log("jev-vista", $"✘ {(encendido ? "abrir" : "cerrar")} las ventanas de Jev lanzó {x.GetType().Name}: {x.Message}");
        }
    }

    /// <summary>
    /// Dónde está la carita, en físicos: su ventana ENTERA —la barra y, abierto, el menú—, preguntada a Windows al
    /// encender Jev y antes de cada recolocación. Si la carita está guardada, el muelle, que es donde vive. El panel no
    /// tapa nada de ella (377): hasta el 2026-09-23 esto daba solo el centro, y a 56 DIP de él el panel caía encima de
    /// la barra de 150 y del menú de 292. Si Windows no lo da, lanza con su código: el panel no se pone junto a un sitio
    /// inventado.
    /// </summary>
    private Rect DondeEstaLaCarita()
    {
        Window cual = IsVisible || _muelle == null ? this : _muelle;
        string quien = ReferenceEquals(cual, this) ? "la carita" : "el muelle";
        var h = new WindowInteropHelper(cual).Handle;
        if (h == IntPtr.Zero)
            throw new InvalidOperationException($"{quien} todavía no tiene ventana de Windows (handle 0), así que no hay sitio junto al que poner el panel");
        if (!GetWindowRect(h, out var r))
            throw new InvalidOperationException($"GetWindowRect de {quien} falló (error {Marshal.GetLastWin32Error()}), así que no hay sitio junto al que poner el panel");
        return new Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    }
}
