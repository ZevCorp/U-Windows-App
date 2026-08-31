# Aquí solo se toca Mac

> Regla del proyecto desde el 2026-08-18. Nace de que la carita nació en Windows y el trabajo se
> mudó a `mac-client/`: mientras dure esa mudanza, un arreglo «de paso» en el lado Windows es un
> conflicto con la rama de otro y un binario que nadie en esta máquina puede compilar ni probar.

## La regla

**El agente escribe en `mac-client/` y en nada más.** Si un cambio parece necesitar tocar otra
carpeta, se dice y se para — no se toca y se avisa después.

| Se puede escribir | Nunca, sin que el dueño lo pida por su nombre |
|---|---|
| `mac-client/**` | `windows-client/**` |
| `docs/specs/**` (la spec del trabajo en curso) | `windows-graph/**` |
| `.claude/rules/**`, `CLAUDE.md` (cuando se pida) | `tests/ContratoDelGrafo/**` |
| | `scripts/*.ps1` · `versiones/**` |
| | `WINDOWS.md` · `RELEASING-WINDOWS.md` · `PRODUCTION.md` |
| | `backend/**` · `agente-arquitecto/**` · `graphify-out/**` |

## Por qué, y no es orden por gusto

1. **No se puede verificar.** La compuerta de Windows —`verificar.ps1`, `contrato-del-grafo.ps1`,
   `ci-terreno.ps1`— es PowerShell + .NET sobre un escritorio con SAP GUI. En este Mac **no corre
   ninguno de los cuatro niveles**. Un cambio en `windows-client/` desde aquí entra a ciegas, y eso
   es exactamente lo que la compuerta existe para impedir.
2. **Es la zona de choque.** `CLAUDE.md` ya lo dice: la UI de `windows-client` es riesgo **alto** —
   es lo que tocan los tres. Editarla desde la rama de Mac es abrir una feature paralela sobre
   trabajo ajeno sin hablarlo.
3. **El contrato es .NET.** `tests/ContratoDelGrafo/` es el juez del núcleo de Windows. Aquí no
   compila; tocarlo sería cambiar lo que promete un sistema que esta máquina no puede juzgar.

## Qué sí se hace en vez de tocar Windows

- Si Mac necesita algo que hoy vive en Windows: **se reescribe en Swift dentro de `mac-client/`**.
  Se copia el *comportamiento* y el *porqué* documentado, no el archivo.
- Si de verdad hay que cambiar el lado Windows: se anota en la spec bajo «lo que queda fuera», con
  el archivo y el motivo, y lo abre una rama propia de quien tenga esa zona.

## Su límite honesto

Esto es una regla de conducta del agente, no un candado: no hay hook que lo imponga, y un `git add`
demasiado ancho puede colar un archivo de Windows en un commit de Mac. Antes de commitear,
`git status` y mirar que todo lo listado cuelgue de `mac-client/`.
