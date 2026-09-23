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
    /// <summary>Por qué dejó de esperar. Las «Techo…» son las causas que la cuenta tiene que distinguir (patrón nº2).</summary>
    public enum PorQue { Asentada, CambioDeSitio, TechoSeMovia, TechoNadieMiraba, TechoSabeQueLleva, TechoNoSePudoMirar }

    /// <summary>Qué le pasó a la pantalla en un sondeo.</summary>
    public enum Paso { Sigue, Asentada, CambioDeSitio, NoSePudoMirar }

    public readonly record struct Veredicto(
        QueCambio QueCambio, Parte Parte, long MsHastaElVeredicto, PorQue PorQueDejoDeEsperar,
        string Causa, string SitioAhora, int Sondeos);

    private readonly Func<HuellaDeLoQueSeVe?> _huella;
    private readonly Func<string> _sitioFresco;
    private readonly HuellaDeLoQueSeVe _antes;
    private readonly int _respiroMs, _primeraMs;

    private HuellaDeLoQueSeVe? _referencia;   // la huella con la que empezó la racha quieta
    private long _tReferencia;
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

    public EsperaAsentada(Func<HuellaDeLoQueSeVe?> huella, Func<string> sitioFresco, HuellaDeLoQueSeVe antes, int respiroMs, int primeraMs)
    {
        _huella = huella; _sitioFresco = sitioFresco; _antes = antes;
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

        if (_referencia == null || !Iguales(_referencia, h))
        {
            if (_referencia != null)
            {
                VecesQueSeMovio++;
                if (MsAsentada >= 0) VecesQueSeMovioTrasAsentarse++;
            }
            _referencia = h; _tReferencia = t;
        }
        else if (MsAsentada < 0 && t >= _primeraMs && t - _tReferencia >= _respiroMs)
        {
            // ANTES DE DECLARAR, EL SITIO FRESCO (regla 2b): si cambió, manda el cambio de sitio aunque las dos
            // huellas coincidieran.
            if (ReleeElSitio(t)) return Paso.CambioDeSitio;
            MsAsentada = t; SondeoAsentada = Sondeos;
            return Paso.Asentada;
        }

        if ((_tFresco is not long tFresco || t - tFresco >= _respiroMs) && ReleeElSitio(t)) return Paso.CambioDeSitio;
        return Paso.Sigue;
    }

    /// <summary>Relee el sitio fresco y dice si dejó de ser el de partida. Anota la primera vez que pasó.</summary>
    private bool ReleeElSitio(long t)
    {
        var crono = System.Diagnostics.Stopwatch.StartNew();
        string s;
        try { s = _sitioFresco() ?? ""; }
        catch (Exception e) { _rota = true; Causa = "no pude releer el sitio: " + Cadena(e); return false; }
        crono.Stop();
        _tFresco = t;
        SitioFrescoVeces++; SitioFrescoMsSumado += crono.ElapsedMilliseconds; SitioFrescoMsMaximo = Math.Max(SitioFrescoMsMaximo, crono.ElapsedMilliseconds);
        if (s.Length > 0) SitioAhora = s;
        bool cambio = s.Length > 0 && !string.Equals(s, _antes.Sitio, StringComparison.Ordinal);
        if (cambio && MsCambioDeSitio < 0) MsCambioDeSitio = t;
        return cambio;
    }

    /// <summary>El veredicto con lo anotado hasta los <paramref name="t"/> ms. <paramref name="siLlegoAlTecho"/> es la causa cuando no decidió nada.</summary>
    public Veredicto Cierra(long t, PorQue siLlegoAlTecho)
    {
        var dif = _ultima == null ? new Diferencia(QueCambio.Nada, Parte.Nada) : Comparar(_antes, _ultima);
        if (MsCambioDeSitio >= 0) return new(QueCambio.DeSitio, Parte.Sitio, MsCambioDeSitio, PorQue.CambioDeSitio, "", SitioAhora, Sondeos);
        if (_rota) return new(dif.QueCambio, dif.Parte, t, PorQue.TechoNoSePudoMirar, Causa, SitioAhora, Sondeos);
        if (MsAsentada >= 0 && VecesQueSeMovioTrasAsentarse == 0) return new(dif.QueCambio, dif.Parte, MsAsentada, PorQue.Asentada, "", SitioAhora, Sondeos);
        return new(dif.QueCambio, dif.Parte, t, siLlegoAlTecho, "", SitioAhora, Sondeos);
    }

    /// <summary>
    /// EL BUCLE ENTERO: sondea cada <paramref name="cadenciaMs"/> hasta asentarse, cambiar de sitio, no poder mirar, o
    /// agotar el <paramref name="compas"/>. Gasta del reloj: con un sondeo lento termina en el techo más un sondeo, no
    /// en techo × vueltas (la forma de la promesa 245; promesa 358).
    /// </summary>
    public static Veredicto Espera(Func<HuellaDeLoQueSeVe?> huella, Func<string> sitioFresco, HuellaDeLoQueSeVe antes,
        Compas compas, int respiroMs, int primeraMs, int cadenciaMs)
    {
        var s = new EsperaAsentada(huella, sitioFresco, antes, respiroMs, primeraMs);
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
                : $"nunca se asentó (se movió en {VecesQueSeMovio} de {Sondeos} sondeo(s))";
        string coste = SondeosConCoste == 0 ? "coste: sin sondeos"
            : $"coste por sondeo (media/máx ms): sitio {CosteSumado.SitioMs / SondeosConCoste}/{CosteMaximo.SitioMs} · delante {CosteSumado.DelanteMs / SondeosConCoste}/{CosteMaximo.DelanteMs} · dentro {CosteSumado.DentroMs / SondeosConCoste}/{CosteMaximo.DentroMs} · ventanas {CosteSumado.VentanasMs / SondeosConCoste}/{CosteMaximo.VentanasMs}";
        string fresco = SitioFrescoVeces == 0 ? "sitio fresco: 0 veces"
            : $"sitio fresco {SitioFrescoMsSumado / SitioFrescoVeces}/{SitioFrescoMsMaximo} ms × {SitioFrescoVeces}";
        return $"{asentada} · {coste} · {fresco} · {Sondeos} sondeo(s) de huella";
    }

    /// <summary>La cadena ENTERA de la excepción: un mensaje envuelto se guarda para sí el porqué (patrón nº3).</summary>
    private static string Cadena(Exception e)
    {
        var partes = new List<string>();
        for (Exception? x = e; x != null; x = x.InnerException) partes.Add($"{x.GetType().Name}: {x.Message}");
        return string.Join(" ← ", partes);
    }
}
