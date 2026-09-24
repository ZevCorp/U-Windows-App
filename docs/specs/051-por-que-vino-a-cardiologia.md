# «¿Por qué vino a cardiología?», al soltar la historia clínica en la Nota

Estado: **en curso** · Nace de la petición del dueño del 2026-09-24 · Rama: `jose/documentos-en-la-nota`,
construida ENCIMA de `claude/u-cardio-upload-panel-8jza7i` (spec 046 de esa rama, promesas 350-357)

> Spec 051 y promesas 420-439, reservadas el 2026-09-24 con la sesión que trabaja el collar en paralelo
> (049/410 y 050/411-419 son suyas). Los números no se reciclan.

## Qué se pidió, en sus palabras y en orden de importancia

1. **Lo principal:** «cuando le subo los documentos, que responda: ¿por qué vino a cardiología? La
   historia clínica puede contener muchas cosas; lo que hará principalmente, al solo enviar los
   archivos, es ayudarle al médico a entender, de todo lo que hay, por qué vino a cardiología».
2. Se sueltan en la ventana de **Nota** (la de «Pulsa grabar»), también durante la grabación. Son
   fotos tomadas a documentos, y PDFs.
3. **Extra, fase 2:** un botón de recargar bajo el titular que lee la transcripción en vivo, entiende
   qué pidió el médico de los documentos y lo contesta citado: una línea corta y los párrafos enteros.

## Lo que ya había y lo que no servía tal cual

La rama de la spec 046 ya lee fotos de estudios con OpenAI (Responses API, `store:false`, lotes de 3)
y **tira en código todo lo que no es cardiológico** (promesa 351). Para el resumen de estudios eso es
lo correcto. Para esta pregunta es justo al revés: **el motivo de consulta casi nunca está en un
estudio cardiológico** — está en la remisión de medicina general, en la nota de urgencias, en la
interconsulta. Por eso aquí se lee TODA la historia, y lo de la 351 sigue intacto para su panel.

## Cómo se garantiza que no se invente nada

El modelo no escribe las citas: **las elige por id**. Cada documento se transcribe literal, en
párrafos con id estable (`D2-p1-3`: documento 2, página 1, párrafo 3). Para el motivo, el modelo
recibe esos párrafos etiquetados y devuelve una frase y los ids que la sostienen. El párrafo que se
enseña **lo copia el código** de la transcripción por su id. Un id que no existe se descarta, y sin
ninguna cita válida no se enseña la frase del modelo: se dice que los documentos no lo dicen.

## Las promesas

| # | Promesa | Fase |
|---|---|---|
| 420 | lo soltado se reparte por su tipo: una foto (jpg, jpeg, png, bmp, gif, tif, tiff, webp, heic, heif) se lee como imagen, un PDF como archivo, y cualquier otra cosa se nombra como no admitida —con su nombre y qué hacer— sin tumbar a las demás; la extensión se mira sin distinguir mayúsculas | 1 |
| 421 | leer la historia la TRANSCRIBE literal, por páginas y párrafos: cada documento viaja precedido de «Documento n — id: Dn», la foto como imagen con detalle alto y el PDF como archivo con su nombre, todo con store:false; las fotos van de 3 en 3 y cada PDF solo; lo que vuelve se convierte en párrafos con id estable «Dn-pP-k»; y un documento del que no volvió nada queda sin leer, diciendo por qué | 1 |
| 422 | «¿por qué vino a cardiología?» se pregunta con los párrafos de TODA la historia —de cardiología o no— y sin ninguna imagen ni archivo; la respuesta es UNA frase de como mucho 220 caracteres, y los párrafos que la sostienen se copian en código de la transcripción por su id: un id que no existe se descarta, y sin ninguna cita válida el titular es «Los documentos no dicen por qué vino a cardiología», aunque el modelo haya escrito una frase | 1 |
| 423 | soltar más documentos no vuelve a leer los ya leídos y rehace el motivo con todos; si la lectura falla a mitad, lo leído se conserva, el error nombra el documento que faltó, y reintentar lee solo lo que faltó | 1 |

La que cierra el asunto es la **422**. Mientras no exista, el titular sería una frase del modelo sin
nada debajo que la sostenga, y en una historia clínica eso es peor que no tener titular.

### Con qué se juzga

Todas con el modelo de mentira del contrato (`Func<string, CancellationToken, Task<string>>`), sin red,
sin clave y sin pantalla, igual que las 350-357. La interfaz —soltar en la ventana, pintar el titular—
se prueba a mano sobre `U.exe`.

## Las fases

| Fase | Qué | Promesas |
|---|---|---|
| 0 | Las promesas en `Contrato.cs`, en rojo | ninguna (el rojo es el entregable) |
| 1 | `Cardio/HistoriaClinica.cs` (modelo + lectura pura) y `Cardio/LectorDeLaHistoria.cs` | 420-423 |
| 1b | `Ui/PanelDelMotivo.cs` + soltar en `ConsultaWindow` (solo la zona de la Nota) | a mano |
| 2 | Recargar desde la transcripción: la pregunta del médico, la línea corta y los párrafos enteros | 424+ |

## Decisiones

- **Vive en memoria, con la consulta.** No se escribe a disco: son datos de un paciente y solo sirven
  mientras se le atiende. Cerrar la ventana los borra. (El panel de la spec 046 sí guarda, cifrado,
  24 h; son dos usos distintos.)
- **Mismo camino hacia OpenAI que la spec 046**: la clave que entrega Graph (spec 045), las
  instrucciones dentro del `.exe`. Llevarlas a Graph es el corte que la 046 ya dejó escrito.
- **PDF sin librerías nuevas**: viaja como `input_file` de la Responses API y lo lee el modelo. Tope de
  20 MB por PDF; uno más grande se nombra y se pide partirlo.

## Lo que queda fuera

- La retención de OpenAI (30 días para abuso salvo retención cero): la misma nota que la spec 046.
- Word, Excel y demás: se nombran como no admitidos y se pide exportarlos a PDF.

## Hallazgos

## Cierre

- [ ] 420-423 verdes, contrato intacto, sabotaje comprobado
- [ ] A mano: soltar una historia real (fotos + PDF) en la Nota, en ≥2 casos, con el titular citado
