namespace U.WindowsClient.Teach;

/// <summary>Un paso de la demo, tal como se le ofrece al modelo para que lo interprete.</summary>
/// <param name="Orden">1, 2, 3… El orden importa: «nwp1» al principio es navegación; el mismo texto
/// en medio de un formulario, probablemente no.</param>
/// <param name="Campo">El selector EXACTO. Es la identidad, y es lo que permite casar la respuesta
/// sin que el modelo tenga que acertar ningún nombre.</param>
/// <param name="Valor">Lo que se tecleó ahí, o vacío si el paso fue un clic.</param>
/// <param name="Dicho">Lo que el humano estaba diciendo mientras. El contexto que hace que el
/// modelo pueda acertar donde una regla no puede.</param>
public sealed record PasoQueSePregunta(int Orden, string Campo, string Valor, string Dicho);

/// <summary>
/// LO QUE SE LE PREGUNTA AL VIDEO SOBRE UNA DEMOSTRACIÓN. Promesa 135 (spec 009, segunda tanda).
/// </summary>
/// <remarks>
/// EL GUARDARRAÍL DE LA IDENTIDAD, EN EL LADO DE LA PREGUNTA. La promesa 129 ya descarta lo que el
/// modelo nombre de más al LEER la respuesta; esta dice que la pregunta tampoco le ofrece campos
/// que la demo no tocó. Las dos, y no es redundancia: un guardarraíl que vive solo en la lectura
/// depende de que nadie cambie el orden de las capas, y este sistema va a crecer.
///
/// AQUÍ NO HAY PROMPT, y esa es la regla de la casa (`CLAUDE.md`: el cliente no contiene prompts ni
/// decisiones). Esto empaqueta HECHOS —qué se tocó, qué se tecleó, qué se dijo—; el criterio y las
/// palabras con las que se le pide viven en Graph, que es donde se pueden cambiar sin repartir un
/// .exe nuevo a un hospital.
///
/// POR QUÉ VIAJA EL VALOR TECLEADO. Sin él el modelo no puede juzgar lo único que le estamos
/// preguntando: «70» en un campo llamado Peso es un dato del paciente y «nwp1» en el campo de
/// comandos es cómo se llega. Es el mismo valor que ya viaja a Graph dentro del workflow desde
/// julio, así que no abre ninguna puerta nueva — pero conviene saberlo y no descubrirlo después.
///
/// PURO: no toca red ni disco. Así el contrato juzga la parte que puede equivocarse en silencio.
/// </remarks>
public static class LoQueSePregunta
{
    /// <summary>Los pasos de ESTA demo, en orden, con lo justo para poder interpretarlos.</summary>
    public static IReadOnlyList<PasoQueSePregunta> De(Navigation.SkillEnsenada skill)
    {
        var salida = new List<PasoQueSePregunta>();
        if (skill == null) return salida;

        int n = 0;
        foreach (var p in skill.Pasos)
        {
            n++;
            salida.Add(new PasoQueSePregunta(n, (p.Exit ?? "").Trim(),
                (p.Texto ?? "").Trim(), (p.Dicho ?? "").Trim()));
        }
        return salida;
    }

    /// <summary>
    /// ¿Hay que preguntarlo SIN el video? Promesa 136.
    /// </summary>
    /// <remarks>
    /// EL VIDEO MEJORA LA INTERPRETACIÓN; NO ES DE LO QUE DEPENDE. El 2026-09-03 la cuenta de Gemini
    /// se quedó sin saldo —«429: Your prepayment credits are depleted», cuatro veces— y con ella se
    /// cayó la interpretación entera. Mirándolo de cerca, el juicio que le pedimos no necesita ver
    /// la pantalla: distinguir «nwp1 es cómo se llega» de «70 es el peso de este paciente» se decide
    /// con los pasos y con lo que la persona iba narrando, que ya están en disco.
    ///
    /// Tres peldaños, y cada uno solo se pisa si falló el de arriba: el video, el texto, y la regla
    /// del narrado. Esta función es la que decide si se pisa el de en medio, y dice que NO en dos
    /// casos:
    ///
    ///   · SI EL VIDEO YA OPINÓ. Preguntarlo otra vez sería pagar dos veces por lo mismo y quedarse
    ///     con dos opiniones del mismo hecho — que es lo que este repo lleva una tanda entera
    ///     quitando de en medio (un solo ejecutor, un solo lector, un solo prompt).
    ///   · SI NO SE TECLEÓ NADA. Sin un solo valor no hay nada de lo que decidir si es dato o
    ///     navegación: la pregunta no tendría objeto y la llamada sería gasto puro.
    /// </remarks>
    public static bool SinPantalla(Navigation.SkillEnsenada skill, string loQueDijoElVideo)
    {
        if (skill == null) return false;
        if (!string.IsNullOrWhiteSpace(loQueDijoElVideo)) return false;
        return skill.Pasos.Any(p => (p.Texto ?? "").Trim().Length > 0);
    }
}
