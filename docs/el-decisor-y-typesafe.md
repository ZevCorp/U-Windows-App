# El decisor: cómo se cambia entre Luna y Jev

> 2026-09-17 · spec [035](specs/035-el-decisor-se-puede-cambiar.md) · promesas 275-283

## En una línea

Por defecto **no cambia nada**: decide Luna, exactamente como hasta hoy. Con una variable de
entorno, quien elige qué puerta accionar pasa a ser **Jev**, el modelo de TypeSafe AI.

```powershell
$env:U_DECISOR = "jev"          # y ya está. Sin recompilar.
$env:TYPESAFE_API_KEY = "..."   # sin esto, se queda en Luna y lo dice.
```

Para volver, se borra la variable o se pone `luna`.

## Lo que Jev es y lo que no

Jev **no sustituye a Luna, y no puede**. Luna (GPT-Live) hace tres cosas: oye, habla y elige qué
herramienta llamar. Jev no genera texto, no llama herramientas y no mantiene conversación — solo
contesta preguntas cerradas sobre un estado que se le manda entero en cada llamada.

De las tres cosas de Luna, Jev solo puede hacer **la tercera**, y solo si el código le pone delante
la lista de puertas entre las que elegir. Eso es exactamente lo que hace el decisor.

| | Luna (GPT-Live) | Jev (TypeSafe) |
|---|---|---|
| Oír y hablar | sí | **no** |
| Razonar en texto | sí | **no** |
| Llamar herramientas | sí | **no** |
| Elegir de una lista cerrada | sí | **sí**, y no puede salirse de ella |
| Decir lo segura que está | no | **sí** (`confidence`) |

Por qué merece la pena, entonces: `CLAUDE.md` tiene escrito desde julio que *«el puente consciente
improvisa»* y que una vez pulsó «Buscar pacientes» en vez de «Crear Triage Administrativo». Un
`choice` de Jev devuelve una de las claves que se le dieron — **inventarse una puerta no es una
respuesta posible**— y su `confidence` permite no actuar cuando duda.

## Las variables

| Variable | Por defecto | Qué hace |
|---|---|---|
| `U_DECISOR` | *(ausente)* → `luna` | `luna`, `jev` o `simulado`. Un valor que no se entiende cae en `luna` y lo dice. |
| `TYPESAFE_API_KEY` | — | La credencial. **Obligatoria para `jev`**; sin ella se queda en Luna. |
| `U_TYPESAFE_CONFIANZA` | `0.70` | Mínimo de confianza para actuar. Se actúa **al alcanzarlo**, no solo al superarlo. |
| `U_TYPESAFE_TIMEOUT_MS` | `2000` | Plazo total, esperas entre reintentos incluidas. |
| `U_TYPESAFE_MODELO` | `jev-latest` | Alias o versión fija (`jev-1.13.0`). |

### La clave no vive en el código, y no viaja en el cuerpo

Sale de `TYPESAFE_API_KEY` y va a la cabecera `Authorization: Bearer`. No está en ningún archivo del
repo, no se registra en el log y **no entra en el cuerpo de la petición** — eso último es la promesa
277, y se comprueba: el cuerpo se pega en el log cuando algo falla, y un secreto en el log ya se
filtró.

Para dejarla puesta en una máquina sin escribirla en ningún archivo del repo:

```powershell
[Environment]::SetEnvironmentVariable("TYPESAFE_API_KEY", "sk-...", "User")
```

## El modo simulado

`U_DECISOR=simulado` ejercita la cadena entera —inventario, elección, validación, compuerta— **sin
red y sin clave**. Elige con una regla fija: la puerta cuya etiqueta comparte más palabras con el
objetivo, y a igualdad la primera en el orden de lectura.

**No imita a Jev y no pretende hacerlo.** Es andamiaje para ver pasar los datos. Un verde en
simulado no dice nada sobre la calidad de Jev, y por eso cada decisión simulada lo declara en su
propio `Porque`.

## Qué pasa cuando algo va mal

En todos estos casos **el trabajo sigue**: la decisión vuelve a Luna y se dice por qué. Nunca se
acciona a medias, y la excepción no sale de la pieza.

