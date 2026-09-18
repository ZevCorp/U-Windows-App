# El decisor se puede cambiar: Jev elige, Luna habla

Estado: **implementada, fases 1-4, con nivel 4 en dos pantallas** · 2026-09-18 · Rama: `jose/el-decisor-se-puede-cambiar`

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
| **281** | El modo simulado no toca la red: decide con una regla fija y sirve para las pruebas; con **cero palabras en común no actúa**, y su confianza es cuánto de la puerta explica el objetivo, no un 1,00 fijo. *(Ampliada el 2026-09-18 tras el nivel 4: accionaba la primera puerta con 1,00 sin ninguna coincidencia.)* |
| **282** | La petición que se arma cumple el contrato HTTP de TypeSafe, campo por campo. |
| **283** | `429` y `529` se reintentan con espera creciente; `401` y `422` **no se reintentan**, porque reintentar una clave mala es gastar cupo. |
| **284** | `map_decidir` existe solo con el decisor encendido: apagado no está en el catálogo, las instrucciones no lo nombran, y llamarlo contesta que decide Luna sin leer la pantalla ni pulsar; encendido está en el catálogo con `objetivo`, y las instrucciones mandan pedirlo con el objetivo en vez de elegir la puerta. |
| **285** | `map_decidir` ofrece al decisor **exactamente las puertas que `map_what_i_see` lista, en su orden**, y acciona la elegida **por el mismo camino que `map_take`**: la mano recibe ese paso, la cuenta dice qué se eligió y con qué confianza, y el acto cuenta lo que dejó delante. |
| **286** | Cuando el decisor no actúa —duda, puerta fuera de lista, TypeSafe caído, o el propio decisor lanza— `map_decidir` no pulsa nada, dice por qué con las palabras del decisor, la mano no cuenta un intento, y el control vuelve con el inventario delante; sin `objetivo` dice qué falta. |

## Las fases

| Fase | Qué deja | Promesa que pone verde |
|---|---|---|
| 1 | La configuración y el interruptor: `ConfiguracionDelDecisor`, leída del entorno | 275, 276, 277 |
| 2 | El cliente de TypeSafe y las preguntas tipadas, con su modo simulado | 281, 282, 283 |
| 3 | La decisión de un paso: elegir puerta, validarla contra el inventario, compuerta de confianza, caída segura | 278, 279, 280 |
| 4 | `map_decidir` en `SurfaceMapTools` y en el catálogo de la voz, detrás del mismo interruptor; el cableado en `FaceWindow` | 284, 285, 286 |

### La fase 4: cómo entra sin romper nada

**Una herramienta nueva, no un cambio a las de siempre.** `map_take`, `map_type` y el resto siguen
idénticos. `map_decidir(objetivo)` es aditiva: arma el inventario **con la misma función** que
`map_what_i_see` —una sola lista, sin dos catálogos del mismo terreno que se desincronicen en
silencio—, le da las etiquetas al decisor, y si decide, acciona **llamando a `Take`**, el mismo
cuerpo de `map_take`: misma coreografía, mismos vetos, mismo juez, misma `Mano`.

**Con el decisor apagado la herramienta no existe.** No está en el catálogo, las instrucciones de
Luna no la nombran, y si un cliente la pide igual, contesta «todavía no sé decidir» sin leer la
pantalla. Así, `U_DECISOR` ausente deja el catálogo de Luna **byte a byte como hoy**.

**Con el decisor encendido, Luna pide `map_decidir` con el objetivo** en vez de elegir la puerta
con `map_take`. Eso es «Jev decide, Luna habla»: Luna sigue oyendo, hablando y sabiendo a dónde
va; quién pulsa qué lo decide Jev sobre la lista real. Y cuando Jev no se atreve, la respuesta
trae el porqué **y el inventario**, para que Luna elija ella como hasta hoy — el control vuelve,
no se pierde.

