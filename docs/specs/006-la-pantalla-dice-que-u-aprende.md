# Plan de implementación: la pantalla entera dice que Ü está aprendiendo

Estado: **implementado, nivel 4 a mano pendiente** (2026-09-02) · Nace del diagnóstico del 2026-09-02 · Rama: `jose/aura-de-aprendizaje`

> **La petición, en una frase:** que enseñarle a Ü se VEA — un aura ligera en los bordes de la
> pantalla, en el color de Ü y degradada hacia dentro, mientras graba una demostración.

## Diagnóstico: qué se midió

| Qué | Medida | Fuente |
|---|---|---|
| Lo que hoy dice «estoy grabando» | el botón 🎓 pasa a ⏸ sobre fondo rojo (24 px) y la carita cambia a la pose `Grabando`; nada más | `FaceWindow.SetTeachingUi`, `FaceControl` |
| El color de «grabando» | **el mismo rojo que «fallo»** — colisión escrita y asumida en `UiPalette.Fallo`, con la nota «si algún día hay que separarlos, el que se mueve es grabando» | `Ui/UiPalette.cs` |
| Dónde está el operador mientras enseña | en OTRA app (SAP, el navegador): la valla de foco exige que el foco salga de Ü, y la carita queda plegada en una esquina | `WorkflowTeachSession.StartAsync` |
| Las dos fases de una enseñanza | cuenta atrás de 3 s (todavía no graba) → grabación (recorder de pasos + video) | `WorkflowTeachSession.StartAsync:74-77`, `IsRecording` |
| Qué monitor se graba | solo el principal (`SourceOptions.MainMonitor`) | `Teach/ScreenRecorder.cs:54` |
| Overlays click-through ya existentes | `HighlightOverlay`, `InspectorOverlay`, `LocatorBadge`, `PanelDeAcciones` — todos con `WS_EX_TRANSPARENT \| WS_EX_LAYERED \| WS_EX_TOOLWINDOW` | `Ui/*.cs` |

En resumen: el operador está mirando SAP, la carita está lejos y plegada, y el único indicio de que
Ü graba es un botón diminuto en un color que también significa «se rompió». La grabación se olvida
—y una demo con pasos de más es una demo mala.

## Por qué esto va dirigido por especificación

Porque un overlay «se ve y ya», y una prueba escrita después comprobaría que la ventana existe. Lo
que importa se puede juzgar sin pantalla: **en qué fase se enciende y en cuál se apaga** (encendida
durante la cuenta atrás mentiría: aún no graba; encendida durante la subida mentiría al revés), y
**que el centro queda limpio** (un aura que tape el trabajo es peor que no tener aura).

**La regla del flujo, y no tiene excepciones:** ninguna línea de producción entra antes que la
promesa que la juzga. Cada fase empieza con el contrato ROTO y termina con el contrato INTACTO.

## La especificación

| # | Promesa | Fase que la pone verde |
|---|---|---|
| 107 | mientras Ü aprende, los bordes de la pantalla lo dicen: el aura se enciende al grabar, se apaga al terminar y deja el centro limpio | 1 |

### Con qué se juzga

**Mapa a mano en la propia prueba**, sobre una regla pura separada del dibujo
(`Ui/ReglaDelAura`), por el mismo camino que la 104 juzga `SesionDeDemo`:

- `Decidir(ensenando, grabando)` → `Apagada` / `Preparando` / `Aprendiendo`. Sin enseñar: apagada.
  Enseñando sin grabar (cuenta atrás): preparando, tenue. Grabando: aprendiendo. **Terminado pero
  aún cerrando** (`ensenando=false, grabando=true`): apagada — el cierre sube el video y no aprende
  nada de lo que hagas ahora.
- `Opacidad(distanciaAlBorde, grosor)` → en el borde se ve, a partir del grosor es **cero**, y entre
  medias baja de forma monótona: un degradado, no una franja.

El overlay real (`AuraDeAprendizaje`) construye sus pinceles **muestreando esa misma función**, para
que lo juzgado y lo pintado no puedan discrepar (aprendizaje nº16: comparar por el mismo camino).

