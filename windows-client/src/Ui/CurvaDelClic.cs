using System.Windows.Media.Animation;

namespace U.WindowsClient.Ui;

/// <summary>
/// La curva del viaje al clic, envuelta para WPF. Promesa 240 (spec 022).
/// </summary>
/// <remarks>
/// Envoltorio y no una copia: la curva se juzga en el contrato sobre
/// <see cref="U.Graph.Surfaces.ComoViajaLaCarita.Curva"/>, así que si aquí se escribiera otra vez la
/// misma fórmula, el día que una cambie la promesa seguiría verde sobre la que nadie usa. Un solo
/// sitio donde vive la forma del movimiento.
/// </remarks>
public sealed class CurvaDelClic : IEasingFunction
{
    public double Ease(double t) => U.Graph.Surfaces.ComoViajaLaCarita.Curva(t);
}
