# Operations pilota Miracle Notes por API

El cerebro puede ahora trabajar consultas de Miracle Notes **sin tocar una pantalla**: las
herramientas `notes_*` (crear consulta, elegir/crear plantilla, guardar el dictado, generar
y ajustar la nota, listar el historial) las declara Graph y las ejecuta este cliente contra
`/api/clinical/*` con la identidad del equipo. El médico llega después, revisa en el portal,
firma y exporta — **eso nunca lo hace el equipo**: esas rutas no aceptan aparatos, y firmar
ni siquiera existe en Graph.

Contrato y matriz de auth: `docs/operations-notes-lane.md` del repo Graph.

## Las piezas de este repo

| Pieza | Dónde | Qué hace |
|---|---|---|
| `ClinicalApiClient` | `windows-graph/src/Clinical/` | HTTP del carril: enroll, código de emparejamiento, plantillas/consultas/nota. Errores con CÓDIGO (`DEVICE_NOT_PAIRED` ≠ fallo) |
| `ClinicalContracts` | ídem | Espejo C# de las respuestas. Se verifica EJECUTANDO: `verify-clinical-contract-mirror.js` |
| Identidad del equipo | `GraphConfig` | `DeviceId` estable + token per-install **protegido con DPAPI** (`SetDeviceToken`/`GetDeviceToken`; respaldo plano solo si DPAPI falla) |
| `ClinicalMcpRunner` | `windows-client/src/Mcp/` | Ejecuta las `notes_*` que decide el cerebro; buffer de dictado local; enrolamiento automático la primera vez; al `DEVICE_NOT_PAIRED` pide el código y lo muestra/dice |
| Dictado continuo | `VoiceIO.StartDictation/StopDictation` | Corre de abrir la consulta a generar la nota; lo oído va DIRECTO al buffer del runner — **jamás al modelo** |

Reglas de PHI: este cliente loguea ids y status; nunca transcripciones ni cuerpos de nota.
El código de emparejamiento tampoco se loguea (se muestra y se dice, no se persiste).

## Cómo se enrola y vincula un equipo (flujo real)

1. Primera llamada `notes_*` sin token → el runner **se enrola solo** con la key del build
   (`POST /api/v1/enroll`; esa key ya no puede hacer nada más) y guarda el token con DPAPI.
2. Si nadie ha vinculado un médico → Graph responde `DEVICE_NOT_PAIRED` → el runner pide un
   código, lo pinta en el estado de la carita y lo dice por voz.
3. El médico entra a **Miracle Notes → Equipos** y teclea el código (10 minutos, un solo
   uso). Desde ahí, el equipo actúa a su nombre; él lo desvincula desde esa misma página.

## Probar sin backend real (banco de pruebas)

```powershell
# En una máquina con los dos repos lado a lado y `npm install` hecho en Graph:
node scripts/fake-graph-clinical.js --scenario paired      # imprime GRAPH_BASE_URL/KEY/TOKEN
node scripts/fake-graph-clinical.js --scenario unpaired    # para ensayar el emparejamiento
node scripts/fake-graph-clinical.js --self-test            # la cadena entera, sola: 11 checks
node scripts/verify-clinical-contract-mirror.js            # espejo C# vs respuestas reales
```

El arnés NO inventa el contrato: levanta las rutas reales del repo Graph con su base falsa
en memoria y un LLM enlatado. Si U.exe pasa contra el arnés, habló el contrato real.

## Checklist manual en Windows (lo que este entorno no puede verificar)

- [ ] Compila: `cd windows-client && dotnet build -c Release` (nuevo paquete:
      `System.Security.Cryptography.ProtectedData`).
- [ ] Primer arranque contra el arnés `unpaired`: se enrola solo, `graph.json` queda con
      `DeviceTokenProtected` (base64, NO el token en claro) y `DeviceId`.
- [ ] `DEVICE_NOT_PAIRED` → el código aparece en el estado de la carita y se dice por voz.
- [ ] Canje (curl del arnés o la página Equipos contra Graph real) → la misma herramienta
      reintenta y funciona.
- [ ] Dictado: abrir consulta por voz → hablar → `notes_guardar_dictado` guarda LO DICHO
      literal (comparar contra el portal). Convivencia mic dictado ↔ push-to-talk: es la
      limitación conocida; si pelean, el dictado gana mientras la consulta esté abierta.
- [ ] `notes_generar_nota` con el Graph real: si tarda >75 s, el runner pasa a sondear y
      termina bien (el rescate del backend cierra la nota).
- [ ] El borrador aparece en el historial del médico; ajustar la nota desde el equipo
      refresca el historial (`mirror.refreshed`), y si el médico editó en el portal, NO lo
      pisa (`web_edito`).
- [ ] Revocar el vínculo desde Notes → la siguiente `notes_*` vuelve a pedir código.
