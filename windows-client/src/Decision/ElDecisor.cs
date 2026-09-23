using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace U.WindowsClient.Decision;

/// <summary>
/// LO QUE SE DECIDIÓ, Y SI SE PUEDE ACTUAR CON ELLO.
/// </summary>
/// <remarks>
/// <see cref="Confianza"/> VIENE TAMBIÉN CUANDO NO SE ACTÚA, y ese es medio punto de la pieza: el
/// umbral hay que ajustarlo con datos del terreno, y para eso hace falta ver las confianzas de las
/// decisiones que se descartaron. Una decisión que se tira sin dejar su número no deja aprender.
/// </remarks>
public sealed class DecisionDeUnPaso
{
    /// <summary>Si el llamador puede tomar <see cref="Puerta"/>.</summary>
    public bool Actuar { get; }

    /// <summary>La puerta elegida, tal como la nombra el inventario. Vacía si no se actúa.</summary>
    public string Puerta { get; }

    /// <summary>Lo segura que estaba, de 0 a 1. Se conserva aunque no se actúe.</summary>
    public double Confianza { get; }

    /// <summary>
    /// Qué pasó. DESCRIBE EL PASO, NO CONCLUYE (patrón nº2): «contestó "Grabar", que no está en el
    /// inventario» y «se agotó el plazo» son cosas distintas y tienen que poder distinguirse.
    /// </summary>
    public string Porque { get; }

    /// <summary>
    /// TODAS LAS OPCIONES CON SU PROBABILIDAD, de mayor a menor, la elegida incluida (promesa 288). Jev las
    /// devuelve en la misma respuesta: la segunda mejor viene gratis, y tirarla obliga a otra llamada cuando
    /// la primera no está viva.
    /// </summary>
    public IReadOnlyList<(string Puerta, double Probabilidad)> Alternativas { get; init; } = Array.Empty<(string, double)>();

    /// <summary>Cuánto dice Jev que el objetivo YA está cumplido en esta pantalla (0-1). Si no vino o no se entendió, el caso peor: 0, y <see cref="QueNoCuadro"/> lo dice (389).</summary>
    public double Cumplido { get; init; }

    /// <summary>Cuánto dice Jev que accionar la elegida es irreversible o peligroso (0-1). Si no vino o no se entendió, el caso peor: 1, y <see cref="QueNoCuadro"/> lo dice (389).</summary>
    public double Peligro { get; init; }

    /// <summary>
    /// Vacío si la respuesta de Jev estaba en forma. Si no, TODAS las reglas que falló, cada una con su campo
    /// y su valor crudo, separadas por « · » (promesa 388). Es un dato, no una conclusión (patrón nº2): es lo
    /// que se pinta y lo que se calibra. Con algo aquí, <see cref="Actuar"/> es falso y <see cref="Alternativas"/>
    /// está vacía: la segunda mejor de una distribución inválida no es una segunda mejor.
    /// </summary>
    public string QueNoCuadro { get; init; } = "";

    /// <summary>Cuántos ids de puerta se le entregaron a Jev en el cuerpo, sin contar «ninguna» (350). 0 si no se le preguntó.</summary>
    public int Viajaron { get; init; }

    /// <summary>De esos, cuántos fueron SIN SU TEXTO: filas que viajaron como «N) fila (Tipo)» (350).</summary>
    public int FilasSinTexto { get; init; }

    /// <summary>
    /// La longitud del cuerpo que se le entregó al transporte, en caracteres (350); 0 si no se le entregó nada. Si el
    /// transporte lanzó, es lo que se le dio, no lo que llegó a salir: eso no lo sabe esta pieza.
    /// </summary>
    public int Caracteres { get; init; }

    private DecisionDeUnPaso(bool actuar, string puerta, double confianza, string porque)
    {
        Actuar = actuar;
        Puerta = puerta;
        Confianza = confianza;
        Porque = porque;
    }

    internal static DecisionDeUnPaso Si(string puerta, double confianza, string porque) =>
        new DecisionDeUnPaso(true, puerta, confianza, porque);

    internal static DecisionDeUnPaso No(string porque, double confianza = 0) =>
        new DecisionDeUnPaso(false, "", confianza, porque);

    internal DecisionDeUnPaso Con(IReadOnlyList<(string, double)> alternativas, double cumplido, double peligro, string queNoCuadro = "") =>
        new DecisionDeUnPaso(Actuar, Puerta, Confianza, Porque)
            { Alternativas = alternativas, Cumplido = cumplido, Peligro = peligro, QueNoCuadro = queNoCuadro,
              Viajaron = Viajaron, FilasSinTexto = FilasSinTexto, Caracteres = Caracteres };

    /// <summary>La misma decisión, con lo que viajó para tomarla (350).</summary>
    internal DecisionDeUnPaso ConLoQueViajo(int viajaron, int filasSinTexto, int caracteres) =>
        new DecisionDeUnPaso(Actuar, Puerta, Confianza, Porque)
            { Alternativas = Alternativas, Cumplido = Cumplido, Peligro = Peligro, QueNoCuadro = QueNoCuadro,
              Viajaron = viajaron, FilasSinTexto = filasSinTexto, Caracteres = caracteres };
}

