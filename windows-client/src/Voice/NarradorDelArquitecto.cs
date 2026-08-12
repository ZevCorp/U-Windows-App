using System.Text.RegularExpressions;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Voice;

/// <summary>
/// LA VOZ QUE CUENTA EL MAPEO MIENTRAS OCURRE.
///
/// El arquitecto navega la app durante minutos y todo lo que hace sale por el log: un río de
/// llamadas a herramientas que solo se entiende leyéndolo con calma. Quien mira la pantalla ve una
/// app moviéndose sola sin saber por qué. Esto lo convierte en alguien contando lo que va
/// entendiendo — «este botón solo aparece si entro aquí, así que es de esta sección» — con la
/// misma voz de la conversación en vivo (2026-08-12, pedido por el usuario).
///
/// NO ESCRIBE LA NARRACIÓN. Le pasa a Gemini los hechos crudos de cada paso y deja que él los
/// cuente. Escribir aquí las frases sería poner en boca del sistema conclusiones que el sistema no
/// sacó — un narrador que dice «¡ah, esto es cromo!» sin que nadie lo haya derivado es exactamente
/// la clase de mentira que este proyecto lleva semanas quitando del grafo. Aquí van los hechos; la
/// interpretación es suya, y si se equivoca se le nota, que es como debe ser.
///
/// Y PUEDE SEÑALAR MIENTRAS HABLA, sin que haga falta nada nuevo: Gemini ya tiene `map_show` entre
/// sus herramientas —la misma con la que ilumina un elemento cuando le preguntas «¿ves esto?»— y la
/// instrucción de arranque le dice que la use al nombrar algo. Reutilizar ese camino en vez de
/// construir un señalador propio es lo que impide que existan dos formas de señalar que acaben
/// divergiendo.
/// </summary>
public sealed class NarradorDelArquitecto : IDisposable
{
    private readonly GeminiLive _voz;
    private readonly Queue<string> _cola = new();
    private readonly object _llave = new();
    private bool _hablando;
    private bool _encendido;
    private bool _laEncendimosNosotros;
    private string _app = "";

    /// <summary>
    /// Cuántos hechos se guardan esperando turno. Al llegar al tope se tira el MÁS VIEJO.
    ///
    /// Es deliberado y va contra la intuición: lo normal sería descartar lo nuevo para no perder el
    /// hilo. Pero el arquitecto va más rápido de lo que se habla, así que una cola que crece
    /// acabaría narrando con un minuto de retraso lo que ya pasó — y una narración desfasada es
    /// peor que un hueco, porque quien mira la pantalla ve una cosa y oye otra.
    /// </summary>
    private const int TopeCola = 4;

    public NarradorDelArquitecto(GeminiLive voz)
    {
        _voz = voz;
        _voz.Cerro += TerminoDeHablar;
    }

    /// <summary>
    /// Empieza a narrar el mapeo de una app, ENCENDIENDO LA VOZ si hiciera falta.
    ///
    /// La enciende en vez de exigir que ya lo esté porque lanzar el arquitecto ES pedir que te
    /// cuenten el mapeo: obligar a acordarse de pulsar el micrófono antes convertía la narración en
    /// algo que se pierde justo cuando más se quiere (2026-08-12, corregido por el usuario). Se
    /// apaga sola al terminar solo si la encendimos nosotros: si ya estaba abierta era una
    /// conversación suya, y colgársela sería quitarle algo que no habíamos dado.
    /// </summary>
    public async Task EmpezarAsync(string app)
    {
        if (!_voz.Viva)
        {
            LogBus.Log("narrador", "la voz en vivo estaba apagada: se enciende para narrar el mapeo");
            await _voz.ArrancarAsync();
            _laEncendimosNosotros = _voz.Viva;
            if (!_voz.Viva)
            {
                LogBus.Log("narrador", "no se pudo encender la voz: el mapeo va callado");
                return;
            }
        }
        lock (_llave)
        {
            _app = app;
            _encendido = true;
            _cola.Clear();
        }
        LogBus.Logged += Oir;
        Encolar($"Vas a narrar en voz alta un mapeo automático de «{app}» mientras ocurre. "
              + "Te iré pasando lo que el explorador va haciendo, paso a paso, en crudo. "
              + "Cuéntalo como quien va entendiendo la app en el momento: en una o dos frases "
              + "cortas por paso, sin listas ni tecnicismos, y solo lo que los hechos sostengan — "
              + "si algo te parece una conclusión pero nadie la ha comprobado, dilo como sospecha. "
              + "SEÑALA MIENTRAS HABLAS con map_show: un elemento cuando nombres uno, y varios "
              + "separados por comas cuando hables de un grupo («Inicio, Catálogo, Informes, "
              + "Ajustes») para que se recorran todos a la vista. Si un paso no aporta nada nuevo, "
              + "cállatelo: es mejor un silencio que rellenar. Empieza ya.");
    }

    /// <summary>
    /// Deja de narrar. El último turno que ya salió se termina de decir.
    ///
    /// Y APAGA LA VOZ SOLO SI LA ENCENDIMOS NOSOTROS. Si el micrófono ya estaba abierto cuando
    /// arrancó el mapeo, es que había una conversación en curso: cerrarla al terminar de narrar
    /// sería colgarle a alguien a mitad de frase.
    /// </summary>
    public async Task PararAsync()
    {
        bool apagar;
        lock (_llave)
        {
            if (!_encendido) return;
            _encendido = false;
            _cola.Clear();
            apagar = _laEncendimosNosotros;
            _laEncendimosNosotros = false;
        }
        LogBus.Logged -= Oir;
        if (apagar)
        {
            LogBus.Log("narrador", "terminó el mapeo: se apaga la voz que habíamos encendido");
            await _voz.TerminarAsync();
        }
    }

