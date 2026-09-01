# Plan de implementación: la consulta médico-paciente se graba desde Windows

Estado: **implementado** (2026-09-01) · Nace de la medición del 2026-09-01 · Rama: `jose/la-consulta-vive-en-windows`

El médico abre un icono del escritorio, entra con **su cuenta de Miracle** —la misma de la web—,
pulsa grabar, habla con el paciente, para, y la nota organizada queda en **el mismo encounter** que
la web ya sabe leer. Al recargar el portal, la consulta está ahí.

---

## Diagnóstico: qué se midió

Se midió leyendo el código de los tres lados —cliente Windows, portal, backend Graph— y el contrato
que ya está escrito. **La mitad del trabajo ya existe y funciona**; lo que falta es concreto.

| Qué | Medida | Fuente |
|---|---|---|
| Windows **ya transcribe en vivo** contra el mismo backend | `DictadoSoniox` pide `POST /api/v1/transcription/session`, abre el WS a Soniox y manda PCM16 mono con el formato declarado. Probado con voz el 2026-08-14: «ciento veinte sobre ochenta» → `120/80` | `windows-client/src/Clinical/DictadoSoniox.cs:1-37` |
| Windows **no tiene identidad de usuario** | Se identifica con la `X-API-Key` de **máquina** + `X-Miracle-User-Email`. El popup de bienvenida pide nombre y correo **sin contraseña**: es una declaración, no un login | `src/Backend/BackendClient.cs:56-70` · `src/Ui/OnboardingWindow.cs:10-13` |
| Las rutas clínicas exigen **el JWT del médico** | `Authorization: Bearer <access_token de Supabase>`; `requireClinicalAuth` protege `/api/clinical/{templates,encounters,assistant,exports}` | `Graph/web/server.js:475-484` · `docs/backend-clinical-api-contract.md` (copia en el repo web) |
| La cadena completa de la web son **4 llamadas** | `POST /encounters` → `POST /:id/transcript` → `POST /:id/generate-note` → `GET /:id`. Nada más | `Pagina-web-clientes-final/lib/api/clinical.ts:675-714` |
| El dictado de Windows **solo habla Soniox** | `MensajeDeArranque` devuelve `""` si la sesión no trae `start_message`; una sesión Deepgram (`auth_scheme: "bearer"`) no arranca | `src/Clinical/DictadoSoniox.cs:273-276` |
| El motor de la web **sí habla los dos** | Deepgram: subprotocolo `[auth_scheme, access_token]` + `channel.alternatives[0].transcript`. Soniox: socket pelado + `start_message` + `tokens[]` | `lib/stt/deepgram-dictation.js:362-381` |
| El dictado de Windows **no guarda el verbatim** | Emite `Frase` y lo olvida. No hay texto completo que mandar a `/transcript` | `src/Clinical/DictadoSoniox.cs:234-241` |
| Ya hay micrófono PCM16 mono con AEC | `LiveAudio` abre WASAPI a `RitmoEntrada` Hz, 16 bits, 1 canal | `src/Voice/LiveAudio.cs:211` |
| Ya hay instalador y auto-update; el acceso directo **no está verificado** | Velopack instala en `%LocalAppData%\U` y el `.exe` ya lleva el icono, pero `vpk pack` corre **sin** `--shortcuts` | `RELEASING-WINDOWS.md` · `.github/workflows/windows-release.yml:102` |

**Lo que esto significa:** no hay que portar el motor de audio ni el de transcripción. Falta
**la identidad del médico** y **el hilo que va del micrófono al encounter**.

---

## Por qué esto va dirigido por especificación

Porque el sistema que se construye **se declara terminado a sí mismo en tres sitios distintos**, y
este repo ya pagó esa factura:

1. **La sesión** dirá que está viva porque tiene un token guardado — aunque esté vencido. Es el
   aprendizaje nº9 (*vacío no es ausente*) con otra ropa: «hay token» no es «hay sesión».
2. **La transcripción** dirá que mandó todo porque no falló ninguna llamada — aunque la última
   frase se quedara dentro. Es exactamente el patrón nº10 (*un paso no ejecutado deja rastro*), que
   en este repo ya produjo un `29/30` con 19 pasos comidos.
