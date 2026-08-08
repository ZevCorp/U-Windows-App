# Graph Report - C:\Users\Jose David Jaramillo\Documents\U-windows  (2026-08-08)

## Corpus Check
- cluster-only mode — file stats not available

## Summary
- 3400 nodes · 7197 edges · 197 communities (155 shown, 42 thin omitted)
- Extraction: 96% EXTRACTED · 4% INFERRED · 0% AMBIGUOUS · INFERRED: 302 edges (avg confidence: 0.8)
- Token cost: 372,514 input · 2,877 output

## Community Hubs (Navigation)
- UI Element Enumeration
- Live Screen Video Capture
- UI Automation Location Lookup
- Audio and Screen Recording
- Global Click Hook Watcher
- System Gesture Simulation
- Windows App Core Services
- Surface Map Element Model
- Client Backend & Teach Modules
- Surface Map Tooling
- Animated Face Control
- Window Alignment Interop
- Face Window UI Elements
- UI Element Tree Discovery
- SAP GUI UIA Investigation
- UI Field Detection
- Graph Explorer Window
- Workflow Playback Alignment
- Element Inspector Overlay
- Graph API HTTP Client
- SAP GUI Surface Reader
- Window Focus Strategy
- COM Type Introspection
- UI Automation Surface
- Graph Node Visualization
- Main Window Command Handlers
- Win32 Window API Bindings
- OpenAI API Client
- Workflow Recording & Observation
- Agent Execution Loop
- Backend HTTP Endpoint Handlers
- Windows Input Simulation
- UI Element Fingerprints
- Autofill API Contracts
- Clinical Note UI Controls
- SAP Tree/Grid Traversal
- SAP Step Execution
- Vercel Deployment Setup
- Backend Package Config
- Prompt & Tool Assembly
- TypeScript Compiler Config
- Workflow Teaching Session
- Action Plan Execution
- Screen Capture Utility
- Surface Map Crawler
- Avatar Face Animation
- Tree Row Geometry Cache
- Scroll Launcher Overlay
- Low-Level Mouse Hook
- Windows Client Repo Guide
- Graph Health Indicator
- Collapsed Pill Hover UI
- Gemini API Client
- Onboarding Window UI
- Clinical Concept Binding
- Windows Release Pipeline
- Global Hotkey Registration
- UIA Selector Builder
- UI Automation Event Capture
- Turn Session Handling
- Telemetry Bus Client
- Windows Screenshot Interop
- Surface Map Node Graph
- UI Element Inspection
- Tree Node Selection Reading
- Backend Turn Orchestration
- Installed Apps Enumeration
- Backend HTTP Client
- Screen Highlight Overlay
- Window Focus Management
- In-Memory Learning Store
- SAP Scripting Timing Notes
- Windows Client Config
- File Explorer Navigation
- SAP/UIA Diagnostics
- Field Description Helpers
- Step Debugger Window
- Backend Config and Health
- SAP GUI Tree Workflow Notes
- Project Dependencies & Build
- Visual Zone Detection
- Log Window UI
- Locator Badge Overlay
- Workflow Dry Run
- Windows Client Architecture Contracts
- Surface Path Normalization
- UIA/SAP Automation Design
- Surface & Autofill Detection
- CI Scenario Verdicts
- Fill Preview Window UI
- UI Graph Visualization
- Visual Element Highlighting
- Navigation Design Principles
- Pending Task Persistence Queue
- Screen Contract Principles
- SAP Visual Element Reading
- Teach Session Request Models
- Field Detection & Hit Testing
- Graphify Decision Rules
- Tree Selection After Click
- Vercel API Deployment Config
- Roadmap: Control por Grafo
- App Update & Learning Manager
- Voice Input Output
- Run Timing Metrics
- Local Dev Server
- Desktop Window Focus
- Animation Easing Functions
- Log Bus
- Graph Client Stepping
- User Path Resolution
- App Teaching Orchestrator
- Clinical Values API
- Win32 Input Structs
- Client Agent & Actions Modules
- UI Automation Heuristics Rules
- Action Panel UI
- Step-by-Step Screenshot Camera
- WebSocket Client State
- Production Deployment Guide
- Face Gesture Control
- SAP GUI Automation Research
- Brain Turn Engine
- Graphify Setup Guide
- Menu and Option Learning
- Live Microphone Audio
- UI Automation Research Notes
- Cursor Trail Tracking
- Client/Brain Architecture Docs
- Tool Execution Pipeline
- SAP GUI Scripting Guide
- Project Architecture Docs
- Windows Explorer Rules
- SAP Scripting and UIA
- SAP Event Recording Model
- Client-Backend Architecture Docs
- Win32 Window Ownership Checks
- Windows Client Auto-Update Release
- Clinical Bridge Pairing
- Keystroke Capture Event Hooks
- UI Element Recognizer
- Project README Docs
- Installed Apps Discovery
- SAP GUI Table Notes
- Workflow List Navigation UI
- Dialog Prompt Window
- Async Session Client
- DOM UIA Selector Mapping
- Windows Form Automation Spec
- Architect Agent Package Config
- Architect Agent Tooling
- WPF App Startup
- Mouse Hook Interop
- Build Versioning Script
- Graph Contract Project
- Step Readiness Gating
- Key Spec Parsing
- Win32 Geometry Structs
- Live Debug Console
- Rationale Notes
- Voice And Workflow Runner
- JSON Parsing Helpers
- Learned Tap Playback
- Turn Protocol Models
- Keyboard Input Handling
- Rectangle Struct Types
- Action Delegates
- Cancellation Tokens
- Process Handling
- Rectangle Geometry
- Return Statements
- Async Tasks
- Alternative Options
- Graph Edges
- Click Events
- UI Control Types
- Edge Source Node
- Node Grouping
- Graph Hop Traversal
- Node Info Metadata
- Node Labels
- Graph Nodes
- UI Element Selectors
- Edge Target Node
- Mouse Button Events
- Mouse Event Handling
- WPF Routed Events
- Solid Color Brushes
- UI Thread Dispatcher
- String Building

