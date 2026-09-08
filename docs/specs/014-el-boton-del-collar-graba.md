# Plan de implementación: el botón del collar graba la consulta

Estado: **propuesto** · Nace de la petición del 2026-09-07 · Rama: `jose/el-boton-del-collar-graba`

Petición del dueño, literal: *«haz que al oprimir el botón del collar se empiece a grabar el botón
grabar en vez de que la carita escuche»*.

## Diagnóstico: qué se midió

| Qué | Medida | Fuente |
|---|---|---|
| A dónde va hoy el botón del collar | a `StartMicByFace()`: carrillón y `OnMic` — **abre la voz en vivo de la carita** | `FaceWindow.xaml.cs:2258` |
| Cuántos sitios enganchan el botón | **1** (`EngancharCollar`); el servicio lo publica en `CollarPermanente.BotonPulsado` | `grep -rn BotonPulsado windows-client/src` |
| Qué hace «Grabar» en la consulta | `AlternarAsync()`: empieza si no graba, termina si graba; se desactiva mientras cambia | `ConsultaWindow.cs:1413` |
| Cómo abre la carita la consulta | `App.AbrirLaConsulta()`; si ya hay una, `ReglaDeLaVentana` la trae al frente | `FaceWindow.xaml.cs:1836-1866`, `App.xaml.cs` |
| El botón llega aunque no haya voz abierta | sí: el enlace es del servicio, no de la sesión de voz (spec 001) | `CollarPermanente.cs:39` |
| Quién tiene el collar ahora | la instancia `C:\U-dev2\bin\U.exe` (PID 40048, 16:09), «collar abierto» a las 16:20:26 | `C:\U-dev2\local\U\logs\u-20260907.log` |

**Lo que decide la forma:** el collar habla con **una** app a la vez. El botón lo recibe la
instancia que tenga el enlace BLE, así que probar esto en una app paralela exige que la otra
suelte el collar (elegir «Computador» en su selector, o cerrarla).

## Por qué esto va dirigido por especificación

Cambia lo que el sistema promete al único botón físico que tiene. Se puede escribir la frase que hoy
es falsa y mañana verdadera, y una regla pura la deja juzgable sin collar: el contrato no puede
pulsar uno, pero sí puede preguntar qué haría la carita si lo pulsaran.

**La regla del flujo, y no tiene excepciones:** ninguna línea de producción entra antes que la
promesa que la juzga.

## La especificación

En el contrato del grafo. **184 y no 168**: 168-174 viven en `jose/estetica-de-la-carita` (spec 012)
y 175-183 las reserva la spec 013.

| # | Promesa | Fase |
|---|---|---|
| 184 | el botón del collar graba la consulta y no abre la voz de la carita: pulsarlo empieza a grabar —abriendo la consulta si no está— y pulsarlo grabando para | 1 |

### Con qué se juzga

Mapa a mano contra `U.WindowsClient.Ui.ReglaDelBotonDelCollar.AlPulsar(bool hayConsulta, bool grabando)`,
como la 147 contra `ReglaDelToque`: sin consulta → abrirla y grabar; abierta y parada → grabar;
grabando → parar; y ninguna respuesta posible nombra la voz. Que la carita consulte la regla y que
la voz no se abra es **nivel 4**: el log tiene que decir `collar: botón → Grabar` y a continuación
la consulta grabando, sin ninguna línea de `voz-viva` abriéndose.

## Las fases

### Fase 1 — el botón graba

| | |
|---|---|
| **Promesa que pone verde** | 184 |
| **Qué toca** | `src/Ui/ReglaDelBotonDelCollar.cs` (nuevo), `src/Ui/FaceWindow.xaml.cs` (`EngancharCollar`), `src/Ui/ConsultaWindow.cs` (puerta pública a «Grabar») |
| **¿Núcleo congelado?** | no |
| **Terminado** | 184 verde, 1..167 intactas |
| **Sitios con esta clase de error** | 1 enganche del botón; se cambia ese |

## Lo que NO entra

- **Traer la consulta al frente** cuando ya está abierta. El botón es un mando a distancia: quien lo
  pulsa está mirando al paciente o a SAP, y robarle el foco es peor que no verlo. Si la consulta no
  existía, se abre delante como siempre.
- Que el collar deba ser además el micrófono elegido. El botón graba con lo que esté elegido.

## Hallazgos

- **2026-09-07.** `collar.json` (permanente/enlazado) vive en `%LOCALAPPDATA%` real, no bajo
  `U_DATA_DIR`: dos instancias comparten la intención de conectar y se disputan el collar por BLE.

## Cierre

- [ ] 184 verde (`.\scripts\contrato-del-grafo.ps1` → CONTRATO INTACTO)
- [ ] Sabotaje comprobado: con la regla contestando «Grabar» sin consulta, la 184 se pone roja
- [ ] Probado con el collar real, con la consulta cerrada, abierta y grabando; log en el PR
- [ ] Estado: **implementado** (AAAA-MM-DD)
