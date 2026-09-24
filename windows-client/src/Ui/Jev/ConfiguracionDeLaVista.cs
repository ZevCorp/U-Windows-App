namespace U.WindowsClient.Ui.Jev;

/// <summary>
/// SI EL OVERLAY DE JEV SE ENCIENDE, y por qué quedó como quedó. Promesa 379 (spec 049).
/// </summary>
/// <remarks>
/// APAGADO POR DEFECTO. El overlay pinta cajas encima de la app de trabajo —SAP con pacientes delante—, y lo que
/// no se ha medido en el PC real (la CPU de la ventana en capas animada, el orden en Z con el muelle) no se enciende
/// solo. Encenderlo es un acto explícito: <c>U_JEV_OVERLAY=si</c>, leído al encender Jev. El panel y la flecha no
/// dependen de esto: nacen con Jev.
///
/// PURA, COMO <see cref="Decision.ConfiguracionDelDecisor"/>: el entorno se pasa, no se lee, para que el contrato
/// juzgue cada caso sin ensuciar el de la máquina que lo corre. Y con su misma regla para lo que no se entiende:
/// queda lo seguro —apagado— y se dice QUÉ se leyó, para que se pueda corregir. «sin U_JEV_OVERLAY» a secas para
/// una variable que sí estaba («no», «1», vacía) sería un mensaje que no distingue sus causas (patrón nº2).
/// </remarks>
public sealed class ConfiguracionDeLaVista
{
    /// <summary>La variable que enciende el overlay.</summary>
    public const string Variable = "U_JEV_OVERLAY";

    /// <summary>Si el overlay se enciende al encender Jev.</summary>
    public bool OverlayEncendido { get; }

    /// <summary>Lo que se leyó, en pocas palabras: «sin U_JEV_OVERLAY», «U_JEV_OVERLAY=si»… Es lo que va entre paréntesis en el estado de la vista.</summary>
    public string Motivo { get; }

    /// <summary>Por qué quedó así, para la línea de log y de estado. Describe lo que se leyó; nunca concluye (patrón nº2).</summary>
    public string Porque => $"overlay {(OverlayEncendido ? "encendido" : "apagado")}: {Motivo}";

    private ConfiguracionDeLaVista(bool overlayEncendido, string motivo)
    {
        OverlayEncendido = overlayEncendido;
        Motivo = motivo;
    }

    /// <summary>Lee el entorno y decide si el overlay se enciende.</summary>
    /// <param name="entorno">De dónde salen las variables: <c>Environment.GetEnvironmentVariable</c> en la app, uno de mentira en el contrato.</param>
    public static ConfiguracionDeLaVista Leer(Func<string, string?> entorno)
    {
        ArgumentNullException.ThrowIfNull(entorno);
        string? pedido = entorno(Variable);
        if (pedido == null) return new ConfiguracionDeLaVista(false, $"sin {Variable}");
        // VACÍA NO ES AUSENTE (patrón nº9), pero tampoco es «si»: es lo que deja un `set U_JEV_OVERLAY=` en un .bat.
        if (string.IsNullOrWhiteSpace(pedido)) return new ConfiguracionDeLaVista(false, $"{Variable} vacía");
        return pedido.Trim().ToLowerInvariant() switch
        {
            "si" => new ConfiguracionDeLaVista(true, $"{Variable}=si"),
            "no" => new ConfiguracionDeLaVista(false, $"{Variable}=no"),
            _ => new ConfiguracionDeLaVista(false, $"{Variable}=«{pedido.Trim()}» no se entiende (se esperaba si o no)"),
        };
    }
}
