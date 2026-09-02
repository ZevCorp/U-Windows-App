using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using U.Graph;
using U.Graph.Surfaces;
using U.WindowsClient.Backend;
using U.WindowsClient.Diagnostics;
using U.WindowsClient.Teach;
using U.WindowsClient.Workflows;

namespace U.WindowsClient.Ui;

/// <summary>
/// Enseñar/ejecutar workflows contra Graph (windows-graph) + autofill en vivo por nota. Es la única
/// ventana que sabe de <c>U.Graph</c>: FaceWindow solo la abre, no conoce nada de lo que hay dentro.
/// </summary>
public partial class WorkflowLibraryWindow : Window
{
    private readonly GraphConfig _graphConfig;
    private readonly BackendClient _backend;
    private readonly VideoLibrary _videoLibrary;
    private readonly string _userId;

    private GraphClient _graphClient;
    private readonly UiaSurface _uia = new();
    private readonly SapGuiSurface _sap = new();
    private WorkflowPlayer _player;

    private WorkflowTeachSession? _teachSession;
    private bool _teaching;
    private CancellationTokenSource? _runCts;

    private readonly DispatcherTimer _noteDebounce;
    private readonly List<FulfilledField> _fulfilled = new();
    private readonly string _autofillSessionId = Guid.NewGuid().ToString();
    private readonly List<AutofillRow> _rows = new();

    public WorkflowLibraryWindow(
        GraphConfig graphConfig, BackendClient backend, VideoLibrary videoLibrary, string userId)
    {
        InitializeComponent();
        _graphConfig = graphConfig;
        _backend = backend;
        _videoLibrary = videoLibrary;
        _userId = userId;

        _graphClient = new GraphClient(_graphConfig);
        // TERCERA superficie SAP del cliente, y la que usa el operador de verdad para probar: los
        // workflows se lanzan desde esta ventana. Sin este cable, un fallo diagnosticable desde el botón
        // de la carita era mudo desde aquí — que es donde se corre. Si aparece un cuarto sitio que
        // construya SapGuiSurface para ejecutar, tiene que llevar este mismo cable.
        _sap.Diagnostic += (_, msg) => LogBus.Log("sap", msg);

        _player = new WorkflowPlayer(_graphClient, _graphConfig, _uia, _sap)
        {
            Aligner = U.WindowsClient.Uia.AppAligner.EnsureAsync,
            Log = s => LogBus.Log("workflow", s)
        };
        _player.StepDone += (_, outcome) => Dispatcher.Invoke(() => AppendProgress(outcome));

        _noteDebounce = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(700) };
        _noteDebounce.Tick += async (_, _) => { _noteDebounce.Stop(); await RunAutofillMatchAsync(); };

