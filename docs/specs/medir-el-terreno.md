# Medir el terreno: grafo + vídeo contrastados

**Qué se quiere.** Instalar Ü en los equipos de médicos de urgencias, ver el grafo que construyen,
y poder mirar el vídeo del momento exacto en que el grafo se rompió — moviendo la línea de tiempo y
viendo el grafo construirse y deconstruirse con ella.

**Para qué.** Encontrar TODOS los puntos donde el terreno se detecta mal. El grafo es el terreno; lo
que no se detecta bien hoy es lo que hay que arreglar.

---

## Lo primero: esto es más simple de lo que suena

Cuatro de las siete piezas ya están construidas, y la que parecía más difícil —correlacionar el
vídeo con el grafo al segundo exacto— es la más fácil de todas.

| Pieza | Estado | Dónde |
|---|---|---|
| Grabar pantalla a mp4 | **Ya está** | `Teach/ScreenRecorder.cs` (ScreenRecorderLib, H.264+AAC) |
| Un embudo único por donde pasa todo lo que ocurre | **Ya está** | `LogBus.Anotado` |
| Subir cada línea al backend, en vivo | **Ya está** | `Telemetry/EspejoDelLog.cs` → `POST /api/v1/agent/events` |
| Panel del backend con grafo por usuario | **Ya está** | Provider Studio |
| Detectar los errores del terreno | **Ya está, disperso** | ver abajo |
| Trocear por horas + recortar clips | Falta | — |
| Línea de tiempo que reconstruye el grafo | Falta | — |

### Los errores del terreno YA se detectan

Esto fue la sorpresa al revisar el código. No hay que inventar qué es un error: el sistema ya los
canta, solo que como texto suelto en el log en vez de como hechos que se puedan contar.

| Error | Qué significa | Dónde se detecta hoy |
|---|---|---|
| `SIN atribuir` | Se cambió de pantalla y NINGÚN clic explica el viaje. **La arista no se creó.** | `MapaVivo.cs:419` |
| `pasó de largo` | Se cruzó una pantalla tan rápido que el clic guardado ya no explica el viaje | `SurfaceMap.cs:284` |
| `identidad transitoria` | Una web cambió de URL sola: el nodo puede ser falso | `SurfaceMap.cs:310` |
| `SATURADO` | Se descartaron lecturas de pantalla por ir demasiado lento: **hay terreno que no se vio** | `MapaVivo.cs:69` |
| `NO SE ACTÚA` | Se creía estar en una pantalla y era otra | `SurfaceMapTools` (ancla) |
| `no veo nada que se llame X` | Se pidió pulsar algo que no se encontró: **elemento no reconocido** | `SurfaceMapTools` |
| `no lo tengo en el mapa` | Se señaló algo que el grafo no conoce | `LoQueSenala` |

Lo único que falta es **recogerlos**: un catálogo con nombre, gravedad y marca de tiempo, en vez de
siete cadenas de texto que hay que buscar con grep. Eso es una tarde de trabajo, no un proyecto.

---

## La correlación vídeo ↔ grafo es una resta

Es la parte que sonaba difícil y es la más fácil, y conviene decirlo claro para no
sobrediseñarla:

```
segundo_del_video = instante_del_hecho − instante_en_que_empezó_ese_trozo
```

No hace falta sincronizar relojes, ni incrustar marcas en el vídeo, ni nada parecido. Basta con que
el trozo de vídeo sepa a qué hora empezó y que cada hecho lleve su hora. Las dos cosas ya existen.

Y como `LogBus.Anotado` es el embudo por el que pasa **todo**, la correlación se engancha en UN
sitio, no en cada punto que informa. Es la misma razón por la que el espejo del log se enganchó ahí:
lo que está en el archivo del equipo es exactamente lo que llega al panel, sin dos listas que se
separan en cuanto alguien añade una línea y olvida la otra.

---

## La decisión de diseño que lo vuelve simple: segmentos, no recortes

**El problema.** Recortar «20 s antes y 20 s después» de un mp4 pide ffmpeg. Y este proyecto
rechazó bundlear ffmpeg a propósito — está escrito en `ScreenRecorder.cs:9`: son ~50 MB más en el
instalador y una forma nueva de que la app no arranque en una máquina limpia. Ese criterio sigue
siendo bueno y no conviene romperlo por esto.

**La salida.** No grabar una hora y cortarla: grabar en **segmentos cortos (20 s)** desde el
principio. Entonces un clip deja de ser un recorte y pasa a ser *«los segmentos que tocan esta
ventana»* — que se copian tal cual, sin recodificar y sin ninguna herramienta externa.

```
segmentos:   [ 12 ][ 13 ][ 14 ][ 15 ][ 16 ][ 17 ][ 18 ]
error en el 15  ───────────────▲
clip = 14,15,16                └── 20 s antes, 20 s después, sin cortar nada
```