/// <summary>
/// LA RESPUESTA DE JEV, LEÍDA ENTERA ANTES DE JUZGARLA (promesa 388, spec 046).
/// </summary>
/// <remarks>
/// HASTA EL 2026-09-22 la confianza y las probabilidades se leían sin rango: un <c>confidence</c> de 95 pasaba
/// el umbral de 0,70 y accionaba, y una clave que no viajó entraba en <c>Alternativas</c> como segunda mejor.
/// La rama del dueño (<c>3215466</c>) corregía a 0 lo que caía fuera de [0,1] — y 0 en una compuerta significa
/// «adelante». Aquí un número que no se entiende es una respuesta que no se entiende, y lo que no se entiende
/// NO ACCIONA: ni se convierte en 0, ni se satura, ni se toma «la más probable» de un empate.
///
/// SE REPORTAN TODAS LAS VIOLACIONES, no la primera: una respuesta que rompe dos reglas dice las dos. Es lo que
/// deja que cada regla tenga su propio sabotaje con su propia aserción (revisión 2 de la spec), y lo que hace
/// que el porqué describa lo que vino en vez de concluir «respuesta inválida».
///
/// LAS NOULS («cumplido», «peligro») NO SE JUZGAN AQUÍ a propósito: son de la 389 y se leen en <see cref="ElDecisor"/>.
/// </remarks>
internal sealed class RespuestaDeJev
{
    /// <summary>Por encima de 1 hasta aquí se lee como 1: 1,0000001 es redondeo del modelo, no un valor fuera de dominio.</summary>
    public const double Tolerancia = 1e-6;

    /// <summary>La suma de las probabilidades puede alejarse de 1 hasta aquí (jev-ultrafast <c>model.py:38</c>, que corre contra la API real).</summary>
    public const double ToleranciaDeLaSuma = 0.02;

    /// <summary>La clave que Jev eligió, tal como vino.</summary>
    public string Elegida { get; }

    /// <summary>
    /// La confianza CRUDA: 95 si vino 95, <c>Infinity</c> si vino 1e400, <c>NaN</c> si no era número. Un 95
    /// registrado como 0,00 se leería al calibrar como «Jev duda siempre». Con <see cref="EnForma"/> falso este
    /// número no acciona nada. Dentro de la tolerancia (≤ 1+1e-6) se lee como 1.
    /// </summary>
    public double Confianza { get; }

    /// <summary>Todas las claves con su probabilidad, de mayor a menor. VACÍA si la respuesta no cuadra.</summary>
    public IReadOnlyList<(string Puerta, double Probabilidad)> Alternativas { get; }

    /// <summary>Cada regla que falló, con su campo y su valor crudo. Vacía = en forma.</summary>
    public IReadOnlyList<string> Violaciones { get; }

    public bool EnForma => Violaciones.Count == 0;

    /// <summary>Las violaciones en una línea, separadas por « · ». Vacío si está en forma.</summary>
    public string QueNoCuadro => string.Join(" · ", Violaciones);

    private RespuestaDeJev(string elegida, double confianza, IReadOnlyList<(string, double)> alternativas, IReadOnlyList<string> violaciones)
    {
        Elegida = elegida;
        Confianza = confianza;
        Alternativas = alternativas;
        Violaciones = violaciones;
    }

