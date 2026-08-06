# Graph Report - C:\Users\Jose David Jaramillo\Documents\U-windows  (2026-08-05)

## Corpus Check
- cluster-only mode — file stats not available

## Summary
- 2439 nodes · 5493 edges · 123 communities (111 shown, 12 thin omitted)
- Extraction: 96% EXTRACTED · 4% INFERRED · 0% AMBIGUOUS · INFERRED: 240 edges (avg confidence: 0.8)
- Token cost: 80,326 input · 1,709 output

## Community Hubs (Navigation)
- UI Graph Crawler
- Audio & WebSocket Capture
- Face Window UI Layout
- Audio & Screen Recording
- Mouse Input Hooks
- Keyboard Gesture Shortcuts
- Face Control Widget
- Surface Map Model
- Windows Client Namespaces
- Surface Map Utilities
- Face Animation Rendering
- Surface Location Detection
- Face Window Event Handlers
- UI Element Tree Walking
- SAP GUI vs UIA Research
- UI Field Selectors
- Graph Explorer Window
- Surface Alignment Matching
- Inspector Overlay Drawing
- Graph Backend HTTP Client
- SAP GUI Surface Reader
- Navigation Strategies
- SAP COM Event Binding
- UIA Surface Provider
- Graph Node Visualization
- Workflow Library Window
- Win32 Window Interop
- OpenAI Provider Adapter
- Workflow Step Recorder
- Agent Execution Loop
- Teach API Handlers
- Low-Level Input Executor
- Clinical Data Bridge
- Autofill API Contracts
- Workflow Library UI Bindings
- SAP Tree & Grid Reading
- SAP Step Selector
- Client/Brain Architecture Docs
- Node Package Manifest
- Turn Engine & MCP Catalog
- TypeScript Compiler Config
- Workflow Teach Session
- Plan Step Execution
- Face Backend Menu Panel
- Graph Navigation Concepts
- Surface Readiness Waiting
- Tree Row Geometry Cache
- App Window Focus Aligner
- UI Inspector Hooks
- Windows Repo Guide Docs
- Graph Health Status
- Global Hotkeys
- Gemini Provider Adapter
- Onboarding Window
- Clinical Concept Binding
- Velopack Release Pipeline
- UI Graph Crawl Drawing
- UIA Condition Selectors
- UIA Event Publishing
- Turn Endpoint & Session
- Telemetry Client
- Step Screenshot Capture
- Desktop Window Manager
- Inspector Diagnostics
- SAP Field Hit Testing
- Backend Module Layout
- Installed Apps Launcher
- Backend HTTP Client
- Highlight Overlay
- Win32 Window Enumeration
- In-Memory Learning Store
- SAP Scripting Event Notes
- App Startup & Config
- Menu & Dialog Reading
- SAP/UIA Hit Diagnostics
- SAP Element Description
- Step Debugger Pause UI
- Velopack Auto-Updater
- Health & Video Config
- SAP Method Learnings
- Project Dependencies
- Voice Input & Speech
- Log Window
- Locator Badge Overlay
- Workflow Dry Run
- Log Bus
- Surface Place Matching
- UIA Threading Architecture
- Foreground Surface Detector
- Face Mood Animation
- Fill Preview Dialog
- Surface Navigator
- Screen Pointer Overlay
- Agent Computer-Use Bridge
- Pending Finish Queue
- Workflow List Menu
- SAP Visual Elements
- Teach Session Contracts
- Own Window Detection
- Graphify Decision Graph Design
- UI Tree Node Selection
- Vercel Function Config
- Windows Screen Capture
- Client Telemetry Events
- JSON Workflow Parsing
- Run Timing Metrics
- Local Dev Server
- Agent Prompt Building
- Throw Easing Animation
- Agent Turn Protocol
- Graph Client Exceptions
- User Path Resolution
- UI Element Fingerprints
- Clinical Values API Bridge
- Win32 Input Structs
- Relative Click Coordinates
- Window Cursor Detection
- UI Surface Readiness Gate
- Step-by-Step Screenshots

## God Nodes (most connected - your core abstractions)
1. `FaceWindow` - 149 edges
2. `UiaSurface` - 116 edges
3. `SapGuiSurface` - 111 edges
4. `SurfaceMapTools` - 72 edges
5. `Window` - 63 edges
6. `GraphExplorerWindow` - 60 edges
7. `SurfaceMap` - 45 edges
8. `GraphCrawler` - 41 edges
9. `SurfaceLocator` - 35 edges
10. `WorkflowLibraryWindow` - 34 edges

