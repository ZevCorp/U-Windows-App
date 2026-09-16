# Plan de implementación: las esperas se miden con el reloj

Estado: **propuesto** · 2026-09-15 · Rama: `jose/el-reloj-manda`

> El dueño: «el asistente se está demorando mucho en ejecutar… veo que intenta tres cosas para
> completar una tarea que tiene enfrente, como hacer un clic». Y después: «si el modelo ya lo pidió,
> nuestro código debería ejecutarlo instantáneo, e instantáneamente el modelo debería recibir
> feedback para poder continuar».

## Diagnóstico: dónde se va el tiempo

Del log de hoy (`C:/U-clic`, 229 llamadas con tiempo medido):

| | |
|---|---|
| Tiempo dentro de nuestras herramientas | **887 s** |
| Tiempo del modelo pensando entre llamada y llamada | **1.002 s** (53% del reloj) |
| Llamadas que acabaron **sin hacer nada** | 27, y se llevaron **348 s: el 39%** del tiempo de herramienta |

Y las peores, medidas: `map_go_to` 32 llamadas y 211 s, con casos de **32 y 38 segundos** para acabar
diciendo «no hay ningún camino aprendido»; un `map_take` de **28,8 s** para contestar «lo conozco aquí
pero AHORA no lo veo».

**La causa de fondo, y explica los tres números:** las esperas se cuentan en milisegundos
**ficticios**. Los cuatro bucles que esperan a que la pantalla reaccione están escritos así:

```csharp
for (int ido = 0; ido <= EsperaMaximaMs; ido += 120)   // «como mucho 1,8 s»
{
    donde = _donde();          // …pero esto cuesta 2,8 s de verdad
    Thread.Sleep(120);
}
```

El presupuesto suma 120 por vuelta, pero cada vuelta paga además lo que cueste el sondeo. Con un
sondeo de 2,8 s, una espera de «1,8 segundos» dura **más de treinta**. Cuanto más pesada la pantalla,
más se infla — justo cuando ya iba lento.

**Y el sondeo se encareció por nuestra culpa.** Medido ahora mismo, ocho veces seguidas:

| Llamada | Cuánto tarda |
|---|---|
| `map_where_am_i` (solo decir dónde estás) | **2.771 ms** |
| `map_what_i_see` (leer la pantalla ENTERA) | 823 ms |

Contestar dónde estás cuesta más del triple que leer toda la pantalla, y eso no tiene defensa. Salió
de las specs 020 y 022: cada «dónde estoy» vuelve a identificar la ventana de trabajo en vivo y
además la lee entera para alimentar la compuerta. Las dos cosas hacen falta antes de ACTUAR; ninguna
hace falta para contestar una pregunta.

## Por qué va dirigido por especificación

Porque «que vaya más rápido» no se puede juzgar, y «una espera de 1,8 s no puede tardar 30» sí. Sin
promesa, el próximo sondeo caro vuelve a inflar los mismos bucles y nadie se entera hasta que alguien
lo cronometre a mano otra vez.

## El diseño

**El reloj manda.** Los cuatro bucles miden el tiempo transcurrido de verdad. Es un cambio de una
línea en cada uno —el contador pasa a ser el reloj— y no toca ni su forma ni sus salidas.

**Lo que se acaba de mirar no se vuelve a mirar.** La ventana de trabajo se recuerda un instante:
dentro de ese instante, preguntar otra vez devuelve lo recordado sin tocar la pantalla. Fuera, se
vuelve a mirar. Es la misma idea que ya usa `LaBarraDeTareas` con su caducidad de cuatro segundos.

**Mirar es para actuar.** Leer la ventana de trabajo entera alimenta la compuerta que decide si un
elemento está vivo, y eso hace falta antes de pulsar o de recorrer. Contestar «dónde estás» no acciona
nada, así que deja de pagarlo.

## La especificación

| # | Promesa | Fase |
|---|---|---|
| 245 | las esperas se acotan con el RELOJ y no contando vueltas: con un sondeo lento, una espera de N milisegundos termina en N y no en N por el número de vueltas — ni al pulsar, ni al comprobar la llegada, ni en la compuerta que espera a que un elemento esté vivo | 1 |
| 246 | lo que se acaba de mirar no se vuelve a mirar: una memoria corta con su caducidad devuelve lo recordado sin volver a la fuente mientras no caduque, vuelve a preguntar cuando caduca, y se puede olvidar a mano cuando algo cambió | 1 |

### Con qué se juzga

Sin pantalla: se le da a las clases de verdad un «dónde estoy» que tarda 400 ms y se cronometra que
la espera respeta su presupuesto (245); y la memoria corta con un reloj inyectado, contando cuántas
veces llega a la fuente (246).

Sobre la máquina: `map_where_am_i` cronometrado antes y después. Hoy: 2.771 ms de mediana.

### Límites dichos, no escondidos

- Esto no toca lo que tarda el modelo en pensar, que hoy es el 53% del reloj. Lo único que lo baja es
  que fallen menos llamadas, y de eso van las specs 021 a 024.
- La compuerta sigue esperando lo que dice esperar: se arregla el reloj, no se recorta la espera.
- La memoria corta puede devolver algo que acaba de cambiar, durante su caducidad. Por eso es corta, y
  por eso se puede olvidar a mano en cuanto una acción cambia la pantalla.

## Las fases

### Fase 1 — el reloj y la memoria corta (245, 246)

Los cuatro bucles de `PulsarSegunElNucleo`, `RecorrerSegunElNucleo` y `AbrirSegunElNucleo`;
`Navigation.MemoriaCorta`; y el cableado de la carita para no releer la ventana al preguntar.

## Lo que NO entra

- Recortar las esperas: son las que verifican por consecuencia, y acortarlas es cambiar lo que se
  promete, no hacerlo más rápido.
- El catálogo de aplicaciones del sistema, que tarda segundos en cada apertura: se anota y va aparte.
