# Plan de implementación: el clic no se rechaza a sí mismo

Estado: **propuesto** · 2026-09-17 · Rama: `jose/el-clic-no-se-rechaza`

> El dueño, describiendo lo que ve: «cuando un clic falla lo noto porque dice “hice 0 de 1 y paré en el paso
> 1”… sucede mucho. Y hay una demora entre que dice “pulsando” y que realmente se pulsa. Me gusta que la
> carita se haga al lado de donde va a hacer clic y haga clic, pero se siente que lo hace en dos tandas.
> Quiero que no fallen los clics, y que la carita se mueva rápido al lado y haga clic al instante.»

## Lo que se midió

**El clic era lo más caro de todo el sistema y no salía en ninguna tabla.** El modelo lanza `map_take` y
`map_esto_es` en la misma tanda y el lector de logs de la spec 029 los emparejaba mal: los clics se
descartaban. Emparejando por herramienta, la corrida del 17 a las 13:17 no son 67 s de herramienta: son
**143, y 76 son clics**. Tres días: **1.249 s en `map_take`**, más que cualquier otra cosa. Un clic cuesta
~5 s cuando sale bien, ~6–7 cuando no cambia nada o no se pudo.

**Por qué fallan: 74 «hice 0 de 1» en tres días más la corrida, por motivo:**

| Motivo | n | Qué es |
|---|---|---|
| «lo conozco aquí pero AHORA no lo veo» | 19 | la compuerta |
| «no lo conozco en esta pantalla» | 18 | la compuerta |
| resolver falló tras 5 intentos | 15 | el elemento no está en la ventana de trabajo |
| homónimos: varias puertas con ese nombre | 13 | se pide `which`: correcto, pero es una vuelta |
| el patrón falló | 3 | Invoke lanzó y se devolvió fallo sin bajar al clic físico |

**La mitad de los fallos —37 de 74— los pone nuestra propia compuerta antes de intentar pulsar.** La
compuerta juzga si la puerta está viva contra la memoria del mapa vivo (`Grafo.DesdeAqui` → la última
observación), que va 1–2 s por detrás de la pantalla y además recorta (400 elementos, 40 niveles,
solo lo visible). Prueba de la corrida: a las 13:19:52 `map_pointing_at` leyó la pantalla y dijo «señalas
“Abre el perfil Miracle” (Button) · **puedo pulsarlo ahora**»; a las 13:20:18 el modelo pidió el clic, la
compuerta esperó su presupuesto entero —**4 s**— y contestó «no lo conozco en web://instagram.com». Dos
veces seguidas: 14 s y dos «otras vías» para un botón que el propio sistema había declarado pulsable 26 s
antes. Dos jueces con dos criterios (aprendizaje nº16), y el que veta es el más viejo.

**Las dos tandas que siente el dueño son la coreografía de la spec 014 aplicada fuera de su sitio.** La
014 pidió que *al comprobar una lección* la carita se ponga al lado, diga la frase, escriba el recuerdo,
muestre la tarjeta y **espere a que se lea** (900 ms + 25 ms por carácter, hasta 4 s) antes de tocar. Pero
el catálogo ofrece `decir` y `recuerdo` en todo `map_take`, y el modelo los pone en **el 89% de los clics
normales** (16 de 18 en la corrida). La tarjeta no se ve —el dueño no la ha visto nunca—; la pausa sí.
Dos clics, fase a fase:

```
13:21:59  → map_take «Mensajes»  decir=…  recuerdo=…
13:22:00–01   map_esto_es                                   1,5 s
13:22:01–04   tarjeta + PAUSA DE LECTURA + señalar             3,0 s
13:22:04      resolver + Invoke                             <1 s
13:22:04–05   cambió, inventario                             ~1 s      ← 6.334 ms
```
```
13:23:44  → map_take «Search»  decir=…  recuerdo=…
13:23:44–45   map_esto_es                                   0,3 s
13:23:45–47   tarjeta + pausa                               2,0 s
13:23:47      resolver + clic por mensaje                   <1 s
13:23:47–49   esperar un cambio que un campo no produce     1,8 s      ← 4.979 ms «no cambió»
```

Y el notch pinta **✓** sobre «hice 0 de 1»: `Terminado` decide el icono buscando «No », «falló»,
«no se pudo» al principio del texto, y «hice 0 de 1» no empieza por nada de eso.

## La especificación

