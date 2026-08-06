using System.Windows;

namespace U.WindowsClient.Ui;

/// <summary>
/// «La ventana está viajando ahora mismo». Mientras lo esté, nadie le toca la posición.
/// </summary>
/// <remarks>
/// Existe porque el movimiento con física se moría antes de verse y no había forma de notarlo: la
/// carita llegaba al destino, que es lo que se le pide, solo que de un salto.
///
/// La causa es que hay DOS clases de código que mueven esta ventana y no se conocen entre sí. Uno
/// anima —el lanzamiento al borde, y la carita yendo junto a lo que señala—. El otro corrige: cada
/// vez que la ventana cambia de tamaño hay que recolocarla para que no se salga de la pantalla, y
/// esa corrección asigna <c>Left</c>/<c>Top</c> directamente, lo que en WPF CANCELA cualquier
/// animación sobre esas propiedades.
///
/// Y los cambios de tamaño llueven justo mientras se viaja: al soltar la carita se reordena la barra
/// hacia el lado nuevo, y al señalar algo aparece texto en el globo. Así que la animación se moría en
/// el primer fotograma y la ventana daba el salto que la corrección le mandaba (2026-08-06). Solo
/// sobrevivía el movimiento al abrir el catálogo de apps, que es el único que no cambia el tamaño —
/// y era justo el único que el usuario veía suave, lo que confirmó dónde estaba el problema.
///
/// Se lleva por TIEMPO y no por un evento de fin de animación: quien anima son dos sitios distintos
/// y con dos propiedades cada uno, así que contar finales es contar mal. Saber cuánto va a durar el
/// viaje se sabe al empezarlo.
/// </remarks>
internal static class Vuelo
{
    private static DateTime _hasta = DateTime.MinValue;

    /// <summary>¿Hay un viaje en curso? Si lo hay, la posición es suya.</summary>
    public static bool EnCurso => DateTime.UtcNow < _hasta;

    /// <summary>Empieza un viaje de esta duración. El margen cubre el último cuadro.</summary>
    public static void Empieza(Duration d)
    {
        var t = d.HasTimeSpan ? d.TimeSpan : TimeSpan.Zero;
        _hasta = DateTime.UtcNow + t + TimeSpan.FromMilliseconds(50);
    }

    /// <summary>Se acabó: alguien ha puesto la ventana en un sitio a mano.</summary>
    public static void Termina() => _hasta = DateTime.MinValue;
}
