# El decisor se puede cambiar: Jev elige, Luna habla

Estado: **fase 1 y 2 implementadas** · 2026-09-17 · Rama: `jose/el-decisor-se-puede-cambiar`

> El dueño, 2026-09-17: «lo vamos a implementar utilizando Jev en reemplazo del modelo Luna […] el
> modelo razonador y ejecutor es Luna. Entonces lo que queremos hacer es que el modelo que ejecute y
> que se comunique con la voz sea Jev de TypeSafe». Y en seguida: **«dejando la funcionalidad actual
> intacta de como funciona Luna y todo el sistema actual, y que podamos de forma muy facil hacer
> switch entre uno y otro»**.

## Lo que TypeSafe es, medido contra su documentación

TypeSafe AI anunció **Jev** el 2026-09-15, dos días antes de escribir esto. El contrato HTTP está
leído de `docs.typesafe.ai/api.md` el 2026-09-17, no de memoria:

```http
POST https://api.typesafe.ai/v1/systemone
Authorization: Bearer <TYPESAFE_API_KEY>
Content-Type: application/json
```

El cuerpo lleva `state` (lo que hay que juzgar), `model` (`jev-latest` → hoy `jev-1.13.0`) y un mapa
`questions`. Cada pregunta es de **uno de tres tipos, y no hay más**:

| Tipo | Qué pregunta | Qué devuelve |
|---|---|---|
| `noul` | un sí/no | `noul`: probabilidad de que sí, de 0 a 1 |
| `choice` | elige una de **una lista que TÚ defines** en `criteria` | `choice` (la elegida), `probabilities` de cada una, y `confidence` |
| `score` | puntúa contra niveles que tú defines | `score` (entre niveles), `legend`, `probabilities`, `confidence` |

Errores: `401` clave mala, `422` cuerpo inválido, `429` pasado de cupo, `529` sobrecargado. Límites
hoy: 250.000 tokens/s y 1.200 peticiones/minuto, y la propia documentación avisa de que **se mueven
sin aviso**. Precio: $0,042 por millón de tokens de entrada, salida gratis.

### El hallazgo que decide la forma de esta spec

**Jev no puede sustituir a Luna, y no es una limitación nuestra.** No genera texto, no llama
herramientas, no mantiene conversación y no guarda estado entre peticiones. No podría decir una
frase por la voz, ni pedir `map_take`, ni armar un plan. Lo único que hace es contestar preguntas
cerradas sobre un estado que le mandamos entero en cada llamada.

Luna (GPT-Live) hoy hace tres cosas a la vez: oye, habla, y **elige qué puerta tomar** llamando a
las herramientas `map_*`. De esas tres, Jev solo puede hacer la tercera — y solo si el código le
pone delante la lista de puertas entre las que elegir.

Por eso esta spec **no reemplaza a Luna**: le quita *una* responsabilidad y se la da a Jev, detrás
de un interruptor, dejando el camino de hoy intacto y por defecto.

### Y por qué eso es justo lo que a este repo le duele

No es una integración de escaparate. `CLAUDE.md` ya tiene escrito el pendiente nº2 desde julio:

> **El puente consciente improvisa.** […] una vez pulsó «Buscar pacientes» en vez de «Crear Triage
> Administrativo». […] Le falta […] **el inventario de lo accionable sin coordenadas**.

Un `choice` de Jev **no puede inventarse una puerta que no esté en la lista**: la respuesta es una
de las claves que le dimos, o el código la rechaza. Y su `confidence` es la segunda mitad de lo que
falta — el pendiente nº3 dice que hoy hay «dos jueces y uno es un modelo optimista».

## La costura: dónde entra, exactamente

`map_what_i_see` (`SurfaceMapTools.cs:158`) ya produce el inventario: las puertas vivas de la
pantalla con su etiqueta y su tipo, fundiendo lo que ve UIA, lo que sabe el terreno y los campos del
dynpro. `map_take(exit, …)` (`SurfaceMapTools.cs:2354`) ejecuta una **por su nombre**.

Entre esas dos está el hueco donde hoy decide Luna. Ahí entra el decisor.

## Las promesas