3. **El dictado** dirá que el backend no contestó cuando en realidad contestó *Deepgram* y nosotros
   solo sabemos leer Soniox. Es el aprendizaje nº2 (*un mensaje no debe concluir*) incumplido hoy,
   en código, en `src/Clinical/DictadoSoniox.cs:276`.

Los tres fallan **en verde**. Una prueba escrita después del código se escribiría para que pasara.

**La regla del flujo, y no tiene excepciones:** ninguna línea de producción entra antes que la
promesa que la juzga. Cada fase empieza con el contrato ROTO y termina con el contrato INTACTO.

---

## La especificación

Numeradas en continuación de la 83, que es la última que hay hoy. Los números no se reciclan.

| # | Promesa | Fase que la pone verde |
|---|---|---|
| 84 | **sin médico con sesión, la consulta no empieza: ni se abre micrófono ni se crea encounter** | 2 |
| 85 | un token vencido se renueva **antes** de usarse, no después de que el backend lo rechace | 1 |
| 86 | cerrar sesión no deja el token en disco | 1 |
| 87 | cada fallo del backend clínico dice **una cosa distinta**: `TRANSCRIPT_REQUIRED` no se cuenta igual que `LLM_NOT_CONFIGURED` | 3 |
| 88 | lo que se manda es **todo lo dicho**: la frase que quedó sin cerrar al parar también viaja | 4 |
| 89 | el lector se elige por **lo que dice la sesión**, no por lo que se compiló: `bearer` → subprotocolo y `channel.alternatives`; `message` → `start_message` y `tokens` | 5 |
| 90 | la consulta se atribuye **al médico que entró**, no a la máquina | 6 |
| 91 | si la nota no se generó, la consulta **no** se declara terminada | 7 |

**La que cierra el asunto es la 84.** Mientras no exista, todo lo demás es cosmético: sin el JWT del
médico las rutas `/api/clinical/*` devuelven 401, así que no hay encounter, no hay nota, y «misma
base de datos, mismo todo» es una frase y no un hecho. Las otras siete protegen el camino; la 84 es
la que lo abre.

### Con qué se juzga cada una

Las ocho son **mapa a mano dentro de la propia prueba** — ninguna necesita red, micrófono ni
pantalla, y esa es la razón de estar escritas así:

| # | Cómo se juzga sin tocar nada |
|---|---|
| 84 | un `Consulta` con sesión vacía: se le pide empezar y se comprueba que ni pidió sesión STT ni llamó a `/encounters` |
| 85 | reloj falso + HTTP falso: token con `expires_at` a 10 s → la siguiente llamada trae `Authorization` con el token **nuevo**, y hubo exactamente un `refresh_token` |
| 86 | se guarda, se cierra sesión, se mira el archivo: no existe |
| 87 | los siete códigos del contrato como JSON de entrada → siete mensajes distintos (`Distinct().Count() == 7`) |
| 88 | se alimentan tokens finales **sin** `<end>` y se para: el verbatim los contiene |
| 89 | dos sesiones de ejemplo (una de cada proveedor) → dos lectores distintos, y el de Deepgram declara el subprotocolo |
| 90 | un JWT de ejemplo → la cabecera `X-Miracle-User-Id` lleva el `sub`, no el correo ni el id de máquina |
| 91 | `generate-note` que devuelve 502 → el estado NO es `completed` y el motivo se puede nombrar |

Nada de esto necesita fixture de bronce ni escenario de CI local. **La ventana y el acceso directo
no tienen promesa a propósito**: son nivel 4 de la compuerta y se prueban a mano, con el log pegado
en el PR (§*Lo que no entra*).

---

## Las fases

Ocho fases de media jornada son **cuatro días**, y una rama de este repo dura de medio día a tres.
Se dice ahora y no al final: si la 1→4 tarda más de lo previsto, **la rama se corta después de la
fase 4** (identidad + cliente clínico + verbatim), se mergea eso, y las fases 5-8 abren rama nueva
desde `main` fresco. Lo que no se hace es estirar la rama a dos semanas.

### Fase 0 — las ocho promesas, en rojo