    /// <summary>
    /// Juzga la respuesta de la pregunta «puerta» contra la lista que viajó, y devuelve lo leído con TODAS las
    /// reglas que no cuadran: cada probabilidad número, finita y en [0,1]; las claves exactamente las que
    /// viajaron; Σ = 1 ± <see cref="ToleranciaDeLaSuma"/>; la elegida es el máximo y sin empate; la confianza
    /// número, finita y en [0,1].
    /// </summary>
    /// <param name="puerta">El objeto <c>answers.puerta</c> de la respuesta (ya se comprobó que trae <c>choice</c> como texto).</param>
    /// <param name="queViaja">Las claves que se mandaron en <c>criteria</c>, por el mismo camino con que se construyeron (aprendizaje nº16).</param>
    public static RespuestaDeJev Validar(JsonElement puerta, IReadOnlyList<string> queViaja)
    {
        var ic = CultureInfo.InvariantCulture;
        var violaciones = new List<string>();
        string Crudo(double v) => v.ToString("R", ic);
        string Dos(double v) => v.ToString("0.00", ic);

        string elegida = puerta.TryGetProperty("choice", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() ?? "" : "";
        if (elegida.Length == 0) violaciones.Add("falta «choice»");

        // LA CONFIANZA: se conserva cruda aunque no cuadre. Solo se recorta lo que cae en la tolerancia.
        double confianza = double.NaN;
        if (!puerta.TryGetProperty("confidence", out var cf))
            violaciones.Add("falta «confidence»");
        else if (cf.ValueKind != JsonValueKind.Number)
            violaciones.Add($"confidence={cf.GetRawText()} no es número");
        else
        {
            confianza = cf.GetDouble();
            if (EnRango(confianza, out var leida)) confianza = leida;
            else violaciones.Add($"confidence={Crudo(confianza)} fuera de [0,1]");
        }

        // LAS PROBABILIDADES: cada una número, finita y en [0,1]; y las claves, exactamente las que viajaron.
        var vistas = new HashSet<string>(StringComparer.Ordinal);
        var leidas = new List<(string Puerta, double Probabilidad)>();
        bool hayObjeto = puerta.TryGetProperty("probabilities", out var probs) && probs.ValueKind == JsonValueKind.Object;
        if (!hayObjeto)
            violaciones.Add(puerta.TryGetProperty("probabilities", out var raw) ? $"probabilities={Recorta(raw.GetRawText())} no es un objeto" : "falta «probabilities»");
        else
        {
            foreach (var pr in probs.EnumerateObject())
            {
                vistas.Add(pr.Name);
                if (pr.Value.ValueKind != JsonValueKind.Number)
                {
                    violaciones.Add($"probabilities[«{pr.Name}»]={pr.Value.GetRawText()} no es número");
                    continue;
                }
                double v = pr.Value.GetDouble();
                if (!EnRango(v, out var l)) { violaciones.Add($"probabilities[«{pr.Name}»]={Crudo(v)} fuera de [0,1]"); continue; }
                leidas.Add((pr.Name, l));
            }
            foreach (var k in vistas)
                if (!Contiene(queViaja, k)) violaciones.Add($"sobra «{k}»");
            foreach (var k in queViaja)
                if (!vistas.Contains(k)) violaciones.Add($"falta «{k}»");
        }

        // LA SUMA Y EL MÁXIMO solo se juzgan cuando cada valor que vino se pudo leer: la suma de un NaN no dice
        // nada de la distribución, y ya se dijo arriba qué clave no era número.
        bool todasLeidas = hayObjeto && leidas.Count == vistas.Count && leidas.Count > 0;
        if (todasLeidas)
        {
            double suma = 0;
            foreach (var (_, p) in leidas) suma += p;
            if (Math.Abs(suma - 1) > ToleranciaDeLaSuma) violaciones.Add($"Σ={Dos(suma)}");

            if (elegida.Length > 0)
            {
                int iElegida = leidas.FindIndex(x => string.Equals(x.Puerta, elegida, StringComparison.Ordinal));
                if (iElegida < 0)
                    violaciones.Add($"choice «{elegida}» no tiene probabilidad");
                else
                {
                    double pElegida = leidas[iElegida].Probabilidad;
                    var (quien, max) = leidas[0];
                    foreach (var (k, p) in leidas) if (p > max) { max = p; quien = k; }
                    if (pElegida < max - Tolerancia)
                        violaciones.Add($"choice «{elegida}» {Dos(pElegida)} < {Dos(max)} («{quien}»)");
                    else
                        foreach (var (k, p) in leidas)
                            if (!string.Equals(k, elegida, StringComparison.Ordinal) && p >= pElegida - Tolerancia)
                            { violaciones.Add($"empate: «{elegida}» y «{k}» con {Dos(pElegida)}"); break; }
                }
            }
        }

        IReadOnlyList<(string, double)> alternativas = Array.Empty<(string, double)>();
        if (violaciones.Count == 0)
        {
            leidas.Sort((x, y) => y.Probabilidad.CompareTo(x.Probabilidad));
            alternativas = leidas;
        }
        return new RespuestaDeJev(elegida, confianza, alternativas, violaciones);
    }

    /// <summary>Finito y en [0, 1 + <see cref="Tolerancia"/>]; lo que pasa de 1 dentro de la tolerancia se devuelve como 1.</summary>
    private static bool EnRango(double v, out double leida)
    {
        leida = v;
        if (double.IsNaN(v) || double.IsInfinity(v) || v < 0 || v > 1 + Tolerancia) return false;
        if (v > 1) leida = 1;
        return true;
    }

    private static bool Contiene(IReadOnlyList<string> lista, string clave)
    {
        foreach (var x in lista) if (string.Equals(x, clave, StringComparison.Ordinal)) return true;
        return false;
    }

    private static string Recorta(string s) => s.Length > 60 ? s.Substring(0, 60) + "…" : s;
}

/// <summary>
/// QUIÉN ELIGE LA PUERTA. Promesas 275, 278, 279, 280 y 281 (spec 035).
/// </summary>
/// <remarks>
/// LO QUE ESTA PIEZA NO HACE: no habla, no ejecuta, no planea. Elige una puerta de las que hay en
/// pantalla, o dice que no se atreve. Ejecutar sigue siendo cosa de <c>map_take</c>, y hablar y
/// razonar siguen siendo de Luna — Jev no puede hacer ninguna de las dos (no genera texto ni llama
/// herramientas), así que esto no es un recorte de ambición: es la forma del modelo.
///
/// «NO SÉ» ES UNA RESPUESTA, Y LA IMPORTANTE. El aprendizaje nº17 de este repo dice que un juez que
/// no puede correr no dice «no sé», dice «culpable» — y que eso manda la investigación al sitio
/// equivocado. Aquí se aplica al revés: cuando TypeSafe no contesta, contesta tarde, contesta algo
/// que no está en la pantalla, o contesta dudando, esta pieza devuelve <c>Actuar=false</c> y el
/// paso vuelve a Luna. Nunca se toma la puerta «más probable» de un empate.
///
/// EL TRANSPORTE SE INYECTA, y no por elegancia: el contrato corre en CI sin red y sin clave, y una
/// prueba que llamara de verdad fallaría por no tener credencial diciendo «CONTRATO ROTO» — un
/// fallo del arnés disfrazado de núcleo roto.
/// </remarks>
public static class ElDecisor
{
    /// <summary>Desde cuánto «ya está cumplido» no se acciona. El mismo listón que la confianza (spec 036).</summary>
    public const double CumplidoMinimo = 0.70;

