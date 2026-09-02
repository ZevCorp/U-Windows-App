# Plan de implementación: el collar es el micrófono de la consulta

Estado: **propuesto** · Nace del diagnóstico del 2026-09-01 · Rama: `jose/omi-microfono-de-toda-la-app`

Que el médico elija con qué micrófono se graba la consulta —el del portátil, el collar por
Bluetooth, o el collar a través de su teléfono— desde la ventana de consulta, y que en todo momento
vea cuál está entrando. La investigación completa que la precede está en
[`docs/omi-como-microfono.md`](../omi-como-microfono.md).

## Diagnóstico: qué se midió

Todo con fecha del 2026-09-01, sobre el collar Omi CV1 real y un Android con la app oficial.

| Qué | Medida | Fuente |
|---|---|---|
| **La ruta directa por WebSocket funciona** | 100,7 s continuos · 3.856 tramas · 2.467.840 bytes | sonda `omi-directo` en Supabase |
| Formato que entrega el teléfono | **PCM16 · 16 kHz · mono**, listo para usar, sin Opus que decodificar | ritmo sostenido de ~32.000 B/s |
| Tamaño de trama | **640 bytes constantes = 320 muestras = 20 ms** | es la trama nativa del CV1; **la firma del collar**, no del micro del teléfono |
| Latencia | trama cada **20 ms** | frente a ráfagas de 4 s del webhook |
| Continuidad | **32.000 B/s sostenidos**: el flujo dura lo que dura el reloj | sin los huecos de silencio de la otra ruta |
| Camino de vuelta | la app acepta nuestro esquema (`segments[].text/start/end`) | respuestas cada 3 s aceptadas |
| Handshake | **ninguno**: abre el socket y manda binario | lectura de `pure_streaming_stt.dart` |
| El plan de Omi no aplica | `if use_custom_stt: user_has_credits = True`; *«exempt from transcription caps»* | `listen_session_bootstrap.py`, `fair_use.py` |
| **La ruta por webhook, para comparar** | ráfagas de 4 s · 84 % del reloj hablando con pausas · 100 % hablando seguido | 2 corridas contra `/api/omi/audio` |
| Con el teléfono bloqueado | **sin diferencia**: 257 peticiones en 8 min | corrida de las 03:26 UTC |
| Lo que hay hoy en la ventana | `ConsultaWindow` crea su `LiveAudio` y **no se suscribe a `FuenteCambio`** | `ConsultaWindow.cs:89`, `LiveAudio.cs:266` |
| Captadores de micrófono en la app | **tres**, y solo uno ve el collar | `LiveAudio.cs:209`, `VoiceIO.cs:96`, `ScreenRecorder.cs:64` |
| Identidad estable del médico | `SesionMiracle.MedicoId` = uuid de Supabase, en `%APPDATA%\U\sesion.dat` cifrado con DPAPI | `SesionMiracle.cs:70`, `:63` |
| Identidad estable del PC | `Config.InstallId`, GUID por instalación | `Config.cs:56`, generado en `FaceWindow.xaml.cs:988` |

## Por qué esto va dirigido por especificación

Porque el subsistema **ya se dio por bueno a sí mismo tres veces**, y todas con el mismo disfraz:
el collar «conectado» sin entregar una trama durante 56 minutos en una demo, el punto azul pintando
verde con el audio entrando por otro sitio, y el botón del collar abriendo la voz por el micrófono
del portátil. Un indicador de micrófono es exactamente esa clase de pieza: **la que miente sin dar
error**. Una prueba escrita después mediría «el selector cambió de estado», que es justo lo que el
bug no rompe.

**La regla del flujo, y no tiene excepciones:** ninguna línea de producción entra antes que la
promesa que la juzga. Cada fase empieza con el contrato ROTO y termina con el contrato INTACTO.

## La forma: qué se puede juzgar sin hardware

CI no tiene collar, ni teléfono, ni tarjeta de sonido. Igual que en la spec 001, eso obliga a partir:

| Dónde | Qué | ¿Se juzga en CI? |
|---|---|---|
| `voz/Omi/` (`net8.0` puro) | qué fuente manda, cuándo se releva, cómo se arma y valida el enlace de emparejamiento | **sí** |
| `windows-client/` | el WebSocket al relé, el dibujo del selector | no → nivel 4, a mano |

## La especificación

Van al **contrato de la voz** (`voz/Contrato/Contrato.cs`), en continuación de la 22.

