using System.Windows;

namespace U.WindowsClient.Ui;

/// <summary>Por qué lado de la pantalla está la barra de tareas.</summary>
public enum LadoDeLaBandeja { Abajo, Arriba, Izquierda, Derecha }

/// <summary>
/// DÓNDE SE PONE EL NOTCH PARA QUE NO TAPE A NADIE.
/// </summary>
/// <remarks>
/// El panel de acciones vivía clavado abajo a la izquierda con un `Left = wa.Left + 12`. Sobre un
/// Windows 10 —iconos pegados a la izquierda— esa esquina está vacía y la elección era buena. Sobre
/// un Windows 11 de fábrica NO: los iconos van al CENTRO, y el hueco libre pasa a ser justo la
/// esquina que antes estaba ocupada. Una posición fija acierta en una de las dos configuraciones y
/// falla en la otra, y la que falla es la que trae de serie el sistema que la gente instala hoy.
///
/// LO QUE DECIDE ES DÓNDE ESTÁN LOS ICONOS, no dónde está la barra: el notch flota ENCIMA de la
/// barra de tareas, así que nunca choca físicamente con nada. Lo que se busca es que se lea como
/// una extensión del trozo vacío de la barra y no como un cartel plantado sobre el racimo de
/// iconos — que es lo que lo hace parecer del sistema en vez de parecer un intruso.
///
/// PURA Y SIN PANTALLA, como <see cref="ReglaDelMuelle"/> y por lo mismo: el contrato juzga esto en
/// un segundo y sin escritorio, y <see cref="PanelDeAcciones"/> pregunta aquí en vez de llevar la
/// cuenta suelta entre dos asignaciones de <c>Left</c>. Lo juzgado y lo que corre no pueden
/// discrepar (aprendizaje nº16).
/// </remarks>
public static class ReglaDeLaBandeja
{
    /// <summary>
    /// El aire entre el notch y la barra de tareas.
    ///
    /// No es cero a propósito: pegado del todo, el notch y la barra se leen como UNA pieza rota —dos
    /// negros distintos tocándose sin junta—. Con un hilo de escritorio en medio se lee como lo que
    /// es, algo que flota justo encima. Es el mismo margen que Apple deja bajo la isla dinámica.
    /// </summary>
    public const double Aire = 8;

    /// <summary>
    /// Por dónde está la barra de tareas, deducido del trozo de pantalla que se reserva para ella.
    /// </summary>
    /// <remarks>
    /// Se deduce del área de trabajo y NO se pregunta a <c>Shell_TrayWnd</c> por Win32, aunque sería
    /// lo obvio: <c>GetWindowRect</c> devuelve píxeles físicos y todo lo que coloca ventanas en esta
    /// aplicación trabaja en unidades de WPF. Mezclar las dos escalas es correcto al 100 % de zoom y
    /// falso al 150 %, que es donde está media base instalada — un fallo que solo aparece en la
    /// máquina de otro es el peor de los fallos.
    ///
    /// Con la barra oculta automáticamente no se reserva nada y el área de trabajo ES la pantalla:
    /// se contesta <see cref="LadoDeLaBandeja.Abajo"/>, que es donde está el 95 % de las barras y
    /// donde el notch queda bien de todas formas.
    /// </remarks>
    public static LadoDeLaBandeja Lado(Rect pantalla, Rect libre)
    {
        if (libre.Bottom < pantalla.Bottom - 0.5) return LadoDeLaBandeja.Abajo;
        if (libre.Top > pantalla.Top + 0.5) return LadoDeLaBandeja.Arriba;
        if (libre.Left > pantalla.Left + 0.5) return LadoDeLaBandeja.Izquierda;
        if (libre.Right < pantalla.Right - 0.5) return LadoDeLaBandeja.Derecha;
        return LadoDeLaBandeja.Abajo;
    }

    /// <summary>
    /// La caja donde va el notch, en coordenadas de pantalla.
    /// </summary>
    /// <remarks>
    /// Tres decisiones, y las tres se pueden leer del resultado:
    ///
    ///  1. <b>Se apoya en la barra.</b> Sea cual sea el lado, el notch se pega al borde del área de
    ///     trabajo que la barra ocupa, con <see cref="Aire"/> de junta. Con la barra arriba, el
    ///     notch cuelga de ella; con la barra abajo, se apoya encima.
    ///  2. <b>Va donde no hay iconos.</b> Iconos al centro (Windows 11 de fábrica) → el notch se va
    ///     a la esquina. Iconos a la izquierda (Windows 10, o un 11 configurado) → el notch se va al
    ///     centro. Es un intercambio, no dos casos: siempre ocupa la mitad que la barra deja libre.
    ///  3. <b>Con la barra en vertical, al centro y abajo.</b> Ahí no hay «esquina libre» que
    ///     valga —los iconos bajan por el costado entero— y el centro inferior es el sitio que no
    ///     estorba a nada. Es también la salida honesta del caso raro: el 11 ya no admite barras
    ///     verticales, así que esto solo lo ve quien sigue en el 10.
    ///
    /// Y al final se mete a la fuerza dentro del área libre: un notch ancho en una pantalla estrecha
    /// no puede acabar con media caja fuera del cristal.
    /// </remarks>
    /// <param name="libre">El área de trabajo: la pantalla menos lo que se reserva la barra.</param>
    /// <param name="notch">Lo que mide el panel ahora mismo.</param>
    /// <remarks>
    /// ARRIBA Y AL CENTRO (promesa 251, spec 028). Hasta el 2026-09-16 la pieza se apoyaba en la barra
    /// de tareas y buscaba la mitad que los iconos dejaban libre —la promesa 241, que se retiró con su
    /// motivo escrito—. El dueño la subió al centro de arriba, y ahí no hay iconos que esquivar: por eso
    /// se fue con ella el sensor de alineación, en vez de quedarse sin uso.
    ///
    /// Se sigue midiendo contra el ÁREA LIBRE y no contra la pantalla, y eso no es un detalle: con la
    /// barra de tareas puesta arriba, el área libre ya la excluye y la pieza queda justo debajo en vez
    /// de encima de ella.
    /// </remarks>
    public static Rect ArribaAlCentro(Rect libre, Size notch)
    {
        double x = libre.Left + (libre.Width - notch.Width) / 2;
        double y = libre.Top + Aire;

        // Dentro del cristal, siempre. Math.Max por delante de Math.Min para que una pieza más grande
        // que el hueco se quede pegada al borde de arriba/izquierda en vez de salirse por los dos.
        x = Math.Max(libre.Left, Math.Min(x, libre.Right - notch.Width));
        y = Math.Max(libre.Top, Math.Min(y, libre.Bottom - notch.Height));
        return new Rect(x, y, notch.Width, notch.Height);
    }
}