| Qué pasa | Qué hace el decisor |
|---|---|
| No hay clave | Se queda en Luna al arrancar, nombrando la variable que falta |
| TypeSafe tarda más del plazo | Cae a Luna |
| Se cae la red | Cae a Luna |
| Contesta algo que no es JSON | Cae a Luna |
| Contesta una puerta que no está en pantalla | **No se acciona**, y se dice cuál contestó |
| Contesta con confianza por debajo del umbral | **No se acciona**, y se conserva la confianza |
| `429` / `529` | Se reintenta con espera creciente (200 → 400 → 800 ms) |
| `401` / `422` | **No se reintenta**: una clave mala no mejora insistiendo, y gasta cupo |

## Lo que se le manda a TypeSafe, y lo que no

Se manda: el nombre de la pantalla, el objetivo, y **las etiquetas de las puertas accionables**.

No se manda: **el contenido de ningún campo**. El terreno es SAP de un hospital — las etiquetas son
cromo de la aplicación («Presión Arterial»), lo que un campo contiene es un dato de un paciente.

## Cómo entra en el bucle vivo: `map_decidir`

Con el decisor encendido aparece **una herramienta nueva** en el catálogo de Luna, `map_decidir`,
que pide un `objetivo` («crear el triage administrativo del paciente») en vez de una puerta:

1. Arma el inventario de la pantalla **con la misma función** que `map_what_i_see` — una sola
   lista, no dos catálogos del mismo terreno que se desincronicen en silencio.
2. Le da al decisor la pantalla, el objetivo y las etiquetas de las puertas.
3. Si el decisor actúa, **acciona llamando al cuerpo de `map_take`**: misma coreografía (decir,
   colgar el recuerdo, señalar), mismos vetos, mismo juez de llegada, misma `Mano` para el tope de
   intentos. La cuenta empieza por qué se eligió y con qué confianza.
4. Si no actúa, no pulsa nada, dice por qué con las palabras del decisor, **y devuelve el
   inventario**: el control vuelve a Luna con lo que hay delante, y ella elige como hasta hoy.

Las instrucciones de Luna cambian en un párrafo, solo cuando está encendido: pedir `map_decidir`
con el objetivo en vez de elegir la puerta con `map_take`. Eso es «Jev decide, Luna habla».

**Con el decisor apagado, `map_decidir` no existe**: no está en el catálogo, las instrucciones no
lo nombran, y si un cliente MCP lo pide igual contesta «todavía no sé decidir» sin leer la
pantalla. El catálogo de Luna queda byte a byte como hoy.

`map_take`, `map_type` y el resto **no cambian**.

## Lo que todavía no se sabe

Si Jev elige *mejor* que Luna sobre SAP. La fase 4 está cableada y probada en seco y con
`U_DECISOR=simulado` sobre este PC; medirlo de verdad exige clave real y el hospital delante.
Cualquier número antes de eso sería inventado.

## El contrato HTTP, para quien lo tenga que tocar

Leído de `docs.typesafe.ai/api.md` el 2026-09-17.

```http
POST https://api.typesafe.ai/v1/systemone
Authorization: Bearer $TYPESAFE_API_KEY
Content-Type: application/json
```

```json
{
  "state": "Pantalla actual: SAP/NWP1\nLo que se quiere conseguir: crear el triage\nPuertas…",
  "model": "jev-latest",
  "questions": {
    "puerta": {
      "type": "choice",
      "instructions": "¿Qué puerta hay que accionar AHORA para avanzar hacia «crear el triage»?",
      "criteria": { "Crear Triage Administrativo": null, "Buscar pacientes": null }
    }
  }
}
```

La respuesta trae, bajo el mismo id, la opción elegida, la probabilidad de cada una y la confianza:

```json
{
  "model": "jev-1.13.0",
  "answers": {
    "puerta": {
      "type": "choice",
      "choice": "Crear Triage Administrativo",
      "probabilities": { "Crear Triage Administrativo": 0.93, "Buscar pacientes": 0.07 },
      "confidence": 0.91
    }
  },
  "usage": { "input_tokens": 312, "output_tokens": 0 }
}
```

Precio: $0,042 por millón de tokens de entrada; la salida no se cobra. Límites hoy: 250.000 tokens/s
y 1.200 peticiones/minuto — y **su propia documentación avisa de que se mueven sin aviso** mientras
sirven la demanda del lanzamiento. Por eso los reintentos distinguen lo que mejora insistiendo de lo
que no.
