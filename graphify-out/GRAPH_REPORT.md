# Graph Report - U-Windows-App  (2026-08-06)

## Corpus Check
- 150 files · ~238,089 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 2963 nodes · 6343 edges · 155 communities (144 shown, 11 thin omitted)
- Extraction: 96% EXTRACTED · 4% INFERRED · 0% AMBIGUOUS · INFERRED: 255 edges (avg confidence: 0.8)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `81419e52`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- GraphCrawler
- LiveVideo
- Window
- TeachSession
- ClickWatcher
- Keyboard Gesture Shortcuts
- FaceWindow
- SurfaceMap
- U.WindowsClient.Diagnostics
- SurfaceMapTools
- FaceControl
- SurfaceLocator
- .SetStatus
- UiaReader
- INVESTIGACION-SAPGUI-UIA.md
- .Describe
- DllImport
- .RunAsync
- Inspector Overlay Drawing
- Graph Backend HTTP Client
- SapGuiSurface
- NavTarget
- SAP COM Event Binding
- UiaSurface
- Graph Node Visualization
- WorkflowLibraryWindow
- IntPtr
- OpenAI Provider Adapter
- WorkflowRecorder
- Agent Execution Loop
- Teach API Handlers
- Low-Level Input Executor
- .OnLoaded
- Contracts.cs
- Window
- SAP Tree & Grid Reading
- .Session
- Desplegar el backend en Vercel
- Explorador
- Turn Engine & MCP Catalog
- TypeScript Compiler Config
- WorkflowTeachSession
- AutomationElement
- .WireMenu
- SurfaceMapTools
- IUiSurface
- Tree Row Geometry Cache
- LanzarConScroll
- UI Inspector Hooks
- windows-client — el inspector visual
- Graph Health Status
- GlobalHotkeys
- Gemini Provider Adapter
- OnboardingWindow
- Clinical Concept Binding
- Publicar actualizaciones (Ü Windows)
- .Render
- UIA Condition Selectors
- .GetForegroundWindow
- Turn Endpoint & Session
- TelemetryClient
- Step Screenshot Capture
- .ExitsFrom
- Inspector Diagnostics
- .ReadTreeSelections
- .PumpMain
- AgentLoop (compuerta de origen)
- CarruselDeApps
- Backend HTTP Client
- Highlight Overlay
- .AsegurarFoco
- In-Memory Learning Store
- GuiSession
- App Startup & Config
- .LeerInterrupcion
- SAP/UIA Hit Diagnostics
- .Str
- Step Debugger Pause UI
- Updater
- Health & Video Config
- GuiTree — árboles de SAP
- WindowsClient.csproj
- VoiceIO
- Log Window
- Locator Badge Overlay
- WorkflowDryRun.cs
- Log Bus
- Surface Place Matching
- UIA Threading Architecture
- Foreground Surface Detector
- .UpdateChip
- Fill Preview Dialog
- Button
- Senalador
- Reglas de arquitectura del mapeo
- Pending Finish Queue
- .SetDirectIndex
- SAP Visual Elements
- Teach Session Contracts
- Own Window Detection
- Graphify — el grafo de decisiones
- UI Tree Node Selection
- Vercel Function Config
- .EnsenarAsync
- Client Telemetry Events
- JSON Workflow Parsing
- Run Timing Metrics
- Local Dev Server
- Agent Prompt Building
- MuelleEase
- Agent Turn Protocol
- PasoAPaso
- User Path Resolution
- UI Element Fingerprints
- Clinical Values API Bridge
- Win32 Input Structs
- PlanStep
- Reglas universales de UIA (cualquier app)
- PanelDeAcciones
- Step-by-Step Screenshots
- GeminiLive
- Runbook de producción — de cero a un usuario usándolo
- FaceGestures
- windows-graph — SAP GUI Scripting: hechos duros
- GraphExplorerWindow
- Graphify en el equipo
- LiveAudio
- 2. Windows UI Automation (UIA)
- RastroDelCursor
- Ü · Backend (el cerebro)
- .EjecutarNucleoAsync
- 1. SAP GUI Scripting
- Ü Windows — guía para trabajar en este repo
- Reglas del explorador de archivos de Windows 11 (`explorer.exe`)
- SAP GUI Scripting (superficie de automatización)
- .EnsenarLaAppAsync
- 1.7 GRABACIÓN — el modelo de eventos (lo crítico)
- Ü · Cliente Windows (el frontend tonto)
- VoiceDot
- src/Update/Updater.cs
- Ventana
- Captura de texto tecleado
- Reconocedor
- Ü — Windows App
- InstalledApps
- Árboles (`GuiTree`)
- .SendKey
- POINT
- .Marcados
- 3. Mapeo conceptual DOM ↔ SAP GUI Scripting ↔ UIA
- Grabar y ejecutar acciones sobre formularios en Windows

## God Nodes (most connected - your core abstractions)
1. `FaceWindow` - 162 edges
2. `UiaSurface` - 125 edges
3. `SapGuiSurface` - 111 edges
4. `SurfaceMapTools` - 79 edges
5. `Window` - 70 edges
6. `GraphExplorerWindow` - 67 edges
7. `SurfaceMap` - 49 edges
8. `GraphCrawler` - 41 edges
9. `GeminiLive` - 41 edges
10. `U.WindowsClient.Diagnostics` - 36 edges

