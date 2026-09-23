# Ü para Mac — cliente nativo desde cero

Esta implementación vive en la rama `codex/mac-from-scratch` y no reutiliza el cliente Mac anterior.
La app está escrita en Swift/AppKit y usa:

- `AXUIElement` para leer y accionar controles accesibles.
- `CGEvent` para teclado, ratón, scroll y arrastre.
- `ScreenCaptureKit` para capturas solicitadas por Graph.
- `AVAudioEngine` con voice processing para hablar y escuchar sin realimentación.
- GPT-Live 1 por `/v1/live/sessions`, con planificación delegada a `gpt-5.6-luna`.
- Jev (`jev-latest`, TypeSafe `/v1/systemone`) para elegir controles AX directamente.
- Graph entrega las credenciales de proveedores una vez al conectar. El modo texto/dictado conserva `/api/v1/agent/turn` como respaldo.

## Voz y ejecución rápida

Deja desactivado **Usar dictado y voz de macOS como respaldo**, pulsa **Comprobar conexión** y luego el micrófono.
Graph debe entregar `openai` y `typesafe` en `/api/v1/agent/claves`. Si falta TypeSafe, se muestra y Luna
puede seguir con las herramientas AX; no se presenta esa ejecución como Jev.

Luna usa `map_tramo` para iniciar navegación (hasta 15 pasos) sin bloquear la conversación y recibe el
desenlace automáticamente. `map_decidir` hace un solo paso. Jev elige exclusivamente controles observados;
Luna se ocupa del texto, la planificación, las ambigüedades y los pasos que Jev rechaza. Confianza mínima
0,70; objetivo cumplido desde 0,70; riesgo desde 0,50 devuelve el control a Luna. La ausencia de cualquiera
de estas respuestas también devuelve el control. No se reintenta un clic fallido con otra etiqueta.

El recorrido de Jev no hace capturas, ni enumera aplicaciones, ni construye contexto Graph, ni espera 180 ms
entre acciones. Lee los atributos AX en lotes, conserva la referencia nativa elegida y comprueba cancelación
y foco antes de pulsar. La siguiente decisión observa el estado posterior. TypeSafe tiene un plazo total
de 2 segundos, incluidos los reintentos de HTTP 429/529. No se pide permiso por acción.

### Medir en la app instalada

Con el Mac desbloqueado y UFixture recién abierta (contador a cero):

```bash
open mac-client/.artifacts/UFixture.app
open -n "$HOME/Applications/U.app" --args --execution-test /tmp/u-execution-test.json
```

La prueba usa las credenciales guardadas: comprueba sesión Live 1 → herramienta de Luna → resultado,
sin abrir el micrófono, y pide a Jev cinco clics en la ventana de prueba. El JSON separa `readAXms`,
`decisionMS` (red y respuesta TypeSafe), `actionMS` y `totalMS` por paso. Solo declara éxito si observa
el contador en cinco. No extrapoles esta ventana pequeña a navegadores o aplicaciones con árboles AX grandes.
La conversación e interrupción de voz se comprueban manualmente con el micrófono de la app.

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
