using System.Globalization;

namespace U.WindowsClient.Ui.Jev;

/// <summary>
/// EL «$» DE LA CABECERA: lo que lleva gastado la tarea, y solo con lo que el cliente dice haber facturado.
/// Promesa 373 (spec 049).
/// </summary>
/// <remarks>
/// NUNCA UN NÚMERO SUPUESTO. Hoy nadie lee <c>usage.input_tokens</c> (<c>ClienteTypeSafe.cs:78</c>,
/// <c>ElDecisor.cs</c>): un «$» calculado con un recuento imaginado sería una cifra falsa en la pantalla de un
/// hospital. Un paso sin tokens facturados enseña <see cref="TextosDeJev.SinDato"/> y el acumulado no se mueve;
/// hasta que A y C traigan los tokens (350, 367), eso es lo que se verá, y es la regla funcionando.
///
/// CINCO DECIMALES, no los cuatro del vídeo. 875 tokens con 20 puertas × $0,042 por millón = $0,000037 por paso
/// (<c>docs/anatomia-del-clic-2026-09-18.md</c> §3 × el precio publicado): con cuatro el contador enseñaría
/// «$0.0000» varios pasos seguidos y parecería roto.
///
/// UNA INSTANCIA POR TAREA. El acumulado es el de la tarea: una tarea nueva es un <see cref="CosteDeJev"/> nuevo,
/// y no hay método de reinicio que olvidarse de llamar.
/// </remarks>
public sealed class CosteDeJev
{
    /// <summary>Dólares por millón de tokens de entrada, el precio publicado (<c>docs/computer-use-con-jev-2026-09-21.md:41</c>).</summary>
    public const double PrecioPorMillon = 0.042;

    private bool _ultimoFacturado;

    /// <summary>Lo gastado por la tarea hasta ahora, en dólares.</summary>
    public double Acumulado { get; private set; }

    /// <summary>
    /// Suma una decisión. Con <c>null</c> —el cliente no dijo cuánto facturó— no suma nada, y el texto pasa a «—»:
    /// el paso no tiene precio conocido, y enseñar el acumulado de antes haría creer que este salió gratis.
    /// </summary>
    public void Acumular(int? tokensFacturados)
    {
        _ultimoFacturado = tokensFacturados.HasValue;
        if (tokensFacturados is int tokens) Acumulado += tokens * PrecioPorMillon / 1_000_000;
    }

    /// <summary>«$0.00004» si la última decisión trajo sus tokens; «—» si no.</summary>
    public string Texto => _ultimoFacturado
        ? "$" + Acumulado.ToString("0.00000", CultureInfo.InvariantCulture)
        : TextosDeJev.SinDato;
}
