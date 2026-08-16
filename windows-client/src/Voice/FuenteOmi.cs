using System.Diagnostics;
using Concentus;
using Omi;
using U.WindowsClient.Diagnostics;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace U.WindowsClient.Voice;

/// <summary>
/// El collar Omi como micrófono: BLE directo desde Windows, sin móvil y sin la nube de Omi.
///
/// Entrega EXACTAMENTE lo mismo que <see cref="LiveAudio"/> saca del micrófono local —PCM16 a 16 kHz
/// mono, en trozos— para que <c>GeminiLive</c> no tenga que enterarse de cuál está puesta. Ese es el
/// punto de la promesa 3 del contrato de la voz.
///
/// AQUÍ SÓLO VIVE EL TRANSPORTE. El códec, la cabecera y la reposición del silencio están en
/// <c>voz\Omi</c>, que es net8.0 puro y por eso se juzga en la nube en cada PR. Un runner no tiene
/// Bluetooth: si esa lógica viviera aquí, sus promesas nacerían incomprobables (spec 001).
///
/// Todo lo de abajo está medido sobre el CV1 el 2026-08-13, 1.555 paquetes en tres corridas:
/// servicio <c>19b10000</c>, audio notify <c>19b10001</c>, códec read <c>19b10002</c>; códec 21
/// (Opus FS320, tramas de 20 ms); cabecera de 3 bytes con el índice SIEMPRE a cero, o sea sin
/// fragmentación; y cero saltos de numeración, o sea que el enlace no pierde nada.
/// </summary>
public sealed class FuenteOmi : IDisposable
{
    private static readonly Guid SvcOmi = new("19b10000-e8f2-537e-4f6c-d104768a1214");
    private static readonly Guid ChrAudio = new("19b10001-e8f2-537e-4f6c-d104768a1214");
    private static readonly Guid ChrCodec = new("19b10002-e8f2-537e-4f6c-d104768a1214");

    // EL BOTÓN. No está en PROTOCOL.md ni en la documentación de Omi: salió de enumerar el árbol GATT
    // del propio collar el 2026-08-13. Manda 8 bytes y el primero es el estado: 0x05 en reposo, 0x01
    // pulsado. NO distingue pulsación simple, doble ni larga — eso lo tiene que contar quien escuche.
    private static readonly Guid SvcBoton = new("23ba7924-0000-1000-7450-346eac492e92");
    private static readonly Guid ChrBoton = new("23ba7925-0000-1000-7450-346eac492e92");
    private const byte BotonPulsadoValor = 0x01;

    /// <summary>Cuánto se busca el collar antes de rendirse y dejar que entre el micrófono local.</summary>
    private static readonly TimeSpan Rastreo = TimeSpan.FromSeconds(8);

    private BluetoothLEDevice? _aparato;

    /// <summary>
    /// EL SERVICIO SE GUARDA, y no es para usarlo: es para que SIGA VIVO.
    ///
    /// En WinRT el <c>GattDeviceService</c> es dueño de la sesión GATT. Si se queda como variable
    /// local, al salir del método nadie lo referencia, el recolector se lo lleva, WinRT lo cierra —y
    /// se lleva la conexión con él—. El síntoma es una desconexión limpia a los pocos segundos, sin
    /// error y sin motivo: exactamente lo que se midió el 2026-08-13, treinta segundos justos entre
    /// «collar abierto» y «Bluetooth dice que se desconectó».
    ///
    /// Guardar la característica NO basta: la característica no mantiene vivo a su servicio.
    /// </summary>
    private GattDeviceService? _servicio;

    /// <summary>
    /// LO QUE MANTIENE VIVA LA CONEXIÓN, y sin esto el collar se caía a los 30,0 segundos EXACTOS.
    ///
    /// Windows no tiene una llamada de «conectar»: el enlace se mantiene siendo dueño de una
    /// <see cref="GattSession"/> con <c>MaintainConnection</c> en true. Sin ella el sistema considera
    /// el enlace ocioso —aunque estén llegando 50 tramas por segundo— y lo tira solo.
    ///
    /// Medido el 2026-08-13, cuatro caídas seguidas: «perdido tras 30,0 s y 1471 trama(s)», 30,0 y
    /// 1470, 30,0 y 1474, 30,0 y 1476. Un número redondo repetido cuatro veces no es una avería de
    /// radio: es un temporizador. Antes se probó guardar el GattDeviceService por si era el
    /// recolector de basura, y no movió el número ni una décima — la basura no se recoge a las
    /// 30,000 s.
    ///
    /// Es exactamente lo que hace el SDK oficial: bleak (que es quien mueve el SDK de Python de Omi)
    /// corre sobre estas MISMAS APIs de WinRT, y su cliente de Windows se reduce a
    /// <c>session.maintain_connection = True</c>. Por eso migrar al SDK no habría arreglado nada que
    /// no arregle esta línea: el motor era el mismo, faltaba encenderlo.
    /// </summary>
    private GattSession? _sesion;

