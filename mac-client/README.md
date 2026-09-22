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

El lanzador abre siempre la copia instalada en `~/Applications/U.app`; si todavía no existe, ejecuta la instalación automáticamente. También puedes abrir directamente:

```bash
open "$HOME/Applications/U.app"
```

Para probar el flujo como lo usará una persona instalada, compila y copia el bundle a `~/Applications`:

```bash
./mac-client/instalar.sh
```

Después abre `~/Applications/U.app`. No alternes entre el ejecutable suelto, `.artifacts/U.app` y
otra copia en `~/Desktop/U/U-Mac/U.app`: macOS registra TCC por el bundle y su firma. El instalador
mueve esa copia heredada a la Papelera y verifica que la app final no use una firma ad hoc.

Las compilaciones locales usan el certificado persistente `U Local Stable Signing`, guardado en un
llavero local. Por tanto, recompilar no cambia su requisito TCC. La distribución a otras personas
debe definir `CODE_SIGN_IDENTITY` con un certificado `Developer ID Application` y notarizar el ZIP,
DMG o PKG resultante; una firma local no se debe distribuir.

## Permisos

En la pestaña **Configuración**:

1. Pulsa **Permitir** junto a **Accesibilidad**. Se abre directamente el panel de macOS.
2. En la lista activa **Ü para Mac** (puede aparecer como `U` porque macOS cachea el nombre del ejecutable).
3. Activa **Grabación de pantalla** si quieres que vea capturas.
4. Activa **Micrófono y voz** si quieres conversación por voz.
5. Regresa a Ü: los estados se vuelven a comprobar automáticamente durante 30 segundos.
6. Si macOS no refleja un permiso hasta el siguiente arranque, pulsa **Reiniciar Ü para aplicar**.

En la instalación nueva la app debe indicar `Bundle: com.zevcorp.u.mac` y `Firma: local-stable`.
Esa combinación es la que se debe autorizar una única vez. No ejecutes `reparar-permisos.sh` después
de una actualización normal: hacerlo borra deliberadamente la autorización para repetir el onboarding.

El permiso de Accesibilidad es el que permite usar otras aplicaciones. Sin él, Ü solo puede mostrar la carita y hablar por texto.

### Recuperar instalaciones antiguas

Solo si una instalación anterior conservó un interruptor verde que Ü no reconoce, ejecuta una vez:

```bash
./mac-client/reparar-permisos.sh
```

Esto resetea únicamente Accesibilidad y Grabación de pantalla de `com.zevcorp.u.mac`; no se ejecuta
desde la app ni durante actualizaciones normales. Vuelve a conceder ambos permisos y, desde entonces,
actualiza siempre con `./mac-client/instalar.sh`.

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

El diagnóstico también muestra la ruta y el bundle ID que macOS está autorizando:

```bash
open -n "$HOME/Applications/U.app" --args --diagnose
```

El diagnóstico debe ejecutarse con Ü cerrada para que `--args` llegue a una instancia nueva.

## Detener y volver a abrir

- Doble toque en la carita: activar o silenciar el micrófono.
- `Esc`: detener una tarea y cerrar la voz en vivo.
- Para cerrar completamente: menú **Ü** en la barra de menús → **Salir de Ü**.
- Para abrir otra vez: `./mac-client/abrir.sh`.