Y agrupar errores cercanos sale gratis: si hay errores en el 15 y en el 17, el clip es `14…18`. Es
exactamente la regla pedida —del primero menos 20 s al último más 20 s— pero resuelta con
aritmética de enteros en vez de con un decodificador de vídeo.

El precio es que un clip trae hasta 20 s de más en cada extremo. Para revisar un fallo eso no
estorba; ver un poco de contexto de más suele ayudar.

**Los segmentos sin error se borran.** Es lo que hace que 24 h de grabación no llenen el disco.

---

## Los números, que es donde está el riesgo de verdad

Con los ajustes actuales del grabador —8 Mbps, 30 fps, pensados para grabar a alguien enseñando un
flujo durante dos minutos— esto **no es viable**:

| Ajuste | Por hora | Por día (24 h) |
|---|---|---|
| Hoy: 8 Mbps / 30 fps | 3,6 GB | **86 GB** ❌ |
| Propuesto: 1 Mbps / 5 fps | 450 MB | 10,8 GB |
| Mínimo legible: 0,5 Mbps / 4 fps | 225 MB | **5,4 GB** |

Grabar una interfaz no es grabar vídeo: la pantalla está quieta la mayor parte del tiempo y H.264
comprime eso muy bien. A 4–5 fps se ve perfectamente qué pasó y en qué orden, que es para lo que se
graba.

Con los segmentos limpios borrados sobre la marcha, lo que queda en disco en cualquier momento son
los últimos minutos más los clips pendientes de subir: del orden de **cientos de MB**, no decenas de
GB. Lo que sube a la nube son solo los clips con error.

---

## Lo que hay que decidir antes de escribir código

**1. Datos de pacientes en la grabación.** Son médicos de urgencias: en esas pantallas hay historias
clínicas, nombres, diagnósticos. Grabar la pantalla 24 h y subirla a la nube significa que esos
datos salen del hospital. Esto no es una objeción técnica y no lo decide un programador — pero hay
que tenerlo resuelto antes de instalar en el primer equipo, no después. Como mínimo hace falta saber
qué dice el hospital, qué firman los médicos, y si la nube donde aterriza cumple lo que tenga que
cumplir. Si la respuesta es que no puede salir, hay una alternativa razonable: dejar los clips en el
equipo y subir solo los hechos del grafo, revisando el vídeo en sitio.

**2. Que se note que está grabando.** El modo se enciende a propósito desde el panel, pero durante
24 h alguien se olvida. Un indicador permanente y un atajo para parar no son cortesía: son lo que
hace la diferencia entre una herramienta de pruebas y una cámara oculta.

**3. Cuánto vídeo de verdad se quiere.** Si con los hechos del grafo bastara para la mayoría de los
errores, el vídeo podría reservarse para los que no se entienden leyendo. Se puede empezar por los
hechos —que ya casi están— y añadir vídeo cuando se vea qué es lo que no se entiende sin él.

---

## Plan por fases

Cada fase deja algo que funciona y se puede probar. No hace falta terminar la última para que la
primera sirva.

**Fase 1 — El catálogo de errores del terreno.**
Recoger los siete errores que ya se detectan en un tipo con nombre, gravedad e instante, y emitirlos
por `TelemetryBus` además de por el log. *Al terminar esto ya se puede contar cuántas veces falla
cada cosa y en qué apps, sin una sola línea de vídeo.* Es la fase que más información da por menos
trabajo.

**Fase 2 — Grabación en segmentos.**
Rotación cada 20 s, ajustes de bitrate/fps bajos, borrado de lo limpio, indicador visible. El
segmento anota a qué hora empezó.

**Fase 3 — Clips.**
Ventana de ±20 s en segmentos, agrupación de errores cercanos, subida del clip con la lista de
errores que contiene.

**Fase 4 — El modo feedback.**
Botón en el panel (doble Ctrl+Shift): oculta la carita y todo lo demás, deja la grabación y el
indicador. Es pegamento, no capacidad nueva.

**Fase 5 — La línea de tiempo.**
El visor donde se mueve el vídeo y el grafo se construye y deconstruye. Es la fase más vistosa y la
más cara, y es la única que no aporta nada hasta estar terminada — por eso va al final.

---

## Respuesta corta

**Las fases 1–3 son la parte que importa y son abarcables**: la detección ya existe, la subida ya
existe, la correlación es una resta, y grabar por segmentos evita la única dependencia fea. La
fase 5 es un proyecto aparte de interfaz.

Lo que de verdad puede hundir esto no es el código: es el disco (resuelto bajando bitrate y
borrando lo limpio) y el permiso para sacar pantallas de urgencias a la nube (no resuelto, y no lo
resuelve el código).
