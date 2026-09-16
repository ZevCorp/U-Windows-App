namespace U.WindowsClient.Ui;

/// <summary>
/// QUÉ DICE EL NOTCH EN CADA MOMENTO: arriba la tarea, abajo lo que pasa ahora.
/// Promesa 252 (spec 028). Pura: aquí no se dibuja nada, solo se decide qué texto va en cada línea.
/// </summary>
/// <remarks>
/// HASTA AHORA EL NOTCH ERA UNA LISTA de frases del mismo peso, y servía para ver una secuencia. El
/// dueño pidió otra cosa el 2026-09-16: «que ahí en el título esté la MACRO TAREA, si le pedí crear
/// un anuncio, que esté "crear un anuncio" hasta que se complete o hasta que cambie la tarea; y
/// donde dice paso en ejecución, lo que el modelo va haciendo, y mi texto cuando yo hable».
///
/// LA TAREA ES, LITERALMENTE, LO QUE SE PIDIÓ, y por eso no hace falta preguntársela al modelo ni
/// inventar una herramienta para que la declare: basta con quedarse con lo último que dijo la
/// persona. Mientras habla, su frase va abajo —en vivo, que es donde se ve que la estás oyendo—, y
/// al cerrar el turno sube a ser la tarea. Un modelo que tuviera que declarar el título podría no
/// hacerlo, decirlo tarde o adornarlo; la transcripción no puede fallar en eso.
///
/// NO SE RESUME NI SE REESCRIBE la frase para el título. Si la persona lo dijo mal dicho, así se
/// lee: adivinar un título bonito es empezar a mentir sobre lo que se pidió.
/// </remarks>
public sealed class LoQueDiceElNotch
{
    /// <summary>Lo que dice cuando todavía no se ha pedido nada. Corto y honesto.</summary>
    public const string SinTareaTodavia = "Ü";

    /// <summary>La línea de arriba: la tarea. Se queda hasta que se pida otra.</summary>
    public string Tarea { get; private set; } = SinTareaTodavia;

    /// <summary>La línea de abajo: lo que está pasando ahora mismo.</summary>
    public string Paso { get; private set; } = "";

    /// <summary>Qué icono toca, según lo que sea esa línea de abajo.</summary>
    public EstadoDelNotch Estado { get; private set; } = EstadoDelNotch.Voz;

    /// <summary>Lo que la persona lleva dicho en este turno, esperando a subir a tarea.</summary>
    private string _enBoca = "";

    /// <summary>La persona está hablando: va abajo, en vivo, y queda en boca hasta que cierre el turno.</summary>
    public void PersonaDice(string texto)
    {
        string t = (texto ?? "").Trim();
        if (t.Length == 0) return;
        _enBoca = t;
        Paso = t;
        Estado = EstadoDelNotch.Voz;
    }

    /// <summary>
    /// Se acabó el turno: lo que la persona dijo SUBE a ser la tarea, y la línea de abajo queda libre
    /// para lo que Ü empiece a hacer.
    /// </summary>
    public void CierraTurno()
    {
        if (_enBoca.Length == 0) return;   // una transcripción vacía no cambia la tarea
        Tarea = _enBoca;
        _enBoca = "";
        Paso = "";
    }

    /// <summary>Ü dice algo: es lo que pasa ahora, y no toca la tarea.</summary>
    public void UDice(string texto)
    {
        string t = (texto ?? "").Trim();
        if (t.Length == 0) return;
        Paso = t;
        Estado = EstadoDelNotch.Voz;
    }

    /// <summary>Ü empieza un paso.</summary>
    public void Empieza(string texto)
    {
        Paso = (texto ?? "").Trim();
        Estado = EstadoDelNotch.EnCurso;
    }

    /// <summary>El paso terminó, bien o mal.</summary>
    public void Termina(string texto, bool ok)
    {
        string t = (texto ?? "").Trim();
        if (t.Length > 0) Paso = t;
        Estado = ok ? EstadoDelNotch.Hecho : EstadoDelNotch.Fallo;
    }

    /// <summary>Se acabó todo: vuelve a como estaba al abrirse.</summary>
    public void Olvida()
    {
        Tarea = SinTareaTodavia;
        Paso = "";
        _enBoca = "";
        Estado = EstadoDelNotch.Voz;
    }
}
