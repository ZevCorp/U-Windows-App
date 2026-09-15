using System.Windows;
using Microsoft.Win32;

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
    private static (Rect Libre, LadoDeLaBandeja Lado, IconosDeLaBandeja Iconos) _ultimo;

    /// <summary>El área libre, por dónde está la barra y dónde tiene los iconos.</summary>
    public static (Rect Libre, LadoDeLaBandeja Lado, IconosDeLaBandeja Iconos) Mirar()
    {
        if (DateTime.UtcNow - _visto < Caducidad) return _ultimo;

        var libre = SystemParameters.WorkArea;
        var pantalla = new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
        _ultimo = (libre, ReglaDeLaBandeja.Lado(pantalla, libre), DondeEstanLosIconos());
        _visto = DateTime.UtcNow;
        return _ultimo;
    }

    /// <summary>La primera build de Windows 11. Debajo de ella, el sistema es un 10.</summary>
    private const int PrimeraDelOnce = 22000;

    /// <summary>
    /// Iconos al centro o a la izquierda, según el ajuste «Alineación de la barra de tareas».
    /// </summary>
    /// <remarks>
    /// <c>TaskbarAl</c> lo escribe Windows 11 al TOCAR el ajuste: 0 = izquierda, 1 = centro. Mientras
    /// nadie lo haya tocado, EL VALOR NO EXISTE — y ahí está la trampa, porque «no existe» significa
    /// cosas opuestas según el sistema: en un 11 de fábrica significa CENTRO, y en un 10 significa
    /// izquierda, que es el único sitio donde el 10 los pone.
    ///
    /// Se comprobó en la máquina del dueño (2026-09-14): Windows 11, iconos centrados a la vista, y
    /// <c>TaskbarAl</c> vacío. Dar por hecho «izquierda» cuando falta el valor habría mandado el
    /// notch al centro en la configuración MÁS común que existe — justo encima del racimo de iconos
    /// que esto se escribió para esquivar. Un fallo que solo aparece en la instalación de fábrica es
    /// el que se lleva por delante a todos los usuarios nuevos y a ninguno de los que probamos.
    ///
    /// Si la lectura revienta, se contesta lo que diga la versión: peor que acertar, mejor que
    /// inventar — y el coste de errar es que el notch se ponga en el otro hueco, que se sigue viendo.
    /// </remarks>
    private static IconosDeLaBandeja DondeEstanLosIconos()
    {
        var porDefecto = Environment.OSVersion.Version.Build >= PrimeraDelOnce
            ? IconosDeLaBandeja.AlCentro        // Windows 11 de fábrica
            : IconosDeLaBandeja.ALaIzquierda;   // Windows 10, que no tiene otra
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
            if (k?.GetValue("TaskbarAl") is not int al) return porDefecto;
            return al == 1 ? IconosDeLaBandeja.AlCentro : IconosDeLaBandeja.ALaIzquierda;
        }
        catch { return porDefecto; }
    }
}
