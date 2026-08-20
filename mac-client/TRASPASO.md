# Traspaso — Ü en Mac, sesión del 2026-08-18

> **Para quien retome esto en otro chat.** Léelo entero antes de tocar nada: casi todo lo de abajo
> costó una ronda de diagnóstico, y varias de las trampas son invisibles hasta que muerden.
>
> Empieza por: `CLAUDE.md` → `.claude/rules/solo-mac.md` → este archivo → `mac-client/PENDIENTES.md`.

## Lo primero, y no es negociable

**Aquí solo se toca `mac-client/`.** La regla está escrita en
[`.claude/rules/solo-mac.md`](../.claude/rules/solo-mac.md) e importada desde `CLAUDE.md`. Nada de
`windows-client/`, `windows-graph/`, `tests/ContratoDelGrafo/` ni `scripts/*.ps1`.

El motivo no es de gustos: la compuerta del repo —los cuatro niveles— es PowerShell + .NET sobre un
escritorio con SAP GUI, y **en este Mac no corre ninguno**. Un cambio en el lado Windows desde aquí
entra a ciegas, que es exactamente lo que la compuerta existe para impedir. Además la UI de
`windows-client` es la zona de choque de las tres personas del equipo.

Sí se puede **leer** el lado Windows, y hay que hacerlo: es la referencia de comportamiento. Lo que
no se hace es copiar archivos — se reescribe en Swift el *comportamiento* y el *porqué*.

## Estado

| | |
|---|---|
| Rama | `isa/las-caras-de-la-carita-en-mac` |
| Commits | **ninguno todavía** — todo está sin commitear en el árbol de trabajo |
| `main` | 2 commits por delante, **cero tocan `mac-client/`** (son Windows, `nucleo/`, `mapeador/`, docs) |
| Compuerta | nivel 1 (compila en Release). Niveles 2–4 **no existen en Mac** |
| Probado | corriendo de verdad, con el registro como evidencia. Ver «Qué está verificado» |

Rebasar sobre `main` está pendiente y **no urge**: no hay solape de archivos. Conviene hacerlo antes
del PR, y habrá un conflicto de una línea en `CLAUDE.md`.

## Qué hay en la rama

### Archivos nuevos

| Archivo | Qué es |
|---|---|
| `mac-client/Sources/U/Vivo.swift` | La conversación en vivo con Gemini: socket, setup, envío, recepción, reconexión |
| `mac-client/Sources/U/AudioVivo.swift` | Micrófono y altavoz en crudo. Captura a 16 kHz, reproduce a 24 kHz, cancelación de eco |
| `mac-client/Sources/U/Retratos.swift` | La app se retrata a sí misma: un PNG por cara en `~/.u/caras/`. **Compila, nunca se ha corrido** |
| `mac-client/PENDIENTES.md` | Lo aparcado a propósito, con las trampas del día |
| `.claude/rules/solo-mac.md` | La regla de arriba |

### Archivos tocados

| Archivo | Qué cambió |
|---|---|
| `main.swift` | La derivación de la cara (`resolverCara`), los flags, el cableado de la voz viva, los items de menú, los mandos nuevos |
| `Oido.swift` | `hayVoz`, `alCambiar`, y `ceder()`/`recuperar()` para prestarle el micrófono a la sesión viva |
| `Voz.swift` | `alEmpezar` — la voz avisa, ya no pone la cara |
| `FaceView.swift` | `alAcabarDeDisculparse` — la vista avisa, ya no se reasigna el estado |
| `Llamado.swift` | «mira» y «mire» como nombre |
| `Cerebro.swift` | Partido en `quienEs` + `lasCaras` + `instrucciones`, para compartir identidad entre las dos bocas |
| `CLAUDE.md` | Una línea: el import de la regla nueva |

---

## Lo que se hizo, y por qué

### 1. El nombre: «Mira»

**El síntoma:** «le hablo y no me contesta».

**Lo que decía el log:** 822 frases oídas y transcritas correctamente, y las 822 rechazadas con
`👂 no era para mí`. Estaba dormida esperando su nombre.

