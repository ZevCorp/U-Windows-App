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

    /// <summary>Cuánto dice Jev que el objetivo YA está cumplido en esta pantalla (0-1). 0 si no se preguntó.</summary>
    public double Cumplido { get; init; }

    /// <summary>Cuánto dice Jev que accionar la elegida es irreversible o peligroso (0-1). 0 si no se preguntó.</summary>
    public double Peligro { get; init; }

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

    internal DecisionDeUnPaso Con(IReadOnlyList<(string, double)> alternativas, double cumplido, double peligro) =>
        new DecisionDeUnPaso(Actuar, Puerta, Confianza, Porque) { Alternativas = alternativas, Cumplido = cumplido, Peligro = peligro };
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
    public static DecisionDeUnPaso Elegir(
        string quien,
        string pantalla,
        string objetivo,
        IReadOnlyList<string> puertas,
        double umbral,
        Func<string, string> transporte)
    {
        if (puertas == null || puertas.Count == 0)
            return DecisionDeUnPaso.No("no hay ninguna puerta accionable en esta pantalla: no hay nada que elegir.");

        switch ((quien ?? "").Trim().ToLowerInvariant())
        {
            case "jev":
                return ConJev(pantalla, objetivo, puertas, umbral, transporte);

            case "simulado":
                return Simulado(objetivo, puertas, umbral);

            default:
                // NI SE LLAMA NI SE MIRA. Un decisor que preguntara y luego descartara la respuesta
                // ya habría gastado cupo, plazo y datos de la pantalla del hospital (promesa 275).
                return DecisionDeUnPaso.No("decide Luna: el decisor no se pronuncia.");
        }
    }

    private static DecisionDeUnPaso ConJev(
        string pantalla, string objetivo, IReadOnlyList<string> puertas, double umbral, Func<string, string> transporte)
    {
        if (transporte == null)
            return DecisionDeUnPaso.No("se pidió Jev pero no hay transporte con el que hablarle.");

        string respuesta;
        try
        {
            string cuerpo = PeticionASystemOne.CuerpoDeEleccion(
                ConfiguracionDelDecisor.ModeloPorDefecto,
                PeticionASystemOne.EstadoDeLaPantalla(pantalla, objetivo, puertas),
                PeticionASystemOne.IdDeLaPuerta,
                PeticionASystemOne.InstruccionesDeLaPuerta(objetivo),
                puertas);
            respuesta = transporte(cuerpo);
        }
        catch (Exception e)
        {
            // LA CADENA ENTERA, no solo el mensaje de fuera (patrón nº3): un try/catch mudo convierte
            // un bug de aridad en «la API no existe», y eso ya costó semanas en este repo.
            var porque = "TypeSafe no contestó: ";
            for (var x = e; x != null; x = x.InnerException)
                porque += $"{x.GetType().Name}: {x.Message}" + (x.InnerException != null ? " ← " : "");
            return DecisionDeUnPaso.No(porque + ". Decide Luna.");
        }

        if (string.IsNullOrWhiteSpace(respuesta))
            return DecisionDeUnPaso.No("TypeSafe contestó vacío. Decide Luna.");

        string elegida;
        double confianza;
        var alternativas = new List<(string Puerta, double Probabilidad)>();
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

            elegida = c.GetString() ?? "";
            confianza = a.TryGetProperty("confidence", out var cf) && cf.ValueKind == JsonValueKind.Number
                ? cf.GetDouble()
                : 0;
            // LAS PROBABILIDADES DE TODAS, ordenadas: la segunda mejor viene en la misma respuesta (288).
            if (a.TryGetProperty("probabilities", out var probs) && probs.ValueKind == JsonValueKind.Object)
                foreach (var pr in probs.EnumerateObject())
                    if (pr.Value.ValueKind == JsonValueKind.Number) alternativas.Add((pr.Name, pr.Value.GetDouble()));
            alternativas.Sort((x, y) => y.Probabilidad.CompareTo(x.Probabilidad));
            // LAS DOS NOULS, si vinieron (289). Un transporte viejo que no las trae sigue valiendo: 0 y 0.
            cumplido = Noul(answers, PeticionASystemOne.IdCumplido);
            peligro = Noul(answers, PeticionASystemOne.IdPeligro);
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

        // LA COMPROBACIÓN QUE CIERRA EL PENDIENTE Nº2. Un choice solo puede devolver una de las
        // claves que se le dieron, pero eso lo promete el servidor y esto se ejecuta sobre SAP de un
        // hospital: lo que promete otro se comprueba. Comparación ORDINAL y por el mismo camino por
        // el que se construyó la lista (aprendizaje nº16: dos identidades de distinta forma dan
        // falso SIEMPRE, y en silencio).
        bool ofrecida = false;
        foreach (var p in puertas)
            if (string.Equals(p, elegida, StringComparison.Ordinal)) { ofrecida = true; break; }

        if (!ofrecida)
            return DecisionDeUnPaso.No(
                $"Jev contestó «{elegida}», que no está entre las {puertas.Count} puertas de esta pantalla: "
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

        return DecisionDeUnPaso.Si(elegida, confianza,
            $"Jev eligió «{elegida}» con confianza {confianza.ToString("0.00", CultureInfo.InvariantCulture)}.")
            .Con(alternativas, cumplido, peligro);
    }

    /// <summary>El valor de una noul de la respuesta, o 0 si no vino o no es número.</summary>
    private static double Noul(JsonElement answers, string id) =>
        answers.TryGetProperty(id, out var n) && n.ValueKind == JsonValueKind.Object
        && n.TryGetProperty("noul", out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDouble() : 0;

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
