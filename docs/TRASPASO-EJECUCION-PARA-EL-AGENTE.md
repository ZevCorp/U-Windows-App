# Contexto para el agente de código de quien lleva la ejecución

Eres el agente de un desarrollador que hereda el frente «ejecución» de Ü: que una nota clínica
dictada acabe escrita en SAP IS-H por medio de una tarea **enseñada** por un humano, sin guiones
fijos y sin inventar datos. El documento corto para la persona es
[`TRASPASO-EJECUCION.md`](TRASPASO-EJECUCION.md); este es el tuyo. Léelo entero antes de tocar
nada, y después lee `CLAUDE.md` y `.claude/rules/*.md`: son ley, no sugerencia.

Todo lo que sigue está medido en el SAP real (sistema QAS del Hospital General de Medellín,
transacción NWP1, formulario del triage `SAPLY000/0001`) entre el 2026-09-03 y el 2026-09-12.
Donde algo NO se ha medido, lo digo.

---

## 1. La tesis, y la ley física de este frente

**La ejecución sale de la enseñanza.** No hay una sola línea que sepa qué campo es el peso en el
triage. Lo sabe una *skill* que nació de una demostración humana, se **comprobó** recorriéndola con
un piloto, y guarda por cada campo un *hueco* con el nombre que el humano lee en pantalla. Cuando
la nota llega, un modelo elige la skill y llena los huecos con lo que la nota trae. Lo que no trae
queda en blanco. Grabar es del médico.

De ahí salen las cinco leyes, cada una con su promesa en `tests/ContratoDelGrafo/Contrato.cs`:

| Ley | Promesa |
|---|---|
| Un valor tecleado en la demo **jamás** se reproduce: es un hueco, y sin dato queda vacío | 123, 196 |
| Una skill sin comprobar **no se ejecuta**; el «no» dice qué falta | 127 |
| Reproducir una skill es **el mismo batch** que todo lo demás: una compuerta, una cuenta | 122 |
| Cada paso exige su **llegada** (la pantalla a la que la demo llegó); acabar en otro sitio no es haberlo hecho | 103, 140 |
| La skill que termina en Grabar **se detiene antes** | 126 |

Y la ley de la casa que las envuelve: **ninguna línea de producción entra antes que la promesa que
la juzga** (`.claude/rules/flujo-sdd.md`). Rojo → verde → sabotaje → PC real. Sin excepciones. Yo
mismo, en esta rama, encontré dos pruebas que no probaban nada solo porque el sabotaje me obligó a
romperlas (spec 016, promesa 200; spec 019, promesa 226).

## 2. El circuito, con sus archivos

```
🎓 ENSEÑAR ──────────► LECCIÓN ──────────► ▶ COMPROBAR ──────────► SKILL COMPROBADA
WorkflowTeachSession   Teach/Leccion.cs     Piloto/ElPiloto.cs      Navigation/SkillEnsenada.cs
ElCierreDeLaDemo       LeccionEnDisco.cs    agente-piloto/piloto.mjs   (Pasos, Huecos, DeLaLeccion,
ElCruceQueVioSap       MensajeDeLaLeccion   RegistroDeLaComprobacion    Comprobada, DondeTermina)
                       ArmarLaLeccion       PlanDeComprobacion
                                            SkillDeLoVerificado
                                                    │
        ✓ NOTA ──────────► ENCARGO ──────────► PILOTO (modo encargo) ──────────► map_skill_run
        ConsultaWindow     Clinical/Encargo    piloto.mjs --encargo=            SurfaceMapTools.CorrerSkill
        PuenteASap         MensajeDelEncargo   CajasDelPiloto.CajaDelEncargo    InstanciarSkill.Pasos
        FaceWindow.        (nota + catálogo                                     RecorrerSkill → DarUnPasoConCoreografia
        EnviarEncargoAsync   con huecos)                                         → RecorrerSegunElNucleo (el batch)

        🧠 PANEL ────────► ConsultaWindow (AbrirAprendizajes, PintarLaLista, PintarFicha, PintarCapturas)
                           PuenteDeAprendizajes.Mostrar → FaceWindow.MostrarAprendizajeAsync
                           LoQuePasaAlMostrar.Decidir · LoQueHaceLaSkill · CapturasDeLaSkill
```

