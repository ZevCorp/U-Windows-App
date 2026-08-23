# Qué protegemos, y cómo se trabaja limpio

El único sitio donde se responde «¿esto se puede romper sin que nos enteremos?» y «¿cómo llevo un
cambio a `main`?». Si estas preguntas aparecen en otro archivo, sobra ahí.

Documento **vivo**: la parte de decisiones al final está abierta a propósito. Lo que está medido
lleva su número; lo que está decidido lo dice; lo que falta por decidir está en §6 y no se
implementa hasta que se decida.

---

## 1. De dónde sale esto

El 2026-08-21, tras una tarde de fallos encadenados en la voz y en SAP, salieron dos quejas del
usuario que son la misma:

> «hay demasiada burocracia para pasar a main pero no creo que realmente me esté verificando lo que
> necesito. Ni siquiera sé qué es lo que está verificando en este momento ni sé qué es la compuerta»

Y la petición:

> «quiero tener en main únicamente el código estable real, con una carpeta de tests para verificar
> que no se hayan roto las funcionalidades. Y que si quiero empezar una funcionalidad nueva, no me
> deje hasta no cerrar la anterior»

La queja era correcta y el diagnóstico salió al medirlo: **no falta proceso, sobra proceso y falta
portero.**

---

## 2. Lo que hay hoy, contado

| zona | líneas | ¿tiene contrato? |
|---|---:|---|
| `src/Ui` — la carita, el notch, el primer encuentro | 14.748 | no |
| `src/Navigation` — `SurfaceMap`, el mapa de producción | 6.653 | **no** |
| `src/Uia` — leer y accionar la pantalla | 4.336 | no |
| `src/Mcp` — las herramientas que llama la voz | 3.705 | **no** |
| `src/Voice` — Gemini en vivo, micrófono, collar | 3.335 | solo el collar (7) |
| `src/Clinical` — historia clínica al dictado | 2.072 | no |
| `nucleo/` — el grafo | 1.614 | **sí, 20 promesas** |
| `mapeador/` — pulso, atribución del clic, candado | 1.233 | **sí, 24 promesas** |
| `src/Onboarding` | 772 | no |
| `src/Actions` — teclado, ratón, freno | 349 | no |
| `src/Update` — la actualización automática | 209 | no |

**2.847 líneas protegidas de 39.026. El 7%.**

Y en `.git/hooks/` solo hay `post-checkout` y `post-commit`: **ni un solo hook que impida nada.**
Los 6 archivos de `.claude/rules/` y las 7 skills describen el proceso, pero un documento es una
petición, no una garantía. Por eso se siente como papeleo: es papeleo sin portero.

---

## 3. Qué se rompió de verdad (2026-08-21)

No es una lista de riesgos imaginados. Son los cinco fallos de una tarde, con su coste medido en el
log:

| falló | vive en | costó |
|---|---|---|
| el gesto que funcionó no se guardaba: el doble clic acertaba y el mapa seguía anotando «clic» | `Navigation/SurfaceMap` | 13 s por intento, **cada vez** |
| `map_open_app` comparaba el *proceso* contra la *superficie*; en SAP nunca coinciden | `Mcp/SurfaceMapTools` | 14 s y un fallo falso |
| el turno del usuario no se cerraba nunca: terminabas de hablar y Ü seguía escuchando | `Voice/GeminiLive` | la conversación entera |
| Ü se silenció sola al creer que oía «cállate» en su propio eco | `Voice/GeminiLive` | muda hasta reactivarla |
| un clic que no movía nada no subía a doble salvo que el mapa ya lo supiera | `Mcp/SurfaceMapTools` | 2 clics muertos |

**Los cinco cayeron en `Navigation`, `Mcp` y `Voice`: 13.693 líneas con cero promesas. Ninguno cayó
en `src/Ui`, que es la zona más grande del repositorio.**

De ahí sale el criterio, y no de la intuición: **no se protege lo más grande, se protege lo que se
rompe en silencio.** Cuando la carita se ve mal, se ve. Cuando el mapa guarda mal un gesto, no lo
nota nadie hasta que alguien pierde trece segundos por intento.

---

## 4. El orden

1. **El mapa de producción** (`Navigation/SurfaceMap`) — *se rompió hoy.* Que cruzar una puerta
   guarde **cómo** se cruzó. Que corregir un destino no deje viva la arista vieja. Que una salida
   homónima no acabe en una pregunta sin respuesta posible.
2. **Identidad de superficie** (`Mcp/SurfaceMapTools`) — *se rompió hoy.* La familia que más ha
   vuelto: confundir el nombre del proceso con el nombre de la pantalla. Pasó con Chrome, con GitHub
   y hoy con SAP. Una promesa por cada forma de nombrar —proceso, dominio, sistema SAP— cierra la
   familia entera.
3. **El turno de la conversación** (`Voice/GeminiLive`) — *se rompió hoy.* Que un turno abierto
   tenga límite y se diga. Que una orden sobre Ü misma —callarse, cerrarse— exija que en lo dicho
   haya de verdad esa palabra; hoy bastaba con que el modelo lo creyera, y se lo creyó por un eco.
