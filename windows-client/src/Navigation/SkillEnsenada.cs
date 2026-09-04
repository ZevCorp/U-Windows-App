using System.IO;
using System.Text.Json;

namespace U.WindowsClient.Navigation;

/// <summary>Un paso tal como la DEMOSTRACIÓN lo vio: qué se tocó (o tecleó), a dónde llegó, y qué
/// estaba diciendo el humano mientras. Promesas 102 y 105 (spec 005).</summary>
public sealed record PasoEnsenado(string Exit, string Texto = "", string Llegada = "", string Dicho = "");

/// <summary>
/// UN HUECO: un sitio de la tarea donde la demo escribió un DATO, no una acción fija. Promesa 123.
/// </summary>
/// <remarks>
/// LA BOMBA SILENCIOSA que esto desactiva: tal como quedó la spec 005, el valor tecleado en la
/// demostración viajaba como un paso normal. La demo del triage se hizo sobre un paciente de prueba
/// con «70» de peso; reproducir esa skill sobre un paciente real escribiría 70 en su historia
/// clínica, y nadie lo notaría porque el paso diría que salió bien.
///
/// El <see cref="Ejemplo"/> se guarda SOLO para poder decir de qué se trata al confirmar («peso,
/// como el 70 de la demo»). Nunca se escribe: para eso está el dato de la corrida.
/// </remarks>
public sealed record Hueco(string Campo, string Significado, string Accion = "texto", string Ejemplo = "");

/// <summary>Lo que un catálogo anuncia de una skill: lo justo para pedirla sin abrirla.</summary>
public sealed record SkillAnunciada(string Nombre, string Description, string Archivo, bool Comprobada = false);

