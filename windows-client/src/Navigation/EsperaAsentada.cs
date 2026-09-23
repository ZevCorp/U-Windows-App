using static U.WindowsClient.Navigation.HuellaDeLoQueSeVe;

namespace U.WindowsClient.Navigation;

/// <summary>
/// LA ESPERA QUE MIRA LO QUE SE VE. Spec 047: «una pantalla que está cargando cambia entre dos miradas; una
/// asentada, no» (la regla de la spec 040, que hasta hoy solo regía en la compuerta de vida).
/// </summary>
/// <remarks>
/// DOS HUELLAS IGUALES SEPARADAS POR UN RESPIRO = ASENTADA. Es lo que hacen los dos referentes que no duermen un
/// tiempo fijo (jev-ultrafast: 2 cuadros; jkudish: dos huellas iguales cada 250 ms, tope 1,5 s). Antes de
/// <c>primeraMs</c> no se declara nada: una página que aún no empezó a pintarse parece asentada.
///
/// EL SITIO SE RELEE FRESCO ANTES DE DECLARAR (regla 2b de la spec). «Dónde» sale de una memoria de 400 ms
/// (<c>MemoriaCorta</c>, FaceWindow :5577-5583) y la mediana del cambio de sitio tras un clic que sí navega es de
/// 405-510 ms: las dos ventanas se cruzan justo donde dos huellas de dentro pueden coincidir con el sitio viejo.
/// Sin esta relectura el veredicto sería «asentada / nada», <c>hasta == desde</c>, y <c>Cruzar</c> no aprendería la
/// puerta: el mapa aprendería menos de lo que cree, y en silencio (aprendizaje nº16).
///
/// SE USA DE DOS FORMAS: <see cref="Espera"/> es el bucle entero, acotado por el <see cref="Compas"/> (gasta del reloj,
/// no cuenta vueltas: promesa 245); y el sondeo suelto (<see cref="Sondea"/>) dentro de la espera de <c>Pulsa</c>, que
/// además mira la ubicación: desde la fase 2 decide con él (351), y en los casos de la regla 4 de la spec lo corre EN
/// SOMBRA, solo para la línea de la 355.
/// </remarks>
public sealed class EsperaAsentada
{
    /// <summary>
    /// LAS DOS METAS DE LA REGLA, en un solo sitio: cuánto tienen que separarse dos huellas iguales (250, jkudish) y antes
    /// de cuándo no se declara nada (400, el de la compuerta, 299). METAS, NO DATOS: las fija el nivel 4 de la fase 0.
    /// </summary>
    /// <remarks>
    /// Viven aquí desde la fase 7 (358), cuando las herramientas del mapa pasaron a usar la misma regla. Antes el 250 estaba
    /// escrito en 2 sitios (Pulsa y la huella en vivo) y el 400 en 1; una tercera copia en el mapa habría hecho que el día
    /// que la medida los cambie, el sitio olvidado juzgara «asentada» con otra regla (aprendizaje nº16).
    /// </remarks>
    public const int RespiroMetaMs = 250;
    /// <summary>Antes de esto no se declara nada: una página que aún no empezó a pintarse parece asentada. Meta; ver <see cref="RespiroMetaMs"/>.</summary>
    public const int PrimeraMetaMs = 400;

    /// <summary>Por qué dejó de esperar. Las «Techo…» son las causas que la cuenta tiene que distinguir (patrón nº2).</summary>
    public enum PorQue { Asentada, CambioDeSitio, TechoSeMovia, TechoNadieMiraba, TechoSabeQueLleva, TechoNoSePudoMirar }

    /// <summary>Qué le pasó a la pantalla en un sondeo.</summary>
    public enum Paso { Sigue, Asentada, CambioDeSitio, NoSePudoMirar }

    public readonly record struct Veredicto(
        QueCambio QueCambio, Parte Parte, long MsHastaElVeredicto, PorQue PorQueDejoDeEsperar,
        string Causa, string SitioAhora, int Sondeos);

    private readonly Func<HuellaDeLoQueSeVe?> _huella;
    private readonly Func<string>? _sitioFresco;
    private readonly Func<string, string, bool> _mismoSitio;
    private readonly HuellaDeLoQueSeVe _antes;
    private readonly int _respiroMs, _primeraMs;

