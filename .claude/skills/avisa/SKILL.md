---
name: avisa
description: Envía al canal de Slack #miracle-updates el resumen de lo que acaba de subirse con push — qué entró, a qué rama y qué zona toca. Úsala inmediatamente después de cada `git push` que publique commits (rama de feature, merge a main o wip), o cuando el usuario diga "avisa al equipo", "manda el resumen a Slack", "cuéntale a Jero/Jose/Pipe".
---

# El aviso — lo que se subió, contado al equipo

Somos tres y `main` es de los tres. Un push del que nadie se entera es trabajo invisible hasta que
choca con el de otro. Esta skill convierte cada push en un aviso corto en `#miracle-updates`: qué
entró, en qué rama, y qué le cambia al que lo lea.

Regla asociada: [`aviso-en-slack.md`](../../rules/aviso-en-slack.md).

**El destino no se pregunta**: `#miracle-updates` (`C0BPJFZ0CBB`), siempre, por decisión del dueño
del 2026-08-11. Lo que sí se pregunta es el DM, que es adicional (paso 5).

## 1. Comprobar que hubo algo que avisar

Si el push dijo `Everything up-to-date`, **no hay aviso**. Un canal con avisos vacíos se silencia,
y entonces deja de avisar de lo que importa.

## 2. Reunir lo que se subió

El resumen se escribe sobre **lo que el push publicó**, no sobre lo que hay en el disco:

```bash
git rev-parse --abbrev-ref HEAD          # la rama, que es medio mensaje
git log --oneline origin/main..HEAD      # los commits que trae de nuevo
git diff --stat origin/main...HEAD       # el tamaño y la ZONA tocada
```

- **Rama de feature**: esos commits son el material.
- **Merge a `main`** (vía `/a-main`): el material es el PR — `gh pr view --json title,url,number`—
  con las promesas que pasaron a verde y la evidencia.
- **`wip:` de aparcar**: el aviso lo dice tal cual. «Aparqué X a medio hacer en tal rama» también
  es información que evita choques.

La **zona** sale del `--stat`, y es lo que decide si al lector le importa:

| Rutas tocadas | Zona a nombrar |
|---|---|
| `windows-graph/` | SAP — riesgo de choque bajo |
| `windows-client/src/...` de UI (carita, paneles, inspector) | **UI — riesgo alto: es lo que todos tocan** |
| `windows-client/` servicios (voz, logging, release) | servicios — bajo |
| `tests/ContratoDelGrafo/`, `SurfaceMap.cs` | **núcleo** — decirlo siempre |

## 3. Redactar

Voz del repo: **lo que el sistema ahora hace**, no los archivos tocados. Si no cabe en un vistazo
de Slack, sobra la mitad.

```
🔀 push a *<rama>*
*Qué entró:* <una frase en la voz de los commits: «el mobiliario derivado abre rutas, y se acaba el segundo cálculo»>
*Zona:* <windows-graph · UI de windows-client · servicios · núcleo>
*Estado:* <contrato 19/19 · o «contrato rojo: 3 pendientes, fase 2 en curso» · o «sin verificar»>
<link al PR, si existe>
```

Para un merge a `main`, la primera línea lo dice de golpe: `✅ *main* ← <título del PR> (#N)`.

Lo que **no** va: la lista de archivos, los hashes, el relato del proceso, ni una sola clave, ruta
de máquina o línea de log. Lo que **sí** va siempre: la rama y la zona.

Y el estado no se supone. Si no se corrió `verificar.ps1` ni el contrato, se escribe **«sin
verificar»** — nunca «verde». Un aviso que certifica lo que no se midió es el mismo vicio que el
contrato existe para impedir.

## 4. Enviar

`slack_send_message` con `channel_id: "C0BPJFZ0CBB"` (`#miracle-updates`). No hace falta preguntar
destino ni pedir el visto bueno del texto: la regla ya lo autorizó. Después, **enseñar al dev el
mensaje enviado y su link**, para que si algo salió mal lo vea al momento y no en el canal.

Parar y preguntar solo si aparece algo que la regla no cubre: material que no debería salir del
repo, un push que no es de este dev, o un aviso que habría que mandar a otro sitio.

Si el MCP de Slack **no está autorizado** en la sesión, no hay respaldo silencioso: se le dice al
dev que autorice el conector (claude.ai → conectores, o `/mcp` en una sesión interactiva) y se le
deja el mensaje redactado, listo para pegar. **Un aviso que parece enviado y no salió es peor que
ninguno.**

## 5. El DM, si de verdad le toca a alguien

Solo si el push le cambia el terreno a una persona concreta: tocaste archivos que tiene abiertos, o
el cambio desbloquea o bloquea su rama. Entonces, y solo entonces, `AskUserQuestion`:

> **Además del canal, ¿le mando DM a alguien?** — Nadie (por defecto) · Jero · Jose · Pipe

El DM **no sustituye** al canal, y su primera línea dice por qué le llega: «esto te toca porque…».
Interrumpir a una persona es decisión del dev, no de la skill.

## Lo que esta skill no hace

- Avisar de pushes que no publicaron nada.
- Mandar un DM sin preguntar.
- Publicar en `#miracle-team`: ese es el canal de conversación, no el del goteo de pushes.
- Afirmar un estado de verificación que nadie corrió.