## Nivel 4: la corrida a mano, medida (2026-09-18)

Sobre este PC, con `U_DECISOR=simulado`, una instancia aislada (`C:\U-decisor\bin`, datos en
`C:\U-decisor`) y las llamadas por MCP (`127.0.0.1:8790/mcp`). **Dos pantallas, no una.** El
guion vive en el scratchpad de la sesión; el diario, en `C:\U-decisor\nivel4.txt`.

```
01:57:16.455 MCP listo; U mia: PID 70244 C:\U-decisor\bin\U.exe
01:57:16.474 tools/list: 29 herramientas; map_decidir SI esta
01:57:20.765 → map_where_am_i   (163 ms)
             Estás en «uia://explorer.exe/disco-local-c». Veo 78 salida(s) que puedo usar ahora
01:57:24.829 → map_decidir objetivo=abrir la carpeta Descargas  (2142 ms)
             elegida «Descargas» con confianza 1.00: hice los 1 paso(s): pulsé «Descargas» y ahora
             estás en «uia://explorer.exe/descargas». Queda aprendido.
             EN PANTALLA AHORA, en «uia://explorer.exe/descargas» (314 elemento(s)): …
01:57:28.060 → map_open_app app=Configuración  (115 ms)
01:57:32.312 → map_where_am_i   (59 ms)
             Estás en «uia://ApplicationFrameHost.exe/configuración». Veo 4 salida(s)
01:57:32.596 → map_decidir objetivo=abrir Bluetooth y dispositivos  (55 ms)
             no se acciona: decisión simulada (sin red, sin TypeSafe): ninguna de las 4 puertas
             comparte una palabra con el objetivo, así que no se acciona. Decide Luna.
             EN PANTALLA AHORA, en «uia://ApplicationFrameHost.exe/configuración» (4 elemento(s)): …
01:57:34.878 → map_decidir   (5 ms)
             falta `objetivo`: qué se quiere conseguir en esta pantalla, para que el decisor elija la puerta
--- el log de la app, C:\U-decisor\local\U\logs\u-20260918.log ---
[01:57:10] decisor: U_DECISOR=simulado: se decide con la regla fija, sin llamar a TypeSafe.
[01:57:23] decisor: «uia://explorer.exe/disco-local-c» · 61 puerta(s) · 2 ms → ACCIONA «Descargas» conf=1.00 · …
[01:57:32] decisor: «uia://ApplicationFrameHost.exe/configuración» · 4 puerta(s) · 0 ms → no acciona conf=0.00 · …
```

**Lo que el nivel 4 cazó y el contrato no** (intento 2, 01:52): con «abrir la carpeta Windows» —cero
palabras en común con las 61 puertas listadas— el simulado accionó «Detalles», la primera, con
confianza 1,00. Salió la promesa 281 ampliada y el arreglo (`87ea407`). El intento 1 (22:02) no
midió nada: el guion perdía el argumento (`$args` es variable automática de PowerShell).

**Lo que el nivel 4 deja a la vista y NO es de esta spec:** `map_what_i_see` lista 60 puertas de UIA
y 160 del terreno, y en el Explorador con 404 elementos «Windows» quedó entre las 184 de fuera. El
decisor solo puede elegir lo que esa lista trae, **a propósito** (promesa 285: una sola lista). Si
Jev tiene que ver la carpeta 61, es el tope de `map_what_i_see` el que se discute, no el decisor.

Nivel 4 con Jev de verdad: **no hecho**. Exige `TYPESAFE_API_KEY` y el hospital delante.

## Lo que queda fuera, dicho a propósito

- **La medida sobre SAP real con clave de verdad.** La fase 4 cablea y se prueba en seco y sobre
  este PC con `U_DECISOR=simulado` (dos pantallas, no una); si Jev elige *mejor* que Luna sobre
  SAP no se sabe hasta medirlo con clave y con el hospital delante. Cualquier número antes de eso
  sería inventado.
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
