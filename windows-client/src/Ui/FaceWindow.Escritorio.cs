using System.Windows;
using System.Windows.Interop;
using U.Graph.Surfaces;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Ui;

/// <summary>
/// EL ESCRITORIO VIRTUAL EN EL QUE VIVE ESTE ASISTENTE. Promesa 270 (spec 031).
/// </summary>
/// <remarks>
/// Archivo parcial aparte a propósito: <c>FaceWindow.xaml.cs</c> es la zona de choque alta del repo
/// (lo tocan los tres), y todo lo del escritorio cabe aquí sin rozar el resto.
///
/// SU ESCRITORIO ES EL DE SU CARITA, leído del sistema —no recordado— al arrancar y al terminar cada
/// viaje. Y las ventanas del centro de operaciones —la carita, el muelle, el notch, la consulta, la
/// consola— viven en ese mismo escritorio: si alguna se quedó en otro (un LogWindow abierto antes
/// del viaje), se trae. Solo las propias: la API pública no mueve ventanas ajenas (medido el
/// 2026-09-17: E_ACCESSDENIED), y las que el sistema no ubica no se tocan.
/// </remarks>
public partial class FaceWindow
{
    private Guid _suEscritorio;

    /// <summary>El escritorio virtual de este asistente; vacío hasta que se lee por primera vez.</summary>
    public Guid SuEscritorio => _suEscritorio;

