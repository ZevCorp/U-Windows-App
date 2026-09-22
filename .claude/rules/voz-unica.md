# Voz única de Ü

- Toda salida hablada del asistente, incluidos recordatorios, alarmas, actualizaciones y mensajes de progreso, usa la sesión viva del modelo (`ConversacionEnVivo`).
- Si la sesión está cerrada, el código debe abrirla y esperar la confirmación del servidor antes de enviar la frase.
- `VoiceIO.Speak` y `System.Speech.Synthesis` no se usan para responder al usuario. `VoiceIO` queda limitado al dictado local de respaldo.
- Los recordatorios deben despertar la misma voz viva aunque el usuario la hubiera apagado, dejando la sesión disponible para que pueda contestar.
- Las pruebas de voz deben comprobar en el log `voz-viva: sesión abierta ... el servidor la confirmó` y no solo que apareció texto en pantalla.
