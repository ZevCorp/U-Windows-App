using NAudio.Wave;
using Omi;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Voice;

/// <summary>
/// El micrófono y el altavoz de la conversación en vivo, en crudo.
///
/// Live API no habla de «frases»: es un caño de audio abierto en los dos sentidos. Eso lo separa de
/// <see cref="VoiceIO"/>, que dicta UNA frase con los motores de Windows y luego se calla: allí el
/// turno lo decide un temporizador de 8 s, aquí lo decide el modelo mientras te oye. Por eso hace
/// falta PCM crudo y no un reconocedor.
///
/// LOS DOS RITMOS SON DISTINTOS A PROPÓSITO. La salida es fija —24 kHz, lo que entregan tanto
/// Gemini como OpenAI— pero la ENTRADA la decide quien abre la conversación: Gemini pedía 16 kHz y
/// OpenAI exige un mínimo de 24 kHz (comprobado contra su servidor real, 2026-08-24). Por eso el
/// ritmo de entrada es un parámetro del constructor y no una constante — la última vez que fue fija
/// hubo que cambiar el archivo entero para cambiar de proveedor.
/// </summary>
public sealed class LiveAudio : IDisposable
{
    public int RitmoEntrada { get; }
    public const int RitmoSalida = 24000;

    /// <summary>El collar entrega SIEMPRE 16 kHz: es el firmware de Omi, no algo que decidamos aquí.</summary>
    private const int RitmoDelCollar = 16000;

    private WaveInEvent? _mic;
    private BufferedWaveProvider? _cola;
    private WaveOutEvent? _altavoz;
    private readonly object _candado = new();

    /// <summary>
    /// Sube el ritmo del collar cuando hace falta. Solo existe si <see cref="RitmoEntrada"/> no es
    /// ya 16 kHz — crearlo sin necesidad sería un remuestreador trabajando para no cambiar nada.
    /// </summary>
    private readonly RemuestreadorPcm16? _remuestreadorCollar;

    /// <summary>El AEC por software (spec 002, fase 3). Nulo si la llave está apagada; su
    /// <c>Activo</c> además exige que el nativo haya cargado. Con él activo, la compuerta se
    /// aparta (promesa 14) y el barge-in por voz vuelve.</summary>
    private readonly CancelaEco? _aec;

    /// <summary>¿El eco se está restando de verdad? Es lo que la compuerta consulta para apartarse.</summary>
    public bool AecPorSoftware => _aec?.Activo == true;

    public LiveAudio(int ritmoEntrada = 16000)
    {
        // U_AEC_SOFTWARE=1 lo enciende (validación en curso); =0 o ausente, la compuerta manda.
        if (Environment.GetEnvironmentVariable("U_AEC_SOFTWARE") == "1")
            _aec = new CancelaEco(ritmoEntrada);
        RitmoEntrada = ritmoEntrada;
        if (ritmoEntrada != RitmoDelCollar)
            _remuestreadorCollar = new RemuestreadorPcm16(RitmoDelCollar, ritmoEntrada);
    }

    // ── EL COLLAR (spec 001) ──────────────────────────────────────────────────
    private Relevo? _relevo;
    private Timer? _vigilante;

    /// <summary>
    /// Red de seguridad para la conexión colgada: sigue diciendo «conectado» y no manda una trama más.
    ///
    /// NO MIDE SILENCIO. Con 4 s medía silencio y la voz se caía sola mientras el usuario ESCUCHABA
    /// a Ü (2026-08-13, 21:25:01 del log): el collar no transmite silencio, así que estar callado se
    /// veía idéntico a haberse ido. Quien dice si el collar sigue ahí es Bluetooth; esto sólo cubre
    /// el caso en que Bluetooth no se entera, y por eso es medio minuto y no cuatro segundos.
    /// </summary>
    private const int UmbralRelevoMs = 30000;

    /// <summary>Cuánto se espera antes de volver a buscar el collar tras perderlo.</summary>
    private const int ReintentoCollarMs = 10000;

    /// <summary>
    /// Esta sesión de voz debe entrar por el collar.
    ///
    /// LO PONE UN GESTO DEL USUARIO —mantener pulsado el micrófono, o triple Ctrl—, y no una variable
    /// de entorno. Un interruptor que hay que saber que existe no se puede probar: obliga a arrancar
    /// la aplicación de una forma especial, y entonces «probarlo» ya no es lo mismo que usarlo
    /// (2026-08-13, pedido por el usuario). <c>U_OMI</c> sigue valiendo para arrancar ya con collar,
    /// pero no hace falta para nada.
    /// </summary>
    public static bool UsarCollar { get; set; }

