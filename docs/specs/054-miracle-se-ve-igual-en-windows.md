# Miracle se ve igual en Windows

Estado: **en curso** · Nace de la petición del dueño del 2026-09-25 · Rama: `claude/admiring-brahmagupta-fghedd`
(asignada por la sesión; la convención `<persona>/<que-hace>` no aplica a esta rama)

> Specs 054-055 y promesas 445-479, reservadas el 2026-09-26 tras comprobar las ramas remotas: 440-479
> no las usa nadie, y la 052 ya la tienen `jero/jev-todo-junto` y `jose/u-desde-cero`. La 053 es la
> rama de Jose integrada (440-444).

## Qué se pidió, en sus palabras

«Pasar la parte visual y las funcionalidades importantes de Miracle Notes (la web) a U Windows, que el
Notes de Windows y el de la web **se entiendan como el mismo**. La experiencia sí distinta: el de
Windows es más ligero, más rápido». Y, sobre lo visual:

1. **Idéntico a la web en colores**, con **un azul un poquitico más clarito**.
2. **El blanco de U reemplaza al de Notes.** Los blancos de la web (canvas `#fbfcfe`, el papel cálido
   `#fdfcf9` de la nota) pasan al blanco de U.
3. **La experiencia de U mantiene sus bases** (sombras, relieve, ventana flotante, velocidad) y se
   puede mejorar. El marco se retoca «un poquitín».

## Diagnóstico: qué se midió

Leído el 2026-09-26 en los dos repos. Las rutas de la web son de `Pagina-web-clientes-final`.

| Qué | Web | Windows hoy |
|---|---|---|
| Letra | Inter (cuerpo), Schibsted Grotesk (títulos), **Source Serif 4 17px/1.62 para el cuerpo de la nota**, Geist Mono (reloj) — `app/layout.tsx:2-38` | Segoe UI por defecto, ninguna fuente embebida (`WindowsClient.csproj`) |
| Iconos | Lucide, trazo 2, `lucide-react` | Glifos de Segoe MDL2 en 9 sitios de `ConsultaWindow.cs` y 1 de `LoginWindow.cs` |
| Azul | `#2f6fe0` (4,7:1 con blanco), `accent-soft #eef4fe`, `accent-ink #1a4fa0` | `#2E6BE6`, `AcentoSuave #EAF1FE` |
| Tinta | ink `#0e1726`, ink-soft `#44546b`, muted `#5d6b80`, line `#e6eaf0`, line-strong `#d4dbe6` | `#0F1524`, `#5A6478`, `#7C8697`, `#E3E7EE` |
| Estados | success `#13795b`/`#dcf4ea`, warning `#a34a06`/`#fdeecf`, danger `#b33224`/`#fbe3df` | Ok `#1B8A5A`, Espera `#B46A0C`, Alerta `#D32F45` |
| Rótulo de sección | Inter 11,2px, 650, MAYÚSCULAS, tracking .11em, `doc-muted #6d6a62` | 10pt negrita mayúsculas `#7C8697`, sin tracking |
| Radios | 12 / 16 / 22 | 10 / 14 / 20; ventana 46 |
| La nota | papel `#fdfcf9`, filete `#e9e3d6`, serif | tarjetas blancas, Segoe 13,5 |

**Lo que esto significa:** no se parecen en letra, iconos, rótulos ni en la nota. Se parecen en el
blanco, en las sombras suaves y en tener un azul — que era casi el mismo por casualidad.

## Decisiones

- **Un solo sitio para los valores: `Ui/Marca.cs`, puro** (cadenas y números, sin un solo tipo de
  WPF). `Estudio` construye sus brochas desde ahí. Es lo que deja al contrato juzgar la paleta sin
  pantalla, y lo que impide que dentro de un mes haya un `#2E6BE6` suelto en otro archivo.
- **El azul.** Un punto más claro que la web **sin perder AA** con texto blanco (≥4,5:1).
  `#3B7BEA` —el primero que se probó— da 4,03:1 y no pasa. El más claro que pasa es **`#3272E3`
  (4,51:1)**: es el `Acento` (texto, iconos, enlaces). El botón primario lleva un **degradado
  `#3A7AEA → #2C66D8`**, como el `--grad-accent` de la web, y ahí es donde se ve el azul más claro.
- **Los blancos son los de U.** `Fondo` y `Superficie` = `#FFFFFF`, la barra `#F7F9FD`. La nota ya no
  va sobre papel cálido: se distingue por su **filete cálido `#ECE7DC`**, su **rótulo en versalitas
  con tracking** y su **cuerpo en Source Serif 4** — la misma razón de uso que la web (el médico
  distingue de un vistazo lo que es registro de lo que son controles), con el blanco de U.