    private HuellaDeLoQueSeVe? _referencia;   // la huella con la que empezó la racha quieta
    // CUÁNDO SE LEYÓ DE VERDAD lo de dentro de la referencia, en el reloj de esta espera: el instante del sondeo menos la edad de
    // su lectura (revisión del 23-09). Puede ser negativo: la lectura de antes de tocar, que la huella en vivo reutiliza.
    private long _lecturaDeLaReferencia;
    private HuellaDeLoQueSeVe? _ultima;
    // NULO = AÚN NO SE RELEYÓ, y no long.MinValue: «t - long.MinValue» desborda a negativo (sonda del 22-09: -9,2e18) y la
    // relectura de cada respiro no ocurría nunca antes de la primera asentada; la 355 lo midió: «sitio fresco: 0 veces».
    private long? _tFresco;
    private bool _rota;

    /// <summary>A los cuántos ms dos huellas coincidieron por primera vez con un respiro en medio; -1 = nunca.</summary>
    public long MsAsentada { get; private set; } = -1;
    /// <summary>En qué sondeo se asentó.</summary>
    public int SondeoAsentada { get; private set; }
    /// <summary>A los cuántos ms el sitio FRESCO dejó de ser el de partida; -1 = no cambió.</summary>
    public long MsCambioDeSitio { get; private set; } = -1;
    /// <summary>Cuántos sondeos vieron una huella distinta de la anterior.</summary>
    public int VecesQueSeMovio { get; private set; }
    /// <summary>Cuántas de esas fueron DESPUÉS de haberse asentado: una «asentada» que luego se movió es una asentada falsa.</summary>
    public int VecesQueSeMovioTrasAsentarse { get; private set; }
    public int Sondeos { get; private set; }
    /// <summary>Por qué no se pudo mirar: la cadena entera de la excepción (patrón nº3). Vacío = se pudo.</summary>
    public string Causa { get; private set; } = "";
    public string SitioAhora { get; private set; }
    public HuellaDeLoQueSeVe? Ultima => _ultima;
    /// <summary>La suma y el máximo de lo que costó cada parte, sobre los sondeos que sí miraron.</summary>
    public Costes CosteSumado { get; private set; }
    public Costes CosteMaximo { get; private set; }
    public int SondeosConCoste { get; private set; }
    public long SitioFrescoMsSumado { get; private set; }
    public long SitioFrescoMsMaximo { get; private set; }
    public int SitioFrescoVeces { get; private set; }
    /// <summary>
    /// Cuántas veces el sitio releído fresco llegó VACÍO justo al ir a declarar «asentada». Vacío no es «no cambió» (patrón nº9):
    /// sin sitio que juzgar no se declara nada, y se sigue mirando.
    /// </summary>
    public int SitioFrescoVacioAlDeclarar { get; private set; }

    /// <summary>
    /// Por qué no se declaró la asentada aunque las huellas coincidían, cuando fue por el sitio fresco vacío; vacío = no fue por eso.
    /// Las palabras las usan las tres esperas que consumen esta clase: una sola definición (patrón nº5).
    /// </summary>
    public string AvisoDelSitio => SitioFrescoVacioAlDeclarar == 0 ? ""
        : $"el sitio releído fresco llegó vacío {SitioFrescoVacioAlDeclarar} vez/veces justo cuando las huellas coincidían, y vacío no es «no cambió»";

    /// <param name="sitioFresco">La ubicación sin memoria, para la regla 2b. Nulo = no hay sitio que juzgar (quien llama no tiene el de
    /// antes): decide solo la huella, y no se relee nada.</param>
    /// <param name="mismoSitio">EL COMPARADOR DE SITIOS DE QUIEN LLAMA (revisión del 23-09): <c>Pulsa</c> juzga «cambió» con
    /// <c>ahora != desde</c>, y la llegada y el mapa con <c>Superficies.MismaPantalla</c>. Hasta ese día aquí se comparaba siempre
    /// con Ordinal: «web://www.google.com/search» antes del Enter y «web://google.com/search» después (medido el 18-09, 7 veces en
    /// una sesión) hacía salir la espera del mapa en la primera relectura como «cambió de sitio», y acto seguido Type decidía que
    /// era la misma pantalla. El mismo ORIGEN no es el mismo CAMINO (aprendizaje nº16): cada consumidor compara como su llamador.</param>
    public EsperaAsentada(Func<HuellaDeLoQueSeVe?> huella, Func<string>? sitioFresco, HuellaDeLoQueSeVe antes, int respiroMs, int primeraMs,
        Func<string, string, bool> mismoSitio)
    {
        _huella = huella; _sitioFresco = sitioFresco; _antes = antes; _mismoSitio = mismoSitio;
        _respiroMs = Math.Max(0, respiroMs); _primeraMs = Math.Max(0, primeraMs);
        SitioAhora = antes.Sitio;
    }

