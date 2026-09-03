# Plan de implementación: la carita es el único botón

Estado: **propuesto** · Nace del diagnóstico del 2026-09-02 · Rama: `jose/la-carita-es-el-unico-boton`

> **La petición, en una frase:** quitar los botones que asoman al lado de la carita y que la
> carita misma sea la interacción —o una forma más rápida y sencilla de llegar a lo mismo—, y
> borrar del todo el de «Dictar y rellenar los campos de SAP», que ya no se usa.

## Diagnóstico: qué se midió

En esta máquina no hay logs de Ü (`%LOCALAPPDATA%\U\logs` no existe), así que la medida es del
código y del repo, el 2026-09-02, no del terreno. Se dice para que nadie la lea como uso real.

| Qué | Medida | Fuente |
|---|---|---|
| Dónde viven las tres pastillas | solo en el estado colapsado: `VoiceDotGrupo` dentro de `CollapsedGroup`. Con la barra abierta no hay micrófono ni chat a la vista | `FaceWindow.xaml:318-403` |
| Qué hace «Escribirle a Ü» en ese estado | **nada visible**: `ShowTalk` empieza con `if (_collapsed) return;` | `FaceWindow.xaml.cs:2729` |
| Cuánto cuesta hablarle | dos gestos que hay que saberse: doble clic, o acercarse y acertarle a una pastilla de 4,5 px; el toque simple abre la barra | `WireFaceGestures` `:1772-1804` |
| En qué se gasta mantener oprimida 750 ms | en cambiar de tema claro/oscuro | `:1778`, `:1791` |
| Cómo se abre el panel de herramientas | solo con Ctrl+Shift dos veces; el chevron de la barra está muerto desde el 2026-08-31 | `:2443`, `AlternarPanelDesarrollo` `:2361` |
| Entradas al dictado clínico | **una**: la pastilla. `_dictadoClinico` solo se toca en `:900-910`, `:1638-1666` y `:2999`. Ningún atajo, comando de voz, MCP ni acceso directo lo llama | grep de `_dictadoClinico`, `Clinical` en `src/Mcp`, `src/Agent`, `src/Actions` |
| Quién más usa sus piezas | `RellenadorSap` → `EjecutorDeExportaciones` (`:917-920`, sin botón, desde el arranque); `SapGuiSurface` → la demo; la clase `DictadoEnVivo` → `ConsultaWindow` (spec 004/005, promesas 84-99) | grep |
| Clic derecho, bandeja del sistema, menú contextual | no existen en `windows-client/src` | grep `ContextMenu`, `NotifyIcon` |
| Lo que ya sabe hacer la carita y no se usa para esto | mirar hacia un lado (`MirarHacia`), un halo que late con el nivel de voz (`VoiceHalo`, hoy detrás de la pastilla), un gancho de teclado global (`AtajoPorGolpes`) | `FaceControl.cs:351`, `FaceWindow.xaml.cs:1686-1746`, `AtajoPorGolpes.cs` |
| Cómo lo resuelve el Mac | clic derecho en la carita con menú contextual, y un interruptor en la barra de menús. Su archivo advierte: *«Un gesto que hay que saber no es un botón; es un secreto»* | `mac-client/Sources/U/Interruptor.swift:5-12`, `main.swift:504` |
| Qué constriñe el contrato | nada de la ventana: la única promesa de UI es la 107, sobre la regla pura `ReglaDelAura`. Es el patrón a copiar | `Contrato.cs:3426` |

## Por qué esto va dirigido por especificación

Porque un mapa de gestos se «arregla» cableando y a la semana nadie sabe qué hace cada gesto ni
por qué; y porque lo que se promete se puede juzgar sin pantalla si la decisión vive en una regla
pura y el cableado solo la pregunta: **qué intención tiene cada gesto**, **qué teclas abren el
globo y cuáles no se roban**, **dónde cabe el anillo**, y **cuándo se calla la pista**. La 107 ya
demostró que un overlay puede juzgarse así.

**La regla del flujo, y no tiene excepciones:** ninguna línea de producción entra antes que la
promesa que la juzga. Cada fase empieza con el contrato ROTO y termina con el contrato INTACTO.

## La experiencia

**Lo más frecuente cuesta un solo gesto; lo demás se descubre desde la carita, no desde botones.**

