namespace U.WindowsClient.Teach;

/// <summary>Lo que una demo cerrada entrega: las tres salidas, juntas o ninguna.</summary>
public sealed record EntregaDeDemo(string Video, IReadOnlyList<Navigation.PasoEnsenado> Pasos,
    Navigation.SkillEnsenada? Skill);

/// <summary>
/// EL CICLO DE VIDA de una demostración: grabando → cerrada O descartada. Promesa 104 (spec 005).
/// </summary>
/// <remarks>
/// «Me equivoqué» hoy sube el video igual: DiscardAsync existe con CERO llamadores y el botón está
/// Collapsed (medido el 2026-09-01). Esta clase es LA DECISIÓN, separada de los efectos, para que
/// el contrato pueda juzgarla sin subir nada a ninguna parte:
///
///   · DESCARTADA no entrega NADA — ni video, ni pasos, ni skill. Un descarte que publica es peor
///     que no poder descartar: el usuario cree que lo malo murió y lo malo viajó.
///   · CERRADA entrega las tres salidas juntas.
///   · Descartar DESPUÉS de cerrar no des-publica: lo entregado ya no es de la sesión. Sin esta
///     regla, un descarte tardío parecería borrar algo que ya está en Gemini/Supabase — la mentira
///     inversa.
///
/// Quien ejecuta los efectos (borrar el mp4, no llamar a ProcessAsync, no empaquetar) pregunta aquí
/// primero; el cableado a WorkflowTeachSession es de su fase.
/// </remarks>
public sealed class SesionDeDemo
{
    private enum Estado { Grabando, Cerrada, Descartada }
    private Estado _estado = Estado.Grabando;
    private EntregaDeDemo? _entrega;

    public string Video { get; set; } = "";
    public List<Navigation.PasoEnsenado> Pasos { get; } = new();
    public Navigation.SkillEnsenada? Skill { get; set; }

    /// <summary>La demo salió mal y se tira. Solo tiene efecto mientras se graba.</summary>
    public void Descartar()
    {
        if (_estado == Estado.Grabando) _estado = Estado.Descartada;
    }

    /// <summary>La demo terminó bien: lo grabado pasa a ser entregable.</summary>
    public void Cerrar()
    {
        if (_estado != Estado.Grabando) return;
        _estado = Estado.Cerrada;
        _entrega = new(Video, Pasos.ToList(), Skill);
    }

    /// <summary>Lo que esta sesión entrega, o null si no entrega nada (descartada o aún grabando).</summary>
    public EntregaDeDemo? Entrega() => _entrega;
}