    /// <summary>
    /// Un sondeo, a los <paramref name="t"/> ms de empezar la espera. Toma la huella, relee el sitio fresco una vez
    /// por respiro y siempre antes de declarar «asentada», y dice qué pasó. Sigue anotando después de asentarse
    /// (para contar las asentadas falsas), y no vuelve a mirar después de no poder.
    /// </summary>
    public Paso Sondea(long t)
    {
        if (_rota) return Paso.NoSePudoMirar;
        Sondeos++;
        HuellaDeLoQueSeVe? h;
        try { h = _huella(); }
        catch (Exception e) { _rota = true; Causa = "no pude mirar: " + Cadena(e); return Paso.NoSePudoMirar; }
        if (h == null) { _rota = true; Causa = "no pude mirar: la huella no devolvió nada"; return Paso.NoSePudoMirar; }

        _ultima = h;
        CosteSumado += h.Coste; CosteMaximo = Costes.Max(CosteMaximo, h.Coste); SondeosConCoste++;

        // CUÁNDO SE LEYÓ DE VERDAD lo de dentro de esta huella (revisión del 23-09; bloqueaba): la huella en vivo lo reutiliza
        // durante un respiro, y también la lectura de antes de tocar. La regla se cumple en lo OBSERVADO, no en el reloj de los
        // sondeos: la última lectura real tiene que ser posterior a la primera huella y estar a un respiro de la de la referencia.
        long lectura = t - h.EdadDeDentroMs;
        if (_referencia == null || !Iguales(_referencia, h))
        {
            if (_referencia != null)
            {
                VecesQueSeMovio++;
                if (MsAsentada >= 0) VecesQueSeMovioTrasAsentarse++;
            }
            _referencia = h; _lecturaDeLaReferencia = lectura;
        }
        else if (MsAsentada < 0 && lectura >= _primeraMs && lectura - _lecturaDeLaReferencia >= _respiroMs)
        {
            // ANTES DE DECLARAR, EL SITIO FRESCO (regla 2b): si cambió, manda el cambio de sitio aunque las dos huellas
            // coincidieran. Y si no se pudo releer, o llegó vacío, NO se declara (revisión del 23-09): hasta ese día la
            // asentada salía igual, y la cuenta decía «el sitio fresco sin cambiar» en la misma espera cuya línea decía «no pude
            // releer el sitio» —un mensaje que concluye, patrón nº2— o tomaba el vacío por «no cambió» (nº9).
            switch (ReleeElSitio(t))
            {
                case Relectura.Cambio: return Paso.CambioDeSitio;
                case Relectura.Rota: return Paso.NoSePudoMirar;
                case Relectura.Vacia: SitioFrescoVacioAlDeclarar++; return Paso.Sigue;
            }
            MsAsentada = t; SondeoAsentada = Sondeos;
            return Paso.Asentada;
        }

        if (_sitioFresco != null && (_tFresco is not long tFresco || t - tFresco >= _respiroMs))
            switch (ReleeElSitio(t))
            {
                case Relectura.Cambio: return Paso.CambioDeSitio;
                case Relectura.Rota: return Paso.NoSePudoMirar;   // hasta el 23-09 este sondeo decía «sigue» y el siguiente, «no pude»
            }
        return Paso.Sigue;
    }

    /// <summary>Lo que dio una relectura del sitio fresco. <see cref="Vacia"/> y <see cref="Rota"/> no son «no cambió» (patrones nº9 y nº2).</summary>
    private enum Relectura { Igual, Cambio, Vacia, Rota, SinSitio }

    /// <summary>Relee el sitio fresco y dice si dejó de ser el de partida, con el comparador de quien llama. Anota la primera vez que pasó.</summary>
    private Relectura ReleeElSitio(long t)
    {
        if (_sitioFresco == null) return Relectura.SinSitio;   // no hay sitio que juzgar: decide la huella
        var crono = System.Diagnostics.Stopwatch.StartNew();
        string s;
        try { s = _sitioFresco() ?? ""; }
        catch (Exception e) { _rota = true; Causa = "no pude releer el sitio: " + Cadena(e); return Relectura.Rota; }
        crono.Stop();
        _tFresco = t;
        SitioFrescoVeces++; SitioFrescoMsSumado += crono.ElapsedMilliseconds; SitioFrescoMsMaximo = Math.Max(SitioFrescoMsMaximo, crono.ElapsedMilliseconds);
        if (string.IsNullOrWhiteSpace(s)) return Relectura.Vacia;
        SitioAhora = s;
        if (_mismoSitio(s, _antes.Sitio)) return Relectura.Igual;
        if (MsCambioDeSitio < 0) MsCambioDeSitio = t;
        return Relectura.Cambio;
    }