    /// <summary>Desde cuánto «es irreversible» no se acciona: ante lo irreversible se pide MENOS evidencia para parar.</summary>
    public const double PeligroMaximo = 0.50;

    /// <summary>La segunda mejor se intenta si su probabilidad llega aquí. Exigirle el umbral de confianza sería no probarla nunca.</summary>
    public const double SegundaMejorMinima = 0.25;

    /// <summary>
    /// Elige qué puerta accionar, o dice que no.
    /// </summary>
    /// <param name="quien">«luna», «jev» o «simulado». Lo que no se entiende se trata como «luna».</param>
    /// <param name="pantalla">Dónde estamos, como lo nombra el mapa.</param>
    /// <param name="objetivo">Lo que se quiere conseguir.</param>
    /// <param name="puertas">El inventario de la pantalla. Fuera de aquí no se puede elegir nada.</param>
    /// <param name="umbral">Mínimo de confianza exigido. Se actúa si se alcanza, no solo si se supera.</param>
    /// <param name="transporte">Manda el cuerpo y devuelve la respuesta. Solo se llama con «jev».</param>
    /// <remarks>
    /// ES EL ÚNICO MÉTODO CON ESTE NOMBRE, Y NO ES ESTILO: el contrato lo pide con <c>GetMethod("Elegir")</c> sin
    /// tipos en 6 sitios (275, 278–281, 289), y con una sobrecarga esa llamada lanza
    /// <c>AmbiguousMatchException</c> —medido el 2026-09-22—. Por eso la larga se llama
    /// <see cref="ElegirConModelo"/> y esta delega en ella con los valores por defecto (392).
    /// </remarks>
    public static DecisionDeUnPaso Elegir(
        string quien,
        string pantalla,
        string objetivo,
        IReadOnlyList<string> puertas,
        double umbral,
        Func<string, string> transporte) =>
        ElegirConModelo(quien, pantalla, objetivo, puertas, umbral, transporte,
            ConfiguracionDelDecisor.ModeloPorDefecto, PoliticaDeLoQueViaja.PorDefecto);

    /// <summary>
    /// Lo mismo que <see cref="Elegir"/>, con el modelo que se le pide a TypeSafe y la política de lo que viaja.
    /// </summary>
    /// <param name="modelo">El alias que va en <c>"model"</c> del cuerpo. Hasta el 2026-09-22 el interruptor leía
    /// <c>U_TYPESAFE_MODELO</c>, lo enseñaba en el botón, y el cuerpo llevaba el alias por defecto de todos modos:
    /// el estado nombraba un modelo y la petición pedía otro (392).</param>
    /// <param name="politica">Qué superficies pueden mandar texto a Jev, y qué parte de la ubicación viaja (393).
    /// <c>null</c> es la de por defecto —SAP no manda, vetados por defecto—: la ausencia no abre nada.</param>
    public static DecisionDeUnPaso ElegirConModelo(
        string quien,
        string pantalla,
        string objetivo,
        IReadOnlyList<string> puertas,
        double umbral,
        Func<string, string> transporte,
        string modelo,
        PoliticaDeLoQueViaja politica)
    {
        if (puertas == null || puertas.Count == 0)
            return DecisionDeUnPaso.No("no hay ninguna puerta accionable en esta pantalla: no hay nada que elegir.");

        switch ((quien ?? "").Trim().ToLowerInvariant())
        {
            case "jev":
                return ConJev(pantalla, objetivo, puertas, umbral, transporte,
                    string.IsNullOrWhiteSpace(modelo) ? ConfiguracionDelDecisor.ModeloPorDefecto : modelo.Trim(),
                    politica ?? PoliticaDeLoQueViaja.PorDefecto);

            case "simulado":
                return Simulado(objetivo, puertas, umbral);

            default:
                // NI SE LLAMA NI SE MIRA. Un decisor que preguntara y luego descartara la respuesta
                // ya habría gastado cupo, plazo y datos de la pantalla del hospital (promesa 275).
                return DecisionDeUnPaso.No("decide Luna: el decisor no se pronuncia.");
        }
    }

