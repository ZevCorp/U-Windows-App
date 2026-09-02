# Investigación: el Omi como micrófono de toda la app

Estado: **investigación, decisiones tomadas, sin código** · 2026-09-01 · Alimenta la spec 005 (por escribir con `/especifica`)

Lo pedido, en dos frases: que el collar Omi sea **el micrófono de Ü** —de toda la app, no de una
función— por dos caminos reales: (A) el collar conectado **directo al PC**, y (B) el collar
conectado **al teléfono** con la app oficial de Omi (la de la App Store, sin que tengamos que
publicar una nosotros) y de ahí hasta nuestro lado. Y que las dos rutas usen **solo lo que Omi
tiene probado y estable**, porque lo que hay hoy «da problemas».

Todo lo de abajo está **medido o leído en código**, con fecha y fuente. Donde algo no se pudo
medir, se dice.

## En una página

**Lo que Omi ofrece de verdad** (leído en `BasedHardware/omi` en `main`, 2026-09-01):

1. **Un protocolo BLE pequeño, público y firmware-estable**: un servicio, una característica de
   audio con notify, una de códec, cabecera de 3 bytes, Opus a 16 kHz mono. **No exige
   emparejamiento.** Es el «contrato compartido» que Omi declara para todos sus SDKs.
2. **Ningún SDK oficial para Windows.** El de Python depende de `bleak` (que en Windows es WinRT,
   lo mismo que ya usamos); los paquetes «device» son UUIDs y quitar 3 bytes; el Rust no trae
   pila BLE a propósito. **La app oficial de Windows de Omi tampoco habla con el collar** — su
   auditoría dice literalmente que la pila BLE «está ausente, fase 7 diferida». Omi resume la
   instrucción para plataformas así: *engancha tu pila BLE al UUID de audio y quita la cabecera*.
3. **La app oficial (iOS/Android) sí exporta el audio en crudo**, por dos mecanismos oficiales
   (Developer Mode y apps de integración). Lo manda **la nube de Omi**, no el teléfono: PCM16
   16 kHz mono, en trozos de exactamente 1 s, por HTTP POST, tras acumular N segundos (N lo pone
   el usuario; por defecto 5; se acepta 1). Con reintentos y auto-apagado a los 100 fallos.
4. **El collar calla cuando no hay voz, y lo hace el firmware** (detección acústica por hardware
   del micrófono T5838). Por eso el flujo **no dura lo que dura el reloj** — en ninguna de las dos
   rutas. La spec 001 lo midió y su promesa 2 (reponer el silencio) sigue siendo el corazón.

**Lo que tenemos** (informe del repo, 2026-09-01):

5. **Dos implementaciones.** La activa —`voz/Omi` puro (7 promesas verdes) + `FuenteOmi.cs`
   (503 líneas de WinRT que **nadie juzga**, y el CI lo dice por escrito)— acumuló **seis fallos
   distintos «parece que funciona»** entre el 13 y el 30 de agosto, uno en una demo delante del
   usuario. El último arreglo no tiene ninguna verificación posterior: en esta máquina no hay
   logs desde el 2026-08-14 y `collar.json` no existe. La spec 001 tiene sus **cinco casillas de
   cierre vacías**. La desactivada —`puente-omi/puente.py`, SDK oficial de Omi + VB-Cable— es
   **la única con evidencia de audio real** (40 s sin fallos, 2026-08-14).
6. **«Toda la app» hoy es falso.** Hay **tres captadores de micrófono** en el cliente y solo
   uno ve el collar. No existe ninguna abstracción de fuente de audio: el collar es un injerto
   con cinco banderas dentro de `LiveAudio`.

**La recomendación**, que se detalla abajo:

- **Caso A (PC directo):** rescatar `voz/Omi`, y **rediseñar el transporte con el modelo del SDK
  oficial: reconectar = reconstruir.** Los seis fallos son una sola clase —un enlace que dice
  «conectado» y no entrega nada— y nacen de *parchear* una sesión reconectada. El SDK de Omi
  (y `bleak` debajo) no parchea: cuando el enlace cae, el cliente muere y se rehace entero. Se
  hace juzgable con una máquina de estados pura, como ya se hizo con `Relevo`. No meter un
  sidecar de Python: Omi no gana nada oficial con eso y Smart App Control sí pierde.
- **Caso B (teléfono):** el webhook «Realtime audio bytes» de Developer Mode, con nuestro código
  de emparejamiento en la URL, hacia un relé nuestro que empuja a `U.exe`. Después, cuando esté
  probado, graduarlo a una app de integración de Omi (instalación de un toque). Latencia mínima
  **2–3 s**, y el audio del hospital **pasa por la nube de Omi**: las dos cosas se deciden con
  los ojos abiertos.
- **Arquitectura común:** una `IFuenteDeAudio` pura con tres implementaciones (micrófono local,
  collar por BLE, collar por teléfono) y **un solo selector** con la política (prioridad, relevo,
  remuestreo, reposición de silencio, AEC) al que le piden el micrófono **los cuatro consumidores**
  que hoy lo agarran cada uno por su lado.

**Las decisiones están tomadas** (§6). **Antes de escribir una línea, hay cinco mediciones de una
tarde** (§5). La primera es correr `U.exe` con el collar en esta máquina y leer el canal `omi` del
log: el estado real del transporte hoy es **desconocido**, no malo.

---

## 1. Lo que Omi ofrece de verdad, medido el 2026-09-01

Sale de leer el repo `BasedHardware/omi` en `main` (código, no prosa de marketing) y la
documentación en `docs.omi.me`. Donde el código y la doc discrepan, manda el código.

### 1.1 El protocolo BLE es pequeño, público y estable

