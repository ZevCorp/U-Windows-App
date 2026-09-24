# Fotos de estudios de cardiología → resumen y preguntas, desde el óvalo

Estado: **implementada, pendiente de corrida a mano en Windows** · Nace de la petición del 2026-09-23 («botón "Subir" en el óvalo de Ü») ·
Rama: `claude/u-cardio-upload-panel-8jza7i`

## Qué se pidió, en una línea

Una píldora **Subir** en el óvalo que abre el explorador de Windows. El médico carga muchas fotos a la
vez (explorador, arrastrar o Ctrl+V): ECG, ecos, laboratorios, epicrisis… Ü separa lo cardiológico de
lo que no lo es, resume lo primero y contesta preguntas **solo** sobre esas fotos. La sesión se borra
sola a las 24 h.

## Lo que se midió antes de escribir

| Qué | Medida | Fuente |
|---|---|---|
| Stack del cliente | WPF sobre .NET 8, sin WebView2 | `windows-client/WindowsClient.csproj` |
| El óvalo | `BarPanel` de `FaceWindow.xaml`, que se muda a la ventana del muelle (150 DIP de ancho) | `FaceWindow.xaml`, `MudarElPanelAlMuelle` |
| Estilo de `Learn` / `Work` | `Estudio.Pastilla(alto/2)` + `ConRelieve()`, fondo `Estudio.Superficie` | `VestirElPanelConElEstudio` |
| Proveedor de IA | **OpenAI**. La clave `OPENAI_API_KEY` no va en el `.exe`: el cliente se la pide a Graph y la guarda en memoria | `ClavesDelBackend` (promesa 300) |
| Imágenes hacia el modelo | ya van **directas del cliente a OpenAI** con esa clave | `MiradaSubida` (promesa 250) |
| Modelo de texto que usa Ü | `gpt-5.6-luna` (el delegado de GPT-Live) | `voz/Realtime/ProtocoloGptLive.cs` |

## Decisiones que se apartan de la petición, y por qué

1. **No es web, es WPF.** La petición habla de IndexedDB, canvas y `clipboardData`: está escrita
   para un cliente web. Aquí se traduce cada pieza a su equivalente nativo, sin dependencias nuevas:
   IndexedDB → carpeta `%LOCALAPPDATA%\U\cardio\` **cifrada con DPAPI** (`Cuenta/Dpapi.cs`, lo mismo
   que protege el token de la cuenta) · canvas → WIC (`TransformedBitmap` + `JpegBitmapEncoder`) ·
   `clipboardData.files` / `items image/*` → `Clipboard.GetFileDropList` / `PNG` / `GetImage`.
   Meter WebView2 solo para esto sería añadir un runtime de 150 MB al instalador.
2. **Sin endpoint nuevo en Graph.** Graph vive en otro repo. El patrón que ya usa Ü para mandar
   imágenes a OpenAI es el directo, con la clave que Graph entrega (spec 045). Se sigue ese: no hay
   clave nueva en el cliente ni ningún servidor nuestro en medio que pudiera guardar o registrar
   nada. Se pide `store:false` para que OpenAI no conserve la respuesta. **El coste**: los prompts
   viajan dentro del `.exe`, igual que ya viajan las instrucciones de la voz. Llevarlos a un
   `POST /api/v1/u-cardio` de Graph es el corte siguiente, y está escrito abajo.
3. **El selector se abre en el mismo clic.** En WPF no hay «activación de usuario» que perder:
   `OpenFileDialog` se abre en el propio clic de la píldora, con el panel ya a la vista detrás.

## Las promesas

> Nacieron como 346-353 y se movieron a 350-357 el mismo día: `main` ya usaba 346-349 en este
> contrato (el archivo no está en orden y solo se miró el final). Los números no se reciclan.

| # | Promesa |
|---|---|
| 350 | la respuesta del modelo da UN resultado por foto: el JSON se encuentra aunque venga envuelto en ``` o con texto alrededor, se empareja por id y, si el id no casa, por orden; y una foto de la que no volvió nada queda «sin leer», ni cardiológica ni omitida |
| 351 | de una foto no cardiológica solo se conserva el motivo: aunque el modelo devuelva tipo, hallazgos o valores, se descartan en código y no llegan ni al resumen ni a las preguntas |
| 352 | las fotos viajan al modelo en lotes de 3, cada imagen precedida de «Imagen n — id: X» y con store:false; el resumen y las preguntas no llevan ninguna imagen, y una pregunta lleva solo las extracciones cardiológicas, el resumen, las últimas 10 vueltas y la pregunta |
| 353 | una foto se prepara a lado mayor 1600 px sin agrandar nunca, en JPEG sobre fondo blanco; y una que no se puede leer se nombra («No se pudo leer X. Expórtala como JPG») sin tumbar a las demás |
| 354 | la sesión caduca 24 h después de la PRIMERA foto, no de la última: al cargarla caducada se borra entera —fotos, lecturas, resumen y chat— y no queda nada en disco; y lo guardado no se lee como texto claro |
| 355 | el resumen se pinta sin ejecutar marcado: títulos, viñetas y negritas se reconocen y todo lo demás es texto literal |
| 356 | el botón dice lo que va a hacer —«Generar resumen (N)», «Actualizar resumen» cuando las fotos cambiaron— y el progreso cuenta sobre el plan: «Leyendo fotos 4–6 de 12…» |
| 357 | si el modelo falla a mitad, lo leído se conserva, el error se dice, y reintentar lee SOLO lo que faltó |

La que cierra el asunto es la **351**: mientras no exista, «omitida» es una etiqueta de la interfaz y
el contenido de una selfie o de una radiografía de rodilla podría acabar en el resumen o en una
respuesta del chat. Es la única que protege al paciente y no a la experiencia.

### Con qué se juzga cada una

Todas con un modelo de mentira inyectado (`Func<string, CancellationToken, Task<string>>`) que anota
los cuerpos que recibe: sin red, sin clave y sin pantalla. La 353 construye la imagen en memoria
(PNG transparente de 3200×800) y la 354 inyecta el reloj y la carpeta.

## Las fases

| Fase | Qué | Promesas en verde |
|---|---|---|
| 0 | Las promesas en `Contrato.cs`, en rojo | ninguna (el rojo es el entregable) |
| 1 | `Cardio/LecturaCardio.cs` + `Cardio/ClienteCardio.cs`: prompts, lotes, cuerpos, emparejado, descarte | 350, 351, 352, 357 |
| 2 | `Cardio/PreparadorDeFotos.cs` + `Cardio/AlmacenCardio.cs` | 353, 354 |
| 3 | `Cardio/ReglaCardio.cs` (botón, progreso, markdown) + `Ui/EstudiosWindow.cs` + píldora en `FaceWindow.xaml` | 355, 356 |

## Lo que queda fuera, y se dice

- **El endpoint en Graph** (`POST /api/v1/u-cardio` con `accion`). Sacaría los prompts del `.exe`.
  Es trabajo del repo Graph y lo abre quien lo tenga.
- **La retención de OpenAI.** `store:false` evita que la respuesta quede guardada para recuperarla,
  pero OpenAI conserva lo enviado hasta 30 días para vigilar abusos, salvo que la cuenta tenga
  retención cero. Eso se decide en la cuenta de OpenAI, no en este código.
- **HEIC.** WIC solo lo lee si el equipo tiene instalada la extensión HEIF. Si no la tiene, la foto
  se nombra y se pide exportarla como JPG. No se añade un decodificador.
