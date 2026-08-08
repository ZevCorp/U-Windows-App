# Graph Report - U-Windows-App  (2026-08-08)

## Corpus Check
- 165 files · ~295,935 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 3001 nodes · 6232 edges · 165 communities (151 shown, 14 thin omitted)
- Extraction: 96% EXTRACTED · 4% INFERRED · 0% AMBIGUOUS · INFERRED: 256 edges (avg confidence: 0.8)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `cbb7319b`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- GraphCrawler
- LiveVideo
- SurfaceLocator
- VideoLibraryWindow
- ClickWatcher
- .Call
- FaceWindow.xaml.cs
- SurfaceMap
- U.WindowsClient.Diagnostics
- SurfaceMapTools
- FaceControl
- AppAligner
- TeachSession
- UiaReader
- INVESTIGACION-SAPGUI-UIA.md
- .Describe
- .Edges
- .RunAsync
- InspectorOverlay
- Graph Backend HTTP Client
- SapGuiSurface
- NavTarget
- SAP COM Event Binding
- UiaSurface
- WorkflowMapWindow
- WorkflowLibraryWindow
- IntPtr
- openai.ts
- WorkflowRecorder
- Agent Execution Loop
- Teach API Handlers
- InputExecutor
- AgentLoop.cs
- Contracts.cs
- Window
- SAP Tree & Grid Reading
- .Execute
- Desplegar el backend en Vercel
- backend/package.json
- mcp.ts
- TypeScript Compiler Config
- WorkflowTeachSession
- AutomationElement
- EnsenarAsync
- SurfaceMapTools
- PlanStep
- Tree Row Geometry Cache
- LanzarConScroll
- UI Inspector Hooks
- windows-client — el inspector visual
- GraphObservation
- GlobalHotkeys
- Gemini Provider Adapter
- OnboardingWindow
- Clinical Concept Binding
- Publicar actualizaciones (Ü Windows)
- GraphExplorerWindow.cs
- UIA Condition Selectors
- .GetForegroundWindow
- handleTurn.ts
- TelemetryClient
- Step Screenshot Capture
- SurfaceMap
- Inspector Diagnostics
- .ReadTreeSelections
- AgentLoop (compuerta de origen)
- CarruselDeApps
- Backend HTTP Client
- CrecerPastilla
- .AsegurarFoco
- In-Memory Learning Store
- GuiSession
- Config
- Explorador
- SAP/UIA Hit Diagnostics
- .Str
- StepDebuggerWindow
- .Log
- Health & Video Config
- GuiTree — árboles de SAP
- WindowsClient.csproj
- VoiceIO
- LogBus
- Locator Badge Overlay
- .Analyze
- AtajoPorGolpes
- .Same
- UIA Threading Architecture
- SurfaceDetector
- NucleoVersiones
- Fill Preview Dialog
- OlvidarJerarquias
- Senalador
- Reglas de arquitectura del mapeo
- Pending Finish Queue
- SurfaceMap
- SapVisualElement
- Teach Session Contracts
- .Session
- Graphify — el grafo de decisiones
- UI Tree Node Selection
- Vercel Function Config
- Screenshotter
- .Contar
- JSON Workflow Parsing
- Run Timing Metrics
- Local Dev Server
- Gestures
- MuelleEase
- Agent Turn Protocol
- PasoAPaso
- User Path Resolution
- UI Element Fingerprints
- Clinical Values API Bridge
- Win32 Input Structs
- Reglas universales de UIA (cualquier app)
- PanelDeAcciones
- Step-by-Step Screenshots
- GeminiLive
- Runbook de producción — de cero a un usuario usándolo
- FaceGestures
- windows-graph — SAP GUI Scripting: hechos duros
- engine.ts
- Graphify en el equipo
- .GoTo
- LiveAudio
- 2. Windows UI Automation (UIA)
- RastroDelCursor
- Ü · Backend (el cerebro)
- .EjecutarNucleoAsync
- 1. SAP GUI Scripting
- Ü Windows — guía para trabajar en este repo
- Reglas del explorador de archivos de Windows 11 (`explorer.exe`)
- SAP GUI Scripting (superficie de automatización)
- .ObserveExits
- 1.7 GRABACIÓN — el modelo de eventos (lo crítico)
- Ü · Cliente Windows (el frontend tonto)
- LocalMcp
- src/Update/Updater.cs
- Ventana
- Captura de texto tecleado
- Reconocedor
- Ü — Windows App
- U.WindowsClient.SystemApi
- Árboles (`GuiTree`)
- .ExitsFrom
- .AuditarAsync
- .ArrancarAsync
- 3. Mapeo conceptual DOM ↔ SAP GUI Scripting ↔ UIA
- Grabar y ejecutar acciones sobre formularios en Windows
- agente-arquitecto/package.json
- arquitecto.mjs
- .EstaDelante
- .RunLearnedTaps
- .FindIn
- version-nucleo.ps1
- ContratoDelGrafo.csproj
- .OlvidarTodo

## God Nodes (most connected - your core abstractions)
1. `UiaSurface` - 123 edges
2. `SapGuiSurface` - 110 edges
3. `SurfaceMapTools` - 82 edges
4. `SurfaceMap` - 73 edges
5. `SurfaceMap` - 55 edges
6. `U.WindowsClient.Diagnostics` - 43 edges
7. `GraphCrawler` - 41 edges
8. `GeminiLive` - 40 edges
9. `SurfaceMap` - 38 edges
10. `Reglas de arquitectura del mapeo` - 36 edges

## Surprising Connections (you probably didn't know these)
- `windows-graph — SAP GUI Scripting: hechos duros` --semantically_similar_to--> `Aceptado no es ejecutado`  [INFERRED] [semantically similar]
  windows-graph/CLAUDE.md → docs/graphify.md
- `CLIENT_TOKEN — candado del endpoint` --references--> `BackendClient`  [INFERRED]
  PRODUCTION.md → windows-client/README.md
- `IsStepReady (compuerta al primer plano)` --calls--> `UiaSurface`  [AMBIGUOUS]
  windows-client/CLAUDE.md → docs/graphify.md