## God Nodes (most connected - your core abstractions)
1. `FaceWindow` - 174 edges
2. `UiaSurface` - 123 edges
3. `SapGuiSurface` - 109 edges
4. `Window` - 83 edges
5. `SurfaceMapTools` - 82 edges
6. `GraphExplorerWindow` - 76 edges
7. `SurfaceMap` - 73 edges
8. `SurfaceMap` - 58 edges
9. `SurfaceMap` - 55 edges
10. `U.WindowsClient.Diagnostics` - 45 edges

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

## Communities (197 total, 42 thin omitted)

### Community 0 - "UI Element Enumeration"
Cohesion: 0.06
Nodes (34): Alternativas, Entrable, Frente, Queue, Tipo, CancellationToken, DllImport, EnumProc (+26 more)

### Community 1 - "Live Screen Video Capture"
Cohesion: 0.14
Nodes (15): CURSORINFO, Graphics, ICONINFO, bool, DllImport, int, IntPtr, long (+7 more)

### Community 2 - "UI Automation Location Lookup"
Cohesion: 0.06
Nodes (28): Dispatcher, StringBuilder, SurfaceLocation, Uri, AutomationElement, bool, Dictionary, DllImport (+20 more)

### Community 3 - "Audio and Screen Recording"
Cohesion: 0.06
Nodes (33): AudioDevice, IAsyncDisposable, Recorder, CancellationToken, IReadOnlyList, string, Task, ValueTask (+25 more)

### Community 4 - "Global Click Hook Watcher"
Cohesion: 0.21
Nodes (9): Click, DllImport, HashSet, HookProc, IntPtr, object, StringBuilder, Click (+1 more)

### Community 5 - "System Gesture Simulation"
Cohesion: 0.07
Nodes (18): HttpListener, byte, DllImport, uint, UIntPtr, Gestures, HashSet, IReadOnlyDictionary (+10 more)

### Community 6 - "Windows App Core Services"
Cohesion: 0.03
Nodes (49): AgentLoop, AtajoPorGolpes, BackendClient, ClinicalBridge, Config, double, FaceTheme, GeminiLive (+41 more)

### Community 7 - "Surface Map Element Model"
Cohesion: 0.06
Nodes (33): Alternatives, App, Aristas, Click, ControlType, Cuantas, DeHumano, Etiqueta (+25 more)

### Community 8 - "Client Backend & Teach Modules"
Cohesion: 0.13
Nodes (5): U.WindowsClient.Backend, U.WindowsClient.Ui, U.WindowsClient.Domain, U.WindowsClient.Teach, U.Graph

### Community 9 - "Surface Map Tooling"
Cohesion: 0.12
Nodes (5): Stack, bool, IReadOnlyDictionary, string, SurfaceMapTools

### Community 10 - "Animated Face Control"
Cohesion: 0.08
Nodes (20): DependencyProperty, DoubleAnimationUsingKeyFrames, DrawingContext, FacePose, FrameworkElement, Geometry, Random, RotateTransform (+12 more)

### Community 11 - "Window Alignment Interop"
Cohesion: 0.15
Nodes (12): CancellationToken, DllImport, EnumWindowsProc, Func, int, IntPtr, StringBuilder, Task (+4 more)

### Community 12 - "Face Window UI Elements"
Cohesion: 0.04
Nodes (88): ActivatorChevron, ActivatorRot, AprendizajeBtn, AutonomoBtn, BackendBody, BackendChevron, BackendDot, BackendHeader (+80 more)

### Community 13 - "UI Element Tree Discovery"
Cohesion: 0.16
Nodes (14): UiElement, AutomationElement, AutomationElementInformation, ControlType, DllImport, EnumWindowsProc, HashSet, int (+6 more)

