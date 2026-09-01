# Plan de implementación: enseñar a Ü por demostración — una skill por demo

Estado: **propuesto** · Nace del diagnóstico del 2026-09-01 · Rama: `jose/ensena-u`

> **La petición, en una frase:** copiar «teach a skill to Claude» para Ü — el humano demuestra un
> flujo completo (pantalla grabada + rastro del ratón + narración por voz) y Ü lo aprende como una
> pieza con nombre que después reproduce sola, con computer use.

## Diagnóstico: qué se midió

Lo midieron **seis agentes en paralelo** el 2026-09-01 sobre main `8a3ea6d` (cinco lectores por
subsistema + un cruce). El hallazgo mayor: **la cadena demostración→artefacto→reproducción existe
entera salvo UN eslabón.**

### Lo que ya existe (no construir)

| Tentación | Ya existe |
|---|---|
| Grabador de pantalla + voz | `Teach/ScreenRecorder.cs` (mp4 H.264 30fps, mic + loopback del PC) + `TeachSession.cs` + `VideoLibrary.cs` (`%LOCALAPPDATA%\U\teach-videos`) |
| Capturador de pasos con valores | `windows-graph/WorkflowRecorder.cs:53-91` + `IUiSurface.ObservedStep` — **el único que ve lo TECLEADO** (actionType/selector/label/value/surfaceHints), en UIA y SAP |
| Rastro del ratón → identidad | `ClickWatcher.cs` (selector + etiqueta al pulsar) → `MapaVivo.cs:383-389` → `Grafo.Cruzar` |
| Voz → significado | `ProtocoloOpenAI.cs:189` (transcripción) → `UnaLeccion.cs` (vigilante 9 s) → `EstoEs` → `Grafo.Ensenar` + `FotosDeLosRecuerdos` |
| Pantallazo por paso | `StepShotCamera.cs` — le falta lector, no productor (su consumidor 👣 murió en e3c3ad8: `OnStepPause` sin llamadores, grep vacío) |
| Motor de reproducción | `RecorrerSegunElNucleo` + `PulsarSegunElNucleo`: compuerta de vida, parcial honesto, gesto ensayado-una-vez (spec 003) |
| Formato de paso | `[{exit},{text}]` de `map_batch` (`SurfaceMapTools.cs:704`) |
| Orquestador de sesión | `WorkflowTeachSession.cs`: cuenta atrás 3 s, **valla de foco** (dos grabaciones inservibles el 2026-07-26 con el foco en Ü), grabadoras en paralelo |
| Descarte de demo fallida | `TeachSession.RestartAsync/DiscardAsync` — escritos, **cero llamadores** |
| Persistencia del saber | `ProyectorNeo4j` (significado, foto y gesto ya sobreviven al apagado) |

### Los huecos que cortan la cadena (medidos, no supuestos)

1. **EL EMPAQUETADOR — el único eslabón inexistente.** Nada convierte la sesión en un artefacto: el
   grafo guarda aristas SUELTAS sin orden ni nombre (`Grafo.cs:47-53`), y el único empaquetador
   actual (`WorkflowTeachSession`) manda `PlanStep` al Graph remoto — formato que `map_batch` no lee.
2. **El vigía acuña clics sintéticos como humanos.** `ClickWatcher.cs:181-201` marshalea `flags` y
   **jamás consulta `LLMHF_INJECTED`**: nuestros propios clics se atribuyen como humanos (la arista
   falsa docs→specs del 2026-08-31, medida por el tester). Reproducir una skill con el vigía vivo
   **re-contamina el grafo en cada corrida** — este hueco bloquea todo lo demás.
3. **La llegada no se exige al reproducir.** La compuerta juzga que el elemento esté VIVO
   (`RecorrerSegunElNucleo.cs:95-99`), no que se llegó A DONDE la demo llegó. El destino está en la
   arista (`Grafo.cs:171`) y nadie lo exige: «terminé» sigue siendo opinión (pendiente nº1 de
   CLAUDE.md de siempre).
4. **La demo mala se publica igual.** Detener procesa y sube; `RestartAsync`/`DiscardAsync` existen
   sin llamadores y el botón está `Collapsed`. «Me equivoqué» hoy sube el video malo a Gemini.
5. **La narración no viaja con su paso.** `Click.When` es la hora de la RESOLUCIÓN (no del golpe:
   `MSLLHOOKSTRUCT.time` se descarta), y la frase literal solo sobrevive como log
   (`ConversacionEnVivo.cs:1203`). Sin ancla temporal, «aquí va el NIT» no se puede colgar del campo
   que estaba sonando.
6. **El cerebro no puede pedir una skill.** El sistema del piloto es fijo: no hay catálogo — no
   existe forma de decir «reproduce radicar factura».

