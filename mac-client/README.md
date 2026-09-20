# Ü para Mac — cliente nativo desde cero

Esta implementación vive en la rama `codex/mac-from-scratch` y no reutiliza el cliente Mac anterior.
La app está escrita en Swift/AppKit y usa:

- `AXUIElement` para leer y accionar controles accesibles.
- `CGEvent` para teclado, ratón, scroll y arrastre.
- `ScreenCaptureKit` para capturas solicitadas por Graph.
- `AVAudioEngine` con voice processing para hablar y escuchar sin realimentación.
- Graph (`/api/v1/agent/turn`) para decidir los pasos; la app ejecuta y verifica.

## Abrir la app

Desde la raíz del repositorio:

```bash
./mac-client/abrir.sh
```

Si todavía no existe `.artifacts/U.app`, el script compila una versión de desarrollo y luego la abre.
También puedes abrir directamente:

```bash
open -n "$PWD/mac-client/.artifacts/U.app"
```

Usa siempre la app dentro de `mac-client/.artifacts`. En este Mac hay una copia vieja en
`~/Desktop/U/U-Mac/U.app`; esa copia no pertenece a esta rama y su permiso aparece como otra entrada.

## Permisos

En la pestaña **Configuración**:

1. Pulsa **Permitir** junto a **Accesibilidad**. Se abre directamente el panel de macOS.
2. En la lista activa **Ü para Mac** (puede aparecer como `U` porque macOS cachea el nombre del ejecutable).
3. Cierra y vuelve a abrir la app con `./mac-client/abrir.sh`.
4. Activa **Grabación de pantalla** si quieres que vea capturas.
5. Activa **Micrófono y voz** si quieres conversación por voz.
6. Pulsa **Actualizar permisos**. Deben verse los tres círculos verdes.

El permiso de Accesibilidad es el que permite usar otras aplicaciones. Sin él, Ü solo puede mostrar la carita y hablar por texto.

## Configurar Graph

En Configuración pega la API key de Graph y pulsa **Guardar**. Se guarda en el Llavero de macOS, no en archivos del proyecto. El token de GitHub no sirve para Graph.

## Prueba local de AX

Para probar la lectura y acción sin red ni Graph, compila la app de fixture:

```bash
cd mac-client
swift build -c debug --product UFixture
open -n .build/arm64-apple-macosx/debug/UFixture
```

La prueba automática completa, sin micrófono ni escritorio:

```bash
swift run -c debug NativeContract
```

La app se puede ejecutar con diagnóstico:

```bash
open -n .artifacts/U.app --args --diagnose
```

## Detener y volver a abrir

- Doble toque en la carita: activar o silenciar el micrófono.
- `Esc`: detener una tarea y cerrar la voz en vivo.
- Para cerrar completamente: menú **Ü** en la barra de menús → **Salir de Ü**.
- Para abrir otra vez: `./mac-client/abrir.sh`.
