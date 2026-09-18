# Las claves viven en el backend: una copia distribuida no lleva secretos dentro

> Spec 041, del 2026-09-18. La 040 y la promesa 299 se las llevó otra rama el mismo día; los números no se reciclan. Nace de una petición concreta del dueño —«generar el instalador para pasárselo
> a un usuario, y que lo pueda utilizar 100%»— y de lo que se encontró al ir a hacerlo.

## El hecho que la provoca

El dueño pidió meter la clave de TypeSafe «en el mismo sitio donde está la de OpenAI». **Ese sitio no
existe.** Lo que hay hoy en un `Setup.exe` distribuido:

| Clave | Cómo llega | Estado |
|---|---|---|
| Graph (el cerebro) | `AssemblyMetadata GraphDefaultApiKey`, inyectada por CI desde un secreto | funciona |
| Token de actualizaciones | `AssemblyMetadata UpdateGithubToken`, ídem | funciona |
| Gemini | `AssemblyMetadata GeminiDefaultApiKey`, ídem | **embebida pero muerta**: nadie la lee desde que la voz pasó a GPT-Live |
| **OpenAI (la voz)** | `Environment.GetEnvironmentVariable("OPENAI_API_KEY")` y nada más | **no viaja**: la copia instalada se queda muda |
| **TypeSafe (Jev)** | `Environment.GetEnvironmentVariable("TYPESAFE_API_KEY")` y nada más | **no viaja**: la copia instalada no puede decidir |

Es decir: el instalador que hoy se distribuye llega **sin voz y sin Jev**, y eso no se sabía — se
creía que el mecanismo de Gemini seguía cubriendo la voz. Lo cubría hasta que la voz cambió de
proveedor, y la clave embebida se quedó ahí sin que nadie la leyera.

## Por qué NO se embeben, habiendo mecanismo

Embeberlas era una línea en el `.csproj` y media hora de trabajo. Se descartó con el dueño:

1. **Un `.exe` que lleva claves de pago las reparte.** Quien reciba el instalador puede extraerlas y
   gastar el saldo de OpenAI y de TypeSafe. Para Graph ese trato ya se aceptó por escrito
   (`windows-release.yml`) porque esa credencial la emite el propio producto y se revoca por
   etiqueta; OpenAI y TypeSafe son dinero directo y de terceros.
2. **Rotar obligaría a sacar instalador nuevo**, y a que cada usuario lo instalara, para algo que
   debería ser cambiar una variable y volver a desplegar.
3. **El repo ya lo tenía escrito como lo correcto.** `ConversacionEnVivo.Clave()` lleva meses
   diciendo que «lo correcto de verdad —una clave temporal emitida por sesión, como ya hace
   `DictadoEnVivo` con Soniox— sigue pendiente».

Lo que se hace en su lugar: **pedirlas al backend**, que es donde ya viven como variables de entorno
(`OPENAI_API_KEY` ya está puesta en Graph), con la credencial de Graph que el instalador **ya** lleva
embebida. Así no viaja ni un secreto nuevo dentro del binario, y rotar es cambiar la variable.

## La promesa

**300.** Una copia distribuida NO lleva dentro las claves de la voz ni de Jev: la del entorno manda si
está, y si no se le piden a Graph con la credencial que ya va embebida, UNA sola vez aunque se
resuelvan varias; lo traído vive solo en memoria; una clave que el backend no da deja su función
apagada diciendo cuál falta; y NINGUNA clave aparece jamás en el log ni en la línea de estado.

Las cinco cosas que juzga, y por qué cada una:

| | Qué exige | Por qué |
|---|---|---|
| 1 | Con la variable puesta, **no se pide nada** | la máquina de quien desarrolla sigue igual que hoy, y no se paga un viaje para no usarlo |
| 2 | Sin ella, se pide **una vez**, no una por clave ni por llamada | dos claves son un viaje, no dos |
| 3 | **Ninguna clave en el log ni en el estado** | un secreto en `%LOCALAPPDATA%\U\logs` es un secreto repartido, y el log se pega en los PR |
| 4 | Lo que no venga deja su función apagada **nombrando cuál falta** | «no hay voz» sin nombre manda la investigación al sitio equivocado (aprendizaje nº2) |
| 5 | Backend caído: ni tumba la app ni **reintenta en bucle** | sin voz se trabaja; y el bucle es el pendiente nº3 de `CLAUDE.md`, ya pagado una vez |

## Las dos piezas

**En el cliente** (`windows-client/src/Config/ClavesDelBackend.cs`): pide una vez, guarda en memoria y
nunca en disco, y resuelve entorno → backend. Se le inyectan el entorno, el «cómo pedir» y el log,
para que el contrato lo juzgue entero **sin red y sin pantalla**.

**En Graph** (`web/api/registerPublicApiRoutes.js`): `GET /api/v1/agent/claves`, que devuelve las
claves que tenga configuradas. No necesita autenticación nueva: todo lo que cuelga de `/api/v1` ya
pasa por `requireApiKey` (`web/server.js:547`), que valida contra un registro **con etiqueta por
cliente** — así se puede cortar a un usuario concreto sin tocar a los demás.

## Lo que queda fuera, y se dice

- **La clave viaja a la memoria del cliente.** Es mejor que dentro del `.exe` —no se reparte con el
  instalador y se puede rotar y revocar— pero no es lo óptimo. Lo óptimo es una **credencial efímera
  por sesión**: OpenAI la emite por `/v1/live/sessions`, y ahí la clave de verdad no saldría nunca del
  backend. TypeSafe no publica nada equivalente, así que para Jev la alternativa sería que Graph haga
  de intermediario, a costa de un salto más en un camino donde hoy se pelea por cada 100 ms. Es el
  corte siguiente, y se hace cuando esto esté rodado.
- **`GeminiDefaultApiKey` se queda donde está.** Está muerta, pero borrarla es otro cambio y no se
  mezcla con este.
