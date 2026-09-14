namespace U.WindowsClient.Navigation;

/// <summary>
/// EL RESCATE CUANDO EL BATCH NO PUEDE: qué se le dice al cerebro consciente, y quién juzga si
/// llegó. Promesas 120 y 121 (spec 009).
/// </summary>
/// <remarks>
/// EL PUENTE CONSCIENTE IMPROVISA DESDE JULIO, y está anotado como pendiente desde entonces: cuando
/// un flujo se detiene, computer-use recibe «retoma y termina la tarea» y elige por su cuenta. Una
/// vez pulsó «Buscar pacientes» en vez de «Crear Triage Administrativo» y declaró éxito. No es que
/// el modelo sea malo: es que se le pide adivinar sin decirle a dónde iba.
///
/// Y EL BATCH YA LO SABE. El paso que falló lleva su `Llegada` —el objetivo— desde la spec 005, y la
/// compuerta ya sabe enumerar lo que está vivo aquí con su selector. Las dos cosas juntas convierten
/// la improvisación en una búsqueda acotada con criterio de éxito verificable.
///
/// LAS DOS MITADES SON DEL MISMO ASUNTO y por eso viven juntas: <see cref="Encargo"/> es lo que se
/// le DICE, y <see cref="Aterrizo"/> es quien lo JUZGA. Tenerlas separadas fue justo el problema:
/// había dos jueces y uno era el propio modelo diciendo «terminé».
///
/// PURO A PROPÓSITO: ni pantalla ni modelo. Aquí solo se redacta y se compara, que es la parte que
/// puede equivocarse en silencio.
/// </remarks>
public static class ElRescate
{
    /// <param name="Llego">Si de verdad se está en el objetivo.</param>
    /// <param name="Motivo">Vacío si llegó. Si no, nombra las DOS pantallas.</param>
    public readonly record struct Aterrizaje(bool Llego, string Motivo);

    /// <summary>
    /// Lo que se le entrega al cerebro consciente para que retome: a dónde había que llegar, dónde
    /// estamos, y qué se puede accionar aquí POR SU IDENTIDAD.
    /// </summary>
    /// <remarks>
    /// NO DICE «TERMINA LA TAREA», y es lo único que esta función promete no decir. Un encargo con
    /// esa frase devuelve al modelo a elegir destino, que es el bug entero.
    ///
    /// LO ACCIONABLE VA CON SELECTOR, no con coordenadas: «pulsa sap:wnd[0]/tbar[0]/btn[0]» en vez
    /// de «clic en (683, 242)». Es lo único que sobrevive a que la ventana cambie de tamaño, de
    /// sitio o de scroll — la regla de la casa desde el primer día.
    /// </remarks>
    public static string Encargo(string objetivo, string aqui,
        IReadOnlyList<(string Selector, string Etiqueta)> accionable)
    {
        string meta = (objetivo ?? "").Trim();
        string donde = (aqui ?? "").Trim();
        var partes = new List<string>();

        partes.Add(meta.Length > 0
            ? $"OBJETIVO: llegar a «{meta}». Ahí se sabrá que salió bien, y en ningún otro sitio."
            : "OBJETIVO: no se sabe a dónde había que llegar, así que NO improvises: cuenta qué ves y para.");

        if (donde.Length > 0)
            partes.Add($"AHORA ESTÁS EN: «{donde}».");

        var puertas = (accionable ?? Array.Empty<(string, string)>())
            .Where(a => a.Selector.Trim().Length > 0)
            .Take(20)
            .Select(a => a.Etiqueta.Trim().Length > 0
                ? $"«{a.Etiqueta.Trim()}» → {a.Selector.Trim()}"
                : a.Selector.Trim())
            .ToList();

        partes.Add(puertas.Count > 0
            ? "PUEDES ACCIONAR ESTO, por su identidad y sin coordenadas: " + string.Join(" · ", puertas)
            : "No hay nada accionable reconocido aquí: di qué ves antes de tocar nada.");

        return string.Join("\n", partes);
    }

    /// <summary>
    /// ¿Llegó el rescate a donde tenía que llegar? Lo juzga la ubicación, no el modelo.
    /// </summary>
    /// <remarks>
    /// «TERMINÉ» NO ES UN VEREDICTO. Está en la compuerta a main desde agosto y viene de un caso
    /// real: el puente declaró éxito habiendo pulsado el botón equivocado. Así que el aterrizaje se
    /// comprueba igual que un paso del batch — por consecuencia, mirando dónde se acabó.
    ///
    /// EL MOTIVO NOMBRA LAS DOS PANTALLAS. «No llegaste» a secas manda la investigación a ciegas
    /// (aprendizaje nº2): saber que se buscaba SAPLY000 y se acabó en SAPLN_WP_FRAMEWORK dice, de un
    /// golpe, que faltó abrir el triage del paciente.
    /// </remarks>
    public static Aterrizaje Aterrizo(string objetivo, string donde)
    {
        string meta = (objetivo ?? "").Trim();
        string aqui = (donde ?? "").Trim();

        if (meta.Length == 0)
            return new(false, "no había objetivo con el que comparar: sin él, llegar no se puede afirmar.");
        if (aqui.Length == 0)
            return new(false, $"no sé dónde acabé, así que no puedo decir que llegué a «{meta}».");
        // POR LA MISMA REGLA QUE EL BATCH (promesa 226): una pantalla cogida a medio cambiar es la
        // misma pantalla. Comparar con Equals dejó una comprobación en «17 de 19» el 2026-09-11.
        if (Superficies.MismaPantalla(meta, aqui))
            return new(true, "");

        return new(false, $"había que llegar a «{meta}» y acabé en «{aqui}».");
    }
}
