# Este puente está DESACTIVADO — se conserva, no se usa

**2026-08-16, decisión del usuario.** Entre las dos formas de usar el collar Omi que
existen en este repo, la activa es la de dentro de `windows-client` (el botón del collar
en la carita, el gesto de mantener pulsado el micrófono, el enlace permanente). Este
puente aparte —collar → SDK oficial de Omi → VB-Cable → micrófono de Windows para
cualquier programa— queda **apagado pero intacto**, por si algún día conviene volver a él.

## Qué significa "desactivado" aquí, en concreto

- **Ningún proceso está corriendo.** Verificado al desactivarlo: cero procesos `python`
  en la máquina.
- **`arrancar.bat` sigue funcionando** si alguien lo ejecuta a mano — no se rompió nada
  a propósito. Desactivado quiere decir «no es el camino de hoy», no «no puede correr».
- **VB-Cable sigue instalado.** Es un driver de audio de verdad, no algo que este repo
  posea; desinstalarlo no es "código nuestro" y no se tocó. Si molesta verlo en la lista
  de dispositivos de sonido, se desinstala desde *Configuración → Aplicaciones* como
  cualquier programa — "VB-Audio Virtual Cable".
- **`.venv/` se queda como está**, con el SDK oficial de Omi (`omi-sdk`) ya instalado y
  con el arreglo de threading (`winrt.init_apartment(0)` en `puente.py`) ya resuelto y
  probado extremo a extremo el 2026-08-14: encontró el collar, decodificó Opus, entregó
  audio real a "CABLE Output" durante 40 s sin fallos.

## Para reactivarlo

```bash
puente-omi\arrancar.bat
```

Nada que reinstalar ni reconfigurar — quedó verificado y listo. Ver `puente.py` para el
porqué de cada decisión (el hilo COM en MTA, la búsqueda de "CABLE Input" por nombre y no
por índice, el UUID del servicio Omi para el rastreo).