    /// <summary>
    /// Si esta conversación tiene que oír por el collar.
    ///
    /// SI HAY COLLAR ENLAZADO Y PRESENTE, ÉSE ES EL MICRÓFONO — y esa cláusula es la que faltaba.
    /// Sin ella, encender la voz con el botón del collar abría la conversación pero seguía oyendo
    /// por el micrófono del portátil: se pulsaba el collar para hablarle al collar y el collar no
    /// escuchaba (2026-08-13, visto en el log — «collar abierto» sin un «la voz entra por el collar»
    /// detrás). El micrófono del PC pasa a ser lo que siempre debió ser: el respaldo.
    /// </summary>
    private static bool QuiereCollar => !EvitarCollar
        && (UsarCollar
            || CollarPermanente.Conectado
            || (Environment.GetEnvironmentVariable("U_OMI") ?? "").Trim().ToLowerInvariant() is "1" or "true" or "si" or "sí");

    /// <summary>
    /// El médico pidió a mano NO oír por el collar. Apaga la cláusula de arriba.
    ///
    /// LA PUERTA ESTABA CERRADA POR DENTRO, y se descubrió el 2026-09-01 probando el selector recién
    /// dibujado: con el collar conectado, elegir el micrófono del PC o el teléfono no hacía nada.
    /// La causa es la cláusula «si hay collar presente, ése es el micrófono», que se añadió el
    /// 2026-08-13 para arreglar el fallo CONTRARIO —se pulsaba el collar y el collar no escuchaba—
    /// y que al no tener contraparte dejó al collar mandando para siempre.
    ///
    /// Las dos cláusulas son correctas y tienen que convivir: sin elección manda el collar en cuanto
    /// aparece (que es lo cómodo), y con elección manda lo elegido (que es lo que se pidió). Esto es
    /// sólo la mitad que faltaba. La regla vive en <see cref="Omi.Selector.Preferir"/>, que el
    /// contrato juzga sin micrófono (promesa 30).
    /// </summary>
    public static bool EvitarCollar { get; set; }

    /// <summary>
    /// Devuelve la voz al micrófono del PC AHORA, aunque el collar siga conectado y entregando.
    ///
    /// Es la contraparte de <see cref="PasarAlCollar"/>. Cierra el collar en vez de esperar a que
    /// el relevo lo dé por perdido: aquí no hay avería que detectar, hay una persona que eligió.
    /// </summary>
    public void PasarAlLocal(string motivo)
    {
        EvitarCollar = true;
        UsarCollar = false;
        bool veniaDelTelefono = _usandoTelefono;
        CerrarTelefono();
        if (_usandoCollar) VolverAlLocal(motivo);
        else if (veniaDelTelefono)
        {
            AbrirLocal();
            LogBus.Log("voz-viva", "la voz vuelve al micrófono local · " + motivo);
            FuenteCambio?.Invoke();
        }
    }

    /// <summary>
    /// Pasa la voz al collar AHORA, con la sesión ya abierta o sin abrir. Es lo que hace el gesto.
    ///
    /// Si ya hay collar no se toca nada: pedirlo dos veces no puede cortar la conversación en curso.
    /// </summary>
    public void PasarAlCollar()
    {
        EvitarCollar = false;   // elegir el collar deshace un «no quiero collar» anterior
        UsarCollar = true;
        if (!_usandoCollar) _ = Task.Run(AbrirCollarAsync);
    }

    /// <summary>Un trozo de micrófono, ya en el formato que espera el modelo.</summary>
    public event Action<byte[]>? Capturado;


    /// <summary>¿El altavoz tiene algo pendiente por decir? La carita lo usa para animarse.</summary>
    public bool Hablando
    {
        get { lock (_candado) return _cola != null && _cola.BufferedBytes > 0; }
    }

