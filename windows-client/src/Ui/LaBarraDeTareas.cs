using System.Windows;

namespace U.WindowsClient.Ui;

/// <summary>
/// LO QUE SE PUEDE SABER DE LA BARRA DE TAREAS SIN TOCARLA: por dónde está y dónde amontona los
/// iconos. Es el sensor; quien decide con esto es <see cref="ReglaDeLaBandeja"/>.
/// </summary>
/// <remarks>
/// La separación es la de siempre en este repo —sensor tonto, regla pura— y aquí paga doble: lo
/// único que no se puede juzgar sin un Windows delante es leer el registro, y son cuatro líneas.
/// Todo el criterio vive al otro lado, donde el contrato lo alcanza.
///
/// SE CACHEA UNOS SEGUNDOS porque esto se pregunta en cada línea que entra al panel —y entran a
/// ritmo de transcripción, varias por segundo— mientras la respuesta cambia como mucho una vez al
/// año. Pero se cachea CON CADUCIDAD y no para siempre: mover la barra de tareas o cambiar la
/// alineación son ajustes de un clic, y un panel que se quedara en el sitio de antes hasta reiniciar
/// la aplicación parecería roto.
/// </remarks>
public static class LaBarraDeTareas
{
    private static readonly TimeSpan Caducidad = TimeSpan.FromSeconds(4);
    private static DateTime _visto = DateTime.MinValue;
    private static (Rect Libre, LadoDeLaBandeja Lado) _ultimo;

    /// <summary>
    /// El área libre y por dónde está la barra. Ya NO dice dónde amontona los iconos: con el notch
    /// arriba al centro (spec 028) no hay iconos que esquivar, y la promesa 241 que lo pedía se retiró.
    /// </summary>
    public static (Rect Libre, LadoDeLaBandeja Lado) Mirar()
    {
        if (DateTime.UtcNow - _visto < Caducidad) return _ultimo;

        var libre = SystemParameters.WorkArea;
        var pantalla = new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
        _ultimo = (libre, ReglaDeLaBandeja.Lado(pantalla, libre));
        _visto = DateTime.UtcNow;
        return _ultimo;
    }
}
