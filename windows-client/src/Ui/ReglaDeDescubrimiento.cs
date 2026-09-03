namespace U.WindowsClient.Ui;

/// <summary>
/// CUÁNDO SE ENSEÑA LA PISTA de gestos bajo la carita («toca: hablar · escribe: texto · mantén:
/// menú»), separada de la UI para que el contrato la juzgue (promesa 116, spec 008).
/// </summary>
/// <remarks>
/// Quitar los botones convierte cada acción en un gesto, y —como dice el interruptor del Mac— un
/// gesto que hay que saber no es un botón, es un secreto. La pista existe para que no lo sea. Pero
/// una pista que no se calla es un cartel: a la tercera vez, quien la vio ya la sabe o no la
/// quiere, y la carita vuelve a ser solo la carita. Se cuenta por ARRANQUE en que se enseñó, no por
/// cada roce del ratón: tres roces seguidos en el primer minuto no son tres oportunidades de
/// aprenderla.
/// </remarks>
public static class ReglaDeDescubrimiento
{
    /// <summary>En cuántos arranques se enseña antes de callar.</summary>
    public const int Veces = 3;

    /// <summary>Si todavía toca enseñarla, sabiendo cuántas veces se enseñó ya.</summary>
    public static bool Mostrar(int vecesMostrada) => vecesMostrada < Veces;
}