| | |
|---|---|
| **Promesa que pone verde** | ninguna: las pone todas en ROJO |
| **Qué toca** | `tests/ContratoDelGrafo/Contrato.cs` |
| **¿Núcleo congelado?** | no |
| **Terminado** | `CONTRATO ROTO: 8 promesa(s) incumplida(s)`, y cada `PENDIENTE` nombra la clase que todavía no existe |

Las capacidades que aún no existen se piden **por nombre y con reflexión**, como manda
`.claude/rules/flujo-sdd.md`, para que el contrato siga compilando contra el núcleo de hoy.

### Fase 1 — la sesión del médico vive entre arranques, y se renueva sola

| | |
|---|---|
| **Promesa que pone verde** | 85 y 86 |
| **Qué toca** | `windows-client/src/Cuenta/SesionMiracle.cs` (nuevo) |
| **¿Núcleo congelado?** | no |
| **Terminado** | 85 y 86 verdes, 21-83 intactas |

`POST {SUPABASE_URL}/auth/v1/token?grant_type=password` con la publishable key (que es pública por
diseño — ya viaja en el `.env` del portal). Se guardan `access_token`, `refresh_token` y
`expires_at`. El refresco es **por adelantado, con margen**: no se espera al 401. En disco,
`%APPDATA%\U\sesion.dat` cifrado con **DPAPI** de usuario, que es lo que ya usa Windows para esto y
no añade dependencia.

> **Por qué el margen y no el 401:** un refresco reactivo convierte cada expiración en una llamada
> fallida visible, y en mitad de una consulta eso es una frase perdida. Se mide con reloj falso,
> no con esperas.

### Fase 2 — el popup de bienvenida se vuelve un login de verdad

| | |
|---|---|
| **Promesa que pone verde** | 84 |
| **Qué toca** | `src/Ui/OnboardingWindow.cs` → `src/Ui/LoginWindow.cs` · `src/Onboarding/Presentacion.cs` |
| **¿Núcleo congelado?** | no |
| **Terminado** | 84 verde; entrar con `medico@miracle.app` trae el `full_name` real desde `profiles` |

**Se conserva el diseño tal cual** —la tarjeta negra-azulada, el triángulo, el acento azul
eléctrico, `CornerRadius 18`, la sombra— y se cambia lo que pide: correo y **contraseña** en vez de
nombre y correo. El nombre deja de teclearse porque ya lo sabe la base: viene de `profiles`.

Esto **reemplaza por completo** la identidad anterior. `Config.Email` deja de ser algo que el
usuario escribe y pasa a ser lo que dice el token.

### Fase 3 — el cliente clínico, con los errores que sí distinguen

| | |
|---|---|
| **Promesa que pone verde** | 87 |
| **Qué toca** | `src/Clinical/ClinicaClient.cs` (nuevo) |
| **¿Núcleo congelado?** | no |
| **Terminado** | 87 verde; las cinco llamadas del contrato responden contra el backend real |

El equivalente en C# de `lib/api/clinical.ts`, recortado a lo mínimo: `GET /templates`,
`POST /encounters`, `POST /:id/transcript`, `POST /:id/generate-note`, `GET /:id`. Todas con
`Authorization: Bearer` de la fase 1.

El envelope `{ error: { code, message } }` se traduce a un error tipado. **Siete códigos, siete
mensajes** — y la promesa 87 lo mide contándolos, porque un `catch` que dice «no se pudo generar la
nota» para los siete es el aprendizaje nº2 escrito otra vez.

### Fase 4 — el verbatim completo, incluida la última frase

| | |
|---|---|
| **Promesa que pone verde** | 88 |
| **Qué toca** | `src/Clinical/DictadoSoniox.cs` |
| **¿Núcleo congelado?** | no |
| **Terminado** | 88 verde |

Hoy `Frase` se emite y se olvida (`DictadoSoniox.cs:234-241`). Se añade el acumulador del verbatim,
y `SoltarLoQueQuede()` —que ya existe— pasa a tener consecuencia: lo que Soniox no llegó a cerrar
entra igual, porque **se dijo**.

### Fase 5 — el lector se elige por la sesión, no por el nombre del archivo