| Gesto | Hoy | Ahora | Por qué |
|---|---|---|---|
| Un toque | abre la barra | **hablar** | es lo que más se hace |
| Doble toque | micrófono | **abrir/cerrar la barra** | doble clic = «abrir» en todo Windows |
| Mantener 750 ms | cambiar tema | **el anillo** | el tema no merece un gesto principal; va al panel |
| Clic derecho | nada | **el anillo** | el «más» de Windows; el Mac ya lo hace |
| Arrastrar, lanzar, dos dedos | mover al borde | igual | |
| Acercar el ratón | tres pastillas | **los ojos siguen al cursor** y una línea tenue «Escríbele…» | la línea ES el campo de texto, no un botón que abre otra cosa |

- **Escribir sin botón.** Clic en la línea, o simplemente teclear con el ratón encima: el globo se
  abre con esa letra puesta. Ctrl/Alt+tecla, F1-F12, Esc, Enter, Tab y flechas no se roban. Enter
  envía; Esc cierra y devuelve el teclado a la app de antes.
- **El anillo.** Cinco burbujas en abanico alrededor de la carita, hacia el centro de la pantalla:
  🎓 Enseñar/Terminar · ▶ Workflows · 📿 Collar · 👁 Ocultar · ⋯ Más (el panel de hoy). Mantener →
  deslizar → soltar elige en un solo gesto; con clic derecho se hace clic. La carita mira hacia él.
- **La carita dice lo que pasa.** Escuchando: el halo late alrededor de la carita, azul si es el
  collar, gris si es el micro del PC. Se respeta `UiPalette`: rojo sigue siendo fallo/grabando.
- **La pista.** Tres veces bajo la carita —«toca: hablar · escribe: texto · mantén: menú»— y calla.

## La especificación

En continuación de la 111 (spec 007).

| # | Promesa | Fase |
|---|---|---|
| 112 | la carita es el único botón: no quedan pastillas ni dictado clínico colgando de ella | 1 |
| 113 | un toque habla, dos abren la barra, mantener o clic derecho abren el anillo, arrastrar mueve | 2 |
| 114 | escribir con el ratón encima abre el globo con esa letra; atajos, teclas F y Esc siguen su camino | 3 |
| 115 | el anillo abre hacia el centro de la pantalla, cabe entero en el área de trabajo y ningún item pisa a otro ni a la carita | 4 |
| 116 | la pista de gestos se enseña tres veces y calla | 5 |

La que cierra el asunto es la **113**: mientras un toque no hable, quitar las pastillas deja a la
carita sin forma de hablarle que no sea un gesto secreto.

### Con qué se juzga cada una

Mapa a mano en la propia prueba, por reflexión sobre `U.dll`, como la 107:

- **112** los campos de `FaceWindow`: el XAML genera uno por `x:Name`, así que «no queda pastilla»
  es «no queda campo» (`ZonaVoz`, `ZonaChat`, `ZonaDictado`, `VoiceDotGrupo`), y «no queda
  dictado» es «no hay campo de tipo `DictadoEnVivo`». Y a la vez que la capacidad sigue:
  `DictadoEnVivo` existe y `RellenadorSap` sigue en la carita.
- **113** `ReglaDeGestos.Decidir(Gesto) → Intencion`, un caso por gesto.
- **114** `ReglaDeEscritura.Abre(vk, ratónEncima, ctrl, alt)`: letra, dígito, signo y numérico
  abren; sin ratón encima, con Ctrl/Alt, F1/F5, Esc/Enter/Tab/flecha, espacio y retroceso, no.
- **115** `ReglaDelAnillo.Posiciones(centro, pegadaALaIzquierda, n, área)` en las cuatro esquinas
  de 1920×1040: cinco puntos, todos hacia el centro, cada uno entero dentro del área, ninguno
  sobre otro ni sobre la carita; y `ElegirEn` elige el tercero al soltar sobre él y nada al soltar
  sobre la carita. **El anillo real se coloca llamando a esta misma regla** (aprendizaje nº16).
- **116** `ReglaDeDescubrimiento.Mostrar(veces)`: 0, 1, 2 sí; 3 y 9 no.

Nivel 4, a mano y con log, en **≥2 pantallas** (SAP GUI y Chrome o el explorador): toque = carrillón
y escuchando; doble = barra; mantener/clic derecho = anillo en las cuatro esquinas; deslizar-soltar
elige; hover + «h» abre el globo con «h»; Ctrl+S, F5 y Esc con el ratón encima llegan a SAP; Esc
devuelve el foco; el globo se abre junto a la carita suelta **sin que se mueva**.

