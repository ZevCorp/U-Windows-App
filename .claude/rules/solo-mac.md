# Cada máquina toca su lado

> El archivo sigue llamándose `solo-mac.md` por su nombre de nacimiento, y se deja así a propósito:
> lo citan `CLAUDE.md` y `mac-client/TRASPASO.md`, y renombrarlo obligaría a editar documentos del
> lado Mac desde una sesión de Windows — justo lo que esta regla dice que no se hace.

> Regla del proyecto desde el 2026-08-18, **acotada el 2026-09-01**. Nació como «aquí solo se toca
> Mac» y se escribió durante la mudanza de la carita a `mac-client/`, en una sesión que corría
> sobre un Mac. Redactada así, se leía como una prohibición del repo entero — y bloqueaba trabajo
> de Windows hecho **desde Windows**, que es donde esa prohibición no tiene ningún sentido.

## La regla, con su condición delante

**Lo que decide qué se puede tocar es la máquina en la que corre la sesión**, porque lo que decide
si un cambio se puede verificar es qué compuerta puede correr esa máquina.

| Sesión corriendo en | Escribe en | No toca sin que el dueño lo pida por su nombre |
|---|---|---|
| **macOS** | `mac-client/**` · `docs/specs/**` · `.claude/**` | `windows-client/**` · `windows-graph/**` · `nucleo/**` · `mapeador/**` · `voz/**` · `tests/**` · `scripts/*.ps1` · `versiones/**` · `backend/**` · `agente-arquitecto/**` |
| **Windows** | todo el lado Windows, con **la compuerta de cuatro niveles** (`.claude/rules/compuerta-a-main.md`) | `mac-client/**` — no compila aquí, y vale el mismo argumento al revés |

Si el trabajo está fuera de la columna que toca, se dice y se para: no se toca y se avisa después.

## Por qué, y no es orden por gusto

1. **Se toca lo que se puede verificar.** La compuerta de Windows —`verificar.ps1`,
   `contrato-del-grafo.ps1`, `ci-terreno.ps1`— es PowerShell + .NET sobre un escritorio con SAP
   GUI. **En un Mac no corre ninguno de los cuatro niveles**, así que un cambio en
   `windows-client/` hecho desde allí entra a ciegas — y eso es justo lo que la compuerta existe
   para impedir. **En Windows sí corren los cuatro**, y entonces el argumento desaparece: no queda
   ninguna razón para no tocar el lado Windows desde Windows.
2. **La zona de choque sigue siendo la misma.** `CLAUDE.md` lo dice: la UI de `windows-client` es
   riesgo **alto** — es lo que tocan los tres. Eso no lo arregla ninguna regla de plataforma; lo
   arregla no tener dos features abiertas en la UI a la vez y que ninguna rama pase de un día.
3. **El contrato es .NET.** `tests/ContratoDelGrafo/` es el juez del núcleo de Windows. En un Mac
   no compila; tocarlo desde allí sería cambiar lo que promete un sistema que esa máquina no puede
   juzgar. Desde Windows es exactamente al revés: **tocarlo es obligatorio**, porque ninguna línea
   de producción entra antes que la promesa que la juzga.

## Qué se hace en vez de cruzar

- Si Mac necesita algo que hoy vive en Windows: **se reescribe en Swift dentro de `mac-client/`**.
  Se copia el *comportamiento* y el *porqué* documentado, no el archivo.
- Si de verdad hay que cambiar el otro lado: se anota en la spec bajo «lo que queda fuera», con el
  archivo y el motivo, y lo abre una rama propia de quien tenga esa zona.

## Su límite honesto

Esto es una regla de conducta del agente, no un candado: no hay hook que lo imponga, y un `git add`
demasiado ancho puede colar un archivo del otro lado en un commit. Antes de commitear, `git status`
y mirar que lo listado sea de la columna que toca.

Y el límite que costó esta corrección: **una regla escrita durante un trabajo concreto tiende a
describir ese trabajo como si fuera el estado permanente del repo.** La versión anterior decía «aquí
solo se toca Mac» sin decir qué era «aquí», así que un agente en Windows la leía como una
prohibición y no tenía forma de saber que no le aplicaba. Si una regla prohíbe algo, tiene que
llevar delante **la condición bajo la que prohíbe**.
