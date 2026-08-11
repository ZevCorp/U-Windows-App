# Patrones de desarrollo: cómo se escribe código aquí

> Cada uno de estos costó rondas de diagnóstico reales, con fecha. No son estilo: son las formas de
> fallo que este repo ya pagó. Un agente que trabaje aquí se acopla a ellos o repite la factura.
>
> La lista larga con el relato de cada caso está en [`CLAUDE.md`](../../CLAUDE.md) §*Aprendizajes de
> método*. Esto es la versión accionable: **el patrón, el antipatrón y cómo se comprueba.**

## Diagnóstico

### 1. El log antes que la teoría

`%LOCALAPPDATA%\U\logs\u-AAAAMMDD.log` es la fuente de verdad. Antes de proponer una causa, leerlo.

- **Antipatrón**: deducir de capturas de pantalla. El 2026-07-26 costó **cuatro rondas** — una
  captura no distingue «espaciado correcto» de «filas equivocadas» de «otra caja encima»; el log sí.
- **Líneas útiles**: `shell subType=`, `filas del árbol ·`, `fila seleccionada`, `CONTRASTE geometría`,
  `✋ no se llegó a`, `⏱ TIEMPOS`.

### 2. Un mensaje describe el paso que falló; nunca concluye

Si un mensaje afirma una causa, **tiene que poder distinguirla de las demás**.

- **Antipatrón**: `"getters de selección sin resultado"` se imprimía también cuando el árbol nunca se
  resolvió — una conclusión disfrazada de hecho. Mandó la investigación al sitio equivocado dos veces.
- **Antipatrón**: `"sin clic reciente que SAP reconozca"` cubría tres situaciones distintas (no hay
  clic anotado / el clic es viejo / el hit-test no encuentra nada).
- **Comprobación**: enumera las causas posibles del mensaje. Si son ≥2, el mensaje está mal escrito.

### 3. `try/catch` mudo: prohibido de facto

Un catch que se traga el motivo convierte un bug de aridad en «la API no existe» — creencia que
estuvo *documentada* en este repo y era falsa.

```csharp
// La cadena ENTERA. Un TypeInitializationException dice «el inicializador lanzó una excepción»
// y se guarda para sí POR QUÉ, que es lo único que sirve.
for (var x = e; x != null; x = x.InnerException)
    Console.WriteLine($"   ✘ {x.GetType().Name}: {x.Message}");
```

### 4. Pregúntale a la API antes de creerle al código

Una sonda de solo lectura con enlace tardío contestó en veinte minutos tres preguntas «resueltas por
deducción» desde hacía semanas. Es barato y sustituye rondas enteras de teoría.

## Arreglos

### 5. Arreglar la clase de error, y contar cuántos sitios la tienen

El veredicto rojo falso se arregló para árboles; media hora después reapareció idéntico en el grid.
Después, el diagnóstico de superficie SAP se cableó en dos de los **tres** sitios que la construyen —
y faltaba justo el que usa el operador.

- **Comprobación obligatoria**: antes de dar por bueno un arreglo, `grep` de la construcción que
  falló y **contar los sitios**. El número va en el commit.
- Si el arreglo se apoya en «este tipo de elemento no usa ese criterio», revisar **todos** los que
  tampoco lo usan.

### 6. Cuando una limitación documentada resulta falsa, borrar la maquinaria — no parchearla

Había tres capas (`topNode` + recorrido con plegado + alto calibrado) compensando algo que SAP sí
hacía. Al probarse falsa la limitación, se siguieron ajustando; cada capa aportaba su modo de fallo.
La versión final es una regla de una línea.

### 7. Contención no es alineación

`CUADRA` solo verifica que el clic caiga *dentro*. Una banda desplazada la pasa igual. Un criterio
que no puede fallar cuando el bug está presente no es un criterio.

## Datos y estado

### 8. Una caja que miente es peor que no tener caja

Invita a confiar en ella. Aplicado dos veces: no dibujar filas sin `topNode`; distinguir geometría
**leída** de **estimada** y decirlo en la UI.

### 9. Vacío no es ausente

`??` no cae al respaldo con cadena vacía. Ese detalle convirtió cada clic de árbol en un `SetFocus()`
que reportaba éxito. Todo dato que venga de red, COM o disco se normaliza: `string.IsNullOrWhiteSpace`,
no `?? `.

### 10. Un paso no ejecutado deja rastro

El salto-adelante se comió 19 pasos y reportó «29 de 30»; otra corrida devolvió `ok=True pasos=2/2`
de un plan de 4. Mismo vicio: el denominador se calculaba sobre los pasos *con veredicto*, y los
saltados no dejaban veredicto, así que el total encogía con ellos.

**Regla**: un paso omitido emite `Omitted`. **El denominador es el plan, nunca lo ejecutado.**

> Lo peor no es que falle: es que parezca que funcionó.

### 11. No anclar rutas al nombre de la carpeta del repo

El guardián del núcleo protegía `*\windows-app\...` y la carpeta real se llama `U-Windows-App`: no
protegía **nada** durante cinco horas. El ancla es la ruta *dentro* del repo, que sí es estable.

## Plataforma

### 12. Enlace tardío siempre para COM

Nada de referenciar `sapfewse.ocx`: así compila en máquinas sin SAP GUI (CI, portátil) y sobrevive a
los cambios de versión (el interop se rompió entre 7.40 → 7.70 → 8.0). Entrada por ProgID
`SapROTWr.SapROTWrapper`, y `session.FindById` con `SapSelector.Normalize(id)` **siempre**.

### 13. Se juzga el binario que se distribuye: Release, no Debug

Con Debug el contrato no puede correr en una máquina con Smart App Control (`0x800711C7`): las diez
promesas fallaban a la vez y el veredicto decía «CONTRATO ROTO» — un fallo del arnés disfrazado de
núcleo roto, que es lo peor que puede decir un juez.

### 14. No optimizar la cadencia antes del costo por iteración

Bajar el refresco a 200 ms antes de arreglar las ~2.600 llamadas COM/s amplificó un bug latente y
disparó una cacería de una hora.

### 15. Antes de escribir en un repo que no conoces, lee sus reglas

El portal clínico avisa en su `AGENTS.md` de que no usa service-role en ninguna parte: eso es una
postura de seguridad, no un olvido.

## Cómo se comenta aquí

Este repo comenta **el porqué, con fecha y con medida**, no el qué. El comentario existe para que el
siguiente no repita el diagnóstico:

```csharp
// RELEASE, NO DEBUG, y no es una preferencia: con Debug el contrato NO PUEDE CORRER en una
// maquina con Smart App Control activo. […] (2026-08-08)
```

- Un comentario que parafrasea la línea siguiente sobra.
- Un comentario que dice «no cambiar esto» sin decir qué pasó cuando se cambió, sobra.
- Nombres y prosa **en español**, como el resto del repo. Los identificadores de dominio también
  (`Bronce`, `Plata`, `FijarNivel`, `Omitted` donde ya existe en inglés se respeta).
