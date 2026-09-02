# Conectar el collar por el teléfono — guía para el usuario

Para médicos con **iPhone**, y para quien prefiera no conectar el collar directo al computador.
Se hace **una sola vez**. Después, el collar funciona solo cada vez que abras la consulta.

> **Cuándo hace falta esto.** Solo si usas iPhone, o si prefieres llevar el collar emparejado con
> el teléfono. Si tienes Android o Windows y quieres lo más simple, el collar se conecta **directo
> al computador** desde la propia ventana de consulta y no necesitas nada de esto.

---

## Antes de empezar

| Necesitas | Nota |
|---|---|
| El collar Omi cargado | La luz azul significa conectado; la roja, encendido sin conectar |
| La app **Omi** instalada en el teléfono | Está en la App Store y en Google Play. **Es gratis y no hace falta plan de pago** |
| Una cuenta de Omi | La gratuita basta. Esta configuración no consume los minutos de su plan |
| La ventana de consulta de Ü abierta en el computador | De ahí sacas tu enlace personal |

---

## Paso 1 · Empareja el collar con el teléfono

Abre la app de Omi y sigue su propio asistente para enlazar el collar. Cuando esté enlazado, la
luz del collar se pone **azul**.

Si ya lo tenías emparejado, sáltate este paso.

---

## Paso 2 · Copia tu enlace personal desde el computador

En la ventana de consulta de Ü, en el selector de micrófono, elige **Por el teléfono** y pulsa
**Copiar mi enlace**.

El enlace es **tuyo y solo tuyo**: lleva un código que identifica tu cuenta y tu computador, para
que el audio llegue a tu pantalla y no a la de otro. Tiene esta forma:

```
wss://…/omi-directo?code=XXXXXXXX
```

**No lo compartas.** Quien tenga ese enlace puede mandar audio a tu consulta.

---

## Paso 3 · Pega el enlace en la app de Omi

En el teléfono, dentro de la app de Omi:

1. Entra en **Ajustes** (Settings).
2. Entra en **Transcripción** (Transcription).
3. En el selector de arriba, elige **Cloud Provider**.
4. En la lista de proveedores, elige **Custom** — el que dice *«Define your own real-time STT endpoint»*.
5. En el campo **URL**, pega el enlace que copiaste.
6. Si te pide una clave de API, **déjala vacía**. Este proveedor no la necesita.
7. Baja hasta el final de la pantalla y **apaga** el interruptor **«Enviar audio sin procesar a Omi»**
   (tiene un icono de nube). Con eso, el audio de tus consultas **no pasa por los servidores de Omi**:
   va solo al tuyo.
8. Pulsa **Guardar** (Save).

---

## Paso 4 · Comprueba que funciona

Con el collar puesto, vuelve a la ventana de consulta en el computador y mira el indicador de
micrófono: cuando el teléfono empiece a mandar audio, se pone **verde con un icono de teléfono**.

Habla unos segundos. Si el indicador se pone verde, ya está: pulsa **Grabar** y la consulta se
transcribe con lo que oye el collar.

---

## Qué esperar en el día a día

- **No hay que repetir nada.** La configuración se queda guardada en el teléfono.
- **Deja la app de Omi abierta**, aunque sea en segundo plano. Puedes bloquear la pantalla y
  guardar el teléfono en el bolsillo: probado, sigue funcionando igual.
- **No cierres la app deslizándola** de las aplicaciones recientes. Eso sí corta el audio.
- **El teléfono necesita internet.** Si se queda sin conexión, el audio se corta y Ü vuelve sola
  al micrófono del computador.
- **Un collar habla con un aparato a la vez.** Si el collar está emparejado con el teléfono, el
  computador no puede conectarse directo, y al revés. La ventana de consulta te dice cuál está
  activo.

---

## Si algo no va

| Lo que ves | Qué hacer |
|---|---|
| El indicador no se pone verde | Comprueba que la luz del collar es azul y que la app de Omi está abierta |
| Se pone verde y luego gris | El teléfono perdió internet o la app se cerró. Vuelve a abrirla |
| La app de Omi muestra transcripciones raras | Es normal durante las pruebas: la que manda es la de Ü, en el computador |
| Pegaste el enlace y no pasa nada | Revisa que el modo esté en **Cloud Provider** y el proveedor en **Custom**, y que pulsaste Guardar |
| Cambiaste de computador | Copia el enlace nuevo desde ese computador y repite el paso 3 |

---

## Volver a como estaba

En la app de Omi, **Ajustes → Transcripción**, elige **Omi** en el selector de arriba y guarda. El
teléfono deja de mandarnos audio y la app de Omi vuelve a funcionar como venía de fábrica.

---

## Lo que este montaje sí y no hace

- **Sí:** el audio del collar llega a tu computador en tiempo real, y Ü lo transcribe con su propio
  motor.
- **Sí:** con el interruptor del paso 3.7 apagado, **el audio no pasa por los servidores de Omi**.
- **Sí:** no consume el plan de Omi. La cuenta gratuita basta.
- **No:** no sustituye el consentimiento del paciente ni las reglas de tu institución sobre grabar
  consultas. Eso va antes que cualquier configuración.