    /// <summary>
    /// Lee del sistema en qué escritorio está la carita y lo hace suyo. Se llama al arrancar, con la
    /// ventana ya mostrada, y al terminar cada viaje.
    /// </summary>
    private void LeerSuEscritorio(string porque)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var leido = EscritorioVirtual.EscritorioDe(hwnd);
        if (leido == Guid.Empty)
        {
            // La carita escondida (guardada en el muelle) no tiene escritorio para el sistema: se pregunta
            // por el muelle, que sí está a la vista y vive donde ella.
            if (_muelle != null) leido = EscritorioVirtual.EscritorioDe(new WindowInteropHelper(_muelle).Handle);
        }
        if (leido == Guid.Empty)
        {
            LogBus.Log("escritorio", $"no sé en qué escritorio vivo ({porque}): el sistema no ubica ni la carita ni el muelle");
            return;
        }
        _suEscritorio = leido;
        LogBus.Log("escritorio", $"vivo en «{EscritorioVirtual.Nombre(leido)}» ({porque})");
        TraerLasVentanasDelCentro();
    }

    // ── El viaje (promesa 274) ───────────────────────────────────────────────

    private bool _viajando;

    /// <summary>
    /// Lleva el centro de operaciones —y con él a la carita sentada en la consulta— a otro escritorio.
    /// Al llegar, su escritorio pasa a ser ese y la carita se suelta de la silla y se posa con su
    /// animación. Si no se llega, se deshace y la carita sigue sentada donde estaba.
    /// </summary>
    private async Task ViajarAsync(Guid destino, bool nuevo)
    {
        if (_viajando) { LogBus.Log("viaje", "ya hay un viaje en marcha: este se ignora"); return; }
        if (_suEscritorio == Guid.Empty) LeerSuEscritorio("antes de viajar");
        var origen = _suEscritorio;
        if (origen == Guid.Empty) { LogBus.Log("viaje", "no sé dónde estoy: no viajo a ciegas"); return; }

        _viajando = true;
        try
        {
            var ventanas = VentanasDelCentro();
            var r = await ViajeDeEscritorio.IrAsync(origen, destino, nuevo, ventanas,
                (h, d) => Dispatcher.Invoke(() => EscritorioVirtual.Mover(h, d)));
            if (!r.Llego)
            {
                SetStatus("no llegué al otro escritorio; me quedo aquí");
                LogBus.Log("viaje", $"viaje fallido tras {r.Ms} ms: {r.Camino}");
                return;
            }
            LeerSuEscritorio($"al llegar, en {r.Ms} ms, {(r.Fijada ? "con la ventana delante todo el viaje" : "sin fijar: la ventana faltó un instante")}");
            SoltarseAlLlegar();
        }
        finally { _viajando = false; }
    }

    /// <summary>Las ventanas propias que viajan: todas las que tienen handle, esté la carita a la vista o sentada.</summary>
    private IntPtr[] VentanasDelCentro()
    {
        var lista = new List<IntPtr>();
        foreach (Window w in Application.Current.Windows)
        {
            var h = new WindowInteropHelper(w).Handle;
            if (h != IntPtr.Zero) lista.Add(h);
        }
        return lista.ToArray();
    }

    /// <summary>
    /// LA ANIMACIÓN DE SOLTARSE: la carita aparece donde estaba sentada y se va a su borde con el
    /// mismo muelle con el que se lanza. Desde ese instante trabaja en este escritorio.
    /// </summary>
    private void SoltarseAlLlegar()
    {
        if (!_silla.Ocupada) return;
        // Dónde está la silla ahora, en puntos: ahí aparece, para que se vea salir de la consulta.
        Point asiento;
        try
        {
            var px = Face.PointToScreen(new Point(0, 0));
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(Face);
            asiento = new Point(px.X / dpi.DpiScaleX, px.Y / dpi.DpiScaleY);
        }
        catch (InvalidOperationException) { asiento = new Point(Left, Top); }   // la silla no está en pantalla

        LevantarLaCarita();
        MoveTo(asiento.X, asiento.Y);
        ShowActivated = false;
        Show();
        // ESCONDIDA NO VIAJA (medido el 2026-09-17, primera corrida a mano): la carita iba oculta durante
        // el viaje, «mover» contestó S_OK para las tres ventanas, y al enseñarla apareció en OTRO
        // escritorio —el sistema no asigna escritorio a una ventana que no se ve—. Ya a la vista, se
        // mueve otra vez y se comprueba dónde quedó, en vez de fiarse del S_OK de antes.
        var hwnd = new WindowInteropHelper(this).Handle;
        int hr = EscritorioVirtual.Mover(hwnd, _suEscritorio);
        var donde = EscritorioVirtual.EscritorioDe(hwnd);
        LogBus.Log("viaje", $"la carita se suelta en «{EscritorioVirtual.Nombre(_suEscritorio)}» desde ({asiento.X:0},{asiento.Y:0}) y se posa en su borde "
                          + $"(ya a la vista: mover → 0x{hr:X8}, está en «{EscritorioVirtual.Nombre(donde)}»)");
        if (donde != _suEscritorio) LogBus.Log("viaje", "✋ la carita no quedó en su escritorio: el sistema la puso en otro");
        EdgeSnap.Aplicar(this, 0, 0, OnWindowMoved);
    }

    /// <summary>
    /// Todas las ventanas propias al escritorio del asistente. Devuelve cuántas hubo que traer.
    /// </summary>
    private int TraerLasVentanasDelCentro()
    {
        if (_suEscritorio == Guid.Empty) return 0;
        var ventanas = new List<IntPtr>();
        var donde = new List<Guid>();
        foreach (Window w in Application.Current.Windows)
        {
            var h = new WindowInteropHelper(w).Handle;
            if (h == IntPtr.Zero) continue;
            ventanas.Add(h);
            donde.Add(EscritorioVirtual.EscritorioDe(h));
        }
        var traer = ReglaDelEscritorio.Descolocadas(ventanas.ToArray(), donde.ToArray(), _suEscritorio);
        foreach (var h in traer)
        {
            int hr = EscritorioVirtual.Mover(h, _suEscritorio);
            LogBus.Log("escritorio", hr == 0
                ? $"ventana {h} traída a «{EscritorioVirtual.Nombre(_suEscritorio)}»"
                : $"ventana {h} no se pudo traer: 0x{hr:X8}");
        }
        return traer.Length;
    }
}
