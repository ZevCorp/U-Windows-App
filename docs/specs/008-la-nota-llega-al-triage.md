# Plan de implementación: la nota llega al triage con un ✓

Estado: **en curso** · Nace de la decisión del 2026-09-02 · Rama: `jose/la-nota-llega-al-triage`

> La experiencia core, cerrada por el camino que ya está validado: **enseñar** (🎓, como hoy) →
> **dictar** la consulta desde el icono → **✓** en cada sección de la nota → los dos batches del
> terreno llevan a SAP hasta el triage del paciente y el rellenador escribe los campos con la nota.
> Sin voz de por medio, sin nombrar nada, sin piloto: dos botones y un check.

## Diagnóstico: qué se midió

Sobre `main` en `8005684` (2026-09-02), con SAP QAS vivo en esta máquina.

| Qué | Medida | Fuente |
|---|---|---|
| La demo del 31 ya llega al triage por batches, de punta a punta | `map_open_app` → batch 1 (`comando`/NWP1 → `Urgencias Adultos/Triage`) → batch 2 (fila del paciente + botón `Triage`) → espera a `GuiTextField` | `Ui/FaceWindow.xaml.cs:3936-3988` (`OnDemoPuntaAPunta`) |
| Pero llena los campos con **valores fijos** y sin releer | `DemoLlenarFormulario`: `DemoValor`/`DemoOpcion` por identidad del campo; `Execute` directo | `FaceWindow.xaml.cs:3997-4088` |
| El rellenador que sí escribe con una nota existe y está probado | `RellenadorSap.RellenarConNotaAsync(nota)`: emparejador de Graph, relectura de cada campo, hasta 4 pasadas de repesca; lo usa «Exportar a HC» desde la web | `Clinical/RellenadorSap.cs:155-183` · `EjecutorDeExportaciones.cs` |
| El rellenador **no ve** los dos editores de texto libre del triage | `ReadFields` no devuelve los shells `GuiTextedit` (Motivo de Consulta `MTVCN`, Conducta `TXTOBS`); la demo los escribe aparte por `ReadVisibleElements` | `FaceWindow.xaml.cs:4031-4045` |
| La nota ya es un resultado tipado en la ventana de consulta | `NotaClinica.Secciones (Clave, Titulo, Contenido)`; `PintarNota` pinta tarjetas sin acción | `Ui/ConsultaWindow.cs:1122-1146` |
| La consulta y la carita son el mismo proceso, y la carita tiene las manos | `_rellenador`, `_clinicalSap` y la puerta MCP 8790 viven en `FaceWindow`; la ventana de consulta no las ve | `FaceWindow.xaml.cs:899, 3000` · `App.xaml.cs` |
| El guardián de pantalla ya existe pero nadie lo promete | `RellenadorSap.EsLaPantallaDeTriage` (por programa `SAPLY000`) | `RellenadorSap.cs:48` |
| La sesión de hoy: el censo del triage tiene un paciente | `GIRALDO GIRALDO HERNAN DE JESUS`, formulario con signos vitales, Glasgow, motivo, conducta | capturas del dueño, 2026-09-02 |

## Por qué esto va dirigido por especificación

Lo mínimo que el portero exige y lo que de verdad puede fallar en silencio: mandar a SAP una
sección que el médico no aprobó, inventar un motivo de consulta cuando la nota no lo trae, y
escribir en una pantalla que no es el triage. Lo demás —llegar y escribir— ya tiene sus promesas
(56-59, 80-81) y su corrida real.

## La especificación

En continuación de la 111. Los números no se reciclan.

| # | Promesa | Fase |
|---|---|---|
| 112 | solo viaja lo marcado: una sección sin ✓ no entra en el envío a SAP | 1 |
| 113 | el motivo de consulta y la conducta salen de las secciones por su título; sin una sección que lo diga, quedan vacíos y no se inventan | 2 |
| 114 | fuera de la pantalla del triage no se escribe nada: el envío dice dónde está y para | 3 |

### Con qué se juzga cada una

