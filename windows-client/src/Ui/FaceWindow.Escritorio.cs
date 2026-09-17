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