## Las fases

| Fase | Promesa | Qué toca |
|---|---|---|
| 0 | — | esta spec y las cinco promesas en rojo |
| 1 | 112 | `FaceWindow.xaml` (fuera `VoiceDot` y `VoiceDotGrupo`; `VoiceHalo` pasa detrás de la carita), `FaceWindow.xaml.cs` (fuera pastillas, `OnDictadoDesdePastilla`, `PintarDictado`, `Recorte`, el cableado `:900-910`, `_dictadoClinico`, `_audioDictado`; `PintarBotonVoz`/`EsconderBotonVoz` → `PintarHalo`), `Onboarding/Presentacion.cs:33` |
| 2 | 113 | nuevo `Ui/ReglaDeGestos.cs`; `WireFaceGestures`; `FaceGestures` gana clic derecho; el tema pasa a un botón del `MenuPanel`; ojos hacia el cursor |
| 3 | 114 | nuevo `Ui/ReglaDeEscritura.cs`; un solo `DockPanel` para los dos estados (`CollapsedHost` dentro de `BarRow`) para que `TalkPanel` se acople a la carita suelta; `ShowTalk` sin el `return`; `GhostInput`; `AtajoPorGolpes` gana `interceptarTecla` y respeta `LLKHF_INJECTED` |
| 4 | 115 | nuevos `Ui/ReglaDelAnillo.cs` y `Ui/AnilloDeAcciones.cs` (satélite como `AuraDeAprendizaje`, sin click-through); `FaceGestures` deja de arrastrar tras `_longFired` y avisa del deslizar/soltar; `Ocultarse()` sale de `AtenderAutocontrol` |
| 5 | 116 | nuevo `Ui/ReglaDeDescubrimiento.cs`; `Config.PistasDeLaCaritaMostradas`; la pista como `Popup`; `windows-client/CLAUDE.md` y `CLAUDE.md` |

**Sitios con la clase de error** (patrón nº5), contados con grep antes de la fase 1: la pastilla de
voz se pinta desde **4** sitios (`_vivo.Cambio` `:776`, `FuenteCambio` `:791`, `ToggleCollapsed`
`:1486`, la boca `:3780`); todos pasan a `PintarHalo`. Ninguno está en el núcleo congelado.

## Lo que NO entra

- **Retirar la barra expandida.** Se queda, y el doble toque la abre. Los contextuales ⏹ ⬇ 🔄
  como satélites son otra spec.
- **`mac-client/`.** Su mapa diverge (toque = pulso, doble = oído) y desde Windows no se toca
  (`solo-mac.md`). Se anota para que el dueño de ese lado decida si converge.
- **Icono en la bandeja.** El «interruptor» del Mac. Otra decisión.
- **Separar el rojo de «grabando» del de «fallo».** Sigue documentado en `UiPalette`.
- **Palabra de activación por voz.** El Mac la tiene y midió que no dispara; aquí ni se intenta.
- **Borrar `RellenadorSap.Deshacer()` y el evento `Escribio`**, que quedan sin llamadas: son del
  rellenador, que sigue vivo por la exportación. Se anota como hallazgo.

## Hallazgos

- **2026-09-02 (spec)** — «Escribirle a Ü» llevaba desde su nacimiento sin hacer nada visible en el
  único estado donde existía: la pastilla vivía colapsada y el globo se negaba a abrirse colapsado.
  Nadie lo reportó, que es la señal de que nadie lo usaba.
- **2026-09-02 (spec)** — `CLAUDE.md` y `windows-client/CLAUDE.md` siguen describiendo «🧪 Ensayo
  en seco» y «👣 Paso a paso» como botones del panel; se quitaron el 2026-08-31. Se corrige en la
  fase 5.
- **2026-09-02 (spec)** — `Presentacion.cs:33` presenta a Ü diciendo que rellena triage
  «dictándotela»; sin la pastilla eso deja de ser verdad desde Windows.

## Cierre

- [ ] Promesas 112-116 verdes (`.\scripts\contrato-del-grafo.ps1` → CONTRATO INTACTO)
- [ ] Rotas a propósito una por una, comprobadas por diff
- [ ] `.\scripts\verificar.ps1` pasa, con evidencia en `out\evidencia.md`
- [ ] Probado en ≥2 pantallas, con nombre: …
- [ ] Estado de este documento: **implementado** (AAAA-MM-DD)