## Surprising Connections (you probably didn't know these)
- `windows-graph — SAP GUI Scripting: hechos duros` --semantically_similar_to--> `Aceptado no es ejecutado`  [INFERRED] [semantically similar]
  windows-graph/CLAUDE.md → docs/graphify.md
- `CLIENT_TOKEN — candado del endpoint` --references--> `BackendClient`  [INFERRED]
  PRODUCTION.md → windows-client/README.md
- `IsStepReady (compuerta al primer plano)` --calls--> `UiaSurface`  [AMBIGUOUS]
  windows-client/CLAUDE.md → docs/graphify.md
- `Fase 4 · La tarea aprendida (workflows)` --conceptually_related_to--> `windows-graph (workflows sobre SAP GUI)`  [INFERRED]
  docs/plan-producto.md → CLAUDE.md
- `Vacío no es ausente (?? y cadena vacía)` --references--> `Graph — el cerebro remoto`  [EXTRACTED]
  windows-graph/CLAUDE.md → CLAUDE.md

## Import Cycles
- None detected.

## Hyperedges (group relationships)
- **Bucle de un turno: cliente tonto ↔ cerebro remoto** — windows_client_readme_uia_reader, windows_client_readme_screenshotter, windows_client_readme_backend_client, backend_readme_turn, backend_architecture_resolve_turn, windows_actions_ts, windows_client_readme_input_executor [EXTRACTED 0.90]
- **Pipeline de publicación y auto-update de U.exe** — production_publish_release_ps1, github_workflows_windows_release, releasing_windows_velopack, releasing_windows_releases_json, releasing_windows_bucket, releasing_windows_updater_cs, releasing_windows_u_setup [EXTRACTED 0.90]
- **Flujo de grabación sobre SAP GUI Scripting** — windows_graph_investigacion_sapgui_uia_record_property, windows_graph_investigacion_sapgui_uia_change_event, windows_graph_claude_sap_com_events, windows_graph_claude_start_request, windows_graph_claude_end_request, windows_graph_claude_session_busy [EXTRACTED 0.85]
- **Flujo híbrido de captura de texto en UIA** — windows_graph_investigacion_sapgui_uia_uia, windows_graph_investigacion_sapgui_uia_captura_de_texto, windows_graph_investigacion_sapgui_uia_event_object_valuechange, windows_graph_investigacion_sapgui_uia_raw_input, windows_graph_investigacion_sapgui_uia_winevents, windows_graph_investigacion_sapgui_uia_enfoque_hibrido [EXTRACTED 0.90]
- **Matriz comparativa SAP GUI Scripting vs UIA** — windows_graph_investigacion_sapgui_uia_sap_gui_scripting, windows_graph_investigacion_sapgui_uia_uia, windows_graph_investigacion_sapgui_uia_identificacion_de_elementos, windows_graph_investigacion_sapgui_uia_granularidad_de_grabacion, windows_graph_investigacion_sapgui_uia_captura_de_texto, windows_graph_investigacion_sapgui_uia_entries_combo, windows_graph_investigacion_sapgui_uia_user_scripting_disable_recording, windows_graph_investigacion_sapgui_uia_fiabilidad_de_eventos [EXTRACTED 0.95]
- **Wrappers .NET para UIA y SAP GUI Scripting** — windows_graph_investigacion_sapgui_uia_flaui, windows_graph_investigacion_sapgui_uia_uiacomwrapper, windows_graph_investigacion_sapgui_uia_sapgui_wrapper, windows_graph_investigacion_sapgui_uia_sap_gui_scripting_net [INFERRED 0.75]

## Communities (123 total, 12 thin omitted)

### Community 0 - "UI Graph Crawler"
Cohesion: 0.06
Nodes (34): Alternativas, Entrable, Frente, Queue, Tipo, CancellationToken, DllImport, EnumProc (+26 more)

### Community 1 - "Audio & WebSocket Capture"
Cohesion: 0.05
Nodes (34): BufferedWaveProvider, ClientWebSocket, CURSORINFO, Graphics, ICONINFO, KeyEventArgs, SemaphoreSlim, WaveInEvent (+26 more)

### Community 2 - "Face Window UI Layout"
Cohesion: 0.05
Nodes (66): ActivatorChevron, ActivatorRot, BackendChevron, BackendDot, BackendHeader, BackendRot, BackendStatus, BarRow (+58 more)

### Community 3 - "Audio & Screen Recording"
Cohesion: 0.06
Nodes (31): AudioDevice, IAsyncDisposable, Recorder, CancellationToken, IReadOnlyList, string, Task, ValueTask (+23 more)

