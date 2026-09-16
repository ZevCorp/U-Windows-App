namespace U.WindowsClient.Navigation;

/// <summary>
/// DE UNA SKILL ENSEÑADA A PASOS DEL BATCH, con los datos de ESTA corrida. Promesas 122, 123 y 126.
/// </summary>
/// <remarks>
/// UN SOLO EJECUTOR, y esa es toda la tesis de esta tanda. Hasta hoy conviven dos: el
/// <c>WorkflowPlayer</c> (julio) reproduce un plan guardado y exige lo exacto —esta ventana, este
/// elemento—, y la pantalla cambia; <c>map_batch</c> (agosto) exige el OBJETIVO contra lo que está
/// vivo ahora, y cuando no puede para honesto diciendo qué sí ve. Dos ejecutores del mismo hecho son
/// dos opiniones, y una acabará contradiciendo a la otra sin avisar.
///
/// Así que reproducir una skill no es «otra forma de correr»: es TRADUCIRLA a pasos del batch. La
/// salida es <see cref="RecorrerSegunElNucleo.Paso"/> literalmente — ni un formato intermedio, ni un
/// tercer player. La llegada de cada paso viaja con él, que es el objetivo que la compuerta exige
/// desde la promesa 103.
///
/// LOS VALORES DE LA DEMO NO SE REPRODUCEN JAMÁS (promesa 123). La demo del triage se hizo sobre un
/// paciente de prueba: su «70» de peso es un ejemplo, no una instrucción. Un hueco sin dato queda
/// VACÍO, y el paso se omite — que es lo mismo que hace el médico cuando no tiene ese dato.
///
/// Y SE PARA ANTES DE LO QUE NO SE DESHACE (promesa 126): si la demo terminó pulsando Grabar, la
/// reproducción llega hasta esa puerta y ahí se detiene. Grabar es del humano.
/// </remarks>
public static class InstanciarSkill
{
    /// <summary>
    /// Los pasos del batch para correr esta skill con estos datos. La cuenta la lleva quien la
    /// recorre; aquí solo se traduce.
    /// </summary>
    /// <param name="datos">Significado del hueco → valor de esta corrida. Lo que no venga, no se
    /// escribe.</param>
    public static List<RecorrerSegunElNucleo.Paso> Pasos(
        SkillEnsenada skill, IReadOnlyDictionary<string, string> datos)
    {
        var salida = new List<RecorrerSegunElNucleo.Paso>();
        if (skill == null) return salida;

        var porCampo = HuecosPorCampo(skill);

        foreach (var p in skill.Pasos)
        {
            // LA PUERTA PELIGROSA CORTA AQUÍ, y lo que va detrás no se traduce: si Grabar quedó a
            // mitad de la demo, todo lo posterior depende de haber grabado y reproducirlo a medias
            // sería peor que no reproducirlo.
            if (PuertasPeligrosas.EsPeligrosa(p.Dicho) || PuertasPeligrosas.EsPeligrosa(p.Exit))
                break;

            // ¿UNA TECLA? Se pliega sobre el paso anterior SI ese paso escribió algo, y se lleva su
            // llegada consigo (promesa 132). Escribir «nwp1» no navega: el Enter sí, así que la
            // pantalla a la que hay que llegar es la de la tecla, no la del tecleo.
            string tecla = TeclaDe(p.Exit);
            if (tecla.Length > 0)
            {
                int ultimo = salida.Count - 1;
                if (ultimo >= 0 && salida[ultimo].Texto.Length > 0 && salida[ultimo].Tecla.Length == 0)
                {
                    salida[ultimo] = salida[ultimo] with
                    {
                        Tecla = tecla,
                        Llegada = p.Llegada.Length > 0 ? p.Llegada : salida[ultimo].Llegada,
                    };
                    continue;
                }
                // Sola —un F3, un F8— es un paso propio SIN puerta: no hay elemento que buscar.
                salida.Add(new RecorrerSegunElNucleo.Paso("", "", p.Llegada, tecla));
                continue;
            }

            // ¿ES UN HUECO? Entonces el texto NO es el de la demo: es el dato de esta corrida, y si
            // no hay dato el paso se omite en vez de escribir el ejemplo.
            if (p.Exit.Length > 0 && porCampo.TryGetValue(Identidad(p.Exit), out var hueco))
            {
                string valor = ValorPara(hueco, datos);
                if (valor.Length == 0) continue;
                salida.Add(new RecorrerSegunElNucleo.Paso(p.Exit, valor, p.Llegada));
                continue;
            }

            // Un paso normal viaja TAL CUAL: pulsar es pulsar, y su llegada es el objetivo. Lo
            // tecleado que nadie narró es parte de la tarea —un código de transacción, un filtro— y
            // se reproduce: sin él la skill no arranca. La regla y su riesgo están en
            // SkillEnsenada.HuecosDe, que es quien decide qué fue dato y qué no.
            salida.Add(new RecorrerSegunElNucleo.Paso(p.Exit, p.Texto, p.Llegada));
        }

        // EL ÚLTIMO PASO EXIGE DONDE ACABÓ LA DEMO (promesa 140). Su llegada observada es, por
        // construcción, la del paso anterior —o nada—, y exigir eso es exigir no haberse movido: el
        // «comprobada en 77 ms» del 2026-09-03. Donde terminó la demo se sabe, y manda.
        if (salida.Count > 0 && !string.IsNullOrWhiteSpace(skill.DondeTermina))
            salida[^1] = salida[^1] with { Llegada = skill.DondeTermina.Trim() };

        // Y CADA PASO DICE QUE ES DE UNA TAREA (promesa 256): el ejecutor no deja que un paso así pulse
        // una fila de una lista. Las skills en disco de antes de la spec 030 traen la fila del paciente
        // de la demo como un paso más; esto es lo que las para ahí en vez de elegir por parecido.
        return salida.Select(p => p with { DeUnaTarea = true }).ToList();
    }

