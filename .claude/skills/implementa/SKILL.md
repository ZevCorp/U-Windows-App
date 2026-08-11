---
name: implementa
description: Implementa UNA fase del plan hasta que su promesa del contrato pasa a verde sin romper las anteriores, siguiendo los patrones de desarrollo del repo. Es la etapa 4 del flujo SDD. Úsala cuando exista una spec con fases y el contrato en rojo, o cuando el usuario diga "implementa la fase N", "hazlo verde", "sigue con la siguiente fase".
---

# Etapa 4 — Implementar una fase

**Una fase por vez.** Terminada = su promesa verde y todas las anteriores intactas.

Antes de tocar nada: [`patrones-de-desarrollo.md`](../../rules/patrones-de-desarrollo.md). Son las
formas de fallo que este repo ya pagó, con fecha.

## 1. Situarse

- ¿La fase toca `SurfaceMap.cs` o `tests\ContratoDelGrafo\`? Entonces **declara la intención** en
  `C:\U-versiones\intencion.txt` (UTF-8, caduca a los 20 min) y avisa al usuario de que va a salir el
  popup de contraseña. Ver [`nucleo-congelado.md`](../../rules/nucleo-congelado.md).
- ¿El arreglo de fondo va en `windows-graph`? Casi siempre sí: es quien sabe de SAP. `windows-client`
  es presentación y diagnóstico.
- `graphify query "<qué toco>"` antes que grep, si el grafo existe.

## 2. Escribir el código mínimo que pone verde esa promesa

Nada más. Lo que «ya que estoy» no está en la spec, no entra en esta fase: entra en la lista de
hallazgos al final.

Lo que este repo exige mientras escribes:

- **Ningún `catch` mudo.** El motivo se reporta, y con la cadena entera de `InnerException`.
- **Ningún mensaje que concluya.** Describe el paso que falló y distingue sus causas: si el texto
  puede salir por dos motivos distintos, está mal escrito (patrón nº2, incumplido dos veces ya).
- **Ningún paso sin rastro.** Lo omitido emite `Omitted`; el denominador es el plan, nunca lo
  ejecutado (patrón nº10 — `29/30` con 19 pasos comidos).
- **Vacío no es ausente**: `string.IsNullOrWhiteSpace`, no `??`.
- **Nada estimado disfrazado de leído.** Si el dato es una estimación, la UI y el log lo dicen.
- **Nada anclado al nombre de la carpeta del repo.**
- COM: **enlace tardío**, y `SapSelector.Normalize(id)` antes de `FindById`.
- Comentarios: el **porqué con fecha y medida**, no el qué. Español.

Y las dos que cambian el tamaño del cambio:

- **Cuenta los sitios** que tienen la clase de error que arreglas (`grep`), no solo el que reportaron.
  El número va al commit.
- Si la fase demuestra falsa una limitación documentada, **borra la maquinaria de compensación** en
  vez de parchearla, y borra también su documentación.

## 3. Juzgar

```powershell
.\scripts\contrato-del-grafo.ps1
```

- [ ] **La promesa de esta fase, verde.**
- [ ] Las anteriores, intactas. Si una se rompió: es una regresión y se arregla ahora, no «después».
- [ ] Los `PENDIENTE` que quedan son exactamente los de las fases que faltan.
- [ ] **No se tocó el enunciado de ninguna promesa para que pasara.** Si una estorba, la conversación
      es sobre el contrato y con el dueño — cambiarla es cambiar lo que el grafo promete a todo lo
      que se construye encima.

Si la fase toca UI o SAP, además: correr `U.exe` sobre **≥2 pantallas** (aprendizaje nº9) y leer el
log —`%LOCALAPPDATA%\U\logs\u-AAAAMMDD.log`— antes de decir que funciona. Una captura no distingue
«correcto» de «otra caja pintada encima»; el log sí.

## 4. Commitear la fase

```
feat(<ámbito>): <lo que el sistema ahora hace, en minúscula, en español>

Promesa <N> en verde (<enunciado corto>). Contrato: <X>/<Y>, <Z> pendientes.
<La clase de error vivía en N sitios; los N corregidos.>
<Probado en <app 1> y <app 2>.>
```

## 5. Reportar

Al usuario, en tres líneas: qué promesa pasó a verde, qué encontraste que no estaba en la spec, y qué
fase sigue. **Los hallazgos van a la spec**, no solo al chat: el plan se corrige con lo que se mide.

Cuando no queden fases: `/verifica`.
