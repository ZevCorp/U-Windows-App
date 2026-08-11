---
name: verifica
description: Corre la compuerta de validación y verificación de la rama —compila, contrato del grafo, escenarios de CI local y corrida a mano— y produce la tabla de evidencia que exige el PR. Es la etapa 5 del flujo SDD y el paso obligatorio antes de tocar main. Úsala cuando el usuario diga "verifica", "está listo para main", "corre las pruebas", "pasa la compuerta", o antes de cualquier PR.
---

# Etapa 5 — Verificar

Producir **evidencia**, no una opinión. La salida es una tabla que se pega en el PR.

Criterios completos en [`compuerta-a-main.md`](../../rules/compuerta-a-main.md).

## El orden importa: lo barato primero

```powershell
.\scripts\verificar.ps1                      # niveles 1-2, y 3 si hay escenarios
.\scripts\verificar.ps1 -PermitirPendientes  # verificación de fase intermedia (NUNCA para main)
.\scripts\verificar.ps1 -Escenarios          # incluye el CI local (minutos, usa el escritorio real)
```

Deja la evidencia en `out\evidencia.md`. Si el script no está disponible, los cuatro niveles a mano:

| # | Nivel | Comando | Bloquea |
|---|---|---|---|
| 1 | compila | `dotnet build windows-client\WindowsClient.csproj -c Release` | siempre |
| 2 | contrato | `.\scripts\contrato-del-grafo.ps1` | siempre |
| 3 | escenarios | `.\scripts\ci-local.ps1` | si hay escenarios de lo tocado |
| 4 | a mano | `U.exe` sobre ≥2 pantallas | si se tocó UI o SAP |

**2 y 3 no se sustituyen.** El contrato vigila las promesas y corre en la nube sin tocar pantalla;
los escenarios vigilan el resultado sobre el escritorio real.

## Nivel 2 — leer el veredicto de verdad

```
CONTRATO INTACTO: el grafo se comporta como el día que se congeló.
```

- Cualquier otra cosa **no pasa**. Un `PENDIENTE` que sobrevive es una promesa sin código: la rama no
  va a `main`, va a la fase que falta.
- Si el contrato falla al **compilar** o las promesas fallan **todas a la vez**, sospecha del arnés
  antes que del núcleo: con Debug en una máquina con Smart App Control salen diez rojas de golpe
  (`0x800711C7`) y el veredicto dice «CONTRATO ROTO» mintiendo. `contrato-del-grafo.ps1` ya compila
  en Release por eso; si lo cambiaste, deshazlo.

## Nivel 4 — la corrida a mano, contada

> **Antes de mergear, contar en cuántas pantallas se probó.** Una sola es una apuesta a que las demás
> se comportan igual — y el run de NWP1 demostró que no.

- Nombra las pantallas: «explorer.exe y Configuración», no «probado».
- Usa **🧪 Ensayo en seco** (recorre el plan sin tocar la pantalla; marca cada cambio de pantalla y
  declara bloqueante un `input` que no navega) y **👣 Paso a paso**.
- Al terminar bien un mapeo: **«guardar esta prueba para CI»** — congela lo logrado como mínimo
  exigible y alimenta el nivel 3 para la próxima.
- **Lee el log**, `%LOCALAPPDATA%\U\logs\u-AAAAMMDD.log`. No concluyas desde capturas.

## La tabla de evidencia

```markdown
| Nivel | Resultado | Detalle |
|---|---|---|
| Compila (Release) | ✅ | windows-client + windows-graph |
| Contrato del grafo | ✅ | 19/19 promesas, 0 pendientes |
| Escenarios (CI local) | ✅ | explorer: pantallas 16/14 · declarados 22/20 · con acción 18/16 |
| A mano | ✅ | explorer.exe (16 pantallas) y Configuración (11) |
| Clase de error | — | vivía en 3 sitios; los 3 corregidos |
```

Un nivel que no se corrió se marca **⚪ no corrido, y por qué** — nunca ✅. Un ✅ que no se ganó es
peor que un ⚪: invita a confiar (patrón nº8).

## Lo que no cuenta como verificado

- Que compile: es el nivel 1 de cuatro.
- «Terminé» dicho por un modelo. El puente consciente ya declaró éxito habiendo pulsado el botón
  equivocado.
- Un recuento sobre lo ejecutado en vez de sobre el plan.
- Una sola pantalla.

## Al terminar

Si todo está verde: `/a-main`. Si no, di **qué nivel falló y con qué salida literal** — sin
interpretarla — y vuelve a `/implementa`.
