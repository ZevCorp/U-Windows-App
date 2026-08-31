# Ü en Mac — lo que queda por decidir

Cosas vistas y aparcadas a propósito, con quién las levantó y por qué no se resolvieron en el sitio.
No es una lista de bugs: es lo que hace falta **mirar con la carita delante** antes de decidir.

## Para revisar mirándola

### 1. La cara al apagar el micrófono — ¿convence `detenido`?

Isabel, 2026-08-18: *«lo de que cuando apague el micro uno haga esa cara de sorprendido, pues no sé
qué tanto me gusta»*.

Esa cara de sorprendido era `.pillado` («la cacharon»), disparada desde el doble toque. **Ya se quitó**
y ahora apagar el micrófono deja `detenido` («apagada a propósito», que es lo que dice el catálogo).
Queda por ver si `detenido` se lee de un vistazo o pasa desapercibido: es una cara quieta, con el color
`UiPalette.inactivo`, y compite con `reposo`, que también es una cara quieta.

Si no se distingue, la salida NO es volver a un talante del espejo —esos tienen su condición escrita—
sino darle a `detenido` algo propio: los ojos cerrados del todo, o el acento apagado más marcado.

### 2. «mira» como nombre — cuántas veces despierta sin que la llamen

Se aceptó «mira» sin filtrar por la palabra que viene detrás, y está razonado en `Llamado.swift`: un
filtro por la siguiente palabra tiraba 2 de las 6 llamadas reales del registro. El precio es que
«mira, te cuento…» dicho a otra persona la despierta.

**Cómo se mide, no se opina:** con un día de uso, contar en `~/.u/u.log` los `✦ me despertaron` que no
tuvieron pregunta detrás. Si son muchos, la salida buena es exigir antesala solo para «mira» —«oye
Mira», «hola Mira»— en vez de una lista de palabras prohibidas.

### 3. `escuchando` acotada, si cansa

Hoy dura lo que dura tu frase, que es lo que dice el catálogo. Se probó ponerla mientras el micrófono
estuviera abierto y se deshizo el mismo día: el catálogo dice que eso es `reposo`, y en Windows
(2026-08-05) esa cara sostenida se leyó como jadeo. Si en el uso diario la frase se hace larga y la
cara cansa, acotarla por tiempo —no quitarla.

## Caras sin condición todavía

`grabando` y `esperando` están dibujadas y no las pone nadie. **No es un olvido:** en Windows dependen
del modo enseñar (`_teaching`) y de una pregunta pendiente (`_pendingAnswer`), y en Mac no existe
ninguna de las dos cosas. Se conectan el día que exista lo que describen, no antes — una cara que se
enciende sin que pase lo que anuncia es peor que una cara apagada.

## La voz en vivo — probada de punta a punta

`Vivo.swift` + `AudioVivo.swift`, reescritura en Swift de `GeminiLive.cs` y `LiveAudio.cs` (leídos de
`main`, no copiados). **Corrida el 2026-08-18**, con la cadena entera verificada en el registro:

```
🎙 eco: entrada sí · salida sí
🎙 audio vivo abierto · entra 48000 Hz → 16000 Hz · sale 24000 Hz → 48000 Hz
🎙 cara «sobrado»              ← la cara llega ANTES
🎙 dice: «Todo funciona.»
```

Del cliente de Windows **no** se trajo, a propósito: los ojos (un fotograma por segundo de la
pantalla), el collar Omi, las herramientas del grafo de SAP, y el reporte de consumo. Ninguna tiene
sentido en Mac todavía.

### Las cuatro trampas que costaron la tarde, para que no se repitan

**0. Un conversor de 9 canales a 1 devuelve SILENCIO, sin error.** El micrófono de esta MacBook
entrega **9 canales**. `AVAudioConverter` sabe cambiar ritmo y tipo de muestra, pero para bajar de 9
a 1 necesita `channelMap`; sin él no mezcla — dice que sí, entrega un buffer del tamaño correcto, y
dentro hay ceros. Se vio así:

```
🎙 crudo 9ch → [0.178 0.178 … 0.178]     ← el micrófono oye perfectamente
🎙 micrófono → 21 trozos · nivel 0.0000  ← lo que salía del conversor
```