### Community 14 - "SAP GUI UIA Investigation"
Cohesion: 0.12
Nodes (18): 4. Correcciones a las premisas del encargo, 5. Arquitectura que se deduce de los hechos, 6. Fuentes, SAP Community Q&A (sesión C#, SessionEvents, 7.40→7.70, Excel 64-bit), Dónde falla cada superficie, FlaUI (wrapper .NET de UIA), IUIAutomation (API COM), LowLevelKeyboardProc (+10 more)

### Community 15 - "UI Field Detection"
Cohesion: 0.13
Nodes (8): Selectors, AutomationElementInformation, ControlType, DetectedField, FieldOption, IReadOnlyList, Label, List

### Community 16 - "Graph Explorer Window"
Cohesion: 0.08
Nodes (22): Border, CarruselDeApps, Func, ScrollViewer, StackPanel, TextBlock, bool, Button (+14 more)

### Community 17 - "Workflow Playback Alignment"
Cohesion: 0.13
Nodes (15): RelX, RelY, SurfaceAligner, string, PlanStep, SurfaceIdentity, CancellationToken, Dictionary (+7 more)

### Community 18 - "Element Inspector Overlay"
Cohesion: 0.12
Nodes (16): caption, mapped, Matrix, shell, box, Brush, DllImport, EventArgs (+8 more)

### Community 19 - "Graph API HTTP Client"
Cohesion: 0.17
Nodes (12): HttpRequestMessage, CancellationToken, Dictionary, Func, HttpClient, HttpResponseMessage, HttpStatusCode, JsonElement (+4 more)

### Community 20 - "SAP GUI Surface Reader"
Cohesion: 0.11
Nodes (18): LowLevelMouseProc, ManualResetEventSlim, Thread, SapContextReader, bool, DateTime, Dispatcher, DispatcherTimer (+10 more)

### Community 21 - "Window Focus Strategy"
Cohesion: 0.11
Nodes (20): byte, CancellationToken, DllImport, int, IntPtr, Task, TimeSpan, uint (+12 more)

### Community 22 - "COM Type Introspection"
Cohesion: 0.07
Nodes (25): Arity, Delegate, DispId, Guid, ITypeInfo, Name, TYPEATTR, bool (+17 more)

### Community 23 - "UI Automation Surface"
Cohesion: 0.09
Nodes (19): AutomationElementCollection, AutomationPropertyChangedEventHandler, bool, byte, Condition, Dictionary, HashSet, HookProc (+11 more)

### Community 24 - "Graph Node Visualization"
Cohesion: 0.11
Nodes (18): Node, Border, CancellationToken, Dictionary, DllImport, EventArgs, HashSet, int (+10 more)

### Community 26 - "Win32 Window API Bindings"
Cohesion: 0.15
Nodes (4): DllImport, IntPtr, RECT, StringBuilder

### Community 27 - "OpenAI API Client"
Cohesion: 0.24
Nodes (15): asArr(), asObj(), asStr(), customFn(), dataUri(), extractMessage(), functionOutput(), mapKey() (+7 more)

### Community 28 - "Workflow Recording & Observation"
Cohesion: 0.12
Nodes (13): Channel, FinishResponse, ObservedStep, SurfaceAvailability, CancellationToken, CancellationTokenSource, Dictionary, int (+5 more)

### Community 29 - "Agent Execution Loop"
Cohesion: 0.21
Nodes (9): CancellationToken, Func, HashSet, int, Task, AgentLoop, IUserChannel, Dictionary (+1 more)

### Community 30 - "Backend HTTP Endpoint Handlers"
Cohesion: 0.15
Nodes (20): handler(), handler(), handler(), checkAuth(), FileStateBody, guard(), handleFileState(), handleProcessVideo() (+12 more)

### Community 31 - "Windows Input Simulation"
Cohesion: 0.16
Nodes (11): INPUT, InputUnion, ushort, DllImport, int, IntPtr, uint, INPUT (+3 more)

### Community 33 - "Autofill API Contracts"
Cohesion: 0.15
Nodes (23): AutofillRow, Dictionary, JsonElement, List, AutofillRequest, AutofillResponse, AutofillResult, ContextNote (+15 more)

### Community 34 - "Clinical Note UI Controls"
Cohesion: 0.08
Nodes (25): ConfidenceLabel, Label, Value, TextChangedEventArgs, ClinicalCodeBox, AutofillStatus, ConnStatus, ForceSurface (+17 more)

### Community 36 - "SAP Step Execution"
Cohesion: 0.15
Nodes (5): KeyValuePair, PlanStep, Regex, string, SapSelector

### Community 37 - "Vercel Deployment Setup"
Cohesion: 0.17
Nodes (13): Apuntar el cliente, Cambiar de proveedor (OpenAI), Comprobar, Desplegar el backend en Vercel, Variables de entorno del backend, Opción A — CLI de Vercel (rápida, recomendada), Opción B — Importar el repo en vercel.com, GET /api/health (+5 more)

### Community 38 - "Backend Package Config"
Cohesion: 0.09
Nodes (21): dependencies, description, devDependencies, tsx, typescript, @vercel/node, engines, node (+13 more)

### Community 39 - "Prompt & Tool Assembly"
Cohesion: 0.14
Nodes (18): assembleTools(), goalPrompt(), learnedRule(), memoryBlock(), workflowRule(), baseCatalog(), catalogNames(), gestureTools (+10 more)

### Community 40 - "TypeScript Compiler Config"
Cohesion: 0.09
Nodes (21): compilerOptions, esModuleInterop, forceConsistentCasingInFileNames, lib, module, moduleResolution, noEmit, noImplicitAny (+13 more)

### Community 41 - "Workflow Teaching Session"
Cohesion: 0.09
Nodes (19): EventHandler, ValueTask, CancellationToken, DllImport, int, IntPtr, string, Task (+11 more)

### Community 42 - "Action Plan Execution"
Cohesion: 0.12
Nodes (6): L, T, IReadOnlyList, AutomationElement, Func, PlanStep

### Community 43 - "Screen Capture Utility"
Cohesion: 0.27
Nodes (6): DllImport, int, IntPtr, RECT, Screenshotter, RECT

### Community 44 - "Surface Map Crawler"
Cohesion: 0.16
Nodes (17): Aceptado no es ejecutado, Cromo global detectado por repetición, EdgeInfo (ActionType / Kind), GraphCrawler, Identidad por sección cuando el título no distingue, Interrupcion (un diálogo no es un lugar), map_go_to, map_unblock (+9 more)

### Community 45 - "Avatar Face Animation"
Cohesion: 0.16
Nodes (5): FaceMood, MouseButtonEventArgs, TranslateTransform, Action, UIElement

### Community 46 - "Tree Row Geometry Cache"
Cohesion: 0.14
Nodes (14): ConcurrentDictionary, Stopwatch, TreeRow, Box, double, int, IsFolder, List (+6 more)

### Community 47 - "Scroll Launcher Overlay"
Cohesion: 0.07
Nodes (27): bool, DispatcherTimer, DllImport, double, EventArgs, Func, Gancho, HwndSource (+19 more)

### Community 48 - "Low-Level Mouse Hook"
Cohesion: 0.21
Nodes (10): Dispatcher, DispatcherTimer, DllImport, HookProc, int, IntPtr, uint, MSLLHOOKSTRUCT (+2 more)

### Community 49 - "Windows Client Repo Guide"
Cohesion: 0.10
Nodes (23): Aprendizajes de método, 🧪 Ensayo en seco, Graph — el cerebro remoto, Guía del repo Ü Windows, Huella estructural por paso, LogBus — el log es la fuente de verdad, windows-client (cliente C#/WPF), Cajas por fila (+15 more)

### Community 50 - "Graph Health Indicator"
Cohesion: 0.18
Nodes (9): Dot, Brush, Text, GraphHealthText, object, TimeSpan, GraphHealth, GraphLink (+1 more)

### Community 51 - "Collapsed Pill Hover UI"
Cohesion: 0.18
Nodes (8): MouseEventArgs, SolidColorBrush, CollapsedGroup, RootPanel, Rectangle, ZonaChat, ZonaVoz, Grid

### Community 52 - "Gemini API Client"
Cohesion: 0.25
Nodes (15): asArr(), asInt(), asObj(), asStr(), builtinFns(), COMPUTER_FNS, fn(), gemHttp() (+7 more)

### Community 53 - "Onboarding Window UI"
Cohesion: 0.12
Nodes (10): ControlTemplate, Border, Brush, Button, Regex, RoutedEventArgs, TextBlock, TextBox (+2 more)

### Community 54 - "Clinical Concept Binding"
Cohesion: 0.18
Nodes (10): U.WindowsClient.Clinical, Field, ClinicalValue, HashSet, IReadOnlyList, Key, List, string (+2 more)

### Community 55 - "Windows Release Pipeline"
Cohesion: 0.14
Nodes (17): Workflow CI «Windows release», GRAPH_DEFAULT_API_KEY (secreto embebido en el build), Firma de código / SmartScreen, scripts/publish-release.ps1, Runbook de producción, README — Ü Windows App, 1. Cómo funciona (resumen), 2. Infraestructura (ya creada, no hay que volver a hacerla) (+9 more)

### Community 56 - "Global Hotkey Registration"
Cohesion: 0.16
Nodes (10): EventArgs, Dictionary, DllImport, HwndSource, int, IntPtr, List, uint (+2 more)

### Community 57 - "UIA Selector Builder"
Cohesion: 0.19
Nodes (6): Condition, ControlType, Dictionary, IEnumerable, string, UiaSelector

### Community 59 - "Turn Session Handling"
Cohesion: 0.24
Nodes (13): handler(), resolveTurn(), assertConfigured(), deps(), decodeSession(), encodeSession(), freshSession(), GeminiPending (+5 more)

### Community 60 - "Telemetry Bus Client"
Cohesion: 0.16
Nodes (9): ConcurrentQueue, IDisposable, int, string, Task, Timer, TelemetryBus, TelemetryClient (+1 more)

### Community 61 - "Windows Screenshot Interop"
Cohesion: 0.34
Nodes (4): DllImport, int, IntPtr, StepShotCamera

### Community 62 - "Surface Map Node Graph"
Cohesion: 0.06
Nodes (33): EdgeInfo, NodeInfo, Alternatives, App, Aristas, Click, ControlType, Cuantas (+25 more)

### Community 63 - "UI Element Inspection"
Cohesion: 0.21
Nodes (9): int, IReadOnlyList, Key, Rect, string, Text, UiElement, Via (+1 more)

### Community 64 - "Tree Node Selection Reading"
Cohesion: 0.39
Nodes (5): Path, TreeId, Key, Text, Via

### Community 66 - "Backend Turn Orchestration"
Cohesion: 0.11
Nodes (24): container.ts — puntos de extensión de persistencia, resolveTurn, Sesión firmada / backend stateless, application/engine.ts (orquesta un turno), brain/gemini.ts, learning/workflows.ts, domain/mcp.ts (catálogo MCP, solo declaración), memory/store.ts (+16 more)

### Community 67 - "Installed Apps Enumeration"
Cohesion: 0.08
Nodes (20): SHFILEINFO, DllImport, ImageSource, int, IntPtr, IReadOnlyList, string, uint (+12 more)

### Community 68 - "Backend HTTP Client"
Cohesion: 0.24
Nodes (10): HttpContent, bool, CancellationToken, HttpClient, HttpResponseMessage, HttpStatusCode, JsonSerializerOptions, string (+2 more)

### Community 69 - "Screen Highlight Overlay"
Cohesion: 0.23
Nodes (7): Canvas, EventArgs, int, IReadOnlyList, Rect, Rectangle, HighlightOverlay

### Community 70 - "Window Focus Management"
Cohesion: 0.20
Nodes (4): DllImport, EnumProc, IntPtr, StringBuilder

### Community 71 - "In-Memory Learning Store"
Cohesion: 0.18
Nodes (4): InMemoryLearningStore, Workflow, InMemoryMemoryStore, MemoryStore

### Community 72 - "SAP Scripting Timing Notes"
Cohesion: 0.16
Nodes (15): La carrera del Busy (pendiente), EndRequest (re-resolver el árbol), Enlace tardío siempre (SapROTWr.SapROTWrapper), session.FindById (rutas relativas), SapComEvents (enganche por introspección), SapSelector.Normalize, `session.Busy`, StartRequest (el instante más valioso) (+7 more)

### Community 73 - "Windows Client Config"
Cohesion: 0.33
Nodes (3): U.WindowsClient, string, Config

### Community 74 - "File Explorer Navigation"
Cohesion: 0.20
Nodes (6): DllImport, int, IntPtr, IReadOnlyList, Entrada, Explorador

### Community 75 - "SAP/UIA Diagnostics"
Cohesion: 0.18
Nodes (3): IReadOnlyList, Rect, UiElement

### Community 77 - "Step Debugger Window"
Cohesion: 0.19
Nodes (8): Image, Button, Task, TaskCompletionSource, TextBlock, StepDebuggerWindow, StepDecision, StepPause

### Community 79 - "Backend Config and Health"
Cohesion: 0.30
Nodes (9): handler(), activeKey(), activeModel(), config, Provider, videoArchiveEnabled(), sanitize(), SignedVideoUpload (+1 more)

### Community 80 - "SAP GUI Tree Workflow Notes"
Cohesion: 0.25
Nodes (8): Workflow NWP1 (admisión de paciente), CONTRASTE geometría (aserción viva), FindByPosition (no resuelve en este SAP), GuiTree — árboles de SAP, Identity() con subdynpro (sub y ssub), PressToolbarButton (#tbbtn=NV44), selectedItemNode (árbol de columnas), VisibleTreeRows (geometría por fila)

### Community 81 - "Project Dependencies & Build"
Cohesion: 0.14
Nodes (12): Microsoft.CSharp (4.7.0), NAudio (2.2.1), ScreenRecorderLib (6.6.0), System.Drawing.Common (8.0.7), System.Speech (8.0.0), Velopack (1.2.0), net8.0-windows, System.Text.Json (8.0.5) (+4 more)

### Community 82 - "Visual Zone Detection"
Cohesion: 0.17
Nodes (5): Caja, Func, IReadOnlyList, Point, Que

### Community 83 - "Log Window UI"
Cohesion: 0.21
Nodes (7): CopyBtn, List, Window, RoutedEventArgs, LogWindow, Button, ListBox

### Community 84 - "Locator Badge Overlay"
Cohesion: 0.24
Nodes (6): DllImport, EventArgs, int, IntPtr, TextBlock, LocatorBadge

### Community 85 - "Workflow Dry Run"
Cohesion: 0.24
Nodes (6): IEnumerable, IReadOnlyList, DryRunFinding, DryRunLevel, DryRunReport, WorkflowDryRun

### Community 86 - "Windows Client Architecture Contracts"
Cohesion: 0.13
Nodes (6): U.WindowsClient.Uia, U.Graph.Surfaces, U.WindowsClient.Navigation, U.WindowsClient.Diagnostics, U.WindowsClient.SystemApi, ContratoDelGrafo

### Community 88 - "UIA/SAP Automation Design"
Cohesion: 0.24
Nodes (11): Arquitectura de tres hilos, AutomationId, CacheRequest (UIA), Correcciones a las premisas del encargo, GuiComboBox.Entries, LegacyIAccessiblePattern (COM-only), Mapeo conceptual DOM ↔ SAP ↔ UIA, SetWinEventHook / WinEvents (fallback) (+3 more)

### Community 89 - "Surface & Autofill Detection"
Cohesion: 0.23
Nodes (6): DetectedField, IReadOnlyList, DllImport, IntPtr, StringBuilder, SurfaceDetector

### Community 90 - "CI Scenario Verdicts"
Cohesion: 0.11
Nodes (15): Detalle, Escenario, Version, IReadOnlyList, Ok, Escenario, EscenarioCi, DateTime (+7 more)

### Community 91 - "Fill Preview Window UI"
Cohesion: 0.25
Nodes (6): Window, Brush, Button, TaskCompletionSource, UIElement, FillPreviewWindow

### Community 92 - "UI Graph Visualization"
Cohesion: 0.20
Nodes (4): IEnumerable, IReadOnlyDictionary, List, UiElement

### Community 93 - "Visual Element Highlighting"
Cohesion: 0.42
Nodes (5): Caja, IReadOnlyList, Que, Rect, Senalador

### Community 94 - "Navigation Design Principles"
Cohesion: 0.06
Nodes (36): Aceptado no es ejecutado (principio transversal del proyecto), Antes de una acción destructiva, di sobre QUÉ actúa, «Atrás» es estado EFÍMERO de la sesión, no dato del mapa (idea del usuario), «Atrás» NO es una arista: es un gesto de historial, Capa 1 determinista; LLM en capa 2, Cruzar una puerta sin explorar se juzga por el CAMBIO, no por el destino, El banco de pruebas también tiene estado: no le tires el suelo a la app, El foco se recupera antes de actuar, y los paneles del shell se descartan con Escape (+28 more)

### Community 95 - "Pending Task Persistence Queue"
Cohesion: 0.28
Nodes (6): Entry, CancellationToken, List, Task, Entry, PendingFinish

### Community 96 - "Screen Contract Principles"
Cohesion: 0.05
Nodes (42): ConAccion, Declarados, Pantallas, Contrato, int, STAThread, string, Alternatives (+34 more)

### Community 97 - "SAP Visual Element Reading"
Cohesion: 0.39
Nodes (3): SapBox, IReadOnlyList, SapVisualElement

### Community 98 - "Teach Session Request Models"
Cohesion: 0.25
Nodes (8): List, FileStateRequest, FileStateResponse, ProcessRequest, ProcessResult, TeachNote, UploadTokenRequest, UploadTokenResponse

### Community 99 - "Field Detection & Hit Testing"
Cohesion: 0.19
Nodes (3): DetectedField, IEnumerable, IReadOnlyList

### Community 100 - "Graphify Decision Rules"
Cohesion: 0.22
Nodes (11): Capa 1 — decisiones de desarrollo (determinista), Capa 2 — decisiones por app y por usuario (futura), El proceso es el ANFITRIÓN, no la app, Formato de una decisión, Graphify — el grafo de decisiones, Las dos capas, Navegación o contenido lo dice el CONTENEDOR, no dónde cae el elemento, No tienen «Atrás» con AutomationId estable (+3 more)

### Community 101 - "Tree Selection After Click"
Cohesion: 0.25
Nodes (6): Key, Text, Via, Key, Text, Via

### Community 102 - "Vercel API Deployment Config"
Cohesion: 0.29
Nodes (6): maxDuration, maxDuration, functions, api/teach/process-video.ts, api/**/*.ts, $schema

### Community 103 - "Roadmap: Control por Grafo"
Cohesion: 0.18
Nodes (13): La ubicación es el ancla (parámetro `at`), map_learn_app, map_take, Una regla ganada en una app no se exporta sin verificar, Plan hasta el producto, Dónde estamos (medido, 2026-08-03), Fase 1 · Que la espera sea por ESTADO, no por reloj, Fase 2 · Repetibilidad (+5 more)

### Community 104 - "App Update & Learning Manager"
Cohesion: 0.13
Nodes (10): AppInstalada, U.WindowsClient.Update, UpdateManager, VelopackAsset, Task, Task, Task, Task (+2 more)

### Community 105 - "Voice Input Output"
Cohesion: 0.20
Nodes (7): SpeechSynthesizer, VoiceActivity, bool, CancellationToken, Task, VoiceActivity, VoiceIO

### Community 107 - "Local Dev Server"
Cohesion: 0.40
Nodes (3): dir, port, server

### Community 109 - "Animation Easing Functions"
Cohesion: 0.28
Nodes (3): IEasingFunction, MuelleEase, ThrowEase

### Community 110 - "Log Bus"
Cohesion: 0.20
Nodes (7): bool, int, IReadOnlyList, List, object, string, LogBus

### Community 111 - "Graph Client Stepping"
Cohesion: 0.18
Nodes (10): Exception, Paso, Ventana, Visto, IReadOnlyList, List, Abandonado, PasoAPaso (+2 more)

### Community 113 - "App Teaching Orchestrator"
Cohesion: 0.15
Nodes (12): Clave(), CancellationToken, IntPtr, IReadOnlyDictionary, Task, UiElement, EnsenarAsync(), Leccion (+4 more)

### Community 114 - "Clinical Values API"
Cohesion: 1.00
Nodes (3): GET /api/agent/values (emparejamiento por código), Conceptos canónicos (vital.talla, vital.peso…), Puente con el portal clínico

### Community 115 - "Win32 Input Structs"
Cohesion: 0.67
Nodes (3): KEYBDINPUT, MOUSEINPUT, InputUnion

### Community 116 - "Client Agent & Actions Modules"
Cohesion: 0.10
Nodes (11): U.WindowsClient.Capture, U.WindowsClient.Telemetry, U.WindowsClient.Actions, U.WindowsClient.Voice, U.WindowsClient.Mcp, U.WindowsClient.Agent, List, AckResponse (+3 more)

### Community 117 - "UI Automation Heuristics Rules"
Cohesion: 0.07
Nodes (28): Corta el sufijo « - App» del título, Desplaza a la vista antes de pulsar, relee la caja después, y comprueba que el punto caiga dentro del contenedor, El `ct=` del selector se aplica al resolver, no es un adorno, El destino se confirma cuando se estabiliza, y las lecturas vacías no rompen el candidato, El punto pulsable lo da UIA (GetClickablePoint), no nuestra aritmética, En lo seleccionable, SELECT va antes que INVOKE, Guarda selectores, nunca referencias de elemento, Hay apps que IGNORAN el ratón sintético (+20 more)

### Community 118 - "Action Panel UI"
Cohesion: 0.12
Nodes (13): Estado, Border, Color, DateTime, DispatcherTimer, DllImport, EventArgs, int (+5 more)

### Community 123 - "WebSocket Client State"
Cohesion: 0.12
Nodes (13): ClientWebSocket, SemaphoreSlim, bool, CancellationTokenSource, DateTime, double, HashSet, int (+5 more)

### Community 124 - "Production Deployment Guide"
Cohesion: 0.08
Nodes (21): 1.1 Importar en Vercel, 1.2 Variables de entorno (Settings → Environment Variables, scope *Production*), 1.3 Desplegar, 1.4 Seguridad del endpoint (léelo), 1.5 Verificar, 3.1 Fijar el backend por defecto en el cliente (para que el usuario no configure nada), 3.2 Publicar, Fase 0 — Decisiones antes de empezar (+13 more)

### Community 125 - "Face Gesture Control"
Cohesion: 0.12
Nodes (13): bool, DispatcherTimer, DllImport, double, EventArgs, int, long, MouseButtonEventArgs (+5 more)

### Community 126 - "SAP GUI Automation Research"
Cohesion: 0.11
Nodes (21): SAP GUI Scripting (COM), UIA — superficie genérica de Windows, windows-graph (workflows sobre SAP GUI), UiaReader (CollectMenus, CollectFromChildren), Fase 4 · La tarea aprendida (workflows), UiaReader.Read(), Aceptado ≠ ejecutado, El snapshot de campos pertenece a UNA pantalla (+13 more)

### Community 127 - "Brain Turn Engine"
Cohesion: 0.40
Nodes (9): TurnRequest, TurnResult, runProviderTurn(), TurnInput, TurnOutput, BrainTurn, ScreenState, McpTool (+1 more)

### Community 128 - "Graphify Setup Guide"
Cohesion: 0.11
Nodes (18): 1. Instalar uv, 2. Instalar graphify, 3. Registrar la skill y los hooks, 4. Traer el grafo, Actualizar el grafo a mano, Camino entre dos partes del sistema, Comandos útiles, Encontrar los archivos más conectados (los críticos) (+10 more)

### Community 129 - "Menu and Option Learning"
Cohesion: 0.24
Nodes (4): List, Opciones, Textos, Titulo

### Community 130 - "Live Microphone Audio"
Cohesion: 0.15
Nodes (8): BufferedWaveProvider, WaveInEvent, WaveOutEvent, DateTime, double, int, object, LiveAudio

### Community 131 - "UI Automation Research Notes"
Cohesion: 0.12
Nodes (17): 2.1 Managed vs COM — el argumento correcto, 2.2 Árbol, TreeWalker y condiciones, 2.3 Caching — obligatorio para un grabador, 2.4 Identificación estable, 2.5 Patterns, 2.6 GRABACIÓN — eventos y threading, 2.7 Lo que UIA **no** puede hacer, 2.8 Detectar ventanas de SAP GUI (+9 more)

### Community 132 - "Cursor Trail Tracking"
Cohesion: 0.13
Nodes (11): Marca, int, IReadOnlyList, List, object, POINT, Timer, TimeSpan (+3 more)

### Community 133 - "Client/Brain Architecture Docs"
Cohesion: 0.15
Nodes (15): Arquitectura — la separación cliente / cerebro, Dónde se corta el bucle, El problema que resuelve (heredado de Android), ExecutionEngine.run (core Android), Mapa al core Kotlin, Por qué el cliente conduce el bucle (y no el servidor), Puntos de extensión (fases siguientes), Arquitectura (+7 more)

### Community 134 - "Tool Execution Pipeline"
Cohesion: 0.29
Nodes (3): CancellationToken, IReadOnlyDictionary, JsonElement

### Community 135 - "SAP GUI Scripting Guide"
Cohesion: 0.13
Nodes (15): 1.1 Qué es y por qué existe, 1.2 Habilitación: servidor, cliente y el aviso modal, 1.3 Árbol de objetos y acceso desde C#, 1.4 Element ID, estabilidad y `FindById`, 1.5 Controles: leer y escribir, 1.6 Enumerar el árbol de la pantalla activa, 1.8 Gotchas de SAP, 1. SAP GUI Scripting (+7 more)

### Community 136 - "Project Architecture Docs"
Cohesion: 0.15
Nodes (12): Arquitectura: cliente tonto, cerebro remoto, Compilar y correr, El agente que se rescata solo (diseño acordado, sin implementar), EL LOG ES LA FUENTE DE VERDAD, El puente con el portal clínico (repo `Pagina-web-clientes-final`), Estado actual (2026-07-26, noche), graphify, La cadena completa funciona, verificada contra el SAP real (+4 more)

### Community 137 - "Windows Explorer Rules"
Cohesion: 0.17
Nodes (12): Carpeta-o-archivo se le pregunta al DISCO, El contenido vive en ventanas hijas con HWND propio, El cromo persistente se detecta por REPETICIÓN y se usa como atajo, El explorador NO refresca su árbol UIA ante cambios hechos fuera de él, El panel de navegación global se aprende una vez, desde la raíz, El retroceso es el botón `aid=backButton`, por identidad, En la lista se entra con doble clic; en el panel, con clic simple, Fuera de alcance (+4 more)

### Community 138 - "SAP Scripting and UIA"
Cohesion: 0.18
Nodes (12): Issues dotnet/winforms#7763 y dotnet/wpf#9382, Entries — opciones de combo en SAP, Fiabilidad de eventos (controles Win32 no siempre notifican), Granularidad de grabación (round-trip vs evento), Identificación de elementos (Id canónico / AutomationId), SAP GUI Scripting (superficie de automatización), SAP.GUI.Scripting.net, SAP GUI Scripting Security Guide (7.60) (+4 more)

### Community 140 - "SAP Event Recording Model"
Cohesion: 0.18
Nodes (11): 1.7.1 Diseño del modelo, según SAP **[SAP-DOC]**, 1.7.2 El interruptor: `GuiSession.Record` **[SAP-DOC]**, 1.7.3 Eventos de `GuiSession` **[SAP-DOC]** — firmas literales, 1.7.4 Eventos de `GuiApplication` **[SAP-DOC]**, 1.7.5 Qué se captura en vivo desde un proceso externo, y qué NO, 1.7.6 STA, apartments y el pump — **la trampa de ingeniería nº1**, 1.7 GRABACIÓN — el modelo de eventos (lo crítico), `Change` — el corazón del grabador (+3 more)

### Community 141 - "Client-Backend Architecture Docs"
Cohesion: 0.24
Nodes (10): macos-client (U.app), backend/src/domain/actions.ts (el contrato), Compilar y ejecutar (Windows 10/11), Cómo la lectura del árbol usa UIA, Estructura, Qué hace (y qué NO), VoiceIO (TTS/STT), Ü · Cliente Windows (el frontend tonto) (+2 more)

### Community 142 - "Win32 Window Ownership Checks"
Cohesion: 0.24
Nodes (3): DllImport, IntPtr, Propio

### Community 143 - "Windows Client Auto-Update Release"
Cohesion: 0.25
Nodes (9): Paso «Subir a Supabase (compatible con S3)», Backend en Vercel (Fase 1), CLIENT_TOKEN — candado del endpoint, windows-client/src/Config.cs, Bucket público `windows` (Supabase miracle-app), Ui/FaceWindow.xaml (UpdateBtn), releases.win.json (el índice del feed), src/Update/Updater.cs (+1 more)

### Community 144 - "Clinical Bridge Pairing"
Cohesion: 0.08
Nodes (11): Binding, Point, CancellationToken, HttpClient, IReadOnlyList, Task, ClinicalBridge, IReadOnlyList (+3 more)

### Community 145 - "Keystroke Capture Event Hooks"
Cohesion: 0.29
Nodes (8): Captura de texto tecleado, Enfoque híbrido UIA + WinEvents/Raw Input, EVENT_OBJECT_VALUECHANGE, Evento Change de SAP GUI Scripting (batched), Raw Input (captura de tecleo), SAP GUI Scripting API — spec completa (release 620), SetWinEventHook y Event Constants, WinEvents (hooks de accesibilidad Win32)

### Community 146 - "UI Element Recognizer"
Cohesion: 0.38
Nodes (3): IReadOnlyList, UiElement, Reconocedor

### Community 147 - "Project README Docs"
Cohesion: 0.33
Nodes (5): Backend, Build, Estructura, Historial, Ü — Windows App

### Community 148 - "Installed Apps Discovery"
Cohesion: 0.40
Nodes (3): IEnumerable, string, InstalledApps

### Community 149 - "SAP GUI Table Notes"
Cohesion: 0.33
Nodes (6): Contar filas: `col.Count`, no `ElementAt` por clave, La geometría por fila SÍ existe: los getters piden `(clave, columna)`, La selección de un árbol de COLUMNAS vive en `selectedItemNode`, Posiciones repetidas = centinela, Una fila no tiene id propio, Árboles (`GuiTree`)

### Community 150 - "Workflow List Navigation UI"
Cohesion: 0.22
Nodes (3): SelectionChangedEventArgs, WorkflowListBox, ListBox

### Community 151 - "Dialog Prompt Window"
Cohesion: 0.25
Nodes (6): Button, Color, TaskCompletionSource, TextBlock, TextBox, Ventana

### Community 153 - "DOM UIA Selector Mapping"
Cohesion: 0.50
Nodes (4): 3.1 Tabla maestra, 3.2 Qué es el "selector" en cada superficie, 3.3 Clasificación de `actionType`, 3. Mapeo conceptual DOM ↔ SAP GUI Scripting ↔ UIA

### Community 154 - "Windows Form Automation Spec"
Cohesion: 0.67
Nodes (3): Convención de marcado de evidencia, Grabar y ejecutar acciones sobre formularios en Windows, SAP GUI Scripting y UI Automation, desde una app WPF/.NET (C#)

### Community 155 - "Architect Agent Package Config"
Cohesion: 0.20
Nodes (9): dependencies, @anthropic-ai/claude-agent-sdk, zod, description, name, private, type, @anthropic-ai/claude-agent-sdk (+1 more)

### Community 156 - "Architect Agent Tooling"
Cohesion: 0.29
Nodes (5): corrida, herramientas, t(), TURNOS, docMarkdown()

### Community 157 - "WPF App Startup"
Cohesion: 0.29
Nodes (4): StartupEventArgs, Application, STAThread, App

### Community 158 - "Mouse Hook Interop"
Cohesion: 0.40
Nodes (4): uint, MSLLHOOKSTRUCT, POINT, DllImport

### Community 160 - "Build Versioning Script"
Cohesion: 0.60
Nodes (3): Compilar(), Leer-Registro(), Snap()

### Community 163 - "Step Readiness Gating"
Cohesion: 0.15
Nodes (8): CancellationToken, double, int, Task, StepGate, StepGateResult, SurfaceReadiness, IUiSurface

### Community 166 - "Win32 Geometry Structs"
Cohesion: 0.67
Nodes (3): int, POINT, RECT

### Community 167 - "Live Debug Console"
Cohesion: 0.13
Nodes (12): object, Porque, Puede, bool, DllImport, int, IntPtr, ConsolaViva (+4 more)

### Community 169 - "Voice And Workflow Runner"
Cohesion: 0.33
Nodes (4): IVoice, CancellationToken, Task, WorkflowMcpRunner

### Community 172 - "Turn Protocol Models"
Cohesion: 0.50
Nodes (4): List, ScreenState, TurnRequest, TurnResponse

## Ambiguous Edges - Review These
- `UiaSurface` → `IsStepReady (compuerta al primer plano)`  [AMBIGUOUS]
  windows-client/CLAUDE.md · relation: calls
- `Entries — opciones de combo en SAP` → `UIA — Control Patterns (Value, Toggle, Selection, ComboBox, Edit)`  [AMBIGUOUS]
  windows-graph/INVESTIGACION-SAPGUI-UIA.md · relation: conceptually_related_to
- `Fiabilidad de eventos (controles Win32 no siempre notifican)` → `Issues dotnet/winforms#7763 y dotnet/wpf#9382`  [AMBIGUOUS]
  windows-graph/INVESTIGACION-SAPGUI-UIA.md · relation: conceptually_related_to

## Knowledge Gaps
- **371 isolated node(s):** `TURNOS`, `herramientas`, `corrida`, `Hop`, `Ensenanza` (+366 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **42 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **What is the exact relationship between `UiaSurface` and `IsStepReady (compuerta al primer plano)`?**
  _Edge tagged AMBIGUOUS (relation: calls) - confidence is low._
- **What is the exact relationship between `Entries — opciones de combo en SAP` and `UIA — Control Patterns (Value, Toggle, Selection, ComboBox, Edit)`?**
  _Edge tagged AMBIGUOUS (relation: conceptually_related_to) - confidence is low._
- **What is the exact relationship between `Fiabilidad de eventos (controles Win32 no siempre notifican)` and `Issues dotnet/winforms#7763 y dotnet/wpf#9382`?**
  _Edge tagged AMBIGUOUS (relation: conceptually_related_to) - confidence is low._
- **Why does `FaceWindow` connect `Windows App Core Services` to `UI Automation Location Lookup`, `Surface Map Element Model`, `Workflow Teaching Session`, `Face Window UI Elements`, `Avatar Face Animation`, `Keyboard Input Handling`, `Clinical Bridge Pairing`, `Graph Explorer Window`, `Collapsed Pill Hover UI`, `Windows Client Architecture Contracts`, `Workflow List Navigation UI`, `Global Hotkey Registration`, `Main Window Command Handlers`, `Telemetry Bus Client`?**
  _High betweenness centrality (0.113) - this node is a cross-community bridge._
- **Why does `UiaSurface` connect `UI Automation Surface` to `UI Element Enumeration`, `Step Readiness Gating`, `UI Automation Event Capture`, `Key Spec Parsing`, `Surface Map Tooling`, `Voice And Workflow Runner`, `Workflow Teaching Session`, `Action Plan Execution`, `UI Field Detection`, `Windows Client Architecture Contracts`, `UIA Selector Builder`, `Win32 Window API Bindings`, `Workflow Recording & Observation`?**
  _High betweenness centrality (0.072) - this node is a cross-community bridge._
- **Why does `SapGuiSurface` connect `SAP GUI Surface Reader` to `Tree Node Selection Reading`, `SAP Event Publishing`, `Step Readiness Gating`, `Field Detection & Hit Testing`, `SAP Tree/Grid Traversal`, `SAP Step Execution`, `Voice And Workflow Runner`, `Workflow Teaching Session`, `Field Description Helpers`, `Tree Row Geometry Cache`, `Windows Client Architecture Contracts`, `COM Type Introspection`?**
  _High betweenness centrality (0.059) - this node is a cross-community bridge._
- **What connects `TURNOS`, `herramientas`, `corrida` to the rest of the system?**
  _371 weakly-connected nodes found - possible documentation gaps or missing edges._