# Desarrollar sobre el grafo de navegación

Esto es para quien llega a construir sobre el grafo **limpio**. No cuenta la historia del repo; cuenta
qué hay hoy, dónde tocar, y qué no tocar.

Antes de nada, lee [`CLAUDE.md`](../CLAUDE.md) — «EL CICLO», los ocho pasos. Este documento asume ese
ciclo y solo añade lo que es propio del grafo.

---

## 1. Lo primero: qué es viejo y qué es nuevo

El repo tiene **dos** sistemas de navegación conviviendo. Trabajas sobre el segundo.

| | dónde | tamaño | qué hacer |
|---|---|---|---|
| **VIEJO** | `windows-client/src/Navigation/SurfaceMap.cs` | 2.063 líneas | **no lo toques** |
| | `versiones/nucleo/v0.cs`, `v1.cs` | 3.620 líneas | fotos congeladas, solo para consultar |
| **NUEVO** | `nucleo/Grafo/Grafo.cs` | 418 líneas | aquí vive el grafo |

El viejo sigue enchufado porque hay pantallas que solo existen ahí. Se va apagando solo, a medida que
cada capacidad se muda. **Ninguna capacidad nueva debe apoyarse en él.**

Cómo saber de un vistazo dónde estás: si el archivo dice `SurfaceMap`, es lo viejo. Si dice `Nucleo`
o termina en `SegunElNucleo`, es lo nuevo.

---

## 2. El núcleo en una página

`nucleo/Grafo/Grafo.cs`. **No depende de nada**: ni WPF, ni UIA, ni Neo4j, ni de este cliente. Por eso
se puede juzgar entero en un segundo y sin abrir una ventana — y por eso hay que mantenerlo así.

Tres tipos, y no hacen falta más:

```csharp
record Elemento(string Selector, string Etiqueta, string Tipo);   // algo que se ve
record Alcanzable(Elemento Que, bool Vivo, string Destino);       // algo a lo que se llega
record Recuerdo(string Significado, string Foto, DateTime Cuando); // algo que te enseñaron
```

Y estos verbos:

```csharp
void Estoy(ubicacion)                      // dónde estoy — SOLO esto lo decide
void Observar(ubicacion, visibles)         // qué hay ahí. NO dice dónde estás
void Recordar(ubicacion, elementos)        // «esto existe ahí», sin afirmar que se vea ahora
bool Cruzar(ubicacion, selector, destino)  // «pulsar esto, desde ahí, llevó allá»
IReadOnlyList<Alcanzable> DesdeAqui(u)     // qué se alcanza, con su bandera Vivo
Camino ComoLlego(desde, hasta)             // el SIGUIENTE paso, no una ruta
bool Ensenar(ubicacion, selector, que, foto)  // crear un recuerdo
IReadOnlyList<...> RecuerdosDe(ubicacion)     // lo enseñado ahí, en orden estable
```

### Las cuatro reglas que explican casi todo

**1. Vivo ≠ recordado.** `Observar` no borra: lo que deja de verse se marca `Vivo = false` y se
queda. Un menú que se cierra no se olvida; deja de estar vivo. Si mezclas las dos cosas, el
asistente intenta pulsar lo que no está delante.

**2. Observar no dice dónde estás.** Leer la pantalla tarda ~400 ms; la ubicación se mira cada
250 ms. Si observar fijara la ubicación, el lento pisaría al rápido y el grafo se quedaría clavado en
la app anterior. Quien dice dónde estás es `Estoy`, y solo él.

**3. Un paso, no una ruta.** `ComoLlego` devuelve el **siguiente** paso. Una ruta completa es una
promesa sobre el futuro, y la pantalla cambia mientras la recorres.

**4. No se inventa nada.** `Cruzar` rechaza un destino de un elemento que nunca se vio ahí.
`Ensenar` rechaza un recuerdo sobre algo que nunca se vio ahí. Un dato sin sujeto no se guarda.

### Neo4j

`nucleo/Grafo/ProyectorNeo4j.cs` — **aparte a propósito**. El grafo no sabe que existe una base de
datos; si lo supiera, no se podría probar sin levantar una y la primera excusa para no probarlo sería
«hoy no está Neo4j». Aquí solo se proyecta lo que el grafo ya decidió: **ninguna regla vive en este
archivo**.

Dos cosas que ya costaron caro y conviene no re-aprender:

- **Vacío significa «no lo tengo», nunca «bórralo».** Escribir el significado con un `SET`
  incondicional destruyó dos recuerdos reales: el núcleo no los tenía cargados y escribió cadena
  vacía encima de los guardados.
- **Leer la memoria y proyectar necesitan lo contrario.** La proyección es un latido que no puede
  bloquear (5 s); la restauración ocurre una vez al arrancar y tiene que salir bien (60 s). Con el
  mismo timeout, el día que el grafo creció, Ü arrancó sin memoria — y por fuera eso no se ve como un
  fallo de red, se ve como «no me has enseñado nada aquí».

---

## 3. Cómo se añade una capacidad

El patrón está en `windows-client/src/Navigation/*SegunElNucleo.cs`. Hay cinco hechas:
`AquiSegunElNucleo`, `AbrirSegunElNucleo`, `PulsarSegunElNucleo`, `IrSegunElNucleo`, y el señalar de
`LoQueSenalas`.

**El reparto, que es lo único que hay que entender:**

```
UIA / la pantalla          →  se queda en windows-client (SurfaceMapTools, los lectores)
decidir qué se contesta    →  vive en Navigation/, sin pantalla, y por eso se puede juzgar
el grafo                   →  nucleo/Grafo, sin nada
```

La pregunta para saber dónde va un trozo de código: **¿puede equivocarse en silencio?** Si sí, no
puede vivir donde haga falta una pantalla para probarlo.

Ejemplo — `PulsarSegunElNucleo` no sabe pulsar. Recibe delegados (`_donde`, `_pulsar`), decide qué
significa lo que pasó, y devuelve una frase. Resolver el selector y escalar al doble clic siguen
siendo de UIA.

```csharp
public Resultado Pulsa(string selector, string etiqueta)
{
    string desde = _donde() ?? "";
    if (!_pulsar(selector, etiqueta)) return new(false, ..., "no pude pulsar…");
    string hasta = EsperarACambiar(desde);
    if (hasta.Length == 0 || hasta == desde) return new(true, false, ..., "…y la pantalla no cambió.");
    bool aprendido = _grafo.Cruzar(desde, selector, hasta);
    return new(true, true, desde, hasta, aprendido, "…ahora estás en «{hasta}».");
}
```

**Verificar por consecuencia.** Una acción es una petición, no una llegada. Pulsar y que no cambie
nada no se cuenta como haber llegado. Esto tiene su propia promesa (la 44) porque se rompió de verdad.

---

## 4. Los contratos

Cuatro, separados a propósito: mezclarlos haría imposible saber cuál se rompió.

| contrato | promesas | qué juzga | cómo se corre |
|---|---|---|---|
| `nucleo/Contrato` | 18 | el grafo, aislado | `dotnet run --project nucleo\Contrato` |
| `tests/ContratoDelGrafo` | 54 | las capacidades del cliente | `.\scripts\contrato-del-grafo.ps1` |
| `mapeador/Contrato` | 24 | el mapeador | `dotnet run --project mapeador\Contrato` |
| `voz/Contrato` | 10 | el collar | `.\scripts\contrato-de-la-voz.ps1` |

El del núcleo comprueba además la fidelidad contra Neo4j si lo encuentra. Para juzgar la ida y vuelta
necesita la base **para él solo** — con la app corriendo se salta esa parte y lo dice. Levanta uno de
usar y tirar:

```bash
docker run -d --name u-neo4j-prueba -p 7475:7474 -e NEO4J_AUTH=neo4j/grafo-local-2026 neo4j:5-community
```

y córrelo con `U_NEO4J_HTTP=http://127.0.0.1:7475`.

### Escribir una promesa

Va **antes** que el código y tiene que **poder ponerse roja**. Se escriben en castellano y diciendo
por qué importa, no qué hace la función:

```csharp
Debe(!g.Ensenar(donde, "uia:aid=jamas-visto", "esto es el total"),
    "el significado de algo que nadie ha visto aquí NO se guarda: sería una frase sin sujeto, "
    + "y después nadie sabría a qué se refería");
```

**El sabotaje también hay que comprobarlo.** Rompe el código a mano, corre el contrato, y mira que la
promesa se ponga roja *por la razón que dice*. Si sale verde, o tu promesa no vale o el sabotaje no
se aplicó — y esto último pasa más de lo que parece:

> ⚠ **Los archivos son CRLF.** Un `sed`/`python` que busque `\n` al final de línea **no encuentra
> nada y no falla**. Ha producido un contrato verde sobre un sabotaje que nunca se aplicó, más de una
> vez. Después de sabotear, **comprueba que el archivo cambió** (`grep -c`) antes de creerte el
> resultado.

---

## 5. Probarlo sobre el PC real

El contrato juzga la lógica sin pantalla. Que UIA conteste, que SAP responda, que el recuadro se
pinte: eso solo lo dice la máquina.

```powershell
# compila a un directorio propio; NUNCA sobre la instalación estable
dotnet build windows-client\WindowsClient.csproj -c Release -o C:\U-versiones\v1\bin
$env:U_MCP_PROBE = "1"     # abre la sonda en 127.0.0.1:8791
Start-Process C:\U-versiones\v1\bin\U.exe
```

Con la sonda puedes ejecutar cualquier herramienta sin hablar:

```bash
curl -s -X POST http://127.0.0.1:8791/mcp/ -H "Content-Type: text/plain" \
  -d '{"tool":"map_recuerdos","args":{"cual":"2"}}'
```

Y preguntarle al núcleo dónde se cree que está:

```bash
curl -s http://127.0.0.1:8792/nucleo
```

**Dos trampas del arnés**, ambas encontradas dando resultados falsos:

- El `curl` desde otra ventana **puede robar el foco** y cambiar la ubicación entre dos llamadas.
  Encadena las llamadas en un solo comando.
- PowerShell con `ConvertTo-Json` **no entrega bien los argumentos** a la sonda. Manda el JSON crudo
  con `-ContentType "text/plain"`.

**Lee el log siempre**: `%LOCALAPPDATA%\U\logs\u-AAAAMMDD.log`. Ahí están los canales `recuerdo`,
`mapa-vivo`, `mapa-mcp`, `voz-viva`, `rastro`. Casi todo lo que ha costado un día de esta sección se
encontró leyendo ese archivo, no el código.

---

## 6. Lo que este sistema ha aprendido a golpes

No son opiniones de estilo. Cada una costó una sesión.

**Una petición no es una garantía.** Pedirle algo al modelo en el prompt —«guarda lo que te
enseñen», «cuéntalos de uno en uno»— falla en cuanto deja de apetecerle. Tres veces seguidas en un
día. Lo que se quiera garantizar va en código: una clase pura que decide, y una promesa que la juzga.
`UnaLeccion` y `ElTurnoDeContar` nacieron así.

**Lo que falla en silencio es lo caro.** El fallo no era que no aprendiera; era que decía «lo tengo
en mente» y no guardaba nada, y desde fuera eso es idéntico a haber aprendido. Cuando algo pueda
omitirse, haz que la omisión **se vea**: un aviso en pantalla y una línea en el log.

**Una fuente para cada pregunta.** «¿Dónde estoy?» la contesta el localizador, nunca una foto.
«¿Hay algo iluminado?» lo contesta `Senalador`, y por eso Escape funciona — cuando algo pintaba por
fuera de él, Escape no lo apagaba. Dos sitios que saben de lo mismo divergen sin avisar.

**Verifica tú, no el usuario.** El contrato en verde no es haberlo probado. Levanta la app, ejecuta,
lee el log, cuenta las ventanas del proceso si hace falta. Un fallo de hilos que compilaba y pasaba
el contrato solo apareció al enumerar las ventanas y ver que la que debía existir no estaba.

**El hilo importa.** Las herramientas llegan por el servidor HTTP, en un hilo cualquiera. Crear o
leer cualquier cosa de WPF desde ahí revienta. Resuélvelo **dentro** de la clase que pinta, no en cada
llamador: el día que la llame alguien nuevo, volvería a fallar igual.

---

## 7. Por dónde seguir

- Las cinco capacidades están migradas; el viejo `SurfaceMap` sigue por debajo para las pantallas que
  solo existen ahí. Ir apagándolo es trabajo pendiente y se hace pantalla a pantalla, no de golpe.
- Los recuerdos (`Ensenar` / `RecuerdosDe`) son lo más reciente. Lo que falta ahí es poder **pedir
  tareas por significado** —«llévame a donde se radican las facturas»— que es una consulta al grafo
  por el texto del recuerdo, no una capacidad nueva.
- `graphify` mantiene un grafo del propio código en `graphify-out/`. Para preguntas sobre el repo,
  úsalo antes que `grep`.