    private GattCharacteristic? _audio;
    private GattDeviceService? _servicioBoton;
    private GattCharacteristic? _boton;
    private IOpusDecoder? _opus;
    private readonly Reposicion _reposicion = new();
    private readonly Stopwatch _reloj = Stopwatch.StartNew();
    private readonly object _candado = new();

    private short[] _pcm = new short[960];
    private long _ultimaTrama;

    /// <summary>Cuándo se abrió, para poder decir cuánto aguantó. Sin esa cifra, «se cae» es una queja.</summary>
    private long _abiertoEn;

    /// <summary>Cuántas tramas se recibieron desde que se abrió. Distingue «se cayó» de «nunca emitió».</summary>
    private long _tramas;

    /// <summary>Un trozo de audio del collar, ya en el formato que espera el modelo.</summary>
    public event Action<byte[]>? Capturado;

    /// <summary>Se perdió el collar y hay que relevar al micrófono local. Lleva el motivo.</summary>
    public event Action<string>? Perdido;

    /// <summary>Se pulsó el botón del collar (el flanco de bajada, no el de soltar).</summary>
    public event Action? BotonPulsado;

    private long _desconectadoEn = -1;

    /// <summary>
    /// Cuánto lleva Bluetooth diciendo que el collar no está. Cero si sigue conectado.
    ///
    /// No es lo mismo que <see cref="SinTrama"/> y confundirlos costó dos rondas: el collar puede
    /// estar conectado y callado —eso es alguien escuchando— o desconectado un segundo mientras
    /// Windows rehace el enlace. Sólo la segunda, sostenida, es una pérdida.
    /// </summary>
    public long MsDesconectado
    {
        get { lock (_candado) return _desconectadoEn < 0 ? 0 : _reloj.ElapsedMilliseconds - _desconectadoEn; }
    }

    public bool Viva { get; private set; }

    private volatile bool _conectado;

    /// <summary>
    /// Lo que dice Bluetooth: si el aparato sigue ahí. NO es «está mandando audio».
    ///
    /// Sale del EVENTO de cambio de estado y no de sondear <c>ConnectionStatus</c>: sondear devuelve
    /// «desconectado» durante el instante en que se está estableciendo el enlace, y eso bastaría para
    /// relevar al micrófono local justo al abrir. El evento sólo dispara en transiciones reales.
    /// </summary>
    public bool Conectado => _conectado;

    /// <summary>Milisegundos desde la última trama. Es lo que <see cref="Relevo"/> necesita saber.</summary>
    public long SinTrama
    {
        get { lock (_candado) return Viva ? _reloj.ElapsedMilliseconds - _ultimaTrama : 0; }
    }

