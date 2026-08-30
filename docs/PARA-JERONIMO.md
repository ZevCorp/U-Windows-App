# Para Jerónimo — qué estamos haciendo y por qué

Hola Jerónimo. Esto es el traspaso de lo que llevamos en la rama `jose/batch-resolver-estable`.
Está escrito para leerse de corrido, sin abrir código. Al final te digo qué está probado, qué
**no** está probado todavía, y dónde entra tu trabajo.

Si después quieres meterle mano con tu agente, hay un segundo archivo pensado para eso:
[`PARA-EL-AGENTE-DE-JERONIMO.md`](PARA-EL-AGENTE-DE-JERONIMO.md).

---

## 1. El problema, en una frase

Un agente de IA que maneja una aplicación trabaja así: **mira la pantalla → decide → hace un clic
→ vuelve a mirar → decide → otro clic…** Cada «mirar y decidir» es un viaje al modelo: segundos de
espera y tokens gastados. Una tarea de veinte clics son veinte viajes. Es lento y es caro, y por
eso la automatización con visión nunca termina de sentirse usable.

La solución conocida es el **batch**: mandar varias acciones en una sola llamada. Pero el batch
solo funciona si el modelo puede **predecir** lo que va a pasar. En Chrome esto ya funciona bien
porque el DOM le entrega el mapa completo de la página: sabe qué elementos hay y a dónde llevan,
así que puede encadenar cinco acciones con confianza.

**En Windows nativo y en SAP no existe ese mapa.** No hay DOM. El agente está a ciegas después del
primer clic, así que no puede batchear nada.

## 2. Nuestra apuesta

**Nosotros fabricamos el mapa que Windows no da, y el Agent SDK lo navega.**

Ese mapa —lo llamamos *el terreno*— es un grafo que se construye solo, usándose:

- **Un nodo es una pantalla.** No una app: una pantalla concreta. En SAP eso es
  sistema/transacción/programa/dynpro, y hasta la subpantalla — `QAS/NWP1/SAPLN_WP_FRAMEWORK/0100`.
- **Cada pantalla guarda sus elementos con una bandera: ¿está vivo?** O sea: ¿esto está en
  pantalla *ahora mismo*, o solo lo recuerdo de otra vez? Nunca se mezclan las dos cosas.
- **Una arista se gana ejecutando.** «Pulsé este elemento desde esta pantalla y acabé en esta
  otra». No se deduce mirando ni se adivina de los clics del usuario — eso lo intentamos antes y
  producía caminos falsos. Solo se apunta lo que hicimos nosotros y comprobamos.

Encima de ese terreno corre **`map_batch`**: el modelo pide una lista de pasos, y **nuestro código
—no el modelo— verifica antes de cada paso que lo que se pide está vivo**. Si lo está, lo pulsa
por identidad (nunca por coordenadas) y aprende la arista. Si no, **para y contesta honesto**:

> «Hice 2 de 4, paré en el paso 3 porque "Guardar" no está vivo aquí. Estás en tal pantalla. Vivo
> aquí: A, B, C.»

Con esa respuesta el modelo replanifica **sin gastar otra llamada** para mirar. Es el equivalente
del screenshot intercalado, pero en texto y con la verdad del terreno.

## 3. Qué resultados nos ha dado (con números)

**La métrica del proyecto es una sola: viajes al modelo por tarea.** Todo lo que hacemos tiene que
bajar ese número.

La primera medición seria fue en Wikipedia, con el Agent SDK conectado a nuestro MCP. La misma
tarea, tres veces, mejorando el terreno entre una y otra:

| iteración | viajes al modelo | tiempo | resultado |
|---|---|---|---|
| terreno crudo | 28 | 7.328 s | **fracasó** |
| + estabilidad del resolvedor | 26 | 71,8 s | éxito |
| + web direccionable | **8** | **19,1 s** | éxito |

De fracasar en dos horas a resolverlo en 19 segundos con 8 viajes. Ese salto es lo que nos hace
creer que la idea va por buen camino: **no cambiamos el modelo ni el prompt — mejoramos el
terreno**.

Después lo llevamos a SAP (sesión real de QAS, el sistema de calidad del hospital):

- SAP no se deja ver por las APIs normales de Windows: dentro de su ventana el sistema operativo
  solo ve un rectángulo opaco. Usamos su propia API de scripting y **el terreno empezó a verlo
  todo**: campos, botones, el campo de comandos.