    /// <summary>
    /// CUÁNTO DE FUERTE estamos sonando ahora mismo, en la misma escala que el micrófono (0–1).
    ///
    /// Hace falta para no confundir nuestra propia voz con la del usuario. Lo que entra por el
    /// micro cuando hablamos es un eco de esto, y lo alto que llegue depende del volumen de los
    /// altavoces: con el volumen bajo no molesta y con el volumen alto tapa la voz de cualquiera.
    /// Saber a qué volumen estamos sonando es lo único que permite distinguir «me están
    /// interrumpiendo» de «me estoy oyendo a mí mismo» (2026-08-05).
    ///
    /// Baja sola: se queda con el pico reciente y lo va soltando, para que el silencio entre dos
    /// palabras de una misma frase no la ponga a cero y abra la puerta al eco de la siguiente.
    ///
    /// SONAR NO ES RECIBIR (2026-08-06). El modelo manda el audio mucho más rápido de lo que se
    /// oye: una frase de cinco segundos entra en menos de uno y se queda en la cola. Medir el
    /// tiempo desde que LLEGA el último trozo dejaba esto en cero a los 400 ms, con el altavoz
    /// todavía hablando —o sea, en cero justo cuando hace falta—. Y de ahí colgaban las tres
    /// defensas contra el eco de GeminiLive: aprender la ganancia, subir el umbral y exigir dos
    /// tramos seguidos. Las tres se apagaban a la vez y Ü se cortaba a media frase oyéndose a sí
    /// misma. Mientras quede cola estamos sonando y no hay nada que soltar; la caída empieza
    /// cuando la cola se vacía.
    /// </summary>
    public double NivelSalida
    {
        get
        {
            lock (_candado)
            {
                if (_cola != null && _cola.BufferedBytes > 0)
                {
                    _cuandoSalida = DateTime.UtcNow;
                    return _nivelSalida;
                }
                double caida = (DateTime.UtcNow - _cuandoSalida).TotalMilliseconds / 400.0;
                if (caida >= 1) { _nivelSalida = 0; return 0; }
                return _nivelSalida * (1 - caida);
            }
        }
    }

    private double _nivelSalida;
    private DateTime _cuandoSalida = DateTime.MinValue;

    /// <summary>El pico de un bloque PCM de 16 bits, normalizado a 0–1. Igual que mide la entrada.</summary>
    private static double Pico(byte[] pcm)
    {
        int max = 0;
        for (int i = 0; i + 1 < pcm.Length; i += 2)
        {
            // (int) ANTES del Abs, y no es cosmético: con un short, C# elige Math.Abs(short), y
            // Math.Abs(-32768) LANZA OverflowException —el +32768 no existe en 16 bits—. Basta una
            // muestra en el tope, que en audio fuerte llega, para tumbar el cálculo del nivel desde
            // dentro de Reproducir. Se cazó el 2026-08-13 cuando la misma línea, copiada en la sonda
            // del collar, mató el proceso; aquí llevaba más tiempo esperando.
            int m = Math.Abs((int)(short)(pcm[i] | (pcm[i + 1] << 8)));
            if (m > max) max = m;
        }
        return max / 32768.0;
    }

    /// <summary>
    /// Abre la entrada de voz. El micrófono local entra YA; si hay collar, releva en cuanto aparezca.
    ///
    /// Ese orden es deliberado: buscar el collar cuesta hasta ocho segundos de rastreo BLE, y
    /// arrancar mudo mientras tanto sería peor que no tener collar. Se empieza a oír al instante y
    /// se mejora la fuente cuando se encuentra, sin que quien escucha se entere: los dos entregan
    /// PCM16 a 16 kHz mono, que es la promesa 3 del contrato de la voz.
    /// </summary>
    public void AbrirMicrofono()
    {
        // Con el collar YA conectado se va directo a él: no hay rastreo que esperar, así que abrir
        // el micrófono local para cerrarlo dos milisegundos después sólo consigue que los dos oigan
        // a la vez durante ese hueco.
        if (CollarPermanente.Conectado) { _ = Task.Run(AbrirCollarAsync); return; }

        AbrirLocal();
        if (QuiereCollar && !_usandoCollar) _ = Task.Run(AbrirCollarAsync);
    }

    private void AbrirLocal()
    {
        lock (_candado)
        {
            if (_mic != null) return;

            // DOS MICRÓFONOS A LA VEZ ES PEOR QUE NINGUNO: se mezclarían dos flujos con relojes
            // distintos y el modelo oiría todo dicho dos veces, desfasado. Pasa de verdad cuando el
            // gesto pide el collar ANTES de que la sesión esté abierta: el collar engancha primero y
            // luego GeminiLive llama aquí como si nada.
            if (_usandoCollar) return;
            _mic = new WaveInEvent
            {
                WaveFormat = new WaveFormat(RitmoEntrada, 16, 1),
                // Trozos cortos: el modelo interrumpe y responde mientras hablas, así que un buffer
                // largo no ahorra nada y sí añade retardo a todo lo que venga después.
                BufferMilliseconds = 100,
            };
            _mic.DataAvailable += (_, e) =>
            {
                if (e.BytesRecorded <= 0) return;
                var trozo = new byte[e.BytesRecorded];
                Buffer.BlockCopy(e.Buffer, 0, trozo, 0, e.BytesRecorded);
                // El eco se resta AQUÍ, antes de que nadie más lo vea: compuerta, detector y
                // servidor reciben ya el micrófono limpio (o crudo tal cual, si no hay AEC).
                if (_aec != null) trozo = _aec.Procesa(trozo);
                Capturado?.Invoke(trozo);
            };
            _mic.StartRecording();
            LogBus.Log("voz-viva", $"micrófono abierto a {RitmoEntrada} Hz");
        }
    }