    /// <summary>
    /// La tecla que un paso enseñado representa, o vacío si es un elemento de la pantalla.
    /// </summary>
    /// <remarks>
    /// EL PREFIJO ES EL CONTRATO DEL GRABADOR: emite «key:enter», «key:f3». No se adivina por el
    /// nombre —«Enter» también es el rótulo de un botón en alguna pantalla— porque una comparación
    /// entre identidades de distinta forma da falso en silencio (aprendizaje nº16).
    /// </remarks>
    private static string TeclaDe(string exit)
    {
        string s = (exit ?? "").Trim();
        return s.StartsWith("key:", StringComparison.OrdinalIgnoreCase) ? s[4..].Trim() : "";
    }

    /// <summary>Qué datos de esta corrida no encontraron hueco. Es lo que la cuenta tiene que decir.</summary>
    public static List<string> Sobrantes(SkillEnsenada skill, IReadOnlyDictionary<string, string> datos)
    {
        var sobran = new List<string>();
        if (datos == null) return sobran;
        var significados = (skill?.Huecos ?? Array.Empty<Hueco>())
            .Select(h => Nombres.Aplanar(h.Significado))
            .Where(x => x.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var d in datos)
            if (!significados.Any(s => Casan(s, Nombres.Aplanar(d.Key))))
                sobran.Add(d.Key);
        return sobran;
    }

    /// <summary>
    /// Los huecos que quedan EN BLANCO en esta corrida, por su nombre (promesa 196). No se pregunta
    /// ni se inventa —decisión del dueño, 2026-09-10—, pero se dice: un campo vacío sin rastro se
    /// parece demasiado a un envío completo.
    /// </summary>
    public static List<string> SinDato(SkillEnsenada skill, IReadOnlyDictionary<string, string> datos)
    {
        var faltan = new List<string>();
        foreach (var h in skill?.Huecos ?? Array.Empty<Hueco>())
            if (h.Significado.Length > 0 && ValorPara(h, datos).Length == 0 && !faltan.Contains(h.Significado))
                faltan.Add(h.Significado);
        return faltan;
    }

    private static Dictionary<string, Hueco> HuecosPorCampo(SkillEnsenada skill)
    {
        var porCampo = new Dictionary<string, Hueco>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in skill.Huecos ?? Array.Empty<Hueco>())
            if (h.Campo.Length > 0) porCampo[Identidad(h.Campo)] = h;
        return porCampo;
    }

    /// <summary>
    /// El dato de esta corrida para un hueco, casado por SIGNIFICADO. Exacto primero y contenido
    /// después, igual que resuelve el batch sus puertas: una sola escalera en todo el sistema.
    /// </summary>
    private static string ValorPara(Hueco hueco, IReadOnlyDictionary<string, string> datos)
    {
        if (datos == null || datos.Count == 0) return "";
        string busco = Nombres.Aplanar(hueco.Significado);
        if (busco.Length == 0) return "";

        foreach (var d in datos)
            if (Nombres.Aplanar(d.Key) == busco) return (d.Value ?? "").Trim();
        foreach (var d in datos)
            if (Casan(busco, Nombres.Aplanar(d.Key))) return (d.Value ?? "").Trim();
        return "";
    }

    private static bool Casan(string significado, string clave) =>
        significado.Length > 0 && clave.Length > 0
        && (significado.Contains(clave, StringComparison.Ordinal)
            || clave.Contains(significado, StringComparison.Ordinal));

    /// <summary>
    /// La misma identidad escrita por los dos lados: el grafo guarda los campos de SAP con el
    /// prefijo <c>sap:</c> y el lector del formulario los devuelve sin él. Comparar sin normalizar
    /// daría falso SIEMPRE y en silencio (aprendizaje nº16).
    /// </summary>
    /// <remarks>
    /// PÚBLICA DESDE LA SPEC 016: el panel de aprendizajes empareja pasos con huecos y con eventos
    /// de la lección, y son las mismas dos formas de escribir la misma identidad. Una segunda copia
    /// de esta regla es exactamente la avería del aprendizaje nº16.
    /// </remarks>
    public static string Identidad(string selector)
    {
        string s = (selector ?? "").Trim();
        if (s.StartsWith("sap:", StringComparison.OrdinalIgnoreCase)) s = s[4..];
        return s;
    }
}