- Al principio solo aparecían 12 puertas, y todas de la barra de herramientas. Investigando
  descubrimos que **todo el contenido de esa pantalla son dos árboles**, y un árbol no se pulsa:
  se pulsa una de sus filas. Al meter las filas visibles como puertas pasamos de **12 a 35**, con
  el menú clínico real: Órdenes Clínicas, Admisiones, Censo Pacientes, Interconsultas…
- Y cerramos la cadena entera: pedimos una fila, el sistema avisó honestamente de que había **dos
  puertas con ese mismo nombre** y dio los identificadores exactos, elegimos una, **la cruzó**, la
  pantalla cambió de verdad, y **la arista quedó aprendida** (lo verificamos en la base del grafo).

## 4. Cómo trabajamos (te va a chocar al principio)

Hay una disciplina que sostiene todo esto, y es la razón de que el contrato tenga hoy **72
promesas y todas en verde**:

1. **La promesa se escribe antes que el código**, y se comprueba que está **roja por el motivo que
   dice**.
2. Se implementa hasta ponerla verde.
3. **Se sabotea la implementación a propósito** y se verifica que la promesa vuelve a rojo. Si no
   muerde, la promesa no vale nada y hay que reescribirla.
4. Se restaura y se prueba **contra la aplicación real**, uno mismo. Nunca «pruébalo tú y me
   dices».

Suena lento y es al revés: es lo que permite tocar el núcleo sin miedo. El `pre-push` corre el
contrato entero y no deja subir nada rojo.

## 5. Lo que TODAVÍA NO está validado (importante)

Quiero ser explícito con esto, porque es fácil leer lo de arriba y creer que está resuelto. **No
lo está.** Lo que tenemos es una hipótesis con buena evidencia parcial:

- **El piloto nunca ha completado una tarea de SAP de punta a punta.** Todo lo que probamos en SAP
  lo disparamos nosotros a mano contra el MCP. La única corrida del piloto sobre SAP gastó 28
  viajes y se detuvo: había dos ventanas de SAP abiertas y el sistema estaba leyendo la
  equivocada. Ya lo arreglamos (promesa 72), pero **no lo hemos vuelto a medir**.
- **La parte nueva —la profundidad— no está construida.** Hoy el terreno sabe lo que hay en la
  pantalla actual. Lo que falta es servirle al modelo **lo que habrá después** de cada paso, para
  que se atreva a pedir batches de 5 o 10 pasos. Eso es la fase T3 del plan y está sin empezar.
- **No sabemos si esto escala.** Lo probado son dos superficies (web y SAP) y tareas de
  navegación. No hemos tocado formularios largos, ni pantallas que cambian solas, ni tablas ALV.
- **Hay deudas conocidas y abiertas**: el árbol del menú principal de SAP devuelve 0 filas
  visibles de 205 (no sabemos por qué); «ponerse delante» de una sesión SAP falla; y el sistema
  solo pregunta por SAP cuando SAP está en primer plano, cosa que es falsa para scripting.

Todo eso está listado en [`plan-terreno-profundo.md`](plan-terreno-profundo.md), que es el plan
vivo del enfoque.

## 6. Dónde entras tú

Tú vienes trabajando el mismo objetivo —ejecución por batch— desde tu propia rama y con tus
propios avances. La idea **no es que abandones tu enfoque**, sino que veas el nuestro con todo el
detalle y decidas qué combinar.

La rama está subida y es la versión limpia:

```bash
git fetch origin && git checkout jose/batch-resolver-estable
```

Puedes trabajar como prefieras: seguir en tu rama tomando de aquí lo que te sirva, o bifurcar esta
y seguir desde el punto en que la dejamos. Tu criterio de diseño manda en lo tuyo.

Si le vas a pasar el contexto a tu agente de código, el archivo
[`PARA-EL-AGENTE-DE-JERONIMO.md`](PARA-EL-AGENTE-DE-JERONIMO.md) está escrito exactamente para
eso: lleva el mapa de archivos, las reglas de la casa, cómo correr todo, los errores que ya
pagamos para que no los repitas, y el estado exacto con la lista de lo que sigue.

**Una advertencia de seguridad, esta sí en serio:** si al conectarte a SAP aparece el diálogo de
licencia diciendo que el usuario ya tiene sesiones abiertas, **nunca elijas «finalizar las
entradas existentes»**. Eso mata sesiones de otras personas y les tira el trabajo sin grabar. Se
cancela y se avisa.
