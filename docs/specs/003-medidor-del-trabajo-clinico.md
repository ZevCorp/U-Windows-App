# Plan de implementación: el medidor del trabajo clínico

Estado: **en implementación** · Nace del encargo de medición de impacto del 2026-09-01 ·
Rama: `claude/miracle-impact-measurement-h1n3k8` (rama designada de la tarea; cruza los tres repos)

> Antecedente directo: [`medir-el-terreno.md`](medir-el-terreno.md). De ahí se heredan dos reglas
> ya escritas y pagadas: **la privacidad se resuelve ANTES de instalar en el primer equipo**, y
> **un indicador permanente y un atajo para parar no son cortesía** — son la diferencia entre una
> herramienta de medición y una cámara oculta. No se hereda el vídeo: este medidor no graba pantalla.

## Qué se quiere

Medir el trabajo operativo de los médicos de urgencias (Hospital General de Medellín, SAP IS-H) en
tres fases con el MISMO instrumento: **baseline** (sin Miracle, empieza esta semana), **Miracle
Notes**, **Notes + Operations**. Por turno y por consulta: tiempo activo en PC y en el HIS, tiempo
escribiendo, clics, cambios de contexto, pantallas SAP recorridas, esperas de SAP, trabajo
posterior a la atención. Sin capturar jamás contenido clínico.

El medidor es **un exe separado de U.exe** (decisión del dueño, 2026-09-01): `UMedidor.exe`, nuevo
subsistema top-level `medidor/`, con la misma separación que `mapeador/` — dominio puro juzgable
sin pantalla + app Windows delgada.

## Diagnóstico: qué se midió

| Qué | Medida | Fuente |
|---|---|---|
| Agregación de duración existente en el repo | **cero** — nadie suma segundos por app ni por pantalla; el dwell se retiró el 2026-08-30 | acta de retiro en `tests/ContratoDelGrafo/Contrato.cs`; búsqueda exhaustiva 2026-09-01 |
| Único timestamp de llegada a una pantalla | `MapaVivo._llegadaAlAnterior`, solo para causalidad, nunca se acumula | `windows-client/src/Navigation/MapaVivo.cs:168` |
| Clics | ya se cuentan siempre (`ClickWatcher.Downs`), callback mínimo, dedup 600 ms/8 px | `windows-client/src/Navigation/ClickWatcher.cs:134` |
| Identidad de pantalla SAP sin contenido clínico | SID+transacción+programa+dynpro+subdynpro, 5-8 llamadas COM por tick | `windows-graph/src/Surfaces/SapGuiSurface.cs` (`Identity()`) |
| Latencia real de round-trip SAP | eventos COM `StartRequest`(514)/`EndRequest`(515), verificados 2026-07-26, hoy solo enganchados en modo Enseñar | `windows-graph/src/SapComEvents.cs`, `windows-graph/CLAUDE.md` |
| Telemetría existente hacia Graph | `TelemetryClient` descarta al llenar la cola y NO reencola en fallo — inaceptable para un estudio | `windows-client/src/Telemetry/Telemetry.cs:145` |
| Caminos que SÍ sacan contenido clínico (prohibidos aquí) | `ReadFields()` (CurrentValue), `SapContextReader`, `WorkflowRecorder.Value`, títulos de ventana, sufijo `vista:` | exploración 2026-09-01 |

## Por qué esto va dirigido por especificación

Un medidor es un subsistema que **se juzga a sí mismo por construcción**: si cuenta mal, nadie lo
nota mirando el panel — los números "se ven normales" (el repo ya pagó esto: el 29/30 con 19 pasos
comidos, y el promedio de 1.461 ms que era una muestra colgada). Y aquí un error no rompe una demo:
**contamina el baseline del estudio**, que no se puede volver a medir. Además las promesas de
privacidad (qué NO puede salir del hospital) tienen que ser ejecutables, no prosa.

**La regla del flujo, y no tiene excepciones:** ninguna línea de producción entra antes que la
promesa que la juzga. Cada fase empieza con el contrato ROTO y termina con el contrato INTACTO.

## La especificación

Contrato propio del subsistema: `medidor/Contrato/` (patrón `mapeador/Contrato`), numeración desde 1.

