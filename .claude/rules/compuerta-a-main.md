# La compuerta: qué tiene que ser cierto para que algo entre a `main`

`main` roto bloquea a los tres. Esto es lo que separa una rama de `main`, y **el orden importa**:
lo barato primero, para que un fallo de veinte segundos no espere a una prueba de veinte minutos.

## Los cuatro niveles

| # | Nivel | Cómo se corre | Cuánto tarda | Qué vigila | ¿Bloquea? |
|---|---|---|---|---|---|
| 1 | **Compila** | `dotnet build windows-client\WindowsClient.csproj -c Release` | ~30 s | que exista | sí |
| 2 | **El contrato** | `.\scripts\contrato-del-grafo.ps1` | ~30 s | las **promesas** del núcleo | sí |
| 3 | **Los escenarios** | `.\scripts\ci-local.ps1` | minutos | el **resultado sobre el terreno real** | sí, si hay escenarios de lo tocado |
| 4 | **La corrida a mano** | `U.exe` sobre ≥2 pantallas | minutos | lo que nadie automatizó | sí, si se tocó UI o SAP |

Todo junto: `.\scripts\verificar.ps1`, que además deja la tabla de evidencia para el PR.

**2 y 3 no son lo mismo y ninguno sustituye al otro.** El contrato vigila las *promesas* y corre en
la nube sin tocar la pantalla; los escenarios vigilan el *resultado* sobre el escritorio real, y por
eso son locales: un runner en la nube no tiene este escritorio ni estas apps.

## Nivel 2: el contrato

Lo corre `.\scripts\contrato-del-grafo.ps1` y también CI en cada PR a `main`
([`.github/workflows/contrato.yml`](../../.github/workflows/contrato.yml)). Dos veredictos:

```
CONTRATO INTACTO: el grafo se comporta como el día que se congeló.
CONTRATO ROTO: N promesa(s) incumplida(s). El cambio no puede entrar así.
```

- **Rojo en la rama, durante el desarrollo: es lo correcto.** Las promesas `PENDIENTE` son el
  entregable de la fase que las escribió.
- **Rojo al llegar a `main`: no entra.** Una rama con promesas pendientes deja `main` rojo para
  todos, y a partir de ahí el contrato deja de significar algo.
- Si una prueba **estorba** para un cambio, la conversación es sobre el **contrato**, no sobre la
  prueba: cambiarla es cambiar lo que el grafo promete a todo lo que se construye encima. Eso se
  discute con el dueño, se anota en la spec, y el número de la promesa retirada **no se recicla**.

## Nivel 4: la corrida a mano, y en cuántas pantallas

> **Antes de mergear, contar en cuántas pantallas se probó.** Una sola pantalla verificada es una
> apuesta a que las demás se comportan igual — y el run de NWP1 demostró que no.

El número va **en el PR**, con nombre: «explorer.exe y Configuración», no «probado». Una pantalla
es un dato incompleto y hay que decirlo como tal.

Para eso están, y se usan antes de decir que algo funciona:

- **🧪 Ensayo en seco** — recorre el plan sin tocar la pantalla; marca cada cambio de pantalla y
  declara bloqueante un `input` que no navega. Detecta en dos segundos el fallo que costó un día.
- **👣 Paso a paso** — se detiene antes de cada paso, con el veredicto y la captura de cuando se
  enseñó al lado. Al terminar bien, **marcar «guardar esta prueba para CI»**: eso congela lo logrado
  como mínimo exigible y es lo que da material al nivel 3.

## Lo que no cuenta como verificación

- Que compile. Compilar es el nivel 1 de cuatro.
- Que el modelo diga que terminó. **«Terminé» no es un veredicto**: el puente consciente ya declaró
  éxito habiendo pulsado «Buscar pacientes» en vez de «Crear Triage Administrativo».
- Un recuento sobre lo ejecutado en vez de sobre el plan (patrón nº10): `29/30` con 19 pasos comidos.
- Una sola pantalla.
- Leer el código y concluir. El log y la API contestan; el código solo dice lo que alguien creyó.

## El PR

Plantilla en [`.github/pull_request_template.md`](../../.github/pull_request_template.md). Lo que no
puede faltar:

1. **Qué promete ahora el sistema que antes no** — con los números de promesa.
2. **La tabla de evidencia** de `verificar.ps1`, pegada, no resumida.
3. **En cuántas pantallas se probó, con nombre.**
4. **Cuántos sitios tenían la clase de error** que se arregló (patrón nº5).

Mergear tú mismo está bien si nadie más tocó esos archivos. Si el PR cruza territorio ajeno, se pide
ojo antes.
