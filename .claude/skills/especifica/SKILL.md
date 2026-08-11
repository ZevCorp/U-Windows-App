---
name: especifica
description: Convierte una petición en prosa en una especificación con promesas numeradas y verificables por máquina, guardada en docs/specs/. Es la etapa 1 del flujo SDD y va ANTES de escribir cualquier código o test. Úsala cuando el usuario pida una feature, un arreglo de fondo, o diga "especifica", "escribe la spec", "qué debería prometer esto". Si el trabajo toca el núcleo de navegación (SurfaceMap, derivación, rutas) o cambia lo que el sistema promete, esta etapa no es opcional.
---

# Etapa 1 — Especificar

Producir un documento en `docs/specs/NNN-<slug>.md` donde **cada promesa se pueda juzgar por
máquina**. Nada de código todavía, ni de tests.

Lee primero [`.claude/rules/flujo-sdd.md`](../../rules/flujo-sdd.md). El modelo a imitar es
[`docs/plan-plata-real.md`](../../../docs/plan-plata-real.md), que fue la primera spec del repo.

## 1. Antes de escribir: medir el presente

Una spec que describe un futuro sin haber medido el presente inventa el problema. Por orden:

1. **El log**, no las capturas ni el código: `%LOCALAPPDATA%\U\logs\u-AAAAMMDD.log`.
2. **El contrato actual**: lee `tests/ContratoDelGrafo/Contrato.cs` entero. ¿Cuál es el número más
   alto? ¿Alguna promesa ya cubre esto? ¿Alguna lo **contradice**?
3. **El grafo**: `graphify query "<la pregunta>"` antes que grep, si `graphify-out/graph.json` existe.
4. **La API, si hay COM de por medio**: una sonda de solo lectura contesta en veinte minutos lo que
   la deducción no cierra en semanas.

Anota lo medido con fecha. Es la sección *Diagnóstico* de la spec y es lo que impide que la spec sea
una opinión.

## 2. Escribir las promesas

Cada una en **una frase declarativa, en presente, en español, que sea falsa hoy y verdadera después**.
El enunciado que escribas es el que irá **literalmente** en `Contrato.cs`.

```
13. el bronce no se entera de lo declarado
16. sin cromo derivado no hay atajo: quitar la plata rompe una ruta
19. una sección alcanzada solo por el mobiliario sigue teniendo hijos
```

Numera **en continuación** de las que ya existen en `Contrato.cs`. Los números no se reciclan.

Una promesa está bien escrita si:

- [ ] Se puede **falsificar**: existe una entrada concreta que la rompe. Si no la encuentras, la
      promesa no dice nada. (`CUADRA` solo comprobaba contención y por eso pasaba con la banda
      desplazada — patrón nº7.)
- [ ] **No nombra implementación.** «Plata.Derivar devuelve un diccionario» es una firma, no una
      promesa. «la misma entrada da la misma plata» sí lo es.
- [ ] **No se satisface declarando.** Si el sistema puede cumplirla escribiendo un dato en vez de
      derivándolo, está midiendo el vicio que vienes a arreglar.
- [ ] Es **una sola** cosa. «el bronce no se entera de lo declarado» y «el archivo del bronce no
      contiene plata» son dos (13 y 18): juntas darían un rojo que no dice cuál falta.

Marca las que **ya se cumplen**: van al contrato igual, para congelarlas. Que una promesa nazca verde
no la hace inútil — impide que alguien meta después un respaldo que adivine.

## 3. Decir con qué se va a juzgar

Por promesa: qué entrada la ejercita.

- **Fixture congelado** (`tests/ContratoDelGrafo/bronce/`) si tiene que dar el mismo resultado en
  cualquier máquina y para siempre. Se captura una vez, **se le quita todo lo declarado antes de
  guardarlo** —si no, nace contaminado con lo que estamos quitando— y no se vuelve a tocar salvo
  para añadir casos.
- **Escenario de CI local** (`C:\U-versiones\escenarios\`, fuera del repo) si mide resultado sobre el
  terreno vivo de esta máquina.
- **Mapa construido a mano** en la propia prueba, como las promesas 1-10.

Si una promesa no se puede juzgar con el arnés actual, **dilo en la spec**: eso es una fase de
arnés, y va antes que las demás.

## 4. Guardar

`docs/specs/NNN-<slug>.md` a partir de [`docs/specs/PLANTILLA.md`](../../../docs/specs/PLANTILLA.md).
`NNN` es el siguiente número libre; `<slug>` en kebab-case dice el resultado, no el área.

## 5. Presentar al usuario

- La tabla de promesas.
- **Lo que la spec encontró y no estaba en la petición.** Escribir la spec suele destapar un bug que
  ninguna lectura del código había visto (así apareció la promesa 19, con meses de antigüedad).
- Lo que **no** entra, y por qué.

Después: `/fases`.

## Lo que esta etapa NO hace

No escribe código, no escribe pruebas, no toca `Contrato.cs` (que además está protegido por el
guardián del núcleo). Si la petición es de las que no pagan el flujo —textos, colores, renombrados,
docs—, dilo en una frase y ofrece hacerlo directo en vez de montar la ceremonia.
