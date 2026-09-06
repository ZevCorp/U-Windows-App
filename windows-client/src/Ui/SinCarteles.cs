using System.Windows;
using System.Windows.Controls;

namespace U.WindowsClient.Ui;

/// <summary>
/// NADA EN Ü ENSEÑA UN CARTEL AL PASAR EL RATÓN. Un solo interruptor, puesto al arrancar, para toda
/// la aplicación (promesa 164, spec 011).
/// </summary>
/// <remarks>
/// El 2026-09-06 había <b>44 carteles declarados en 7 archivos</b>, contados con grep y no de
/// memoria. Borrarlos uno a uno es la mitad del trabajo y la mitad que no dura: el cartel número 45
/// nace la próxima vez que alguien añada un botón, porque poner un <c>ToolTip</c> es lo que WPF
/// invita a hacer. Es el patrón nº5 —arreglar la clase de error, no el caso— aplicado a una clase
/// que tiene 44 miembros hoy y los que hagan falta mañana.
///
/// Se apaga con <c>OverrideMetadata</c> sobre <c>ToolTipService.IsEnabled</c> y no con un estilo
/// implícito de <c>ToolTip</c>: un estilo solo alcanza a los controles cuyo diccionario de recursos
/// lo ve, así que una ventana nueva —o un control creado en código, que es de donde salían 14 de
/// los 44— se lo salta sin avisar. La metadata la lee <b>todo</b> <c>FrameworkElement</c> del
/// proceso, incluidos los que aún no existen.
///
/// Tiene que llamarse ANTES de crear la primera ventana: la metadata de una propiedad se sella para
/// un tipo en cuanto se lee sobre una instancia suya, y a partir de ahí <c>OverrideMetadata</c>
/// lanza. Por eso vive en <c>OnStartup</c> y no dentro de una ventana.
/// </remarks>
public static class SinCarteles
{
    private static bool _puesto;

    /// <summary>Apaga los textos al pasar el ratón en todo el proceso. Idempotente.</summary>
    public static void Aplicar()
    {
        // Idempotente porque el contrato la llama para comprobar que de verdad apaga, y la app la
        // llama al arrancar: la segunda vez OverrideMetadata lanzaría, y un juez que revienta al
        // comprobar dice «roto» cuando lo que pasa es que ya estaba bien (aprendizaje nº17).
        if (_puesto) return;
        _puesto = true;

        ToolTipService.IsEnabledProperty.OverrideMetadata(
            typeof(FrameworkElement), new FrameworkPropertyMetadata(false));
    }
}
