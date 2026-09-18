using System;
using System.Threading.Tasks;

namespace U.WindowsClient.Decision;

/// <summary>
/// EL INTERRUPTOR EN VIVO: enciende y apaga el decisor sin reiniciar. Promesa 290 (spec 036).
/// </summary>
/// <remarks>
/// LA VARIABLE FIJA EL ESTADO INICIAL; EL BOTÓN MANDA DESPUÉS. Hasta la spec 035 el decisor se decidía
/// una vez, al arrancar, leyendo <c>U_DECISOR</c>; el dueño pidió (2026-09-18) «un botón en el panel que
/// haga switch entre con TypeSafe y sin TypeSafe». Esto es lo que hace el botón por dentro, separado de
/// la ventana para que el contrato lo pueda juzgar: cambiar el decisor del mapa, cambiar el catálogo de
/// la voz, y volver a mandárselo a la sesión — lo mismo que ya hacen Learn/Work con
/// <c>CambiarModoAsync</c>.
///
/// APAGAR DEJA EL CATÁLOGO BYTE A BYTE COMO SIN DECISOR. No es un «desactivado» a medias: la herramienta
/// desaparece del catálogo y el mapa se queda sin decisor, que es exactamente el estado de una máquina
/// sin <c>U_DECISOR</c>. Un botón que apaga «casi» es la caja que miente.
///
/// EL ESTADO ES UNA LÍNEA Y SE LEE: el botón la enseña. Un verde sin haberlo medido es peor que no tener
/// botón (plan, §2).
/// </remarks>
public sealed class InterruptorDelDecisor
{
    private readonly Mcp.SurfaceMapTools _mapa;
    private readonly Func<Task> _reenviarCatalogo;
    private readonly Action<string> _log;

    /// <param name="mapa">El mapa cuyo <see cref="Mcp.SurfaceMapTools.Decisor"/> se enchufa y desenchufa.</param>
    /// <param name="reenviarCatalogo">Vuelve a mandar instrucciones y catálogo a la sesión de voz. Si no hay sesión, no hace nada.</param>
    /// <param name="log">Dónde contar lo que pasó.</param>
    public InterruptorDelDecisor(Mcp.SurfaceMapTools mapa, Func<Task> reenviarCatalogo, Action<string> log)
    {
        _mapa = mapa ?? throw new ArgumentNullException(nameof(mapa));
        _reenviarCatalogo = reenviarCatalogo ?? (() => Task.CompletedTask);
        _log = log ?? (_ => { });
    }

    /// <summary>Si el decisor está enchufado ahora.</summary>
    public bool Encendido { get; private set; }

    /// <summary>Qué hay, en una línea: para el botón y para el log.</summary>
    public string Estado { get; private set; } = "apagado: decide Luna.";

    /// <summary>
    /// Enciende según lo que diga el entorno. Devuelve si quedó encendido; si no, <see cref="Estado"/> dice por qué.
    /// </summary>
    /// <remarks>
    /// PEDIR JEV SIN CLAVE SE QUEDA APAGADO. <see cref="ConfiguracionDelDecisor.Leer"/> ya cae a Luna en ese
    /// caso y lo dice con el nombre de la variable; aquí no se llama a TypeSafe sin credencial, igual que
    /// al arrancar (promesa 277).
    /// </remarks>
    public bool Encender(Func<string, string?> entorno)
    {
        if (entorno == null) throw new ArgumentNullException(nameof(entorno));
        var cfg = ConfiguracionDelDecisor.Leer(entorno);
        if (cfg.Quien == "luna")
        {
            Apagar(porque: "apagado: " + UnaLinea(cfg.Porque));
            return false;
        }

        var transporte = ClienteTypeSafe.TransporteSegun(cfg, entorno, _log)
            ?? (_ => throw new InvalidOperationException("no hay transporte con el que hablarle a TypeSafe"));
        _mapa.Decisor = (pantalla, objetivo, puertas) =>
            ElDecisor.Elegir(cfg.Quien, pantalla, objetivo, puertas, cfg.Confianza, transporte);
        Voice.ConversacionEnVivo.ConDecisor = true;
        Encendido = true;
        Estado = $"encendido: {cfg.Quien} ({cfg.Modelo}), umbral {cfg.Confianza.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}.";
        _log($"interruptor → {Estado}");
        Reenviar();
        return true;
    }

    /// <summary>Apaga: el mapa sin decisor y el catálogo de la voz como sin decisor.</summary>
    public void Apagar() => Apagar("apagado: decide Luna.");

    private void Apagar(string porque)
    {
        _mapa.Decisor = null;
        Voice.ConversacionEnVivo.ConDecisor = false;
        Encendido = false;
        Estado = porque;
        _log($"interruptor → {Estado}");
        Reenviar();
    }

    private void Reenviar()
    {
        try
        {
            var t = _reenviarCatalogo();
            // No se espera aquí: el botón no puede bloquear la interfaz mientras la sesión contesta. Pero un
            // fallo no se traga: queda en el log (patrón nº3).
            t?.ContinueWith(x => _log($"no pude re-mandar el catálogo a la voz: {x.Exception?.GetBaseException().Message}"),
                TaskContinuationOptions.OnlyOnFaulted);
        }
        catch (Exception e) { _log($"no pude re-mandar el catálogo a la voz: {e.GetType().Name}: {e.Message}"); }
    }

    private static string UnaLinea(string s) => (s ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
}
