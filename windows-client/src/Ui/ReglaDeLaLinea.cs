namespace U.WindowsClient.Ui;

/// <summary>
/// CUÁNDO ASOMA la línea «Escríbele…» junto a la carita suelta. Separada de la ventana para que el
/// contrato la juzgue sin ratón de verdad (promesa 165, spec 011).
/// </summary>
/// <remarks>
/// La primera versión (rama <c>jose/la-carita-es-el-unico-boton</c>, spec 008) la enseñaba AL
/// INSTANTE de entrar el ratón. Probada a mano el 2026-09-06, el dueño la rechazó por eso mismo:
/// «que yo tenga que hacer hover durante más tiempo… así si yo solo iba a querer arrastrar la
/// carita, esa text área no aparece». Ir a agarrar algo y quedarse a escribirle son gestos que
/// arrancan igual —el ratón se posa encima—, y solo el tiempo que se queda ahí los distingue.
///
/// Por eso <see cref="ReposoMs"/> no es cosmético: es el tiempo que separa «voy a moverla» de «voy
/// a escribirle», y tiene que ser mayor que lo que dura posarse antes de tirar. Y por lo mismo, un
/// arrastre en marcha nunca la llama por mucho que dure: ir a moverla no es ir a escribirle, sin
/// importar cuánto tiempo lleve la mano encima.
/// </remarks>
public static class ReglaDeLaLinea
{
    /// <summary>
    /// Cuánto tiene que estar quieto el ratón sobre la carita antes de que la línea asome. 550 ms:
    /// más que el medio segundo que separa un gesto deliberado de un roce de paso, y bastante menos
    /// que lo que tarda alguien en decidir escribir.
    /// </summary>
    public const int ReposoMs = 550;

    /// <param name="quietoMs">Cuánto lleva el ratón quieto sobre la carita (o sobre la propia
    /// línea, una vez asomada).</param>
    /// <param name="arrastrando">Si ahora mismo se está tirando de la carita para moverla.</param>
    public static bool Asoma(int quietoMs, bool arrastrando) => !arrastrando && quietoMs >= ReposoMs;
}