    private static DecisionDeUnPaso ConJev(
        string pantalla, string objetivo, IReadOnlyList<string> puertas, double umbral, Func<string, string> transporte,
        string modelo, PoliticaDeLoQueViaja politica)
    {
        // LA POLÍTICA ANTES DEL TRANSPORTE (393): lo que no puede viajar no se manda, y tampoco se decide por la regla
        // local —con «jev» eso sería cambiar de juez—. El transporte no se toca ni una vez; decide Luna, y el porqué
        // dice cuál de las dos reglas mordió (SAP sin habilitar, u origin vetado y bajo qué veto).
        if (!politica.PuedeViajar(pantalla, out string noViaja))
            return DecisionDeUnPaso.No(noViaja);

        if (transporte == null)
            return DecisionDeUnPaso.No("se pidió Jev pero no hay transporte con el que hablarle.");

        // LO QUE VIAJA EN EL CHOICE = las puertas ofrecidas + «ninguna» (391), CADA PUERTA CON EL ID QUE LA POLÍTICA
        // PERMITE (350): una fila va como «2) fila (GuiGridFila)», sin su texto, también con SAP habilitado. Es la ÚNICA
        // lista que viaja —en el choice y en el state—, y se construye aquí, no en CuerpoDeEleccion (la 282 exige que el
        // cuerpo lleve exactamente lo que se le da) ni en el state (que lista puertas de la pantalla, y «ninguna» no es
        // una). La 388 compara las claves de la respuesta contra ESTA lista, y la respuesta vuelve a la puerta OFRECIDA
        // por el mapa que se arma a la vez, por el mismo camino (aprendizaje nº16): la mano pulsa por el selector de la
        // ofrecida, no por lo que viajó. Hasta el 2026-09-22 las filas viajaban con su texto: la lista de pacientes.
        var queViaja = new List<string>(puertas.Count + 1);
        var ofrecidaDe = new Dictionary<string, string>(StringComparer.Ordinal);
        int filasSinTexto = 0;
        foreach (var p in puertas)
        {
            string v = PoliticaDeLoQueViaja.IdQueViaja(p);
            if (ofrecidaDe.TryGetValue(v, out string? otra))
            {
                // DOS PUERTAS DISTINTAS CON EL MISMO ID DE VIAJE —dos filas sin número: «fila (GuiGridFila)» las dos—: Jev
                // no podría decir cuál, y quedarse con una sería adivinar. No se pregunta. Con las puertas numeradas de
                // map_decidir no pasa; con una lista sin numerar, sí. La misma puerta repetida no es ambigua y sigue igual.
                if (!string.Equals(otra, p, StringComparison.Ordinal))
                    return DecisionDeUnPaso.No(
                        $"dos puertas distintas viajarían a Jev con el mismo id «{v}»: no podría decir cuál eligió, así que no se "
                      + "le pregunta y no decido por regla local. Decide Luna.");
            }
            else
            {
                ofrecidaDe[v] = p;
                if (!string.Equals(v, p, StringComparison.Ordinal)) filasSinTexto++;
            }
            queViaja.Add(v);
        }
        var puertasQueViajan = new List<string>(queViaja);
        queViaja.Add(PeticionASystemOne.IdNinguna);
        int viajaron = 0;
        foreach (var k in ofrecidaDe.Keys) if (!string.IsNullOrWhiteSpace(k)) viajaron++;

        string cuerpo = "";
        string respuesta;
        try
        {
            // DE LA UBICACIÓN VIAJA SOLO EL ORIGIN (393): hasta el 2026-09-22 viajaba entera, y en uia:// el pathname
            // es el título vivo de la ventana («/Historia clínica de …»).
            cuerpo = PeticionASystemOne.CuerpoDeEleccion(
                modelo,
                PeticionASystemOne.EstadoDeLaPantalla(PoliticaDeLoQueViaja.UbicacionQueViaja(pantalla), objetivo, puertasQueViajan),
                PeticionASystemOne.IdDeLaPuerta,
                PeticionASystemOne.InstruccionesDeLaPuerta(objetivo),
                queViaja);
            respuesta = transporte(cuerpo);
        }
        catch (Exception e)
        {
            // LA CADENA ENTERA, no solo el mensaje de fuera (patrón nº3): un try/catch mudo convierte
            // un bug de aridad en «la API no existe», y eso ya costó semanas en este repo.
            var porque = "TypeSafe no contestó: ";
            for (var x = e; x != null; x = x.InnerException)
                porque += $"{x.GetType().Name}: {x.Message}" + (x.InnerException != null ? " ← " : "");
            return Contada(DecisionDeUnPaso.No(porque + ". Decide Luna."));
        }
        return Contada(Juzgar(respuesta, puertas.Count, ofrecidaDe, queViaja, umbral));

        // LO QUE VIAJÓ VA EN CADA DECISIÓN QUE SALE DESPUÉS DE ENTREGAR EL CUERPO (350), también en las que no accionan:
        // la línea «decisor:» lo cuenta, y lo que no queda contado no se puede auditar.
        DecisionDeUnPaso Contada(DecisionDeUnPaso d) => d.ConLoQueViajo(viajaron, filasSinTexto, cuerpo.Length);
    }