### Etapa 1 — Enseñar (spec 013, promesas 168–174; spec 014, 184–189)

- `Teach/WorkflowTeachSession.cs`: abre la cámara (`CamaraDeCuadros`, un JPG cada 250 ms), el vigía
  de clics (todos, con su punto), el grabador de SAP (`windows-graph` `SapGuiSurface.StepObserved`)
  y la transcripción. Al cerrar: `ElCierreDeLaDemo.Orden` **descarga lo tecleado LO PRIMERO** (187),
  arma la lección y guarda una skill provisional (`Comprobada=false`, huecos por la regla del
  narrado y el criterio del modelo, 134).
- `Teach/Leccion.cs`: `ArmarLaLeccion.Eventos` cuelga pasos de SAP y frases de cada clic por
  identidad primero y por cercanía después (171, 184). `ConLaIdentidadDeSap` ANTES de `Llegadas`
  (178): al terreno se le pregunta con la puerta que SAP vio, no con la que el vigía adivinó.
- `Teach/ElCruceQueVioSap.cs` (189): el grabador de SAP enseña al terreno la arista «puerta →
  pantalla nueva» cuando el mapa vivo no llega a atribuirla (round-trips lentos, >6 s).
- Formato en disco, `%LOCALAPPDATA%\U\lecciones\<id>\` (o `<datos>\local\U\lecciones` en la app de
  desarrollo): `leccion.json` (`Id, Empezo, Termino, Eventos[N, HoraMs, Tipo, X, Y, Selector,
  Etiqueta, Texto, Tecla, Llegada, CuadroAntes, CuadroDespues, Asentado, Dicho[], PorTeclado],
  Frases, Cuadros, Contexto`), `cuadros/*.jpg`, `demo.mp4`, y después `mensaje.json`.

### Etapa 2 — Comprobar (spec 013, 175–179; spec 014, 180–192)

- `Piloto/ElPiloto.cs` lanza `node agente-piloto/piloto.mjs --leccion=<carpeta>` (Claude Agent
  SDK, `bypassPermissions`, MCP de la app en `127.0.0.1:8790/mcp`). `Argumentos(script, carpeta,
  modo)` decide `--leccion=` o `--encargo=` (195).
- `Piloto/CajasDelPiloto.cs`: la caja de comprobar tiene manos desde el principio y **prohíbe por
  nombre** `map_batch` y `map_skill_run` (174). La del encargo es al revés: solo `map_skills`,
  `map_skill_run`, `map_go_to`, `map_open_app`, `map_where_am_i`, `map_what_i_see`, `map_shot`,
  `voz_decir`; todo lo demás prohibido, sin `voz_preguntar` (194).
- El piloto entrega un **plan** (`leccion_plan`, `Piloto/PlanDeComprobacion.cs`) y la app lo
  recorre en `FaceWindow.RecorrerElPlan` → `DarUnPasoConCoreografia` (191): señalar con la carita,
  decir por la voz prestada (192, `VozPrestada.cs`, `conversation:"none"`), colgar y mostrar el
  recuerdo (180), actuar por el batch, juzgar.
- **El juez** es `Piloto/RegistroDeLaComprobacion.cs`. `EventosQueCuentan` = eventos que navegan
  (llegada distinta de la anterior) + el último tecleo por campo (175); **un evento sin selector ni
  etiqueta no cuenta** (227). Un tecleado está hecho si el campo **dice ahora** lo que la demo
  tecleó, leído por SAP (`ValorActual`), no declarado. Un navegante aterrizó si
  `ElRescate.Aterrizo(esperada, aqui)`, que compara por `Superficies.MismaPantalla` (226).
- `Piloto/SkillDeLoVerificado.cs` empaqueta **solo lo que aterrizó** (176); `Comprobada` solo si
  aterrizó todo lo que cuenta; `DeLaLeccion = leccion.Id` (198); `DondeTermina = leccion.Termino`
  (227); **la llegada viaja solo con los pasos que navegan** (229: para un campo, `v.Real` es el
  valor leído, no una pantalla); el nombre del dato es la **etiqueta del campo** (196:
  `NombreDelDato`: etiqueta, si no lo dicho, si no el nombre técnico; el campo de comandos `okcd`
  nunca es hueco). Se guarda con `GuardarComoElUnicoDeSuLeccion` (228): retira la provisional de la
  demo.

### Etapa 3 — La skill en disco

`%LOCALAPPDATA%\U\skills\<nombre saneado>.skill.json` — **compartida** entre la app estable y la de
desarrollo (usa `SpecialFolder.LocalApplicationData` directo; las lecciones no). Campos:
`Nombre, Description, DondeEmpieza, DondeTermina, Pasos[Exit, Texto, Llegada, Dicho],
Huecos[Campo, Significado, Accion, Ejemplo], Sugerencias, Comprobada, DeLaLeccion`. `Exit` es la
puerta **por su nombre** (etiqueta) cuando la hay; el selector es el respaldo. `Catalogo(carpeta)`
devuelve `SkillAnunciada(Nombre, Description, Archivo, Comprobada){Huecos}` (193). `Renombrar`,
`Borrar` (201).

### Etapa 4 — El ✓ (spec 015, 193–197)

`ConsultaWindow.EnviarASapAsync` → `Encargo.De(nota, clavesMarcadas)` (112: solo lo marcado) →
`PuenteASap.Enviar` → `FaceWindow.EnviarEncargoAsync`: `MensajeDelEncargo.Armar(encargo,
catalogo)` (solo comprobadas; con sus huecos), presta la voz, `SenalarAlActuar=true`, lanza el
piloto en modo encargo y devuelve `r.Ultimo`. El piloto elige por su criterio (`SISTEMA_ENCARGO`
en `piloto.mjs`), arma `datos` `{"<nombre del hueco>": "<valor>"}` y llama `map_skill_run`.
`SurfaceMapTools.CorrerSkill`: `PuedeCorrer` (127) → `InstanciarSkill.Pasos(skill, datos)` (huecos
por `Significado`, exacto y luego contenido; sin dato → paso omitido) → si hay coreografía y
`SenalarAlActuar`, `RecorrerSkill` de uno en uno (197), si no, el batch → la cuenta nombra lo que
quedó **en blanco** (`InstanciarSkill.SinDato`, 196) y lo que no tuvo hueco.

El camino fijo de la spec 008 (`EnvioAlTriage`, `EditoresDelTriage`, `LlegarAlTriageAsync`) **ya
no existe**; 113 y 114 están retiradas con el dueño. `EjecutorDeExportaciones` + `RellenadorSap`
son otro camino (la cola del Graph) y no se tocaron; 115/116 siguen.

### Etapa 5 — El panel (spec 016, 198–201 y 225)

`ConsultaWindow`: icono del cerebro en la cabecera (no un tercer segmento del carril: el carril es
del paciente, los aprendizajes son del asistente), lista (listos arriba, «Sin repasar · N» plegado),
ficha (una acción: «Mostrar»; `LoQueHaceLaSkill.EnCastellano` sin selectores, 199; huecos como
fichas; «Ver capturas» solo si `DeLaLeccion` tiene carpeta viva, `CapturasDeLaSkill.De` empareja por
identidad, 200; renombrar y eliminar al fondo). `LoQuePasaAlMostrar.Decidir(skill, hayManos)` (225):
`correr` (comprobada: `RecorrerSkill` sin datos, con coreografía), `comprobar` (pendiente:
`FaceWindow.ComprobarAsync(progreso)` con `_leccionParaComprobar` fijada, y **espera** el
veredicto), `no` (sin puente). Lo que el panel deja fuera y por qué está en la spec 016: léelo antes
de añadirle nada.

## 3. Hechos de SAP que no se deducen del código (medidos)

- **Dentro de SAP GUI, UIA no ve nada**: un `Pane` opaco. Todo es SAP GUI Scripting (COM, enlace
  tardío, ProgID `SapROTWr.SapROTWrapper`). `uia://saplogon.exe/…` **no es una llegada** (178).
- **`FindByPosition` devuelve null en este SAP, siempre** (8 de 8 medidos). Quién está bajo un
  punto se decide por geometría: `ElMasPequenoQueContiene` (186).
- **Un `GuiComboBox` se fija por `Key`, no por `Text`**; `ClaveDeLaOpcion` casa clave o texto sin
  acentos ni mayúsculas (190).
- **SAP publica lo tecleado solo al round-trip**: hay que descargar (`DescargarLoPendiente`) antes
  de armar la lección (187).
- **La pantalla a medio cambiar**: durante un round-trip la sesión dice transacción
  `SESSION_MANAGER` con un programa que ya no es `SAPLSMTR_NAVIGATION`. `Superficies.MismaPantalla`
  las iguala; **es la única función de igualdad** que usan el batch (dos sitios) y el juez (226).
- La identidad de un campo: `sap:wnd[0]/usr/…/txtY0000000-ZTXTPESO`; el grafo la guarda con `sap:`
  y el lector la devuelve sin él. **Normaliza siempre por `InstanciarSkill.Identidad`** (aprendizaje
  nº16 de `CLAUDE.md`: comparar identidades de distinta forma da falso siempre y en silencio).
- El botón «Triage» de la rejilla ALV es `#tbbtn=ZMEDTRIAGE`; una fila se elige por selección,
  cursor o por ser la única (182). La diastólica tiene etiqueta «/».

## 4. Cómo se corre todo (Windows, comandos exactos)

```powershell
git config core.hooksPath .githooks                 # el portero: compila, contratos, y exige promesa
.\scripts\contrato-del-grafo.ps1                    # nivel 2, ~90 s: «CONTRATO INTACTO» o rojo con motivo
.\scripts\contrato-de-la-voz.ps1                    # el contrato de la voz (32)
.\scripts\verificar.ps1                             # los cuatro niveles y out\evidencia.md para el PR
$env:U_PILOTO = "<repo>\agente-piloto\piloto.mjs"   # el piloto de ESTA rama, no el de otra
.\scripts\dev-paralelo.ps1                          # app de desarrollo en C:\U-dev2 (datos y logs aislados)
```

- Log de la app de desarrollo: `C:\U-dev2\local\U\logs\u-AAAAMMDD.log`. Etiquetas útiles:
  `leccion:`, `comprobar:`, `piloto:`, `aprendizajes:`, `envio:`, `skill:`, `mapa-mcp:`.
- El MCP de la app: `POST http://127.0.0.1:8790/mcp` con `tools/list` y `tools/call`. Sirve para
  medir sin la UI: `map_skills`, `map_shot` (devuelve imagen), `map_what_i_see`.
- El piloto a mano: `node piloto.mjs --leccion=<carpeta>` o `--encargo=<carpeta con mensaje.json>`,
  `--model=claude-sonnet-5 --turnos=12` para humo barato. Escribe una línea JSON por evento.
- Sabotaje: un script que rompe UNA regla por promesa, guarda `.sano`, corre el contrato, sana y
  compara huellas SHA1 de los archivos. Hay tres de ejemplo en esta sesión; el patrón está en las
  specs 015, 016 y 019 («Primera tanda»).

## 5. Gotchas ya pagados (no los repitas)

1. **Los archivos son CRLF.** Un ancla multilínea con `\n` no casa nunca y el sabotaje «pasa» sin
   haberse aplicado (`CLAUDE.md`, 2026-08-21). Normaliza `\n → \r\n` en tus scripts, y verifica que
   el sabotaje se aplicó ANTES de leer el contrato.
2. **Un ancla que salta un comentario falla.** El código tiene comentarios largos entre líneas
   contiguas: ancla en una línea única, no en bloques que asumas seguidos.
3. **PowerShell lanzado desde Bash escribe UTF-16 con mojibake.** Corre los scripts del repo con la
   herramienta de PowerShell y redirige con `*>`; la salida queda greppeable.
4. **`Path` es ambiguo** entre `System.IO` y `System.Windows.Shapes` en `ConsultaWindow.cs`: cualifica
   las formas, no las rutas.
5. **`dev-paralelo.ps1` a veces cierra la instancia anterior y no lanza la nueva**: repítelo hasta
   que `Get-Process U` la muestre. Y cierra instancias **por ruta**: `Get-Process U | Where Path
   -like "C:\U-dev2\*"`. El puerto 8790 lo «posee» el PID 4 (http.sys): no sirve para saber qué
   proceso es el dueño.
6. **Las skills y las fotos de recuerdos son compartidas** entre la app estable y la de desarrollo;
   las lecciones y los logs no. Una skill con `DeLaLeccion` apuntando a una lección de otra carpeta
   no tendrá capturas.
7. **El veredicto «N promesa(s) incumplida(s)» cuenta aserciones**, no promesas. Cuenta las líneas
   que empiezan por `✘ NNN.`.
8. **Un `Progress<string>` desde `ConsultaWindow` y `SetStatus` de `FaceWindow` viven en el mismo
   hilo de interfaz** (misma app, un Dispatcher). La coreografía de un paso hace `Thread.Sleep`: si
   la corres desde la UI, `Task.Run`.
9. **La voz en vivo durante comprobación y encargo es PRESTADA** (192): sin herramientas y sin
   turno propio. Si ves `voz-viva: llamada recibida` durante una comprobación, alguien dejó de
   prestarla.
10. **`map_shot` desde el MCP captura la pantalla que está delante**, no la ventana de Ü. Para
    fotografiar el panel hay que traerlo al frente (`SetForegroundWindow` desde PowerShell).

## 6. Estado exacto

**Rama:** `jose/el-check-corre-una-skill`, rebasada sobre `main` en `ca1e12e` (#64, Jero). **Todo
el trabajo de este frente está sin commitear**: 26 archivos, tres specs (`015`, `016`, `019`).
Contrato intacto: **199 promesas verdes**. Cada spec tiene su sección «Primera tanda» con lo medido
y el sabotaje. El repo pide una spec por rama: decidir con Jose cómo entra.

**Probado en el PC real:**
- Enseñar y comprobar el triage entero (spec 014: trece pruebas). Última comprobación limpia,
  2026-09-11: 17 de 19, y los dos fallos son las promesas 226 y 227.
- El panel pintando con datos reales; `Mostrar` lanzando la comprobación de SU lección.
- El ✓ con cero comprobadas: rechazo correcto, 0,66 USD.
- El piloto en modo encargo por humo (`mensaje.json` a mano): elige y se niega bien.

**NO probado en el PC real (lo primero):**
- **El ✓ de punta a punta con una skill comprobada.** Requiere re-comprobar la lección
  `leccion_20260911_112651` (con 226–229 debería dar 18 de 18), grabar una consulta con signos
  vitales y pulsar ✓. Vigila en el log `envio: … 1 skill(s) comprobada(s)`, `skill: corriendo «…» ·
  con coreografía` y la cuenta con «Quedaron EN BLANCO».
- `Mostrar` esperando al veredicto (construido hoy, no visto).

**Límites conocidos (cada uno es una spec):**
- La fila del paciente («GIRALDO») es un paso fijo, no un hueco. Con otro paciente para ahí.
- Radios y casillas: se graban como clic sin valor, el juez no los cuenta, la skill no los
  distingue. Fechas con ayuda, controles de tabla, pestañas, diálogos modales: nunca ejercitados.
- La diastólica se llama «/». Nombrar por el vecino o por el nombre técnico cuando la etiqueta no
  es una palabra.
- El panel no enseña desde el panel (🎓 vive en la carita) a propósito.
- `docs/mapa/` es de otra sesión paralela del dueño («Teach workflow/skill visual polish»); no es
  de este frente y no se toca.
- Los avisos de Slack de los PR #60 y #62 siguen pendientes de pegarse.

## 7. Cómo empezar

1. `contrato-del-grafo.ps1` verde. Si no, nada de lo de arriba es cierto para ti.
2. Lee `docs/specs/015`, `016`, `019` completas, incluida «Lo que NO entra».
3. Levanta la app de desarrollo con `U_PILOTO` apuntando al piloto de tu rama. Abre SAP QAS en Easy
   Access. Abre la consulta desde la carita, entra a Aprendizajes, pulsa «Mostrar». Lee el log
   mientras corre.
4. Antes de cambiar comportamiento: escribe la frase que hoy es falsa y mañana será verdadera. Esa
   frase es la promesa, y va al contrato antes que el código.