- `runBrainTurn()` --indirect_call--> `t()`  [INFERRED]
  backend/src/brain/openai.ts → agente-arquitecto/arquitecto.mjs
- `GET /api/health` --references--> `Ü · Backend (el cerebro)`  [EXTRACTED]
  PRODUCTION.md → backend/README.md

## Import Cycles
- None detected.

## Hyperedges (group relationships)
- **Bucle de un turno: cliente tonto ↔ cerebro remoto** — windows_client_readme_uia_reader, windows_client_readme_screenshotter, windows_client_readme_backend_client, backend_readme_turn, backend_architecture_resolve_turn, windows_actions_ts, windows_client_readme_input_executor [EXTRACTED 0.90]
- **Pipeline de publicación y auto-update de U.exe** — production_publish_release_ps1, github_workflows_windows_release, releasing_windows_velopack, releasing_windows_releases_json, releasing_windows_bucket, releasing_windows_updater_cs, releasing_windows_u_setup [EXTRACTED 0.90]
- **Flujo de grabación sobre SAP GUI Scripting** — windows_graph_investigacion_sapgui_uia_record_property, windows_graph_investigacion_sapgui_uia_change_event, windows_graph_claude_sap_com_events, windows_graph_claude_start_request, windows_graph_claude_end_request, windows_graph_claude_session_busy [EXTRACTED 0.85]
- **Flujo híbrido de captura de texto en UIA** — windows_graph_investigacion_sapgui_uia_uia, windows_graph_investigacion_sapgui_uia_captura_de_texto, windows_graph_investigacion_sapgui_uia_event_object_valuechange, windows_graph_investigacion_sapgui_uia_raw_input, windows_graph_investigacion_sapgui_uia_winevents, windows_graph_investigacion_sapgui_uia_enfoque_hibrido [EXTRACTED 0.90]
- **Matriz comparativa SAP GUI Scripting vs UIA** — windows_graph_investigacion_sapgui_uia_sap_gui_scripting, windows_graph_investigacion_sapgui_uia_uia, windows_graph_investigacion_sapgui_uia_identificacion_de_elementos, windows_graph_investigacion_sapgui_uia_granularidad_de_grabacion, windows_graph_investigacion_sapgui_uia_captura_de_texto, windows_graph_investigacion_sapgui_uia_entries_combo, windows_graph_investigacion_sapgui_uia_user_scripting_disable_recording, windows_graph_investigacion_sapgui_uia_fiabilidad_de_eventos [EXTRACTED 0.95]
- **Wrappers .NET para UIA y SAP GUI Scripting** — windows_graph_investigacion_sapgui_uia_flaui, windows_graph_investigacion_sapgui_uia_uiacomwrapper, windows_graph_investigacion_sapgui_uia_sapgui_wrapper, windows_graph_investigacion_sapgui_uia_sap_gui_scripting_net [INFERRED 0.75]

## Communities (165 total, 14 thin omitted)

### Community 0 - "GraphCrawler"
Cohesion: 0.06
Nodes (34): Alternativas, Entrable, Frente, Queue, Tipo, CancellationToken, DllImport, EnumProc (+26 more)

### Community 1 - "LiveVideo"
Cohesion: 0.14
Nodes (15): CURSORINFO, Graphics, ICONINFO, bool, DllImport, int, IntPtr, long (+7 more)

### Community 2 - "SurfaceLocator"
Cohesion: 0.06
Nodes (27): SurfaceLocation, Uri, AutomationElement, bool, Dictionary, DllImport, EnumProc, HashSet (+19 more)

### Community 3 - "VideoLibraryWindow"
Cohesion: 0.10
Nodes (19): IReadOnlyList, TeachVideo, VideoLibrary, Caption, DeleteBtn, Empty, List, OpenFolderBtn (+11 more)

### Community 4 - "ClickWatcher"
Cohesion: 0.17
Nodes (14): Click, DllImport, HashSet, HookProc, int, IntPtr, object, StringBuilder (+6 more)

### Community 5 - ".Call"
Cohesion: 0.18
Nodes (4): IReadOnlyDictionary, byte, Dictionary, WindowsSystemApi

### Community 6 - "FaceWindow.xaml.cs"
Cohesion: 0.10
Nodes (32): SizeChangedEventArgs, ColocarVentana(), DllImport, EventArgs, IntPtr, MouseButtonEventArgs, RoutedEventArgs, Task (+24 more)

### Community 7 - "SurfaceMap"
Cohesion: 0.07
Nodes (27): NodeInfo, return, Alternatives, Aristas, Click, ControlType, DateTime, Dictionary (+19 more)

### Community 8 - "U.WindowsClient.Diagnostics"
Cohesion: 0.10
Nodes (11): U.WindowsClient.Ui, U.WindowsClient.Uia, U.Graph.Surfaces, U.WindowsClient.Navigation, U.WindowsClient.Diagnostics, U.WindowsClient.Teach, U.Graph, ContratoDelGrafo (+3 more)

### Community 9 - "SurfaceMapTools"
Cohesion: 0.09
Nodes (11): Stack, bool, Caja, int, IReadOnlyDictionary, IReadOnlyList, Point, Que (+3 more)

### Community 10 - "FaceControl"
Cohesion: 0.08
Nodes (20): DependencyProperty, DoubleAnimationUsingKeyFrames, DrawingContext, FacePose, FrameworkElement, Geometry, Random, RotateTransform (+12 more)

### Community 11 - "AppAligner"
Cohesion: 0.09
Nodes (17): CancellationToken, DllImport, EnumWindowsProc, Func, int, IntPtr, StringBuilder, Task (+9 more)

### Community 12 - "TeachSession"
Cohesion: 0.14
Nodes (14): AudioDevice, Recorder, CancellationToken, IReadOnlyList, string, Task, ValueTask, ScreenRecorder (+6 more)

### Community 13 - "UiaReader"
Cohesion: 0.16
Nodes (14): UiElement, AutomationElement, AutomationElementInformation, ControlType, DllImport, EnumWindowsProc, HashSet, int (+6 more)