/// <summary>
/// UNA SKILL ENSEÑADA: el artefacto que sale de una demostración humana. Promesas 102 y 106.
/// </summary>
/// <remarks>
/// Es la copia deliberada del patrón de las skills de Claude (.claude/skills/*/SKILL.md): un nombre,
/// una description-DISPARADOR que dice cuándo usarla, y los pasos — pero cada campo anclado a algo
/// que la sesión de enseñanza ya produce (medido el 2026-09-01 por cinco lectores): los pasos con
/// su valor tecleado vienen del recorder, la llegada de cada paso es el destino que la arista ya
/// guarda, y lo dicho viene de la transcripción anclada por tiempo.
///
/// EL ESLABÓN QUE NO EXISTÍA: el grafo guarda aristas SUELTAS sin orden ni nombre, y el único
/// empaquetador anterior mandaba PlanStep al Graph remoto — un formato que map_batch no lee. Esta
/// clase empaqueta al formato que el batch YA consume (Exit/Texto/Llegada): ni tercer formato ni
/// tercer player.
///
/// EN ARCHIVO LOCAL a propósito (%LOCALAPPDATA%\U\skills\): Neo4j caído no puede significar skills
/// perdidas — la durabilidad del saber con nombre no se fía de un proceso vecino.
///
/// LO QUE ESTA CLASE NO HACE, por doctrina del terreno (GENESIS): sembrar el grafo. La demo produce
/// LA SKILL; las aristas las gana la primera reproducción, ejecutando.
/// </remarks>
public sealed record SkillEnsenada(
    string Nombre, string Description, string DondeEmpieza, IReadOnlyList<PasoEnsenado> Pasos)
{
    /// <summary>
    /// LOS HUECOS de esta skill: los sitios donde la demo puso un dato (promesa 123).
    /// </summary>
    /// <remarks>
    /// VA COMO PROPIEDAD `init` Y NO EN EL CONSTRUCTOR, y es una decisión de compatibilidad medida:
    /// las promesas 102 y 106 construyen esta skill por REFLEXIÓN con cuatro argumentos. Añadir un
    /// quinto parámetro las rompería sin que ninguna de las dos hable de huecos.
    /// </remarks>
    public IReadOnlyList<Hueco> Huecos { get; init; } = Array.Empty<Hueco>();

    /// <summary>
    /// ¿Alguien repasó esta skill recorriéndola? Promesa 127 (decisión del dueño, 2026-09-03).
    /// </summary>
    public bool Comprobada { get; init; }

    /// <summary>
    /// Lo que el modelo propuso que significa un elemento. Se cuelga al COMPROBAR, no antes.
    /// </summary>
    /// <remarks>
    /// SUGERENCIA, no recuerdo, y la palabra importa: el selector lo pone el paso que TOCÓ ese
    /// elemento (promesa 125) y el significado lo propone el modelo. Colgarlo del grafo sin haber
    /// recorrido la tarea sería sembrar el terreno con lo que alguien dedujo mirando un video.
    /// </remarks>
    public sealed record Sugerencia(string Campo, string Significado);

    /// <summary>Lo que el modelo entendió del video sobre cada elemento, a la espera del repaso.</summary>
    public IReadOnlyList<Sugerencia> Sugerencias { get; init; } = Array.Empty<Sugerencia>();

    /// <summary>
    /// Dónde ACABÓ la demostración. Promesa 140. Es la llegada del último paso.
    /// </summary>
    /// <remarks>
    /// 2026-09-03 20:02:52: «hice los 1 paso(s): quedaste en «…SESSION_MANAGER…»» — comprobada en
    /// 77 ms. La llegada del paso N es la superficie del paso N+1, y el Enter era el ÚLTIMO paso:
    /// sin siguiente, sin llegada; al plegarse heredó la pantalla de la que SALÍA, y «llegar» fue no
    /// moverse. Esto es lo que se observa al parar la grabación, y es lo que el último paso exige.
    /// </remarks>
    public string DondeTermina { get; init; } = "";

    /// <summary>La misma skill, con la comprobación hecha. No cambia nada más.</summary>
    public SkillEnsenada ConLaComprobacionHecha() => this with { Comprobada = true };

    /// <summary>
    /// La misma skill, con el criterio del modelo aplicado. Promesa 134.
    /// </summary>
    /// <remarks>
    /// EL MODELO MANDA DONDE OPINÓ; DONDE NO, MANDA LA REGLA. Y el reparto es así porque cada mitad
    /// falla de una forma distinta:
    ///
    ///   · LA REGLA («lo que narras es un dato») falló en su primera corrida real, 2026-09-03:
    ///     marcó «nwp1» —el código de transacción— como dato del paciente, porque el dueño estaba
    ///     hablando todo el rato. Claro que hablaba: estaba enseñando. La skill perdió justo el
    ///     paso que la hace arrancar y el batch reportó «hice 0 de 4».
    ///   · EL MODELO vive al otro lado de una red que se cae, y puede no llegar a mirar un campo.
    ///     Descartar en bloque los huecos de la regla porque el modelo contestó ALGO convertiría
    ///     cada silencio suyo en un dato de paciente reproducido tal cual — el «70» del paciente
    ///     de prueba, que es la catástrofe callada que la promesa 123 existe para impedir.
    ///
    /// Por eso hace falta saber sobre QUÉ opinó (<see cref="LoInterpretado.Juzgados"/>) y no solo
    /// qué dijo: un campo del que no dijo nada conserva el veredicto de la regla, que es el
    /// conservador. Sin interpretación ninguna, esto devuelve la skill tal cual.
    /// </remarks>
    public SkillEnsenada ConLoInterpretado(LoInterpretado lo)
    {
        if (lo == null || !lo.Hubo) return this;

        var opinados = new HashSet<string>(lo.Juzgados ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);

        // Los del modelo primero; después, los de la regla sobre campos que el modelo no miró.
        var huecos = new List<Hueco>(lo.Huecos);
        var yaEsta = new HashSet<string>(huecos.Select(h => h.Campo), StringComparer.OrdinalIgnoreCase);
        foreach (var h in Huecos)
            if (!opinados.Contains(h.Campo) && yaEsta.Add(h.Campo)) huecos.Add(h);

        return this with
        {
            Huecos = huecos,
            Sugerencias = lo.Recuerdos
                .Select(r => new Sugerencia(r.Selector, r.Significado))
                .ToList(),
        };
    }

    /// <param name="Puede">Si se puede ejecutar ya.</param>
    /// <param name="Motivo">Vacío si puede. Si no, QUÉ FALTA — no basta con negarse.</param>
    public readonly record struct Veredicto(bool Puede, string Motivo);

    /// <summary>
    /// ¿Se puede ejecutar esta skill? Promesa 127.
    /// </summary>
    /// <remarks>
    /// COMPROBAR ES OBLIGATORIO, decidido por el dueño el 2026-09-03. Una skill recién enseñada es
    /// una hipótesis: los pasos son lo que la demo VIO, y hasta que no se recorren no se sabe si se
    /// pueden volver a andar. Ejecutar una sin repasar es justo la apuesta que este proyecto lleva
    /// dos meses aprendiendo a no hacer.
    ///
    /// Y EL «NO» DICE QUÉ FALTA. Una compuerta muda se aprende a saltar — es la lección del portero:
    /// un paso obligatorio que nadie entiende no protege nada.
    /// </remarks>
    public Veredicto PuedeCorrer() => Comprobada
        ? new(true, "")
        : new(false, $"«{Nombre}» todavía no se ha comprobado: pulsa «Comprobar aprendizaje» y la "
                   + "recorro una vez contigo mirando. Hasta entonces no la ejecuto.");

    /// <summary>La carpeta por defecto donde viven las skills de esta máquina.</summary>
    public static string CarpetaPorDefecto => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "U", "skills");

    /// <summary>
    /// De la sesión a la skill. Devuelve null si no hay nada que empaquetar: una skill sin pasos
    /// no enseña nada, y empaquetar el vacío sería anunciarle al cerebro una mentira con nombre.
    /// </summary>
    /// <summary>
    /// Como el de cuatro, pero exigiendo saber dónde ACABÓ la demo (promesa 140). Sin eso devuelve
    /// null: una skill sin destino es una que certifica no haberse movido, y es mejor no tenerla
    /// que tenerla mintiendo. Es la puerta por la que entra la sesión de enseñanza real.
    /// </summary>
    public static SkillEnsenada? Empaquetar(
        string nombre, string description, string dondeEmpieza, IReadOnlyList<PasoEnsenado> observados,
        string dondeTermina)
    {
        if (string.IsNullOrWhiteSpace(dondeTermina)) return null;
        var s = Empaquetar(nombre, description, dondeEmpieza, observados);
        return s == null ? null : s with { DondeTermina = dondeTermina.Trim() };
    }

    public static SkillEnsenada? Empaquetar(
        string nombre, string description, string dondeEmpieza, IReadOnlyList<PasoEnsenado> observados)
    {
        if (string.IsNullOrWhiteSpace(nombre) || string.IsNullOrWhiteSpace(dondeEmpieza)) return null;
        // Un paso vale si toca algo o escribe algo; el resto es ruido de sesión.
        var pasos = observados.Where(p => p.Exit.Length > 0 || p.Texto.Length > 0).ToList();
        if (pasos.Count == 0) return null;
        return new(nombre.Trim(), (description ?? "").Trim(), dondeEmpieza.Trim(), pasos)
        {
            Huecos = HuecosDe(pasos),
        };
    }

    /// <summary>
    /// QUÉ DE LO TECLEADO ES UN DATO Y QUÉ ES PARTE DE LA TAREA. Promesa 123.
    /// </summary>
    /// <remarks>
    /// LA REGLA, y es la decisión de diseño que más pesa de esta tanda: **lo que narraste es un
    /// dato; lo que no, es parte de la tarea.** Si el humano teclea 70 y dice «aquí va el peso»,
    /// está declarando un hueco. Si teclea «nwp1» y no dice nada, está navegando — y ese valor SÍ
    /// se reproduce, porque sin él la tarea no arranca.
    ///
    /// POR QUÉ NO AL CONTRARIO. La alternativa era tratar TODO lo tecleado como hueco, que es más
    /// seguro con los datos pero rompe la navegación: el código de transacción quedaría vacío y el
    /// batch pararía en el primer paso de cada skill. Se eligió una regla que el humano puede
    /// aprender en una frase, en vez de una salvaguarda que hace inútil la herramienta.
    ///
    /// EL RIESGO QUE QUEDA, dicho y no escondido: si alguien teclea un dato del paciente SIN
    /// narrarlo, se reproducirá. Por eso comprobar el aprendizaje es OBLIGATORIO (promesa 127) y
    /// enumera todo lo que quedó como valor fijo: el riesgo se ve en vez de vivir callado.
    /// </remarks>
    private static IReadOnlyList<Hueco> HuecosDe(IReadOnlyList<PasoEnsenado> pasos)
    {
        var huecos = new List<Hueco>();
        var yaEsta = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in pasos)
        {
            if (p.Exit.Length == 0 || p.Texto.Length == 0) continue;
            string dicho = (p.Dicho ?? "").Trim();
            if (dicho.Length == 0) continue;          // nadie lo llamó dato: es parte de la tarea
            if (!yaEsta.Add(p.Exit)) continue;        // un campo, un hueco
            huecos.Add(new Hueco(p.Exit, dicho, "texto", p.Texto));
        }
        return huecos;
    }

    /// <summary>Guarda la skill como UN archivo json en la carpeta. Devuelve la ruta.</summary>
    public string Guardar(string carpeta)
    {
        Directory.CreateDirectory(carpeta);
        // El nombre del archivo ES el nombre de la skill, saneado: así el catálogo puede listar
        // sin abrir, y dos guardados del mismo nombre son LA MISMA skill (la nueva pisa a la vieja,
        // que es lo que uno espera de re-enseñar algo).
        string archivo = Path.Combine(carpeta,
            string.Concat(Nombre.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-')) + ".skill.json");
        File.WriteAllText(archivo, JsonSerializer.Serialize(this,
            new JsonSerializerOptions { WriteIndented = true }));
        return archivo;
    }

    /// <summary>Carga una skill desde su archivo, o null si no se deja leer (y el porqué al log lo
    /// pone quien llama, que sabe para qué la quería).</summary>
    public static SkillEnsenada? Cargar(string archivo)
    {
        try { return JsonSerializer.Deserialize<SkillEnsenada>(File.ReadAllText(archivo)); }
        catch { return null; }
    }

    /// <summary>
    /// Lo que hay para anunciar (promesa 106): nombre y CUÁNDO usarla, por skill guardada. Con cero
    /// skills, cero anuncios — anunciar lo que no hay es inventar.
    /// </summary>
    public static IReadOnlyList<SkillAnunciada> Catalogo(string carpeta)
    {
        var lista = new List<SkillAnunciada>();
        if (!Directory.Exists(carpeta)) return lista;
        foreach (var f in Directory.EnumerateFiles(carpeta, "*.skill.json").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var s = Cargar(f);
            // Un archivo que no se deja leer NO entra al catálogo: anunciar una skill que luego no
            // se puede cargar es mandar al cerebro a una puerta pintada.
            if (s != null) lista.Add(new(s.Nombre, s.Description, f, s.Comprobada));
        }
        return lista;
    }
}
