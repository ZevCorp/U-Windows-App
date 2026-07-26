# Sonda de mapeo del árbol SAP (rama `feature/sap-tree-mapping`)

> **Estado:** experimento — Entrega 1 de 2 · **Objetivo final:** que cada botón del scrolleable
> "Entorno de trabajo" quede enmarcado individualmente por el inspector, con su identidad
> (`NodeKey`), en vez del shell ámbar único de hoy ("shell · 600 nodos").

## El problema

La Scripting API de SAP enumera los nodos del árbol (claves + textos) pero **no da ninguna
coordenada por nodo** (límite de SAP, no nuestro — ver `SapVisualElement.cs`). Por eso el overlay
solo puede enmarcar el árbol entero. Para mapear cada botón necesitamos derivar los rectángulos
nosotros, y la única vía nativa es el **hit-test** (`FindByPosition`): preguntarle a SAP punto por
punto qué hay debajo. Son llamadas COM locales — **no generan round-trips al servidor SAP**.

Antes de escribir el mapeo definitivo hay que verificar DOS incógnitas contra este SAP real:

1. **¿Qué devuelve `FindByPosition`?** El código asumía un objeto con `.Id`; la spec dice que es una
   colección de 2 strings (`[0]` Id, `[1]` "inner object"). Si es la colección, el hit-test llevaba
   tiempo devolviendo null en silencio. El código nuevo acepta ambas formas y **registra cuál llegó**.
2. **¿El inner object identifica la fila?** Si `[1]` trae la clave/fila/texto del nodo bajo el punto,
   el mapeo es determinista y no hace falta OCR ni accesibilidad. Si no, caemos al plan B.

## Qué hay en esta rama

- `HitTest` arreglado (acepta componente y colección) + `HitTestDetailed` con el detalle completo.
- **Cada clic con el inspector activo sobre SAP** ahora deja una línea `hit-test (x,y): id=… inner=… shape=…`
  en el registro (evidencia pasiva).
- **Botón "🧭 Sondear SAP"** en la ventana de registro: barre dos columnas verticales del árbol
  visible y vuelca las bandas crudas. Solo lectura; no toca nada.

## Cómo correrla (máquina con SAP)

1. `git fetch && git checkout feature/sap-tree-mapping`
2. `cd windows-client && dotnet run -c Release` (como siempre)
3. Abre SAP con la pantalla del **Entorno de trabajo** visible y **en primer plano** (la del
   screenshot del shell ámbar). No debe haber un round-trip en curso.
4. Abre la ventana de **Registro** de Ü → botón **🧭 Sondear SAP**. Tarda unos segundos.
5. Cuando termine (`sonda: fin en N ms`) → botón **📋 Copiar** → pegar el texto completo en el chat.
6. **Extra que vale oro:** activa el inspector y haz clic en 3-4 botones del panel (p.ej. "Órdenes
   Clínicas", "Admisiones Lab. Ambulatorio", una carpeta como "Laboratorio"). Cada clic deja su línea
   `hit-test` en el registro. Copia también eso, diciendo a qué le hiciste clic en cada caso.

Si la sonda dice `session.Busy=true`, espera a que SAP quede quieto y reintenta.

## Cómo leer el resultado (referencia rápida)

```
sonda: 1 shell(s) de árbol visibles · 600 nodos lógicos · 634 elementos totales
── árbol /app/con[0]/ses[0]/wnd[0]/shellcont/shell
   rect: left=312 top=180 w=940 h=820 · «Entorno de trabajo · 600 nodos»
   TopNode=F00042
   colB y=182..214: id=…/shell · inner=… · collection[2]   ← UNA BANDA POR FILA = éxito
   colB y=215..246: id=…/shell · inner=… · collection[2]
```

- **Bandas distintas por fila con inner distinto** → el mapeo determinista es viable: Entrega 2 lo
  implementa (bandas → rects → overlay por botón + diagnóstico amarillo/rojo por botón).
- **Una sola banda gigante con el mismo id/inner** → SAP no distingue el interior del shell por
  hit-test: pasamos al plan B (activar *"Use accessibility mode"* en SAP GUI → Opciones →
  Accessibility & Scripting, y mapear por UIA) o, en último caso, OCR.
- `shape=component` vs `collection[2]` zanja la contradicción documental del repo — cualquiera de
  los dos es útil, lo importante es saberlo.

## Qué NO hace

- No ejecuta nada en SAP (ni clics, ni valores): solo lee.
- No corre sola: solo cuando se aprieta el botón.
- No toca la pantalla de login ni maneja credenciales (guardrail del hospital intacto).