| # | Promesa | Fase que la pone verde |
|---|---|---|
| 1 | un lote serializado jamás contiene el título de la ventana de entrada | 1 |
| 2 | una app fuera de la lista blanca sale como «otro», y una web solo sale como dominio permitido | 1 |
| 3 | la identidad SAP viaja sin el sufijo `vista:` y sin el título de la ventana | 1 |
| 4 | del identificador de paciente solo sale la huella: el crudo no sobrevive, y normalizar quita los ceros de la izquierda | 1 |
| 5 | la clave de la huella es la del día operativo que corta a las 06:00, y el turno la fija al abrirse | 1 |
| 6 | la misma persona da la misma huella dentro del día operativo, y otra persona da otra | 1 |
| 7 | un tick jamás aporta más de 2 000 ms: despertar de una suspensión no regala horas, y el salto queda contado | 1 |
| 8 | activo es input en los últimos 60 s; sin input el tiempo es de primer plano, no activo | 1 |
| 9 | la escritura se mide por ráfagas — huecos de hasta 1,5 s se unen — y del tecleo solo salen cantidades, jamás qué tecla fue | 1 |
| 10 | una cubeta se parte cuando cambia la app o el encounter, y las partes suman el total sin doble conteo | 1 |
| 11 | cada milisegundo cae en exactamente una cubeta de 15 s alineada al reloj de pared | 1 |
| 12 | un turno se cierra solo por una causa escrita, y el cierre la dice | 1 |
| 13 | un turno sin médico mide igual, y se puede reasignar a un médico mientras siga abierto — nunca después | 1 |
| 14 | el spool no pierde ni duplica: lo tomado sin confirmación se vuelve a entregar idéntico, y la confirmación lo borra de una vez | 1 |
| 15 | el spool lleno descarta lo más viejo contando cada descarte — jamás en silencio | 1 |
| 16 | un lote respeta los topes por colección, y una fila que el servidor rechazó como veneno sale del spool en vez de reintentarse para siempre | 1 |
| 17 | cada evento lleva un uid monotónico por instalación que sobrevive al reinicio: el mismo evento no entra dos veces | 1 |
| 18 | la calidad del turno cuenta lo que faltó: huecos, saltos de reloj, descartes y ganchos degradados | 1 |
| 19 | una visita SAP empieza al llegar a una identidad y termina al salir: su duración y su destino salen del stream, no de un cronómetro aparte | 2 |
| 20 | la espera de SAP es la suma de sus round-trips: `StartRequest` abre, `EndRequest` cierra, y un cierre sin pareja no resta | 2 |
| 21 | time-to-ready va de la llegada al primer `EndRequest` sin `Busy`; si nunca llega queda nulo, no cero | 2 |

La que de verdad cierra el asunto es la **1**: mientras el arnés no pueda demostrar que ningún
título de ventana sale en un lote, todas las demás son cosmética — no se puede instalar en
urgencias un medidor cuya privacidad es prosa.

### Con qué se juzga cada una

- **Mapa a mano en la propia prueba** (promesas 1-18): entradas construidas en el test, incluidas
  hostiles (títulos con nombres de persona, IDs con ceros, saltos de reloj de horas, kill simulado
  del spool reabriendo la base).
- **Fixture congelado** (`medidor/Contrato/bronce/`): un stream de identidades SAP + eventos
  Busy/Start/EndRequest capturado una vez, para las promesas 19-21. Mismo resultado en cualquier
  máquina y para siempre.
- El contrato corre **sin pantalla y sin Windows**: `medidor/Dominio` es net8.0 puro, así el juez
  corre en CI y en el Linux donde se desarrolla. Lo que exige pantalla (ganchos, COM, bandeja) se
  juzga en el nivel 4 de la compuerta, sobre el PC real.

## Las fases

### Fase 0 — La sonda en sitio (equipo, en el hospital; no bloquea la fase 1)

`medidor/Sonda/` — consola de enlace tardío puro (aprendizaje nº13: preguntarle a la API). Con
U.exe corriendo, en este orden, dejando `sonda-AAAAMMDD.log` con horas:

1. Attach por ROT (`SapROTWr.SapROTWrapper`) con U.exe ya atachado → ¿conviven dos engines?
2. `Identity()` cada 1 s durante 15 min de uso real, midiendo ms por tick y saltando `Busy` →
   ¿SAP degrada?
3. Enganchar `SapComEvents` (514/515/1280) con bomba de mensajes mientras U.exe trabaja, y 5 min
   con U.exe en modo Enseñar (doble enganche) → ¿llegan los eventos? ¿interfieren?
4. La regla de identidad: leer el campo del paciente en NV2000 y la regex del título → ¿el PATNR
   está donde se cree? (calibra `reglas_identidad` de la config remota).
5. `SetWindowsHookEx` WH_MOUSE_LL + WH_KEYBOARD_LL → ¿el antivirus/política del hospital lo deja?

Si 1-3 fallan: el medidor opera en modo `foreground-only` por config remota (sin COM; identidad
SAP gruesa por proceso). El esquema de datos no cambia.

### Fase 1 — El medidor mide y lo medido llega (promesas 1-18)

| | |
|---|---|
| **Qué toca** | `medidor/Dominio/**`, `medidor/Contrato/**`, `medidor/App/**`, `medidor/Sonda/**`, `.github/workflows/contrato.yml`, `scripts/verificar.ps1`, `scripts/instalar-medidor.ps1` |
| **¿Núcleo congelado?** | no — subsistema nuevo; `windows-graph` solo se referencia, no se toca |
| **Terminado** | contrato del medidor INTACTO (1-18 verdes, 19-21 PENDIENTES declaradas de fase 2), `App` compila |
| **Del otro lado** | Graph: migración `metrics_terreno` + `/api/v1/metrics/{enroll,config,batch}`; portal: tablas de administración + seeds del HGM |

