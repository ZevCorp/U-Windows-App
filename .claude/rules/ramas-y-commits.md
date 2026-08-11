# Ramas, commits y el tamaño del trabajo

Resumen operativo de [`CLAUDE.md`](../../CLAUDE.md) §*Ramas*. Lo que un agente tiene que respetar
sin preguntar.

## La rama

```
<persona>/<que-hace>     jose/puente-portal-clinico · jero/carrera-del-busy · pipe/inventario-accionable
```

`jero`, `jose` o `pipe` en minúscula, y después **el resultado** en kebab-case. El prefijo es dueño
de *la rama*, no del código.

- **Nace** siempre desde `main` fresco: `git checkout main && git pull origin main && git checkout -b jose/lo-que-sea`.
  **Nunca desde la rama anterior.**
- **Vive** de medio día a tres días. Una vez al día: `git pull --rebase origin main`.
- **Una rama es una feature — ni media, ni dos.** Al cambiar de feature se cambia de rama, aunque la
  anterior no esté terminada (`git commit -am "wip: hasta donde llegué"`, push, y la siguiente desde `main`).
- **Muere** al mergear. `git branch -d` en minúscula: si grita, algo no se integró. No cambiarlo por `-D`.

**En el flujo SDD: una spec = una rama.** Las fases son commits dentro de ella. Se merge cuando
*todas* sus promesas están verdes, no fase a fase.

## `main` no se toca

`main` cambia **solo por merge de un PR**. Si `git status` dice `On branch main` y hay cambios,
te equivocaste de sitio:

```
git stash && git checkout -b jose/lo-que-sea && git stash pop
```

**Squash merge** por defecto. El PR va aunque lo mergees tú mismo cinco minutos después: es donde
queda escrito qué entró y por qué, y donde corre el contrato.

## El mensaje de commit

El repo tiene una voz y se mantiene: **`tipo(ámbito): lo que el sistema ahora hace, en español y en
minúscula`**. Describe el *resultado*, no el archivo tocado.

```
feat(bronce): el terreno guarda lo observado; lo declarado vive en su capa
feat(arquitecto): terminar deja de ser declarar, y pasa a ser entender
test(plata): las promesas de la derivacion, escritas antes que su codigo
fix(plata): el nivel se indexa por SELECTOR, y el mapa deja de salirse de la app
fix(nucleo): la profundidad se CALCULA entera; un atajo no dice a que hondura vive su destino
```

Tipos en uso: `feat`, `fix`, `test`, `docs`, `chore`, `wip`.

Lo que hace bueno a esos mensajes y hay que imitar:

- **Dicen el cambio de comportamiento**, no la edición. `feat(plata): añadir método Derivar` sería peor
  que el original aunque describa lo mismo.
- **Nombran lo que se acabó**: «y se acaba el segundo cálculo», «deja de cortarse en el mobiliario».
- El cuerpo, cuando lo hay, lleva **la medida**: cuántos sitios tenían la clase de error (patrón nº5),
  en cuántas pantallas se probó (aprendizaje nº9), qué promesa pasó a verde.

En el flujo SDD, el commit de una fase cita su promesa:

```
feat(plata): el mobiliario derivado abre rutas, y se acaba el segundo calculo

Promesa 16 en verde (sin cromo derivado no hay atajo). Contrato: 19/19, 0 pendientes.
Probado en explorer.exe y Configuracion — 2 pantallas, no 1 (aprendizaje nº9).
```

## Reparto: lo que de verdad evita choques

Una rama por persona evita pisarse la rama, no el archivo.

| Zona | Riesgo |
|---|---|
| `windows-graph/` | bajo — específico de SAP |
| `windows-client/` UI (carita, paneles, inspector) | **alto** — es lo que todos tocan |
| `windows-client/` servicios (voz, logging, release) | bajo |

**Que dos personas no tengan features abiertas en la UI a la vez.** Si es inevitable: pantallas
distintas y ninguna rama de más de un día.

## Lo que un agente no hace sin que se lo pidan

- Commitear o hacer push.
- Mergear a `main`, ni forzar nada (`push --force`, `-D`, `reset --hard`) sobre trabajo compartido.
- Abrir una rama paralela con trabajo que otro ya tiene abierto. Se habla.
