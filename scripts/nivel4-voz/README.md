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
alcanzado, ≤2 intentos al mismo destino, **a lo sumo un intento fallido en toda la petición** (lo que
el tope frena cuenta; la lista de homónimos no) y cada acción ≤2,0 s. T4 y T5 exigen además haber
pasado por lo que prueban. El denominador es el plan, no lo que se llegó a ejecutar. Con la rama, además, cada petición deja su línea `voz-turno:` y el tope deja la suya
cuando frena un tercer intento.

**Fuera a propósito: «entra a Instagram y ve los mensajes».** Abriría los mensajes privados de quien
esté en el equipo, y la foto de la pantalla viaja a OpenAI. Se prueba a mano, con la persona delante.

## Las piezas

- `conducir.ps1` — la batería sobre un binario. Deja por tarea el trozo de log, el estado final y una captura.
- `analizar.py` — la tabla y las secuencias de una corrida.
- `autoprueba.py` — el juez juzgado: logs sintéticos, un caso por regla, sin pantalla ni modelo.
  `python scripts\nivel4-voz\autoprueba.py`. Si una regla de `analizar.py` cambia, un caso cambia de veredicto.
- `tareas.json` — las frases, en UTF-8 (el `.ps1` es ASCII puro: PowerShell 5.1 leería mal un acento).
- `correr.ps1` — base y rama seguidas, y las dos tablas.
