# Mirar a los usuarios: qué le piden a Ü y qué no consiguió

> Cómo un agente (o una persona) revisa, cada día, lo que los usuarios le pidieron a Ü y dónde falló —
> para convertirlo en el siguiente corte. Escrito el 2026-09-18, con los datos ya vivos en producción.

## Lo primero, porque ahorra el viaje: esto YA funciona

No hay que construir nada. La telemetría lleva meses corriendo y hay **106.334 eventos de 29 usuarios**
en Supabase. Lo único que faltaba era saber dónde mirar.

```
Ü escribe una línea de log  →  LogBus
                            →  EspejoDelLog (se engancha a LogBus, no a cada sitio que informa)
                            →  POST /api/v1/agent/events  (backend Graph, X-API-Key)
                            →  Supabase: tabla graph_windows_events
```

El espejo manda **cada línea del log**, así que lo que se ve aquí es exactamente lo que hay en
`%LOCALAPPDATA%\U\logs` de esa persona. No hay una segunda fuente que se pueda quedar atrás.

## Cómo lo lee Claude Code

Por el MCP de Supabase, que ya está conectado. Proyecto **`miracle-app`** (`zyvfamlhlmztliexvmej`),
tabla **`graph_windows_events`**:

| Columna | Qué trae |
|---|---|
| `email` | quién. Es la identidad canónica (el mismo correo en otra máquina = el mismo usuario) |
| `client_at` | cuándo, según el reloj de esa máquina |
| `kind` | `log` para lo reflejado; también `action`, `workflow_step`, `mcp`… |
| `phase` | la etiqueta del log: `voz-viva`, `mapa-mcp`, `mano`, `decisor`, `nucleo-paso`… |
| `label` | el texto de la línea |
| `detail` | jsonb con `{tag, text}` completo, por si `label` viene recortado a 500 |

**No hace falta pasar por el backend.** Las rutas del panel (`/api/windows/users/:email/events`) están
detrás de `requireProviderAdmin`, que es sesión de navegador: sirven para Provider Studio, no para un
agente. El MCP va directo a la tabla.

## Las cinco preguntas que valen, con su consulta

### 1. ¿Quién usó Ü, cuánto, y cuánto le falló?

```sql
select email,
       count(*) filter (where label like '%hice 0 de%')                          as fallos_clic,
       count(*) filter (where phase='voz-viva' and label like '%usuario dijo%')  as cosas_que_pidio,
       count(*) as eventos, max(client_at) as ultima_actividad
from graph_windows_events
where client_at > now() - interval '3 days'
group by 1 order by eventos desc;
```

### 2. ¿Qué le pidieron y qué no consiguió? (la que de verdad importa)

```sql
select to_char(client_at,'DD/MM HH24:MI') as hora, phase,
       left(regexp_replace(label,'\s+',' ','g'), 150) as linea
from graph_windows_events
where email = 'correo@del.usuario'
  and (label like '%usuario dijo%' or label like '%hice 0 de%' or label like '%no pude%')
order by client_at desc limit 40;
```

Intercala lo que la persona dijo con lo que falló justo después. Es la lectura que convierte un
«no funciona» en un corte concreto.

### 3. ¿En qué falla Ü, por tipo?

```sql
select case
  when label like '%no lo conozco%'              then 'pidio una puerta que no existe alli'
  when label like '%AHORA no lo veo%'            then 'la conoce pero no esta visible'
  when label like '%no encontre el elemento%'    then 'el selector no caso'
  when label like '%puertas vivas para%'         then 'nombre ambiguo (homonimos)'
  when label like '%no pude abrir ni encontrar%' then 'no pudo abrir la app/web'
  when label like '%no pude pulsar%'             then 'no pudo pulsar'
  when label like '%camino aprendido%'           then 'no sabe llegar'
  else 'otros' end as tipo_de_fallo,
  count(*) as veces, count(distinct email) as usuarios
from graph_windows_events
where client_at > now() - interval '7 days'
  and (label like '%hice 0 de%' or label like '%no pude abrir ni encontrar%' or label like '%camino aprendido%')
group by 1 order by veces desc;
```

### 4. ¿En qué pantallas se atasca?

```sql
select substring(label from 'en «([^»]*)»') as pantalla, count(*) as fallos
from graph_windows_events
where client_at > now() - interval '7 days' and label like '%hice 0 de%'
group by 1 having count(*) > 1 order by fallos desc limit 20;
```

### 5. ¿Cuánto tarda cada herramienta en la máquina de un usuario real?

```sql
select substring(label from '← \((\d+) ms\)')::int as ms,
       to_char(client_at,'DD/MM HH24:MI:SS') as hora,
       left(label, 120) as linea
from graph_windows_events
where email = 'correo@del.usuario' and phase='mapa-mcp' and label like '← (%'
  and client_at > now() - interval '1 day'
order by ms desc limit 20;
```

Los tiempos de la máquina del dueño no son los de un usuario: su PC, su red y su carga son otros.
Esta consulta es la que dice si un corte de velocidad sirvió **donde importa**.

## Lo que salió al estrenar esto (2026-09-18)

Primera lectura real, últimos 7 días:

| Tipo de fallo | Veces | Usuarios |
|---|---|---|
| no sabe llegar | 82 | 2 |
| no pudo pulsar | 53 | 2 |
| pidió una puerta que no existe allí | 51 | 3 |
| nombre ambiguo (homónimos) | 40 | 3 |
| la conoce pero no está visible | 34 | 2 |
| no pudo abrir la app/web | 12 | 2 |

Y en una usuaria real de ESIC, trabajando sobre SharePoint/Excel y Teams:

```
22:57  usuario dijo: «Puedes entrar a Chrome y mirar el control de pagos, por favor»
22:57  ← (23534 ms) no pude abrir ni encontrar «teams.microsoft.com» en el navegador
22:57  ← (5257 ms)  hice 0 de 1: «A21» no lo conozco en «…sharepoint.com/…/ControldePagos…»
22:57  ← (2471 ms)  hice 0 de 1: «barra de desplazamiento horizontal» no lo conozco
```

Tres hallazgos que ninguna prueba en la máquina del dueño habría dado:

1. **«no sabe llegar», 82 veces, es el fallo nº1** — y es justo lo que arreglaron las promesas 332/333
   ese mismo día. Estos 82 son de ANTES del arreglo: la próxima lectura dice si sirvió.
2. **Una celda de Excel (`A21`) no es una puerta para Ü.** Trabajar dentro de una hoja de cálculo en el
   navegador es un caso de uso real que hoy no está cubierto.
3. **Los nombres de ventana de Teams se piden como puerta** («(3) Equipos y canales | ESIC · Medellin…»),
   y no lo son. El modelo nombra lo que ve en la barra de tareas.

## El límite honesto, y es el que más duele

**Solo reporta quien completó el onboarding.** `TelemetryBus.Init` es un no-op sin correo
(`FaceWindow.InitTelemetry`: `if (_backend == null || !_config.Onboarded) return;`). Un usuario al que
se le manda el instalador y no pone su correo **no aparece aquí**: ni un evento, ni un heartbeat.

Es decir: la persona que más ayuda necesita —la que acaba de instalar y algo le falla— es
precisamente la que no se ve. Mientras eso siga así, esta consulta solo mira a los que ya funcionan.

Anotado como el siguiente corte de esta línea, y no es cosmético: sin resolverlo, «mejora continua»
solo cubre a los usuarios veteranos.