    public void CerrarMicrofono()
    {
        CerrarCollar();
        lock (_candado)
        {
            if (_mic == null) return;
            try { _mic.StopRecording(); _mic.Dispose(); } catch { }
            _mic = null;
            LogBus.Log("voz-viva", "micrófono cerrado");
        }
    }

    /// <summary>
    /// Busca el collar y, si aparece, se queda con la entrada. No lanza nunca: no encontrar collar es
    /// un resultado normal, y tumbar la sesión de voz por eso sería mucho peor que seguir con el
    /// micrófono del portátil.
    /// </summary>
    private bool _abriendoCollar;

    /// <summary>Esta conversación está oyendo por el collar. NO significa que el enlace sea nuestro.</summary>
    private bool _usandoCollar;

    /// <summary>Por dónde está entrando la voz AHORA. La carita lo pinta.</summary>
    public bool PorElCollar => _usandoCollar;

    /// <summary>
    /// Cambió de dónde entra la voz: collar ↔ micrófono local. AHORA, no en el próximo cuadro.
    ///
    /// Sin este aviso, lo único que repintaba el color era el temporizador de la boca —y ese sólo
    /// vive mientras Ü está HABLANDO, no mientras escucha—. Encender la voz con el botón del collar
    /// pintaba gris en el instante de abrir la sesión —antes de que <c>AbrirCollarAsync</c> terminara
    /// en segundo plano— y se quedaba así para siempre si Ü no llegaba a decir una palabra. El log ya
    /// probaba que el audio SÍ entraba por el collar; era el dibujo el que no se enteraba
    /// (2026-08-14).
    /// </summary>
    public event Action? FuenteCambio;

    /// <summary>
    /// Empieza a oír por el collar. EL ENLACE NO ES DE ESTA CLASE.
    ///
    /// Lo lleva <see cref="CollarPermanente"/>, que vive por encima de la conversación. Ese reparto
    /// es lo que hace posible las dos cosas que se pidieron: que el botón del collar ENCIENDA la voz
    /// —con la voz apagada tiene que haber alguien escuchando el botón— y que colgar no desenlace.
    /// </summary>
    private async Task AbrirCollarAsync()
    {
        // Una búsqueda a la vez: el gesto se puede repetir mientras dura el rastreo.
        lock (_candado)
        {
            if (_abriendoCollar || _usandoCollar) return;
            _abriendoCollar = true;
        }

        try
        {
            if (!CollarPermanente.Conectado && !await CollarPermanente.ConectarAsync())
            {
                // Y SE ABRE EL LOCAL, no se supone que ya estaba: cuando se entra aquí porque el
                // collar estaba conectado, AbrirMicrofono se lo saltó a propósito. Sin esta línea,
                // un collar que se cae entre medias deja la conversación sin ningún micrófono.
                AbrirLocal();
                LogBus.Log("voz-viva", "sigue el micrófono local: no se abrió ningún collar");
                return;
            }

            CollarPermanente.Capturado += TrozoDelCollar;

            lock (_candado)
            {
                // El local se cierra DESPUÉS de que el collar esté entregando, no antes: entre cerrar
                // uno y abrir el otro no puede haber un hueco sin oír a nadie.
                if (_mic != null) { try { _mic.StopRecording(); _mic.Dispose(); } catch { } _mic = null; }
                _usandoCollar = true;
                _relevo = new Relevo(UmbralRelevoMs);
            }

            _vigilante = new Timer(_ => Vigilar(), null, 1000, 1000);
            LogBus.Log("voz-viva", "la voz entra por el collar Omi; el micrófono local queda de reserva");
            FuenteCambio?.Invoke();
        }
        finally { lock (_candado) _abriendoCollar = false; }
    }