| # | Cómo se juzga, sin pantalla |
|---|---|
| 112 | nota de 3 secciones, marcadas 2 → el encargo lleva esas 2 con título y texto tal cual; marcada ninguna → encargo vacío |
| 113 | secciones «Motivo de consulta», «Hallazgos», «Plan y recomendaciones» → motivo = la primera, conducta = la tercera; solo «Hallazgos» → las dos vacías |
| 114 | `sapgui://QAS/NWP1/SAPLY000/0100` → se puede; `sapgui://QAS/NWP1/SAPLN_WP_FRAMEWORK/0100` y `uia://explorer.exe/x` → no, y el motivo nombra la pantalla |

Nivel 4, a mano: el ✓ en la tarjeta, la llegada por batches, los campos escritos y releídos, los
dos editores, SAP sin grabar. Con log.

## Las fases

| Fase | Promesa | Qué toca |
|---|---|---|
| 0 | las tres en rojo | `Contrato.cs` |
| 1 | 112 | `Clinical/Encargo.cs` (nuevo, puro) |
| 2 | 113 | `Clinical/EditoresDelTriage.cs` (nuevo, puro) |
| 3 | 114 | `Clinical/EnvioAlTriage.cs` (nuevo, puro; usa `EsLaPantallaDeTriage`) |
| 4 | nivel 4 | `Clinical/PuenteASap.cs` (el puente estático entre las dos ventanas) · `FaceWindow.xaml.cs` (`LlegarAlTriageAsync` sacado de la demo + `EnviarEncargoAsync`: llegar → compuerta → `RellenarConNotaAsync` → editores) · `ConsultaWindow.cs` (el ✓ por tarjeta y «Todo a SAP», con el resultado en la tarjeta) |

## Lo que NO entra

- **Huecos, skills y piloto** (ramas aparcadas en el stash de la 006): se retoman cuando esto
  funcione de punta a punta, para ver qué de aquello suma.
- **Elegir paciente**: el censo de hoy tiene uno; se toma la primera fila viva, como la demo, y
  se dice cuál.
- **Grabar en SAP**: jamás. El médico revisa y graba.
- **Deshacer**: `RellenadorSap.Deshacer` existe; el botón es otra fase.

## Hallazgos

1. **2026-09-02** — El rojo de la fase 0 **no se vio antes del código**: las tres clases puras y las
   promesas se escribieron en la misma pasada, para cerrar la experiencia en el día. Lo que sí se
   vio, una por una, es el rojo por sabotaje: 112 «viajaron 3» al ignorar las marcas, 113 «no se
   pega la nota entera en Motivo» al caer a la primera sección, 114 «en el puesto de trabajo NO se
   escribe» al dejar pasar cualquier pantalla. Cada sabotaje se comprobó aplicado y restaurado.
2. **2026-09-02** — El batch 1 se manda primero como lo enseñó el dueño (comando → nwp1 →
   Continuar → Urgencias Adultos/Triage) y, si el terreno para a medias, por la puerta de Favoritos
   que usó la demo del 31. Los dos caminos quedan en el log con su cuenta; el nivel 4 dirá cuál
   sobra.
3. **2026-09-02** — La ventana de consulta del acceso directo del escritorio abre OTRO proceso: el
   de la build que apunte el `.lnk`. Para probar una rama hay que arrancar `U.exe --consulta` desde
   la carpeta de esa build, o el ✓ no existe.
4. **2026-09-02** — Escribir desde la ventana de consulta corre en el hilo de interfaz (es el mismo
   Dispatcher que la carita), que es justo lo que el COM de SAP exige; el exportador tenía que
   saltar al Dispatcher porque nacía en el pool. Aquí no hace falta, y no se añade.

## Cierre

- [x] 112-114 verdes (`.\scripts\contrato-del-grafo.ps1` → CONTRATO INTACTO, 114/114, 2026-09-02)
- [x] Rotas a propósito una por una, comprobado el sabotaje aplicado y restaurado (hallazgo 1)
- [x] Compila en Release; corre desde `C:\U-versiones\demo-entera\bin` con `--consulta`
- [ ] `.\scripts\verificar.ps1` con evidencia
- [ ] Corrida real: consulta dictada → ✓ → triage lleno en QAS, con log y horas
- [ ] Estado: **implementado** (AAAA-MM-DD)