| Cómo empezaban las frases | Veces |
|---|---|
| alguna forma de «Ü» (`u`, `you`, `hu`…) | **0** |
| «mira» | **6** |

«Ü» es una vocal suelta y en español hablado se pierde: el reconocedor la escribe de siete maneras y
ninguna cayó nunca al principio de una frase. En todo el historial despertó **3 veces de 822**, y las
tres por accidente («Hola You me escuchas», «You You», «You're broke»).

**Decisión:** «mira» y «mire» entran como nombre, **sin filtrar por la palabra siguiente**. Se probó
un filtro contra las 6 llamadas reales y tiraba dos («Mira acá pues…», «Mira una de las…»). Se acepta
el falso positivo —despertar cuando dices «mira, te cuento» a otra persona— porque dura 30 s y se
corta solo, mientras que rechazar una llamada de verdad rompe la conversación.

### 2. La cara: de ocho sitios a uno

**El síntoma:** «hace otra cara, no la que cuadramos».

**La causa:** había **ocho sitios** escribiendo `mood = .algo` a mano —el oído, la voz, la vista y
cinco puntos de la conversación—. Ganaba siempre el último que corriera, que no es el que sabe.

**La regla que se aplicó** viene del cliente de Windows (`FaceWindow.xaml.cs`, `ResolveMood`):

> El estado NO se asigna a mano desde cada sitio: se DERIVA de los flags que ya existen, en una sola
> función.

Ahora está en `main.swift`, `resolverCara()`, y **el orden es la prioridad**:

```
1  celebrando                                     → logrado
2  fallando (o trabajando si ya pidió perdón)     → fallo
3  vivo.hablando || voz.hablando                  → hablando
4  pensando                                       → pensando
5  !oido.continuo                                 → detenido
6  vivo.viva                                      → conversando
7  despierta && (oido.hayVoz || vivo.teOye)       → escuchando
8  despierta                                      → conversando
9  —                                              → reposo
```

Dos decisiones que viven en el ORDEN y hay que respetar al tocarlo:

- **`detenido` gana a `conversando`.** Si te callaron el micrófono en mitad de una conversación,
  `despierta` sigue puesto — pero enseñar cara de conversar mientras está sorda te deja hablándole a
  algo que no te oye.
- **`escuchando` es «te oigo decir algo *ahora*»**, acotado a la frase. No es «el micrófono está
  abierto», que lo está siempre y sería una cara fija. Micrófono abierto sin nadie hablando es
  `reposo` — así lo dice el catálogo de Isabel.

**Efecto colateral que valía la pena:** `escuchando` y `detenido` estaban dibujados, con su pose y su
color, y **no los ponía nadie**. Ahora salen.

**Se quitó** el guiño (`.complice`) al encender el micrófono y la cara de sorpresa (`.pillado`) al
apagarlo. Son **talantes del espejo**, con su condición escrita —«los dos sabemos de qué va esto»,
«lo cacharon»— y un micrófono apagado no es ninguna de las dos. Gastados como acuse de recibo de un
botón, además, duran medio segundo: un segundo después no sabías en qué había quedado. Lo que hacía
falta era un **estado** que se quedara puesto, y ese es `detenido`.

**`caraForzada`** existe justo porque ahora se deriva: elegir un estado en el menú lo clava por encima
de la derivación hasta soltarlo con *↩︎ Automático*. Sin eso, la primera palabra que se oyera borraría
la cara que estás mirando.

### 3. El catálogo manda

Las condiciones de las caras **no se inventan**: están en el artefacto «Las caras de Ü» que escribió
Isabel, y cada cara nueva trae etiquetado quién la dispara.

- **«Sola»** → condición mecánica, va en el código: `te escucha`, `pensando`, `logrado`, `no entendí`.
  Las cuatro están cableadas.
- **«La elige ella»** → se queda en el prompt (`Cerebro.lasCaras`), que es el corazón del producto
  (el espejo).

Durante la sesión se intentó poner «micrófono abierto → cara de escuchando» y **se revirtió el mismo
día**: el catálogo dice que eso es `reposo`. Si un cambio de cara contradice el catálogo, el catálogo
gana o se cambia el catálogo primero.

### 4. La voz en vivo de Gemini

Portada de `windows-client/src/Voice/GeminiLive.cs` y `LiveAudio.cs` (leídos de `origin/main`, **no
copiados**). Lo esencial del protocolo:

| | |
|---|---|
| Socket | `wss://…/BidiGenerateContent?key=` |
| Audio | envía **PCM16 16 kHz**, recibe **PCM16 24 kHz**, mono |
| Modelo | se PREGUNTA (`bidiGenerateContent`), no se escribe a mano. Hoy cae en `gemini-3.1-flash-live-preview` |
| Voz | `Iapetus`, fija — sin pedirla el servidor sortea una distinta cada sesión |
| Idioma | `es-US` explícito — sin pedirlo transcribía portugués |
| Turno | detección del servidor: `LOW/LOW/700ms/20ms` |
| Caídas | `sessionResumption` — el socket se cae solo cada pocos minutos |

**El portero se queda.** El oído local reconoce el nombre sin que salga un byte del Mac y sin costar
nada; la sesión viva solo se abre al despertar. Al revés —el caño abierto todo el día— sería mandarle
a Google la jornada entera y pagarla.

**La cara se pide como herramienta.** Por texto el talante viaja en `[burlon]` y llega medio segundo
antes del primer sonido; en audio nativo no hay dónde meterlo y la transcripción llega *detrás* del
audio. Así que se declara `poner_cara(talante)` como function call, que sí llega antes. Verificado:

```
🎙 cara «sobrado»              ← 01:45:26.569
🎙 dice: «Todo funciona.»      ← 01:45:27.198
```

**Lo que NO se trajo, a propósito:** los ojos (un fotograma por segundo de la pantalla), el collar
Omi, las herramientas del grafo de SAP y el reporte de consumo. Ninguna tiene sentido en Mac todavía.

---

## Las trampas. Leer antes de tocar audio

Cinco cosas que fallaron en silencio. Ninguna daba error.

### 1. Un conversor de 9 canales a 1 devuelve SILENCIO

El micrófono de esta MacBook entrega **9 canales**. `AVAudioConverter` sabe cambiar ritmo y tipo de
muestra, pero para bajar de 9 a 1 necesita `channelMap`; sin él **no mezcla, devuelve ceros** — y lo
hace sin error: contesta que sí y entrega un buffer del tamaño correcto lleno de nada.

```
🎙 crudo 9ch → [0.178 0.178 … 0.178]     ← el micrófono oye perfectamente
🎙 micrófono → 21 trozos · nivel 0.0000  ← lo que salía del conversor
```

**Once sesiones abiertas y cero transcripciones**, con socket, altavoz, cara y cancelación de eco
funcionando. Arreglo: `conversor?.channelMap = [0]`.

**La lección de método:** la voz se dio por buena habiéndola probado solo con `U_DECIR` (texto). La
única pieza sin comprobar era la que fallaba. **«Abrió» y «oye» son cosas distintas.**

### 2. Nombrar `mainMixerNode` es cablearlo

Es perezoso: la primera vez que se menciona, AVAudioEngine lo crea **y** lo conecta a la salida al
ritmo que él elige (44 100 Hz), mientras la salida con procesado de voz exige 48 000. Ese desacuerdo
mataba el arranque con `-10875 · PerformCommand(*outputNode, kAUInitialize)`, y reconectarlo después
no lo deshace. El reproductor va **directo a `outputNode`**.

**Y lo peor:** la línea de diagnóstico puesta para averiguar por qué fallaba **era una de las
causas** — medía `mainMixerNode.outputFormat` y al medirlo lo creaba. Tres intentos de arreglo
fallaron porque la sonda reintroducía el fallo en cada vuelta. *Si una medición toca un objeto
perezoso, la medición es parte del sistema.*

### 3. Correr el binario suelto la mata por privacidad

`./U.app/Contents/MacOS/U` desde la terminal aborta con `SIGABRT` y
`__TCC_CRASHING_DUE_TO_PRIVACY_VIOLATION__`, diciendo que falta `NSSpeechRecognitionUsageDescription`
— **que sí está** en el `Info.plist`. Lanzada con `open` no pasa. **Siempre `open`.**

### 4. Un pase de reanudación caducado no dice «caducado»

El servidor acepta el socket y lo cierra al instante («Socket is not connected»), que se parece a un
problema de red y no lo es. Reintentar con el mismo pase reproduce el mismo cierre para siempre.

### 5. Un contador que se reinicia al abrir no cuenta nada

`reintentos` volvía a cero al **abrir el socket**; el socket moría a los 200 ms pero alcanzaba a pasar
por esa línea. El tope de tres intentos no se alcanzaba nunca → **2 089 vueltas de reconexión**, una
por segundo, abriendo y cerrando el motor de audio. Visto desde fuera: el punto naranja del micrófono
parpadeando sin parar con la app «cerrada».

Ahora se pone a cero **solo cuando llega contenido de verdad** (`serverContent`). Abrir prueba que el
socket saludó, no que la conversación exista.

### Y dos de comportamiento

- **Contestaban los dos cerebros a la vez.** Al despertar se abría la sesión viva *y* seguía el camino
  de texto: `le cedo el micrófono a la voz en vivo` y en el mismo milisegundo `🧠 le pregunto`. Ahora,
  si la sesión viva se hace cargo, el texto no corre — y lo que dijiste detrás del nombre se le pasa
  por escrito con `vivo.decirle(...)`.
- **Lo que falla no se celebra.** La carita bailó después de decir «no pude abrir la voz en vivo»: la
  celebración solo miraba que hubiera un turno abierto, y un turno abierto lo hay también cuando lo
  que suena es un error. El baile dice «terminé lo que me pediste»; encima de un «no pude» enseña a
  no creerle a la cara.

---

## Cómo se prueba

**Siempre con `open`.** El log es la fuente de verdad: `~/.u/u.log`.

```bash
open --env U_VIVO=1 --env "U_DECIR=hola, cuéntame algo" "…/mac-client/U.app"
```

| Mando | Para qué |
|---|---|
| `U_VIVO=1` | abre la conversación en vivo al arrancar, sin llamarla por su nombre |
| `U_DECIR="…"` | le manda esa frase por escrito a la sesión viva |
| `U_BARGE_IN=1` | deja que la interrumpan hablándole encima (hoy apagado) |
| `U_LIVE_MODEL=…` | fuerza un modelo de voz concreto |
| `U_RETRATOS=1` | se retrata sola en `~/.u/caras/` y sale. **Usar con `U_PESO=1`** |
| `U_CARA=…` `U_TALANTE=…` `U_PESO=…` | clavar un estado / disparar un talante / su intensidad |
| `U_ATENCION` `U_TRANSICION` `U_CELEBRA` | congelar el ladeo / la transición / el brinco |
| `U_VOZ=…` | forzar una voz de macOS en el camino de texto |

Líneas útiles del log: `🎙 micrófono → … nivel` (si dice `⚠︎ SILENCIO` el micro no llega),
`🎙 oye:`, `🎙 dice:`, `🎙 cara «…»`, `🎙 me interrumpiste`, `👂 ✦ me despertaron`,
`👂 no era para mí`, `🎙 ←` (mensajes del servidor sin interpretar).

**No matar y relanzar en menos de 3–4 segundos:** el dispositivo de audio queda ocupado y la sesión
se cuelga abriendo.

La llave vive en `~/.u/gemini-key.txt` (o `GEMINI_API_KEY`). La de Isabel tiene formato `AQ.Ab8…`, 53
caracteres — distinto del clásico `AIza` de 39, y **funciona**: 50 modelos, 6 con voz en vivo.

## Qué está verificado y qué no

**Verificado corriendo, con el log como evidencia:**

- La sesión abre con cancelación de eco: `eco: entrada sí · salida sí`
- Los ritmos: `entra 48000 Hz → 16000 Hz · sale 24000 Hz → 48000 Hz`
- Oye de verdad: `🎙 oye:` con transcripción en español y nivel de entrada > 0
- Habla en español y elige cara, **y la cara llega antes que el audio**
- Sin bucle de reconexión, y sin cortes: **1 interrupción en 26 s** (antes eran decenas)

**Sin verificar:**

- **Interrumpirla hablándole encima.** No se puede probar desde aquí: la cancelación de eco se come
  el altavoz de prueba (nivel cae a 0.006), que es justo lo que debe hacer. Hace falta una persona.
- **Los retratos.** `Retratos.swift` compila y **nunca se ha corrido**. `~/.u/caras/` está vacío.
- **Que el modelo pida cara con la frecuencia justa.** Hoy pide bastante (`perdido` 17, `confundido`
  8, `sobrado` 7, `enojado` 7, `risa` 6, `burlon` 6…), pero está sin juzgar si se pasa o se queda
  corta.

## Lo que sigue, en orden

1. **Correr los retratos y mirar las caras.** Isabel dijo que hay algunas que no le gustaron después
   de conversar de verdad. Sin verlas juntas no se puede decidir cuál cambiar.
   ```bash
   open --env U_RETRATOS=1 --env U_PESO=1 "…/mac-client/U.app"
   ```
   Salen en `~/.u/caras/`, un PNG por cara, en claro y en oscuro. **Está sin correr: puede fallar.**

2. **Devolverle el portero a la sesión viva.** Es el pendiente que más pesa, y cuesta dinero: mientras
   está abierta, **todo lo que suena en la sala va a Gemini y se paga**. El temporizador de 30 s no la
   cierra porque cualquier voz cuenta como conversación. En el registro se vio a Ü contestándole a una
   conversación ajena y preguntando literalmente «¿me están hablando a mí?». Hoy se cierra a mano
   (doble toque, o el menú *⚡︎ Voz en vivo — colgar*).

3. **Un contrato en Swift.** No hay nivel 2 de la compuerta en Mac, y con la voz en vivo dentro pesa
   más que ayer: es el subsistema más grande sin juez. Ya hay medio arnés hecho (`U_VIVO` + `U_DECIR`);
   falta el guion que arranque, mande una frase y **exija** en el log micrófono > 0, `oye:`, `cara`,
   `dice:` y cero bucles.

4. **Lo aparcado**, en `PENDIENTES.md`: si `detenido` se distingue de `reposo` de un vistazo, cuántos
   falsos positivos da «mira» en un día de uso, y si `escuchando` cansa.

5. **Commitear.** No hay ni un commit. Conviene partirlo en varios (el nombre, la derivación de la
   cara, la voz en vivo) y rebasar sobre `main` antes del PR.

## Lo que NO se debe hacer

- **No poner un detector de voz propio por volumen.** En Windows estuvo del 4 al 16 de agosto y hacía
  imposible interrumpirla: usa UN número —el volumen— para dos preguntas que por volumen son
  indistinguibles («¿es mi eco?» y «¿me están cortando?»). Cuanto mejor tapaba el eco, más había que
  gritar. Los síntomas fueron «nunca puedo interrumpirlo» y «me toca hablarle muy duro».
- **No usar talantes del espejo como acuse de recibo de la interfaz.** Tienen su condición escrita.
- **No dar por buena la voz sin probar el micrófono.** Ya pasó.
- **No tocar `mainMixerNode`** en la cadena de audio en vivo, ni siquiera para medirlo.
- **No lanzar el binario suelto.**

## Referencias

- **«Las caras de Ü»** — el catálogo de Isabel, con las condiciones de cada cara y quién la dispara.
  Es la fuente de verdad del comportamiento visual.
- **«Mira por dentro»** — https://claude.ai/code/artifact/f05afa3d-bffa-4a47-8816-1c74ffb8b36b —
  la cascada, las condiciones, dónde se adaptan y qué sabe hacer hoy.
- `mac-client/PENDIENTES.md` — lo aparcado, con las trampas del día.
- `windows-client/src/Voice/GeminiLive.cs` y `LiveAudio.cs` en `origin/main` — la referencia de
  comportamiento. **Se leen, no se tocan.**