### Los riesgos que un diseño ingenuo pisaría

- **Doctrina del terreno** (`GENESIS-terreno.md`): *las aristas se ganan ejecutando, nunca se
  atribuyen a clics humanos*. La skill guarda SUS pasos aparte; **la primera reproducción gana las
  aristas** — la demo NO siembra el grafo como si se hubiera ejecutado.
- **Half-duplex del eco**: no se puede narrar encima de la voz de Ü (la compuerta de eco silencia el
  micro). Durante la demo, Ü escucha y calla.
- **El 504 en el camino crítico**: el resumen LLM del mp4 ya rompió una vez (PendingFinish existe
  por eso), y con `ProcessVideo=false` la voz aporta CERO. **El artefacto no puede depender del
  video procesado**: el mp4 es evidencia, no canal.
- **Ceguera de entrada conocida**: el vigía solo ve `WM_LBUTTONDOWN` (ni teclado, ni derecho, ni
  rueda) — por eso los pasos salen de `ObservedStep` del recorder, que sí ve el valor tecleado, y
  no del ClickWatcher.

## Por qué esto va dirigido por especificación

Porque es el subsistema perfecto para «parecer que funcionó»: una demo siempre produce *algo* (un
mp4, unas aristas, unos pantallazos), y una reproducción siempre *hace* algo. Sin promesas escritas
antes, la prueba natural comprobaría que el archivo existe y que el batch corrió — no que la skill
dice la verdad ni que la corrida llegó. Y el hueco 2 es literalmente el vicio fundacional que el
GENESIS le atribuye al sistema viejo, a punto de repetirse dentro del nuevo.

## La especificación

En `tests/ContratoDelGrafo`, en continuación (la más alta hoy es la 83):

| # | Promesa | Fase |
|---|---|---|
| 84 | una pulsación sintética no se acuña como clic humano: el vigía la deja pasar | 1 |
| 85 | una demostración termina en una skill con nombre: los pasos en orden, de dónde parte y a dónde llega cada paso | 2 |
| 86 | reproducir una skill exige la llegada de cada paso: acabar en otro sitio no es haberlo hecho | 3 |
| 87 | una demo descartada no publica nada: ni video, ni pasos, ni skill | 4 |
| 88 | lo dicho durante la demo viaja con su paso: la frase queda anclada al paso que sonaba | 5 |
| 89 | las skills enseñadas se anuncian al cerebro: se piden por nombre, y el catálogo dice cuándo usarlas | 6 |

**La que cierra el asunto es la 85** — el eslabón que no existe. Pero **la 84 va primero y no es
negociable**: sin el filtro de inyección, cada reproducción re-contamina el grafo con aristas
falsas, y lo que la 85 empaqueta se reproduce sobre un terreno que miente.

La 86 es el pendiente histórico nº1 del repo («terminé» no es un veredicto) hecho promesa: la skill
trae la llegada de cada paso porque la demo la vio, y el batch la exige porque puede.

### El artefacto: qué es una «skill enseñada»

Copia deliberada del patrón `.claude/skills/*/SKILL.md` — metadatos de disparo + pasos + material —
pero **cada campo anclado a algo que la sesión ya produce**:

```json
{
  "nombre": "radicar-factura",
  "description": "cuándo usarla, en la voz del que la pide",   ← disparador, como una skill de Claude
  "donde_empieza": "sapgui://QAS/NWP1…",                        ← Grafo.Aqui al arrancar la demo
  "pasos": [
    { "exit": "sap:…#tbbtn=NV44", "llegada": "sapgui://…",     ← ObservedStep + destino de la arista
      "dicho": "aquí se crea el triage" },                      ← narración anclada (fase 5)
    { "text": "12345", "campo": "sap:…RNPA1-PASSNR" }           ← el valor que el recorder SÍ vio
  ],
  "evidencia": { "video": "teach_….mp4", "shots": "step-shots/…" }
}
```