    private void TrozoDelCollar(byte[] trozo)
    {
        // SE REMUESTREA AQUÍ, en el único punto por el que pasa TODO lo que sale del collar — así
        // no hay dos copias de esta decisión, una por cada quien construya LiveAudio.
        var listo = _remuestreadorCollar?.Remuestrear(trozo) ?? trozo;
        if (listo.Length > 0) Capturado?.Invoke(listo);
    }

    // ── el collar por el teléfono ────────────────────────────────────────────

    private FuenteTelefono? _telefono;
    private RemuestreadorPcm16? _remuestreadorTelefono;
    private volatile bool _usandoTelefono;

    /// <summary>Por dónde está entrando la voz AHORA, si es por el teléfono.</summary>
    public bool PorElTelefono => _usandoTelefono;

    /// <summary>
    /// Empieza a oír por el teléfono: el collar habla con la app de Omi y ésta con nuestro canal.
    ///
    /// ENTREGA EL MISMO FORMATO QUE EL COLLAR —PCM16 16 kHz mono, medido el 2026-09-01 en tramas de
    /// 640 bytes— así que reutiliza el mismo remuestreador y sale por el mismo <see cref="Capturado"/>.
    /// Aguas abajo nadie se entera de cuál de las tres fuentes está puesta, que es la promesa 3.
    /// </summary>
    public async Task<bool> PasarAlTelefonoAsync(string proyecto, string clave, string codigo, CancellationToken ct = default)
    {
        EvitarCollar = true;          // un collar habla con un aparato: si va por el teléfono, no va por aquí
        UsarCollar = false;
        if (_usandoCollar) VolverAlLocal("se eligió oír por el teléfono");

        CerrarTelefono();
        var f = new FuenteTelefono(proyecto, clave, codigo);
        _remuestreadorTelefono = RitmoEntrada == RitmoDelCollar ? null : new RemuestreadorPcm16(RitmoDelCollar, RitmoEntrada);

        f.Capturado += TrozoDelTelefono;
        f.Perdido += m => VolverAlLocalDesdeElTelefono(m);
        f.Cambio += () => FuenteCambio?.Invoke();

        if (!await f.AbrirAsync(ct)) { f.Dispose(); return false; }

        _telefono = f;

        // EL LOCAL SIGUE ABIERTO HASTA QUE LLEGUE LA PRIMERA TRAMA, y no es un descuido: unirse al
        // canal no es recibir audio. Si se cerrara el micrófono aquí, un canal que se une y nunca
        // entrega dejaría la consulta muda — que es exactamente el fallo del 2026-08-25 con otro
        // disfraz. El cambio de verdad se hace en TrozoDelTelefono, con la prueba en la mano.
        LogBus.Log("voz-viva", "canal del teléfono abierto; el micrófono local sigue hasta que llegue audio");
        FuenteCambio?.Invoke();
        return true;
    }

    private void TrozoDelTelefono(byte[] trozo)
    {
        if (!_usandoTelefono)
        {
            // LA PRIMERA TRAMA ES LA QUE MANDA. Aquí sí hay prueba de que el teléfono entrega, y
            // sólo entonces se cierra el micrófono del portátil.
            lock (_candado)
            {
                if (_mic != null) { try { _mic.StopRecording(); _mic.Dispose(); } catch { } _mic = null; }
                _usandoTelefono = true;
            }
            LogBus.Log("voz-viva", "la voz entra por el teléfono; el micrófono local queda de reserva");
            FuenteCambio?.Invoke();
        }

        var listo = _remuestreadorTelefono?.Remuestrear(trozo) ?? trozo;
        if (listo.Length > 0) Capturado?.Invoke(listo);
    }

    private void VolverAlLocalDesdeElTelefono(string motivo)
    {
        CerrarTelefono();
        AbrirLocal();
        LogBus.Log("voz-viva", "la voz vuelve al micrófono local · " + motivo);
        FuenteCambio?.Invoke();
    }

    private void CerrarTelefono()
    {
        var f = _telefono;
        _telefono = null;
        _usandoTelefono = false;
        if (f == null) return;
        try { f.Capturado -= TrozoDelTelefono; } catch { }
        try { f.Dispose(); } catch { }
    }

    /// <summary>
    /// Decide si esta conversación deja de oír por el collar. NO desenlaza: el enlace sobrevive.
    /// </summary>
    private void Vigilar()
    {
        var relevo = _relevo;
        if (!_usandoCollar || relevo == null) return;

        if (!CollarPermanente.Conectado) { VolverAlLocal("el collar dejó de entregar audio"); return; }
        if (relevo.HayQueRelevar(CollarPermanente.SinTrama, CollarPermanente.MsDesconectado))
            VolverAlLocal(relevo.Motivo);
    }

