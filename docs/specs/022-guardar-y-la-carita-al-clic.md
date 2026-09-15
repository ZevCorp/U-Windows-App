# Plan de implementación: guardar es una decisión, y la carita va a donde se pulsa

Estado: **implementado** el 2026-09-14 (239 y 240 en verde, contrato intacto, sabotaje comprobado) · Nace de la corrida del 2026-09-14 (20:26) · Rama: `jose/la-ventana-de-trabajo` (continúa las specs 020 y 021)

> El dueño, al ver que Ü no pudo responder al diálogo del Bloc de notas: «quita esa regla, que pueda
> guardar o decidir no guardar cosas libremente». Y sobre el movimiento: «la carita ya es muy buena
> haciendo clics sin el mouse y quiero que siempre que se haga un clic la carita se mueva hasta allá,
> con un movimiento fluido, no rápido y brusco, sino fluido rápidamente, con una curva de aceleración
> suave pero rápida».

## Diagnóstico: qué se midió

| Qué | Medida | Fuente |
|---|---|---|
| Por qué no se pudo responder «No guardar» | «NO pulso «No guardar»: «No guardar» contiene «guardar». Una opción destructiva la confirma el usuario, no yo» | log 2026-09-14 20:26:37 |
| De dónde sale ese veto | `EsDestructivo` recorre `Prohibido`, la lista del explorador autónomo, donde «guardar» está junto a «eliminar» y «formatear» | `SafeToClick.Prohibido`, 44 verbos |
| Qué preguntan las dos listas | distinta cosa, y el propio código lo dice: `Auto` contesta «¿puede el explorador pulsar esto MIENTRAS MAPEA?» —y por eso rechaza hasta «Opciones»—, mientras `EsDestructivo` contesta «¿esto hace daño aunque me lo pidan?» | comentario de `EsDestructivo`, 2026-08-03 |
| Qué pasa con la negación | «No eliminar» también está vetado: el buscador encuentra el verbo y no ve el «no» que lo invierte, y justo esa es la opción que salva el archivo | `ContienePalabra` |
| Cuánto se mueve hoy la carita al pulsar | nada: solo sigue al cursor cuando el ratón se mueve de verdad (`CursorMoved`, que emite `SmoothMove`), y desde la spec 020 la mayoría de los clics son por patrón o por mensaje, sin ratón | `UiaSurface.SmoothMove`, `FaceWindow.OnAutomationCursorMoved`; log 20:28:33 y 20:28:37 |
| Qué motor de movimiento ya existe | `Vuelo.Mover` con `MuelleEase`, y `JuntoA`, que coloca la carita al lado de una caja sin taparla y en el monitor correcto | `FaceWindow.MoverConMuelle`, `JuntoA`, usado hoy al señalar |

## Por qué va dirigido por especificación

Lo primero cambia lo que Ü se permite hacer: un veto que se levanta es una promesa nueva en los dos
sentidos —lo que ahora sí pulsa y lo que sigue sin pulsar—, y sin escribirlo, el día que alguien
vuelva a tocar la lista nadie sabrá cuál de las dos preguntas estaba contestando. Lo segundo es
comportamiento observable: la carita se mueve o no se mueve, y con qué curva.

**La regla del flujo:** ninguna línea de producción entra antes que la promesa que la juzga.

## El diseño

**Dos listas para dos preguntas.** El explorador autónomo conserva la suya entera, que es larga a
propósito porque al mapear no hay nadie mirando. Responder un diálogo pasa a tener la suya: lo que
NO se deshace. Sale de ahí la familia de guardar —guardar, save, aplicar, apply—, porque guardar es
lo que la persona pidió y no guardar es una decisión legítima. Se quedan: destruir (eliminar,
formatear, desinstalar, vaciar, sobrescribir), la energía y la sesión (apagar, reiniciar, cerrar
sesión), sacar los datos fuera (enviar, compartir, publicar, subir) y comprometer a la persona
(aceptar, permitir, instalar, comprar, pagar). Y una regla nueva que faltaba: **una etiqueta que
niega el verbo no es ese verbo**. «No eliminar» es la opción que salva el archivo.

**La carita viaja a cada clic.** La mano avisa de dónde cayó cada clic —simple, doble o derecho— con
la caja del elemento, y la carita va a plantarse a su lado con el colocador que ya existe, el que no
la tapa y respeta el monitor. El viaje tiene su propia curva, distinta de la del lanzamiento: sale
acelerando en vez de arrancar de golpe, hace la mayor parte del camino en el primer tercio del tiempo
y se posa sin rebotar. Un rebote está bien una vez; en cada clic se lee como gelatina.

## La especificación