- **Las sombras son las de U** (tres niveles, luz de arriba, placa aparte para no perder ClearType).
  Es la «base» que el dueño pidió conservar; la web usa sombras aún más suaves y no se copian.
- **Fuentes embebidas, estáticas.** WPF no aplica los ejes de una fuente variable ni lee woff2. Se
  sacan instancias estáticas de los TTF variables de `google/fonts` con `fontTools`, recortadas a
  latín (unos 980 KB en total): Inter 200/400/500/600/700, Schibsted Grotesk 600/700, Source Serif 4
  400/600, Geist Mono 500/600. Todas OFL; la licencia viaja al lado.
- **Tracking con espacio fino.** WPF no tiene espaciado de letras. El rótulo intercala U+200A (espacio
  de pelo) entre letras, que en Inter mide ≈0,1 em — el `.11em` de la web. Solo en rótulos, que no se
  copian ni se leen en voz alta.
- **Iconos Lucide como trazos.** El SVG de Lucide (ISC) se convierte a una cadena de trazo que WPF
  entiende, dibujada con grosor 2 y extremos redondos, como la web.

## Las promesas

| # | Promesa | Fase |
|---|---|---|
| 445 | las cuatro familias de Miracle —Inter, Schibsted Grotesk, Source Serif 4 y Geist Mono— viajan DENTRO de U.exe, con cada peso que la nota usa, y `Marca` las pide al ensamblado y nunca a las fuentes instaladas del sistema | 1 |
| 446 | los colores de Miracle Notes son los de la web —tinta, línea, estados y sus fondos, tinta del azul— con los blancos de U, y el azul es MÁS CLARO que el de la web sin bajar de 4,5:1 con texto blanco | 1 |
| 447 | `Estudio` pinta con los valores de `Marca`: cada brocha que la nota usa es exactamente su color de `Marca` | 1 |
| 448 | un rótulo de sección se escribe como en la web —en mayúsculas y con espaciado entre letras— y quitar el espaciado devuelve el texto en mayúsculas sin perder ni una letra ni un espacio | 1 |
| 449 | los iconos de la nota son los de la web: el catálogo trae cada icono que la nota usa, con el trazo del SVG de Lucide convertido a instrucciones que WPF lee, y ninguno vacío | 2 |

**La que cierra el asunto es la 446.** Sin ella, «idéntico a la web» es una impresión: la paleta se
puede comparar número a número, y esa comparación es la que el dueño va a hacer con los ojos.

### Con qué se juzga cada una

| # | Cómo, sin pantalla |
|---|---|
| 445 | se lee `U.g.resources` del ensamblado con `ResourceReader` (sin WPF) y deben estar los 11 TTF; `Marca.FuenteCuerpo` etc. empiezan por `pack://application:,,,/U;component/` |
| 446 | valores de `Marca` contra la tabla de esta spec; contraste calculado en la prueba con la fórmula WCAG |
| 447 | **solo en Windows** (toca WPF): el `Color` de cada `SolidColorBrush` de `Estudio` contra `Marca` |
| 448 | `Marca.Rotulo("Plan y recomendaciones")` → mayúsculas, U+200A entre letras, y `Replace(" ","")` = `"PLAN Y RECOMENDACIONES"` |
| 449 | `Iconos.Trazos` contiene los nombres de la lista; cada trazo empieza por `M`, solo usa comandos de trazo SVG y números |

Nivel 4, a mano: capturas lado a lado —login, lista y nota— web contra Windows.

## Las fases

| Fase | Promesas | Qué toca |
|---|---|---|
| 0 | 445-449 en rojo | `Contrato.cs` |
| 1 | 445-448 | `Ui/Marca.cs` (nuevo, puro), `assets/fuentes/*`, `WindowsClient.csproj`, `Ui/Estudio.cs` |
| 2 | 449 | `Ui/Iconos.cs` (nuevo, puro) + `Estudio.Icono(...)` |
| 3 | nivel 4 | reskin de `ConsultaWindow` y `LoginWindow`: fuentes, iconos, botón primario, rótulos, la nota con filete cálido y serif, marco con radio 22 y el orbe de Miracle |

## Lo que queda fuera

- **Modo oscuro.** La web lo tiene; U es claro por decisión del dueño (`Estudio.cs`, 2026-09-06).
- **La carita y su panel flotante** siguen oscuros: son otra superficie (`Estudio`, «la rampa
  FLOTANTE»). Esta spec cambia la ventana de la nota y el login.
- **Las sombras de la web** (`--elev-*`): se conservan las de U, que son su base.

## Hallazgos

## Cierre

- [ ] 445-449 verdes, sabotaje comprobado
- [ ] A mano: login, lista y nota, lado a lado con la web