**Once sesiones abiertas y cero transcripciones**, con el socket, el altavoz, la cara y la
cancelación de eco funcionando. Se dio por buena la voz habiéndola probado solo con `U_DECIR`
(texto): «abrió» y «oye» son cosas distintas, y la única pieza sin comprobar era la que fallaba.
Por eso el medidor de nivel de entrada se queda puesto — avisa `⚠︎ SILENCIO` cada dos segundos.

### Las otras tres trampas

**1. Nombrar `mainMixerNode` es cablearlo.** Es perezoso: la primera vez que se menciona,
AVAudioEngine lo crea Y lo conecta a la salida al ritmo que él elige (44 100 Hz), mientras la salida
con procesado de voz exige 48 000. Ese desacuerdo mataba el arranque con
`-10875 · PerformCommand(*outputNode, kAUInitialize)` — y reconectarlo después no lo deshace.

Lo peor: **la línea de diagnóstico puesta para averiguar por qué fallaba era una de las causas.**
Medía `mainMixerNode.outputFormat` y, al medirlo, lo creaba. Tres intentos de arreglo fallaron
porque la sonda reintroducía el fallo en cada vuelta. Si una medición toca un objeto perezoso, la
medición es parte del sistema.

**2. Correr el binario suelto la mata por privacidad.** `./U.app/Contents/MacOS/U` desde la terminal
aborta con `SIGABRT` y `__TCC_CRASHING_DUE_TO_PRIVACY_VIOLATION__`, diciendo que falta
`NSSpeechRecognitionUsageDescription` — que **sí está** en el `Info.plist`. Lanzada con `open` no
pasa. Para probar con variables de entorno:

```
open --env U_VIVO=1 --env "U_DECIR=hola" "…/mac-client/U.app"
```

`U_VIVO=1` abre la conversación al arrancar sin tener que llamarla; `U_DECIR` le manda una frase por
escrito para comprobar la vuelta entera sin hablarle.

### El bucle de reconexión (2026-08-18) — dos bugs encadenados

Con la app «cerrada», el punto naranja del micrófono parpadeaba sin parar. Era una **reconexión
infinita**: 2.089 vueltas, una por segundo, abriendo y cerrando el motor de audio.

1. **`reintentos` volvía a cero al ABRIR el socket.** El socket moría a los 200 ms, pero alcanzaba a
   pasar por esa línea antes — así el tope de tres intentos no se alcanzaba nunca. Ahora el contador
   solo se pone a cero cuando llega **contenido de verdad**: abrir prueba que el socket saludó,
   no que la conversación funcione.
2. **El pase caducado no falla diciendo «caducado».** El servidor acepta el socket y lo cierra al
   instante («Socket is not connected»), que se parece a un problema de red. Reintentar con el mismo
   pase reproduce el mismo cierre para siempre. Ahora se tira al segundo intento, y la espera sube
   1 s → 2 s → 4 s.

### La sala entera entraba a la conversación

Una vez abierta, la sesión le mandaba a Gemini **todo lo que sonaba**, y el detector del servidor
tomaba cada voz ajena como una interrupción: la carita no llegaba a decir una frase entera nunca.
Llegó a preguntar «¿me están hablando a mí?». Se pidió `activityHandling: NO_INTERRUPTION`
(`U_BARGE_IN=1` devuelve el de fábrica). Medido: de decenas de cortes a **1 en 26 segundos**, con
frases completas.

**Lo que queda de fondo:** al abrir la sesión viva se pierde el portero. `Llamado` decide «¿esto es
para mí?» solo en el oído local; la sesión viva no tiene ese filtro. Mientras esté abierta, todo lo
que suena en la sala se manda y **se paga**. El temporizador de 30 s no la cierra porque cualquier
voz cuenta como conversación. Hoy se cierra a mano (doble toque, o el menú). Es el siguiente arreglo
de verdad.

### Lo que falta mirar con ella delante

- **Interrumpirla.** El eco está cancelado, pero hablar encima de ella mientras habla no se ha
  probado — hace falta una persona.
