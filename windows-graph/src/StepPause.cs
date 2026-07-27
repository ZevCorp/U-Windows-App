namespace U.Graph;

/// <summary>Qué hacer con el paso en el que el operador está parado.</summary>
public enum StepDecision
{
    /// <summary>Ejecutarlo y volver a parar en el siguiente.</summary>
    Ejecutar,

    /// <summary>Ejecutarlo y seguir hasta el final sin más pausas.</summary>
    HastaElFinal,

    /// <summary>No ejecutarlo: pasar al siguiente. Para saltar un paso que sobra de la grabación.</summary>
    Saltar,

    /// <summary>Abortar la corrida aquí.</summary>
    Parar,
}

/// <summary>
/// La foto del momento justo ANTES de ejecutar un paso: qué se va a hacer, dónde estamos, si el
/// elemento responde, y cómo se veía la pantalla cuando el operador lo enseñó.
///
/// Es lo que convierte el paso a paso en un depurador y no en un botón de «siguiente»: sin el contraste
/// entre lo grabado y lo actual, pausar solo hace la ejecución más lenta.
/// </summary>
public sealed record StepPause(
    int Index,
    int Total,
    int StepOrder,
    string Label,
    string ActionType,
    string Selector,
    string Value,
    string ExpectedSurface,
    string CurrentSurface,
    bool ElementReady,
    string PreviousOutcome,
    string TaughtShotPath,
    string ExpectedFingerprint = "",
    string CurrentFingerprint = "")
{
    /// <summary>
    /// ¿La pantalla está en el mismo ESTADO que al enseñar, no solo en la misma transacción? Sin huella
    /// grabada (workflows viejos) se responde que sí: no se puede afirmar lo contrario.
    /// </summary>
    public bool SameStructure =>
        ExpectedFingerprint.Length == 0 || CurrentFingerprint.Length == 0
        || ExpectedFingerprint == CurrentFingerprint;

    /// <summary>¿La pantalla de ahora es la que se grabó para este paso? Vacío en grabaciones viejas.</summary>
    public bool AtExpectedSurface =>
        ExpectedSurface.Length == 0 || SurfacePlace.Same(CurrentSurface, ExpectedSurface);

    /// <summary>Resumen de una línea para el título de la pausa.</summary>
    public string Headline => $"paso {StepOrder} · {ActionType} «{Label}»";

    /// <summary>
    /// El veredicto en corto. Se calcula aquí y no en la UI para que el paso a paso y el registro digan
    /// exactamente lo mismo — dos varas distintas sobre el mismo paso ya nos costó una jornada.
    /// </summary>
    public string Verdict =>
        !AtExpectedSurface ? "✋ NO estás en la pantalla de este paso"
        : !SameStructure ? "⚠ misma transacción, pero la pantalla NO está como cuando se enseñó"
        : ElementReady ? "✓ el elemento resuelve y está habilitado"
        : "⚠ estás en la pantalla correcta pero el elemento no responde todavía";
}
