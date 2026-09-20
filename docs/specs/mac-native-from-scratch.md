# Cliente nativo Mac desde cero

Rama: `codex/mac-from-scratch`, nacida de `origin/main` (27d851b).
Por petición expresa del dueño, se elimina el cliente Mac anterior. Ninguna fuente,
prueba, interfaz, configuración ni script del port anterior se reutiliza. La referencia
funcional es exclusivamente Windows y los contratos de Graph.

## Promesas comprobables

1. Graph conserva sesión, resultados ordenados, preguntas e imágenes a pedido.
2. Cancelar impide nuevas acciones, incluso si había una respuesta de red en vuelo.
3. Un límite de turnos, un fallo de herramienta o un permiso ausente nunca se vende como éxito.
4. Coordenadas relativas a la captura se convierten a puntos Quartz, incluidos monitores secundarios.
5. Una acción sin coordenadas no puede convertirse accidentalmente en un clic en (0,0).
6. AX enumera y acciona controles por identidad o etiqueta exacta; etiquetas ambiguas se rechazan.
7. Antes de teclear o pulsar se valida el proceso observado. Cambiar de app exige observar otra vez.
8. La credencial de Graph vive en Keychain; nunca en el repositorio, URL ni registro.
9. La carita flota sin robar foco; voz, texto, estado de tarea y detener comparten un controlador.
10. El paquete compilado tiene identidad y declaraciones de privacidad, y puede abrirse como app.

## Validación

Tests independientes del escritorio para 1–8, compilación real en macOS y escenario local
para AX/captura/entrada. La verificación de micrófono real y Graph requiere permisos TCC
y credencial del servicio; se registra como pendiente si no están disponibles.

## Diferencias de plataforma

SAP GUI Scripting COM y ejecutables Windows no existen de forma nativa en macOS. AX
puede operar aplicaciones Mac y navegadores que expongan accesibilidad; para superficies
opacas el backend recibe una captura y usa coordenadas. Nunca se ejecuta COM por simulación.
