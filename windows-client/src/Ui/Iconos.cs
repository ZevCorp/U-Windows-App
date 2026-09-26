namespace U.WindowsClient.Ui;

/// <summary>
/// LOS ICONOS DE MIRACLE: los de la web (Lucide), como trazos que WPF lee. Puro —cadenas y nada
/// más— para que el contrato los juzgue sin pantalla (promesa 449, spec 054). Los dibuja
/// <see cref="Estudio.Icono"/> con grosor 2 y extremos redondos, que es como los pinta la web.
/// </summary>
/// <remarks>
/// GENERADO desde los SVG de lucide-static v1.48.0 (licencia ISC), no escrito a mano: cada elemento del SVG
/// —path, rect, circle, line, polyline— se vuelve una sola cadena de trazo en la rejilla de 24.
/// Dos detalles que el generador cuida y que rompen el dibujo si no: el primer «m» de cada path SVG
/// es ABSOLUTO aunque venga en minúscula (al juntar elementos se encadenaría con el anterior), y las
/// banderas de un arco se escriben separadas, porque WPF no lee la forma compacta de SVG («a2 2 0 012 2»).
///
/// Sustituyen a los glifos de Segoe MDL2 en las ventanas de la nota. MDL2 dibuja otra familia de
/// iconos —más finos, de otra geometría— y eso bastaba para que la nota de Windows no pareciera la de
/// la web.
/// </remarks>
public static class Iconos
{
    /// <summary>El trazo de cada icono, por su nombre de Lucide (el mismo que importa la web).</summary>
    public static readonly IReadOnlyDictionary<string, string> Trazos = new Dictionary<string, string>
    {
        ["mic"] = "M 12 19 v 3 M 19 10 v 2 a 7 7 0 0 1 -14 0 v -2 M 12 2 H 12 A 3 3 0 0 1 15 5 V 12 A 3 3 0 0 1 12 15 H 12 A 3 3 0 0 1 9 12 V 5 A 3 3 0 0 1 12 2 Z",
        ["square"] = "M 5 3 H 19 A 2 2 0 0 1 21 5 V 19 A 2 2 0 0 1 19 21 H 5 A 2 2 0 0 1 3 19 V 5 A 2 2 0 0 1 5 3 Z",
        ["pause"] = "M 15 3 H 18 A 1 1 0 0 1 19 4 V 20 A 1 1 0 0 1 18 21 H 15 A 1 1 0 0 1 14 20 V 4 A 1 1 0 0 1 15 3 Z M 6 3 H 9 A 1 1 0 0 1 10 4 V 20 A 1 1 0 0 1 9 21 H 6 A 1 1 0 0 1 5 20 V 4 A 1 1 0 0 1 6 3 Z",
        ["play"] = "M 5 5 a 2 2 0 0 1 3.008 -1.728 l 11.997 6.998 a 2 2 0 0 1 0.003 3.458 l -12 7 A 2 2 0 0 1 5 19 z",
        ["sparkles"] = "M 11.017 2.814 a 1 1 0 0 1 1.966 0 l 1.051 5.558 a 2 2 0 0 0 1.594 1.594 l 5.558 1.051 a 1 1 0 0 1 0 1.966 l -5.558 1.051 a 2 2 0 0 0 -1.594 1.594 l -1.051 5.558 a 1 1 0 0 1 -1.966 0 l -1.051 -5.558 a 2 2 0 0 0 -1.594 -1.594 l -5.558 -1.051 a 1 1 0 0 1 0 -1.966 l 5.558 -1.051 a 2 2 0 0 0 1.594 -1.594 z M 20 2 v 4 M 22 4 h -4 M 2 20 A 2 2 0 1 0 6 20 A 2 2 0 1 0 2 20 Z",
        ["send"] = "M 14.536 21.686 a 0.5 0.5 0 0 0 0.937 -0.024 l 6.5 -19 a 0.496 0.496 0 0 0 -0.635 -0.635 l -19 6.5 a 0.5 0.5 0 0 0 -0.024 0.937 l 7.93 3.18 a 2 2 0 0 1 1.112 1.11 z M 21.854 2.147 l -10.94 10.939",
        ["clipboard-copy"] = "M 9 2 H 15 A 1 1 0 0 1 16 3 V 5 A 1 1 0 0 1 15 6 H 9 A 1 1 0 0 1 8 5 V 3 A 1 1 0 0 1 9 2 Z M 8 4 H 6 a 2 2 0 0 0 -2 2 v 14 a 2 2 0 0 0 2 2 h 12 a 2 2 0 0 0 2 -2 v -2 M 16 4 h 2 a 2 2 0 0 1 2 2 v 4 M 21 14 H 11 M 15 10 l -4 4 4 4",
        ["copy"] = "M 10 8 H 20 A 2 2 0 0 1 22 10 V 20 A 2 2 0 0 1 20 22 H 10 A 2 2 0 0 1 8 20 V 10 A 2 2 0 0 1 10 8 Z M 4 16 c -1.1 0 -2 -0.9 -2 -2 V 4 c 0 -1.1 0.9 -2 2 -2 h 10 c 1.1 0 2 0.9 2 2",
        ["check"] = "M 20 6 9 17 l -5 -5",
        ["circle-check"] = "M 2 12 A 10 10 0 1 0 22 12 A 10 10 0 1 0 2 12 Z M 16 9 l -5.5 5.5 L 8 12",
        ["triangle-alert"] = "M 21.73 18 l -8 -14 a 2 2 0 0 0 -3.48 0 l -8 14 A 2 2 0 0 0 4 21 h 16 a 2 2 0 0 0 1.73 -3 M 12 9 v 4 M 12 17 h 0.01",
        ["circle-alert"] = "M 2 12 A 10 10 0 1 0 22 12 A 10 10 0 1 0 2 12 Z M 12 8 L 12 12 M 12 16 L 12.01 16",
        ["lightbulb"] = "M 15 14 c 0.2 -1 0.7 -1.7 1.5 -2.5 1 -0.9 1.5 -2.2 1.5 -3.5 A 6 6 0 0 0 6 8 c 0 1 0.2 2.2 1.5 3.5 0.7 0.7 1.3 1.5 1.5 2.5 M 9 18 h 6 M 10 22 h 4",
        ["chevron-down"] = "M 6 9 l 6 6 6 -6",
        ["chevron-right"] = "M 9 18 l 6 -6 -6 -6",
        ["x"] = "M 18 6 6 18 M 6 6 l 12 12",
        ["minus"] = "M 5 12 h 14",
        ["zap"] = "M 15.914 4 a 1.5 1.5 0 0 0 -2.474 -1.561 l -9 9 A 1.5 1.5 0 0 0 5.5 14 h 4.002 a 0.5 0.5 0 0 1 0.471 0.666 L 8.086 20 a 1.5 1.5 0 0 0 2.475 1.56 l 9 -9 A 1.5 1.5 0 0 0 18.5 10 h -3.997 a 0.5 0.5 0 0 1 -0.472 -0.667 z",
        ["file-text"] = "M 6 22 a 2 2 0 0 1 -2 -2 V 4 a 2 2 0 0 1 2 -2 h 8 a 2.4 2.4 0 0 1 1.704 0.706 l 3.588 3.588 A 2.4 2.4 0 0 1 20 8 v 12 a 2 2 0 0 1 -2 2 z M 14 2 v 5 a 1 1 0 0 0 1 1 h 5 M 10 9 H 8 M 16 13 H 8 M 16 17 H 8",
        ["layout-template"] = "M 4 3 H 20 A 1 1 0 0 1 21 4 V 9 A 1 1 0 0 1 20 10 H 4 A 1 1 0 0 1 3 9 V 4 A 1 1 0 0 1 4 3 Z M 4 14 H 11 A 1 1 0 0 1 12 15 V 20 A 1 1 0 0 1 11 21 H 4 A 1 1 0 0 1 3 20 V 15 A 1 1 0 0 1 4 14 Z M 17 14 H 20 A 1 1 0 0 1 21 15 V 20 A 1 1 0 0 1 20 21 H 17 A 1 1 0 0 1 16 20 V 15 A 1 1 0 0 1 17 14 Z",
        ["user-round"] = "M 7 8 A 5 5 0 1 0 17 8 A 5 5 0 1 0 7 8 Z M 20 21 a 8 8 0 0 0 -16 0",
        ["user-plus"] = "M 16 21 v -2 a 4 4 0 0 0 -4 -4 H 6 a 4 4 0 0 0 -4 4 v 2 M 5 7 A 4 4 0 1 0 13 7 A 4 4 0 1 0 5 7 Z M 19 8 L 19 14 M 22 11 L 16 11",
        ["search"] = "M 21 21 l -4.34 -4.34 M 3 11 A 8 8 0 1 0 19 11 A 8 8 0 1 0 3 11 Z",
        ["refresh-cw"] = "M 3 12 a 9 9 0 0 1 9 -9 9.75 9.75 0 0 1 6.74 2.74 L 21 8 M 21 3 v 5 h -5 M 21 12 a 9 9 0 0 1 -9 9 9.75 9.75 0 0 1 -6.74 -2.74 L 3 16 M 8 16 H 3 v 5",
        ["save"] = "M 15.2 3 a 2 2 0 0 1 1.4 0.6 l 3.8 3.8 a 2 2 0 0 1 0.6 1.4 V 19 a 2 2 0 0 1 -2 2 H 5 a 2 2 0 0 1 -2 -2 V 5 a 2 2 0 0 1 2 -2 z M 17 21 v -7 a 1 1 0 0 0 -1 -1 H 8 a 1 1 0 0 0 -1 1 v 7 M 7 3 v 4 a 1 1 0 0 0 1 1 h 7",
        ["star"] = "M 11.525 2.295 a 0.53 0.53 0 0 1 0.95 0 l 2.31 4.679 a 2.123 2.123 0 0 0 1.595 1.16 l 5.166 0.756 a 0.53 0.53 0 0 1 0.294 0.904 l -3.736 3.638 a 2.123 2.123 0 0 0 -0.611 1.878 l 0.882 5.14 a 0.53 0.53 0 0 1 -0.771 0.56 l -4.618 -2.428 a 2.122 2.122 0 0 0 -1.973 0 L 6.396 21.01 a 0.53 0.53 0 0 1 -0.77 -0.56 l 0.881 -5.139 a 2.122 2.122 0 0 0 -0.611 -1.879 L 2.16 9.795 a 0.53 0.53 0 0 1 0.294 -0.906 l 5.165 -0.755 a 2.122 2.122 0 0 0 1.597 -1.16 z",
        ["external-link"] = "M 15 3 h 6 v 6 M 10 14 21 3 M 18 13 v 6 a 2 2 0 0 1 -2 2 H 5 a 2 2 0 0 1 -2 -2 V 8 a 2 2 0 0 1 2 -2 h 6",
        ["info"] = "M 2 12 A 10 10 0 1 0 22 12 A 10 10 0 1 0 2 12 Z M 12 16 v -4 M 12 8 h 0.01",
        ["loader-circle"] = "M 21 12 a 9 9 0 1 1 -6.219 -8.56",
        ["log-out"] = "M 16 17 l 5 -5 -5 -5 M 21 12 H 9 M 9 21 H 5 a 2 2 0 0 1 -2 -2 V 5 a 2 2 0 0 1 2 -2 h 4",
        ["plus"] = "M 5 12 h 14 M 12 5 v 14",
        ["pencil"] = "M 21.174 6.812 a 1 1 0 0 0 -3.986 -3.987 L 3.842 16.174 a 2 2 0 0 0 -0.5 0.83 l -1.321 4.352 a 0.5 0.5 0 0 0 0.623 0.622 l 4.353 -1.32 a 2 2 0 0 0 0.83 -0.497 z M 15 5 l 4 4",
        ["bluetooth"] = "M 7 7 l 10 10 -5 5 V 2 l 5 5 L 7 17",
        ["smartphone"] = "M 7 2 H 17 A 2 2 0 0 1 19 4 V 20 A 2 2 0 0 1 17 22 H 7 A 2 2 0 0 1 5 20 V 4 A 2 2 0 0 1 7 2 Z M 12 18 h 0.01",
        ["laptop"] = "M 18 5 a 2 2 0 0 1 2 2 v 8.526 a 2 2 0 0 0 0.212 0.897 l 1.068 2.127 a 1 1 0 0 1 -0.9 1.45 H 3.62 a 1 1 0 0 1 -0.9 -1.45 l 1.068 -2.127 A 2 2 0 0 0 4 15.526 V 7 a 2 2 0 0 1 2 -2 z M 20.054 15.987 H 3.946",
        ["notebook-pen"] = "M 13.4 2 H 6 a 2 2 0 0 0 -2 2 v 16 a 2 2 0 0 0 2 2 h 12 a 2 2 0 0 0 2 -2 v -7.4 M 2 6 h 4 M 2 10 h 4 M 2 14 h 4 M 2 18 h 4 M 21.378 5.626 a 1 1 0 1 0 -3.004 -3.004 l -5.01 5.012 a 2 2 0 0 0 -0.506 0.854 l -0.837 2.87 a 0.5 0.5 0 0 0 0.62 0.62 l 2.87 -0.837 a 2 2 0 0 0 0.854 -0.506 z",
        ["shield-check"] = "M 20 13 c 0 5 -3.5 7.5 -7.66 8.95 a 1 1 0 0 1 -0.67 -0.01 C 7.5 20.5 4 18 4 13 V 6 a 1 1 0 0 1 1 -1 c 2 0 4.5 -1.2 6.24 -2.72 a 1.17 1.17 0 0 1 1.52 0 C 14.51 3.81 17 5 19 5 a 1 1 0 0 1 1 1 z M 9 12 l 2 2 4 -4",
        ["clipboard-list"] = "M 9 2 H 15 A 1 1 0 0 1 16 3 V 5 A 1 1 0 0 1 15 6 H 9 A 1 1 0 0 1 8 5 V 3 A 1 1 0 0 1 9 2 Z M 16 4 h 2 a 2 2 0 0 1 2 2 v 14 a 2 2 0 0 1 -2 2 H 6 a 2 2 0 0 1 -2 -2 V 6 a 2 2 0 0 1 2 -2 h 2 M 12 11 h 4 M 12 16 h 4 M 8 11 h 0.01 M 8 16 h 0.01",
        ["arrow-right"] = "M 5 12 h 14 M 12 5 l 7 7 -7 7",
        ["settings"] = "M 9.671 4.136 a 2.34 2.34 0 0 1 4.659 0 2.34 2.34 0 0 0 3.319 1.915 2.34 2.34 0 0 1 2.33 4.033 2.34 2.34 0 0 0 0 3.831 2.34 2.34 0 0 1 -2.33 4.033 2.34 2.34 0 0 0 -3.319 1.915 2.34 2.34 0 0 1 -4.659 0 2.34 2.34 0 0 0 -3.32 -1.915 2.34 2.34 0 0 1 -2.33 -4.033 2.34 2.34 0 0 0 0 -3.831 A 2.34 2.34 0 0 1 6.35 6.051 a 2.34 2.34 0 0 0 3.319 -1.915 M 9 12 A 3 3 0 1 0 15 12 A 3 3 0 1 0 9 12 Z",
        ["brain"] = "M 12 18 V 5 M 15 13 a 4.17 4.17 0 0 1 -3 -4 4.17 4.17 0 0 1 -3 4 M 17.598 6.5 A 3 3 0 1 0 12 5 a 3 3 0 1 0 -5.598 1.5 M 17.997 5.125 a 4 4 0 0 1 2.526 5.77 M 18 18 a 4 4 0 0 0 2 -7.464 M 19.967 17.483 A 4 4 0 1 1 12 18 a 4 4 0 1 1 -7.967 -0.517 M 6 18 a 4 4 0 0 1 -2 -7.464 M 6.003 5.125 a 4 4 0 0 0 -2.526 5.77",
        ["arrow-left"] = "M 12 19 l -7 -7 7 -7 M 19 12 H 5",
        ["circle-dot"] = "M 11 12 A 1 1 0 1 0 13 12 A 1 1 0 1 0 11 12 Z M 2 12 A 10 10 0 1 0 22 12 A 10 10 0 1 0 2 12 Z",
        // Plan y egreso, y los papeles del paciente (specs 058-059).
        ["pill"] = "M 10.5 20.5 l 10 -10 a 4.95 4.95 0 1 0 -7 -7 l -10 10 a 4.95 4.95 0 1 0 7 7 Z M 8.5 8.5 l 7 7",
        ["leaf"] = "M 11 20 a 10 10 0 0 0 10 -10 25.9 25.9 0 0 0 -1.04 -7.281 1 1 0 0 0 -1.755 -0.325 C 15.833 5.5 13 5.5 9.8 6.1 A 7 7 0 0 0 11 20 M 2 21 a 5 5 0 0 1 2.911 -4.544 C 7.613 15.212 8.351 15.24 11 13",
        ["calendar"] = "M 8 2 v 3 M 16 2 v 3 M 5 3 H 19 A 2 2 0 0 1 21 5 V 19 A 2 2 0 0 1 19 21 H 5 A 2 2 0 0 1 3 19 V 5 A 2 2 0 0 1 5 3 Z M 3 9 h 18",
        ["message-square-text"] = "M 22 17 a 2 2 0 0 1 -2 2 H 6.828 a 2 2 0 0 0 -1.414 0.586 l -2.202 2.202 A 0.71 0.71 0 0 1 2 21.286 V 5 a 2 2 0 0 1 2 -2 h 16 a 2 2 0 0 1 2 2 z M 7 11 h 10 M 7 15 h 6 M 7 7 h 8",
        ["trash-2"] = "M 10 11 v 6 M 14 11 v 6 M 19 6 v 14 a 2 2 0 0 1 -2 2 H 7 a 2 2 0 0 1 -2 -2 V 6 M 3 6 h 18 M 8 6 V 4 a 2 2 0 0 1 2 -2 h 4 a 2 2 0 0 1 2 2 v 2",
        ["printer"] = "M 6 18 H 4 a 2 2 0 0 1 -2 -2 v -5 a 2 2 0 0 1 2 -2 h 16 a 2 2 0 0 1 2 2 v 5 a 2 2 0 0 1 -2 2 h -2 M 6 9 V 3 a 1 1 0 0 1 1 -1 h 10 a 1 1 0 0 1 1 1 v 6 M 7 14 H 17 A 1 1 0 0 1 18 15 V 21 A 1 1 0 0 1 17 22 H 7 A 1 1 0 0 1 6 21 V 15 A 1 1 0 0 1 7 14 Z",
    };
}
