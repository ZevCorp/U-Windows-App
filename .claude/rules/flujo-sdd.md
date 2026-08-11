# El flujo dirigido por especificación

> Regla del proyecto desde el 2026-08-10. Nace de [`docs/plan-plata-real.md`](../../docs/plan-plata-real.md),
> que la aplicó primero y encontró un bug de meses (la promesa 19) **antes de escribir código**.

## La regla que no tiene excepciones

**Ninguna línea de producción entra antes que la promesa que la juzga.**

No es metodología. Es que este repo construye subsistemas que se dan por buenos a sí mismos —la plata
declarada subía su métrica escribiendo, el criterio de terminado del arquitecto se satisfacía
declarando— y una prueba escrita *después* del código se escribe para que pase. Ese es el mismo vicio
con otro nombre.

Corolario operativo: **cada fase empieza con el contrato ROTO y termina con el contrato INTACTO.**
El rojo de la primera fase es un entregable, no un accidente. Se ve así:

```
⧗ PENDIENTE: «Plata.Derivar» todavía no existe (fase 0 del plan).
  La promesa está escrita y en rojo, que es donde tiene que estar.
```

## Las seis etapas

| # | Skill | Entrada | Salida | Termina cuando |
|---|---|---|---|---|
| 1 | `/especifica` | una petición en prosa | `docs/specs/NNN-<slug>.md` con promesas numeradas | cada promesa se puede juzgar por máquina |
| 2 | `/fases` | la spec | tabla de fases en la misma spec | cada fase tiene la promesa que pone verde |
| 3 | `/promesas` | la spec | promesas en `tests/ContratoDelGrafo/Contrato.cs` | el contrato está ROJO por las razones escritas |
| 4 | `/implementa` | una fase | código de producción | esa promesa verde, ninguna anterior rota |
| 5 | `/verifica` | la rama | tabla de evidencia | compila + contrato intacto + escenarios |
| 6 | `/a-main` | la rama verificada | PR con evidencia | `main` sigue verde |

Se repiten 4 y 5 por cada fase. 6 se hace una vez, cuando **todas** las promesas de la spec están
verdes: una rama que llega a `main` con promesas pendientes deja `main` rojo para los tres.

## Cuándo NO aplica

El flujo cuesta media hora larga. No se paga por todo:

| Aplica | No aplica |
|---|---|
| Tocar el núcleo de navegación (`SurfaceMap`, derivación, rutas) | Textos, tooltips, colores del inspector |
| Cambiar lo que el sistema *promete* (comportamiento observable) | Renombrar una variable, extraer un método |
| Un subsistema que hoy se juzga a sí mismo | Corregir un log que dice mal el nombre de algo |
| Un bug que ya volvió una vez | Documentación |

La prueba de si aplica: **¿se puede escribir la frase que estaría siendo falsa hoy y verdadera
después?** Si sí, esa frase es una promesa y va al contrato. Si no se puede escribir, no está claro
qué se está arreglando — y ese es el hallazgo.

## Dónde vive cada cosa

- **La spec**: `docs/specs/NNN-<slug>.md`. El enunciado de la promesa es el que va **literalmente**
  en `Contrato.cs`; si difiere, la spec miente.
- **Las promesas ejecutables**: `tests/ContratoDelGrafo/Contrato.cs`, numeradas en continuación de
  las que ya hay. **Los números no se reciclan**: una promesa retirada deja su hueco, porque los
  commits y los planes viejos la citan por número.
- **Los fixtures congelados**: `tests/ContratoDelGrafo/bronce/`. Entradas que tienen que dar el mismo
  resultado en cualquier máquina y para siempre. Distinto de `C:\U-versiones\escenarios\` (mide
  resultado sobre el terreno vivo de *esta* máquina, y por eso no se commitea).

## Pedir capacidades que todavía no existen

Una promesa escrita antes que su código llama a APIs que aún no están. **Se piden por nombre, con
reflexión**, para que el contrato siga compilando contra núcleos que no las conocen:

```csharp
var t = Nucleo.GetType("U.WindowsClient.Navigation.Plata");
var p = t?.GetMethod("Derivar")?.Invoke(null, new object[] { m, app });
if (p == null) { Pendiente("Plata.Derivar", "1"); return; }
```

La distinción es deliberada y no se puede relajar: **ausente en un núcleo viejo = «no aplicable»;
ausente en el que estamos escribiendo = `Pendiente`, y cuenta como incumplida.** Una promesa sin
código que dijera «no aplicable» se sumaría al verde, y el contrato pasaría a certificar el vacío.
