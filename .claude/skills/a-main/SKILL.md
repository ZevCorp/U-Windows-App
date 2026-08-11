---
name: a-main
description: Lleva una rama verificada a main de forma segura — comprobaciones previas, rebase sobre main fresco, PR con la tabla de evidencia y squash merge. Es la etapa 6 del flujo SDD. Úsala cuando el usuario diga "sube esto a main", "abre el PR", "mergea", "listo para integrar". No la uses si /verifica no está en verde.
---

# Etapa 6 — El paso a `main`

`main` roto bloquea a los tres. Esta skill **no mergea nada por su cuenta**: prepara, comprueba y
propone. El push, el PR y el merge los confirma el usuario.

Reglas: [`ramas-y-commits.md`](../../rules/ramas-y-commits.md) y [`compuerta-a-main.md`](../../rules/compuerta-a-main.md).

## 1. Preflight — todo esto antes de tocar la red

- [ ] **No estamos en `main`.** Si `git status` dice `On branch main` con cambios:
      `git stash && git checkout -b <persona>/<que-hace> && git stash pop`.
- [ ] **La rama sigue la convención** `<persona>/<que-hace>` (`jero`, `jose`, `pipe`). Si no, se
      renombra: `git branch -m jose/lo-que-sea`. (Ramas históricas fuera de convención se dejan estar.)
- [ ] **El árbol está limpio.** Nada sin commitear, nada `wip:` sin squashear.
- [ ] **La spec está al día**: los hallazgos de la implementación están escritos en ella, y su estado
      pasó de *propuesto* a *implementado*, con fecha.
- [ ] **`/verifica` en verde**, con `CONTRATO INTACTO` y **0 pendientes**. Una rama con promesas
      pendientes no va a `main`: va a la fase que falta.
- [ ] **No se coló nada que no se debe commitear**: `nucleo-clave.hash`, `.env`, `out/`, `bin/`,
      respaldos de graphify. `git diff --stat origin/main...HEAD` y míralo entero.

## 2. Ponerse al día

```powershell
git fetch origin
git pull --rebase origin main
```

Con `--rebase` la historia queda lineal y los conflictos llegan de a uno. **Y después del rebase se
vuelve a verificar**: `.\scripts\verificar.ps1`. Un rebase limpio no es un contrato verde — lo nuevo
de `main` puede romper una promesa tuya, que es justo para lo que existe el contrato.

## 3. El PR

```powershell
git push -u origin <persona>/<que-hace>
gh pr create --base main --title "<mismo estilo que el commit>" --body-file <cuerpo>
```

El cuerpo sale de [`.github/pull_request_template.md`](../../../.github/pull_request_template.md) y no
puede faltarle:

1. **Qué promete ahora el sistema que antes no**, con los números de promesa.
2. **La tabla de evidencia** de `verificar.ps1`, pegada literal.
3. **En cuántas pantallas se probó, con nombre.**
4. **En cuántos sitios vivía la clase de error** y cuántos se corrigieron.
5. **Lo que se dejó fuera**, y por qué.

Ese `git push` ya publica commits, así que **deja su aviso**: [`/avisa`](../avisa/SKILL.md) a
`#miracle-updates` con la rama y el link al PR. No se espera al merge para contarlo — el sentido del
aviso es que los otros dos se enteren *antes* de que llegue el conflicto.

CI corre el contrato en cada PR a `main` ([`contrato.yml`](../../../.github/workflows/contrato.yml)).
Si sale rojo allí y verde en local: no es «cosa del CI» — es una diferencia de máquina, y descubrirla
es el trabajo.

## 4. Mergear

- **Squash merge** por defecto: los doce `wip` entran como uno con mensaje limpio.
- Mergear tú mismo está bien **si nadie más tocó esos archivos**. Si el PR cruza territorio ajeno
  —sobre todo la UI de `windows-client`, que es lo que todos tocan— se pide ojo antes.
- Aceptar el borrado de rama que ofrece GitHub. Local: `git branch -d <rama>` (minúscula: si grita,
  algo no se integró; es una red, no un estorbo — no lo cambies por `-D`).

## 5. Después

```powershell
git checkout main && git pull origin main
graphify update .        # el grafo se queda al día, sin coste de API
```

Y decir al usuario, en una línea: qué entró, con qué evidencia, y qué queda pendiente de la spec.

El merge a `main` deja su aviso en Slack como cualquier otro push: correr [`/avisa`](../avisa/SKILL.md),
que publica en `#miracle-updates` con el título del PR, las promesas que pasaron a verde y el link
([regla](../../rules/aviso-en-slack.md)).

## Lo que nunca se hace sin que lo pidan

Commitear, pushear, mergear, `push --force`, `reset --hard`, `branch -D`, tocar `main` directamente.
