using System.Windows;

namespace U.WindowsClient.Ui.Jev;

/// <summary>
/// QUÉ RECT SE APLICA AL CAMBIAR EL DPI. Promesa 380 (spec 049), su regla pura; la llamada desde
/// <c>OnDpiChanged</c> la pone el overlay (fase 8) y el contrato la lee en su fuente.
/// </summary>
/// <remarks>
/// EL CALCULADO, NUNCA EL SUGERIDO. Con <c>WM_DPICHANGED</c> Windows propone un rect que conserva el tamaño en
/// DIP de la ventana a la escala nueva: es lo correcto para una ventana que vive en DIP, y lo equivocado para las
/// de Jev, que se ponen en FÍSICOS del escritorio virtual por <c>SetWindowPos</c> (el overlay cubre su
/// <c>rcMonitor</c> exacto; el panel sale de <see cref="DondeVaElPanel"/> contra <c>rcWork</c>). Aceptar el
/// sugerido reescalaría un rect que ya estaba en la unidad de Windows: el overlay dejaría de calzar con su monitor
/// y las cajas se pintarían desplazadas, que es la caja que miente (aprendizaje nº4). El rect sugerido se recibe
/// para que la firma diga que se descarta a sabiendas, no por olvido.
/// </remarks>
public static class ReglaDeDpi
{
    public static Rect RectTrasCambio(Rect calculado, Rect sugerido) => calculado;
}