## Surprising Connections (you probably didn't know these)
- `windows-graph — SAP GUI Scripting: hechos duros` --semantically_similar_to--> `Aceptado no es ejecutado`  [INFERRED] [semantically similar]
  windows-graph/CLAUDE.md → docs/graphify.md
- `CLIENT_TOKEN — candado del endpoint` --references--> `BackendClient`  [INFERRED]
  PRODUCTION.md → windows-client/README.md
- `IsStepReady (compuerta al primer plano)` --calls--> `UiaSurface`  [AMBIGUOUS]
  windows-client/CLAUDE.md → docs/graphify.md
- `GET /api/health` --references--> `Ü · Backend (el cerebro)`  [EXTRACTED]
  PRODUCTION.md → backend/README.md
- `Fase 4 · La tarea aprendida (workflows)` --conceptually_related_to--> `windows-graph (workflows sobre SAP GUI)`  [INFERRED]
  docs/plan-producto.md → CLAUDE.md

## Import Cycles
- None detected.

## Hyperedges (group relationships)
- **Bucle de un turno: cliente tonto ↔ cerebro remoto** — windows_client_readme_uia_reader, windows_client_readme_screenshotter, windows_client_readme_backend_client, backend_readme_turn, backend_architecture_resolve_turn, windows_actions_ts, windows_client_readme_input_executor [EXTRACTED 0.90]
- **Pipeline de publicación y auto-update de U.exe** — production_publish_release_ps1, github_workflows_windows_release, releasing_windows_velopack, releasing_windows_releases_json, releasing_windows_bucket, releasing_windows_updater_cs, releasing_windows_u_setup [EXTRACTED 0.90]
- **Flujo de grabación sobre SAP GUI Scripting** — windows_graph_investigacion_sapgui_uia_record_property, windows_graph_investigacion_sapgui_uia_change_event, windows_graph_claude_sap_com_events, windows_graph_claude_start_request, windows_graph_claude_end_request, windows_graph_claude_session_busy [EXTRACTED 0.85]
- **Flujo híbrido de captura de texto en UIA** — windows_graph_investigacion_sapgui_uia_uia, windows_graph_investigacion_sapgui_uia_captura_de_texto, windows_graph_investigacion_sapgui_uia_event_object_valuechange, windows_graph_investigacion_sapgui_uia_raw_input, windows_graph_investigacion_sapgui_uia_winevents, windows_graph_investigacion_sapgui_uia_enfoque_hibrido [EXTRACTED 0.90]
- **Matriz comparativa SAP GUI Scripting vs UIA** — windows_graph_investigacion_sapgui_uia_sap_gui_scripting, windows_graph_investigacion_sapgui_uia_uia, windows_graph_investigacion_sapgui_uia_identificacion_de_elementos, windows_graph_investigacion_sapgui_uia_granularidad_de_grabacion, windows_graph_investigacion_sapgui_uia_captura_de_texto, windows_graph_investigacion_sapgui_uia_entries_combo, windows_graph_investigacion_sapgui_uia_user_scripting_disable_recording, windows_graph_investigacion_sapgui_uia_fiabilidad_de_eventos [EXTRACTED 0.95]
- **Wrappers .NET para UIA y SAP GUI Scripting** — windows_graph_investigacion_sapgui_uia_flaui, windows_graph_investigacion_sapgui_uia_uiacomwrapper, windows_graph_investigacion_sapgui_uia_sapgui_wrapper, windows_graph_investigacion_sapgui_uia_sap_gui_scripting_net [INFERRED 0.75]

## Communities (155 total, 11 thin omitted)

### Community 0 - "GraphCrawler"
Cohesion: 0.06
Nodes (34): Alternativas, Entrable, Frente, Queue, Tipo, CancellationToken, DllImport, EnumProc (+26 more)

### Community 1 - "LiveVideo"
Cohesion: 0.15
Nodes (15): CURSORINFO, Graphics, ICONINFO, bool, DllImport, int, IntPtr, long (+7 more)

### Community 2 - "Window"
Cohesion: 0.06
Nodes (48): KeyEventArgs, ActivatorChevron, ActivatorRot, BackendChevron, BackendDot, BackendHeader, BackendRot, BackendStatus (+40 more)

### Community 3 - "TeachSession"
Cohesion: 0.06
Nodes (34): AudioDevice, IAsyncDisposable, Recorder, CancellationToken, IReadOnlyList, string, Task, ValueTask (+26 more)

### Community 4 - "ClickWatcher"
Cohesion: 0.18
Nodes (12): Click, DllImport, HashSet, HookProc, int, IntPtr, object, StringBuilder (+4 more)

### Community 5 - "Keyboard Gesture Shortcuts"
Cohesion: 0.07
Nodes (18): HttpListener, byte, DllImport, uint, UIntPtr, Gestures, HashSet, IReadOnlyDictionary (+10 more)

### Community 6 - "FaceWindow"
Cohesion: 0.07
Nodes (20): Config, SizeChangedEventArgs, SoundPlayer, FaceTheme, bool, Brush, Button, CancellationTokenSource (+12 more)

### Community 7 - "SurfaceMap"
Cohesion: 0.09
Nodes (22): Alternatives, Aristas, Info, NodeInfo, Nodos, ControlType, DateTime, Dictionary (+14 more)

