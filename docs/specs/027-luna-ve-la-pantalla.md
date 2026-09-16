# Plan de implementación: Luna ve la pantalla

Estado: **propuesto** · 2026-09-16 · Rama: `jose/luna-ve-la-pantalla`

> El dueño: «me parece muy importante que el modelo tenga acceso a mirar la pantalla… no quiero que
> Luna tenga una limitación de cuántas imágenes puede ver, ni de tener que ver imágenes comprimidas de
> baja calidad». Y sobre dónde vive lo mirado: «que queden alojadas en local, en una memoria del
> asistente, para que pueda recordar en cualquier momento acciones pasadas». Y: «QUE NO DUREN MUCHO
> TIEMPO EN OPENAI».

## Diagnóstico: la limitación era nuestra

Hasta hoy, Ü no podía enseñarle la pantalla a Luna. El motivo escrito en el código era que no cabía:
la sesión admite «128 items and 32768 UTF-8 bytes», y una captura pesa 118.000 bytes ya codificada. De
ahí salió `Mira => false` y la promesa 49, que dice que GPT-Live «no se declara capaz de mirar».

**Esa premisa es falsa, y se midió contra el servidor real el 2026-09-16.** Lo que no cabe es la
imagen METIDA DENTRO del mensaje. El campo del protocolo se llama `image_url`, y nosotros le
poníamos la imagen entera en base64. Probando qué acepta de verdad:

| Qué se mandó | Qué contestó Luna |
|---|---|
| `image_url` con una URL https | «Un cachorro negro está recostado sobre un suelo de madera» |
| Otra, en la misma sesión | «Un pug envuelto en una manta a cuadros sentado en un bosque» |
| Y otra más, sin vaciar nada | «Un paisaje montañoso con un río, árboles altos y acantilados» |
| `file_id` de la API de archivos de OpenAI | «Veo un cachorro negro sentado sobre un suelo de madera» |

Las tres descripciones son correctas, y las tres imágenes viajaron a tamaño completo. El servidor se
las descarga él. En el buzón de la sesión entran unos treinta bytes en vez de ciento dieciocho mil, y
por eso no hay tope: se puede mirar tantas veces como haga falta.

De los dos caminos se elige el `file_id`, y por una razón de producto: la captura va a OpenAI, que es
donde ya iba, y **no hace falta publicarla en ninguna dirección accesible desde internet** ni montar
nada nuevo. Se sube, se mira, y se borra.

## Por qué va dirigido por especificación

Porque cambia lo que el sistema promete —hoy promete que NO mira— y porque la forma en que viaja la
imagen es justo lo que se revierte sin querer: basta que alguien vuelva a llamar a `Fotograma(bytes)`
para que todo vuelva a no caber, y el síntoma sería «Ü dejó de ver» sin ninguna pista.

## La especificación

| # | Promesa | Contrato | Fase |
|---|---|---|---|
| 54 | GPT-Live SÍ mira, y su foto viaja por REFERENCIA: un `input_image` con el identificador del archivo y sin un solo byte de imagen dentro, que pesa menos de 300 bytes frente a los 118.000 de la forma incrustada; la forma incrustada sigue existiendo para Realtime, que es lo que su servidor acepta | voz | 1 |
| 250 | mirar deja la copia en OpenAI el tiempo justo: se sube, se mira y se borra, y el borrado ocurre también cuando la mirada falla; soltar dos veces no borra dos veces, y sin nada subido no se borra nada | grafo | 1 |
| 251 | el álbum de miradas vive en local con su ficha —cuándo, en qué ubicación y qué estaba pasando—, se guarda en JPEG, se puede pedir la última foto de una ubicación, y se poda por edad y por tamaño: siete días o dos gigas, lo más viejo primero | grafo | 2 |

### La promesa 49 se corrige, y aquí queda escrito por qué

La 49 dice hoy que GPT-Live «no se declara capaz de mirar, porque una captura de pantalla no cabe y
una segunda foto pequeña tampoco». La medida que la respaldaba era correcta y su conclusión era
demasiado ancha: lo que no cabe es la imagen incrustada. Se le quita esa cláusula y se queda con lo
demás, que sigue siendo verdad —el recorte de un resultado de más de 32.768 bytes y la confirmación
de la apertura—. Es el aprendizaje nº6 del repo aplicado a nosotros mismos: cuando una limitación
documentada resulta falsa, se borra la maquinaria de compensación en vez de parchearla.

### Con qué se juzga

Sin red: la forma del mensaje con referencia y su peso, y que no queda ni un byte de imagen dentro
(54); la secuencia subir–mirar–borrar con un almacén falso, incluido el caso en que la mirada falla
(250); y el álbum sobre una carpeta temporal con un reloj inyectado, comprobando la ficha, la
búsqueda por ubicación y las dos podas (251).

Sobre el servidor real: ya está hecho y está arriba en la tabla. Las tres imágenes por URL y la
cuarta por identificador, descritas correctamente, y el borrado confirmado por la API.

### Límites dichos, no escondidos

- La captura sale de la máquina hacia OpenAI, igual que hoy sale el audio. Lo que cambia es que ahora
  llega entera en vez de no llegar.
- El borrado depende de que la API conteste; si falla, queda anotado en el log y el archivo caduca
  igualmente por la política de la cuenta.
- Esto no acelera nada: quita una ceguera.

## Las fases

### Fase 1 — que vea (54, 250)

`IProtocolo.FotogramaPorReferencia`, `ProtocoloGptLive` mirando y por referencia,
`Voice.MiradaSubida` (subir, dar el id, soltar) y el cableado en `ConversacionEnVivo`.

### Fase 2 — la memoria local (251)

`Navigation.AlbumDeMiradas` con su ficha y su poda, y las fotos de los recuerdos pasando a JPEG.

## Lo que NO entra

- Capturar constantemente: medido hoy, una captura cuesta 34 ms y tener la foto lista de antemano
  ahorra un 5% del viaje; a cambio pone una captura por segundo compitiendo con un lector de pantalla
  que ya está saturado. Se guarda una por ubicación, que es lo que no se puede capturar después.