Lo que solo puede decir el PC real (nivel 4): que el aura se ve en el color de Ü y no estorba, que
el clic la atraviesa, y que **no sale en el mp4** (`WDA_EXCLUDEFROMCAPTURE`): el aura es para el
humano, no para el video que va al LLM.

## Las fases

### Fase 1 — el aura de aprendizaje

| | |
|---|---|
| **Promesa que pone verde** | 107 |
| **Qué toca** | nuevo `windows-client/src/Ui/AuraDeAprendizaje.cs` · `FaceWindow.xaml.cs` (tres sitios: preparar, grabar, apagar) · `WorkflowTeachSession.cs` (evento de pasos enviados) |
| **¿Núcleo congelado?** | no |
| **Terminado** | 107 verde, 1..106 intactas |
| **Sitios que dicen «grabando» hoy** | 2 (`SetTeachingUi`, `ResolveMood`) — ninguno se quita; el aura se suma |

## Lo que NO entra

- **Mover «grabando» fuera del rojo** en la carita y el botón. El aura ya distingue las dos cosas
  por sí sola (azul de Ü = aprendiendo; rojo a secas = fallo); cambiar la paleta es otra decisión.
- **El segundo monitor.** Se graba solo el principal, y el aura marca lo que se graba: pintarla
  en un monitor que no entra al video diría lo contrario de lo que pasa.
- **Reinicio en caliente / descartar desde el aura.** El aura no es interactiva (click-through);
  descartar es la fase 4 de la spec 005.

## Hallazgos

- **2026-09-02 (rojo → verde)** — La 107 salió roja por la razón escrita («todavía no existe
  ReglaDelAura») con las 106 anteriores intactas, y verde a la primera con el código. Dos sabotajes
  la devolvieron al rojo, cada uno con diff contra backup y archivo restaurado idéntico:
  (a) «terminado pero cerrando» dejaba el aura encendida; (b) opacidad constante — una franja en
  vez de un degradado.
- **2026-09-02 (el arnés juzgó un binario saboteado)** — El nivel 4 se hizo con un arnés aparte
  que muestra el aura sola (sin abrir sesión en Graph: una enseñanza de prueba deja un workflow
  basura en el catálogo). La primera corrida salió «verde» y la captura enseñaba una **franja
  dura** con el texto en mojibake («Ãœ estÃ¡ aprendiendo»): el `bin-app` del scratch era el que
  dejó el **último sabotaje**, porque el fuente se restauró pero nadie recompiló después. Y el
  mojibake tenía causa propia: PowerShell 5.1 lee un archivo UTF-8 **sin BOM** como ANSI, así que
  el script del sabotaje reescribió cada «Ü» doblemente codificada. Dos lecciones: **después del
  sabotaje se recompila antes de cualquier nivel 4**, y un script que reescriba fuentes del repo
  lee con `-Encoding UTF8` o no lee. (Mismo parentesco que el hallazgo del U.dll del 31 de agosto
  en la spec 005: un binario viejo en el scratch pasa por el código de hoy.)
- **2026-09-02 (medido por el arnés, binario correcto)** — Mostrar el aura no cambia la ventana
  en primer plano (la valla de foco de la enseñanza sigue en pie); con `WDA_EXCLUDEFROMCAPTURE`
  la captura de pantalla da 0/3800 píxeles azules en el borde y sin la exclusión 3800/3800; apagada
  desaparece y la captura vuelve a 0/3800.

## Lo que queda fuera de esta rama, dicho

La spec 005 dejó `DiscardAsync` sin cablear y el 🔄 `Collapsed`: el aura no cambia eso. Cuando se
cablee, el descarte pasa por `SetTeachingUi(false)` y el aura se apaga sola por la regla.

## Cierre

- [x] Promesa 107 verde (`.\scripts\contrato-del-grafo.ps1` → CONTRATO INTACTO, 2026-09-02)
- [x] Rota a propósito dos veces y comprobada por diff
- [x] Nivel 4 con el arnés sobre el escritorio real: foco, exclusión de captura, encendido y apagado
- [ ] Nivel 4 con una enseñanza real desde 🎓 (cuenta atrás tenue → grabación respirando → apagada
      al terminar), en ≥2 apps con nombre y con el log — **pendiente, a mano**
- [ ] Estado de este documento: **implementado** (AAAA-MM-DD)