| | |
|---|---|
| **Promesa que pone verde** | 89 |
| **Qué toca** | `src/Clinical/DictadoSoniox.cs` → `src/Clinical/DictadoEnVivo.cs` |
| **¿Núcleo congelado?** | no |
| **Terminado** | 89 verde |
| **Sitios con esta clase de error** | 1 (contado con grep sobre `websocket_url` en `windows-client/`) |

Se replica lo que ya hace el motor de la web: `auth_scheme`/`provider` deciden. Deepgram abre con
subprotocolo y lee `channel.alternatives[0].transcript`; Soniox abre pelado, manda `start_message` y
lee `tokens[]` con `<end>`/`<fin>`.

> Hoy, si el *provider studio* se cambia a Deepgram, el dictado de Windows muere diciendo «el
> backend no devolvió la configuración del stream» — un mensaje que no distingue «es Deepgram» de
> «el backend falló». La 89 cierra las dos cosas a la vez.

### Fase 6 — la consulta es del médico, no del ordenador

| | |
|---|---|
| **Promesa que pone verde** | 90 |
| **Qué toca** | `src/Cuenta/SesionMiracle.cs` · `src/Clinical/DictadoEnVivo.cs` · `src/Backend/BackendClient.cs` |
| **¿Núcleo congelado?** | no |
| **Terminado** | 90 verde; en el ledger de consumo los minutos salen a nombre del médico |

La sesión STT se pide con `X-Miracle-User-Id: <sub del JWT>`, que es **exactamente** lo que manda la
web (`app/api/stt/session/route.ts:55`). Hoy Windows manda `X-Miracle-User-Email`, y el backend
valida el uuid contra `profiles`: con el correo, la atribución no cuadra con la de la web.

### Fase 7 — la consulta sabe en qué punto se quedó

| | |
|---|---|
| **Promesa que pone verde** | 91 |
| **Qué toca** | `src/Clinical/Consulta.cs` (nuevo) |
| **¿Núcleo congelado?** | no |
| **Terminado** | 91 verde |

La máquina de estados del encounter, con los mismos nombres del contrato del backend:
`created → transcript_ready → note_generating → note_generated`. Un `generate-note` que devuelve 502
deja la consulta en un estado que **se puede nombrar**, y el texto no se pierde: el `encounter_id`
ya existe, así que reintentar no duplica nada.

### Fase 8 — la ventana y el icono del escritorio

| | |
|---|---|
| **Promesa que pone verde** | ninguna: nivel 4, se prueba a mano |
| **Qué toca** | `src/Ui/ConsultaWindow.cs` (nuevo) · `App.xaml.cs` · `.github/workflows/windows-release.yml` |
| **¿Núcleo congelado?** | no |
| **Terminado** | log pegado en el PR con hora: entrar → grabar → hablar → parar → nota, y la consulta visible en la web |

Una sola ventana, cuatro cosas y nada más:

```
┌────────────────────────────────────┐
│  Dr. <nombre>                    ⏻ │
│  [ Consulta inicial · Med. gral ▾] │
│                                    │
│              ●  GRABAR             │   ← el botón es el 60 % de la ventana
│                                    │
│  ────────────────────────────────  │
│  lo que se va oyendo, en gris      │
│  lo confirmado, en blanco          │
└────────────────────────────────────┘
```

Al parar: manda el verbatim, pide la nota, y la pinta por secciones. Nada más — ni editar, ni
firmar, ni pacientes (§*Lo que no entra*).

**El icono:** `U.exe --consulta` abre esta ventana al frente, y el acceso directo del escritorio
apunta ahí. La carita sigue existiendo y sigue haciendo lo suyo: es el mismo proceso, no se le quita
nada.

> **Sin verificar, y por eso es trabajo de esta fase y no un supuesto:** `vpk pack` se invoca sin
> `--shortcuts` (`.github/workflows/windows-release.yml:102`), así que **no está comprobado** que el
> instalador deje hoy un icono en el escritorio. Se mira en una máquina limpia antes de dar la fase
> por hecha; si no lo deja, se declara explícito. Para probar sin instalar nada, la fase deja además
> un `.\scripts\atajo-consulta.ps1` que crea el `.lnk` apuntando al build local.

