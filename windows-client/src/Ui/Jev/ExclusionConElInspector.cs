namespace U.WindowsClient.Ui.Jev;

/// <summary>
/// EL OVERLAY DE JEV Y EL INSPECTOR NO SE ENCIENDEN A LA VEZ. Promesa 371 (spec 049). La regla es pura
/// —dos banderas y una pregunta— y cada una de las dos superficies la consulta antes de encenderse.
/// </summary>
/// <remarks>
/// POR QUÉ NO BASTA CON ELEGIR OTROS COLORES. El overlay de Jev y el inspector pintan cajas sobre la misma
/// pantalla, y tres de sus colores se parecen con significados distintos (medido el 2026-09-22, HSL):
/// el ámbar de <see cref="PaletaDeJev.Ausente"/> está a 3,0° del «shell sin mapear» del inspector, el
/// verde de <see cref="PaletaDeJev.Candidata"/> a 25,4° del «shell mapeado», y el rosa de lo pulsado a
/// 14,9° del rojo de fallo. Recolorear uno de los dos rompe o el plano de TipTour o una paleta que el
/// repo ya pagó cara (<c>windows-client/CLAUDE.md</c> §Paleta: el destello amarillo indistinguible del
/// ámbar). La única forma de que un color no signifique dos cosas es que no se vean juntos.
///
/// Las dos banderas las ponen y las quitan sus dueños —<c>UiInspector.Start/Stop</c> y el overlay de Jev
/// al mostrarse y esconderse— y las dos viven en el hilo de la interfaz, que es donde se encienden las
/// dos superficies: no hay carrera que proteger.
/// </remarks>
public static class ExclusionConElInspector
{
    /// <summary>Cuál de las dos superficies quiere encenderse.</summary>
    public enum Cual
    {
        /// <summary>El overlay de Jev: las cajas de las candidatas y la de lo pulsado.</summary>
        Overlay,
        /// <summary>El inspector de elementos (<c>Uia/UiInspector.cs</c>).</summary>
        Inspector,
    }

    /// <summary>¿El inspector está encendido? Lo pone <c>UiInspector</c> al arrancar y lo quita al parar.</summary>
    public static bool InspectorActivo { get; set; }

    /// <summary>¿El overlay de Jev está encendido? Lo pone el overlay al mostrarse y lo quita al esconderse.</summary>
    public static bool OverlayActivo { get; set; }

    /// <summary>¿Puede encenderse esta superficie ahora? Solo si la otra está apagada.</summary>
    public static bool PuedeEncender(Cual cual) => cual switch
    {
        Cual.Overlay => !InspectorActivo,
        Cual.Inspector => !OverlayActivo,
        _ => throw new ArgumentOutOfRangeException(nameof(cual), cual, "solo hay dos superficies: Overlay e Inspector"),
    };

    /// <summary>
    /// Por qué esta superficie no puede encenderse, nombrando la que lo impide; <c>null</c> si puede. Es el
    /// texto de la línea de log de quien se queda apagado: «no se enciende» a secas no distingue entre
    /// «la otra está encendida» y cualquier otro fallo (patrón nº2).
    /// </summary>
    public static string? PorQueNo(Cual cual) => PuedeEncender(cual) ? null : cual switch
    {
        Cual.Inspector => "el overlay de Jev está encendido, y comparten ámbar, verde y rosa con otro significado (promesa 371); con Jev apagado, el inspector se enciende",
        _ => "el inspector está encendido, y comparten ámbar, verde y rosa con otro significado (promesa 371); con el inspector apagado, el overlay se enciende",
    };
}