Fuente: [`sdks/device/PROTOCOL.md`](https://github.com/BasedHardware/omi/blob/main/sdks/device/PROTOCOL.md),
que Omi declara «contrato compartido» de todos sus SDKs, y `omi/firmware/omi/src/lib/core/transport.c`.

| Qué | Valor |
|---|---|
| Servicio | `19b10000-e8f2-537e-4f6c-d104768a1214` |
| Audio (notify) | `19b10001-…` — propiedades `READ \| NOTIFY`, permiso `READ` **sin cifrado ni bonding** |
| Códec (read) | `19b10002-…` — primer byte: `0` PCM16 · `1` PCM8 · `20` Opus 160 muestras/10 ms (DevKit) · `21` Opus FS320, 320 muestras/20 ms (**CV1, el nuestro**) |
| Batería | `0x180F` / `0x2A19` |
| Trama | `[idx_lo, idx_hi, sub]` + carga Opus. Si la carga supera `MTU − 3`, se parte y `sub` cuenta los pedazos (`MAX_POSSIBLE_MTU 517`). La sonda del 2026-08-13 midió `sub` **siempre 0** con el MTU que negocia Windows |
| Salida | PCM 16 bits LE, mono, **16 000 Hz** — igual para todos los códecs |
| Conexión que pide el firmware | intervalo 7,5–15 ms, PHY 2M, DLE al máximo |
| Emparejamiento | **no lo exige**: no hay `bt_conn_set_security` en el firmware |

Y la instrucción de Omi para plataformas sin SDK completo, literal
([`sdks/device/README.md`](https://github.com/BasedHardware/omi/blob/main/sdks/device/README.md)):
*«BLE stacks are OS-specific (CoreBluetooth, bluer/btleplug, **WinRT**, Web Bluetooth, noble)…
Wire your platform BLE library to `AUDIO_DATA_UUID` and strip headers with these helpers.»* Los
helpers son cinco constantes y una función que quita 3 bytes.

**El collar calla cuando no hay voz, y eso lo hace el firmware.** `omi/firmware/omi/src/mic.c`
usa la detección acústica por hardware del micrófono T5838 (AAD): si la amplitud media absoluta
baja de `CONFIG_OMI_VAD_ABS_THRESHOLD` durante `CONFIG_OMI_VAD_HOLD_MS`, apaga el PDM y **deja de
producir tramas** hasta que el pin WAKE oye algo. En `main` hoy: umbral 250 y espera 10 s
(`omi.conf`); el `Kconfig` trae 600 y 3 s por defecto. Opus va con `DTX(0)`: la supresión no es
del códec, es del micrófono. La sonda del 2026-08-13 midió huecos **mucho más cortos que 10 s**
con numeración contigua (54 % del reloj en silencio, un parón de 2,01 s sin saltar un número), así
que el firmware que lleva nuestro CV1 calla más agresivamente que el de `main`. Vale lo medido.
La consecuencia no depende de la ruta: **el flujo no dura lo que dura el reloj**, y cualquier
detector de turno por pausa (OpenAI `semantic_vad`, Soniox) necesita que alguien reponga el
silencio. Eso ya existe en `voz/Omi/Reposicion.cs` y es lo que la promesa 2 juzga.

### 1.2 Los SDKs, y para qué sirve cada uno en Windows

| SDK | Pila BLE | ¿Sirve para `U.exe`? |
|---|---|---|
| `sdks/python` (`omi-sdk` 0.1.0 en PyPI) | `bleak` 0.22.3 + `opuslib` | La lógica útil son ~40 líneas: `start_notify`, quitar 3 bytes, `opus_decode`. El `pyproject.toml` de `main` declara `pyobjc-*` (solo macOS) **sin condición**; sin embargo la versión de PyPI **sí se instaló en Windows** el 2026-08-14 (`puente-omi/.venv`), o sea que PyPI va detrás de `main`. Hay que comprobarlo antes de contar con `pip install` en un PC nuevo |
| `sdks/swift` | CoreBluetooth | iOS/macOS |
| `sdks/react-native`, `sdks/omi-expo` | react-native-ble-plx | iOS/Android |
| `sdks/device/{typescript,go,rust,cpp,dart}` | ninguna o **opcional** (btleplug, SimpleBLE, noble) | solo el contrato: UUIDs + `strip_packet_header`. Ningún C# |
| `sdks/rust/omi-device` | ninguna, a propósito | protocolo + comandos: *«deliberately does not choose a BLE runtime»* |
| **App de escritorio para Windows** (`desktop/windows`, Electron, v1.0.35) | **ninguna** | Su auditoría interna: *«Entire BLE stack still absent (Phase 7 deferred)»*. Graba el micrófono del PC y lo manda a `/v4/listen`. **No habla con el collar** |

**Conclusión para el caso A:** no existe camino oficial de Omi en Windows. Lo oficial y estable es
**el contrato del protocolo**; la pila BLE de Windows es WinRT, que es exactamente lo que `bleak`
y `btleplug` usan por debajo. Escribir WinRT + 3 bytes + Opus no es «reinventar»: es lo que Omi
indica para toda plataforma sin SDK completo, y es lo que `FuenteOmi.cs` ya hace.

### 1.3 La app oficial exporta el audio en crudo, y lo manda su nube — no el teléfono

Fuentes: [`AudioStreaming.mdx`](https://docs.omi.me/doc/developer/apps/AudioStreaming),
[`Integrations.mdx`](https://docs.omi.me/doc/developer/apps/Integrations),
`backend/routers/pusher.py`, `backend/utils/webhooks.py`, `backend/utils/http_client.py`,
`backend/database/webhook_health.py`, `backend/utils/app_integrations.py`,
`app/lib/providers/developer_mode_provider.dart`.

Hay **dos** mecanismos oficiales, y comparten tubería:

| | Developer Mode → «Realtime audio bytes» | App de integración con trigger `audio_bytes` |
|---|---|---|
| Quién lo configura | el usuario pega la URL y «Every x seconds» (se guarda como `url,segundos`; por defecto `5`) | nosotros creamos la app; el usuario la **instala** desde la tienda de Omi |
| Cada cuánto llega | cada **N segundos de audio** (N lo pone el usuario; el código acepta `1`) | **4 s fijos** (`audio_bytes_trigger_delay_seconds = 4`) |
| Reintentos | a 1 s, 5 s y 30 s (4 intentos) + cola de muertos | **ninguno** |
| Se apaga solo | tras **100** fallos consecutivos (cualquier no-2xx cuenta; un 2xx resetea), y avisa al usuario por push | corta-circuitos |
| Identidad del usuario | la que metamos en la URL: Omi **conserva** los parámetros existentes y añade los suyos | el `uid` de Omi; el «Auth URL» de la app recibe `uid` para mapearlo |
| Publicación | nada | privada → pública exige revisión (~24 h) |

Lo que llega, en los dos casos:

```
POST <tu-url>?…&sample_rate=16000&uid=<uid de Omi>
Content-Type: application/octet-stream
Idempotency-Key: <uuid4>            (solo la ruta Developer Mode)
Body: PCM16 little-endian, mono, 16 kHz — trozos de EXACTAMENTE 1 s (32 000 bytes)
```

Cómo funciona por dentro, que es lo que fija los límites:

1. El teléfono manda el Opus del collar por WebSocket a `/v4/listen` (la «sesión de escucha en
   vivo» de Omi). El backend decodifica a PCM.
2. `pusher.py` acumula bytes y **dispara cuando `len > sample_rate × N × 2`** — N segundos *de
   audio*, no de reloj. Si el collar calla (AAD), el buffer no crece y no llega nada.
3. `send_audio_bytes_developer_webhook` parte el buffer en trozos de 1 s
   (`_AUDIO_BYTES_WEBHOOK_CHUNK_SECONDS = 1`) y los manda **en serie, con un candado por uid**.
4. Cliente HTTP: timeout total 30 s, conexión 2 s, 64 en vuelo; corta-circuitos a 5 fallos →
   30 s abierto → 1 sonda.

Restricciones que salen del código y de la doc, no de suponer:

- **Exige sesión de escucha activa**: la app abierta (en segundo plano vale; *swipe-away* la
  mata, lo dice su centro de ayuda) y el teléfono con internet. Lo grabado sin conexión
  (store-and-forward) **no** se reproduce por el webhook.
- **También dispara con el micrófono del teléfono**, sin collar (issue #11365, ago-2026): un
  iPhone solo ya sirve de micrófono de Ü.
- **Latencia mínima**: N s de buffer (≥1) + POST + relé. Con N=1, unos **2–3 s** de punta a
  punta; con el 5 por defecto, 6–7 s. Suficiente para dictado y órdenes; **no** para
  interrumpir a Ü a media frase.
- La URL puede ser `http` y hasta IP privada (la validación es una regex de esquema y host).
  Es un dato, no un plan: el destino tiene que ser un servidor nuestro, no el PC.
- **`audio_bytes_websocket`** existe como tipo en el backend, pero en la app está **comentado**:
  no hay variante por WebSocket utilizable.
- Está vivo y con aristas: en agosto de 2026 se arregló que no disparara con Limitless Pendant
  (PR #12006) y que el toggle se reactivara solo. Es una función que se usa y se mantiene.

### 1.4 Lo que la ruta por el teléfono implica y hay que decidir con los ojos abiertos

- **El audio del hospital pasa por la nube de Omi**: se transcribe (Deepgram) y se guarda como
  conversación en la cuenta Omi del usuario, además de llegarnos. Con pacientes de por medio es
  una decisión de cumplimiento, no técnica. Ni el webhook ni la app de integración la evitan.
- **Un collar, un central.** BLE solo admite un conectado. Si la app de Omi está en el teléfono
  y emparejada, **se reconecta sola y el PC no puede conectarse** mientras tanto. Los casos A y
  B son excluyentes en cada instante, y `U.exe` tiene que decir «el collar está con el teléfono»
  en vez de fallar mudo.
- **Omi no manda silencio ni reloj.** Reponer el silencio hay que hacerlo nosotros, y con
  trozos de 1 s la única referencia es la hora de llegada al relé.

---

## 2. Lo que tenemos, y por qué «da problemas»

Informe completo del repo del 2026-09-01; aquí lo que decide.

### 2.1 Dos implementaciones, una activa sin verificar y otra verificada y apagada

| | Activa: `voz/Omi` + `windows-client/src/Voice/FuenteOmi.cs` | Apagada: `puente-omi/puente.py` |
|---|---|---|
| Qué es | núcleo puro `net8.0` (códec, trama, reposición, relevo) + transporte WinRT en el `.exe` | SDK oficial `omi-sdk` + `bleak` + `sounddevice` → VB-Cable; el collar aparece como micrófono de Windows para cualquier programa |
| Quién lo juzga | las 7 promesas del núcleo, verdes (`voz/Contrato/Contrato.cs`, 22/22). **El transporte, nadie**: `contrato.yml:99-101` lo dice: *«que este job esté verde no dice que el collar funcione»* | nadie automático; a mano |
| Evidencia de audio real | **ninguna en este disco**: el log más reciente es `u-20260814.log` (17 días antes del último trabajo de voz) y tiene 0 líneas de `omi`/`collar`; `%LOCALAPPDATA%\U\collar.json` **no existe** | **sí**: *«encontró el collar, decodificó Opus, entregó audio real a "CABLE Output" durante 40 s sin fallos»* (2026-08-14) |
| Estado | spec 001 en **propuesto**, 5/5 casillas de cierre vacías; la fase 7 (nivel 4 a mano) nunca se cerró; la spec cita `GeminiLive.cs`, que ya no existe | apagada por decisión del usuario el 2026-08-16 «porque la activa es la del `.exe`» |

### 2.2 Los seis fallos del transporte son una sola clase

Están conservados como comentarios en el código, con fecha y medida. Leídos juntos:

| Fecha | Qué se vio | Causa | Dónde quedó |
|---|---|---|---|
| 08-13 | el collar se caía a **30,0 s exactos**, cuatro veces (1471, 1470, 1474, 1476 tramas) | faltaba `GattSession.MaintainConnection = true` | `FuenteOmi.cs:159-160` |
| 08-13 | encender la voz con el botón del collar abría la conversación **oyendo por el portátil** | bandera `UsarCollar` no puesta | `LiveAudio.cs:89-93` |
| 08-1x | Ü hablando y a los 4 s «collar perdido: 4416 ms sin trama» | el umbral medía silencio, no ausencia | `Relevo.cs:45`, `LiveAudio.cs:70` |
| 08-1x | Bluetooth «desconectado» y «volvió» en el mismo segundo | parpadeo de Windows | `GraciaDesconexionMs = 3000` |
| 08-14 | el punto azul decía gris con el audio entrando | el dibujo no se enteraba | `LiveAudio.cs:259-264` |
| **08-25, en demo** | **56 minutos «conectado» sin una sola trama**: verde en el panel, azul en la carita, sordo | WinRT tira las suscripciones GATT al caer el enlace; `MaintainConnection` lo rehace cada ~31 s y nadie resuscribía | `ReengancharAsync`, `FuenteOmi.cs:337-346` (commit `45213a0`, 08-30, **sin verificar después**) |
| 08-30 | tras reenganchar, «3358464 ms sin trama» a los dos segundos | el reloj viejo juzgaba el enlace nuevo | `FuenteOmi.cs:375-380` |

Cinco de siete son variantes de **«el enlace dice conectado y no llega nada»**, y el diseño que
los produce es uno: `FuenteOmi` **parchea** una sesión que Windows reconectó por su cuenta —
reescribe el CCCD, rearma un reloj, recrea el servicio del botón— y cada parche deja un estado
viejo vivo que el siguiente fallo aprovecha. Es el aprendizaje nº6: cuando la maquinaria de
compensación crece por capas, cada capa trae su modo de fallo.

El SDK oficial de Omi no tiene este problema porque **no parchea**: `listen_to_omi` es un
`async with BleakClient(...)` — si el enlace cae, el cliente muere, el `with` sale y quien llama
**reconstruye todo desde el rastreo**. `puente.py` heredó ese modelo y es la única ruta con audio
real verificado. No es casualidad.

### 2.3 «Toda la app» hoy es falso, y no hay dónde colgarlo

- **Tres captadores de micrófono** en el cliente, contados con grep (patrón nº5), y solo uno ve
  el collar: `LiveAudio.cs:209-215` (`WaveInEvent`, sí), `VoiceIO.cs:96-98`
  (`SetInputToDefaultAudioDevice`, **no**), `ScreenRecorder.cs:64-83` (ScreenRecorderLib, **no**).
  `DictadoSoniox` se cuelga de `LiveAudio.Capturado`, así que hereda lo que `LiveAudio` decida.
- **Cero interfaces** de fuente de audio (`grep interface I(Audio|Fuente|Micro|Voz)` → 0).
  `Omi.Fuente` es un contrato de *formato* (Hz/Bits/Canales), no de comportamiento.
- El collar es un **injerto** dentro de `LiveAudio`: `UsarCollar` (static), `_usandoCollar`,
  `QuiereCollar`, `_abriendoCollar`, un `Timer _vigilante`. `LiveAudio` hace tres trabajos:
  captura local, orquestación del collar y reproducción.
- Asimetrías que ya duelen: el **AEC solo se aplica al micro local** (`LiveAudio.cs:223`);
  `Omi.Fuente.Hz = 16000` pero al socket viajan **24 kHz** desde el cambio a OpenAI (08-24) y el
  remuestreo (`RemuestreadorPcm16`) **no lo juzga ningún contrato**, así que la promesa 3 hoy
  certifica algo que ya no describe lo que viaja; `CancelaEco` asume 16 kHz y se construye a 24.

La costura buena ya existe: **`LiveAudio.Capturado`** (`LiveAudio.cs:111`). Todo lo que pasa por
ahí es indistinguible aguas abajo — es lo que hoy permite que la compuerta de eco cubra las dos
fuentes sin enterarse (`ConversacionEnVivo.cs:946`). Falta invertirla: que las fuentes la
implementen en vez de que `LiveAudio` las cablee.

---

## 3. Los dos casos, y qué es «oficial» en cada uno

### 3.1 Caso A — el collar directo al PC

Tres formas posibles, contra los criterios que importan en un PC de hospital:

| | A1 · WinRT propio, rediseñado (`FuenteOmi`) | A2 · sidecar Python con el SDK oficial | A3 · `puente.py` + VB-Cable (existe) |
|---|---|---|---|
| Qué tiene de «oficial» | el contrato del protocolo, que es todo lo que Omi da para Windows | `omi-sdk` + `bleak` (que es WinRT por debajo) | ídem + un driver de terceros |
| Qué se instala en el PC | nada nuevo: `U.exe` ya lleva WinRT por el TFM y Concentus 2.2.2 (Opus en C# puro, 0 tramas falladas en la sonda) | un Python 3.12 empaquetado (~40 MB), `opus.dll`, y un canal `U.exe` ↔ sidecar | Python + **driver de audio en kernel** (VB-Cable, donationware; uso empresarial pide licencia) |
| Smart App Control | ya resuelto para `U.exe` (Release firmado) | **riesgo real**: el repo ya pagó `0x800711C7` con binarios sin firmar; un `.exe` de PyInstaller es exactamente eso | driver firmado por VB-Audio; el Python, igual que A2 |
| Reconexión | hoy parchea (§2.2); **hay que pasar al modelo «reconstruir»** | reconstruye por diseño (`async with`) | ídem |
| Sirve a otras apps del PC | no | no | **sí** — es su razón de ser |
| Evidencia hoy | 7 promesas verdes; transporte **desconocido** | — | 40 s de audio real (08-14) |
| Juzgable en CI | el núcleo sí; el transporte **puede serlo** si la máquina de estados es pura | no (proceso aparte) | no |

**Recomendación: A1, adoptando el modelo de A2.** Concretamente:

1. **Reconectar = reconstruir.** Ante cualquier `Disconnected`, ningún objeto GATT anterior se
   reutiliza: se redescubre el servicio (`Uncached`), se relee el códec, se recrea el decoder, se
   resuscribe, y el reloj de la reposición arranca de cero. `MaintainConnection` se queda (la
   caída a 30 s es real), pero cada reconexión de Windows se trata como una conexión nueva, no
   como una sesión que hay que remendar. Se borra `ReengancharAsync` en vez de arreglarlo
   (aprendizaje nº6).
2. **La máquina de estados sale del `.exe` y se vuelve pura**, en `voz/Omi/Enlace.cs` (o
   similar): entradas `Conectado`, `Desconectado`, `Trama(nº, ms)`, `Tick(ms)`; salidas
   `Reconstruir`, `Relevar`, `Nada`. Los siete fallos de §2.2 se vuelven **fixtures**, igual que
   los dos que ya viven en `Relevo` (promesas 6 y 7). Es el mismo movimiento que la spec 001 hizo
   para justificar `voz/Omi`: lo que CI no puede tocar se deja delgado, lo que decide se juzga.
3. **El criterio de «funciona» no es «conectado»: es «primera trama en < N s tras reconectar».**
   Va al log con número (`collar reconstruido en 840 ms · trama nº1 a los 1210 ms`) y es lo que
   se pega en el PR. Un `U.exe --sonda-collar` que imprima tramas/s, huecos y reconexiones
   convierte el nivel 4 en una tabla en vez de en «probado».

Lo que **no** se toca del transporte actual porque cada línea tiene medición detrás: guardar el
`GattDeviceService` para que el GC no cierre la sesión, `MaintainConnection`, `Uncached`,
rastrear por UUID de servicio y no por nombre, el UUID del botón (`23ba7924/23ba7925`, que no
está en ninguna doc de Omi y salió de enumerar el GATT).

Sobre A3: no resuelve el pedido («el micrófono de Ü») sino otro («el micrófono de Windows»). Se
conserva apagado como está. Si algún día hace falta que Zoom o Teams oigan el collar, ya existe.

### 3.2 Caso B — el collar al teléfono, y del teléfono a Ü

```
collar ──BLE──► app oficial Omi (iOS/Android) ──WS /v4/listen──► nube Omi
                                                                   │  buffer N s · trozos de 1 s
                                                                   ▼
        POST https://<graph>/omi/audio?code=XXXXXXXX&sample_rate=16000&uid=<omi>
                                                                   │  valida code → usuario · 200 en < 100 ms
                                                                   ├──► canal en vivo hacia U.exe   (relé)
                                                                   └──► (opcional) bucket omi/<code>/<fecha>/<seq>.pcm
U.exe ◄── suscripción ── FuenteOmiPorTelefono: base64 → PCM16 16 k → reposición por hora de llegada → Selector
```

**Qué es oficial aquí:** la app de la App Store tal cual, y el webhook de Developer Mode
documentado y mantenido por Omi. No hay que publicar nada en ninguna tienda para empezar.

**Emparejamiento por código, como el portal clínico** (`CLAUDE.md` §*El puente con el portal*):
`U.exe` muestra un código de 8 caracteres; el usuario pega en la app de Omi
`https://<graph>/omi/audio?code=XXXXXXXX` y el intervalo `1`. Omi conserva `code` y añade
`sample_rate` y `uid`. El primer POST **ata** ese `uid` de Omi al código: si otro `uid` llega con
el mismo código, se rechaza con 4xx y queda en el log. El código caduca (8 h, como el portal) y
un 410 sostenido hace que Omi apague el webhook solo a los 100 fallos — que es lo que queremos.

**El relé.** El webhook llega a un servidor nuestro, no al PC, así que hace falta un canal
servidor → `U.exe`. Dos opciones razonables, y una recomendación:

| | Supabase Realtime Broadcast (ya tenemos proyecto) | Sondeo corto contra Graph |
|---|---|---|
| Cómo | el endpoint reenvía cada trozo con `POST /realtime/v1/api/broadcast` a un canal privado `omi:<code>`; `U.exe` se suscribe por WebSocket | el endpoint guarda el trozo (Redis/tabla); `U.exe` pide `GET /omi/audio?code&since` cada 500 ms |
| Tamaño | 1 s PCM = 32 000 B → ~43 KB en base64 (los clientes C# no traen broadcast binario). Límite Free **256 KB/msg, 100 msg/s, 200 conexiones**; Pro 3 MB y 500 msg/s. Sobra | igual |
| Latencia añadida | decenas de ms | hasta 500 ms |
| Qué añade | una dependencia (`realtime-csharp` o ~100 líneas de canal Phoenix) | otro almacén y un bucle de sondeo |

**Recomendación: Supabase Realtime**, porque ya está en la pila (Storage, el archivo de
`TeachSession`) y Vercel no sostiene WebSockets. El «bucket» que pediste sale gratis del mismo
endpoint: cada trozo se puede archivar en Storage además de emitirse; sirve para escuchar después
lo que Ü oyó, no para el vivo.

**Lo que esta ruta acepta y hay que decir en voz alta:**

- **2–3 s de latencia** en el mejor caso. Dictado, órdenes y conversación con turnos lentos:
  bien. Interrumpir a Ü: no. La fuente lo declara (`LatenciaNominalMs`) para que
  `ConversacionEnVivo` no espere lo que no puede haber.
- **La nube de Omi oye y guarda todo** lo que se diga con el collar en modo teléfono. Decisión
  del dueño y del hospital, antes de la primera prueba con un paciente.
- Depende del teléfono: app viva (no *swipe-away*), internet, batería. Si el teléfono se va,
  el relevo al micrófono local es el mismo que ya existe para BLE (promesa 4).
- Graduar a **app de integración** de Omi cuando el flujo esté probado: instalación de un toque y
  nuestro «Auth URL» recibe el `uid` para atarlo a la cuenta — pero 4 s fijos y sin reintentos.
  Primero Developer Mode, que es configurable y reintenta.

### 3.3 Los dos casos se excluyen en cada instante

Un collar solo tiene un central. El selector (§4) tiene que **saber** en qué modo está el
collar y decirlo: «conectado al teléfono, audio por relé» / «conectado al PC» / «no se ve». Sin
eso, el caso más común en la práctica —la app de Omi en el bolsillo reconectándose sola— se vive
como «el PC no encuentra el collar», que es exactamente el mensaje que no distingue sus causas
(aprendizaje nº2).

---

## 4. La arquitectura: una fuente de audio para toda la app

```
                 ┌────────────────────┐
  micro del PC ─►│ MicrofonoLocal     │─┐
   (NAudio)      └────────────────────┘ │
                 ┌────────────────────┐ │    ┌──────────────────────────────────────┐
  collar BLE ───►│ CollarPorBle       │─┼───►│ SelectorDeFuente (puro, contratable)  │──► Capturado (PCM16, Hz destino)
   (FuenteOmi)   └────────────────────┘ │    │  prioridad · relevo · reposición ·    │        │
                 ┌────────────────────┐ │    │  remuestreo · AEC · «quién manda» y   │        ├─► ConversacionEnVivo (OpenAI)
  relé ─────────►│ CollarPorTelefono  │─┘    │  por qué, en el log                   │        ├─► DictadoSoniox
   (Realtime)    └────────────────────┘      └──────────────────────────────────────┘        ├─► VoiceIO (System.Speech)
                                                                                               └─► ScreenRecorder (entrada)
```

- **`IFuenteDeAudio`**, en un proyecto puro (`voz/Audio/` o dentro de `voz/Omi/`):
  `AbrirAsync()`, `Cerrar()`, `event Capturado(byte[] pcm, long msLlegada)`, `Viva`, `Hz`,
  `LatenciaNominalMs`, `Quien`. Tres implementaciones reales y una `Nula` para pruebas.
  El transporte WinRT y el cliente Realtime son cascarones delgados que implementan la interfaz;
  la lógica (estados, relojes) vive en el proyecto puro.
- **`SelectorDeFuente`**: una sola política, en un solo sitio, juzgable: prioridad
  `CollarPorBle > CollarPorTelefono > MicrofonoLocal`; relevo con la gracia de `Relevo`;
  **reposición por hora de llegada para toda fuente** (el collar calla en las dos rutas);
  remuestreo al `Hz` que pida el consumidor (hoy 24 k para OpenAI, 16 k para Soniox) — y con eso
  la promesa 3 vuelve a describir la realidad; el AEC **antes** del selector, sobre cualquier
  fuente, no solo la local. Cada cambio de fuente deja una línea con el motivo.
- **Los cuatro consumidores le piden el micrófono al selector.** Hoy son tres captadores y un
  colgado; después, uno. Ese «uno» es lo que hace verdadero «el Omi como micrófono de toda la
  app», y es contable con grep: la promesa es «hay exactamente un sitio que abre un dispositivo
  de captura».
- **`LiveAudio` se queda con la reproducción** y con enchufar la fuente que el selector elija.
  Las cinco banderas y el vigilante desaparecen con `ReengancharAsync`.

Qué juzga cada nivel:

| Nivel | Qué | Cómo |
|---|---|---|
| CI (contrato de la voz) | selector, máquina de estados del enlace, reposición por llegada, formato de salida, «un solo captador» | fixtures con relojes sintéticos, como las promesas 2, 6 y 7 |
| A mano, nivel 4 | que el audio real llegue por las **tres** fuentes y que Ü **conteste** | `--sonda-collar` + log, en ≥2 pantallas, con nombre |

---

## 4-bis. Lo medido con el collar y el teléfono, 2026-09-01

Primera medición real de la ruta B, sobre un **Android** con la app oficial de Omi y el collar
puesto. El receptor es nuestro propio backend: `POST /api/omi/audio` en Graph (commit `56e62ab`),
que responde 200 y anota hora y tamaño. **Del audio solo se mide el tamaño: no se guarda.**

### Lo que se aprendió antes de poder medir, y costó 40 minutos

| Qué pasó | Qué lo explicaba |
|---|---|
| Con el interruptor encendido, la URL correcta y la transcripción funcionando en pantalla, **no llegó ni un byte durante 40 minutos** | El receptor de pruebas gratuito (webhook.site) **corta a las 50 peticiones** y desde ahí devolvía `429`. Con intervalo de 1 s se agotó en dos minutos |
| Se acusó al collar, al interruptor de Omi y al modo segundo plano | Ninguno tenía nada que ver. **El fallo estaba en el receptor**, y era invisible desde el teléfono |

**De aquí sale una restricción de diseño, no una anécdota:** Omi apaga el webhook del usuario tras
**100 respuestas seguidas que no sean 2xx**, en silencio. Nuestro endpoint va declarado **antes**
de `app.use('/api', apiLimiter)` (120/min) justo por eso: a ~60 peticiones por minuto, el limitador
propio reproduciría el mismo apagón contra nosotros mismos. Es el aprendizaje nº2 con otra cara —
un `429` no distingue «te estás pasando» de «este servicio no te quiere», y el síntoma aparece a
kilómetros de la causa.

### La medición (03:20:48 → 03:22:00 UTC, ~70 s de reloj)

| Qué | Medida |
|---|---|
| Formato | PCM16 · 16 000 Hz · mono, exactamente lo que dice la doc |
| Tamaño típico | **32 000 bytes = 1 s** de audio, y colas parciales de 900, 600, 100, 80 y 20 ms |
| Cadencia | **ráfagas de ~4 trozos (≈4 s de audio) cada ~4 s de reloj**, con 0,2 s entre trozos de la misma ráfaga |
| Latencia extremo a extremo | **≈4-5 s** con la configuración de esta prueba, no los 2-3 s del mejor caso teórico |
| Audio entregado vs. reloj | **58,6 s de audio para 69,9 s de reloj = 84 %** |
| Hueco mayor sin nada | **14 s** seguidos (03:21:00 → 03:21:14) |
| Respuestas | **todas 200**, ni un `429`, ni un fallo |
| Identidad | `uid` de Omi constante; nuestro `code` sobrevive intacto en la URL |

**Las dos conclusiones que cambian el diseño:**

1. **Omi no manda el silencio, y ahora está medido en la ruta del teléfono también** (84 % del
   reloj, un hueco de 14 s). La promesa 2 —reponer el silencio por hora de llegada— **no es
   exclusiva del BLE**: aplica igual aquí, y por eso vive en el selector y no en la fuente.
2. **La latencia real es de 4-5 s, no de 1 s.** Aunque el intervalo se pida en 1 s, lo observado
   son ráfagas de 4 s. Eso confirma que esta ruta sirve para dictar y dar órdenes y **no** para
   interrumpir a Ü a media frase, y que la fuente tiene que declarar su latencia
   (`LatenciaNominalMs`) en vez de que el consumidor la suponga.

### Segunda corrida: el teléfono bloqueado y en el bolsillo (03:26 UTC)

El dueño dictó seguido durante varios minutos **con la pantalla del teléfono apagada** y el collar
con el punto azul. Resultado:

| Qué | Medida |
|---|---|
| Peticiones en 8 minutos | **257**, todas 200 |
| Ventana analizada | 03:26:06 → 03:26:38 = **32 s de reloj** |
| Audio entregado | **~35 s** — es decir, **el 100 % del habla, sin un solo hueco** |
| Cadencia | **exactamente 4 trozos cada ~3,9 s**, sin desviación |
| Con la pantalla apagada | **no cambia nada**: ni corte, ni retraso extra, ni reconexión |

**Esto cierra dos cosas de golpe:**

1. **La ruta B sirve fuera del escritorio.** El teléfono bloqueado y en el bolsillo entrega igual
   que con la app delante. La duda de si Android mataría la app en segundo plano queda descartada
   para sesiones de minutos (falta el caso de horas, que es otra medición).
2. **El 16 % que faltaba en la primera corrida era silencio, no pérdida.** Hablando seguido llega
   el 100 %; con pausas llegó el 84 % y un hueco de 14 s. La diferencia es exactamente lo que el
   collar decide no transmitir. **No hay pérdida en la tubería de Omi**, y la reposición del
   silencio es lo único que hay que construir.

La regularidad de las ráfagas —4 trozos cada 3,9 s, sin variación— dice además que **el intervalo
efectivo es 4 s**, no el 1 que se pidió. Hay que confirmar qué número quedó guardado en la app;
si de verdad es 1 y Omi lo agrupa a 4 igual, entonces 4 s es el suelo de latencia de esta ruta y
no se puede bajar.

### Un hallazgo lateral

Llegaron también peticiones **sin `sample_rate` y con cuerpo vacío**. No son un fallo de Omi: son
el webhook de **transcripción en tiempo real**, que seguía apuntando a la misma URL. Su cuerpo es
JSON y `bodyParser.json` (declarado antes en `server.js`) ya lo había consumido, así que
`express.raw` recibía el flujo vacío. Cuando la transcripción de Omi se quiera usar de verdad, va
a **otra ruta**, no a la del audio.

## 4-ter. Cómo NO depender del plan de Omi: el modo «Custom STT»

Pregunta del dueño el 2026-09-01: *«¿hay forma de configurar la app para que no transcriba, sino
que solo mande el audio crudo, o de que su plan no nos afecte?»*. **Sí, y es una vía oficial de
Omi, no un truco.** Todo lo de abajo está leído en el código de `BasedHardware/omi@main`.

### La prueba, en cuatro citas del propio backend de Omi

| Dónde | Qué dice | Qué significa |
|---|---|---|
| `utils/listen_session_bootstrap.py` | `if use_custom_stt: user_has_credits = True` `else: user_has_credits = await ... has_transcription_credits(uid, ...)` | Con Custom STT **ni siquiera se consulta el saldo**: se da por bueno |
| `utils/fair_use.py` | *«custom_stt is metered but never live-enforced: those users transcribe on their own provider and **are exempt from transcription caps**»* (issue #7690) | El tope del plan **no aplica**. Se contabiliza para estadística, no para cortar |
| `utils/fair_use.py` | `LIVE_SPEECH_SOURCES = ('realtime', 'sync_fresh')` y las funciones de restricción solo miran esas | `custom_stt` queda fuera de la vigilancia por diseño |
| `routers/listen/receiver.py` | *«Custom-STT clients own transcript production. Their channel sockets are intentionally absent, but **captured audio still proceeds to the pusher mix path**»* | Omi deja de transcribir **y el audio sigue llegándonos** |

Y el webhook de audio no depende del saldo por otro camino: en `receiver.py` la acumulación se
decide **solo** por `if self.host.audio_bytes_send is not None` — es decir, por si hay un consumidor
configurado. `user_has_credits` aparece **únicamente** en `transcripts.py`, y lo que apaga es el
envío de *transcripciones*, no el de audio.

Detalle adicional: el muro de pago por antigüedad (`is_trial_paywalled`) **solo aplica a escritorio**
(`macos`, `windows`, `desktop`). Desde el móvil, que es nuestra ruta B, devuelve `False`.

### Y hay algo mejor: Omi puede mandarnos el audio DIRECTO, sin pasar por su nube

La app tiene, en **Ajustes → Transcription**, un modo con cuatro fuentes: `Omi`, `On Device`,
`Cloud Provider` y `Omi Parakeet`. El de «Cloud Provider» admite **un endpoint arbitrario**:

| Qué ofrece la app | Fuente |
|---|---|
| **URL de WebSocket que escribe el usuario**, sin validación ni lista blanca | `services/sockets/pure_streaming_stt.dart`: `IOWebSocketChannel.connect(config.url, headers: config.headers, …)` |
| **Cabeceras propias** — sirven para autenticar por usuario | mismo archivo; `CustomSttConfig` guarda `headers` |
| **Audio PCM crudo**, ritmo configurable (16 kHz por defecto) | `'mimeType': 'audio/pcm;rate=$sampleRate'` |
| **Respuesta con el esquema que definamos** (`schemaJson`) | `SttTranscriptionResult.fromJsonWithSchema(...)` |
| Interruptor **«Send raw audio to Omi»** (`sendRawAudioToOmi`), por proveedor | `transcription_settings_page.dart`; su test fija el nombre del campo |
| Funciona con el audio **del collar por BLE**, no solo con el micro del teléfono | `transcription_service.dart`: la fábrica toma `sampleRate` y `codec` del aparato |
| Modo compuesto: primario = nuestro endpoint, secundario = Omi | `CompositeTranscriptionSocket(primarySocket, secondarySocket, forwardRawAudioToSecondary: config.sendRawAudioToOmi)` |

**Eso convierte la ruta B en algo mucho mejor de lo diseñado en §3.2:**

| | Ruta B por webhook (§3.2) | Ruta B por Custom STT directo |
|---|---|---|
| Camino del audio | collar → móvil → **nube de Omi** → nuestro backend | collar → móvil → **nuestro backend** |
| Latencia medida / esperable | **4-5 s** (ráfagas de 4 s) | streaming continuo, del orden de **cientos de ms** |
| Plan de Omi | consume minutos; tope aplica | **exento**, y Omi ni transcribe |
| El audio del hospital | pasa por Omi y se guarda allí | **no toca los servidores de Omi** si el interruptor está apagado |
| Qué es la app de Omi | pasarela + nube | **solo una pasarela BLE** |
| Emparejamiento | código en la URL | código en la URL **y** en cabeceras propias |

Con esto, la objeción de cumplimiento de §1.4 —el audio clínico pasando por un tercero— **deja de
existir en la ruta B**, y la latencia deja de descartar la conversación hablada.

### MEDIDO el 2026-09-01: funciona, y en tiempo real

Sonda desplegada como función de Supabase (`omi-directo`, proyecto `miracle-app`), configurada en
la app como **Cloud Provider → Custom** con la URL
`wss://…/functions/v1/omi-directo?code=PRUEBA01`. Anota solo metadatos; el audio no se guarda.

| Qué | Medida | Qué significa |
|---|---|---|
| Duración de la sesión | **100,7 s continuos**, sin cortes | el enlace aguanta |
| Tramas | **3.856** | ~50 por segundo |
| Bytes | **2.467.840** | |
| Ritmo sostenido | **~32.000 B/s** (29.323–34.560 con jitter) | exactamente PCM16 · 16 kHz · mono en tiempo real |
| Tamaño de trama | **640 bytes, constante** | **320 muestras = 20 ms**: es la trama nativa del CV1 (Opus FS320) ya decodificada. **La firma del collar**, no del micro del teléfono |
| Cadencia | una trama cada **20 ms** | frente a ráfagas de 4 s del webhook |
| Camino de vuelta | la app aceptó nuestras respuestas con el esquema `openAI` cada 3 s | podemos devolver transcripción propia |
| Handshake | **ninguno**: la app abre el socket y manda binario directo | no hay mensaje de arranque que imitar |

**Las tres preguntas quedan contestadas:** sí llega el audio **del collar** (lo prueba la trama de
640 bytes), en **PCM16 16 kHz listo para usar** (sin Opus que decodificar de nuestro lado), y la app
**acepta nuestro esquema de respuesta**.

Y una cuarta que no estaba en la lista: **por este camino el flujo dura lo que dura el reloj.**
32.000 B/s sostenidos durante 100 s es audio continuo, sin los huecos de supresión de silencio que
sí medimos en la ruta del webhook (84 %). O sea que **la reposición del silencio no hace falta en
esta ruta** — el teléfono ya entrega la línea de tiempo completa.

**Lo que costó y no se puede quitar del código:** sin `EdgeRuntime.waitUntil()`, Supabase da la
petición por terminada al devolver la respuesta del upgrade y **apaga la función con el socket
abierto**. Medido: `abierto`, `primera-trama`, y `shutdown` 600 ms después, con el cliente viendo un
cierre `1006` sin motivo. La promesa que espera al cierre es lo que sostiene la invocación.

**Lo que queda por comprobar de esta ruta:** que con «Enviar audio sin procesar a Omi» **apagado**
el audio deja de llegar a la nube de Omi (el interruptor está en esa misma pantalla, debajo de la
configuración del proveedor; en la corrida del 2026-09-01 quedó encendido). Y el consumo de batería
del teléfono con el socket abierto en sesiones largas.

## 5. Lo que hay que medir antes de escribir una línea

Aprendizaje nº13: pregúntale a la API antes de creerle al código. Una tarde, en este orden:

| # | Medición | Cómo | Decide | Estado |
|---|---|---|---|---|
| 1 | **Estado real de `FuenteOmi` hoy** | correr `U.exe` con el collar, hablarle 5 min con pausas, leer el canal `omi` del log: tramas/s, reconexiones, «primera trama tras reconectar» | si el arreglo del 08-30 aguanta o si la reconstrucción va primero | **pendiente** |
| 2 | **Qué manda Omi de verdad** | app de Omi + collar contra **nuestro** endpoint; tamaño de cada POST, cadencia real, si llega el silencio | el diseño de la reposición en el relé y la latencia que prometemos | **hecha 2026-09-01** → §4-bis |
| 2b | **Con el teléfono bloqueado y en el bolsillo** | bloquear la pantalla, hablar seguido, mirar si sigue llegando | si la ruta B sirve fuera del escritorio, o solo con la app delante | **hecha 2026-09-01** → §4-bis: sí, sin diferencia |
| **2c** | **El endpoint propio de Custom STT** | WebSocket propio (función de Supabase), app en Cloud Provider → Custom | si la ruta B se queda en el webhook de 4-5 s o pasa a streaming directo | **hecha 2026-09-01** → §4-ter: **streaming directo, PCM16 16 kHz, tramas de 20 ms, 100 s sin cortes** |
| 2d | **Que el interruptor «Enviar audio sin procesar a Omi» corte de verdad** | apagarlo y ver si el webhook de audio deja de dispararse | si el audio clínico puede quedarse fuera de la nube de Omi | **pendiente** |
| 3 | **Exclusividad** | con la app de Omi emparejada y viva, intentar `U.exe` por BLE; después cerrar la app y repetir | el mensaje del selector y el flujo de usuario | **pendiente** |
| 4 | **`pip install omi-sdk` en un PC limpio de Windows** | una VM o el portátil de otro | si A2/A3 siguen siendo opciones o si PyPI ya trae `pyobjc` | pendiente (solo si se reabre A2/A3) |
| 5 | **Firmware del collar** | leer versión desde la app de Omi; comparar con `omi.conf` de `main` (AAD 250 / 10 s) | explica los huecos de la sonda; dice si un update de firmware cambia la supresión | **pendiente** |

---

## 6. Decisiones tomadas

Tomadas el 2026-09-01 por el agente a petición del dueño («no soy técnico, tómalas tú; ya conoces
mi objetivo»). El objetivo que las ordena: el collar como micrófono de toda la app, por PC directo
y por la app oficial de Omi en el iPhone, sin publicar app propia y usando solo lo estable de Omi.

| # | Decisión | Por qué, en una frase |
|---|---|---|
| 1 | **Caso A: A1.** El transporte BLE se rediseña dentro de `U.exe` con «reconectar = reconstruir». **No** hay sidecar de Python ni VB-Cable | Omi no da nada oficial para Windows que un sidecar aporte, y un ejecutable Python sin firmar es justo lo que Smart App Control bloquea en un PC de hospital (`0x800711C7`, ya pagado) |
| 2 | **Caso B: sí, con la app oficial y su webhook**, aceptando que el audio pasa por la nube de Omi y que llega con 2–3 s de retraso | Es la única forma de que un iPhone sirva sin publicar app propia, que es el objetivo. La latencia vale para dictar y dar órdenes; interrumpir a Ü a media frase no será posible por esta ruta, y la app lo dirá |
| 3 | **El relé va sobre Supabase Realtime**, con archivo opcional en Storage | Ya está en la pila; Vercel no sostiene la conexión abierta que hace falta; el tamaño y el ritmo caben en el plan gratuito con margen |
| 4 | **La spec 001 se cierra absorbida en la 005** | Sus fases 0-6 están hechas, la 7 nunca se corrió, nombra un archivo que ya no existe y sus casillas llevan 19 días vacías |
| 5 | **Developer Mode primero; la app de integración de Omi, después** | Developer Mode es configurable a 1 s y reintenta; la app de integración va a 4 s fijos sin reintentos y exige revisión. Se gradúa cuando el flujo esté probado |

**Lo que el dueño tiene que saber de la decisión 2, dicho sin técnica:** con el collar conectado al
teléfono, todo lo que se diga lo oye y lo guarda también Omi, en la cuenta Omi del usuario, como
hace con cualquier conversación. Con el collar conectado al PC, no: el audio va del collar a Ü y
a nadie más. En un hospital eso puede importar. La decisión técnica está tomada; si el hospital
no puede aceptar ese paso por Omi, la ruta del teléfono se apaga con un interruptor y la del PC
sigue igual.

---

## 7. Lo que sigue

1. Las cinco mediciones de §5 (una tarde). Sus números van al «Diagnóstico» de la spec.
2. `/especifica` → `docs/specs/005-el-omi-es-el-microfono-de-toda-la-app.md`, en una rama
   `jose/omi-microfono-de-toda-la-app` nacida de `main`. Promesas candidatas, en continuación de
   la 22 del contrato de la voz:

   | # | Promesa candidata |
   |---|---|
   | 23 | hay exactamente **un** sitio en la app que abre un dispositivo de captura, y todos los consumidores le piden el micrófono a él |
   | 24 | con el collar disponible manda el collar; sin él, el micrófono local — y cada cambio deja el motivo en el log |
   | 25 | reconectar es reconstruir: tras una desconexión no se reutiliza ningún estado del enlace anterior, y la primera trama llega antes de N s o se releva |
   | 26 | el silencio se repone por hora de llegada **para toda fuente**, no solo para el collar por BLE |
   | 27 | el formato que sale del selector es el que pide el consumidor, venga la voz de donde venga (la 3, dicha sobre lo que viaja de verdad) |
   | 28 | el AEC se aplica a la fuente activa, sea cual sea |
   | 29 | un trozo del relé con un `uid` distinto al atado al código se rechaza y deja rastro |
   | 30 | un código caducado responde 410 y no acepta más audio |
   | 31 | la fuente por teléfono declara su latencia nominal y el consumidor la puede leer |

3. Fases: arnés (la máquina de estados pura entra en la compuerta) → selector y «un solo
   captador» → reconstrucción del enlace BLE → relé y `CollarPorTelefono` → nivel 4 con las tres
   fuentes, en ≥2 pantallas.

---

## Fuentes

Omi, leído el 2026-09-01 en `BasedHardware/omi@main`:
[PROTOCOL.md](https://github.com/BasedHardware/omi/blob/main/sdks/device/PROTOCOL.md) ·
[sdks/device/README.md](https://github.com/BasedHardware/omi/blob/main/sdks/device/README.md) ·
[sdks/python](https://github.com/BasedHardware/omi/tree/main/sdks/python) ·
[sdks/rust/omi-device](https://github.com/BasedHardware/omi/tree/main/sdks/rust/omi-device) ·
[desktop/windows](https://github.com/BasedHardware/omi/tree/main/desktop/windows) (README y
`docs/mac-parity-audit/08-bluetooth-wearables.md`) ·
`backend/routers/pusher.py` · `backend/utils/webhooks.py` · `backend/utils/http_client.py` ·
`backend/database/webhook_health.py` · `backend/utils/app_integrations.py` ·
`backend/models/users.py` · `app/lib/providers/developer_mode_provider.dart` ·
`omi/firmware/omi/src/mic.c` · `omi/firmware/omi/src/lib/core/{transport,codec}.c` ·
`omi/firmware/omi/{Kconfig,omi.conf}`.

Documentación de Omi: [Real-Time Audio Streaming](https://docs.omi.me/doc/developer/apps/AudioStreaming) ·
[Integration Apps](https://docs.omi.me/doc/developer/apps/Integrations) ·
[Submitting](https://docs.omi.me/doc/developer/apps/Submitting) ·
[Python SDK](https://docs.omi.me/doc/developer/sdk/python) ·
[Omi necklace issues (Help Center)](https://help.omi.me/en/articles/13154278-omi-necklace-issues) ·
[Issue #11365](https://github.com/BasedHardware/omi/issues/11365) ·
[Changelog 2026-04-09](https://feedback.omi.me/changelog/phone-calls-bluetooth-overhaul-offline-sync-and-more).

Supabase: [Realtime limits](https://supabase.com/docs/guides/realtime/limits) ·
[Broadcast](https://supabase.com/docs/guides/realtime/broadcast).

Windows/.NET: [GattCharacteristic.ValueChanged](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.genericattributeprofile.gattcharacteristic.valuechanged) ·
[Concentus 2.2.2](https://www.nuget.org/packages/Concentus).

Este repo: `docs/specs/001-hablarle-a-la-carita-por-el-omi.md` · `voz/Omi/*` ·
`voz/Contrato/Contrato.cs` · `windows-client/src/Voice/{FuenteOmi,CollarPermanente,LiveAudio}.cs`
· `puente-omi/{puente.py,DESACTIVADO.md}` · `.github/workflows/contrato.yml:99-101` ·
`out/evidencia.md` (2026-09-01) · `%LOCALAPPDATA%\U\logs\` (último: `u-20260814.log`).