### Fase 2 — SAP profundo (promesas 19-21) y calidad servida

Viaje + EsperaSap con el fixture congelado; `HiloSap` engancha los eventos COM fuera del modo
Enseñar; visitas finas a `metrics_sap_visits`; heartbeat con el PC bloqueado; detección de
silencio por dispositivo en el cron diario de Graph.

### Fase 3 — Lectura

RPCs `superadmin_medicion_*` + `/superadmin/medicion` (institución, turno, comparación, config,
export CSV) en el portal.

### Fase 4 — Estudio completo

Comparación de fases pareada por médico, columna Notes al lado (por `profile_id` + día), brazo
Operations (`RunTimings` estructurado en `workflow_end` + espejo `ops_run`), Velopack en
`windows-release.yml`.

## Lo que NO entra

- **Vídeo o capturas de pantalla** — el antecedente lo evaluó y aquí ni siquiera hace falta.
- **Contenido de campos SAP** (`ReadFields`), contexto para LLM (`SapContextReader`), valores de
  workflow (`WorkflowRecorder`): prohibidos; el medidor no los referencia.
- **Códigos de tecla o texto**: solo cantidades y ráfagas.
- **El sufijo `vista:`** de la identidad SAP (texto de árbol) y **títulos de ventana**.
- **Unir `encounter_key` (HMAC de paciente SAP) con `session_id` de Notes (uuid de consulta)**:
  claves de mundos distintos; la comparación Notes va por (médico, día), no por paciente.
- **Enrolamiento fuerte por dispositivo de Operations** (`graph_windows_devices`): el medidor usa
  su propio código de enrolamiento corto + API key existente; endurecer identidad es proyecto de
  Operations y no se duplica aquí.

## El aviso al médico (borrador para que lo apruebe el hospital — punto abierto nº1)

> **Este computador mide tiempos de trabajo, no contenido.** El medidor registra cuánto tiempo se
> usa cada aplicación, cuántos clics y cuánto tecleo hay (nunca QUÉ se escribe), y qué pantallas
> del sistema clínico se recorren (nunca los datos del paciente). El nombre del paciente y su
> historia no salen de este computador. Puedes pausar la medición con el botón del icono — queda
> registrado el tiempo pausado, nada más.

## Hallazgos

<!-- Se rellena DURANTE la implementación. -->

- (2026-09-01) `dotnet` no existe en el entorno de desarrollo Linux; se instala SDK 8 vía apt para
  poder correr el contrato del medidor en CI y en local.
- (2026-09-01) **El contrato del medidor pasa 21/21 en Linux**, y el sabotaje deliberado
  (Normalizador que fuga el título, spool que no confirma) lo pone en ROJO — el juez juzga.
- (2026-09-01) **El SDK de .NET de este Linux (Ubuntu source-built) NO trae los targets de
  WindowsDesktop**, así que `Medidor.App` (WPF + WinForms + la referencia COM a `windows-graph`)
  **no compila aquí ni con `EnableWindowsTargeting`** — falta `Microsoft.NET.Sdk.WindowsDesktop`,
  que solo viene en el SDK oficial de Microsoft. Es exactamente el límite que la regla `solo-mac`
  y la compuerta ya asumen: `windows-graph`/WPF se juzga sobre el PC real (nivel 4). Lo que SÍ se
  verificó en Linux: `Medidor.Dominio` + su contrato (verde), y el slice portable de la App
  (`ClienteGraph` + `Subidor`) compilado contra el Dominio para cazar desajustes de firma. El
  resto de `Medidor.App` (ganchos, sonda de primer plano, hilo SAP, bandeja, ventanas, energía,
  orquestador, programa) se compila en Windows.
- (2026-09-01) Corrupción de reloj evitada: los ganchos y el orquestador tienen `Stopwatch`
  distintos; el «hace cuánto fue el último input» se devuelve RELATIVO, no como timestamp absoluto
  de un reloj ajeno (restar entre dos monotónicos distintos no significa nada).
- (2026-09-01) El usuario SAP (`session.Info.User`, señal secundaria de validación del médico)
  pide un método nuevo en `SapGuiSurface` → queda para fase 2 con su propia promesa; el selector
  de turno basta para el baseline.

## Cierre

- [ ] Promesas 1-18 verdes y 19-21 en el estado declarado de su fase (`dotnet run --project medidor/Contrato`)
- [ ] Sabotaje comprobado: romper Normalizador/Huella/Spool a propósito pone el contrato ROJO
- [ ] `App` compila (`EnableWindowsTargeting` en Linux/CI; Release en Windows)
- [ ] Sonda de Fase 0 corrida en el hospital, log con horas pegado en el PR
- [ ] Nivel 4 sobre el PC real: selector + icono + pausa; lote llega a Supabase; kill a mitad de
      turno → el spool re-entrega sin duplicar; U.exe convive (contar pantallas probadas, con nombre)
- [ ] Estado de este documento: **implementado** (AAAA-MM-DD)
