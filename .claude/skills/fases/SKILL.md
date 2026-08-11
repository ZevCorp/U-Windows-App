---
name: fases
description: Parte una especificación en fases ordenadas, cada una con la promesa que pone verde, el archivo que toca y su criterio de terminado. Es la etapa 2 del flujo SDD, va después de /especifica y antes de /promesas. Úsala cuando exista una spec en docs/specs/ y haya que decidir el orden de implementación, o cuando el usuario diga "haz el plan", "en qué fases", "por dónde empiezo".
---

# Etapa 2 — Partir en fases

Entrada: una spec en `docs/specs/`. Salida: la tabla de fases **dentro de esa misma spec** (no un
documento aparte: un plan que vive lejos de sus promesas se desincroniza).

## Qué es una fase

**Un commit que pone verde una promesa concreta sin romper ninguna anterior.** Si no puedes nombrar
qué promesa pasa a verde, no es una fase: es «trabajo».

| Campo | Regla |
|---|---|
| **Promesa que pone verde** | exactamente una, por número. Dos promesas = dos fases |
| **Qué toca** | los archivos, nombrados. Si aparece `SurfaceMap.cs`, la fase necesita al dueño (ver [`nucleo-congelado.md`](../../rules/nucleo-congelado.md)) |
| **Terminado** | «promesa N verde, 1..N-1 intactas». Nada de «funciona bien» |
| **Tamaño** | medio día. Si no cabe, son dos fases |

## El orden

1. **Arnés primero.** Si alguna promesa no se puede escribir con lo que hay (falta el fixture, falta
   una sonda), esa es la fase 0. Sin ella las demás no se pueden ni poner en rojo.
2. **Después lo que desbloquea.** La capacidad de la que dependen otras promesas (`Bronce.De` antes
   que «el disco no mezcla»).
3. **Al final lo que cierra el asunto.** En el plan de la plata era la 16 — «sin cromo derivado no
   hay atajo»—: mientras no existiera, plata era cosmética por definición, se podía borrar entera y
   no se rompía nada. **Identifica cuál es esa promesa y no la dejes fuera del alcance.**

Las que ya nacen verdes no llevan fase: se marcan «ninguna: ya se cumple; se congela».

## Toda la spec en una rama

Una rama = una feature = **una spec entera**. Las fases son commits dentro de ella. No se mergea fase
a fase: una rama que llega a `main` con promesas pendientes deja `main` rojo para los tres.

Si la spec no cabe en tres días, se parte en **dos specs** con promesas propias — no en una rama larga.

## Riesgos, y qué los desactiva

Por fase, si aplica: **en cuántos sitios más vive la clase de error** que vas a tocar (patrón nº5:
el veredicto rojo falso se arregló para árboles y reapareció idéntico en el grid media hora después).
Cuéntalos ahora con `grep`, no después: el número decide si la fase es una o son tres.

Y si el cambio elimina una limitación que estaba documentada: la fase **borra la maquinaria de
compensación**, no la parchea (patrón nº6). Escríbelo así en la fase, con los archivos a borrar.

## Presentar

Tabla de fases, cuál necesita autorización del dueño, y en qué orden se ven los rojos volverse verdes.
Después: `/promesas`.