| # | Promesa | Fase |
|---|---|---|
| 23 | hay exactamente **un** sitio en la app que abre un dispositivo de captura, y los demás le piden la voz a él | 2 |
| 24 | la fuente activa se puede leer y decir con su nombre: micrófono del PC, collar por Bluetooth, o collar por teléfono | 1 |
| 25 | con el collar disponible manda el collar; al perderlo la voz vuelve al micrófono local, y el cambio deja el motivo en el log | 1 |
| 26 | el enlace de emparejamiento lleva el código del médico, y un código que no cuadra se rechaza **nombrando** que no cuadra | 3 |
| 27 | un código caducado no acepta audio, y lo dice con 410 y no con silencio | 3 |
| 28 | la fuente por teléfono declara su latencia nominal, y quien la consume la puede leer antes de esperar nada | 1 |
| 29 | el indicador no puede pintar «conectado» sin trama reciente: sin audio en N segundos, deja de decir que hay micrófono | 4 |

**La que cierra el asunto es la 29.** Mientras no exista, todas las demás son cosméticas: el repo ya
pagó tres veces el mismo fallo —verde en pantalla, cero audio— y un selector nuevo sin esa promesa
es una cuarta oportunidad de repetirlo.

### Con qué se juzga cada una

Mapa a mano en la propia prueba, con relojes sintéticos, como las promesas 4/6/7 y 11-14:

- **23**: se cuenta por reflexión cuántos tipos abren captura. Falsifica: dos.
- **24**: `Selector.Activa` devuelve el enum y su nombre para las tres fuentes. Falsifica: devolver
  «collar» cuando la fuente viva es la local.
- **25**: secuencia sintética collar vivo → collar muerto → se exige `Relevar` y una línea con motivo.
  Falsifica: relevar sin motivo, o no relevar.
- **26**: enlaces con código bueno, con código de otro médico y sin código. El malo se rechaza
  **diciendo que el código no cuadra**, no «error». Falsifica: un mensaje que no distinga sus causas.
- **27**: código con caducidad pasada → 410. Falsifica: 200 silencioso, que es lo que haría que Omi
  siguiera mandando a un destino muerto.
- **28**: `LatenciaNominalMs` > 0 para la fuente por teléfono y ~0 para las locales.
- **29**: reloj sintético; con la última trama a N+1 segundos, el estado deja de ser «conectado».
  Falsifica: un indicador que solo mire el estado del socket.

## Las fases

| Fase | Promesa que pone verde | Qué toca | Terminado cuando |
|---|---|---|---|
| **0** | ninguna — **arnés** | `voz/Contrato/Contrato.cs` | las 7 salen **ROJAS** y el resto sigue 22/22 |
| **1** | 24, 25, 28 | `voz/Omi/Selector.cs` (nuevo, puro) | 24-25-28 verdes, 1-22 intactas |
| **2** | 23 | `windows-client/src/Voice/LiveAudio.cs` + los tres captadores | 23 verde; un solo sitio abre captura |
| **3** | 26, 27 | `voz/Omi/Emparejamiento.cs` (puro) + la función de Supabase | 26-27 verdes |
| **4** | 29 | `voz/Omi/Vigia.cs` (puro) + `ConsultaWindow` | 29 verde |
| **5** | ninguna — **UI** | `windows-client/src/Ui/ConsultaWindow.cs` | el selector se ve y respeta `Estudio` |
| **6** | ninguna — **nivel 4** | — | se graba una consulta por cada una de las tres fuentes, en ≥2 pantallas |

## Lo que NO entra

| Fuera | Por qué |
|---|---|
| Reescribir el transporte BLE («reconectar = reconstruir») | Es la decisión 1 del documento y merece su propia rama. Aquí el collar por Bluetooth entra **tal como está hoy** |
| Que Ü suene por el collar | La salida se queda en los altavoces. Otra spec y otra medición |
| Publicar la app de integración en la tienda de Omi | Developer Mode primero, decisión 5 |
| Usar la transcripción de Omi | Transcribimos nosotros. Es el motivo de todo esto |

## Deuda con nombre: el segundo captador de la carita

**`VoiceIO.ListenOnceAsync` abre el micrófono del portátil por su cuenta** (`VoiceIO.cs:98`), y lo
usa la carita cuando se pulsa el micrófono **sin conversación viva** (`FaceWindow.xaml.cs:1894`).
Con el collar puesto, ese camino sigue oyendo por el portátil.

No entra en esta spec y la razón es de método, no de pereza: arreglarlo bien es pasar
`SpeechRecognitionEngine` de `SetInputToDefaultAudioDevice()` a `SetInputToAudioStream()` alimentado
por el selector, y eso se descubrió con un usuario esperando una instalación. Un refactor de la
captura hecho con prisa es exactamente cómo se llega a tres captadores.

