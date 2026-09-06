namespace U.WindowsClient.Ui;

/// <summary>
/// CUÁNDO ASOMA la línea «Escríbele…» junto a la carita suelta. Separada de la ventana para que el
/// contrato la juzgue sin ratón de verdad (promesa 167, spec 011).
/// </summary>
/// <remarks>
/// EL NÚMERO ESTÁ AFINADO A LA TERCERA, y las dos primeras veces se rechazaron mirando la pantalla:
///
///   · rama 008: salía AL INSTANTE de entrar el ratón. Rechazada — ir a agarrar la carita para
///     arrastrarla invocaba un cartel que nadie había pedido.
///   · 550 ms: rechazada igual. «Yo pongo el mouse encima y lo veo muy rápido» (2026-09-06).
///   · segundo y medio: lo que pidió el dueño, y es lo que hay.
///
/// Por eso el contrato exige un suelo de 1.200 ms y comprueba explícitamente que 550 ya NO basta:
/// un número que costó tres vueltas no puede volver a bajarse sin que alguien se entere.
///
/// Y por lo mismo, un arrastre en marcha nunca la llama por mucho que dure: ir a moverla no es ir a
/// escribirle, sin importar cuánto tiempo lleve la mano encima.
/// </remarks>
public static class ReglaDeLaLinea
{
    /// <summary>Cuánto tiene que estar quieto el ratón sobre la carita antes de que la línea asome.</summary>
    public const int ReposoMs = 1500;

    /// <param name="quietoMs">Cuánto lleva el ratón quieto sobre la carita (o sobre la propia
    /// línea, una vez asomada).</param>
    /// <param name="arrastrando">Si ahora mismo se está tirando de la carita para moverla.</param>
    public static bool Asoma(int quietoMs, bool arrastrando) => !arrastrando && quietoMs >= ReposoMs;
}