    /// <summary>
    /// Busca el collar, comprueba que su códec se sabe decodificar, y se suscribe al audio.
    /// Devuelve false —sin lanzar— si no aparece: quedarse sin collar no puede tumbar la voz.
    /// </summary>
    public async Task<bool> AbrirAsync(CancellationToken ct)
    {
        try
        {
            ulong direccion = await RastrearAsync(ct);
            if (direccion == 0)
            {
                LogBus.Log("omi", $"no apareció ningún collar en {Rastreo.TotalSeconds:0} s");
                return false;
            }

            _aparato = await BluetoothLEDevice.FromBluetoothAddressAsync(direccion);
            if (_aparato == null) { LogBus.Log("omi", "el collar se anunció pero no se dejó abrir"); return false; }

            // ANTES de descubrir servicios: se pide la sesión y se declara que la queremos viva. Ver
            // el comentario del campo — es lo único que separa una conexión que dura de una que se
            // cae a los 30 s clavados.
            _sesion = await GattSession.FromDeviceIdAsync(_aparato.BluetoothDeviceId);
            _sesion.MaintainConnection = true;

            // Uncached: Windows cachea el árbol GATT entre sesiones, y una caché vieja devuelve
            // servicios que ya no están. Se le pregunta al aparato, no a Windows.
            var svcs = await _aparato.GetGattServicesForUuidAsync(SvcOmi, BluetoothCacheMode.Uncached);
            if (svcs.Status != GattCommunicationStatus.Success || svcs.Services.Count == 0)
            {
                LogBus.Log("omi", $"sin servicio de audio: {svcs.Status} "
                    + "(si dice Unreachable, el collar está cogido por otro host o se durmió)");
                return false;
            }
            var svc = svcs.Services[0];
            _servicio = svc;   // ver el comentario del campo: sin esto la conexión se cae sola

            byte codec = await CodecAsync(svc);
            if (!Codec.Soportado(codec))
            {
                // SE DICE CUÁL ERA. Un «códec no soportado» a secas no distingue el 7 del 1 ni de
                // «no contestó nadie», y manda la investigación al sitio equivocado.
                LogBus.Log("omi", "no se abre el collar: " + Codec.Motivo(codec));
                return false;
            }

            // PCM16 no necesita decodificador; los dos Opus sí, y comparten decodificador porque lo
            // único que cambia entre ellos es el tamaño de trama, que Opus lleva dentro.
            _opus = codec == Codec.Pcm16 ? null : OpusCodecFactory.CreateDecoder(Fuente.Hz, Fuente.Canales, null);

            var chrs = await svc.GetCharacteristicsForUuidAsync(ChrAudio, BluetoothCacheMode.Uncached);
            if (chrs.Status != GattCommunicationStatus.Success || chrs.Characteristics.Count == 0)
            {
                LogBus.Log("omi", $"sin característica de audio: {chrs.Status}");
                return false;
            }

            _audio = chrs.Characteristics[0];
            _audio.ValueChanged += AlLlegarTrama;
            var estado = await _audio.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify);
            if (estado != GattCommunicationStatus.Success)
            {
                LogBus.Log("omi", $"el collar no aceptó la suscripción: {estado}");
                return false;
            }

            // Se marca conectado y se escucha la transición SÓLO cuando ya hay suscripción: antes de
            // esto el enlace está a medio hacer y un aviso de «desconectado» sería el estado normal.
            _conectado = true;
            _desconectadoEn = -1;
            _aparato.ConnectionStatusChanged += AlCambiarConexion;
            await BotonAsync();

            lock (_candado)
            {
                _reposicion.Reiniciar();
                _ultimaTrama = _reloj.ElapsedMilliseconds;
                _abiertoEn = _ultimaTrama;
                _tramas = 0;
                Viva = true;
            }
            LogBus.Log("omi", $"collar abierto · códec {codec} · {Fuente.Hz} Hz mono");
            return true;
        }
        catch (Exception e)
        {
            // La cadena entera: un catch mudo aquí convertiría «no hay adaptador Bluetooth» en
            // indistinguible de «el collar está apagado» (aprendizaje nº3).
            for (var x = e; x != null; x = x.InnerException)
                LogBus.Log("omi", $"no se pudo abrir el collar · {x.GetType().Name}: {x.Message}");
            return false;
        }
    }

    private static async Task<ulong> RastrearAsync(CancellationToken ct)
    {
        var encontrado = new TaskCompletionSource<ulong>();
        var vigia = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };

        // Por SERVICIO y no por nombre: un nombre puede coincidir por casualidad, el UUID no.
        vigia.Received += (_, a) =>
        {
            if (a.Advertisement.ServiceUuids.Contains(SvcOmi)) encontrado.TrySetResult(a.BluetoothAddress);
        };

        vigia.Start();
        try
        {
            var gano = await Task.WhenAny(encontrado.Task, Task.Delay(Rastreo, ct));
            return gano == encontrado.Task ? await encontrado.Task : 0;
        }
        finally { try { vigia.Stop(); } catch { /* parar un vigía ya parado no es noticia */ } }
    }

    private static async Task<byte> CodecAsync(GattDeviceService svc)
    {
        var c = await svc.GetCharacteristicsForUuidAsync(ChrCodec, BluetoothCacheMode.Uncached);
        if (c.Status != GattCommunicationStatus.Success || c.Characteristics.Count == 0) return 0xFF;

        var v = await c.Characteristics[0].ReadValueAsync(BluetoothCacheMode.Uncached);
        if (v.Status != GattCommunicationStatus.Success) return 0xFF;

        var b = Bytes(v.Value);
        return b.Length == 0 ? (byte)0xFF : b[0];
    }

    /// <summary>
    /// Una notificación del collar: cabecera fuera, Opus a PCM, y el silencio que falta por delante.
    /// </summary>
    private void AlLlegarTrama(GattCharacteristic _, GattValueChangedEventArgs a)
    {
        byte[]? payload = Trama.Payload(Bytes(a.CharacteristicValue));
        if (payload == null) return;   // truncada o sólo cabecera: se pierde 20 ms, no la sesión

        byte[] trozo;
        lock (_candado)
        {
            int muestras;
            try
            {
                muestras = _opus == null
                    ? DesdePcm(payload)
                    : _opus.Decode(payload.AsSpan(), _pcm.AsSpan(), _pcm.Length, false);
            }
            catch (Exception e)
            {
                LogBus.Log("omi", $"trama ilegible, se descarta · {e.GetType().Name}: {e.Message}");
                return;
            }
            if (muestras <= 0) return;

            _ultimaTrama = _reloj.ElapsedMilliseconds;
            _tramas++;

            // AQUÍ ESTÁ TODO EL ASUNTO. El collar no transmite el silencio, así que el total incluye
            // las muestras mudas que faltan delante de esta trama. Sin ellas Gemini Live no oye
            // ninguna pausa, y sin pausa no cierra el turno: Ü escucharía sin contestar nunca.
            int total = _reposicion.Muestras(_ultimaTrama, muestras);
            int silencio = Math.Max(0, total - muestras);

            trozo = new byte[(silencio + muestras) * 2];
            // El silencio va delante y son ceros, que es lo que ya trae un array recién hecho.
            for (int i = 0; i < muestras; i++)
            {
                int p = (silencio + i) * 2;
                trozo[p] = (byte)(_pcm[i] & 0xFF);
                trozo[p + 1] = (byte)((_pcm[i] >> 8) & 0xFF);
            }
        }
        Capturado?.Invoke(trozo);
    }

    /// <summary>PCM16 crudo: no hay nada que decodificar, sólo copiarlo al mismo buffer de salida.</summary>
    private int DesdePcm(byte[] payload)
    {
        int muestras = payload.Length / 2;
        if (muestras > _pcm.Length) _pcm = new short[muestras];
        for (int i = 0; i < muestras; i++) _pcm[i] = (short)(payload[i * 2] | (payload[i * 2 + 1] << 8));
        return muestras;
    }

    private void AlCambiarConexion(BluetoothLEDevice aparato, object _)
    {
        bool ahora = aparato.ConnectionStatus == BluetoothConnectionStatus.Connected;
        _conectado = ahora;

        // CUÁNDO empezó la desconexión, no sólo que la hay: con MaintainConnection, Windows tira y
        // rehace el enlace solo —se midió un ciclo completo en un segundo—, así que lo que decide es
        // cuánto AGUANTA caída, no que se haya caído.
        lock (_candado)
        {
            if (ahora) _desconectadoEn = -1;
            else if (_desconectadoEn < 0) _desconectadoEn = _reloj.ElapsedMilliseconds;
        }

        LogBus.Log("omi", ahora
            ? "Bluetooth dice que el collar volvió a conectarse"
            : "Bluetooth dice que el collar se desconectó");
    }

    /// <summary>
    /// Se suscribe al botón del collar. Si no está, se sigue sin él: un collar sin botón es un collar
    /// que sirve igual de micrófono, y no poder pulsarlo no puede impedir que se oiga.
    /// </summary>
    private async Task BotonAsync()
    {
        try
        {
            var s = await _aparato!.GetGattServicesForUuidAsync(SvcBoton, BluetoothCacheMode.Uncached);
            if (s.Status != GattCommunicationStatus.Success || s.Services.Count == 0)
            {
                LogBus.Log("omi", $"este collar no publica botón ({s.Status}): se sigue sin él");
                return;
            }
            _servicioBoton = s.Services[0];   // guardado, o se cae la suscripción con él

            var c = await _servicioBoton.GetCharacteristicsForUuidAsync(ChrBoton, BluetoothCacheMode.Uncached);
            if (c.Status != GattCommunicationStatus.Success || c.Characteristics.Count == 0) return;

            _boton = c.Characteristics[0];
            _boton.ValueChanged += AlPulsarBoton;
            var estado = await _boton.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify);
            LogBus.Log("omi", estado == GattCommunicationStatus.Success
                ? "botón del collar suscrito"
                : $"el botón no aceptó la suscripción: {estado}");
        }
        catch (Exception e) { LogBus.Log("omi", $"al enganchar el botón: {e.GetType().Name}: {e.Message}"); }
    }

    private void AlPulsarBoton(GattCharacteristic _, GattValueChangedEventArgs a)
    {
        var b = Bytes(a.CharacteristicValue);
        if (b.Length == 0) return;

        // Sólo el flanco de PULSAR. El collar manda 0x01 al apretar y 0x05 al soltar; actuar en los
        // dos haría dos veces todo lo que se cuelgue de aquí.
        if (b[0] != BotonPulsadoValor) return;

        LogBus.Log("omi", "botón del collar pulsado");
        BotonPulsado?.Invoke();
    }

    /// <summary>Avisa de que se perdió el collar. Lo llama quien vigila el reloj, no esta clase.</summary>
    public void Perder(string motivo)
    {
        if (!Viva) return;
        Viva = false;

        // CUÁNTO AGUANTÓ Y CUÁNTO MANDÓ, siempre. «Se cae» es una queja; «aguantó 31 s y mandó 812
        // tramas» es un dato que se puede comparar con la corrida siguiente, que es lo único que
        // dice si un arreglo sirvió (2026-08-13).
        long duro = _reloj.ElapsedMilliseconds - _abiertoEn;
        LogBus.Log("omi", $"collar perdido tras {duro / 1000.0:0.0} s y {_tramas} trama(s): {motivo}");
        Perdido?.Invoke(motivo);
    }

    public void Cerrar()
    {
        lock (_candado) Viva = false;
        _conectado = false;
        try { if (_aparato != null) _aparato.ConnectionStatusChanged -= AlCambiarConexion; }
        catch (Exception e) { LogBus.Log("omi", $"al soltar el aviso de conexión: {e.Message}"); }
        try
        {
            if (_audio != null)
            {
                _audio.ValueChanged -= AlLlegarTrama;
                _ = _audio.WriteClientCharacteristicConfigurationDescriptorAsync(
                    GattClientCharacteristicConfigurationDescriptorValue.None);
            }
        }
        catch (Exception e) { LogBus.Log("omi", $"al desuscribir: {e.Message}"); }

        _audio = null;

        try { if (_boton != null) _boton.ValueChanged -= AlPulsarBoton; }
        catch (Exception e) { LogBus.Log("omi", $"al soltar el botón: {e.Message}"); }
        _boton = null;
        try { _servicioBoton?.Dispose(); } catch (Exception e) { LogBus.Log("omi", $"al soltar el servicio del botón: {e.Message}"); }
        _servicioBoton = null;

        // El servicio ANTES que el aparato: soltarlo después dejaría una sesión huérfana que impide
        // reconectar hasta que Windows la recicle.
        try { _servicio?.Dispose(); } catch (Exception e) { LogBus.Log("omi", $"al soltar el servicio: {e.Message}"); }
        _servicio = null;

        // Y la sesión se suelta DICIENDO que ya no la queremos viva. Sin poner MaintainConnection en
        // false primero, Windows seguiría reservando el enlace para un dueño que ya se fue.
        try { if (_sesion != null) { _sesion.MaintainConnection = false; _sesion.Dispose(); } }
        catch (Exception e) { LogBus.Log("omi", $"al soltar la sesión: {e.Message}"); }
        _sesion = null;

        try { _aparato?.Dispose(); } catch (Exception e) { LogBus.Log("omi", $"al soltar el collar: {e.Message}"); }
        _aparato = null;
        _opus = null;
    }

    private static byte[] Bytes(IBuffer b)
    {
        var lector = DataReader.FromBuffer(b);
        var a = new byte[b.Length];
        lector.ReadBytes(a);
        return a;
    }

    public void Dispose() => Cerrar();
}