- **Que la cara no se quede pegada.** El modelo llama a `poner_cara` cuando quiere; si llama poco, la
  carita se queda seria toda la conversación.

## Lo que espera a otra rama

- **No hay nivel 2 de la compuerta en Mac.** El contrato es .NET y no corre aquí, así que hoy
  «verificado» en este cliente significa «compila y se probó a mano». Falta un contrato en Swift, y
  con la voz en vivo dentro pesa más que ayer: es el subsistema más grande sin juez.

---

## CERRADO (2026-08-19) · El oído quedaba sordo después de cada conversación en vivo

**Es el fallo que la usuaria reporta como «le hablo y no me responde».** Descubierto el 2026-08-19,
reproducido cinco veces seguidas. No es nuevo: está en el registro de las 20:37 de ese mismo día,
antes de que se tocara nada.

### Cómo se reproduce, siempre

```
1. Arrancar la app.                     → 👂 escuchando · oye bien
2. Decirle «Mira».                      → 🎙 sesión de voz abierta · 👂 le cedo el micrófono
3. Esperar 30 s sin hablar.             → 👂 ✧ me duermo · 👂 recupero el micrófono
4. Hablarle.                            → NADA. Ni una línea en el registro.
```

El paso 3 imprime `👂 escuchando (formato 48000.0 Hz, 1 canal/es)` **y es mentira**: el motor arrancó,
el tap está puesto, y no entra un solo búfer. Un mensaje que afirma lo que no comprobó — el
antipatrón nº2 del repo, cometido en el sitio que más duele.

### Lo que ya se sabe, para no repetirlo

- El audio **sí acaba entrando** tras dos o tres reaperturas (el vigía deja de quejarse), y **aun así
  el reconocedor no transcribe una palabra.** O sea que son DOS cosas, no una.
- El disparador es `AudioVivo`: enciende `setVoiceProcessingEnabled(true)` sobre el mismo micrófono.
  Sin sesión viva de por medio, el oído aguanta horas — verificado vivo y oyendo a los 3,5 minutos.

### Tres hipótesis descartadas, con su medida

| # | Se probó | Resultado |
|---|---|---|
| 1 | Darle 0,8 s de respiro al aparato antes de retomarlo | no basta |
| 2 | Un `AVAudioEngine` nuevo en cada apertura | **peor**: la entrada aparece con 3 canales y el tap sigue mudo. Revertido |
| 3 | `setVoiceProcessingEnabled(false)` al abrir + recrear el `SFSpeechRecognizer` | el audio entra, la transcripción no |

### Lo que sí quedó puesto

Un vigía en `Oido.vigilarQueLlegueAudio`: cuenta búferes y, si a los 2,5 s no llegó ninguno, lo dice
y reabre — **con tope de tres**, que si no es el bucle de 2.089 vueltas otra vez. Agotado el tope
avisa en voz alta «Me quedé sin micrófono. Ciérrame y ábreme otra vez». No arregla nada; deja de
mentir, que es el paso previo.

### Por dónde seguir

Preguntarle a la API antes de creerle al código (aprendizaje nº13): una sonda pequeña que abra el
motor de la voz viva, lo cierre, y pruebe a transcribir — sin la app de por medio. Sospechosos por
orden: la sesión de audio del proceso, no del nodo; y que `SFSpeechRecognizer` necesite que el tap
entregue el formato exacto que negoció.


### Cómo se cerró: era EL ORDEN de dos líneas

Lo encontró la sonda (`open --env U_SONDA=1 U.app`, informe en `~/.u/sonda.txt`), y en cinco minutos
descartó de un golpe lo que llevaba una tarde:

```
5 · captura limpia OTRA VEZ    ✅ 30 búferes · pico 0.1024 · 1 canal
6 · transcripción OTRA VEZ     ✅ «123456»
```

**El aparato NO se ensucia.** Encender la cancelación de eco, apagarla, y volver a capturar y
transcribir funciona perfecto fuera de la app. O sea que la sordera era nuestra, y las tres
hipótesis de arriba miraban al sitio equivocado porque partían de una premisa falsa.

