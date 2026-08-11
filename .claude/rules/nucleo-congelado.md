# El núcleo está congelado

Desde el 2026-08-08, `SurfaceMap` es la base sobre la que se construye el resto de la app y está
**congelado**: se puede tocar —hay candado, no muralla— pero cada cambio pasa por una decisión humana
consciente y sale del contrato en verde.

## Lo protegido

Lo vigila [`.claude/hooks/guardia-nucleo.ps1`](../hooks/guardia-nucleo.ps1) como hook `PreToolUse`
sobre `Edit|Write`. **Exige contraseña del dueño en un popup**:

- `windows-client\src\Navigation\SurfaceMap.cs`
- `tests\ContratoDelGrafo\*`
- `.claude\hooks\guardia-nucleo.ps1` y su `nucleo-clave.hash`
- `.claude\settings.json` — el interruptor también se protege: un candado cuyo interruptor se puede
  apagar no es un candado.

**Vetado incluso con contraseña** (el agente no puede ni pedir permiso):

- `versiones\nucleo\*.cs` · `C:\U-versiones\versiones.json` · `C:\U-versiones\*version.txt`

La única vía de editar el núcleo es el archivo de trabajo, que por construcción **es** la versión que
el dueño eligió con `scripts\version-nucleo.ps1 -Editar N`. Cambiar de versión en edición es un acto
del dueño, jamás del agente.

## Declarar antes de pedir permiso

Autorizar «se va a editar `SurfaceMap.cs`» no autoriza nada: dice el archivo, no el cambio. **Sin
declaración fresca el hook bloquea.**

Antes de tocar el núcleo, escribir en `C:\U-versiones\intencion.txt`:

```
Qué se cambia:  <la línea, el método, el comportamiento>
Por qué:        <el síntoma medido, con fecha; no la hipótesis>
Qué promesa lo juzga: <número en Contrato.cs>
Qué NO cambia:  <lo que se deja igual a propósito>
```

- **UTF-8.** El hook lo lee con `-Encoding UTF8`; escrito en otra codificación, «añadir» sale
  «aÃ±adir» en el popup — y una descripción que se lee mal es una descripción que no se lee.
- **Caduca a los 20 minutos.** La ventana deja que un mismo cambio abarque varias ediciones; pasada,
  se vuelve a decir a qué se viene.
- El dueño ve ese texto al teclear la contraseña. Escríbelo para él, no para el expediente.

## Después de tocarlo

```powershell
.\scripts\contrato-del-grafo.ps1     # medio minuto; encuentra solo lo que a mano cuesta días
```

Y en la duda, siempre. También cuando el cambio «no toca el núcleo pero pasa cerca».

## Su límite honesto

Esto protege de un **agente** usando Claude Code sobre este repo. No protege de una edición humana
con otro editor ni de borrar los archivos por fuera — eso ninguna configuración de hooks lo impide.
Es fricción deliberada para el agente, no un candado de sistema de archivos.

Cada máquina crea **su propia** contraseña la primera vez; el hash vive junto al script y **nunca se
commitea** (`.gitignore`). Un desarrollador nuevo queda protegido de su propio agente desde el primer
clone, sin heredar ni conocer la contraseña de nadie.