    private void VolverAlLocal(string motivo)
    {
        CerrarCollar();
        AbrirLocal();
        LogBus.Log("voz-viva", "la voz vuelve al micrófono local · " + motivo);

        // Y se vuelve a intentar mientras se siga queriendo el collar: perderlo por salirse del
        // alcance es reversible, y sin reintento la única salida era colgar y volver a pedirlo — que
        // desde fuera no se lee como «se cayó», se lee como «esto es inestable» (2026-08-13).
        if (!UsarCollar) return;
        _ = Task.Run(async () =>
        {
            await Task.Delay(ReintentoCollarMs);
            if (UsarCollar && !_usandoCollar) await AbrirCollarAsync();
        });
    }

    /// <summary>Deja de oír por el collar. Soltar el ENLACE es cosa de la pantalla, no de colgar.</summary>
    private void CerrarCollar()
    {
        lock (_candado)
        {
            if (!_usandoCollar) return;
            _usandoCollar = false;
            _relevo = null;
        }
        CollarPermanente.Capturado -= TrozoDelCollar;
        try { _vigilante?.Dispose(); } catch (Exception e) { LogBus.Log("voz-viva", $"al parar el vigía: {e.Message}"); }
        _vigilante = null;
        FuenteCambio?.Invoke();
    }

    /// <summary>Encola audio del modelo. Se reproduce en cuanto llega, sin esperar a la frase entera.</summary>
    public void Reproducir(byte[] pcm)
    {
        if (pcm.Length == 0) return;
        lock (_candado)
        {
            if (_altavoz == null)
            {
                _cola = new BufferedWaveProvider(new WaveFormat(RitmoSalida, 16, 1))
                {
                    // Que descarte lo viejo en vez de reventar: si el audio se acumula porque la
                    // máquina va justa, preferimos perder un fragmento a que se caiga la sesión.
                    BufferDuration = TimeSpan.FromSeconds(30),
                    DiscardOnBufferOverflow = true,
                };
                _altavoz = new WaveOutEvent { DesiredLatency = 120 };
                // EL GRIFO DEL CONSUMO (fase 3): la referencia del AEC se toma de lo que el
                // dispositivo LEE, no de lo que se encola — la cola adelanta frases enteras y
                // una referencia adelantada no casa con el eco que de verdad suena.
                _altavoz.Init(_aec == null ? _cola : new GrifoDeConsumo(_cola, _aec.Referencia));
                _altavoz.Play();
            }
            _cola!.AddSamples(pcm, 0, pcm.Length);

            // Se anota lo fuerte que va a sonar esto. Se queda el pico más alto mientras no haya
            // decaído: dentro de una frase hay silencios cortos, y dejar caer el nivel en cada uno
            // abriría la puerta al eco de la sílaba siguiente.
            //
            // Se compara contra el pico CRUDO, no contra el getter: el getter devuelve el valor ya
            // soltado, así que un trozo flojo lo superaba y bajaba el pico sostenido en vez de
            // mantenerlo. El getter lo pone a cero solo cuando la cola se vacía y termina la caída,
            // que es cuando empieza de verdad una frase nueva.
            double p = Pico(pcm);
            if (p >= _nivelSalida) _nivelSalida = p;
            _cuandoSalida = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Callar AHORA lo que el modelo estaba diciendo.
    ///
    /// Es lo que ocurre cuando el usuario habla encima: Live API avisa con «interrupted» y lo que ya
    /// se había enviado sigue en nuestra cola. Sin vaciarla, Ü seguiría diciendo la frase que el
    /// propio modelo ya dio por cancelada, que es exactamente la sensación de no ser escuchado.
    /// </summary>
    public void Callar()
    {
        lock (_candado) { try { _cola?.ClearBuffer(); } catch { } }
        // Lo pendiente de la referencia tampoco va a sonar ya (promesa 20).
        _aec?.Vacia();
    }

    public void Dispose()
    {
        CerrarMicrofono();
        lock (_candado)
        {
            try { _altavoz?.Stop(); _altavoz?.Dispose(); } catch { }
            _altavoz = null; _cola = null;
            try { _aec?.Dispose(); } catch { }
        }
        try { _remuestreadorCollar?.Dispose(); } catch { }
        CerrarTelefono();
        try { _remuestreadorTelefono?.Dispose(); } catch { }
    }
}
