# Plan de implementación: la voz no se oye a sí misma

Estado: **propuesto** · Nace del diagnóstico del 2026-08-30 · Rama: `jose/la-voz-no-se-oye-a-si-misma`

## Diagnóstico: qué se midió

El síntoma: con micrófono y altavoces abiertos, Ü se interrumpe a sí misma a media frase. El camino
de realimentación está abierto de punta a punta y no hay ni un solo corte en él:

```
WaveOutEvent (altavoces, sin AEC) → aire → WaveInEvent (mic, sin AEC, sin compuerta)
  → input_audio_buffer.append incondicional → semantic_vad del servidor
  → speech_started → Hecho.HablaronEncima → _audio.Callar() → Ü se corta sola
```

| Qué | Medida | Fuente |
|---|---|---|
| El envío no mira si Ü está sonando | `MandarTrozo` envía incondicionalmente; no consulta ni `Hablando` ni `NivelSalida` | `windows-client/src/Voice/ConversacionEnVivo.cs:940-945` (leído 2026-08-30) |
| La captura no tiene AEC | `WaveInEvent` clásico, API WaveIn; cero usos de WASAPI/AEC en el repo | `windows-client/src/Voice/LiveAudio.cs:198-204` + grep 2026-08-30 |
| El turno lo decide solo el servidor | `turn_detection = semantic_vad`, ningún VAD local | `voz/Realtime/ProtocoloOpenAI.cs:83` |
| La auto-interrupción es INVISIBLE en el log | 0 apariciones de `speech_started` en todos los logs de agosto: el protocolo lo traduce a `HablaronEncima` y el handler llama `Callar()` sin dejar línea | grep sobre `%LOCALAPPDATA%\U\logs\u-202608*.log` (2026-08-30) + `ConversacionEnVivo.cs:1146-1148` |
| El mic capta todo lo que suena en la sala | La transcripción de hoy 16:49:14 atribuye al usuario dos intervenciones pegadas con habla que no era para Ü | `u-20260830.log`, canal `voz-viva` |
| Lo que se intentó antes y por qué se borró | Las defensas por VOLUMEN (umbral aprendido + ganancia de eco + dos tramos) se apagaron el 2026-08-16: «nunca puedo interrumpirlo», «me toca hablarle muy duro». Y el 2026-08-06 se apagaban solas a la vez y «Ü se cortaba a media frase oyéndose a sí misma» | `git show e429fa8^:.../GeminiLive.cs` ~478-513 · `LiveAudio.cs:121-128` |
| Infraestructura lista y sin usar | `Hablando` (cola > 0) y `NivelSalida` (pico con caída de 400 ms) existen; el comentario de `ConversacionEnVivo.cs:56-58` cita un «detector» que ya no existe | `LiveAudio.cs:103-149` |

La lección del 2026-08-16 es la restricción de diseño de esta spec: **el volumen no puede ser la
llave**, porque un número no distingue «¿esto es el eco de Ü?» de «¿esto es quien me habla?». Lo que
sí distinguimos con certeza —porque lo controlamos nosotros— es **si nuestra propia cola de
reproducción tiene audio sonando**. Esa es la llave determinística.

Investigado además (2026-08-30, foros de OpenAI y docs de Microsoft): este es el fallo nº1 del
Realtime API en escritorio con altavoces; la solución canónica tiene dos capas — cancelación de eco
acústico (AEC) del lado del cliente restando la señal de referencia (el principio del noise
cancelling), y como respaldo pragmático, no mandar micrófono mientras suena la voz. Windows 11
expone el AEC del sistema (`IAcousticEchoCancellationControl`, categoría Communications), pero
depende del endpoint/driver: no puede ser la única defensa.

## Por qué esto va dirigido por especificación

Porque este subsistema ya se dio por bueno a sí mismo dos veces: las defensas anti-eco de GeminiLive
«funcionaban» hasta que se apagaban a la vez (2026-08-06), y hoy `Callar()` ejecuta la interrupción
sin dejar rastro — el fallo ni siquiera se puede ver en la fuente de verdad. Una prueba escrita
después del código volvería a medir «se envió audio», que es justo la métrica que este bug no rompe.

**La regla del flujo, y no tiene excepciones:** ninguna línea de producción entra antes que la
promesa que la juzga. Cada fase empieza con el contrato ROTO y termina con el contrato INTACTO.

## La especificación