    /// <summary>
    /// Escucha el log y se queda solo con lo que el ARQUITECTO hace. El log lleva de todo —la
    /// sonda, el mapa, el dibujo— y narrarlo entero sería leerle a alguien el registro del sistema.
    /// </summary>
    private void Oir(object? _, string linea)
    {
        if (!_encendido) return;
        // El puente escribe cada línea suya con la etiqueta «arquitecto:». Lo demás no es él.
        int i = linea.IndexOf("arquitecto: ", StringComparison.Ordinal);
        if (i < 0) return;
        string dice = linea[(i + "arquitecto: ".Length)..].Trim();

        string hecho = Traducir(dice);
        if (hecho.Length > 0) Encolar(hecho);
    }

    /// <summary>
    /// De la línea cruda del arquitecto al HECHO que se le cuenta a la voz.
    ///
    /// Traduce, no interpreta: «cruzó "Familia A" y llegó a maqueta-familia-a» es un hecho;
    /// «descubrió que Familia A es navegación de nivel 2» sería una conclusión, y esa la saca quien
    /// narra o nadie.
    /// </summary>
    private static string Traducir(string l)
    {
        // Su razonamiento va tal cual: ya está en primera persona y es lo más informativo que hay.
        if (l.StartsWith("[arquitecto]", StringComparison.Ordinal))
        {
            string t = l["[arquitecto]".Length..].Trim();
            return t.Length > 0 ? $"El explorador está pensando: «{t}»" : "";
        }

        if (!l.StartsWith("→", StringComparison.Ordinal)) return "";

        // Las llamadas vienen como  → cruzar({"puerta":"Familia A"})
        var m = Regex.Match(l, @"^→\s*(\w+)\((.*)\)\s*$", RegexOptions.Singleline);
        if (!m.Success) return "";
        string tool = m.Groups[1].Value;
        string args = m.Groups[2].Value;
        string que = Regex.Match(args, @"""(?:puerta|salida|pantalla)""\s*:\s*""([^""]+)""").Groups[1].Value;

        return tool switch
        {
            "cruzar" when que.Length > 0 => $"Acaba de pulsar «{que}» para ver a dónde lleva.",
            "ir_a" when que.Length > 0 => $"Está volviendo a una pantalla que ya conocía: {Corto(que)}.",
            "marcar_accion" when que.Length > 0 => $"Ha decidido que «{que}» hace algo pero no lleva a otra pantalla.",
            "marcar_atras" when que.Length > 0 => $"Ha identificado «{que}» como el botón de volver de esta app.",
            "fijar_nivel" when que.Length > 0 => $"Está declarando en qué nivel de la app vive «{que}».",
            "mirar" => "Está mirando una foto de la pantalla para decidir qué es cada cosa.",
            "que_veo" => "",              // pura lectura: narrarla sería contar que respira
            "donde_estoy" => "",
            "rutas_desde" => "",
            "jerarquia_del_grafo" => "",
            "sin_situar" => "",
            "cuanto_entiende" => "Está comprobando cuánto entiende ya de la app.",
            "feedback" => "Ha encontrado algo que no cuadra y lo está apuntando para nosotros.",
            _ => "",
        };
    }

    private static string Corto(string id)
    {
        int i = id.LastIndexOf('/');
        return i >= 0 && i < id.Length - 1 ? id[(i + 1)..] : id;
    }

    /// <summary>
    /// Pone un hecho en la cola y suelta el siguiente si no se está hablando.
    ///
    /// LA COLA ES LA PIEZA QUE HACE QUE ESTO NO SEA UN DESASTRE. `EnviarTextoAsync` abre un turno
    /// nuevo, y Gemini corta lo que estuviera diciendo para atenderlo. El arquitecto llama a una
    /// herramienta cada uno o dos segundos —más rápido de lo que se pronuncia una frase—, así que
    /// mandárselos según llegan lo dejaría tartamudeando: empezaría cada frase y no terminaría
    /// ninguna. Se espera a que cierre turno (evento `Cerro`) y solo entonces va el siguiente.
    /// </summary>
    private void Encolar(string hecho)
    {
        bool soltarYa;
        lock (_llave)
        {
            if (!_encendido && _cola.Count == 0 && !_hablando) { /* el arranque entra igual */ }
            _cola.Enqueue(hecho);
            while (_cola.Count > TopeCola) _cola.Dequeue();
            soltarYa = !_hablando;
            if (soltarYa) _hablando = true;
        }
        if (soltarYa) Soltar();
    }

    private void TerminoDeHablar()
    {
        lock (_llave) { _hablando = false; }
        Soltar();
    }

    private async void Soltar()
    {
        string? siguiente;
        lock (_llave)
        {
            if (_cola.Count == 0) { _hablando = false; return; }
            siguiente = _cola.Dequeue();
            _hablando = true;
        }
        try { await _voz.EnviarTextoAsync(siguiente); }
        catch (Exception e)
        {
            LogBus.Log("narrador", $"no pude contarlo: {e.Message}");
            lock (_llave) { _hablando = false; }
        }
    }

    public void Dispose()
    {
        _ = PararAsync();
        _voz.Cerro -= TerminoDeHablar;
    }
}
