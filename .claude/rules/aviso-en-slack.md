# Todo push deja un aviso en `#miracle-updates`

> Regla del proyecto desde el 2026-08-11. Somos tres sobre un repo donde la zona de choque real es
> la UI de `windows-client`: enterarse de un push *cuando llega el conflicto* es enterarse tarde.

## La regla

**Cada `git push` que publique commits termina con un aviso en el canal `#miracle-updates`**
(`C0BPJFZ0CBB`), redactado y enviado con la skill [`/avisa`](../skills/avisa/SKILL.md). Aplica a los
tres tipos de push:

| Push | Aviso |
|---|---|
| Rama de feature (`git push -u origin jose/...`) | qué hace ahora la rama y qué zona toca |
| Merge a `main` (etapa 6, `/a-main`) | qué entró a `main`, con promesas y link al PR |
| `wip:` de aparcar trabajo | qué quedó a medias y dónde |

Un push sin nada nuevo (`Everything up-to-date`) no genera aviso.

## El destino es fijo, y no se pregunta

`#miracle-updates` es el canal de **avisos**: goteo constante, se lee en diagonal, y por eso el
destino no se decide push a push. Preguntar cada vez convertía el aviso en una gestión, y una
gestión se acaba saltando.

- **`#miracle-updates`** — todos los pushes. Automático.
- **`#miracle-team`** (`C0BPHB1MKEE`) — la conversación del equipo. **No** recibe el goteo de
  pushes: un canal de avisos mezclado con conversación deja de leerse, y entonces no avisa de nada.
- **DM a una persona** (Jero, Jose o Pipe) — **adicional, no sustituto**, y ese sí se pregunta:
  interrumpir a alguien es una decisión del dev. Se usa cuando el push le cambia el terreno a esa
  persona en concreto —tocaste su zona, o el cambio desbloquea/bloquea su rama— y el mensaje dice
  por qué le llega a ella.

## Qué lleva el mensaje

Lo mínimo para que el que lo lea decida en un vistazo si le importa:

1. **A qué rama se subió** — y si es `main`, se ve de un golpe.
2. **Qué se subió**, en la voz de los commits: el cambio de comportamiento, no los archivos.
3. **Qué zona toca** — `windows-graph`, UI de `windows-client` (riesgo alto de choque), servicios.
4. **En qué estado quedó**: contrato verde/rojo, o «sin verificar» si no se corrió. Nunca se
   supone: un aviso que dice verde sin haberlo medido es peor que no avisar.

## Lo que el agente no hace

- Escribir en Slack nada que no esté ya en el repo: ni claves, ni rutas de máquinas, ni logs.
- Inventar el estado del contrato o las pantallas probadas. Lo que no se midió se dice sin medir.
- Mandar un DM sin preguntar.
- Avisar de un push que no publicó nada.

## Su límite honesto

Esto cubre los pushes **que hace el agente en una sesión de Claude Code**. Un `git push` tecleado a
mano en otra terminal no dispara nada — ninguna configuración de este repo puede enterarse. Si el
aviso tiene que ser incondicional para los tres, el sitio es un workflow de GitHub Actions sobre el
evento `push`, que sí ve todos. Hoy **no está montado**, y conviene no confundir una cosa con la
otra: el canal no es el registro completo de lo que entra al repo, es lo que el agente publicó.
