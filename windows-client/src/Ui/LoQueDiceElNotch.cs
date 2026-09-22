namespace U.WindowsClient.Ui;

/// <summary>
/// QUÉ DICE EL NOTCH EN CADA MOMENTO. La memoria conserva tarea y actividad por separado, pero la
/// superficie enseña una sola frase con el peso visual principal.
/// Promesa 252 (spec 028, refinada). Pura: aquí no se dibuja nada, solo se decide qué texto visible toca.
/// </summary>
/// <remarks>Mientras la persona habla, su frase ocupa la actividad visible; al cerrar el turno pasa a tarea.</remarks>
public sealed class LoQueDiceElNotch
{
    /// <summary>Lo que dice cuando todavía no se ha pedido nada. Corto y honesto.</summary>
    public const string SinTareaTodavia = "Ü";

    /// <summary>La tarea persistente. Se queda hasta que se pida otra.</summary>
    public string Tarea { get; private set; } = SinTareaTodavia;

    /// <summary>La actividad efímera: lo que está pasando ahora mismo.</summary>
    public string Paso { get; private set; } = "";

    /// <summary>
    /// El único texto que se pinta en el notch. Mientras hay algo pasando gana la actividad; cuando
    /// queda libre, la pieza conserva la última tarea como ancla. La memoria de las dos fuentes no se
    /// mezcla: solo se simplifica la salida visual.
    /// </summary>
    public string Texto => Paso.Length > 0 ? Paso : Tarea;

    /// <summary>Qué icono toca, según el estado de la actividad visible.</summary>
    public EstadoDelNotch Estado { get; private set; } = EstadoDelNotch.Voz;

    /// <summary>Lo que la persona lleva dicho en este turno, esperando a subir a tarea.</summary>
    private string _enBoca = "";
    private bool _pasoEsDeLaPersona;

    /// <summary>La persona está hablando: la frase viva queda en memoria hasta que cierre el turno.</summary>
    public void PersonaDice(string texto)
    {
        string t = (texto ?? "").Trim();
        if (t.Length == 0) return;
        _enBoca = t;
        Paso = t;
        _pasoEsDeLaPersona = true;
        Estado = EstadoDelNotch.Voz;
    }

    /// <summary>
    /// Se acabó el turno: lo que la persona dijo pasa a ser la tarea y la actividad queda libre para
    /// lo que Ü empiece a hacer.
    /// </summary>
    public void CierraTurno()
    {
        if (_enBoca.Length == 0) return;   // una transcripción vacía no cambia la tarea
        Tarea = _enBoca;
        _enBoca = "";
        if (_pasoEsDeLaPersona) Paso = "";
        _pasoEsDeLaPersona = false;
    }

    /// <summary>Ü dice algo: es lo que pasa ahora, y no toca la tarea.</summary>
    public void UDice(string texto)
    {
        string t = (texto ?? "").Trim();
        if (t.Length == 0) return;
        Paso = t;
        _pasoEsDeLaPersona = false;
        Estado = EstadoDelNotch.Voz;
    }

    /// <summary>Ü empieza un paso.</summary>
    public void Empieza(string texto)
    {
        Paso = (texto ?? "").Trim();
        _pasoEsDeLaPersona = false;
        Estado = EstadoDelNotch.EnCurso;
    }

    /// <summary>El paso terminó, bien o mal.</summary>
    public void Termina(string texto, bool ok)
    {
        string t = (texto ?? "").Trim();
        if (t.Length > 0) Paso = t;
        _pasoEsDeLaPersona = false;
        Estado = ok ? EstadoDelNotch.Hecho : EstadoDelNotch.Fallo;
    }

    /// <summary>
    /// La persona lo paró a mano. Promesa 259 (spec 028, ampliada 2026-09-17). Ni se hizo ni falló:
    /// se quedó sin desenlace, que es justo lo que ya significaba <see cref="EstadoDelNotch.Omitido"/>
    /// y que hasta ahora nadie disparaba —el estado y su icono llevaban ahí desde la spec 028,
    /// escritos para este caso y sin ninguna llamada que los usara—.
    /// </summary>
    public void Detenido(string texto)
    {
        string t = (texto ?? "").Trim();
        Paso = t.Length > 0 ? t : "detenido";
        _pasoEsDeLaPersona = false;
        Estado = EstadoDelNotch.Omitido;
    }

    /// <summary>Se acabó todo: vuelve a como estaba al abrirse.</summary>
    public void Olvida()
    {
        Tarea = SinTareaTodavia;
        Paso = "";
        _enBoca = "";
        _pasoEsDeLaPersona = false;
        Estado = EstadoDelNotch.Voz;
    }
}