    /// <summary>
    /// Lee la respuesta de Jev y aplica las compuertas, todas cerradas. Las claves de la respuesta son los ids que
    /// VIAJARON; <paramref name="ofrecidaDe"/> los devuelve a la puerta ofrecida, que es la que la mano sabe pulsar (350).
    /// </summary>
    private static DecisionDeUnPaso Juzgar(
        string respuesta, int ofrecidas, IReadOnlyDictionary<string, string> ofrecidaDe, IReadOnlyList<string> queViaja, double umbral)
    {
        if (string.IsNullOrWhiteSpace(respuesta))
            return DecisionDeUnPaso.No("TypeSafe contestó vacío. Decide Luna.");

        string elegida;
        double confianza;
        IReadOnlyList<(string Puerta, double Probabilidad)> alternativas;
        string queNoCuadro;
        double cumplido = 0, peligro = 0;
        try
        {
            using var doc = JsonDocument.Parse(respuesta);
            if (!doc.RootElement.TryGetProperty("answers", out var answers))
                return DecisionDeUnPaso.No("la respuesta de TypeSafe no trae «answers». Decide Luna.");
            if (!answers.TryGetProperty(PeticionASystemOne.IdDeLaPuerta, out var a))
                return DecisionDeUnPaso.No(
                    $"la respuesta no trae la pregunta «{PeticionASystemOne.IdDeLaPuerta}» que se hizo. Decide Luna.");
            if (!a.TryGetProperty("choice", out var c) || c.ValueKind != JsonValueKind.String)
                return DecisionDeUnPaso.No("la respuesta no trae una elección. Decide Luna.");

            // LA DISTRIBUCIÓN ENTERA, contra la lista que viajó (388): confianza y probabilidades con rango, las
            // claves exactas, la suma, y la elegida como máximo sin empate. Hasta el 2026-09-22 estas dos lecturas
            // no tenían rango y un confidence de 95 accionaba. Las alternativas (288) salen de aquí, ordenadas.
            var leida = RespuestaDeJev.Validar(a, queViaja);
            elegida = leida.Elegida;
            confianza = leida.Confianza;
            // LAS ALTERNATIVAS VUELVEN A LAS PUERTAS OFRECIDAS (350): la segunda mejor se busca por el selector de la
            // ofrecida (288), y «2) fila (GuiGridFila)» no está en esa lista. «Ninguna» no es una puerta y se queda como vino.
            var alt = new List<(string Puerta, double Probabilidad)>(leida.Alternativas.Count);
            foreach (var (k, p) in leida.Alternativas) alt.Add((ofrecidaDe.TryGetValue(k, out var o) ? o : k, p));
            alternativas = alt;
            queNoCuadro = leida.QueNoCuadro;
            // LAS DOS NOULS FALLAN CERRADAS (389, y la 289 desde el 2026-09-22). Hasta hoy una noul ausente o que no
            // era número valía 0, y 0 es justamente lo que ABRE la compuerta de peligro: una respuesta sin «peligro»
            // accionaba con más soltura que una que lo traía. El cuerpo SIEMPRE pide las dos (289), así que ausente no
            // es «transporte viejo»: es una respuesta malformada. Fuera de forma → el caso PEOR (peligro 1, cumplido 0)
            // y lo que vino, crudo, se suma a lo que no cuadra; por ahí no se acciona.
            var nouls = new List<string>(2);
            cumplido = NoulCerrada(answers, PeticionASystemOne.IdCumplido, peor: 0, nouls);
            peligro = NoulCerrada(answers, PeticionASystemOne.IdPeligro, peor: 1, nouls);
            if (nouls.Count > 0)
                queNoCuadro = queNoCuadro.Length > 0 ? queNoCuadro + " · " + string.Join(" · ", nouls) : string.Join(" · ", nouls);
        }
        catch (Exception e)
        {
            // NO SOLO JsonException, y se descubrió revisando: si «answers» llegara como texto en vez
            // de objeto, TryGetProperty lanza InvalidOperationException — que un catch de JsonException
            // dejaría escapar, tumbando el paso en vez de devolvérselo a Luna. Se atrapa ancho, pero
            // NO mudo (patrón nº3): el tipo y el mensaje van en el porqué, que es lo que distingue
            // «vino algo raro» de «no vino nada».
            return DecisionDeUnPaso.No(
                $"no se pudo leer la respuesta de TypeSafe ({e.GetType().Name}: {e.Message}). Decide Luna.");
        }

        // LO QUE NO CUADRA NO ACCIONA, y se dice TODO lo que no cuadró con su valor crudo (388). Ni se corrige
        // a 0, ni se satura, ni se ofrece una segunda mejor: la de una distribución inválida no es una segunda
        // mejor. La confianza va cruda para poder calibrar con ella; con Actuar=false no acciona nada.
        if (queNoCuadro.Length > 0)
            return DecisionDeUnPaso.No(
                $"la respuesta de Jev no cuadra y no se acciona: {queNoCuadro}. Decide Luna.", confianza)
                .Con(Array.Empty<(string, double)>(), cumplido, peligro, queNoCuadro);

        // LA COMPROBACIÓN QUE CIERRA EL PENDIENTE Nº2. Un choice solo puede devolver una de las
        // claves que se le dieron, pero eso lo promete el servidor y esto se ejecuta sobre SAP de un
        // hospital: lo que promete otro se comprueba. Comparación ORDINAL y por el mismo camino por
        // el que se construyó la lista (aprendizaje nº16: dos identidades de distinta forma dan
        // falso SIEMPRE, y en silencio): el mapa id que viajó → puerta ofrecida, armado con la lista (350).
        bool ofrecida = ofrecidaDe.TryGetValue(elegida, out string? puerta);

        // «NINGUNA» VIAJÓ Y JEV LA ELIGIÓ (391): no es una puerta, así que no se acciona; se dice con su
        // probabilidad para poder mirar, con cien pasos, si separa aciertos de pérdidas. No es compuerta
        // calibrada: se registra y se devuelve a Luna.
        if (!ofrecida && string.Equals(elegida, PeticionASystemOne.IdNinguna, StringComparison.Ordinal))
            return DecisionDeUnPaso.No(
                $"Jev eligió «ninguna» ({confianza.ToString("0.00", CultureInfo.InvariantCulture)}): no lo veo en esta pantalla "
              + "—nada de lo que hay avanza hacia el objetivo—. No se acciona. Decide Luna.", confianza).Con(alternativas, cumplido, peligro);

        if (!ofrecida)
            return DecisionDeUnPaso.No(
                $"Jev contestó «{elegida}», que no está entre las {ofrecidas} puertas de esta pantalla: "
              + "no se acciona. Decide Luna.", confianza).Con(alternativas, cumplido, peligro);

        // YA ESTÁ: si Jev dice que el objetivo ya se cumplió en esta pantalla, accionar es pasarse (289).
        if (cumplido >= CumplidoMinimo)
            return DecisionDeUnPaso.No(
                $"Jev dice que el objetivo ya está cumplido en esta pantalla ({cumplido.ToString("0.00", CultureInfo.InvariantCulture)}): "
              + "no se acciona nada más. Decide Luna.", confianza).Con(alternativas, cumplido, peligro);

        // LO IRREVERSIBLE NO SE ACCIONA POR UN DECISOR: se para con menos evidencia de la que se pide para actuar.
        if (peligro >= PeligroMaximo)
            return DecisionDeUnPaso.No(
                $"Jev dice que accionar «{elegida}» sería irreversible o peligroso ({peligro.ToString("0.00", CultureInfo.InvariantCulture)}): "
              + "no se acciona. Decide Luna.", confianza).Con(alternativas, cumplido, peligro);

        if (confianza < umbral)
            return DecisionDeUnPaso.No(
                $"Jev eligió «{elegida}» con confianza {confianza.ToString("0.00", CultureInfo.InvariantCulture)}, "
              + $"por debajo del mínimo exigido ({umbral.ToString("0.00", CultureInfo.InvariantCulture)}): "
              + "no se acciona a medias. Decide Luna.", confianza).Con(alternativas, cumplido, peligro);

        // LA PUERTA ES LA OFRECIDA, EL PORQUÉ NOMBRA LO QUE VIAJÓ (350): la mano necesita el id que lleva detrás el
        // selector; el porqué sale al log y a la cuenta, y una fila no se escribe por su texto.
        return DecisionDeUnPaso.Si(puerta!, confianza,
            $"Jev eligió «{elegida}» con confianza {confianza.ToString("0.00", CultureInfo.InvariantCulture)}.")
            .Con(alternativas, cumplido, peligro);
    }