### Community 4 - "Mouse Input Hooks"
Cohesion: 0.06
Nodes (35): Click, Duration, MouseButtonEventArgs, MouseEventArgs, RECT, DllImport, HashSet, HookProc (+27 more)

### Community 5 - "Keyboard Gesture Shortcuts"
Cohesion: 0.07
Nodes (18): HttpListener, byte, DllImport, uint, UIntPtr, Gestures, HashSet, IReadOnlyDictionary (+10 more)

### Community 6 - "Face Control Widget"
Cohesion: 0.06
Nodes (18): Config, SizeChangedEventArgs, SoundPlayer, FaceTheme, bool, Button, CancellationTokenSource, DispatcherTimer (+10 more)

### Community 7 - "Surface Map Model"
Cohesion: 0.09
Nodes (23): Alternatives, EdgeInfo, Hop, Info, NodeInfo, ControlType, DateTime, Dictionary (+15 more)

### Community 8 - "Windows Client Namespaces"
Cohesion: 0.09
Nodes (15): U.WindowsClient.Backend, U.WindowsClient.Ui, U.WindowsClient.Uia, U.WindowsClient.Telemetry, U.Graph.Surfaces, U.WindowsClient.Navigation, U.WindowsClient.Domain, U.WindowsClient.Diagnostics (+7 more)

### Community 9 - "Surface Map Utilities"
Cohesion: 0.13
Nodes (7): Stack, bool, int, IReadOnlyDictionary, string, RECT, SurfaceMapTools

### Community 10 - "Face Animation Rendering"
Cohesion: 0.08
Nodes (18): Color, DoubleAnimationUsingKeyFrames, DrawingContext, FacePose, FrameworkElement, Geometry, Random, ScaleTransform (+10 more)

### Community 11 - "Surface Location Detection"
Cohesion: 0.12
Nodes (16): SurfaceLocation, Uri, bool, Dispatcher, DispatcherTimer, DllImport, HashSet, int (+8 more)

### Community 12 - "Face Window Event Handlers"
Cohesion: 0.14
Nodes (3): CancellationToken, RoutedEventArgs, Task

### Community 13 - "UI Element Tree Walking"
Cohesion: 0.13
Nodes (16): EnumWindowsProc, UiElement, AutomationElement, AutomationElementInformation, ControlType, DllImport, HashSet, int (+8 more)