### Community 8 - "U.WindowsClient.Diagnostics"
Cohesion: 0.08
Nodes (17): U.WindowsClient.Backend, U.WindowsClient.Ui, U.WindowsClient.Capture, U.WindowsClient.Uia, U.WindowsClient.Telemetry, U.Graph.Surfaces, U.WindowsClient.Navigation, U.WindowsClient.Domain (+9 more)

### Community 9 - "SurfaceMapTools"
Cohesion: 0.10
Nodes (9): Stack, bool, Func, int, IReadOnlyDictionary, Point, string, RECT (+1 more)

### Community 10 - "FaceControl"
Cohesion: 0.08
Nodes (18): DependencyProperty, DoubleAnimationUsingKeyFrames, DrawingContext, FacePose, FrameworkElement, Geometry, Random, ScaleTransform (+10 more)

### Community 11 - "SurfaceLocator"
Cohesion: 0.06
Nodes (30): SurfaceLocation, Uri, CancellationToken, DllImport, EnumWindowsProc, Func, int, IntPtr (+22 more)

### Community 12 - ".SetStatus"
Cohesion: 0.13
Nodes (5): DryRunBtn, RunWorkflowBtn, StopBtn, CancellationToken, Task

### Community 13 - "UiaReader"
Cohesion: 0.13
Nodes (16): UiElement, AutomationElement, AutomationElementInformation, ControlType, DllImport, EnumWindowsProc, HashSet, int (+8 more)

