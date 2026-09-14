# El nivel 4 de la voz: ¿lo hace a la primera?

La prueba en el PC real de la [spec 017](../../docs/specs/017-lo-hace-a-la-primera.md). Le escribe a
Ü por «Escríbele…» con la voz abierta —el mismo camino que una persona— y mide lo que la nota de voz
del 2026-09-10 pedía en cifras: **máximo dos intentos, máximo dos segundos**.

```powershell
powershell -ExecutionPolicy Bypass -File scripts\nivel4-voz\correr.ps1
```

Corre la misma batería dos veces —con el binario de `main` y con el de la rama—, cada una sobre su
propia copia de los datos de Ü, y saca la tabla de las dos. Unos 25 minutos.

## Antes de correrlo

- **Alguien delante, o al menos sin salvapantallas ni bloqueo.** En este portátil el salvapantallas
  del fabricante (OLED Care) se traga toda la entrada inyectada, y `keybd_event` falla sin avisar:
  la noche del 2026-09-10 eso dejó la voz «sin abrir» sin un solo error. `conducir.ps1` comprueba que
  el escritorio de entrada sea `Default` al empezar y antes de cada Enter.
- **`OPENAI_API_KEY`** en el entorno, y **auriculares** o la compuerta de eco (`U_SIN_ECO=0`): sin
  eso Ü se oye a sí misma por el altavoz y entra en bucle (medido el 2026-09-07).
- **Python 3** para el analizador.
- Los binarios: por defecto usa los congelados esa noche en `%LOCALAPPDATA%\Temp\claude\U-base-main`
  y `U-rama-017`. Si ya no están, compila `main` y la rama con
  `dotnet build windows-client\WindowsClient.csproj -c Release -o <carpeta>` y pásalos con `-Base` y `-Rama`.

## Qué mide

| T | Lo que se le pide | Estado final exigido |
|---|---|---|
| T1 | «abre el explorador de archivos y ve a Documentos» | el explorador en Documentos |
| T3 | «abre Configuración y ve a Bluetooth» | la página de Bluetooth |
| T4 | «en el explorador, pulsa Descargas en el panel de la izquierda» | Descargas |
| T5 | «en Configuración entra en Sistema, vuelve atrás y entra en Bluetooth» | Bluetooth, pasando por Sistema |

Tres repeticiones de cada una: el fallo del audio es intermitente. **Aprobada** = estado final
alcanzado **con al menos una acción de voz** (el 2026-09-11 el juez aprobó tareas que resolvió el
agente por coordenadas con la sesión de voz ya cerrada: cero acciones y todo lo demás en vacío), ≤2 intentos al mismo destino, **a lo sumo un intento fallido en toda la petición** (lo que
el tope frena cuenta; la lista de homónimos no) y cada acción ≤2,0 s. T4 y T5 exigen además haber
pasado por lo que prueban. El denominador es el plan, no lo que se llegó a ejecutar. Con la rama, además, cada petición deja su línea `voz-turno:` y el tope deja la suya
cuando frena un tercer intento.

**Fuera a propósito: «entra a Instagram y ve los mensajes».** Abriría los mensajes privados de quien
esté en el equipo, y la foto de la pantalla viaja a OpenAI. Se prueba a mano, con la persona delante.

## Lo que el conductor decide solo (desde el 2026-09-13)

Tres fallos del intento del 2026-09-12 (spec 018, V5, V7 y V8), que no midió nada:

- **La voz está abierta si lo dice la última línea de estado de `voz-viva`**, no si alguna vez dijo
  «sesión abierta». Aquella noche `sesión abierta con «…»` salió en el mismo segundo que el error de cuota, y
  el conductor la tomó por voz abierta. Por eso, a los 6 s de abrirla, vuelve a mirar y, si ya no está
  abierta, para con el código 3. La línea `cuenta: … sesión abierta` no cuenta: es la cuenta, no la voz.
- **Al terminar, Ctrl+Alt+M solo se pulsa si la voz sigue abierta.** El atajo alterna, y con la voz ya
  cerrada la reabría justo antes de matar a Ü (23:39:40 y 23:43:06).
- **Sin crédito se para al momento**, con el código 5 y la línea del log que lo dice
  (`credit_balance_exhausted`, `insufficient_quota` o `You have no credits remaining`). `correr.ps1` no corre
  la otra pasada.
- **Antes de cada tarea queda escrito el estado inicial.** Si el estado final ya se cumple (T4 empezó con
  tres exploradores en Descargas), lleva la app a una partida neutra: el explorador a «Este equipo», y
  Configuración a `ms-settings:home`. Medido: `ms-settings:` la deja en la página donde estaba. Si no puede,
  la corrida se hace igual y queda `ya_cumplido` en `resumen.jsonl`. El juez la da por **no medible** y no la
  aprueba.

| Código de `conducir.ps1` | Qué pasó |
|---|---|
| 0 | la batería entera |
| 2 | este Ü no escribió ningún log en 12 s |
| 3 | la voz no abrió, o dijo que abría y a los 6 s ya no estaba abierta |
| 4 | el escritorio de entrada no es `Default` (salvapantallas, bloqueo) |
| 5 | la cuenta de OpenAI no tiene crédito |

## Las piezas

- `conducir.ps1` — la batería sobre un binario. Deja por tarea el trozo de log, el estado final y una captura.
  Con `-Ensayo` no pulsa ninguna tecla: escribe en el diario lo que pulsaría.
- `piezas.ps1` — lo que el conductor lee del log (estado de la voz, falta de crédito) y del escritorio
  (estado final, partida neutra). Vive aparte para poder juzgarlo sin Ü.
- `analizar.py` — la tabla y las secuencias de una corrida.
- `autoprueba.py` — el juez juzgado: logs sintéticos, un caso por regla, sin pantalla ni modelo.
  `python scripts\nivel4-voz\autoprueba.py`. Si una regla de `analizar.py` cambia, un caso cambia de veredicto.
- `autoprueba-conductor.ps1` — el conductor juzgado, sin Ü ni crédito. Sin argumentos, lo que lee del log,
  con las líneas literales del 2026-09-12. Con `-Conductor`, `conducir.ps1 -Ensayo` entero contra un Ü falso
  que escribe un log con guion: voz que se cierra sola, voz que sigue abierta, sin crédito. Con `-Escritorio`
  abre un explorador en Descargas y Configuración en Bluetooth, y comprueba que vuelven a la partida neutra.
  Los dos juntos tardan unos 2 minutos y dicen `CONDUCTOR INTACTO` o `CONDUCTOR ROTO`.
- `tareas.json` — las frases, en UTF-8 (el `.ps1` es ASCII puro: PowerShell 5.1 leería mal un acento).
- `correr.ps1` — base y rama seguidas, y las dos tablas.