### Community 14 - "SAP GUI vs UIA Research"
Cohesion: 0.08
Nodes (35): Investigación SAP GUI vs UIA, Captura de texto tecleado, SAP Community Q&A (sesión C#, SessionEvents, 7.40→7.70, Excel 64-bit), Issues dotnet/winforms#7763 y dotnet/wpf#9382, Enfoque híbrido UIA + WinEvents/Raw Input, Entries — opciones de combo en SAP, EVENT_OBJECT_VALUECHANGE, Evento Change de SAP GUI Scripting (batched) (+27 more)

### Community 15 - "UI Field Selectors"
Cohesion: 0.10
Nodes (11): AutomationElementCollection, Selectors, AutomationElement, AutomationElementInformation, Condition, ControlType, DetectedField, FieldOption (+3 more)

### Community 16 - "Graph Explorer Window"
Cohesion: 0.11
Nodes (17): ScrollViewer, bool, Border, Button, CancellationTokenSource, DateTime, DispatcherTimer, DllImport (+9 more)

### Community 17 - "Surface Alignment Matching"
Cohesion: 0.16
Nodes (13): SurfaceAligner, string, PlanStep, SurfaceIdentity, CancellationToken, Dictionary, Func, IReadOnlyList (+5 more)

### Community 18 - "Inspector Overlay Drawing"
Cohesion: 0.12
Nodes (17): caption, mapped, Matrix, shell, box, Brush, Canvas, DllImport (+9 more)

### Community 19 - "Graph Backend HTTP Client"
Cohesion: 0.17
Nodes (12): HttpRequestMessage, CancellationToken, Dictionary, Func, HttpClient, HttpResponseMessage, HttpStatusCode, JsonElement (+4 more)

### Community 20 - "SAP GUI Surface Reader"
Cohesion: 0.10
Nodes (18): LowLevelMouseProc, ManualResetEventSlim, Thread, SapContextReader, bool, DateTime, Dispatcher, DispatcherTimer (+10 more)

### Community 21 - "Navigation Strategies"
Cohesion: 0.14
Nodes (15): byte, CancellationToken, DllImport, int, IntPtr, Task, TimeSpan, uint (+7 more)

### Community 22 - "SAP COM Event Binding"
Cohesion: 0.12
Nodes (15): Arity, Delegate, DispId, Guid, ITypeInfo, Name, TYPEATTR, IEnumerable (+7 more)

### Community 23 - "UIA Surface Provider"
Cohesion: 0.12
Nodes (16): AutomationPropertyChangedEventHandler, bool, byte, Dictionary, HashSet, HookProc, int, object (+8 more)

### Community 24 - "Graph Node Visualization"
Cohesion: 0.11
Nodes (18): Node, SolidColorBrush, Border, CancellationToken, Dictionary, DllImport, EventArgs, HashSet (+10 more)

### Community 25 - "Workflow Library Window"
Cohesion: 0.13
Nodes (12): ValueTask, bool, CancellationTokenSource, DispatcherTimer, EventArgs, List, RoutedEventArgs, string (+4 more)

### Community 26 - "Win32 Window Interop"
Cohesion: 0.14
Nodes (4): DllImport, IntPtr, RECT, StringBuilder

### Community 27 - "OpenAI Provider Adapter"
Cohesion: 0.18
Nodes (22): TurnRequest, TurnResult, asArr(), asObj(), asStr(), customFn(), dataUri(), extractMessage() (+14 more)

### Community 28 - "Workflow Step Recorder"
Cohesion: 0.14
Nodes (13): Channel, ValueTask, FinishResponse, ObservedStep, CancellationToken, CancellationTokenSource, Dictionary, int (+5 more)

### Community 29 - "Agent Execution Loop"
Cohesion: 0.16
Nodes (13): CancellationToken, Func, HashSet, int, Task, AgentLoop, IUserChannel, IVoice (+5 more)

### Community 30 - "Teach API Handlers"
Cohesion: 0.15
Nodes (20): handler(), handler(), handler(), checkAuth(), FileStateBody, guard(), handleFileState(), handleProcessVideo() (+12 more)

### Community 31 - "Low-Level Input Executor"
Cohesion: 0.15
Nodes (11): INPUT, InputUnion, ushort, DllImport, int, IntPtr, uint, INPUT (+3 more)

### Community 32 - "Clinical Data Bridge"
Cohesion: 0.10
Nodes (10): CancellationToken, HttpClient, IReadOnlyList, Task, ClinicalBridge, CollapsedFace, Face, IReadOnlyList (+2 more)

### Community 33 - "Autofill API Contracts"
Cohesion: 0.15
Nodes (23): AutofillRow, Dictionary, JsonElement, List, AutofillRequest, AutofillResponse, AutofillResult, ContextNote (+15 more)

### Community 34 - "Workflow Library UI Bindings"
Cohesion: 0.10
Nodes (21): ConfidenceLabel, Label, Value, TextChangedEventArgs, ListBox, AutofillStatus, ConnStatus, ForceSurface (+13 more)

### Community 36 - "SAP Step Selector"
Cohesion: 0.20
Nodes (5): KeyValuePair, PlanStep, Regex, string, SapSelector

### Community 37 - "Client/Brain Architecture Docs"
Cohesion: 0.13
Nodes (22): Arquitectura — la separación cliente / cerebro, ExecutionEngine.run (core Android), Desplegar el backend en Vercel, Variables de entorno del backend, Ü · Backend (el cerebro), GET /api/health, Backend en Vercel (Fase 1), CLIENT_TOKEN — candado del endpoint (+14 more)

### Community 38 - "Node Package Manifest"
Cohesion: 0.09
Nodes (21): dependencies, description, devDependencies, tsx, typescript, @vercel/node, engines, node (+13 more)

### Community 39 - "Turn Engine & MCP Catalog"
Cohesion: 0.17
Nodes (16): assembleTools(), resolveTurn(), runProviderTurn(), baseCatalog(), catalogNames(), gestureTools, LEARNED_VIA, McpParam (+8 more)

### Community 40 - "TypeScript Compiler Config"
Cohesion: 0.09
Nodes (21): compilerOptions, esModuleInterop, forceConsistentCasingInFileNames, lib, module, moduleResolution, noEmit, noImplicitAny (+13 more)

### Community 41 - "Workflow Teach Session"
Cohesion: 0.13
Nodes (10): EventHandler, CancellationToken, DllImport, int, IntPtr, string, Task, WorkflowTeachSession (+2 more)

### Community 42 - "Plan Step Execution"
Cohesion: 0.12
Nodes (8): L, T, Times, IReadOnlyList, Func, Key, PlanStep, POINT

### Community 43 - "Face Backend Menu Panel"
Cohesion: 0.23
Nodes (5): BackendBody, BackendZone, ContextZone, RotateTransform, StackPanel

### Community 44 - "Graph Navigation Concepts"
Cohesion: 0.13
Nodes (21): Aceptado no es ejecutado, La ubicación es el ancla (parámetro `at`), Cromo global detectado por repetición, EdgeInfo (ActionType / Kind), GraphCrawler, Identidad por sección cuando el título no distingue, Interrupcion (un diálogo no es un lugar), map_go_to (+13 more)

### Community 45 - "Surface Readiness Waiting"
Cohesion: 0.15
Nodes (7): CancellationToken, double, int, Task, SurfaceReadiness, PlanStep, IUiSurface

### Community 46 - "Tree Row Geometry Cache"
Cohesion: 0.14
Nodes (14): ConcurrentDictionary, Stopwatch, TreeRow, Box, double, int, IsFolder, List (+6 more)

### Community 47 - "App Window Focus Aligner"
Cohesion: 0.24
Nodes (7): CancellationToken, DllImport, Func, int, IntPtr, Task, AppAligner

### Community 48 - "UI Inspector Hooks"
Cohesion: 0.21
Nodes (10): Dispatcher, DispatcherTimer, DllImport, HookProc, int, IntPtr, uint, MSLLHOOKSTRUCT (+2 more)

### Community 49 - "Windows Repo Guide Docs"
Cohesion: 0.15
Nodes (18): 🧪 Ensayo en seco, Graph — el cerebro remoto, Guía del repo Ü Windows, Huella estructural por paso, SAP GUI Scripting (COM), UIA — superficie genérica de Windows, windows-client (cliente C#/WPF), windows-graph (workflows sobre SAP GUI) (+10 more)

### Community 50 - "Graph Health Status"
Cohesion: 0.16
Nodes (9): Dot, Brush, Text, GraphHealthText, object, TimeSpan, GraphHealth, GraphLink (+1 more)

### Community 51 - "Global Hotkeys"
Cohesion: 0.16
Nodes (10): HwndSource, EventArgs, Dictionary, DllImport, int, IntPtr, List, uint (+2 more)

### Community 52 - "Gemini Provider Adapter"
Cohesion: 0.25
Nodes (15): asArr(), asInt(), asObj(), asStr(), builtinFns(), COMPUTER_FNS, fn(), gemHttp() (+7 more)

### Community 53 - "Onboarding Window"
Cohesion: 0.12
Nodes (10): ControlTemplate, TextBox, Border, Brush, Button, Regex, RoutedEventArgs, TextBlock (+2 more)

### Community 54 - "Clinical Concept Binding"
Cohesion: 0.18
Nodes (10): U.WindowsClient.Clinical, Field, ClinicalValue, HashSet, IReadOnlyList, Key, List, string (+2 more)

### Community 55 - "Velopack Release Pipeline"
Cohesion: 0.14
Nodes (16): Workflow CI «Windows release», GRAPH_DEFAULT_API_KEY (secreto embebido en el build), Paso «Subir a Supabase (compatible con S3)», Firma de código / SmartScreen, scripts/publish-release.ps1, Bucket público `windows` (Supabase miracle-app), Ui/FaceWindow.xaml (UpdateBtn), releases.win.json (el índice del feed) (+8 more)

### Community 56 - "UI Graph Crawl Drawing"
Cohesion: 0.18
Nodes (4): IEnumerable, List, Task, UiElement

### Community 57 - "UIA Condition Selectors"
Cohesion: 0.18
Nodes (6): Condition, ControlType, Dictionary, IEnumerable, string, UiaSelector

### Community 59 - "Turn Endpoint & Session"
Cohesion: 0.23
Nodes (13): handler(), assertConfigured(), deps(), decodeSession(), encodeSession(), freshSession(), GeminiPending, GeminiState (+5 more)

### Community 60 - "Telemetry Client"
Cohesion: 0.17
Nodes (9): ConcurrentQueue, IDisposable, int, string, Task, Timer, TelemetryBus, TelemetryClient (+1 more)

### Community 61 - "Step Screenshot Capture"
Cohesion: 0.34
Nodes (4): DllImport, int, IntPtr, StepShotCamera

### Community 62 - "Desktop Window Manager"
Cohesion: 0.17
Nodes (5): DllImport, IntPtr, string, StringBuilder, Escritorio

### Community 63 - "Inspector Diagnostics"
Cohesion: 0.21
Nodes (9): int, IReadOnlyList, Key, Rect, string, Text, UiElement, Via (+1 more)

### Community 64 - "SAP Field Hit Testing"
Cohesion: 0.17
Nodes (7): Path, TreeId, DetectedField, IReadOnlyList, Key, Text, Via

### Community 66 - "Backend Module Layout"
Cohesion: 0.17
Nodes (15): container.ts — puntos de extensión de persistencia, resolveTurn, Sesión firmada / backend stateless, application/engine.ts (orquesta un turno), brain/gemini.ts, learning/workflows.ts, domain/mcp.ts (catálogo MCP, solo declaración), memory/store.ts (+7 more)

### Community 67 - "Installed Apps Launcher"
Cohesion: 0.17
Nodes (6): U.WindowsClient.SystemApi, IEnumerable, string, InstalledApps, IEnumerable, StartMenuLauncher

### Community 68 - "Backend HTTP Client"
Cohesion: 0.24
Nodes (10): HttpContent, bool, CancellationToken, HttpClient, HttpResponseMessage, HttpStatusCode, JsonSerializerOptions, string (+2 more)

### Community 69 - "Highlight Overlay"
Cohesion: 0.23
Nodes (7): Rectangle, Canvas, EventArgs, int, IReadOnlyList, Rect, HighlightOverlay

### Community 70 - "Win32 Window Enumeration"
Cohesion: 0.27
Nodes (4): DllImport, EnumProc, IntPtr, StringBuilder

### Community 71 - "In-Memory Learning Store"
Cohesion: 0.18
Nodes (4): InMemoryLearningStore, Workflow, InMemoryMemoryStore, MemoryStore

### Community 72 - "SAP Scripting Event Notes"
Cohesion: 0.16
Nodes (15): La carrera del Busy (pendiente), EndRequest (re-resolver el árbol), session.FindById (rutas relativas), PressToolbarButton (#tbbtn=NV44), SapComEvents (enganche por introspección), SapSelector.Normalize, session.Busy, StartRequest (el instante más valioso) (+7 more)

### Community 73 - "App Startup & Config"
Cohesion: 0.14
Nodes (7): U.WindowsClient, StartupEventArgs, STAThread, Application, App, string, Config

### Community 74 - "Menu & Dialog Reading"
Cohesion: 0.25
Nodes (4): List, Opciones, Textos, Titulo

### Community 75 - "SAP/UIA Hit Diagnostics"
Cohesion: 0.18
Nodes (3): IReadOnlyList, Rect, UiElement

### Community 76 - "SAP Element Description"
Cohesion: 0.22
Nodes (3): Dictionary, FieldOption, IEnumerable

### Community 77 - "Step Debugger Pause UI"
Cohesion: 0.19
Nodes (8): Image, Button, Task, TaskCompletionSource, TextBlock, StepDebuggerWindow, StepDecision, StepPause

### Community 78 - "Velopack Auto-Updater"
Cohesion: 0.31
Nodes (5): UpdateManager, VelopackAsset, Task, TimeSpan, Updater

### Community 79 - "Health & Video Config"
Cohesion: 0.30
Nodes (9): handler(), activeKey(), activeModel(), config, Provider, videoArchiveEnabled(), sanitize(), SignedVideoUpload (+1 more)

### Community 80 - "SAP Method Learnings"
Cohesion: 0.18
Nodes (11): Aprendizajes de método, LogBus — el log es la fuente de verdad, Workflow NWP1 (admisión de paciente), CONTRASTE geometría (aserción viva), InspectorDiagnostics, FindByPosition (no resuelve en este SAP), GuiTree — árboles de SAP, Identity() con subdynpro (sub y ssub) (+3 more)

### Community 81 - "Project Dependencies"
Cohesion: 0.20
Nodes (10): Microsoft.CSharp (4.7.0), NAudio (2.2.1), ScreenRecorderLib (6.6.0), System.Drawing.Common (8.0.7), System.Speech (8.0.0), Velopack (1.2.0), net8.0-windows, System.Text.Json (8.0.5) (+2 more)

### Community 82 - "Voice Input & Speech"
Cohesion: 0.20
Nodes (7): SpeechSynthesizer, VoiceActivity, bool, CancellationToken, Task, VoiceActivity, VoiceIO

### Community 83 - "Log Window"
Cohesion: 0.23
Nodes (6): CopyBtn, List, Window, RoutedEventArgs, LogWindow, Button

### Community 84 - "Locator Badge Overlay"
Cohesion: 0.24
Nodes (6): DllImport, EventArgs, int, IntPtr, TextBlock, LocatorBadge

### Community 85 - "Workflow Dry Run"
Cohesion: 0.26
Nodes (6): IEnumerable, IReadOnlyList, DryRunFinding, DryRunLevel, DryRunReport, WorkflowDryRun

### Community 86 - "Log Bus"
Cohesion: 0.20
Nodes (7): bool, int, IReadOnlyList, List, object, string, LogBus

### Community 88 - "UIA Threading Architecture"
Cohesion: 0.24
Nodes (11): Arquitectura de tres hilos, AutomationId, CacheRequest (UIA), Correcciones a las premisas del encargo, GuiComboBox.Entries, LegacyIAccessiblePattern (COM-only), Mapeo conceptual DOM ↔ SAP ↔ UIA, SetWinEventHook / WinEvents (fallback) (+3 more)

### Community 89 - "Foreground Surface Detector"
Cohesion: 0.36
Nodes (4): DllImport, IntPtr, StringBuilder, SurfaceDetector

### Community 90 - "Face Mood Animation"
Cohesion: 0.31
Nodes (3): TranslateTransform, FaceMood, UIElement

### Community 91 - "Fill Preview Dialog"
Cohesion: 0.20
Nodes (7): Window, Brush, Button, Task, TaskCompletionSource, UIElement, FillPreviewWindow

### Community 92 - "Surface Navigator"
Cohesion: 0.36
Nodes (5): CancellationToken, Func, IReadOnlyList, Task, SurfaceNavigator

### Community 93 - "Screen Pointer Overlay"
Cohesion: 0.33
Nodes (5): Caja, Que, IReadOnlyList, Rect, Senalador

### Community 94 - "Agent Computer-Use Bridge"
Cohesion: 0.25
Nodes (9): El agente que se rescata solo, PrependAlignmentStepAsync, El puente consciente (computer-use), /api/v1/agent/turn (Graph), GRAPH_API_KEY (miracle_…), AgentLoop (compuerta de origen), BackendClient, InputExecutor (SendInput) (+1 more)

### Community 95 - "Pending Finish Queue"
Cohesion: 0.28
Nodes (6): Entry, CancellationToken, List, Task, Entry, PendingFinish

### Community 96 - "Workflow List Menu"
Cohesion: 0.19
Nodes (3): SelectionChangedEventArgs, BarPanel, WorkflowListBox

### Community 97 - "SAP Visual Elements"
Cohesion: 0.39
Nodes (3): SapBox, IReadOnlyList, SapVisualElement

### Community 98 - "Teach Session Contracts"
Cohesion: 0.25
Nodes (8): List, FileStateRequest, FileStateResponse, ProcessRequest, ProcessResult, TeachNote, UploadTokenRequest, UploadTokenResponse

### Community 99 - "Own Window Detection"
Cohesion: 0.28
Nodes (3): DllImport, IntPtr, Propio

### Community 100 - "Graphify Decision Graph Design"
Cohesion: 0.29
Nodes (8): Capa 1 — decisiones de desarrollo (determinista), Capa 2 — decisiones por app y por usuario (futura), Graphify — el grafo de decisiones, map_learn_app, Una regla ganada en una app no se exporta sin verificar, REGLAS (app) vs TERRENO (usuario), Plan hasta el producto, Fase 5 · Cualquier app

### Community 101 - "UI Tree Node Selection"
Cohesion: 0.25
Nodes (6): Key, Text, Via, Key, Text, Via

### Community 102 - "Vercel Function Config"
Cohesion: 0.29
Nodes (6): maxDuration, maxDuration, functions, api/teach/process-video.ts, api/**/*.ts, $schema

### Community 103 - "Windows Screen Capture"
Cohesion: 0.33
Nodes (4): U.WindowsClient.Capture, DllImport, int, Screenshotter

### Community 104 - "Client Telemetry Events"
Cohesion: 0.33
Nodes (5): List, AckResponse, EventsPayload, RegisterPayload, TelemetryEvent

### Community 107 - "Local Dev Server"
Cohesion: 0.40
Nodes (3): dir, port, server

### Community 108 - "Agent Prompt Building"
Cohesion: 0.70
Nodes (4): goalPrompt(), learnedRule(), memoryBlock(), workflowRule()

### Community 110 - "Agent Turn Protocol"
Cohesion: 0.50
Nodes (4): List, ScreenState, TurnRequest, TurnResponse

### Community 111 - "Graph Client Exceptions"
Cohesion: 0.67
Nodes (3): Exception, FinishPendingException, GraphException

### Community 114 - "Clinical Values API Bridge"
Cohesion: 1.00
Nodes (3): GET /api/agent/values (emparejamiento por código), Conceptos canónicos (vital.talla, vital.peso…), Puente con el portal clínico

### Community 115 - "Win32 Input Structs"
Cohesion: 0.67
Nodes (3): KEYBDINPUT, MOUSEINPUT, InputUnion

## Ambiguous Edges - Review These
- `UiaSurface` → `IsStepReady (compuerta al primer plano)`  [AMBIGUOUS]
  windows-client/CLAUDE.md · relation: calls
- `Entries — opciones de combo en SAP` → `UIA — Control Patterns (Value, Toggle, Selection, ComboBox, Edit)`  [AMBIGUOUS]
  windows-graph/INVESTIGACION-SAPGUI-UIA.md · relation: conceptually_related_to
- `Fiabilidad de eventos (controles Win32 no siempre notifican)` → `Issues dotnet/winforms#7763 y dotnet/wpf#9382`  [AMBIGUOUS]
  windows-graph/INVESTIGACION-SAPGUI-UIA.md · relation: conceptually_related_to

## Knowledge Gaps
- **125 isolated node(s):** `dir`, `server`, `port`, `name`, `version` (+120 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **12 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **What is the exact relationship between `UiaSurface` and `IsStepReady (compuerta al primer plano)`?**
  _Edge tagged AMBIGUOUS (relation: calls) - confidence is low._
- **What is the exact relationship between `Entries — opciones de combo en SAP` and `UIA — Control Patterns (Value, Toggle, Selection, ComboBox, Edit)`?**
  _Edge tagged AMBIGUOUS (relation: conceptually_related_to) - confidence is low._
- **What is the exact relationship between `Fiabilidad de eventos (controles Win32 no siempre notifican)` and `Issues dotnet/winforms#7763 y dotnet/wpf#9382`?**
  _Edge tagged AMBIGUOUS (relation: conceptually_related_to) - confidence is low._
- **Why does `FaceWindow` connect `Face Control Widget` to `Audio & WebSocket Capture`, `Face Window UI Layout`, `Audio & Screen Recording`, `Mouse Input Hooks`, `Surface Map Model`, `Windows Client Namespaces`, `Surface Location Detection`, `Face Window Event Handlers`, `UI Element Tree Walking`, `Graph Explorer Window`, `Graph Backend HTTP Client`, `SAP GUI Surface Reader`, `UIA Surface Provider`, `Graph Node Visualization`, `Workflow Library Window`, `Agent Execution Loop`, `Clinical Data Bridge`, `Workflow Teach Session`, `Face Backend Menu Panel`, `UI Inspector Hooks`, `Graph Health Status`, `Global Hotkeys`, `Backend HTTP Client`, `Step Debugger Pause UI`, `Velopack Auto-Updater`, `Voice Input & Speech`, `Log Window`, `Locator Badge Overlay`, `Face Mood Animation`, `Fill Preview Dialog`, `Workflow List Menu`?**
  _High betweenness centrality (0.319) - this node is a cross-community bridge._
- **Why does `SapGuiSurface` connect `SAP GUI Surface Reader` to `SAP Field Hit Testing`, `SAP Event Publishing`, `SAP Tree & Grid Reading`, `SAP Step Selector`, `Face Control Widget`, `Windows Client Namespaces`, `Surface Location Detection`, `SAP Element Description`, `Surface Readiness Waiting`, `Tree Row Geometry Cache`, `SAP COM Event Binding`, `Workflow Library Window`, `Agent Execution Loop`?**
  _High betweenness centrality (0.085) - this node is a cross-community bridge._
- **Why does `UiaSurface` connect `UIA Surface Provider` to `UI Graph Crawler`, `UIA Event Publishing`, `Face Control Widget`, `Windows Client Namespaces`, `Surface Map Utilities`, `Workflow Teach Session`, `Plan Step Execution`, `Surface Readiness Waiting`, `UI Field Selectors`, `Graph Explorer Window`, `Workflow Library Window`, `Win32 Window Interop`, `Agent Execution Loop`, `UIA Condition Selectors`?**
  _High betweenness centrality (0.085) - this node is a cross-community bridge._
- **What connects `dir`, `server`, `port` to the rest of the system?**
  _125 weakly-connected nodes found - possible documentation gaps or missing edges._