### Community 14 - "INVESTIGACION-SAPGUI-UIA.md"
Cohesion: 0.12
Nodes (18): 4. Correcciones a las premisas del encargo, 5. Arquitectura que se deduce de los hechos, 6. Fuentes, SAP Community Q&A (sesión C#, SessionEvents, 7.40→7.70, Excel 64-bit), Dónde falla cada superficie, FlaUI (wrapper .NET de UIA), IUIAutomation (API COM), LowLevelKeyboardProc (+10 more)

### Community 15 - ".Describe"
Cohesion: 0.15
Nodes (8): AutomationPropertyChangedEventArgs, Selectors, AutomationElementInformation, ControlType, DetectedField, FieldOption, Label, List

### Community 16 - "DllImport"
Cohesion: 0.18
Nodes (3): DllImport, IntPtr, WinEventProc

### Community 17 - ".RunAsync"
Cohesion: 0.20
Nodes (10): SurfaceAligner, CancellationToken, Dictionary, Func, IReadOnlyList, List, Task, RunResult (+2 more)

### Community 18 - "Inspector Overlay Drawing"
Cohesion: 0.12
Nodes (17): caption, mapped, Matrix, shell, box, Brush, Canvas, DllImport (+9 more)

### Community 19 - "Graph Backend HTTP Client"
Cohesion: 0.17
Nodes (12): HttpRequestMessage, CancellationToken, Dictionary, Func, HttpClient, HttpResponseMessage, HttpStatusCode, JsonElement (+4 more)

### Community 20 - "SapGuiSurface"
Cohesion: 0.09
Nodes (18): LowLevelMouseProc, ManualResetEventSlim, Thread, SapContextReader, bool, DateTime, Dispatcher, DispatcherTimer (+10 more)

### Community 21 - "NavTarget"
Cohesion: 0.10
Nodes (20): byte, CancellationToken, DllImport, int, IntPtr, Task, TimeSpan, uint (+12 more)

### Community 22 - "SAP COM Event Binding"
Cohesion: 0.12
Nodes (15): Arity, Delegate, DispId, Guid, ITypeInfo, Name, TYPEATTR, IEnumerable (+7 more)

### Community 23 - "UiaSurface"
Cohesion: 0.10
Nodes (17): AutomationPropertyChangedEventHandler, bool, byte, Dictionary, HashSet, HookProc, int, object (+9 more)

### Community 24 - "Graph Node Visualization"
Cohesion: 0.11
Nodes (18): Node, SolidColorBrush, Border, CancellationToken, Dictionary, DllImport, EventArgs, HashSet (+10 more)

### Community 25 - "WorkflowLibraryWindow"
Cohesion: 0.11
Nodes (15): ReloadBtn, RunBtn, TeachBtn, bool, CancellationTokenSource, DispatcherTimer, EventArgs, List (+7 more)

### Community 26 - "IntPtr"
Cohesion: 0.15
Nodes (4): DllImport, IntPtr, RECT, StringBuilder

### Community 27 - "OpenAI Provider Adapter"
Cohesion: 0.18
Nodes (22): TurnRequest, TurnResult, asArr(), asObj(), asStr(), customFn(), dataUri(), extractMessage() (+14 more)

### Community 28 - "WorkflowRecorder"
Cohesion: 0.11
Nodes (14): Channel, JsonElement, FinishResponse, ObservedStep, SurfaceAvailability, CancellationToken, CancellationTokenSource, Dictionary (+6 more)

### Community 29 - "Agent Execution Loop"
Cohesion: 0.16
Nodes (13): CancellationToken, Func, HashSet, int, Task, AgentLoop, IUserChannel, IVoice (+5 more)

### Community 30 - "Teach API Handlers"
Cohesion: 0.15
Nodes (20): handler(), handler(), handler(), checkAuth(), FileStateBody, guard(), handleFileState(), handleProcessVideo() (+12 more)

### Community 31 - "Low-Level Input Executor"
Cohesion: 0.15
Nodes (11): INPUT, InputUnion, ushort, DllImport, int, IntPtr, uint, INPUT (+3 more)

### Community 32 - ".OnLoaded"
Cohesion: 0.11
Nodes (11): CancellationToken, HttpClient, IReadOnlyList, Task, ClinicalBridge, ClinicalPairBtn, CollapsedFace, Face (+3 more)

### Community 33 - "Contracts.cs"
Cohesion: 0.16
Nodes (22): AutofillRow, Dictionary, List, AutofillRequest, AutofillResponse, AutofillResult, ContextNote, ContextNoteRequest (+14 more)

### Community 34 - "Window"
Cohesion: 0.12
Nodes (19): ConfidenceLabel, Label, Value, TextChangedEventArgs, ListBox, AutofillStatus, ConnStatus, ForceSurface (+11 more)

### Community 36 - ".Session"
Cohesion: 0.19
Nodes (3): KeyValuePair, PlanStep, IReadOnlyList

### Community 37 - "Desplegar el backend en Vercel"
Cohesion: 0.17
Nodes (13): Apuntar el cliente, Cambiar de proveedor (OpenAI), Comprobar, Desplegar el backend en Vercel, Variables de entorno del backend, Opción A — CLI de Vercel (rápida, recomendada), Opción B — Importar el repo en vercel.com, GET /api/health (+5 more)

### Community 38 - "Explorador"
Cohesion: 0.07
Nodes (27): dependencies, description, devDependencies, tsx, typescript, @vercel/node, engines, node (+19 more)

### Community 39 - "Turn Engine & MCP Catalog"
Cohesion: 0.17
Nodes (16): assembleTools(), resolveTurn(), runProviderTurn(), baseCatalog(), catalogNames(), gestureTools, LEARNED_VIA, McpParam (+8 more)

### Community 40 - "TypeScript Compiler Config"
Cohesion: 0.09
Nodes (21): compilerOptions, esModuleInterop, forceConsistentCasingInFileNames, lib, module, moduleResolution, noEmit, noImplicitAny (+13 more)

### Community 41 - "WorkflowTeachSession"
Cohesion: 0.16
Nodes (10): EventHandler, CancellationToken, DllImport, int, IntPtr, string, Task, ValueTask (+2 more)

### Community 42 - "AutomationElement"
Cohesion: 0.13
Nodes (7): AutomationElementCollection, L, IReadOnlyList, AutomationElement, Condition, Func, PlanStep

### Community 44 - "SurfaceMapTools"
Cohesion: 0.09
Nodes (30): Aceptado no es ejecutado, La ubicación es el ancla (parámetro `at`), Cromo global detectado por repetición, EdgeInfo (ActionType / Kind), GraphCrawler, Identidad por sección cuando el título no distingue, Interrupcion (un diálogo no es un lugar), map_go_to (+22 more)

### Community 45 - "IUiSurface"
Cohesion: 0.14
Nodes (9): CancellationToken, double, int, Task, StepGate, StepGateResult, SurfaceReadiness, PlanStep (+1 more)

### Community 46 - "Tree Row Geometry Cache"
Cohesion: 0.14
Nodes (14): ConcurrentDictionary, Stopwatch, TreeRow, Box, double, int, IsFolder, List (+6 more)

### Community 47 - "LanzarConScroll"
Cohesion: 0.06
Nodes (28): Gancho, double, IEasingFunction, Window, EdgeSnap, bool, DispatcherTimer, DllImport (+20 more)

### Community 48 - "UI Inspector Hooks"
Cohesion: 0.21
Nodes (10): Dispatcher, DispatcherTimer, DllImport, HookProc, int, IntPtr, uint, MSLLHOOKSTRUCT (+2 more)

### Community 49 - "windows-client — el inspector visual"
Cohesion: 0.10
Nodes (23): Aprendizajes de método, 🧪 Ensayo en seco, Graph — el cerebro remoto, Guía del repo Ü Windows, Huella estructural por paso, LogBus — el log es la fuente de verdad, windows-client (cliente C#/WPF), Cajas por fila (+15 more)

### Community 50 - "Graph Health Status"
Cohesion: 0.16
Nodes (9): Dot, Brush, Text, GraphHealthText, object, TimeSpan, GraphHealth, GraphLink (+1 more)

### Community 51 - "GlobalHotkeys"
Cohesion: 0.16
Nodes (10): EventArgs, Dictionary, DllImport, HwndSource, int, IntPtr, List, uint (+2 more)

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

### Community 57 - "UIA Condition Selectors"
Cohesion: 0.18
Nodes (6): Condition, ControlType, Dictionary, IEnumerable, string, UiaSelector

### Community 59 - "Turn Endpoint & Session"
Cohesion: 0.23
Nodes (13): handler(), assertConfigured(), deps(), decodeSession(), encodeSession(), freshSession(), GeminiPending, GeminiState (+5 more)

### Community 60 - "TelemetryClient"
Cohesion: 0.19
Nodes (8): ConcurrentQueue, int, string, Task, Timer, TelemetryBus, TelemetryClient, TelemetryIdentity

### Community 61 - "Step Screenshot Capture"
Cohesion: 0.34
Nodes (4): DllImport, int, IntPtr, StepShotCamera

### Community 62 - ".ExitsFrom"
Cohesion: 0.23
Nodes (3): EdgeInfo, Hop, List

### Community 63 - "Inspector Diagnostics"
Cohesion: 0.21
Nodes (9): int, IReadOnlyList, Key, Rect, string, Text, UiElement, Via (+1 more)

### Community 64 - ".ReadTreeSelections"
Cohesion: 0.27
Nodes (7): Path, TreeId, DetectedField, IReadOnlyList, Key, Text, Via

### Community 65 - ".PumpMain"
Cohesion: 0.16
Nodes (3): Regex, string, SapSelector

### Community 66 - "AgentLoop (compuerta de origen)"
Cohesion: 0.11
Nodes (24): container.ts — puntos de extensión de persistencia, resolveTurn, Sesión firmada / backend stateless, application/engine.ts (orquesta un turno), brain/gemini.ts, learning/workflows.ts, domain/mcp.ts (catálogo MCP, solo declaración), memory/store.ts (+16 more)

### Community 67 - "CarruselDeApps"
Cohesion: 0.08
Nodes (21): SHFILEINFO, DllImport, ImageSource, int, IntPtr, IReadOnlyList, string, uint (+13 more)

### Community 68 - "Backend HTTP Client"
Cohesion: 0.24
Nodes (10): HttpContent, bool, CancellationToken, HttpClient, HttpResponseMessage, HttpStatusCode, JsonSerializerOptions, string (+2 more)

### Community 69 - "Highlight Overlay"
Cohesion: 0.23
Nodes (7): Rectangle, Canvas, EventArgs, int, IReadOnlyList, Rect, HighlightOverlay

### Community 70 - ".AsegurarFoco"
Cohesion: 0.21
Nodes (4): DllImport, EnumProc, IntPtr, StringBuilder

### Community 71 - "In-Memory Learning Store"
Cohesion: 0.18
Nodes (4): InMemoryLearningStore, Workflow, InMemoryMemoryStore, MemoryStore

### Community 72 - "GuiSession"
Cohesion: 0.16
Nodes (15): La carrera del Busy (pendiente), EndRequest (re-resolver el árbol), Enlace tardío siempre (SapROTWr.SapROTWrapper), session.FindById (rutas relativas), SapComEvents (enganche por introspección), SapSelector.Normalize, `session.Busy`, StartRequest (el instante más valioso) (+7 more)

### Community 73 - "App Startup & Config"
Cohesion: 0.14
Nodes (7): U.WindowsClient, StartupEventArgs, STAThread, Application, App, string, Config

### Community 74 - ".LeerInterrupcion"
Cohesion: 0.53
Nodes (4): List, Opciones, Textos, Titulo

### Community 75 - "SAP/UIA Hit Diagnostics"
Cohesion: 0.18
Nodes (3): IReadOnlyList, Rect, UiElement

### Community 76 - ".Str"
Cohesion: 0.20
Nodes (3): Dictionary, FieldOption, IEnumerable

### Community 77 - "Step Debugger Pause UI"
Cohesion: 0.19
Nodes (8): Image, Button, Task, TaskCompletionSource, TextBlock, StepDebuggerWindow, StepDecision, StepPause

### Community 78 - "Updater"
Cohesion: 0.27
Nodes (5): UpdateManager, VelopackAsset, Task, TimeSpan, Updater

### Community 79 - "Health & Video Config"
Cohesion: 0.30
Nodes (9): handler(), activeKey(), activeModel(), config, Provider, videoArchiveEnabled(), sanitize(), SignedVideoUpload (+1 more)

### Community 80 - "GuiTree — árboles de SAP"
Cohesion: 0.25
Nodes (8): Workflow NWP1 (admisión de paciente), CONTRASTE geometría (aserción viva), FindByPosition (no resuelve en este SAP), GuiTree — árboles de SAP, Identity() con subdynpro (sub y ssub), PressToolbarButton (#tbbtn=NV44), selectedItemNode (árbol de columnas), VisibleTreeRows (geometría por fila)

### Community 81 - "WindowsClient.csproj"
Cohesion: 0.16
Nodes (12): Microsoft.CSharp (4.7.0), NAudio (2.2.1), ScreenRecorderLib (6.6.0), System.Drawing.Common (8.0.7), System.Speech (8.0.0), Velopack (1.2.0), net8.0-windows, System.Text.Json (8.0.5) (+4 more)

### Community 82 - "VoiceIO"
Cohesion: 0.18
Nodes (8): IDisposable, SpeechSynthesizer, VoiceActivity, bool, CancellationToken, Task, VoiceActivity, VoiceIO

### Community 83 - "Log Window"
Cohesion: 0.23
Nodes (6): CopyBtn, List, Window, RoutedEventArgs, LogWindow, Button

### Community 84 - "Locator Badge Overlay"
Cohesion: 0.24
Nodes (6): DllImport, EventArgs, int, IntPtr, TextBlock, LocatorBadge

### Community 85 - "WorkflowDryRun.cs"
Cohesion: 0.33
Nodes (5): IEnumerable, DryRunFinding, DryRunLevel, DryRunReport, WorkflowDryRun

### Community 86 - "Log Bus"
Cohesion: 0.20
Nodes (7): bool, int, IReadOnlyList, List, object, string, LogBus

### Community 88 - "UIA Threading Architecture"
Cohesion: 0.24
Nodes (11): Arquitectura de tres hilos, AutomationId, CacheRequest (UIA), Correcciones a las premisas del encargo, GuiComboBox.Entries, LegacyIAccessiblePattern (COM-only), Mapeo conceptual DOM ↔ SAP ↔ UIA, SetWinEventHook / WinEvents (fallback) (+3 more)

### Community 89 - "Foreground Surface Detector"
Cohesion: 0.36
Nodes (4): DllImport, IntPtr, StringBuilder, SurfaceDetector

### Community 90 - ".UpdateChip"
Cohesion: 0.27
Nodes (3): TranslateTransform, FaceMood, UIElement

### Community 91 - "Fill Preview Dialog"
Cohesion: 0.20
Nodes (7): Window, Brush, Button, Task, TaskCompletionSource, UIElement, FillPreviewWindow

### Community 92 - "Button"
Cohesion: 0.07
Nodes (23): ExplorerBtn, InspectorBtn, LocatorBtn, LogsBtn, MapBtn, MenuActivator, MicBtn, MuteBtn (+15 more)

### Community 93 - "Senalador"
Cohesion: 0.42
Nodes (5): Caja, IReadOnlyList, Que, Rect, Senalador

### Community 94 - "Reglas de arquitectura del mapeo"
Cohesion: 0.06
Nodes (36): Aceptado no es ejecutado (principio transversal del proyecto), Antes de una acción destructiva, di sobre QUÉ actúa, «Atrás» es estado EFÍMERO de la sesión, no dato del mapa (idea del usuario), «Atrás» NO es una arista: es un gesto de historial, Capa 1 determinista; LLM en capa 2, Cruzar una puerta sin explorar se juzga por el CAMBIO, no por el destino, El banco de pruebas también tiene estado: no le tires el suelo a la app, El foco se recupera antes de actuar, y los paneles del shell se descartan con Escape (+28 more)

### Community 95 - "Pending Finish Queue"
Cohesion: 0.28
Nodes (6): Entry, CancellationToken, List, Task, Entry, PendingFinish

### Community 96 - ".SetDirectIndex"
Cohesion: 0.40
Nodes (3): SelectionChangedEventArgs, WorkflowListBox, ListBox

### Community 97 - "SAP Visual Elements"
Cohesion: 0.39
Nodes (3): SapBox, IReadOnlyList, SapVisualElement

### Community 98 - "Teach Session Contracts"
Cohesion: 0.25
Nodes (8): List, FileStateRequest, FileStateResponse, ProcessRequest, ProcessResult, TeachNote, UploadTokenRequest, UploadTokenResponse

### Community 99 - "Own Window Detection"
Cohesion: 0.28
Nodes (3): DllImport, IntPtr, Propio

### Community 100 - "Graphify — el grafo de decisiones"
Cohesion: 0.22
Nodes (11): Capa 1 — decisiones de desarrollo (determinista), Capa 2 — decisiones por app y por usuario (futura), El proceso es el ANFITRIÓN, no la app, Formato de una decisión, Graphify — el grafo de decisiones, Las dos capas, Navegación o contenido lo dice el CONTENEDOR, no dónde cae el elemento, No tienen «Atrás» con AutomationId estable (+3 more)

### Community 101 - "UI Tree Node Selection"
Cohesion: 0.25
Nodes (6): Key, Text, Via, Key, Text, Via

### Community 102 - "Vercel Function Config"
Cohesion: 0.29
Nodes (6): maxDuration, maxDuration, functions, api/teach/process-video.ts, api/**/*.ts, $schema

### Community 103 - ".EnsenarAsync"
Cohesion: 0.13
Nodes (15): Leccion, DllImport, int, IntPtr, RECT, Screenshotter, RECT, CancellationToken (+7 more)

### Community 104 - "Client Telemetry Events"
Cohesion: 0.33
Nodes (5): List, AckResponse, EventsPayload, RegisterPayload, TelemetryEvent

### Community 107 - "Local Dev Server"
Cohesion: 0.40
Nodes (3): dir, port, server

### Community 108 - "Agent Prompt Building"
Cohesion: 0.70
Nodes (4): goalPrompt(), learnedRule(), memoryBlock(), workflowRule()

### Community 109 - "MuelleEase"
Cohesion: 0.28
Nodes (3): IEasingFunction, MuelleEase, ThrowEase

### Community 110 - "Agent Turn Protocol"
Cohesion: 0.50
Nodes (4): List, ScreenState, TurnRequest, TurnResponse

### Community 111 - "PasoAPaso"
Cohesion: 0.18
Nodes (10): Exception, Paso, Ventana, Visto, IReadOnlyList, List, Abandonado, PasoAPaso (+2 more)

### Community 114 - "Clinical Values API Bridge"
Cohesion: 1.00
Nodes (3): GET /api/agent/values (emparejamiento por código), Conceptos canónicos (vital.talla, vital.peso…), Puente con el portal clínico

### Community 115 - "Win32 Input Structs"
Cohesion: 0.67
Nodes (3): KEYBDINPUT, MOUSEINPUT, InputUnion

### Community 116 - "PlanStep"
Cohesion: 0.23
Nodes (5): RelX, RelY, string, PlanStep, SurfaceIdentity

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
Nodes (13): MouseButtonEventArgs, bool, DispatcherTimer, DllImport, double, EventArgs, int, long (+5 more)

### Community 126 - "windows-graph — SAP GUI Scripting: hechos duros"
Cohesion: 0.11
Nodes (21): SAP GUI Scripting (COM), UIA — superficie genérica de Windows, windows-graph (workflows sobre SAP GUI), UiaReader (CollectMenus, CollectFromChildren), Fase 4 · La tarea aprendida (workflows), UiaReader.Read(), Aceptado ≠ ejecutado, El snapshot de campos pertenece a UNA pantalla (+13 more)

### Community 127 - "GraphExplorerWindow"
Cohesion: 0.11
Nodes (16): ScrollViewer, bool, Border, Button, CancellationTokenSource, DateTime, Dictionary, DispatcherTimer (+8 more)

### Community 128 - "Graphify en el equipo"
Cohesion: 0.11
Nodes (18): 1. Instalar uv, 2. Instalar graphify, 3. Registrar la skill y los hooks, 4. Traer el grafo, Actualizar el grafo a mano, Camino entre dos partes del sistema, Comandos útiles, Encontrar los archivos más conectados (los críticos) (+10 more)

### Community 130 - "LiveAudio"
Cohesion: 0.14
Nodes (8): BufferedWaveProvider, WaveInEvent, WaveOutEvent, DateTime, double, int, object, LiveAudio

### Community 131 - "2. Windows UI Automation (UIA)"
Cohesion: 0.12
Nodes (17): 2.1 Managed vs COM — el argumento correcto, 2.2 Árbol, TreeWalker y condiciones, 2.3 Caching — obligatorio para un grabador, 2.4 Identificación estable, 2.5 Patterns, 2.6 GRABACIÓN — eventos y threading, 2.7 Lo que UIA **no** puede hacer, 2.8 Detectar ventanas de SAP GUI (+9 more)

### Community 132 - "RastroDelCursor"
Cohesion: 0.13
Nodes (11): Marca, int, IReadOnlyList, List, object, POINT, Timer, TimeSpan (+3 more)

### Community 133 - "Ü · Backend (el cerebro)"
Cohesion: 0.15
Nodes (15): Arquitectura — la separación cliente / cerebro, Dónde se corta el bucle, El problema que resuelve (heredado de Android), ExecutionEngine.run (core Android), Mapa al core Kotlin, Por qué el cliente conduce el bucle (y no el servidor), Puntos de extensión (fases siguientes), Arquitectura (+7 more)

### Community 134 - ".EjecutarNucleoAsync"
Cohesion: 0.23
Nodes (4): CancellationToken, IReadOnlyDictionary, JsonElement, Task

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

### Community 139 - ".EnsenarLaAppAsync"
Cohesion: 0.25
Nodes (4): AppInstalada, IReadOnlyDictionary, Task, Task

### Community 140 - "1.7 GRABACIÓN — el modelo de eventos (lo crítico)"
Cohesion: 0.18
Nodes (11): 1.7.1 Diseño del modelo, según SAP **[SAP-DOC]**, 1.7.2 El interruptor: `GuiSession.Record` **[SAP-DOC]**, 1.7.3 Eventos de `GuiSession` **[SAP-DOC]** — firmas literales, 1.7.4 Eventos de `GuiApplication` **[SAP-DOC]**, 1.7.5 Qué se captura en vivo desde un proceso externo, y qué NO, 1.7.6 STA, apartments y el pump — **la trampa de ingeniería nº1**, 1.7 GRABACIÓN — el modelo de eventos (lo crítico), `Change` — el corazón del grabador (+3 more)

### Community 141 - "Ü · Cliente Windows (el frontend tonto)"
Cohesion: 0.24
Nodes (10): macos-client (U.app), backend/src/domain/actions.ts (el contrato), Compilar y ejecutar (Windows 10/11), Cómo la lectura del árbol usa UIA, Estructura, Qué hace (y qué NO), VoiceIO (TTS/STT), Ü · Cliente Windows (el frontend tonto) (+2 more)

### Community 142 - "VoiceDot"
Cohesion: 0.24
Nodes (6): BackendZone, CollapsedGroup, ContextZone, VoiceDot, MouseEventArgs, StackPanel

### Community 143 - "src/Update/Updater.cs"
Cohesion: 0.25
Nodes (9): Paso «Subir a Supabase (compatible con S3)», Backend en Vercel (Fase 1), CLIENT_TOKEN — candado del endpoint, windows-client/src/Config.cs, Bucket público `windows` (Supabase miracle-app), Ui/FaceWindow.xaml (UpdateBtn), releases.win.json (el índice del feed), src/Update/Updater.cs (+1 more)

### Community 144 - "Ventana"
Cohesion: 0.25
Nodes (6): Button, Color, TaskCompletionSource, TextBlock, TextBox, Ventana

### Community 145 - "Captura de texto tecleado"
Cohesion: 0.29
Nodes (8): Captura de texto tecleado, Enfoque híbrido UIA + WinEvents/Raw Input, EVENT_OBJECT_VALUECHANGE, Evento Change de SAP GUI Scripting (batched), Raw Input (captura de tecleo), SAP GUI Scripting API — spec completa (release 620), SetWinEventHook y Event Constants, WinEvents (hooks de accesibilidad Win32)

### Community 146 - "Reconocedor"
Cohesion: 0.38
Nodes (3): IReadOnlyList, UiElement, Reconocedor

### Community 147 - "Ü — Windows App"
Cohesion: 0.33
Nodes (5): Backend, Build, Estructura, Historial, Ü — Windows App

### Community 148 - "InstalledApps"
Cohesion: 0.40
Nodes (3): IEnumerable, string, InstalledApps

### Community 149 - "Árboles (`GuiTree`)"
Cohesion: 0.33
Nodes (6): Contar filas: `col.Count`, no `ElementAt` por clave, La geometría por fila SÍ existe: los getters piden `(clave, columna)`, La selección de un árbol de COLUMNAS vive en `selectedItemNode`, Posiciones repetidas = centinela, Una fila no tiene id propio, Árboles (`GuiTree`)

### Community 151 - "POINT"
Cohesion: 0.40
Nodes (4): uint, MSLLHOOKSTRUCT, POINT, DllImport

### Community 152 - ".Marcados"
Cohesion: 0.50
Nodes (3): Caja, IReadOnlyList, Que

### Community 153 - "3. Mapeo conceptual DOM ↔ SAP GUI Scripting ↔ UIA"
Cohesion: 0.50
Nodes (4): 3.1 Tabla maestra, 3.2 Qué es el "selector" en cada superficie, 3.3 Clasificación de `actionType`, 3. Mapeo conceptual DOM ↔ SAP GUI Scripting ↔ UIA

### Community 154 - "Grabar y ejecutar acciones sobre formularios en Windows"
Cohesion: 0.67
Nodes (3): Convención de marcado de evidencia, Grabar y ejecutar acciones sobre formularios en Windows, SAP GUI Scripting y UI Automation, desde una app WPF/.NET (C#)

## Ambiguous Edges - Review These
- `UiaSurface` → `IsStepReady (compuerta al primer plano)`  [AMBIGUOUS]
  windows-client/CLAUDE.md · relation: calls
- `Entries — opciones de combo en SAP` → `UIA — Control Patterns (Value, Toggle, Selection, ComboBox, Edit)`  [AMBIGUOUS]
  windows-graph/INVESTIGACION-SAPGUI-UIA.md · relation: conceptually_related_to
- `Fiabilidad de eventos (controles Win32 no siempre notifican)` → `Issues dotnet/winforms#7763 y dotnet/wpf#9382`  [AMBIGUOUS]
  windows-graph/INVESTIGACION-SAPGUI-UIA.md · relation: conceptually_related_to

## Knowledge Gaps
- **346 isolated node(s):** `dir`, `server`, `port`, `name`, `version` (+341 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **11 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **What is the exact relationship between `UiaSurface` and `IsStepReady (compuerta al primer plano)`?**
  _Edge tagged AMBIGUOUS (relation: calls) - confidence is low._
- **What is the exact relationship between `Entries — opciones de combo en SAP` and `UIA — Control Patterns (Value, Toggle, Selection, ComboBox, Edit)`?**
  _Edge tagged AMBIGUOUS (relation: conceptually_related_to) - confidence is low._
- **What is the exact relationship between `Fiabilidad de eventos (controles Win32 no siempre notifican)` and `Issues dotnet/winforms#7763 y dotnet/wpf#9382`?**
  _Edge tagged AMBIGUOUS (relation: conceptually_related_to) - confidence is low._
- **Why does `FaceWindow` connect `FaceWindow` to `Window`, `TeachSession`, `ClickWatcher`, `SurfaceMap`, `U.WindowsClient.Diagnostics`, `SurfaceLocator`, `.SetStatus`, `UiaReader`, `VoiceDot`, `Graph Backend HTTP Client`, `SapGuiSurface`, `UiaSurface`, `Graph Node Visualization`, `WorkflowLibraryWindow`, `Agent Execution Loop`, `.OnLoaded`, `WorkflowTeachSession`, `.WireMenu`, `UI Inspector Hooks`, `Graph Health Status`, `GlobalHotkeys`, `Backend HTTP Client`, `Step Debugger Pause UI`, `Updater`, `VoiceIO`, `Log Window`, `Locator Badge Overlay`, `.UpdateChip`, `Fill Preview Dialog`, `Button`, `.SetDirectIndex`, `PanelDeAcciones`, `GeminiLive`, `GraphExplorerWindow`?**
  _High betweenness centrality (0.248) - this node is a cross-community bridge._
- **Why does `UiaSurface` connect `UiaSurface` to `GraphCrawler`, `.GetForegroundWindow`, `FaceWindow`, `U.WindowsClient.Diagnostics`, `SurfaceMapTools`, `AutomationElement`, `IUiSurface`, `UIA Condition Selectors`, `.Describe`, `.SendKey`, `WorkflowLibraryWindow`, `IntPtr`, `WorkflowRecorder`, `Agent Execution Loop`, `GraphExplorerWindow`?**
  _High betweenness centrality (0.089) - this node is a cross-community bridge._
- **Why does `SapGuiSurface` connect `SapGuiSurface` to `.ReadTreeSelections`, `.PumpMain`, `SAP Tree & Grid Reading`, `.Session`, `FaceWindow`, `U.WindowsClient.Diagnostics`, `SurfaceLocator`, `.Str`, `IUiSurface`, `Tree Row Geometry Cache`, `SAP COM Event Binding`, `WorkflowLibraryWindow`, `Agent Execution Loop`?**
  _High betweenness centrality (0.079) - this node is a cross-community bridge._
- **What connects `dir`, `server`, `port` to the rest of the system?**
  _346 weakly-connected nodes found - possible documentation gaps or missing edges._