| # | Promesa | Fase |
|---|---|---|
| 264 | vivo se juzga mirando AHORA: si el mapa no tiene una puerta como viva, antes de rendirse la compuerta vuelve a mirar la ventana; si al mirar aparece, se pulsa en el acto; si no aparece ni mirando, se dice que no se ve, como hasta hoy | 1 |
| 265 | un patrón que lanza no es un clic que falló: la escalera de pulsar termina siempre en el clic físico —tras el patrón, tras el mensaje— y sólo el físico decide que no se pudo | 1 |
| 266 | fuera de una comprobación, pulsar es señalar y tocar en un solo gesto: sin tarjeta ni pausa de lectura, y el recuerdo que el modelo mande se escribe DESPUÉS de tocar; dentro de una comprobación la coreografía de la 180 sigue entera | 1 |
| 267 | un paso que hizo 0 de N se ve como fallo, no con ✓: el notch y el registro lo pintan como lo que fue | 1 |

### Con qué se juzga

Sin pantalla: el batch con un grafo donde la puerta está recordada pero no viva y un «mira otra vez» que la
hace aparecer —se pulsa—, y otro que no la hace aparecer —no se pulsa y se dice, como la 56— (264); la
escalera como lista de gestos, que termina en Físico venga de donde venga (265); la coreografía fuera de una
comprobación como secuencia pura: señalar, decir, TOCAR, escribir, soltar, sin mostrar ni esperar, y la de
dentro idéntica a la 180 (266); y `Terminado` con «hice 0 de 1» delante (267).

Sobre la máquina: el botón de Instagram que la compuerta rechazaba, pulsado; y una tarea del dueño contando
«hice 0 de 1» y midiendo el clic de «pulsando» a «pulsado».

### Límites dichos, no escondidos

- Mirar otra vez cuesta una lectura de la ventana (0,4–1 s). Se paga sólo cuando el mapa no tiene la puerta
  viva, que es justo cuando hoy se pagaban 4 s y un fallo.
- Los 15 «resolver falló» y los 13 homónimos no entran aquí: son de otra clase (el ámbito de la ventana de
  trabajo, y elegir entre iguales) y se anotan.
- Esperar 1,8 s a que cambie la pantalla al pulsar un campo de texto tampoco entra: es la promesa 44 y se
  discute aparte con el dueño.

## Las fases

### Fase 1 — las cuatro (264–267)

`RecorrerSegunElNucleo.MiraOtraVez` y su uso en la compuerta; la carita lo cablea a una observación de la
ventana de trabajo sin el freno de 800 ms. `ComoSePulsa.Escalera` y `PulsarEnLaVentana` recorriéndola.
`ElRecuerdoQueSeVe.Coreografia` con `enComprobacion`, y `DarUnPasoConCoreografia` pasándole si hay una
comprobación en curso. `Terminado` con «hice 0 de».

## Lo que se encontró al implementar (2026-09-17)

- **Dos jueces viejos pedían la coreografía por reflexión sin firma.** La 180 y la 191 hacían
  `GetMethod("Coreografia")`; con la sobrecarga de cuatro argumentos eso es `AmbiguousMatchException`
  y las dos se ponían rojas sin que su promesa hubiera cambiado. Ahora piden la de tres por firma. Es
  el juez, no el núcleo: la coreografía dentro de una comprobación sigue siendo la misma.
- **«En el acto» se mide contra el reloj de después del clic.** En el batch, tras pulsar se espera a
  que cambie la pantalla hasta agotar el presupuesto (240 ms en el juez), y en un grafo de mentira no
  cambia nunca: una compuerta que mira en el acto tarda un presupuesto; una que se rindió primero
  tarda dos. La 264 pide menos de uno y medio.
- **Un patrón que lanza deja escrito que bajó al ratón.** `PulsarEnLaVentana` anota «patrón … falló
  (…) → ratón real» y la escalera entera, para que el log distinga «no se pudo» de «bajó un peldaño».
- **El contrato no se puede correr en el `%TEMP%` compartido cuando hay otra sesión corriéndolo**: el
  juez muere a mitad —tras la 163 una vez, tras la 231 la siguiente— y el guion dice «NO SE PUDO
  JUZGAR» sin excepción ni evento. Se corre con `TEMP` propio.

**Estado:** fase 1 implementada el 2026-09-17; contrato 236/236. Nivel 4 en la sección de evidencia.
