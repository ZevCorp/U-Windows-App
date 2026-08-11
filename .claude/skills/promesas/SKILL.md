---
name: promesas
description: Escribe las promesas de una spec como pruebas ejecutables en tests/ContratoDelGrafo/Contrato.cs ANTES de que exista el código que las cumple, y comprueba que el contrato queda rojo por las razones escritas. Es la etapa 3 del flujo SDD (fase roja). Úsala cuando exista una spec con fases y toque escribir el contrato, o cuando el usuario diga "escribe las promesas", "pon el contrato en rojo", "los tests primero".
---

# Etapa 3 — Poner el contrato en rojo

**El entregable de esta etapa es un rojo.** Concreto, con nombre y con motivo:

```
⧗ PENDIENTE: «Plata.Derivar» todavía no existe (fase 1 del plan).
  La promesa está escrita y en rojo, que es donde tiene que estar.
CONTRATO ROTO: 7 promesa(s) incumplida(s). El cambio no puede entrar así.
(4 de ellas PENDIENTES: la capacidad todavía no existe.)
```

## Antes de empezar: el candado

`tests\ContratoDelGrafo\*` está **protegido** por [`guardia-nucleo.ps1`](../../hooks/guardia-nucleo.ps1).
Escribe primero `C:\U-versiones\intencion.txt` **en UTF-8** (caduca a los 20 min), con qué promesas
vas a añadir y de qué spec salen. Sin declaración fresca el hook bloquea la edición. Ver
[`nucleo-congelado.md`](../../rules/nucleo-congelado.md).

## Cómo se escribe una promesa

Registrarla con **el enunciado literal de la spec**, en continuación de la numeración:

```csharp
Prueba("20. <el enunciado tal cual está en la spec>", NombreDelCuerpo);
```

El cuerpo, sobre un `SurfaceMap` recién nacido (el arnés le da su propio `U_DATA_DIR`):

```csharp
private static void NombreDelCuerpo(SurfaceMap m)
{
    // El caso medido el <fecha>: <el síntoma real, no la hipótesis>.
    var h = Derivacion(m, "fake.exe");
    if (h == null) { Pendiente("Plata.Derivar", "1"); return; }   // fase que lo cumplirá

    Debe(<condición>, "<qué queda probado, en la voz de la promesa>");
}
```

Reglas del arnés, todas con motivo:

- **Capacidades que aún no existen se piden por nombre** (reflexión sobre `Nucleo.GetType(...)`).
  Llamar a `Plata` directamente rompe la **compilación** del contrato contra núcleos viejos.
- **Ausente ⇒ `Pendiente(capacidad, fase)`, que cuenta como incumplida.** Nunca «no aplicable»: eso
  sumaría al verde y el contrato pasaría a certificar el vacío. («No aplicable» solo para núcleos
  antiguos que jamás prometieron eso, como en la promesa 10.)
- **Un `Debe` por afirmación**, con el texto en la voz de la promesa. `Debe(a && b, "...")` da un
  rojo que no dice cuál de las dos falló.
- **Nada de `try/catch` en el cuerpo.** El arnés ya imprime la cadena entera de `InnerException`;
  capturar dentro es exactamente el patrón nº3, cometido dentro del arnés que existe para evitarlo.
- Si hace falta esperar al `MinDwell`, `Thread.Sleep(Dwell)` (1450 ms, con margen para no medir la
  casualidad).

## Comprobar que el rojo es el correcto

```powershell
.\scripts\contrato-del-grafo.ps1
```

Y verificar **una por una**:

- [ ] Cada promesa nueva falla, y falla por **la razón escrita** — no por un `NullReferenceException`
      del arnés, no por un typo en el nombre del tipo. Un rojo que no distingue sus causas manda la
      investigación al sitio equivocado (patrón nº2).
- [ ] Las promesas 1..N **antiguas siguen verdes**. Si alguna se puso roja al añadir las nuevas, el
      arnés se contaminó: arréglalo ahora, porque a partir de aquí ya no sabrás si es regresión.
- [ ] El recuento de `PENDIENTES` **coincide con las fases** que faltan. Si hay más pendientes que
      fases, alguna promesa no tiene quién la ponga verde.
- [ ] Alguna promesa nueva **ya está verde**: bien, es una que se congela. Confírmalo intencional y
      anótalo; una promesa que nace verde por accidente suele estar mal escrita (no se puede falsificar).

## Commitear el rojo

```
test(<ámbito>): las promesas de <lo que sea>, escritas antes que su codigo
```

El rojo se commitea. Es el registro de que la prueba no se escribió para pasar.

Después: `/implementa` fase por fase.