        Loaded += (_, _) =>
        {
            // Este es el SEGUNDO indicador de conexión de la aplicación, y mentía igual que el de la
            // carita: decía «conectado» por tener una API key, sin haber hablado con nadie. Los dos
            // pasan ahora por el mismo formateador para que no puedan contradecirse.
            ShowConnStatus();
            GraphHealth.Changed += OnGraphHealthChanged;
            _ = ReloadWorkflowsAsync();
        };
        // Evento estático: sin esto, cada apertura de la biblioteca deja una ventana viva.
        Closed += (_, _) => GraphHealth.Changed -= OnGraphHealthChanged;
    }

    private void OnGraphHealthChanged(object? sender, GraphObservation obs) =>
        Dispatcher.BeginInvoke(new Action(ShowConnStatus));

    private void ShowConnStatus()
    {
        var (_, text) = GraphHealthText.Describe(
            _graphConfig.IsConfigured
                ? GraphHealth.CurrentFor(_graphConfig.BaseUrl)
                : GraphHealth.CurrentFor(_graphConfig.BaseUrl) with { Link = GraphLink.SinKey });
        ConnStatus.Text = text;
    }

    // ── Conexión ─────────────────────────────────────────────────────────────

    // ── Enseñar ──────────────────────────────────────────────────────────────

    private async void OnToggleTeach(object sender, RoutedEventArgs e)
    {
        if (_teaching) await StopTeachingAsync();
        else await StartTeachingAsync();
    }

    private async Task StartTeachingAsync()
    {
        // El título ya NO es obligatorio: si va vacío, se autogenera al final desde lo aprendido (Graph).
        string description = TeachDescription.Text.Trim();

        _teachSession = new WorkflowTeachSession(
            _graphClient, _graphConfig, _uia, _sap, _backend, _videoLibrary, _userId);
        _teachSession.StatusChanged += (_, msg) => Dispatcher.Invoke(() => { StatusLine.Text = msg; AppendProgressLine(msg); });

        TeachBtn.IsEnabled = false;
        try
        {
            await _teachSession.StartAsync(description, CancellationToken.None);
            SetTeachingUi(true);
        }
        catch (Exception ex)
        {
            LogBus.Log("workflow-ui", $"no se pudo iniciar la enseñanza de workflow: {ex}");
            StatusLine.Text = $"No se pudo iniciar: {ex.Message}";
            await _teachSession.DisposeAsync();
            _teachSession = null;
        }
        finally { TeachBtn.IsEnabled = true; }
    }

    private async Task StopTeachingAsync()
    {
        SetTeachingUi(false);
        if (_teachSession == null) return;

        StatusLine.Text = "Deteniendo y estructurando el workflow…";
        try
        {
            FinishResponse finish = await _teachSession.StopAsync(CancellationToken.None);
            StatusLine.Text = string.IsNullOrWhiteSpace(finish.Error)
                ? $"Workflow guardado: {finish.Summary}"
                : $"Graph reportó un error al cerrar: {finish.Error}";
            AppendProgressLine(StatusLine.Text);
            TeachDescription.Text = "";
            await ReloadWorkflowsAsync();
        }
        catch (Exception ex)
        {
            LogBus.Log("workflow-ui", $"error al detener la enseñanza de workflow: {ex}");
            StatusLine.Text = $"Error al detener: {ex.Message}";
        }
        finally
        {
            await _teachSession.DisposeAsync();
            _teachSession = null;
        }
    }

    private void SetTeachingUi(bool teaching)
    {
        _teaching = teaching;
        TeachBtn.Content = teaching ? "⏸ Detener enseñanza" : "● Enseñar workflow";
        TeachBtn.Background = new System.Windows.Media.SolidColorBrush(teaching
            ? System.Windows.Media.Color.FromRgb(255, 59, 48)
            : System.Windows.Media.Color.FromRgb(34, 34, 34));
        TeachDescription.IsEnabled = !teaching;
    }

    // ── Lista + ejecución ────────────────────────────────────────────────────

    private async void OnReload(object sender, RoutedEventArgs e) => await ReloadWorkflowsAsync();

    private async Task ReloadWorkflowsAsync()
    {
        ReloadBtn.IsEnabled = false;
        try
        {
            var raw = await _graphClient.ListWorkflowsAsync(CancellationToken.None);
            if (raw.Count > 0)
                LogBus.Log("workflow-ui", $"primer workflow crudo (para ajustar el parseo si hace falta): {raw[0]}");

            // Mismo nombre y mismo orden que el carrusel de la carita (spec 007): dos listas del
            // mismo catálogo con nombres distintos serían dos catálogos.
            var nombres = new NombresDeWorkflows();
            var lista = SelectorDeWorkflows.Ordenar(raw.Select(WorkflowSummary.FromJson));
            foreach (var wf in lista) wf.NombrePropio = nombres.De(wf.Id);
            WorkflowList.ItemsSource = lista;
            StatusLine.Text = $"{raw.Count} workflow(s) cargado(s).";
        }
        catch (Exception ex)
        {
            LogBus.Log("workflow-ui", $"ListWorkflowsAsync falló: {ex}");
            StatusLine.Text = $"No se pudo listar workflows: {ex.Message}";
        }
        finally { ReloadBtn.IsEnabled = true; }
    }

    private async void OnRunSelected(object sender, RoutedEventArgs e)
    {
        if (WorkflowList.SelectedItem is not WorkflowSummary wf)
        {
            StatusLine.Text = "Selecciona un workflow de la lista primero.";
            return;
        }

        ProgressLog.Text = "";
        RunBtn.IsEnabled = false;
        StatusLine.Text = $"Ejecutando «{wf.Nombre}»…";
        _runCts = new CancellationTokenSource();
        try
        {
            RunResult result = await _player.RunAsync(wf.Id, null, ForceSurface.IsChecked != true, _runCts.Token);
            StatusLine.Text = result.Ok
                ? (result.AlignedConsciously
                    ? $"«{wf.Nombre}» terminó bien (me alineé abriendo la app): {result.Tally}."
                    : $"«{wf.Nombre}» terminó bien: {result.Tally}.")
                : $"«{wf.Nombre}» se detuvo: {result.Error}";
            if (result.Ok && result.AlignedConsciously)
                _ = _graphClient.PrependAlignmentStepAsync(wf.Id, _runCts.Token); // aprende a alcanzar su superficie
        }
        catch (Exception ex)
        {
            LogBus.Log("workflow-ui", $"RunAsync falló: {ex}");
            StatusLine.Text = $"Error ejecutando: {ex.Message}";
        }
        finally { RunBtn.IsEnabled = true; _runCts = null; }
    }

    private void AppendProgress(StepOutcome o) =>
        AppendProgressLine($"{(o.Ok ? "✅" : "❌")} paso {o.StepOrder} · {o.ActionType} «{o.Label}»" +
            (o.Ok ? "" : $" — {o.Error}"));

    private void AppendProgressLine(string line) => ProgressLog.Text += line + "\n";

    // ── Autofill en vivo ─────────────────────────────────────────────────────

    private void OnNoteChanged(object sender, TextChangedEventArgs e)
    {
        _noteDebounce.Stop();
        _noteDebounce.Start();
    }

    private async Task RunAutofillMatchAsync()
    {
        string note = NoteBox.Text;
        if (string.IsNullOrWhiteSpace(note)) return;

        IUiSurface surface = SurfaceDetector.Detect(_uia, _sap);
        AutofillStatus.Text = $"Leyendo campos de «{surface.Name}»…";
        List<DetectedField> fields;
        try { fields = await Task.Run(() => surface.ReadFields().ToList()); }
        catch (Exception ex) { AutofillStatus.Text = $"No se pudieron leer los campos: {ex.Message}"; return; }

        if (fields.Count == 0) { AutofillStatus.Text = "No se detectaron campos en la pantalla actual."; return; }

        try
        {
            var result = await _graphClient.MatchAsync(new AutofillRequest
            {
                SessionId = _autofillSessionId,
                PageUrl = surface.Identity().Url,
                NoteContent = note,
                Fields = fields,
                AlreadyFulfilled = _fulfilled,
            }, CancellationToken.None);

            _rows.Clear();
            foreach (var m in result.Matches)
            {
                var field = fields.FirstOrDefault(f => f.StepOrder == m.StepOrder);
                if (field != null) _rows.Add(new AutofillRow(field, m));
            }
            MatchesList.ItemsSource = null;
            MatchesList.ItemsSource = _rows;
            AutofillStatus.Text = $"{_rows.Count} campo(s) propuesto(s) — nunca se aplican solos.";
        }
        catch (Exception ex)
        {
            LogBus.Log("workflow-ui", $"MatchAsync falló: {ex}");
            AutofillStatus.Text = $"No se pudo pedir el match a Graph: {ex.Message}";
        }
    }

    private async void OnApplyMatch(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not AutofillRow row) return;

        IUiSurface surface = SurfaceDetector.Detect(_uia, _sap);
        var step = PlanStep.ForAutofill(row.Field, row.Match);

        bool ok = false; string err = "";
        await Task.Run(() => ok = surface.Execute(step, out err));

        if (ok)
        {
            _fulfilled.Add(new FulfilledField { StepOrder = row.Match.StepOrder, Value = row.Match.Value });
            AutofillStatus.Text = $"Aplicado: {row.Label}";
        }
        else
        {
            AutofillStatus.Text = $"No se pudo aplicar «{row.Label}»: {err}";
        }

        RemoveRow(row);
    }

    private void OnRejectMatch(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is AutofillRow row) RemoveRow(row);
    }

    private void RemoveRow(AutofillRow row)
    {
        _rows.Remove(row);
        MatchesList.ItemsSource = null;
        MatchesList.ItemsSource = _rows;
    }

    protected override void OnClosed(EventArgs e)
    {
        _noteDebounce.Stop();
        _runCts?.Cancel();
        if (_teaching) _ = StopTeachingAsync();
        base.OnClosed(e);
    }
}

/// <summary>
/// Fila liviana para el panel de autofill: el campo detectado (de dónde tocar) + el match propuesto
/// (qué poner). Se reconstruye en un PlanStep solo cuando el operador aprieta "Aplicar".
/// </summary>
public sealed class AutofillRow
{
    public DetectedField Field { get; }
    public FieldMatch Match { get; }

    public AutofillRow(DetectedField field, FieldMatch match) { Field = field; Match = match; }

    public string Label => Field.Label;
    public string Value => Match.Value;
    public string ConfidenceLabel => $"confianza {Match.Confidence:P0}" +
        (string.IsNullOrWhiteSpace(Match.Evidence) ? "" : $" — \"{Match.Evidence}\"");
}
