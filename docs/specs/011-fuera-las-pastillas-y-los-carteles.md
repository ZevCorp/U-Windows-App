# Plan de implementación: la carita deja de explicarse — fuera las pastillas y los carteles

Estado: **implementada** (162-164; 165 y 166 retiradas) · Nace de la prueba a mano de la rama `jose/la-carita-es-el-unico-boton` del
2026-09-06 · Rama: `jose/fuera-las-pastillas-y-los-carteles`

Esto **no es** un rebase de la rama 008. Es un rescate selectivo: de las cinco promesas de aquella
spec, el dueño probó la app el 2026-09-06 y se quedó con tres cosas, rechazó el anillo por no estar
terminado, y **prefirió el modelo de gestos de `main`** —un clic habla, sin doble toque que
distinguir (promesa 147)— al de la rama. Rebasar habría arrastrado justo lo que se rechazó, y sobre
cuatro archivos en conflicto en la zona de choque alto. Se porta lo elegido, y se dice cuál es.

## Diagnóstico: qué se midió

| Qué | Medida | Fuente |
|---|---|---|
| Textos al pasar el ratón vivos en la app | **44** — 30 declarados en XAML, 14 asignados en código, repartidos en **7 archivos** | `grep -rho 'ToolTip="'` y `grep -rn '\.ToolTip = '` sobre `windows-client/src/`, 2026-09-06 |
| Las tres pastillas siguen en `main` | `ZonaVoz`, `ZonaChat`, `ZonaDictado` dentro de `VoiceDotGrupo` | `FaceWindow.xaml:306-400` |
| El halo de la voz vive DENTRO de la pastilla | `VoiceHalo`, elipse de 26×26 en `ZonaVoz` | `FaceWindow.xaml:327` |
| El globo ya sabe abrirse con la carita guardada | `ShowTalk` despliega el muelle según `ReglaDelGlobo.DespliegaElMuelle` | `FaceWindow.xaml.cs:3462` (entró con el PR #54) |
| Los ojos NO siguen al cursor en `main` | `MirarHacia` solo lo usa el `Senalador` al señalar algo | `FaceWindow.xaml.cs:239, 4094, 4233` |

Lo último es lo que hace barata esta spec y caro un rebase: `main` **ya resolvió por su cuenta** el
motivo por el que la rama 008 tuvo que mudar la carita a la fila de la barra. Aquí no se toca nada
de eso.

## Por qué esto va dirigido por especificación

Toca la UI de `windows-client` —zona de choque alto— y cambia comportamiento observable en cuatro
frentes a la vez. Pero la razón concreta es otra: **el apagado de los carteles es la clase de error
del patrón nº5.** Hay 44 sitios; corregir 44 sitios a mano deja la puerta abierta a que el 45 nazca
mañana y nadie se entere. La promesa tiene que juzgar el *apagado*, no la lista — o dentro de un mes
volvemos a contar.

Y el segundo motivo: **una ausencia no se ve.** «Ya no hay pastillas» y «ningún elemento muestra
texto al pasar» son hechos negativos, y un hecho negativo no se comprueba mirando la pantalla: se
comprueba preguntándole al binario. Eso es exactamente lo que sabe hacer el contrato.

**La regla del flujo, y no tiene excepciones:** ninguna línea de producción entra antes que la
promesa que la juzga. Cada fase empieza con el contrato ROTO y termina con el contrato INTACTO.

## La especificación

| # | Promesa | Fase que la pone verde |
|---|---|---|
| 162 | fuera las tres pastillas: la carita no lleva colgando voz, chat ni dictado a SAP, y lo que otros usan de ese dictado sigue en pie | 1 |
| 163 | el halo de la voz rodea a la carita, cabe entero en el aire que tiene, y dice por dónde te oyen | 2 |
| 164 | ningún elemento de la interfaz muestra texto al pasar el ratón, ni uno que lo declare: se apaga en un solo sitio y para todo lo que se escriba después | 3 |
| ~~165~~ | ~~la línea «Escríbele…» pide reposo~~ — **RETIRADA** por el dueño el 2026-09-06 (ver *Lo que NO entra*) | — |
| ~~166~~ | ~~escribir con el ratón sobre la carita abre el globo con esa letra~~ — **RETIRADA** con la 165 | — |

**La que cierra el asunto es la 164.** Mientras el apagado sea una lista de 44 tachones, las otras
cuatro son cosmética: el cartel vuelve en cuanto alguien añada un botón.

### Con qué se juzga cada una

Las cinco se juzgan **en la propia prueba**, sin fixture ni escenario, porque las cinco son
preguntas al binario que se distribuye:

- **162** — reflexión sobre `U.dll`: los `x:Name` de XAML nacen como campos de la clase parcial, así
  que `FaceWindow` no puede tener `ZonaVoz`, `ZonaChat`, `ZonaDictado` ni `VoiceDotGrupo`; y
  `RellenadorSap`, `SapGuiSurface` y `DictadoEnVivo` **sí** tienen que seguir existiendo — quitar la
  entrada no es matar lo que la consulta, la demo y la exportación a HC usan.
- **163** — `ReglaDelHalo`, pura: la escala máxima por el tamaño de la carita cabe en la ventana, y
  el color sale de por dónde entra el audio.
- **164** — el contrato es `net8.0-windows` con `UseWPF` y `[STAThread]`: puede crear un
  `FrameworkElement` de verdad, ponerle un `ToolTip`, aplicar la regla y comprobar que **no se
  muestra**. Es la única forma de juzgar el apagado en vez de la lista.
- **165** — `ReglaDeLaLinea`, pura: reposo mínimo y qué lo cancela.
- **166** — `ReglaDeEscritura`, pura (se porta tal cual de la rama 008, donde ya se sabotearon sus
  casos): qué teclas escriben y cuáles no se roban nunca.

## Las fases

### Fase 1 — la carita no lleva nada colgando

| | |
|---|---|
| **Promesa que pone verde** | 162 |
| **Qué toca** | `Ui/FaceWindow.xaml`, `Ui/FaceWindow.xaml.cs`, `Onboarding/Presentacion.cs` |
| **¿Núcleo congelado?** | no |
| **Terminado** | 162 verde, 1..161 intactas |
| **Sitios con esta clase de error** | la pastilla de voz se pintaba desde **4** sitios (`Cambio`, `FuenteCambio`, `ToggleCollapsed`, la boca); los 4 pasan por el halo |

### Fase 2 — el halo rodea a la carita

| | |
|---|---|
| **Promesa que pone verde** | 163 |
| **Qué toca** | `Ui/ReglaDelHalo.cs` (nuevo), `Ui/FaceWindow.xaml`, `Ui/FaceWindow.xaml.cs` |
| **Terminado** | 163 verde, 1..162 intactas |

### Fase 3 — se apagan los carteles, en un solo sitio

| | |
|---|---|
| **Promesa que pone verde** | 164 |
| **Qué toca** | `Ui/SinCarteles.cs` (nuevo), `App.xaml.cs`, y los 44 tachones en los 7 archivos |
| **Terminado** | 164 verde, 1..163 intactas, y `grep -c ToolTip` sobre `windows-client/src` en **0** |
| **Sitios con esta clase de error** | **44** en **7** archivos, contados con grep |

El apagado va primero y los tachones después, en ese orden y en el mismo commit: el interruptor es
la promesa, y borrar las declaraciones es no dejar código inerte que aparente hacer algo.

### Fase 4 — la línea pide reposo

| | |
|---|---|
| **Promesa que pone verde** | 165 |
| **Qué toca** | `Ui/ReglaDeLaLinea.cs` (nuevo), `Ui/FaceWindow.xaml`, `Ui/FaceWindow.xaml.cs` |
| **Terminado** | 165 verde, 1..164 intactas |

En la rama 008 la línea salía **al instante** de entrar el ratón (`OnCollapsedHoverIn` llamaba a
`MostrarGhost` sin esperar). El dueño lo probó y pidió lo contrario: que ir a agarrar la carita para
arrastrarla no invoque un cartel. Así que el reposo no es un número de gusto — es lo que separa
«voy a escribirle» de «voy a moverla».

### Fase 5 — escribirle es escribir

| | |
|---|---|
| **Promesa que pone verde** | 166 |
| **Qué toca** | `Ui/ReglaDeEscritura.cs` y `Ui/TeclaReinyectada.cs` (portados), `Ui/AtajoPorGolpes.cs`, `Ui/FaceWindow.xaml.cs` |
| **Terminado** | 166 verde, 1..165 intactas |

Abre **el globo de `main`**, por el camino de `main` (`ShowTalk`), sin tocar cómo se ve ni cómo se
despliega. Es el requisito explícito del dueño: lo que pasa al hablarle a la carita se conserva.

## Lo que NO entra, y por qué

| Qué | Por qué |
|---|---|
| **El anillo** (`AnilloDeAcciones`, `ReglaDelAnillo`) | «me encanta, pero todavía no está lo suficientemente perfeccionado para llevarlo a main» — dueño, 2026-09-06 |
| **El modelo de gestos de la 008** (`ReglaDeGestos`: toque / doble toque / mantener) | `main` ya decidió lo contrario en la promesa 147 y el dueño prefiere el de `main`. Traerlo sería reabrir una decisión ya tomada y medida |
| **Los ojos siguiendo al cursor** | pedido explícito de no traerlo. En `main` no existe, así que no hay nada que quitar: solo no portarlo |
| **La pista de gestos** (`ReglaDeDescubrimiento`, «toca: hablar · escribe: texto · mantén: menú») | es un cartel, y los carteles se van (164). Además explica gestos que aquí no entran |
| **Cualquier cambio al globo de conversación** | «cuando hablo con la carita, que se conserve tal como está en la rama main» |
| **`mac-client/`** | desde Windows no se toca (`.claude/rules/solo-mac.md`) |

## Riesgo

La zona es `windows-client/` UI: **choque alto**. La rama 008 sigue abierta en el PR #51 sobre estos
mismos archivos, y esta spec la deja obsoleta en tres de sus cinco promesas — al mergear esto, aquel
PR se cierra o se reduce a lo que quede (el anillo). Se avisa en `#miracle-updates`.

## Acta de retiro de las promesas 165 y 166 (2026-09-06)

Llegaron a verde y las dos pasaron su sabotaje. Las retira el dueño después de verlas en pantalla:
la burbuja «Escríbele…» no le convence, y la probó en dos formas —opaca y de cristal oscuro con
tipografía de sistema— antes de decidir. **Los números no se reciclan.**

Se retiran las dos juntas y no solo la línea, a propósito: la 166 es un gancho global de teclado
que se traga letras, y sin la 165 no queda nada en pantalla que diga que puede pasar. Un gancho
invisible que roba teclas sin afordancia es peor que no tener el gesto.

**El hueco que deja está cubierto.** Al morir la pastilla del chat (162) se fue la única puerta al
globo *con el ratón desde la carita suelta*, pero quedan dos que no dependen de ella: `Ctrl+Alt+U`,
que abre el globo con el foco ya en la caja, y el muelle del borde derecho, que lo tiene a un cursor
de distancia. Esto se comprobó leyendo `InvocarPorAtajo` y `Muelle`, no se supone.