| # | Lo que el sistema promete |
|---|---|
| **275** | El decisor por defecto es Luna: sin configuración, se decide como hoy y a TypeSafe **no se le llama nunca**. |
| **276** | El interruptor cambia quién decide sin recompilar (`U_DECISOR`), y un valor que no se entiende cae en Luna, diciéndolo. |
| **277** | La clave **no vive en el código**: sale de `TYPESAFE_API_KEY`, y sin ella el decisor de Jev no se activa — se queda en Luna y dice por qué, en vez de llamar sin credencial. |
| **278** | Jev solo puede elegir entre las puertas que se le dieron: una respuesta con una puerta que no está en el inventario **se rechaza y no se ejecuta**. |
| **279** | Por debajo del umbral de confianza no se actúa: la decisión se declara insegura y el control vuelve a Luna. |
| **280** | TypeSafe caído, lento o con error no detiene el trabajo: al agotarse el plazo o fallar, se cae a Luna y el trabajo sigue. |
| **281** | El modo simulado no toca la red: decide con una regla fija y sirve para las pruebas. |
| **282** | La petición que se arma cumple el contrato HTTP de TypeSafe, campo por campo. |
| **283** | `429` y `529` se reintentan con espera creciente; `401` y `422` **no se reintentan**, porque reintentar una clave mala es gastar cupo. |

## Las fases

| Fase | Qué deja | Promesa que pone verde |
|---|---|---|
| 1 | La configuración y el interruptor: `ConfiguracionDelDecisor`, leída del entorno | 275, 276, 277 |
| 2 | El cliente de TypeSafe y las preguntas tipadas, con su modo simulado | 281, 282, 283 |
| 3 | La decisión de un paso: elegir puerta, validarla contra el inventario, compuerta de confianza, caída segura | 278, 279, 280 |
| 4 | *(fuera de esta rama)* Cablearlo en `SurfaceMapTools` y medirlo sobre SAP real | — |

## Lo que queda fuera, dicho a propósito

- **La fase 4 no entra aquí.** Cablear el decisor dentro del bucle vivo de `SurfaceMapTools` toca el
  camino que usa el hospital, y el nivel 4 de la compuerta pide dos pantallas reales con SAP
  delante. Esta rama deja la pieza construida, probada y apagada; encenderla es su propia rama, con
  su propia corrida a mano.
- **No se llama a la API de verdad en ninguna prueba.** El contrato no tiene red y no debe tenerla:
  el transporte se inyecta, y las pruebas le dan uno de mentira.

## Las decisiones de producto que faltaban, y la opción conservadora que se eligió

El encargo llegó cortado (la lista numerada empezaba en el 6). Donde faltaba una decisión, se tomó
la más conservadora y se anota aquí:

| Duda | Qué se eligió | Por qué |
|---|---|---|
| ¿Jev encendido o apagado por defecto? | **Apagado.** `U_DECISOR` ausente = Luna | Es lo que el dueño pidió en su segundo mensaje, y lo que no se ha medido no manda sobre un hospital |
| ¿Qué umbral de confianza? | **0,70**, configurable | La documentación de TypeSafe no publica un umbral recomendado; 0,70 deja fuera la banda dudosa y se ajusta con datos, no antes |
| ¿Cuánto se espera a TypeSafe? | **2.000 ms**, configurable | `map_where_am_i` ya costaba 2.771 ms de mediana (spec 025): pasar de ahí sería empeorar lo que se viene arreglando |
| ¿Qué pasa si Jev duda? | **No se actúa; decide Luna** | Es el pendiente nº3: un juez que no puede juzgar debe decir «no sé», nunca «culpable» (aprendizaje nº17) |
| ¿Se reintenta un `401`? | **No** | Una clave mala no mejora reintentando, y gasta cupo de un límite que la propia documentación dice que se mueve |
| ¿Se manda la pantalla en claro a TypeSafe? | **Solo etiquetas y tipos de las puertas**, nunca valores de campos | El terreno es SAP de un hospital: las etiquetas son cromo de la aplicación, los valores son datos de pacientes |

## El límite honesto de esta spec

Lo que aquí se promete es que **la pieza decide bien y se cae bien**. Lo que NO se promete todavía
es que Jev elija mejor que Luna sobre SAP: eso no se sabe hasta medirlo con clave real sobre
pantallas reales, y hasta entonces cualquier número sería inventado. La fase 4 existe para eso.