### Community 14 - "INVESTIGACION-SAPGUI-UIA.md"
Cohesion: 0.12
Nodes (18): 4. Correcciones a las premisas del encargo, 5. Arquitectura que se deduce de los hechos, 6. Fuentes, SAP Community Q&A (sesión C#, SessionEvents, 7.40→7.70, Excel 64-bit), Dónde falla cada superficie, FlaUI (wrapper .NET de UIA), IUIAutomation (API COM), LowLevelKeyboardProc (+10 more)

### Community 15 - ".Describe"
Cohesion: 0.15
Nodes (8): AutomationPropertyChangedEventArgs, Selectors, AutomationElementInformation, ControlType, DetectedField, FieldOption, Label, List

### Community 16 - ".Edges"
Cohesion: 0.22
Nodes (7): Contrato, int, STAThread, string, From, Info, To

### Community 17 - ".RunAsync"
Cohesion: 0.16
Nodes (11): SurfaceAligner, SurfaceIdentity, CancellationToken, Dictionary, Func, IReadOnlyList, List, Task (+3 more)

### Community 18 - "InspectorOverlay"
Cohesion: 0.12
Nodes (17): Canvas, caption, mapped, Matrix, shell, box, Brush, DllImport (+9 more)

### Community 19 - "Graph Backend HTTP Client"
Cohesion: 0.17
Nodes (12): HttpRequestMessage, CancellationToken, Dictionary, Func, HttpClient, HttpResponseMessage, HttpStatusCode, JsonElement (+4 more)

### Community 20 - "SapGuiSurface"
Cohesion: 0.13
Nodes (17): LowLevelMouseProc, ManualResetEventSlim, Thread, bool, DateTime, Dispatcher, DispatcherTimer, DllImport (+9 more)

### Community 21 - "NavTarget"
Cohesion: 0.10
Nodes (20): byte, CancellationToken, DllImport, int, IntPtr, Task, TimeSpan, uint (+12 more)

### Community 22 - "SAP COM Event Binding"
Cohesion: 0.12
Nodes (15): Arity, Delegate, DispId, Guid, ITypeInfo, Name, TYPEATTR, IEnumerable (+7 more)

### Community 23 - "UiaSurface"
Cohesion: 0.09
Nodes (19): AutomationPropertyChangedEventHandler, Times, bool, byte, Dictionary, HashSet, HookProc, int (+11 more)

### Community 24 - "WorkflowMapWindow"
Cohesion: 0.11
Nodes (18): Node, Border, CancellationToken, Dictionary, DllImport, EventArgs, HashSet, int (+10 more)

### Community 25 - "WorkflowLibraryWindow"
Cohesion: 0.11
Nodes (15): ReloadBtn, RunBtn, TeachBtn, bool, CancellationTokenSource, DispatcherTimer, EventArgs, List (+7 more)

### Community 26 - "IntPtr"
Cohesion: 0.15
Nodes (4): DllImport, IntPtr, RECT, StringBuilder

### Community 27 - "openai.ts"
Cohesion: 0.24
Nodes (15): asArr(), asObj(), asStr(), customFn(), dataUri(), extractMessage(), functionOutput(), mapKey() (+7 more)

### Community 28 - "WorkflowRecorder"
Cohesion: 0.13
Nodes (13): Channel, FinishResponse, ObservedStep, SurfaceAvailability, CancellationToken, CancellationTokenSource, Dictionary, int (+5 more)

### Community 29 - "Agent Execution Loop"
Cohesion: 0.16
Nodes (13): CancellationToken, Func, HashSet, int, Task, AgentLoop, IUserChannel, IVoice (+5 more)

### Community 30 - "Teach API Handlers"
Cohesion: 0.15
Nodes (20): handler(), handler(), handler(), checkAuth(), FileStateBody, guard(), handleFileState(), handleProcessVideo() (+12 more)

### Community 31 - "InputExecutor"
Cohesion: 0.16
Nodes (11): INPUT, InputUnion, ushort, DllImport, int, IntPtr, uint, INPUT (+3 more)

### Community 32 - "AgentLoop.cs"
Cohesion: 0.13
Nodes (7): U.WindowsClient.Backend, U.WindowsClient.Telemetry, U.WindowsClient.Domain, U.WindowsClient.Actions, U.WindowsClient.Voice, U.WindowsClient.Mcp, U.WindowsClient.Agent

### Community 33 - "Contracts.cs"
Cohesion: 0.16
Nodes (21): Dictionary, List, AutofillRequest, AutofillResponse, AutofillResult, ContextNote, ContextNoteRequest, DetectedField (+13 more)

### Community 34 - "Window"
Cohesion: 0.13
Nodes (18): ConfidenceLabel, Label, Value, TextChangedEventArgs, AutofillStatus, ConnStatus, ForceSurface, MatchesList (+10 more)

### Community 36 - ".Execute"
Cohesion: 0.18
Nodes (5): KeyValuePair, PlanStep, Regex, string, SapSelector

### Community 37 - "Desplegar el backend en Vercel"
Cohesion: 0.17
Nodes (13): Apuntar el cliente, Cambiar de proveedor (OpenAI), Comprobar, Desplegar el backend en Vercel, Variables de entorno del backend, Opción A — CLI de Vercel (rápida, recomendada), Opción B — Importar el repo en vercel.com, GET /api/health (+5 more)

### Community 38 - "backend/package.json"
Cohesion: 0.09
Nodes (21): dependencies, description, devDependencies, tsx, typescript, @vercel/node, engines, node (+13 more)

### Community 39 - "mcp.ts"
Cohesion: 0.14
Nodes (18): assembleTools(), goalPrompt(), learnedRule(), memoryBlock(), workflowRule(), baseCatalog(), catalogNames(), gestureTools (+10 more)

### Community 40 - "TypeScript Compiler Config"
Cohesion: 0.09
Nodes (21): compilerOptions, esModuleInterop, forceConsistentCasingInFileNames, lib, module, moduleResolution, noEmit, noImplicitAny (+13 more)

### Community 41 - "WorkflowTeachSession"
Cohesion: 0.15
Nodes (11): EventHandler, IAsyncDisposable, CancellationToken, DllImport, int, IntPtr, string, Task (+3 more)

### Community 42 - "AutomationElement"
Cohesion: 0.12
Nodes (6): L, T, IReadOnlyList, AutomationElement, Func, PlanStep

### Community 43 - "EnsenarAsync"
Cohesion: 0.13
Nodes (13): U.WindowsClient.Capture, Clave(), CancellationToken, IntPtr, IReadOnlyDictionary, Task, UiElement, EnsenarAsync() (+5 more)

### Community 44 - "SurfaceMapTools"
Cohesion: 0.09
Nodes (30): Aceptado no es ejecutado, La ubicación es el ancla (parámetro `at`), Cromo global detectado por repetición, EdgeInfo (ActionType / Kind), GraphCrawler, Identidad por sección cuando el título no distingue, Interrupcion (un diálogo no es un lugar), map_go_to (+22 more)

### Community 45 - "PlanStep"
Cohesion: 0.09
Nodes (14): RelX, RelY, JsonElement, string, PlanStep, CancellationToken, double, int (+6 more)

### Community 46 - "Tree Row Geometry Cache"
Cohesion: 0.14
Nodes (14): ConcurrentDictionary, Stopwatch, TreeRow, Box, double, int, IsFolder, List (+6 more)

### Community 47 - "LanzarConScroll"
Cohesion: 0.07
Nodes (28): MoverConMuelle(), bool, DispatcherTimer, DllImport, double, EventArgs, Func, Gancho (+20 more)

### Community 48 - "UI Inspector Hooks"
Cohesion: 0.21
Nodes (10): Dispatcher, DispatcherTimer, DllImport, HookProc, int, IntPtr, uint, MSLLHOOKSTRUCT (+2 more)

### Community 49 - "windows-client — el inspector visual"
Cohesion: 0.10
Nodes (23): Aprendizajes de método, 🧪 Ensayo en seco, Graph — el cerebro remoto, Guía del repo Ü Windows, Huella estructural por paso, LogBus — el log es la fuente de verdad, windows-client (cliente C#/WPF), Cajas por fila (+15 more)

### Community 50 - "GraphObservation"
Cohesion: 0.16
Nodes (9): Dot, Brush, Text, GraphHealthText, object, TimeSpan, GraphHealth, GraphLink (+1 more)

### Community 51 - "GlobalHotkeys"
Cohesion: 0.18
Nodes (9): Dictionary, DllImport, HwndSource, int, IntPtr, List, uint, Window (+1 more)

### Community 52 - "Gemini Provider Adapter"
Cohesion: 0.25
Nodes (15): asArr(), asInt(), asObj(), asStr(), builtinFns(), COMPUTER_FNS, fn(), gemHttp() (+7 more)

### Community 53 - "OnboardingWindow"
Cohesion: 0.12
Nodes (10): ControlTemplate, Border, Brush, Button, Regex, RoutedEventArgs, TextBlock, TextBox (+2 more)

### Community 54 - "Clinical Concept Binding"
Cohesion: 0.18
Nodes (10): U.WindowsClient.Clinical, Field, ClinicalValue, HashSet, IReadOnlyList, Key, List, string (+2 more)

### Community 55 - "Publicar actualizaciones (Ü Windows)"
Cohesion: 0.14
Nodes (17): Workflow CI «Windows release», GRAPH_DEFAULT_API_KEY (secreto embebido en el build), Firma de código / SmartScreen, scripts/publish-release.ps1, Runbook de producción, README — Ü Windows App, 1. Cómo funciona (resumen), 2. Infraestructura (ya creada, no hay que volver a hacerla) (+9 more)

### Community 56 - "GraphExplorerWindow.cs"
Cohesion: 0.40
Nodes (5): BorrarTerreno(), IReadOnlyDictionary, UiElement, LimpiarGrafo(), PuertasNumeradas()

### Community 57 - "UIA Condition Selectors"
Cohesion: 0.18
Nodes (6): Condition, ControlType, Dictionary, IEnumerable, string, UiaSelector

### Community 59 - "handleTurn.ts"
Cohesion: 0.24
Nodes (13): handler(), resolveTurn(), assertConfigured(), deps(), decodeSession(), encodeSession(), freshSession(), GeminiPending (+5 more)

### Community 60 - "TelemetryClient"
Cohesion: 0.13
Nodes (14): ConcurrentQueue, IDisposable, int, List, string, Task, Timer, AckResponse (+6 more)

### Community 61 - "Step Screenshot Capture"
Cohesion: 0.34
Nodes (4): DllImport, int, IntPtr, StepShotCamera

### Community 62 - "SurfaceMap"
Cohesion: 0.06
Nodes (32): EdgeInfo, Alternatives, App, Aristas, Click, ControlType, Cuantas, DateTime (+24 more)

### Community 63 - "Inspector Diagnostics"
Cohesion: 0.21
Nodes (9): int, IReadOnlyList, Key, Rect, string, Text, UiElement, Via (+1 more)

### Community 64 - ".ReadTreeSelections"
Cohesion: 0.36
Nodes (5): Path, TreeId, Key, Text, Via

### Community 66 - "AgentLoop (compuerta de origen)"
Cohesion: 0.11
Nodes (24): container.ts — puntos de extensión de persistencia, resolveTurn, Sesión firmada / backend stateless, application/engine.ts (orquesta un turno), brain/gemini.ts, learning/workflows.ts, domain/mcp.ts (catálogo MCP, solo declaración), memory/store.ts (+16 more)

### Community 67 - "CarruselDeApps"
Cohesion: 0.10
Nodes (19): SHFILEINFO, DllImport, ImageSource, int, IntPtr, IReadOnlyList, string, uint (+11 more)

### Community 68 - "Backend HTTP Client"
Cohesion: 0.24
Nodes (10): HttpContent, bool, CancellationToken, HttpClient, HttpResponseMessage, HttpStatusCode, JsonSerializerOptions, string (+2 more)

### Community 69 - "CrecerPastilla"
Cohesion: 0.24
Nodes (11): Rectangle, CrecerPastilla(), MouseEventArgs, SolidColorBrush, UIElement, OnCollapsedHoverIn(), OnCollapsedHoverOut(), OnZonaChatEntra() (+3 more)

### Community 70 - ".AsegurarFoco"
Cohesion: 0.17
Nodes (5): DllImport, EnumProc, Func, IntPtr, StringBuilder

### Community 71 - "In-Memory Learning Store"
Cohesion: 0.18
Nodes (4): InMemoryLearningStore, Workflow, InMemoryMemoryStore, MemoryStore

### Community 72 - "GuiSession"
Cohesion: 0.16
Nodes (15): La carrera del Busy (pendiente), EndRequest (re-resolver el árbol), Enlace tardío siempre (SapROTWr.SapROTWrapper), session.FindById (rutas relativas), SapComEvents (enganche por introspección), SapSelector.Normalize, `session.Busy`, StartRequest (el instante más valioso) (+7 more)

### Community 73 - "Config"
Cohesion: 0.29
Nodes (3): U.WindowsClient, string, Config

### Community 74 - "Explorador"
Cohesion: 0.20
Nodes (6): DllImport, int, IntPtr, IReadOnlyList, Entrada, Explorador

### Community 75 - "SAP/UIA Hit Diagnostics"
Cohesion: 0.18
Nodes (3): IReadOnlyList, Rect, UiElement

### Community 76 - ".Str"
Cohesion: 0.17
Nodes (3): Dictionary, FieldOption, IEnumerable

### Community 77 - "StepDebuggerWindow"
Cohesion: 0.18
Nodes (8): Image, Button, Task, TaskCompletionSource, TextBlock, StepDebuggerWindow, StepDecision, StepPause

### Community 78 - ".Log"
Cohesion: 0.10
Nodes (15): U.WindowsClient.Update, StartupEventArgs, UpdateManager, VelopackAsset, Application, STAThread, App, CancellationToken (+7 more)

### Community 79 - "Health & Video Config"
Cohesion: 0.30
Nodes (9): handler(), activeKey(), activeModel(), config, Provider, videoArchiveEnabled(), sanitize(), SignedVideoUpload (+1 more)

### Community 80 - "GuiTree — árboles de SAP"
Cohesion: 0.25
Nodes (8): Workflow NWP1 (admisión de paciente), CONTRASTE geometría (aserción viva), FindByPosition (no resuelve en este SAP), GuiTree — árboles de SAP, Identity() con subdynpro (sub y ssub), PressToolbarButton (#tbbtn=NV44), selectedItemNode (árbol de columnas), VisibleTreeRows (geometría por fila)

### Community 81 - "WindowsClient.csproj"
Cohesion: 0.14
Nodes (12): Microsoft.CSharp (4.7.0), NAudio (2.2.1), ScreenRecorderLib (6.6.0), System.Drawing.Common (8.0.7), System.Speech (8.0.0), Velopack (1.2.0), net8.0-windows, System.Text.Json (8.0.5) (+4 more)

### Community 82 - "VoiceIO"
Cohesion: 0.20
Nodes (7): SpeechSynthesizer, VoiceActivity, bool, CancellationToken, Task, VoiceActivity, VoiceIO

### Community 83 - "LogBus"
Cohesion: 0.10
Nodes (14): bool, int, IReadOnlyList, List, object, string, LogBus, CopyBtn (+6 more)

### Community 84 - "Locator Badge Overlay"
Cohesion: 0.24
Nodes (6): DllImport, EventArgs, int, IntPtr, TextBlock, LocatorBadge

### Community 85 - ".Analyze"
Cohesion: 0.26
Nodes (6): IEnumerable, IReadOnlyList, DryRunFinding, DryRunLevel, DryRunReport, WorkflowDryRun

### Community 86 - "AtajoPorGolpes"
Cohesion: 0.18
Nodes (10): bool, DateTime, DllImport, Gancho, int, IntPtr, TimeSpan, uint (+2 more)

### Community 88 - "UIA Threading Architecture"
Cohesion: 0.24
Nodes (11): Arquitectura de tres hilos, AutomationId, CacheRequest (UIA), Correcciones a las premisas del encargo, GuiComboBox.Entries, LegacyIAccessiblePattern (COM-only), Mapeo conceptual DOM ↔ SAP ↔ UIA, SetWinEventHook / WinEvents (fallback) (+3 more)

### Community 89 - "SurfaceDetector"
Cohesion: 0.42
Nodes (4): DllImport, IntPtr, StringBuilder, SurfaceDetector

### Community 90 - "NucleoVersiones"
Cohesion: 0.14
Nodes (9): Version, DateTime, IReadOnlyList, List, Ok, Porque, NucleoVersiones, RegistroCrudo (+1 more)

### Community 91 - "Fill Preview Dialog"
Cohesion: 0.20
Nodes (7): Window, Brush, Button, Task, TaskCompletionSource, UIElement, FillPreviewWindow

### Community 92 - "OlvidarJerarquias"
Cohesion: 0.13
Nodes (11): App, Cuantas, DeHumano, Etiqueta, IReadOnlyList, Nivel, Button, Color (+3 more)

### Community 93 - "Senalador"
Cohesion: 0.42
Nodes (5): Caja, IReadOnlyList, Que, Rect, Senalador

### Community 94 - "Reglas de arquitectura del mapeo"
Cohesion: 0.06
Nodes (36): Aceptado no es ejecutado (principio transversal del proyecto), Antes de una acción destructiva, di sobre QUÉ actúa, «Atrás» es estado EFÍMERO de la sesión, no dato del mapa (idea del usuario), «Atrás» NO es una arista: es un gesto de historial, Capa 1 determinista; LLM en capa 2, Cruzar una puerta sin explorar se juzga por el CAMBIO, no por el destino, El banco de pruebas también tiene estado: no le tires el suelo a la app, El foco se recupera antes de actuar, y los paneles del shell se descartan con Escape (+28 more)

### Community 95 - "Pending Finish Queue"
Cohesion: 0.28
Nodes (6): Entry, CancellationToken, List, Task, Entry, PendingFinish

### Community 96 - "SurfaceMap"
Cohesion: 0.15
Nodes (11): DateTime, Dictionary, int, IReadOnlyDictionary, string, EdgeInfo, Ensenanza, Hop (+3 more)

### Community 97 - "SapVisualElement"
Cohesion: 0.46
Nodes (3): SapBox, IReadOnlyList, SapVisualElement

### Community 98 - "Teach Session Contracts"
Cohesion: 0.25
Nodes (8): List, FileStateRequest, FileStateResponse, ProcessRequest, ProcessResult, TeachNote, UploadTokenRequest, UploadTokenResponse

### Community 99 - ".Session"
Cohesion: 0.17
Nodes (3): SapContextReader, DetectedField, IReadOnlyList

### Community 100 - "Graphify — el grafo de decisiones"
Cohesion: 0.22
Nodes (11): Capa 1 — decisiones de desarrollo (determinista), Capa 2 — decisiones por app y por usuario (futura), El proceso es el ANFITRIÓN, no la app, Formato de una decisión, Graphify — el grafo de decisiones, Las dos capas, Navegación o contenido lo dice el CONTENEDOR, no dónde cae el elemento, No tienen «Atrás» con AutomationId estable (+3 more)

### Community 101 - "UI Tree Node Selection"
Cohesion: 0.25
Nodes (6): Key, Text, Via, Key, Text, Via

### Community 102 - "Vercel Function Config"
Cohesion: 0.29
Nodes (6): maxDuration, maxDuration, functions, api/teach/process-video.ts, api/**/*.ts, $schema

### Community 103 - "Screenshotter"
Cohesion: 0.31
Nodes (6): DllImport, int, IntPtr, RECT, Screenshotter, RECT

### Community 104 - ".Contar"
Cohesion: 0.16
Nodes (9): ConAccion, Declarados, Detalle, Escenario, Pantallas, IReadOnlyList, Ok, Escenario (+1 more)

### Community 107 - "Local Dev Server"
Cohesion: 0.40
Nodes (3): dir, port, server

### Community 108 - "Gestures"
Cohesion: 0.27
Nodes (5): byte, DllImport, uint, UIntPtr, Gestures

### Community 109 - "MuelleEase"
Cohesion: 0.28
Nodes (3): IEasingFunction, MuelleEase, ThrowEase

### Community 110 - "Agent Turn Protocol"
Cohesion: 0.50
Nodes (4): List, ScreenState, TurnRequest, TurnResponse

### Community 111 - "PasoAPaso"
Cohesion: 0.17
Nodes (10): Exception, Paso, Ventana, Visto, IReadOnlyList, List, Abandonado, PasoAPaso (+2 more)

### Community 114 - "Clinical Values API Bridge"
Cohesion: 1.00
Nodes (3): GET /api/agent/values (emparejamiento por código), Conceptos canónicos (vital.talla, vital.peso…), Puente con el portal clínico

### Community 115 - "Win32 Input Structs"
Cohesion: 0.67
Nodes (3): KEYBDINPUT, MOUSEINPUT, InputUnion

### Community 117 - "Reglas universales de UIA (cualquier app)"
Cohesion: 0.07
Nodes (28): Corta el sufijo « - App» del título, Desplaza a la vista antes de pulsar, relee la caja después, y comprueba que el punto caiga dentro del contenedor, El `ct=` del selector se aplica al resolver, no es un adorno, El destino se confirma cuando se estabiliza, y las lecturas vacías no rompen el candidato, El punto pulsable lo da UIA (GetClickablePoint), no nuestra aritmética, En lo seleccionable, SELECT va antes que INVOKE, Guarda selectores, nunca referencias de elemento, Hay apps que IGNORAN el ratón sintético (+20 more)

### Community 118 - "PanelDeAcciones"
Cohesion: 0.12
Nodes (13): Estado, Border, Color, DateTime, DispatcherTimer, DllImport, EventArgs, int (+5 more)

### Community 123 - "GeminiLive"
Cohesion: 0.12
Nodes (13): ClientWebSocket, SemaphoreSlim, bool, CancellationTokenSource, DateTime, double, HashSet, int (+5 more)

### Community 124 - "Runbook de producción — de cero a un usuario usándolo"
Cohesion: 0.08
Nodes (21): 1.1 Importar en Vercel, 1.2 Variables de entorno (Settings → Environment Variables, scope *Production*), 1.3 Desplegar, 1.4 Seguridad del endpoint (léelo), 1.5 Verificar, 3.1 Fijar el backend por defecto en el cliente (para que el usuario no configure nada), 3.2 Publicar, Fase 0 — Decisiones antes de empezar (+13 more)

### Community 125 - "FaceGestures"
Cohesion: 0.12
Nodes (13): bool, DispatcherTimer, DllImport, double, EventArgs, int, long, MouseButtonEventArgs (+5 more)

### Community 126 - "windows-graph — SAP GUI Scripting: hechos duros"
Cohesion: 0.11
Nodes (21): SAP GUI Scripting (COM), UIA — superficie genérica de Windows, windows-graph (workflows sobre SAP GUI), UiaReader (CollectMenus, CollectFromChildren), Fase 4 · La tarea aprendida (workflows), UiaReader.Read(), Aceptado ≠ ejecutado, El snapshot de campos pertenece a UNA pantalla (+13 more)

### Community 127 - "engine.ts"
Cohesion: 0.40
Nodes (9): TurnRequest, TurnResult, runProviderTurn(), TurnInput, TurnOutput, BrainTurn, ScreenState, McpTool (+1 more)

### Community 128 - "Graphify en el equipo"
Cohesion: 0.11
Nodes (18): 1. Instalar uv, 2. Instalar graphify, 3. Registrar la skill y los hooks, 4. Traer el grafo, Actualizar el grafo a mano, Camino entre dos partes del sistema, Comandos útiles, Encontrar los archivos más conectados (los críticos) (+10 more)

### Community 129 - ".GoTo"
Cohesion: 0.18
Nodes (4): List, Opciones, Textos, Titulo

### Community 130 - "LiveAudio"
Cohesion: 0.15
Nodes (8): BufferedWaveProvider, WaveInEvent, WaveOutEvent, DateTime, double, int, object, LiveAudio

### Community 131 - "2. Windows UI Automation (UIA)"
Cohesion: 0.12
Nodes (17): 2.1 Managed vs COM — el argumento correcto, 2.2 Árbol, TreeWalker y condiciones, 2.3 Caching — obligatorio para un grabador, 2.4 Identificación estable, 2.5 Patterns, 2.6 GRABACIÓN — eventos y threading, 2.7 Lo que UIA **no** puede hacer, 2.8 Detectar ventanas de SAP GUI (+9 more)

### Community 132 - "RastroDelCursor"
Cohesion: 0.12
Nodes (13): Marca, POINT, DllImport, int, IReadOnlyList, List, object, POINT (+5 more)

### Community 133 - "Ü · Backend (el cerebro)"
Cohesion: 0.15
Nodes (15): Arquitectura — la separación cliente / cerebro, Dónde se corta el bucle, El problema que resuelve (heredado de Android), ExecutionEngine.run (core Android), Mapa al core Kotlin, Por qué el cliente conduce el bucle (y no el servidor), Puntos de extensión (fases siguientes), Arquitectura (+7 more)

### Community 134 - ".EjecutarNucleoAsync"
Cohesion: 0.29
Nodes (3): CancellationToken, IReadOnlyDictionary, JsonElement

### Community 135 - "1. SAP GUI Scripting"
Cohesion: 0.13
Nodes (15): 1.1 Qué es y por qué existe, 1.2 Habilitación: servidor, cliente y el aviso modal, 1.3 Árbol de objetos y acceso desde C#, 1.4 Element ID, estabilidad y `FindById`, 1.5 Controles: leer y escribir, 1.6 Enumerar el árbol de la pantalla activa, 1.8 Gotchas de SAP, 1. SAP GUI Scripting (+7 more)

### Community 136 - "Ü Windows — guía para trabajar en este repo"
Cohesion: 0.15
Nodes (12): Arquitectura: cliente tonto, cerebro remoto, Compilar y correr, El agente que se rescata solo (diseño acordado, sin implementar), EL LOG ES LA FUENTE DE VERDAD, El puente con el portal clínico (repo `Pagina-web-clientes-final`), Estado actual (2026-07-26, noche), graphify, La cadena completa funciona, verificada contra el SAP real (+4 more)

### Community 137 - "Reglas del explorador de archivos de Windows 11 (`explorer.exe`)"
Cohesion: 0.17
Nodes (12): Carpeta-o-archivo se le pregunta al DISCO, El contenido vive en ventanas hijas con HWND propio, El cromo persistente se detecta por REPETICIÓN y se usa como atajo, El explorador NO refresca su árbol UIA ante cambios hechos fuera de él, El panel de navegación global se aprende una vez, desde la raíz, El retroceso es el botón `aid=backButton`, por identidad, En la lista se entra con doble clic; en el panel, con clic simple, Fuera de alcance (+4 more)

### Community 138 - "SAP GUI Scripting (superficie de automatización)"
Cohesion: 0.18
Nodes (12): Issues dotnet/winforms#7763 y dotnet/wpf#9382, Entries — opciones de combo en SAP, Fiabilidad de eventos (controles Win32 no siempre notifican), Granularidad de grabación (round-trip vs evento), Identificación de elementos (Id canónico / AutomationId), SAP GUI Scripting (superficie de automatización), SAP.GUI.Scripting.net, SAP GUI Scripting Security Guide (7.60) (+4 more)

### Community 139 - ".ObserveExits"
Cohesion: 0.19
Nodes (6): Alternatives, ControlType, Grupo, IEnumerable, Label, Selector

### Community 140 - "1.7 GRABACIÓN — el modelo de eventos (lo crítico)"
Cohesion: 0.18
Nodes (11): 1.7.1 Diseño del modelo, según SAP **[SAP-DOC]**, 1.7.2 El interruptor: `GuiSession.Record` **[SAP-DOC]**, 1.7.3 Eventos de `GuiSession` **[SAP-DOC]** — firmas literales, 1.7.4 Eventos de `GuiApplication` **[SAP-DOC]**, 1.7.5 Qué se captura en vivo desde un proceso externo, y qué NO, 1.7.6 STA, apartments y el pump — **la trampa de ingeniería nº1**, 1.7 GRABACIÓN — el modelo de eventos (lo crítico), `Change` — el corazón del grabador (+3 more)

### Community 141 - "Ü · Cliente Windows (el frontend tonto)"
Cohesion: 0.24
Nodes (10): macos-client (U.app), backend/src/domain/actions.ts (el contrato), Compilar y ejecutar (Windows 10/11), Cómo la lectura del árbol usa UIA, Estructura, Qué hace (y qué NO), VoiceIO (TTS/STT), Ü · Cliente Windows (el frontend tonto) (+2 more)

### Community 142 - "LocalMcp"
Cohesion: 0.21
Nodes (6): HttpListener, HashSet, LocalMcp, int, Task, McpDevProbe

### Community 143 - "src/Update/Updater.cs"
Cohesion: 0.25
Nodes (9): Paso «Subir a Supabase (compatible con S3)», Backend en Vercel (Fase 1), CLIENT_TOKEN — candado del endpoint, windows-client/src/Config.cs, Bucket público `windows` (Supabase miracle-app), Ui/FaceWindow.xaml (UpdateBtn), releases.win.json (el índice del feed), src/Update/Updater.cs (+1 more)

### Community 144 - "Ventana"
Cohesion: 0.20
Nodes (7): Button, Color, Task, TaskCompletionSource, TextBlock, TextBox, Ventana

### Community 145 - "Captura de texto tecleado"
Cohesion: 0.29
Nodes (8): Captura de texto tecleado, Enfoque híbrido UIA + WinEvents/Raw Input, EVENT_OBJECT_VALUECHANGE, Evento Change de SAP GUI Scripting (batched), Raw Input (captura de tecleo), SAP GUI Scripting API — spec completa (release 620), SetWinEventHook y Event Constants, WinEvents (hooks de accesibilidad Win32)

### Community 146 - "Reconocedor"
Cohesion: 0.38
Nodes (3): IReadOnlyList, UiElement, Reconocedor

### Community 147 - "Ü — Windows App"
Cohesion: 0.33
Nodes (5): Backend, Build, Estructura, Historial, Ü — Windows App

### Community 148 - "U.WindowsClient.SystemApi"
Cohesion: 0.20
Nodes (4): U.WindowsClient.SystemApi, IEnumerable, string, InstalledApps

### Community 149 - "Árboles (`GuiTree`)"
Cohesion: 0.33
Nodes (6): Contar filas: `col.Count`, no `ElementAt` por clave, La geometría por fila SÍ existe: los getters piden `(clave, columna)`, La selección de un árbol de COLUMNAS vive en `selectedItemNode`, Posiciones repetidas = centinela, Una fila no tiene id propio, Árboles (`GuiTree`)

### Community 151 - ".AuditarAsync"
Cohesion: 0.27
Nodes (5): Puede, CancellationToken, Porque, Task, Arquitecto

### Community 153 - "3. Mapeo conceptual DOM ↔ SAP GUI Scripting ↔ UIA"
Cohesion: 0.50
Nodes (4): 3.1 Tabla maestra, 3.2 Qué es el "selector" en cada superficie, 3.3 Clasificación de `actionType`, 3. Mapeo conceptual DOM ↔ SAP GUI Scripting ↔ UIA

### Community 154 - "Grabar y ejecutar acciones sobre formularios en Windows"
Cohesion: 0.67
Nodes (3): Convención de marcado de evidencia, Grabar y ejecutar acciones sobre formularios en Windows, SAP GUI Scripting y UI Automation, desde una app WPF/.NET (C#)

### Community 155 - "agente-arquitecto/package.json"
Cohesion: 0.20
Nodes (9): dependencies, @anthropic-ai/claude-agent-sdk, zod, description, name, private, type, @anthropic-ai/claude-agent-sdk (+1 more)

### Community 156 - "arquitecto.mjs"
Cohesion: 0.29
Nodes (5): corrida, herramientas, t(), TURNOS, docMarkdown()

### Community 157 - ".EstaDelante"
Cohesion: 0.38
Nodes (3): DllImport, IntPtr, UIntPtr

### Community 160 - "version-nucleo.ps1"
Cohesion: 0.60
Nodes (3): Compilar(), Leer-Registro(), Snap()

## Ambiguous Edges - Review These
- `UiaSurface` → `IsStepReady (compuerta al primer plano)`  [AMBIGUOUS]
  windows-client/CLAUDE.md · relation: calls
- `Entries — opciones de combo en SAP` → `UIA — Control Patterns (Value, Toggle, Selection, ComboBox, Edit)`  [AMBIGUOUS]
  windows-graph/INVESTIGACION-SAPGUI-UIA.md · relation: conceptually_related_to
- `Fiabilidad de eventos (controles Win32 no siempre notifican)` → `Issues dotnet/winforms#7763 y dotnet/wpf#9382`  [AMBIGUOUS]
  windows-graph/INVESTIGACION-SAPGUI-UIA.md · relation: conceptually_related_to

## Knowledge Gaps
- **368 isolated node(s):** `TURNOS`, `herramientas`, `corrida`, `name`, `private` (+363 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **14 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **What is the exact relationship between `UiaSurface` and `IsStepReady (compuerta al primer plano)`?**
  _Edge tagged AMBIGUOUS (relation: calls) - confidence is low._
- **What is the exact relationship between `Entries — opciones de combo en SAP` and `UIA — Control Patterns (Value, Toggle, Selection, ComboBox, Edit)`?**
  _Edge tagged AMBIGUOUS (relation: conceptually_related_to) - confidence is low._
- **What is the exact relationship between `Fiabilidad de eventos (controles Win32 no siempre notifican)` and `Issues dotnet/winforms#7763 y dotnet/wpf#9382`?**
  _Edge tagged AMBIGUOUS (relation: conceptually_related_to) - confidence is low._
- **Why does `WorkflowLibraryWindow` connect `WorkflowLibraryWindow` to `Window`, `VideoLibraryWindow`, `Backend HTTP Client`, `U.WindowsClient.Diagnostics`, `WorkflowTeachSession`, `.RunAsync`, `GraphObservation`, `Graph Backend HTTP Client`, `SapGuiSurface`, `UiaSurface`, `Fill Preview Dialog`?**
  _High betweenness centrality (0.080) - this node is a cross-community bridge._
- **Why does `UiaSurface` connect `UiaSurface` to `GraphCrawler`, `.GetForegroundWindow`, `U.WindowsClient.Diagnostics`, `SurfaceMapTools`, `AutomationElement`, `PlanStep`, `UIA Condition Selectors`, `.Describe`, `WorkflowLibraryWindow`, `IntPtr`, `WorkflowRecorder`, `Agent Execution Loop`, `.FindIn`?**
  _High betweenness centrality (0.072) - this node is a cross-community bridge._
- **Why does `SapGuiSurface` connect `SapGuiSurface` to `.ReadTreeSelections`, `.PumpMain`, `SurfaceLocator`, `.Session`, `SAP Tree & Grid Reading`, `.Execute`, `U.WindowsClient.Diagnostics`, `.Str`, `PlanStep`, `Tree Row Geometry Cache`, `SAP COM Event Binding`, `WorkflowLibraryWindow`, `Agent Execution Loop`?**
  _High betweenness centrality (0.071) - this node is a cross-community bridge._
- **What connects `TURNOS`, `herramientas`, `corrida` to the rest of the system?**
  _368 weakly-connected nodes found - possible documentation gaps or missing edges._