4. **Accionar la pantalla** (`Uia`) — que una acción se compruebe por consecuencia y no por «no
   falló». El principio ya está escrito por todo el código en comentarios; falta volverlo promesas
   que alguien pueda romper y ver rojo.
5. **La actualización** (`Update`) — 209 líneas, pero deciden si un usuario recibe lo que publicas.
   Si esto se rompe no lo ves tú: lo ve él, y no te lo cuenta.
6. **La interfaz** (`Ui`) — a mano, y está bien. Aquí una captura vale más que una promesa.

**Tamaño del trabajo, dicho antes de empezar:** hoy son 51 promesas para 2.847 líneas. Llevar esa
densidad a las 13.693 de los puntos 1-3 son del orden de **60-80 promesas nuevas**. Se hace por
partes; no es una tarde.

---

## 5. El flujo

Una sola rama larga. **`main` es la estable** — no se crea una rama «maestra» aparte. Dos ramas
largas es GitFlow, la opción pesada pensada para releases programados; para un equipo pequeño con
despliegue continuo la recomendación establecida es GitHub Flow, y añadir una segunda rama solo daría
dos sitios donde preguntarse cuál es el bueno.

```
rama          feat/lo-que-sea      ← corta, UNA sola cosa
   ↓
al empujar    pre-push             ← el portero, ~60 s
              ├── compila
              ├── pasan los contratos          51/51
              └── ¿esta rama añade promesas?
   ↓
              main                 ← solo lo que pasó por ahí
```

**Por qué un hook y no un `.md`.** Un documento se puede olvidar; un hook no se puede saltar. Todo
lo que TIENE que pasar va en `.git/hooks/`; los `.md` explican el porqué, que es lo que un hook no
puede hacer.

**Qué bloquea el portero** (decidido con el usuario, 2026-08-21):

- ✅ que compile y pasen los contratos
- ✅ que la funcionalidad traiga su propio test
- ❌ los escenarios sobre el escritorio real — **no bloquean** hasta que sean fiables (ver §6)

---

## 6. Lo que falta decidir

No se implementa nada de esto hasta que se decida. Están aquí para que no se pierdan.

### 6.1 ¿Un contrato por zona, o uno solo que crezca?

Hoy son tres proyectos separados (`ContratoDelGrafo`, el del mapeador, el de la voz). Seis serían
seis compilaciones en cada empujón. Uno solo es más rápido pero mezcla el núcleo congelado —que no
se toca— con lo que cambia cada día.

### 6.2 ¿Qué hacemos con los escenarios sobre el escritorio real?

Hay **uno solo** (`C:\U-versiones\escenarios\explorer.json`) para toda la app, y el 2026-08-21 se
quedó **13 minutos sin escribir una línea** y hubo que matarlo. El nivel que más tarda es el que
menos cubre. O se arregla y se amplía, o sale del camino — pero no puede seguir siendo un paso
obligatorio que nadie corre.

### 6.3 ¿Qué hacemos con el flujo SDD que ya existe?

Seis reglas en `.claude/rules/` y siete skills (`especifica`, `fases`, `promesas`, `implementa`,
`verifica`, `a-main`, `avisa`). Parte es bueno y parte no lo usa nadie. Conservarlo entero es seguir
cargando justo lo que hoy pesa.

### 6.4 El backend no está en esta lista, y debería

Todo lo anterior es del cliente Windows. El backend (Graph: Node + Express sobre Vercel, 18.229
líneas, con un anexo en Python) tiene su propio `tests/` y varios scripts de prueba, y nadie ha
mirado todavía qué cubren de verdad. Ver §7.

---

## 7. Dónde está lo demás

- **El mapa de qué proteger, en visual:** https://claude.ai/code/artifact/7c3b3d63-5940-4861-b2f8-7b0790b0c097
- **Cómo funciona el backend:** https://claude.ai/code/artifact/83115924-8cb2-4e84-9eaa-3d84d2ad84b6
- **El análisis completo del backend:** `Code/Graph/architecture-infrastructure.md`
- **Por qué va lento el mapeador:** [`velocidad.md`](velocidad.md)
- **La compuerta actual, la que hay que simplificar:** [`../.claude/rules/compuerta-a-main.md`](../.claude/rules/compuerta-a-main.md)

---

## 8. Lo que NO hay que volver a discutir

Cosas que ya se decidieron o se midieron, para no re-derivarlas:

- **No se crea una rama «maestra».** `main` es la estable. (§5)
- **No se protege por tamaño.** Se protege por dónde se rompe en silencio. (§3)
- **Un `.md` no puede impedir nada.** Lo que tiene que pasar va en un hook. (§5)
- **La interfaz no lleva contrato automático.** Cuando se ve mal, se ve. (§4.6)
- **Los contratos que ya existen funcionan.** 20/20, 24/24, 7/7 el 2026-08-21. El problema nunca fue
  que fallaran: fue dónde no estaban.
