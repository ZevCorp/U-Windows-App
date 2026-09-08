namespace U.WindowsClient.Ui;

/// <summary>Lo que hace el botón físico del collar al pulsarse.</summary>
public enum QueHaceElBotonDelCollar
{
    /// <summary>No había consulta: se abre y se empieza a grabar.</summary>
    AbrirLaConsultaYGrabar,

    /// <summary>La consulta está abierta y parada: se empieza a grabar.</summary>
    Grabar,

    /// <summary>Se estaba grabando: se pausa, sin cerrar nada.</summary>
    Pausar,

    /// <summary>Estaba en pausa: vuelve a grabar sobre la misma consulta.</summary>
    Reanudar,

    /// <summary>Dos toques seguidos: se cierra la consulta y se pide la nota.</summary>
    Terminar,
}

/// <summary>
/// EL BOTÓN DEL COLLAR: UN TOQUE PAUSA, DOS TERMINAN. Promesas 185 y 186 (spec 014).
/// </summary>
/// <remarks>
/// Hasta el 2026-09-07 el botón iba al mismo sitio que el clic en la carita: abría la voz en vivo.
/// El dueño lo cambió dos veces ese día — primero a «graba la consulta» (promesa 184, hoy retirada)
/// y luego al gesto de dos niveles que vive aquí. La razón de fondo no cambió: quien lleva el collar
/// puesto está con un paciente delante, y lo que quiere del único botón que tiene es la consulta.
///
/// EL COLLAR NO DISTINGUE UN TOQUE DE DOS. Medido el 2026-08-13 y confirmado el 2026-09-07 con una
/// sonda: manda <c>0x01</c> al apretar y <c>0x05</c> al soltar, y nada más. Contarlos es cosa
/// nuestra, y por eso esta regla recibe <c>msDesdeElToqueAnterior</c> en vez de un «hubo doble».
///
/// EL PRIMER TOQUE ACTÚA YA, y no espera a ver si viene un segundo. Es lo contrario de lo que haría
/// un menú, y se puede porque <b>pausar es un prefijo compatible de terminar</b>: las dos cosas
/// paran el audio. Un doble toque hace «pausa… y cierre», y no se pierde nada — el audio se cortó en
/// el primer toque, que es donde el médico quiso cortarlo. A cambio, cada toque tiene respuesta
/// inmediata, que es lo que hace que un botón que no se ve se sienta vivo (es el mismo argumento de
/// la promesa 147: un botón que tarda un cuarto de segundo se siente roto).
/// </remarks>
public static class ReglaDelBotonDelCollar
{
    /// <summary>
    /// Cuánto se espera entre dos toques para que cuenten como uno solo.
    /// </summary>
    /// <remarks>
    /// 400 ms Y NO LOS 250 DE LA CARITA (<see cref="ReglaDelToque"/>), y la diferencia no es un
    /// gusto: aquel número es para un ratón bajo la mano. Este botón se pulsa con un dedo, sobre una
    /// pieza pequeña que cuelga del cuello, a veces por encima de la ropa y sin mirarla.
    /// </remarks>
    public const int VentanaDelDobleToqueMs = 400;

    /// <param name="hayConsulta">Existe una ventana de consulta, esté donde esté.</param>
    /// <param name="grabando">…y está grabando ahora mismo.</param>
    /// <param name="pausada">…o está en pausa, que también es una sesión viva.</param>
    /// <param name="msDesdeElToqueAnterior">
    /// Desde el toque anterior. Grande (o <c>long.MaxValue</c>) si es el primero.
    /// </param>
    /// <param name="msGrabando">
    /// Qué edad tiene la sesión viva. Cero si no hay ninguna.
    /// </param>
    public static QueHaceElBotonDelCollar AlPulsar(
        bool hayConsulta, bool grabando, bool pausada,
        long msDesdeElToqueAnterior, long msGrabando)
    {
        bool haySesion = grabando || pausada;

        // DOS TOQUES TERMINAN — pero nunca una sesión que empezó en este mismo golpeteo. Sin esa
        // guarda, dos toques desde parado abrirían la consulta y la cerrarían 300 ms después: una
        // nota de nada y un encounter gastado. Que la sesión sea MÁS VIEJA que la ventana es la
        // forma exacta de decir «esto ya venía de antes de que empezaras a golpear».
        if (haySesion
            && msDesdeElToqueAnterior < VentanaDelDobleToqueMs
            && msGrabando > VentanaDelDobleToqueMs)
            return QueHaceElBotonDelCollar.Terminar;

        // TERMINAR VALE TAMBIÉN DESDE LA PAUSA (lo cubre la línea de arriba, porque «pausada» cuenta
        // como sesión viva): el médico pausa, lo piensa, y cierra. Exigirle volver a grabar para
        // poder terminar sería pedirle que reabra el micrófono para cerrarlo.

        if (!hayConsulta) return QueHaceElBotonDelCollar.AbrirLaConsultaYGrabar;
        if (pausada) return QueHaceElBotonDelCollar.Reanudar;
        if (grabando) return QueHaceElBotonDelCollar.Pausar;
        return QueHaceElBotonDelCollar.Grabar;
    }
}