Persistido **LOCAL, en archivo** (`%LOCALAPPDATA%\U\skills\`), no solo en Neo4j: Neo4j caído no
puede significar skills perdidas. Los `pasos` son el formato que `map_batch` **ya consume** — no
nace un tercer formato ni un tercer player.

### Con qué se juzga cada una

El contrato no toca la pantalla: las seis juzgan **decisiones y transformaciones puras**, como las
82-83.

| # | Se juzga con |
|---|---|
| 84 | La regla de inyección como función: flags con `LLMHF_INJECTED` → no se atribuye. Sin tocar hooks reales |
| 85 | El empaquetador como función pura: lista de ObservedStep/cruces de una sesión → skill con nombre, pasos ordenados y llegada por paso; una sesión vacía NO produce skill |
| 86 | El arnés del batch que ya existe: paso con llegada declarada que aterriza en otro sitio → parcial honesto, no «hecho» |
| 87 | Descartar como decisión: la sesión descartada no entrega nada a ninguna de las tres salidas |
| 88 | El anclador como función: (frases con hora, pasos con hora) → cada frase al paso que sonaba; frase sin paso cerca queda como contexto general, no se inventa el ancla |
| 89 | El catálogo: con N skills en disco, la lista las anuncia con nombre y description; con cero, lista vacía — no se inventa |

Que la demo real de punta a punta funcione (grabar → hablar → empaquetar → reproducir en el
Explorador o SAP) es **nivel 4**, a mano, con log — como siempre.

## Las fases

| Fase | Promesa | Qué toca | Nota |
|---|---|---|---|
| 1 | 84 | `ClickWatcher.cs` | El filtro `LLMHF_INJECTED` — protege todo lo demás |
| 2 | 85 | nuevo `Navigation/SkillEnsenada.cs` + `WorkflowTeachSession` (destino local además del Graph) | El eslabón inexistente |
| 3 | 86 | `RecorrerSegunElNucleo.cs` (la llegada opcional por paso) | «Terminé» deja de ser opinión |
| 4 | 87 | `TeachSession` (cablear `DiscardAsync`), UI mínima | Ya está escrito, cero llamadores |
| 5 | 88 | `ClickWatcher` (hora del golpe), `ConversacionEnVivo` (frase con hora) + el anclador | La voz viaja con su paso |
| 6 | 89 | `SurfaceMapTools` (catálogo `map_skills` o skills en `tools/list`) | El cerebro puede pedirlas |

## Lo que NO entra

- **Sembrar el grafo desde la demo.** Doctrina del terreno: la demo produce LA SKILL; las aristas
  las gana la primera reproducción, ejecutando.
- **El resumen LLM del video como canal.** Sigue siendo opcional (`ProcessVideo`), y el artefacto
  no depende de él. El 504 ya enseñó dónde no poner el camino crítico.
- **Reproducir teclado/derecho/rueda capturados del hook.** El vigía no los ve y no se le añaden
  ojos: los pasos salen de `ObservedStep`. (El `rightclick` ejecutable de la spec 003 sigue
  esperando a que `Take` deje de descartar `accionPedida` — pendiente ya anotado allí.)
- **Resucitar el 👣 paso a paso** (el lector muerto de los step-shots). Los shots entran como
  evidencia de la skill; el depurador visual es otra spec.

## Hallazgos

- **2026-09-01 (fase roja)** — Dos tropiezos de arnés, los dos con moraleja:
  1. En el contrato de main, `Nucleo` es el **namespace** del terreno, no el alias de ensamblado
     que tenía el contrato viejo — el helper de capacidades los confundió y no compilaba.
  2. El scratch tenía un `U.dll` **del 31 de agosto** (la arquitectura pre-rebase) que el build
     incremental nunca refrescó: los errores «no existe RecorrerSegunElNucleo» eran fantasmas de
     un binario viejo, no del código. La pista que lo destapó: tipos que compilaban ayer
     «desapareciendo» hoy en líneas que nadie tocó.
- **2026-09-01 (fase verde)** — Lo que quedó **cableado de verdad** vs lo que es capacidad juzgada
  a la espera de su integración:
  | Pieza | Estado |
  |---|---|
  | Filtro de inyección (84) | **Cableado en el hook real** — `HookCallback` pregunta antes de resolver |
  | Llegada exigida (86) | **Cableada en el batch real** — `Recorre` la exige cuando el paso la trae |
  | Empaquetador + catálogo (85, 89) | Capacidad completa y juzgada; falta que `WorkflowTeachSession` la llame al cerrar y que el MCP anuncie el catálogo (`map_skills`) |
  | Descarte (87) | La decisión existe y está juzgada; falta cablear `DiscardAsync` y el botón |
  | Anclador (88) | Capacidad completa; falta capturar la hora del golpe en el hook y la frase con hora en la voz |
- **2026-09-01** — La promesa 87 juzga la **decisión**, y tiene un punto ciego asumido: un
  `Descartar` que no hiciera nada (sesión sigue «grabando») tampoco entregaría, así que el contrato
  no lo distingue de un descarte bueno. El sabotaje elegido (descartar→publica) es el fallo con
  dientes; el no-op lo tiene que cazar el nivel 4.

## Cierre

- [ ] Promesas 84-89 verdes (y las 83 anteriores intactas)
- [ ] Rotas a propósito, una por una (verificación por diff, build sin silenciar, veredicto exigido)
- [ ] Nivel 4: una demo real de punta a punta, grabada, empaquetada y reproducida, en ≥2 pantallas
      con nombre y log
- [ ] Estado: **implementado** (AAAA-MM-DD)
