# Plan de implementación: Luna ve la pantalla

Estado: **fase 1 implementada y CORREGIDA** · 2026-09-16 · Ramas: `jose/luna-ve-la-pantalla`,
`jose/la-mirada-dura-la-conversacion`

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

## Lo que la fase 1 NO arregló, y se descubrió usándola (2026-09-16, por la tarde)

La fase 1 se dio por buena con una evidencia de nivel 4 que **probó la capacidad y no el camino**: una
sonda mandaba la imagen y preguntaba en el acto. La app no hace eso. El dueño la probó de verdad y
Luna no dijo nunca lo que veía. El log dice por qué, con las horas:

```
20:26:54  el modelo pide map_look
20:26:55  mirada subida: 42254 bytes → file-XPZzgfU94qCkKL9Ntm8Pdu
20:27:03  mirada borrada de OpenAI          ← ocho segundos después
20:27:04  el servidor dice: Files [file-XPZ…] were not found
20:27:10  el servidor dice: Files [file-XPZ…] were not found
20:27:28  el servidor dice: Files [file-XPZ…] were not found
```

**La copia se borra antes de que el modelo la lea, y el orden del código lo garantiza.**
`MandarFotoAsync` manda la referencia, duerme ocho segundos y suelta; sólo *después* se devuelve el
resultado de la herramienta y se pide el turno. Cuando Luna va a descargar el archivo, ya no está. No
es una carrera que a veces se pierde: por este camino se pierde siempre.

Y el daño no acaba ahí: **la referencia muerta se queda en el historial de la sesión**, así que cada
respuesta posterior vuelve a tropezar con ella —se ve en las tres líneas de arriba, a las 20:27:04,
20:27:10 y 20:27:28—. Una mirada fallida no deja a Ü como estaba: deja la conversación envenenada.

### La promesa 250 se corrige, y aquí queda escrito por qué

La 250 decía «se sube, se mira y se borra, y el borrado ocurre también cuando la mirada falla». Ese
enunciado **lo cumple igual de bien un borrado después de mirar que uno antes**, que es exactamente
el aprendizaje nº2 del repo —un enunciado que no distingue sus casos— incumplido en el acto de
escribirlo. Estaba verde mientras el producto era ciego.

El arreglo no es subir el temporizador. **Con la referencia viva en el historial, ningún temporizador
funciona**: siempre habrá una respuesta posterior que la relea. La copia tiene que durar lo que dure
la conversación que puede leerla, y retirarse al cerrarla. Eso sigue cumpliendo la condición del
dueño —«QUE NO DUREN MUCHO TIEMPO EN OPENAI»: duran una llamada, no días— y de paso quita los ocho
segundos de bloqueo, que hacían que mirar costara ocho segundos de nada.

## La especificación

| # | Promesa | Contrato | Fase |
|---|---|---|---|
| 54 | GPT-Live SÍ mira, y su foto viaja por REFERENCIA: un `input_image` con el identificador del archivo y sin un solo byte de imagen dentro, que pesa menos de 300 bytes frente a los 118.000 de la forma incrustada; la forma incrustada sigue existiendo para Realtime, que es lo que su servidor acepta | voz | 1 |
| 250 | mirar deja la copia en OpenAI el tiempo justo Y NO MENOS: mientras la conversación que puede leerla siga viva la copia NO se borra, al cerrarla se retiran todas las que subió, soltar dos veces no borra dos veces, y sin nada subido no se borra nada | grafo | 1, corregida en 2 |
| 254 | mandar la foto cede el turno de inmediato y con la copia en pie: no se duerme esperando a que el servidor la descargue, y cuando el turno vuelve al modelo no se ha borrado nada — borrar antes de que lea deja la sesión apuntando a un archivo que ya no existe | grafo | 2 |
| 255 | el álbum de miradas vive en local con su ficha —cuándo, en qué ubicación y qué estaba pasando—, se guarda en JPEG, se puede pedir la última foto de una ubicación, y se poda por edad y por tamaño: siete días o dos gigas, lo más viejo primero | grafo | 3 |

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
(250, y ahora también que NO se borre mientras la sesión vive); que mandar la foto no duerme y no
borra, sobre la conversación de verdad con su salida falsa (254); y el álbum sobre una carpeta
temporal con un reloj inyectado, comprobando la ficha, la búsqueda por ubicación y las dos podas
(255 — nació como 251 y se renumeró: otra sesión mergeó antes y se llevó el 251, 252 y 253).

Sobre el servidor real, y esta vez **por el camino que usa la app, no por una sonda**: pedirle a Ü que
mire en una conversación viva, y que diga lo que hay en la pantalla. La evidencia de la fase 1 —tres
imágenes por URL y una por identificador— sigue siendo cierta y sigue sin bastar: probó que el
servidor acepta la referencia, no que nuestro código se la deje leer.

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

### Fase 2 — que la mirada dure lo que la conversación (250 corregida, 254)

`MiradaSubida` guarda TODAS las copias de la sesión y las suelta juntas al cerrarla;
`ConversacionEnVivo` tiene una sola mirada por sesión, manda la referencia y cede el turno sin dormir,
y suelta al terminar.

### Fase 3 — la memoria local (255)

`Navigation.AlbumDeMiradas` con su ficha y su poda, y las fotos de los recuerdos pasando a JPEG.

## Lo que NO entra

- Capturar constantemente: medido hoy, una captura cuesta 34 ms y tener la foto lista de antemano
  ahorra un 5% del viaje; a cambio pone una captura por segundo compitiendo con un lector de pantalla
  que ya está saturado. Se guarda una por ubicación, que es lo que no se puede capturar después.