El diseño, en una frase: **mientras nuestra cola de reproducción suena (más una gracia que cubre la
latencia del altavoz y el resto de sala), lo que entra por el micrófono viaja al servidor como
silencio del mismo tamaño** — el servidor no puede creer que le hablan porque literalmente no le
llega nada que oír, y el flujo de audio no se corta ni pierde el compás. La llave es el estado de
reproducción, jamás el volumen. Donde el sistema operativo ofrezca su AEC (el noise cancelling de
verdad), la compuerta se hace a un lado y el barge-in vuelve; donde no, la compuerta garantiza que
la auto-interrupción es imposible.

Van al **contrato de la voz** (`voz/Contrato/Contrato.cs`), en continuación de la 10:

| # | Promesa | Fase que la pone verde |
|---|---|---|
| 11 | mientras Ü suena, el micrófono viaja al servidor como silencio del mismo tamaño: ni una muestra de la sala | 1 |
| 12 | al vaciarse la cola la compuerta no se abre de golpe: aguanta la gracia que tarda el eco en morir, y pasada la gracia el micrófono viaja intacto | 1 |
| 13 | lo tragado deja rastro: la compuerta cuenta los milisegundos que sustituyó y el total se puede leer | 1 |
| 14 | con el AEC del sistema puesto la compuerta no actúa, y sin él —o forzada— actúa siempre: nunca los dos, nunca ninguno | 2 |

La que de verdad cierra el asunto es la **11**: mientras no exista, todo lo demás es cosmético — el
eco sigue llegando al VAD del servidor y Ü sigue callándose sola.

### Con qué se juzga cada una

Las cuatro, con **mapa a mano en la propia prueba**, al estilo de las promesas 4/6/7 (`Omi.Relevo`):
la compuerta es una clase pura en el ensamblado de la voz, con el reloj y el estado de la cola como
parámetros — sin micrófono, sin altavoz, sin socket:

- **11**: compuerta con la cola sonando + un trozo de 3200 bytes con muestras no nulas → sale un
  trozo de 3200 bytes todo ceros. (La entrada que la falsifica: un trozo que pase con una sola
  muestra viva.)
- **12**: cola recién vaciada en t=0 → en t < gracia sigue tragando; en t > gracia el mismo trozo
  sale **byte a byte idéntico**, no una copia recortada. (Falsifica: abrirse en t=0, o quedarse
  cerrada para siempre.)
- **13**: tras tragar N trozos de duración conocida, el contador dice la suma; tras dejar pasar,
  no crece. (Falsifica: un contador que cuenta lo enviado en vez de lo tragado — patrón nº10.)
- **14**: la decisión de modo es una función pura: `aec=true → compuerta apagada`,
  `aec=false → encendida`, `forzada=true → encendida aunque aec=true`. (Falsifica: los dos
  encendidos a la vez, que haría el AEC inútil; o los dos apagados, que es el bug de hoy.)

El AEC físico (fase 3) **no se puede juzgar con el arnés**: depende del endpoint de esta máquina.
Se juzga en el nivel 4 de la compuerta a `main`, a mano y con el log delante, y su lógica de
decisión (lo único determinístico) queda fijada por la 14.

## Las fases

### Fase 1 — la compuerta existe y es pura: sonando → silencio, gracia → paso

| | |
|---|---|
| **Promesa que pone verde** | 11, 12, 13 |
| **Qué toca** | `voz/` (clase nueva junto a `Realtime/`), `voz/Contrato/Contrato.cs` |
| **¿Núcleo congelado?** | no — el guardián cubre `tests/ContratoDelGrafo/*`, no el contrato de la voz |
| **Terminado** | 11-13 verdes, 1-10 intactas |
| **Sitios con esta clase de error** | 1: solo `MandarTrozo` envía audio al servidor (grep `input_audio_buffer\|_protocolo.Audio`, 2026-08-30) |

La gracia cubre `DesiredLatency` = 120 ms del altavoz más el resto de sala; arranca en **300 ms**
con el porqué en un comentario, y se ajusta con el log, no con teoría.

### Fase 2 — la compuerta está cableada y lo invisible deja rastro

| | |
|---|---|
| **Promesa que pone verde** | 14 |
| **Qué toca** | `windows-client/src/Voice/ConversacionEnVivo.cs` (`MandarTrozo`, caso `HablaronEncima`), `voz/Contrato/Contrato.cs` |
| **¿Núcleo congelado?** | no |
| **Terminado** | 14 verde, 1-13 intactas; `HablaronEncima` y cada tramo tragado dejan línea en `voz-viva` |
| **Sitios con esta clase de error** | 2 mensajes mudos: `Callar()` en `HablaronEncima` (1146-1148) y el catch de `MandarTrozo` (945) — se les da voz en esta fase |