| # | Promesa | Fase |
|---|---|---|
| 239 | responder un diálogo deja guardar y deja NO guardar: el veto de lo destructivo tiene su propia lista —lo que no se deshace— y no la del explorador autónomo; «Guardar», «No guardar» y «Aplicar» se pulsan cuando el modelo lo pide, mientras «Eliminar», «Formatear», «Reiniciar», «Enviar» y «Aceptar» siguen vetados; una etiqueta que niega el verbo pegado a él no es ese verbo; y el explorador autónomo no se relaja | 1 |
| 240 | la carita va a donde Ü acaba de pulsar: los tres clics de la mano avisan con la caja del elemento y escribir o elegir no, el viaje solo vale la pena a partir de un salto real, y su curva es fluida y rápida —empieza acelerando, no se devuelve, no rebota, hace más de medio camino en el primer tercio del tiempo y cruzar la pantalla entera no pasa de 450 ms— | 1 |

### Con qué se juzga cada una

Sin pantalla: las dos listas y la negación, verbo por verbo (239); la decisión de qué avisa, el
umbral del viaje y la curva muestreada en 200 puntos (240).

Sobre la máquina: responder «No guardar» en el Bloc de notas y que el archivo se cierre sin guardar;
y ver a la carita acompañar una cadena de clics en Chrome sin que ninguno falle por tenerla encima.

### Límites dichos, no escondidos

- La carita viaja **colapsada**, que es como trabaja mientras Ü opera. Con el panel abierto no se
  mueve: arrastrar el panel entero en cada clic taparía justo lo que la persona está leyendo.
- Los clics dentro de SAP van por su propia mano y no avisan todavía.
- `map_unblock` pulsa el botón del diálogo por su patrón, no por `Actuar`: responder un diálogo no
  mueve la carita.
- La carita queda plantada al lado de lo último que pulsó. Si el siguiente objetivo cae justo debajo
  de ella, un clic FÍSICO podría no llegar; el patrón y el mensaje no se ven afectados, que son la
  mayoría. Se mide en la corrida a mano y, si aparece, se trata aparte.

## Las fases

### Fase 1 — las dos cosas (239, 240)

`SafeToClick.Destructivo` y la negación. `U.Graph.Surfaces.ComoViajaLaCarita` (pura),
`UiaSurface.Pulso` disparado desde `Actuar`, y en la carita `CurvaDelClic` sobre el `Vuelo` que ya
existe, entrando por `IrJuntoA`.

## Lo que se encontró al implementar

- **Las dos listas ya estaban separadas en el comentario, no en el código.** `EsDestructivo` llevaba
  desde el 2026-08-03 un comentario explicando que contesta otra pregunta que `Auto`, y aun así
  recorría la misma lista. Un comentario que dice lo que el código no hace envejece peor que no
  tenerlo: el veto de «No guardar» estaba escrito ahí desde el principio.
- **La negación necesita mirar DOS palabras atrás, no una.** `Normalizar` convierte el apóstrofo en
  espacio, así que «Don't delete» llega como «don t delete» y la palabra pegada al verbo es «t».
- **El viaje se mide en el log o no se mide.** Sin una línea que diga de dónde a dónde y en cuánto,
  la promesa 240 solo se podía comprobar mirando la pantalla. Se añadió `ui-anim: viaje al clic`.

### La corrida a mano (nivel 4), del log de `C:/U-clic`, 2026-09-14

| Hora | Qué | Resultado |
|---|---|---|
| 21:50:23 | abrir un Bloc de notas nuevo y ensuciarlo | «Su ventana ya está» (la espera de la 021) y escrito por patrón |
| 21:51:03 | cerrar y preguntar dónde | INTERRUPCIÓN con «Guardar», «No guardar», «Cancelar» |
| 21:51:07 | `map_unblock` con `choose=No guardar` | «pulsado «No guardar» por Invoke sobre el botón del diálogo»; el Bloc se cerró sin guardar y la ventana de trabajo avisó de que ya no existe |
| 21:50:57 | clic en «Cerrar» del Bloc | «viaje al clic: (1408,91) → (922,176) · 493 px en 239 ms» |
| 21:51:41 | clic en «Nueva pestaña» de Chrome | «viaje al clic: (922,176) → (411,0) · 541 px en 247 ms» |
| 21:51:50 | clic en «Preguntar a Google» (por mensaje) | «viaje al clic: (411,0) → (1048,347) · 725 px en 281 ms» |

Tres pantallas con nombre: Bloc de notas, Chrome y la propia carita. En la cadena de tres clics
seguidos ninguno falló por tener la carita cerca: fueron Patrón, Patrón y Mensaje, y los tres
agarraron. El riesgo de que la carita tape el siguiente objetivo sigue anotado como límite, sin
haberse manifestado todavía.

## Lo que NO entra

- Que la carita se aparte sola cuando tapa el siguiente objetivo.
- Que los clics de SAP la muevan.
