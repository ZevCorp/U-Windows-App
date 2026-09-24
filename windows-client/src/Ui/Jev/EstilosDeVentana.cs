using System.Windows;

namespace U.WindowsClient.Ui.Jev;

/// <summary>
/// CÓMO SON LAS VENTANAS DE JEV PARA WINDOWS, escrito como números que se juzgan sin abrir ninguna. Promesas 379 y
/// 385 (spec 049). Las ventanas (fase 8) aplican estas máscaras con <c>SetWindowLong(GWL_EXSTYLE)</c> y
/// <c>SetWindowDisplayAffinity</c>, y eso se lee en su fuente; aquí vive el QUÉ.
/// </summary>
/// <remarks>
/// LOS BITS SON LOS DE <c>AuraDeAprendizaje.cs:84-86</c>, que ya los usa por la misma razón: una ventana nuestra
/// encima del trabajo que no puede tomar ni el ratón ni el foco. <c>WS_EX_TRANSPARENT</c> solo deja pasar el
/// ratón en una ventana EN CAPAS, por eso van juntos.
///
/// EL PANEL NO TOMA EL RATÓN NI EL FOCO NUNCA, y su máscara es una constante y no una función del estado: no
/// tiene campo de texto (spec 049 §Lo que NO entra), así que no hay ningún estado en el que lo necesite. Hoy vale
/// lo mismo que la del overlay; son dos constantes porque son dos promesas, y si una cambia la otra no se entera.
/// </remarks>
public static class EstilosDeVentana
{
    /// <summary><c>WS_EX_TRANSPARENT</c>: los clics pasan a la ventana de debajo.</summary>
    public const uint DejaPasarElRaton = 0x20;

    /// <summary><c>WS_EX_LAYERED</c>: la ventana va en capas, que es lo que hace efectivo a <see cref="DejaPasarElRaton"/>.</summary>
    public const uint EnCapas = 0x80000;

    /// <summary><c>WS_EX_TOOLWINDOW</c>: sin botón en la barra de tareas ni sitio en Alt+Tab.</summary>
    public const uint DeHerramienta = 0x80;

    /// <summary><c>WS_EX_NOACTIVATE</c>: mostrarla o pulsarla no le quita el foco a la app de trabajo.</summary>
    public const uint NoActivable = 0x08000000;

    /// <summary>La máscara extendida del overlay: transparente al ratón, en capas, de herramienta y no activable (379).</summary>
    public const uint ExtendidosDelOverlay = DejaPasarElRaton | EnCapas | DeHerramienta | NoActivable;

    /// <summary>La máscara extendida del panel, en reposo y en corrida: nunca toma el ratón ni el foco (385).</summary>
    public const uint ExtendidosDelPanel = DejaPasarElRaton | EnCapas | DeHerramienta | NoActivable;

    /// <summary>
    /// <c>WDA_EXCLUDEFROMCAPTURE</c>: el overlay no sale en las capturas. Lo que se pinta encima de la app no puede
    /// acabar en la foto que se le manda a nadie, ni tapar lo que la foto tenía que enseñar (379).
    /// </summary>
    public const uint Afinidad = 0x11;

    /// <summary>
    /// Una ventana de overlay por monitor, cada una con el <c>rcMonitor</c> ENTERO en físicos: ni el área de
    /// trabajo —la barra de tareas también se pulsa—, ni solo el primario —el patrón de <c>InspectorOverlay</c> y
    /// <c>AuraDeAprendizaje</c>, que dejan sin cajas los demás—, ni en DIP.
    /// </summary>
    /// <param name="rcMonitores">El <c>rcMonitor</c> de cada monitor, en físicos del escritorio virtual.</param>
    public static IReadOnlyList<Rect> UnaPorMonitor(IReadOnlyList<Rect> rcMonitores)
    {
        ArgumentNullException.ThrowIfNull(rcMonitores);
        return rcMonitores.ToList();
    }
}