Se cablea sobre `Capturado` a la altura de `MandarTrozo`, así cubre **las dos fuentes** (micrófono
local y collar) sin que ninguna se entere. Y `HablaronEncima` pasa a decir en el log qué pasó y
cuánta cola calló — hoy la interrupción no deja ni una línea.

### Fase 3 — donde Windows sabe hacer noise cancelling, se le deja

| | |
|---|---|
| **Promesa que pone verde** | ninguna nueva: la 14 ya fija su lógica; lo físico se juzga a mano |
| **Qué toca** | `windows-client/src/Voice/LiveAudio.cs` (`AbrirLocal`) |
| **¿Núcleo congelado?** | no |
| **Terminado** | la captura local pide el AEC del sistema (WASAPI, categoría Communications, render como referencia); lo que decidió el endpoint queda dicho en el log con su motivo; si el endpoint no lo da, `WaveInEvent` sigue tal cual y la compuerta manda |
| **Sitios con esta clase de error** | 1: `AbrirLocal` es el único que abre micrófono local |

Esta fase es **mejora, no requisito**: sin ella la solución ya es determinística. Puede quedar para
una rama posterior si el nivel 4 de las fases 1-2 sale limpio y urge integrar.

## Lo que NO entra

- **Mandar `response.cancel` al servidor cuando hablan encima.** Hoy solo se vacía la cola local y
  el servidor sigue generando. Es real, pero es otro asunto con su propia promesa; mezclarlo casaría
  dos features en una rama.
- **Mover `eagerness` de `semantic_vad`.** El propio comentario de `ProtocoloOpenAI.cs:73-82` dice
  que se mueve tras medir, y compensaría el síntoma sin cerrar el camino del eco.
- **El bug de `self_mute`** (silencia `VoiceIO`, no la voz realtime — `ConversacionEnVivo.cs:824-826`).
  Hallazgo de esta spec; va aparte.
- **Volver a cualquier defensa por volumen.** Prohibido por diseño: es lo que se enterró el
  2026-08-16 y esta spec existe para no resucitarlo.

## Hallazgos

- 2026-08-30 · Fases 1 y 2 implementadas y verdes (voz 14/14, grafo 81/81 intacto). Sabotajes
  comprobados, no supuestos: una muestra escapada pone rojas la 11 y la 12; sin gracia y contando
  lo que fluye, la 12 y la 13; con AEC y compuerta apagados a la vez, la 14.
- 2026-08-30 · La fase 3 (AEC de WASAPI) se queda para una rama posterior, como esta spec ya
  permitía: la garantía determinística está completa sin ella, y hoy `AecDelSistema` es una
  constante `false` en `ConversacionEnVivo` que esa fase pondrá a verdad.
- 2026-08-30 · Falta el nivel 4, que aquí no es «pantallas» sino escenarios de sonido: altavoces a
  volumen alto (el caso que disparaba el bug) y auriculares (el que no debe empeorar). Lo juzga el
  log: `compuerta de eco: tragó N ms` y ningún `speech_started` mientras Ü suena sola.

- 2026-08-30 · La auto-interrupción no deja NI UNA línea de log: `speech_started` se traduce y el
  handler calla en silencio. Cuatro rondas de agosto se diagnosticaron sin poder ver el disparo.
- 2026-08-30 · El comentario de `ConversacionEnVivo.cs:56-58` cita un «detector de voz» que ya no
  existe (borrado el 2026-08-16). Se corrige de paso en la fase 2, que toca ese archivo.
- 2026-08-30 · `self_mute` no silencia la voz realtime ni cierra el micrófono: llega a
  `VoiceIO.Muted`, que es el TTS clásico de Windows. Pendiente de verificar en vivo; va en rama propia.

## Cierre

- [ ] Todas las promesas verdes (`.\scripts\contrato-de-la-voz.ps1` → VOZ ÍNTEGRA)
- [ ] `.\scripts\verificar.ps1` pasa, con evidencia en `out\evidencia.md`
- [ ] Probado en ≥2 escenarios reales, con nombre: altavoces del portátil a volumen alto (el caso que
  dispara el bug) y con auriculares (el caso que no debe empeorar); si hay collar a mano, también
- [ ] Estado de este documento: **implementado** (AAAA-MM-DD)