> **Supuesto declarado, porque la petición admite dos lecturas.** «Un icono en el escritorio donde
> al abrirlo esté la interacción principal para empezar a grabar» puede querer decir *el* icono de
> Ü (y entonces la carita deja de ser lo primero) o *un* icono nuevo. Se implementa como un icono
> que abre la consulta **sin desplazar la carita**, porque es reversible con una línea; si la
> intención era reemplazar el arranque, se cambia `App.xaml` y ya.

---

## A dónde va la nota después (la experiencia completa, dicho el 2026-09-01)

Esta spec cierra la cadena mínima, pero su salida no es una pantalla: es **el insumo del cierre de
la experiencia completa**, que el usuario definió así:

> El usuario **enseña** a usar un app con recuerdos → **activa la nota clínica** → los campos se
> **rellenan en SAP con batches**.

El reparto, con lo que ya existe:

| Capa | Quién la hace | Estado |
|---|---|---|
| **Guía** — dónde va cada dato en la UI | los **recuerdos** grabados sobre elementos (promesas 48-55) | construida |
| **Ejecución** — llenar los campos de verdad | la **navegación por batches** (promesas 56-59) + `RellenadorSap` | construida |
| **Materia prima** — la nota organizada | **esta spec** | en curso |

La consecuencia arquitectónica para esta rama es una sola, y es deliberada: **`Consulta` expone la
nota terminada como resultado consumible** (`NotaClinica` con sus secciones tipadas `clave → texto`),
no como texto pintado en una ventana. El puente nota→recuerdos→batch es la spec siguiente; lo que a
esta le toca es no cerrarle la puerta.

Y la transversalidad se respeta donde ya se respetaba: la nota no sabe de SAP —igual que el portal
no sabe de SAP— porque SAP es solo la superficie en la que hoy nos hiper-enfocamos. El acoplamiento
concepto↔campo vive en los recuerdos, que es exactamente el reparto que `CLAUDE.md` ya documenta
para el puente con el portal clínico.

## Lo que NO entra

| Qué | Por qué |
|---|---|
| **Login con Google** | El grant de contraseña se prueba hoy con las cuentas que ya existen. Google necesita PKCE, listener en loopback y deep-link: es una fase propia, no un «ya que estoy». |
| **Editar y firmar la nota** | Decisión del usuario: cadena mínima primero. `PUT /:id/note` es una llamada más cuando se quiera. |
| **Pacientes, CIE-10/CUPS, exportar a HC** | Es reescribir el portal en WPF. La exportación, además, **ya funciona** por el otro carril (`EjecutorDeExportaciones`, por `X-API-Key`). |
| **Sesión STT con el JWT del médico** | Hoy se pide con la `X-API-Key` de plataforma, que **ya viaja en el `.exe`** desde antes de este plan: esto no expone nada nuevo. Pero el sitio correcto es una ruta de Graph que acepte el Bearer del médico, y Graph es otro repo. Anotado abajo. |
| **Subir una grabación existente** | La web lo hace (`transcribe-audio-file.ts`). En Windows no se pidió. |

---

## Hallazgos

0. **`update_own_profile` estaba ROTO en producción, y no era nuestro** (2026-09-01). Al construir
   el guardado del nombre, la RPC contestó `400 · 42703 · column "updated_at" of relation
   "profiles" does not exist`. La función escribía `updated_at = now()` y esa columna **nunca
   existió**: contado con grep sobre `supabase/migrations`, se referencia en **un solo sitio** —esa
   función— y ninguna migración la crea.

   No bloqueaba solo a Windows: la pantalla de Configuración de cuenta del **portal web** usa la
   misma RPC, así que ningún médico podía editar su perfil desde ningún sitio. Arreglado quitando
   la línea (nadie lee ese dato; crear una columna para que una función deje de fallar sería
   inventar esquema para tapar un error de escritura) y verificado extremo a extremo con el JWT de
   `medico@miracle.app`: `HTTP 204`, el nombre cambió, se revirtió, y la especialidad y el país
   quedaron intactos — que es justo lo que la promesa 100 protege.

   **Queda fuera de este repo**: el arreglo se aplicó a la base de Supabase, pero la migración
   versionada vive en `Pagina-web-clientes-final/supabase/migrations/`. Ahí hay que replicarlo o el
   siguiente `db reset` lo revierte.