    /// <summary>
    /// El valor de una noul de la respuesta, o <c>null</c> si no vino o no es número; <paramref name="crudo"/> lleva
    /// el texto JSON tal cual llegó («80», «"sí"», «1e400»), o vacío si no vino. Hasta el 2026-09-22 devolvía 0 en
    /// esos casos, y 0 abre la compuerta (promesa 389): quien la llama decide el caso peor, no esta función.
    /// </summary>
    private static double? Noul(JsonElement answers, string id, out string crudo)
    {
        crudo = "";
        if (!answers.TryGetProperty(id, out var n) || n.ValueKind != JsonValueKind.Object
            || !n.TryGetProperty("noul", out var v)) return null;
        crudo = v.GetRawText();
        // Utf8JsonReader lee «1e400» como Infinity sin lanzar (medido en la fase 2): es número, pero no finito.
        return v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
    }

    /// <summary>
    /// La noul en [0,1] si Jev la dijo bien; si falta, no es número, no es finita o se sale del rango, el caso
    /// <paramref name="peor"/> —y la regla que falló, con el crudo, entra en <paramref name="violaciones"/> para que
    /// no se accione y se diga cuál (389). Un 0 solo abre la compuerta cuando Jev lo dijo. ≤ 1 + 1e-6 se lee como 1,
    /// la misma tolerancia que las probabilidades (<see cref="RespuestaDeJev"/>).
    /// </summary>
    private static double NoulCerrada(JsonElement answers, string id, double peor, List<string> violaciones)
    {
        double? leida = Noul(answers, id, out string crudo);
        if (crudo.Length == 0) { violaciones.Add($"falta «{id}»"); return peor; }
        if (leida == null) { violaciones.Add($"{id}={crudo} no es número"); return peor; }
        double v = leida.Value;
        if (!double.IsFinite(v)) { violaciones.Add($"{id}={crudo} no es finito"); return peor; }
        if (v < 0 || v > 1 + 1e-6) { violaciones.Add($"{id}={crudo} fuera de [0,1]"); return peor; }
        return v > 1 ? 1 : v;
    }