Lo que sí queda puesto es el **trinquete**: la promesa 23 cuenta los sitios leyendo el repo y declara
los dos conocidos con su motivo. Si aparece un cuarto, el contrato se pone rojo. La cuenta ya no
puede crecer en silencio, que es como llegó a tres.

El otro declarado, `ScreenRecorder`, **no es deuda**: graba la sala en un vídeo de enseñanza, que es
otro producto. Forzarlo por el selector sería un error, no un arreglo.

## Deuda con nombre: dos identidades compitiendo al arrancar

**2026-09-02, encontrado en una instalación real, con el usuario delante.** Al abrir `U.exe
--consulta` en un equipo limpio salió el `OnboardingWindow` **viejo** —solo nombre y correo, sin
contraseña— en vez del `LoginWindow` de `SesionMiracle` que la consulta necesita de verdad.

La causa, leída en `FaceWindow.xaml.cs:996-1022`: la carita arranca **siempre** por `StartupUri`,
`--consulta` sólo añade la ventana de consulta encima, y las dos disparan su propio arranque de
identidad en paralelo. La carita mira si hay sesión de Supabase (`sesion.Restaurar()`); si en ese
instante todavía no la hay —la ventana de consulta puede no haber llegado a pedirla— cae a
`Identidad.HayQuePreguntar` y abre el onboarding viejo, que **no crea ninguna `SesionMiracle`**: es
un sistema de identidad distinto y anterior, pensado para la telemetría, no para la clínica.

El dueño, viéndolo: *«esa vieja ya no debería ni existir»*. Se anota como deuda y no se toca aquí, a
propósito: es una carrera entre dos arranques dentro de la misma ventana de tiempo, y arreglarla bien
es decidir **cuál de las dos identidades manda** —o fundirlas en una— con el mismo cuidado que
`SesionMiracle`/`Identidad.cs` ya tuvieron, no un parche para ganar la carrera.

## Hallazgos

- **2026-09-01, probando el selector recién dibujado** — **la puerta estaba cerrada por dentro.** Con
  el collar conectado por Bluetooth, elegir «micrófono del PC» o «teléfono» no hacía nada: la
  elección se pintaba y al segundo siguiente la prioridad automática la deshacía. La causa es
  `LiveAudio.QuiereCollar`, que devuelve `true` **siempre** que el collar esté conectado — una
  cláusula añadida el 2026-08-13 para arreglar el fallo *contrario* («se pulsaba el collar para
  hablarle al collar y el collar no escuchaba») y que al no tener contraparte dejó al collar
  mandando para siempre. Las dos cláusulas son correctas y tienen que convivir. De aquí sale la
  **promesa 30**, que no estaba en la spec: lo elegido manda sobre lo automático, y sólo la ausencia
  de lo elegido devuelve el volante.
- **2026-09-01, primera versión de la interfaz, corregida por el dueño** — el indicador nació como
  una pastilla con punto, icono y nombre encima del botón de grabar, más textos de ayuda en el menú.
  Veredicto: *«está horrible… demasiado texto que se ve superpoco estético… los usuarios no van a
  leer esos textos largos»*. Pasa a ser **un icono redondo de 28 px junto al nombre**, y el menú a
  tres palabras: Computador · Collar Omi · Teléfono. El enlace **se enseña** con su botón de copiar
  en vez de anunciarse en prosa: un mensaje que dice «copiado» obliga a creerse algo invisible.

- **2026-09-01** — El camino directo por WebSocket entrega **la trama nativa del collar (640 bytes,
  20 ms)** ya en PCM. No hay que decodificar Opus del lado del teléfono, y **no hay que reponer
  silencio**: llega el reloj completo. Dos piezas que la spec 001 tuvo que construir para el
  Bluetooth aquí no hacen falta.
- **2026-09-01** — Sin `EdgeRuntime.waitUntil()`, Supabase apaga la función con el socket abierto:
  `abierto` → `primera-trama` → `shutdown` en 600 ms, y el cliente ve un cierre `1006` sin motivo.
- **2026-09-01** — Un receptor que devuelve `429` mata el envío **en silencio**: Omi apaga el webhook
  del usuario tras 100 respuestas no-2xx seguidas. Costó 40 minutos de diagnóstico en el sitio
  equivocado. Por eso `/api/omi/audio` va declarado **antes** del limitador de `/api`.

## Cierre

- [ ] Las siete promesas verdes en el contrato de la voz
- [ ] `.\scripts\verificar.ps1` pasa, con evidencia en `out\evidencia.md`
- [ ] Probado en ≥2 pantallas, con nombre: …
- [ ] Grabada una consulta real por cada una de las tres fuentes
- [ ] Estado de este documento: **implementado** (AAAA-MM-DD)