La causa, en `AudioVivo.cerrar()`: se apagaba el procesado de voz **con el motor todavía corriendo**,
y con `try?`. Apagarlo sobre un motor en marcha FALLA, el `try?` se comía el motivo, y la unidad de
entrada del proceso se quedaba en modo procesado. Quien lo heredaba era el oído local, que abría y
veía la entrada con **3 canales en vez de 1** y no recibía un solo búfer.

El arreglo es mover una línea debajo de otra —parar el motor y después apagarlo— y quitar el `try?`.
En `Oido.arrancar()`, además, motor nuevo **y** apagar el eco: por separado ninguna de las dos sirve,
y probarlas de una en una fue lo que las descartó en falso.

Juzgado por la promesa 10 del contrato, hoy en verde.

**Lo que deja como aprendizaje:** las tres hipótesis se descartaron *dentro* de la app, donde había
cinco culpables posibles para un síntoma. Veinte minutos de sonda fuera de la app dejaron uno solo.
Es el aprendizaje nº13 del repo, y esta vez se pagó por no aplicarlo antes.

---

## ABIERTO (2026-08-31) · El contrato quedó desactualizado tras el cambio a OpenAI y quitar «mira»

El mismo día se tomaron dos decisiones que cambian lo que el sistema promete de verdad, y el
contrato solo se actualizó a medias:

1. **La voz en vivo pasó de Gemini a OpenAI Realtime** — `vivo` ahora existe siempre que hay llave
   de OpenAI, y el camino de texto (Gemini) quedó como respaldo SOLO para cuando `vivo` es `nil`.
2. **Se quitó «mira» como alias del nombre** — a partir de ahora solo despiertan formas de «Ü»,
   sabiendo que eso es más frágil (medido: 0 de 822 disparos reales en el registro del 2026-08-18).

**Lo que se arregló ya:** la promesa 2 («contesta en menos de 3 segundos») se reescribió para medir
el camino real —`U_LLAMAR` en vez de `U_PREGUNTAR`, y `🎙 dice:` en vez de `🧠 dice a los`—. Probada
tres veces seguidas: 1,16 s · 1,00 s · 0,98 s · 1,14 s. Verde de verdad, no por casualidad.

**Lo que sigue roto o ciego, y por qué, promesa por promesa:**

| # | Qué prueba | Por qué falla hoy |
|---|---|---|
| 3 | No promete tocar el Mac | Usa `U_PREGUNTAR`, que fuerza `despierta=true` sin `vivo.arrancar()` — la frase se descarta por el arreglo de la carrera del mismo día. Necesita el mismo cambio que la 2: leer «🎙 dice:», no «🧠 dice». |
| 4 | Voz masculina en español | Depende de que la 3 la haga hablar por el camino de TEXTO — ese subsistema (macOS TTS de respaldo) sigue vivo pero casi nunca se ejerce ahora que `vivo` casi siempre existe. Hay que decidir si sigue midiéndose por ahí o se retira. |
| 5 | La cara se deriva sola | Misma causa que la 3: usa `U_PREGUNTAR`, nunca la hace hablar. |
| 6 | No se transcribe a sí misma | Depende de la 5. |
| 9 | Despierta con «Mira» | **Esta ya no debe pasar — es el comportamiento correcto.** Probaba que «mira» despertara, y se quitó a propósito el 2026-08-31. Hay que reescribir el enunciado y el cuerpo para probar SOLO formas de «Ü», sabiendo que el riesgo medido es real (ver `Llamado.swift`). |
| 11 | El portero de la sesión en vivo | Usa `U_VIVO=1`, que en el código actual sigue apuntando bien a `vivo` (OpenAI) — pero no se pudo verificar en esta sesión por el mismo desgaste del subsistema de audio de tantas corridas seguidas. Repetir en frío antes de dar por roto.

**Por qué no se arregló todo de una vez:** cinco promesas rediseñadas bajo presión de tiempo es
exactamente el apuro que el patrón nº14 de este repo advierte no tomar («no optimizar la cadencia
antes del costo por iteración» — aquí, no apurar cinco arneses antes de pensar cada uno). Se arregló
la que de verdad importaba medir hoy (la 2, la velocidad de respuesta que motivó toda la sesión) y
se dejó el resto escrito, no escondido.