    /// <summary>El veredicto con lo anotado hasta los <paramref name="t"/> ms. <paramref name="siLlegoAlTecho"/> es la causa cuando no decidió nada.</summary>
    public Veredicto Cierra(long t, PorQue siLlegoAlTecho)
    {
        var dif = _ultima == null ? new Diferencia(QueCambio.Nada, Parte.Nada) : Comparar(_antes, _ultima);
        if (MsCambioDeSitio >= 0) return new(QueCambio.DeSitio, Parte.Sitio, MsCambioDeSitio, PorQue.CambioDeSitio, "", SitioAhora, Sondeos);
        if (_rota) return new(dif.QueCambio, dif.Parte, t, PorQue.TechoNoSePudoMirar, Causa, SitioAhora, Sondeos);
        if (MsAsentada >= 0 && VecesQueSeMovioTrasAsentarse == 0) return new(dif.QueCambio, dif.Parte, MsAsentada, PorQue.Asentada, "", SitioAhora, Sondeos);
        // SE ASENTÓ A LA VISTA PERO EL SITIO LLEGÓ VACÍO AL IR A DECLARARLA: no se pudo mirar el sitio, y se dice con esas palabras.
        if (MsAsentada < 0 && SitioFrescoVacioAlDeclarar > 0) return new(dif.QueCambio, dif.Parte, t, PorQue.TechoNoSePudoMirar, AvisoDelSitio, SitioAhora, Sondeos);
        return new(dif.QueCambio, dif.Parte, t, siLlegoAlTecho, "", SitioAhora, Sondeos);
    }

    /// <summary>
    /// EL BUCLE ENTERO: sondea cada <paramref name="cadenciaMs"/> hasta asentarse, cambiar de sitio, no poder mirar, o
    /// agotar el <paramref name="compas"/>. Gasta del reloj: con un sondeo lento termina en el techo más un sondeo, no
    /// en techo × vueltas (la forma de la promesa 245; promesa 358).
    /// </summary>
    public static Veredicto Espera(Func<HuellaDeLoQueSeVe?> huella, Func<string>? sitioFresco, HuellaDeLoQueSeVe antes,
        Compas compas, int respiroMs, int primeraMs, int cadenciaMs, Func<string, string, bool> mismoSitio)
    {
        var s = new EsperaAsentada(huella, sitioFresco, antes, respiroMs, primeraMs, mismoSitio);
        do
        {
            var paso = s.Sondea(compas.Transcurrido);
            if (paso != Paso.Sigue) return s.Cierra(compas.Transcurrido, PorQue.TechoSeMovia);
        }
        while (compas.Respira(Math.Max(1, cadenciaMs)));
        return s.Cierra(compas.Transcurrido, PorQue.TechoSeMovia);
    }

    /// <summary>La medida de esta espera en una frase: para la línea de la 355.</summary>
    public string Resumen()
    {
        string asentada = _rota ? Causa
            : MsAsentada >= 0
                ? $"asentada a los {MsAsentada} ms (sondeo {SondeoAsentada}" + (VecesQueSeMovioTrasAsentarse > 0 ? $"; se movió {VecesQueSeMovioTrasAsentarse} vez/veces DESPUÉS: asentada falsa)" : ")")
                : $"nunca se asentó (se movió en {VecesQueSeMovio} de {Sondeos} sondeo(s))" + (AvisoDelSitio.Length > 0 ? $"; {AvisoDelSitio}" : "");
        string coste = SondeosConCoste == 0 ? "coste: sin sondeos"
            : $"coste por sondeo (media/máx ms): sitio {CosteSumado.SitioMs / SondeosConCoste}/{CosteMaximo.SitioMs} · delante {CosteSumado.DelanteMs / SondeosConCoste}/{CosteMaximo.DelanteMs} · dentro {CosteSumado.DentroMs / SondeosConCoste}/{CosteMaximo.DentroMs} · ventanas {CosteSumado.VentanasMs / SondeosConCoste}/{CosteMaximo.VentanasMs}";
        string fresco = SitioFrescoVeces == 0 ? "sitio fresco: 0 veces"
            : $"sitio fresco {SitioFrescoMsSumado / SitioFrescoVeces}/{SitioFrescoMsMaximo} ms × {SitioFrescoVeces}";
        return $"{asentada} · {coste} · {fresco} · {Sondeos} sondeo(s) de huella";
    }

    /// <summary>La cadena ENTERA de la excepción: un mensaje envuelto se guarda para sí el porqué (patrón nº3). La usa también
    /// la llegada (356), para no escribir una tercera copia.</summary>
    internal static string Cadena(Exception e)
    {
        var partes = new List<string>();
        for (Exception? x = e; x != null; x = x.InnerException) partes.Add($"{x.GetType().Name}: {x.Message}");
        return string.Join(" ← ", partes);
    }
}
