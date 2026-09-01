# medidor — el instrumento que mide el trabajo clínico

Subsistema nuevo (2026-09-01), aparte de `windows-client`. Mide cuánto trabajo operativo cuesta una
consulta —tiempo en PC y en el HIS, escritura, clics, navegación SAP, esperas, trabajo
post-atención— en tres fases con el MISMO instrumento (baseline sin Miracle → Notes → Notes+Ops),
para poder demostrar el impacto con datos propios. Spec: [`../docs/specs/003-medidor-del-trabajo-clinico.md`](../docs/specs/003-medidor-del-trabajo-clinico.md).

**Es un `.exe` separado de U.exe** (`UMedidor.exe`), a propósito. Conviven en el mismo PC del
hospital y solo comparten la capa SAP (referencia a `windows-graph`, sin tocarla).

## La regla que manda aquí: medir el trabajo, no vigilar al médico

Todo lo demás se subordina a esto, y va escrito como PROMESAS del contrato (rojas antes del código):

- **Nunca sale contenido del PC.** Ni texto tecleado (solo cantidades y ráfagas), ni títulos de
  ventana, ni valores de campos SAP, ni el sufijo `vista:` de la identidad. La promesa 1 lo prueba
  serializando un lote con un título hostil y exigiendo que no aparezca ni un fragmento.
- **El paciente es una huella, no un nombre.** El identificador visible en SAP se convierte en un
  HMAC irreversible EN EL PC (clave por día operativo, corte 06:00, fijada al abrir el turno) y el
  crudo se suelta en el acto. Sirve para saber que dos segmentos son la misma consulta (el A→B→A de
  urgencias) sin almacenar identidad.
- **Un indicador permanente y un atajo para parar** (icono de bandeja con estado + pausa). Sin eso
  esto sería una cámara oculta — lo dejó escrito la spec anterior (`medir-el-terreno.md`).

**Prohibido de facto** (el medidor no los referencia): `SapGuiSurface.ReadFields()` (CurrentValue),
`SapContextReader`, `WorkflowRecorder.Value`. La ÚNICA lectura de contenido permitida es el título
SAP / un campo por selector, y solo para pasarlo por la regla de extracción → HMAC → soltar.

## La forma: dominio puro juzgable + app delgada (como mapeador/Pulso)

| Proyecto | Qué es | Dónde se juzga |
|---|---|---|
| `Dominio/` | net8.0 **puro**: sesionización, cubetas, HMAC, normalización, spool SQLite, visitas SAP. Sin WPF, sin COM, sin pantalla. | **Contrato en CI y en Linux** — corre en < 1 s |
| `Contrato/` | Las promesas como pruebas que llaman al código real. `bronce/` tiene el stream SAP congelado. | `dotnet run --project medidor/Contrato` |
| `App/` | net8.0-windows WPF/tray: ganchos, sonda de primer plano, hilo SAP (STA, disciplina Busy), bandeja, subidor, enrolamiento. | **Nivel 4, sobre el PC real** (WPF + COM no compilan sin el SDK de Windows) |
| `Sonda/` | Consola de fase 0: contesta en sitio si dos engines COM conviven, el coste por tick, la regla del ID. | En el hospital, log con horas al PR |

**Lo que se puede juzgar sin pantalla se pone en `Dominio` a propósito.** Si un cambio necesita
mover lógica de medición a `App`, es señal de que se está escapando de su juez. El `App` es I/O:
lee la pantalla, cuenta input, y deja caer en el `Dominio` lo que este sabe agregar.

## El caño con el backend

`App` habla SOLO con Graph, saliente, `X-API-Key` (misma topología que U.exe: nunca un puerto
entrante, funciona tras el firewall del hospital). Contrato: `POST /api/v1/metrics/{enroll,config,batch}`.
El spool SQLite es durable y **lo contrario del TelemetryClient de U.exe**: no descarta en silencio
ni deja de reencolar — un estudio no puede perder datos. El formato de cable
(`Dominio/Cable.cs`) es espejo de `Graph/src/domain/metrics/vocabulario.js`; cambiarlo en un lado
sin el otro es deriva de contrato.

## Compilar y probar

```bash
dotnet run -c Release --project medidor/Contrato     # el juez (Linux/CI, verde = 21/21)
```

En Windows, además:

```powershell
dotnet build -c Release medidor/App/App.csproj        # UMedidor.exe
dotnet run   -c Release --project medidor/Sonda        # la sonda de fase 0, con SAP delante
```

El contrato entra a la compuerta como los demás: job `medidor` en `.github/workflows/contrato.yml`
(en Linux, porque el Dominio es puro) y paso en `scripts/verificar.ps1`. El portero pre-push queda
satisfecho porque la rama trae su `medidor/Contrato`.