    /// <summary>
    /// La regla fija: la puerta cuya etiqueta comparte más palabras con el objetivo; a igualdad, la
    /// primera en el orden de lectura de la pantalla. Su confianza es cuánto de la puerta explica el
    /// objetivo (palabras compartidas / palabras de la puerta), y pasa por el mismo umbral que Jev.
    /// </summary>
    /// <remarks>
    /// REPETIBLE A PROPÓSITO, sin azar y sin reloj. Una prueba que dependiera del azar no probaría
    /// nada, y este modo existe justamente para poder ejercitar toda la cadena —inventario, elección,
    /// validación, compuerta— en una máquina sin clave y sin red.
    ///
    /// CERO COINCIDENCIAS NO ES UNA ELECCIÓN. Medido en el nivel 4 del 2026-09-18 sobre el Explorador:
    /// «abrir la carpeta Windows» no casaba con ninguna de las 61 puertas listadas y esta regla
    /// accionó igual «Detalles» —la primera— con confianza 1,00. Era el juez optimista que la spec
    /// prohíbe, en el doble que existe para probar que no lo hay. Ahora con cero palabras en común
    /// no se actúa, y la confianza deja de ser un 1,00 fijo.
    ///
    /// NO PRETENDE IMITAR A JEV. Es un doble de andamiaje: sirve para ver pasar los datos, no para
    /// estimar qué haría el modelo. Un verde en simulado no dice nada sobre la calidad de Jev, y por
    /// eso lo dice en su propio <c>Porque</c>.
    /// </remarks>
    private static DecisionDeUnPaso Simulado(string objetivo, IReadOnlyList<string> puertas, double umbral)
    {
        var palabras = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var w in (objetivo ?? "").Split(_separadores, StringSplitOptions.RemoveEmptyEntries))
            if (w.Length > 2) palabras.Add(w);

        string mejor = "";
        int mejorPuntos = 0;
        double mejorConfianza = 0;
        foreach (var p in puertas)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            int puntos = 0, total = 0;
            // Desde la 287 las puertas llegan como «2) Detalles (RadioButton)»: se puntúa la ETIQUETA, no el
            // número ni el tipo, que no son palabras de ningún objetivo.
            foreach (var w in EtiquetaDe(p).Split(_separadores, StringSplitOptions.RemoveEmptyEntries))
            {
                if (w.Length <= 2) continue;
                total++;
                if (palabras.Contains(w)) puntos++;
            }
            // Más palabras compartidas gana; a igual número, la puerta mejor explicada; a igual todo, la primera.
            double confianza = total == 0 ? 0 : (double)puntos / total;
            if (puntos > mejorPuntos || (puntos == mejorPuntos && puntos > 0 && confianza > mejorConfianza))
            { mejorPuntos = puntos; mejorConfianza = confianza; mejor = p; }
        }

        if (mejorPuntos == 0)
            return DecisionDeUnPaso.No(
                $"decisión simulada (sin red, sin TypeSafe): ninguna de las {puertas.Count} puertas comparte una palabra con el objetivo, así que no se acciona. Decide Luna.");

        string porque = $"decisión simulada (sin red, sin TypeSafe): «{mejor}» comparte {mejorPuntos} palabra(s) con el objetivo, "
                      + $"confianza {mejorConfianza.ToString("0.00", CultureInfo.InvariantCulture)}.";
        if (mejorConfianza < umbral)
            return DecisionDeUnPaso.No(porque + $" Por debajo del mínimo exigido ({umbral.ToString("0.00", CultureInfo.InvariantCulture)}): no se acciona. Decide Luna.", mejorConfianza);

        return DecisionDeUnPaso.Si(mejor, mejorConfianza, porque);
    }

    /// <summary>De «2) Detalles (RadioButton)» a «Detalles». Una etiqueta a secas se devuelve tal cual.</summary>
    private static string EtiquetaDe(string id)
    {
        var m = System.Text.RegularExpressions.Regex.Match(id, @"^\d+\)\s*(.*?)\s*(\([^()]*\))?\s*$");
        return m.Success ? m.Groups[1].Value : id;
    }

    private static readonly char[] _separadores = { ' ', '\t', '\n', '\r', '.', ',', ':', ';', '(', ')', '«', '»', '/', '-', '_' };
}
