namespace U.Graph.Surfaces;

/// <summary>
/// Identidad de la pantalla donde se graba o ejecuta. Graph la modela como una URL porque nació del
/// DOM; aquí sintetizamos una equivalente para que un workflow de Windows sea indistinguible de uno
/// web del lado del backend.
///
///   SAP GUI → <c>sapgui://PRD/VA01</c>   (origin = sistema SAP, pathname = transacción)
///   UIA     → <c>uia://notepad.exe/Sin título</c>
/// </summary>
public sealed record SurfaceIdentity(string Origin, string Pathname, string Title)
{
    /// <summary>Lo que viaja como source_url / page_url.</summary>
    public string Url => $"{Origin.TrimEnd('/')}{Pathname}";

    public static readonly SurfaceIdentity Unknown = new("unknown://", "/", "");
}

/// <summary>Un paso que el usuario acaba de hacer, tal como lo vio la superficie.</summary>
public sealed record ObservedStep(
    string ActionType,
    string Selector,
    string Label,
    string ControlType,
    string? Value,
    IReadOnlyList<FieldOption>? AllowedOptions,
    string? SelectedValue,
    string? SelectedLabel,
    string? SurfaceSection,
    IReadOnlyList<string> AlternativeTargets)
{
    /// <summary>La superficie (URL de Windows) donde ocurrió el paso — su "nodo". Aditivo: la usa el
    /// player para reanudar en cualquier punto del workflow. Vacío = sin dato (comportamiento viejo).</summary>
    public string Surface { get; init; } = "";

    /// <summary>Meta de carga del nodo: nº de elementos interactivos listos al grabar (100%). La usa el
    /// motor de carga para esperar a que la UI cargue antes de ejecutar. Vacío/0 = sin métrica.</summary>
    public string Readiness { get; init; } = "";

    /// <summary>Posición del clic RELATIVA a la ventana ("relX,relY"): fallback cuando el elemento no tiene
    /// selector estable (paneles SAP con id volátil). Vacío para inputs/selects. Ver UiaSurface.Execute.</summary>
    public string ClickPos { get; init; } = "";
}

/// <summary>Por qué una superficie no está disponible. Se le enseña al operador tal cual.</summary>
public sealed record SurfaceAvailability(bool Available, string Reason)
{
    public static readonly SurfaceAvailability Ok = new(true, "");
    public static SurfaceAvailability No(string reason) => new(false, reason);
}

/// <summary>
/// Una superficie de UI que se puede leer, observar y ejecutar. Es la ÚNICA abstracción que separa
/// "hablar con Graph" de "tocar Windows": el grabador y el ejecutor trabajan contra esto, y da igual
/// si debajo hay SAP GUI Scripting o UIA.
///
/// Contrato de hilos: <see cref="ReadFields"/> y <see cref="Execute"/> pueden bloquear (UIA y el COM
/// de SAP lo hacen); llámalos FUERA del hilo de UI. <see cref="StepObserved"/> se dispara en un hilo
/// de la superficie, no en el de UI.
/// </summary>
public interface IUiSurface : IDisposable
{
    /// <summary>Identificador corto y estable: "sap" · "uia". Prefija los selectores que emite.</summary>
    string Name { get; }

    /// <summary>
    /// ¿Se puede usar aquí y ahora? En la máquina de un cliente esto falla de verdad: SAP GUI
    /// Scripting puede estar apagado por política de Basis, o no haber sesión abierta. Nunca se lanza
    /// excepción por eso — se devuelve el motivo para poder mostrarlo.
    /// </summary>
    SurfaceAvailability Check();

    /// <summary>Qué pantalla está delante ahora mismo.</summary>
    SurfaceIdentity Identity();

    /// <summary>
    /// "Cuánto está cargada" la pantalla: nº de elementos interactivos listos (visibles+habilitados). Es
    /// la métrica del motor de carga (<see cref="U.Graph.SurfaceReadiness"/>): al grabar se guarda como
    /// meta, al ejecutar se compara para esperar a que la UI cargue antes de actuar. 0 = sin métrica.
    /// </summary>
    int ReadinessCount();

    /// <summary>
    /// ¿El elemento objetivo del paso ya está LISTO para actuar (presente y habilitado)? Es el
    /// corto-circuito del motor de carga: en cuanto es true se ejecuta, sin esperar el % global (que es
    /// inestable). Los pasos sin elemento resoluble (tecla/scroll) devuelven true (no hay nada que esperar).
    /// </summary>
    bool IsStepReady(PlanStep step);

    /// <summary>
    /// Los campos accionables visibles, en el shape que Graph espera para autofill. El stepOrder es
    /// posicional dentro de ESTA lectura: es el identificador con el que Graph devuelve los matches.
    /// </summary>
    IReadOnlyList<DetectedField> ReadFields();

    /// <summary>
    /// Ejecuta un paso del plan. Devuelve false y el motivo en vez de lanzar: un workflow a medias en
    /// un SAP de producción tiene que reportar exactamente dónde se rompió.
    /// </summary>
    bool Execute(PlanStep step, out string error);

    /// <summary>Se dispara por cada acción del usuario mientras se graba.</summary>
    event EventHandler<ObservedStep>? StepObserved;

    /// <summary>Empieza a observar al usuario. Idempotente.</summary>
    void StartObserving();

    /// <summary>Deja de observar. Idempotente.</summary>
    void StopObserving();
}