> Se rellena durante la implementación. Lo de aquí salió de la medición del 2026-09-01, **antes** de
> escribir código — que es justo para lo que sirve esta etapa.

1. **Un médico sin suscripción activa podría transcribir desde Windows** (2026-09-01). El portal
   comprueba el derecho comercial en su propio proxy (`requireEntitledApiUser` →
   `current_org_has_access()`, `lib/api/guard.ts:36`) y devuelve **402** antes de tocar el
   proveedor. El `.exe` habla **directo** con Graph, así que no pasa por esa comprobación. No es un
   detalle de este plan: es un agujero de cobro que este plan **ensancha**, porque abre un segundo
   camino a un servicio que cuesta por minuto. La comprobación tiene que vivir en Graph, no en cada
   cliente — el portal no puede ser el único que cierra la puerta.

2. **La regla `.claude/rules/solo-mac.md` prohíbe tocar `windows-client/`** y este trabajo la
   contradice entera. El usuario lo pidió por su nombre, así que queda superada **para esta rama**;
   pero un agente futuro leerá la regla y no esta conversación. Hay que actualizarla o fecharla, o
   volverá a bloquear a alguien sin que nadie sepa por qué.

3. **`DictadoSoniox` se llama por su proveedor** y por eso nadie notó que solo habla uno. El nombre
   del archivo estaba describiendo una limitación como si fuera una decisión. Es la razón de que la
   fase 5 renombre a `DictadoEnVivo`: mientras se llame Soniox, «no arranca con Deepgram» parece
   correcto.

4. **La atribución de Windows y la de la web no cuadran**: la web manda `X-Miracle-User-Id` (uuid) y
   Windows manda `X-Miracle-User-Email`. Los dos lados creen que están atribuyendo, y el backend
   valida el uuid contra `profiles`. Es el aprendizaje nº16 —*una comparación entre identidades de
   distinta forma da falso siempre, y en silencio*— por quinta vez en este repo.

---

5. **El sabotaje de la 84 descubrió una defensa doble** (2026-09-01, durante la fase de romper a
   propósito). Quitar la puerta de `Consulta` NO puso la promesa en rojo: `ClinicaClient` también se
   niega a llamar sin token, y el comportamiento prometido se sostuvo con una sola de las dos capas.
   Hubo que tumbar las dos para ver el rojo — que llegó, con su afirmación exacta («se pidieron 1»).
   Las otras siete mordieron al primer corte. El sabotaje completo: 8/8 comprobadas.

6. **PowerShell 5.1 lee los `.ps1` sin BOM como ANSI** (2026-09-01): un guión largo UTF-8 en
   `atajo-consulta.ps1` partió el parser con un error que no menciona la codificación. Por eso los
   scripts del repo no llevan acentos; ahora está escrito en el propio script.

## Cierre

- [x] Las ocho promesas verdes (`.\scripts\contrato-del-grafo.ps1` → CONTRATO INTACTO, 91/91) —
      y las ocho **vistas en rojo por sabotaje** antes del verde (hallazgo 5)
- [x] `.\scripts\verificar.ps1` pasa, con evidencia en `out\evidencia.md`
- [x] Probado a mano: **la app arranca con `--consulta` y muestra el login** (proceso vivo, ventana
      «Miracle», 2026-09-01) + **tres sondas contra el backend real**: Supabase auth → 400 con
      credencial mala; Graph `/api/clinical/templates` → envelope `UNAUTHORIZED` sin token y con
      Bearer basura — la forma exacta que `ClinicaClient` espera
- [ ] **PENDIENTE, y se dice como tal**: la corrida entera con cuenta real (entrar → grabar → nota →
      verla en el portal). Teclear la contraseña es del usuario; el flujo queda listo en el icono
      del escritorio. Es UNA pantalla arrancada, no dos — el aprendizaje nº9 aplica y por eso este
      cajón queda abierto
- [x] Aviso en `#miracle-updates` con `/avisa` (al push del PR)
- [x] Estado de este documento: **implementado** (2026-09-01)
