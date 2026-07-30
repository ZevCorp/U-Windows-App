using U.Graph.Surfaces;

namespace U.Graph.NoteExport;

/// <summary>Qué política se aplica cuando el paciente no se puede verificar por máquina.</summary>
public enum PatientPolicy
{
    /// <summary>
    /// DEFAULT. Una persona confirma, viendo a la vez lo que SAP tiene abierto y de qué consulta es
    /// el trabajo. Sin confirmación no se escribe: el resultado es <c>needs_doctor</c>.
    /// </summary>
    OperatorConfirms,

    /// <summary>
    /// Se escribe sin verificar el paciente. SOLO para entornos de prueba (QAS) con datos ficticios.
    /// Cada trabajo deja constancia a gritos en el registro; no es un modo que deba llegar a un
    /// sistema con pacientes reales, y el ejecutor lo repite en su estado para que se vea.
    /// </summary>
    Off,
}

/// <summary>Qué dice la pantalla sobre a quién pertenece. <see cref="Sensitive"/> = no va al registro.</summary>
public sealed record PatientClue(string FieldId, string Label, string Value, bool Sensitive);

/// <summary>El resultado de mirar. NO afirma que el paciente sea correcto — describe lo que hay.</summary>
public sealed record PatientInspection(
    IReadOnlyList<PatientClue> Clues,
    string Screen,
    string AutoVerdict)
{
    public bool FoundAnything => Clues.Count > 0;

    /// <summary>Lo que SÍ puede ir al registro: los ids de los campos hallados, nunca sus valores.</summary>
    public string LogSummary => Clues.Count == 0
        ? "ninguna pista de identidad en la pantalla"
        : string.Join(", ", Clues.Select(c => c.FieldId));
}

/// <summary>
/// La compuerta del paciente: impedir que una nota se escriba en la historia de otra persona.
///
/// EL HECHO INCÓMODO, verificado en los tres repos: hoy es IMPOSIBLE verificar esto por máquina.
/// El payload de la cola identifica al paciente con <c>patient_ref</c>, que es el uuid de Miracle
/// (<c>consultations.patient_id</c>); SAP no lo conoce ni puede conocerlo. Y en el esquema de
/// Miracle Notes no existe ningún identificador institucional —ni número de historia, ni MRN—:
/// solo <c>patients.documento</c>, que es texto libre, opcional y no único, y que además NO viaja
/// en el payload (Graph tiene un test que falla si aparece «documento» o «nombre» dentro).
///
/// La omisión es deliberada y correcta: es minimización de PHI. Pero descansa sobre una suposición
/// que el código de Graph deja escrita —«el ejecutor ya está dentro del contexto del paciente en el
/// HIS»— y que NADIE comprueba. Con una cola asíncrona esa suposición se rompe sola: un trabajo
/// encolado hace veinte minutos se ejecuta cuando SAP puede tener abierto otro paciente.
///
/// Qué se hace en vez de inventar una comprobación que no lo es:
///
///   1. Se LEE lo que la pantalla dice de la identidad del paciente y se presenta, sin concluir.
///   2. Si algún día el payload trae un identificador del HIS, esto lo verifica solo y, si NO
///      coincide, frena en seco pase lo que pase (ver <see cref="Contradicted"/>). Ese camino está
///      escrito y probado hoy aunque el dato todavía no llegue.
///   3. Mientras no llegue, quien verifica es una persona (<see cref="PatientPolicy"/>).
///
/// El cambio mínimo que convertiría esto en automático está en el informe de la entrega: que el
/// payload lleve el identificador del paciente en el HIS. No se puede resolver desde este cliente.
/// </summary>
public sealed class PatientGuard
{
    /// <summary>
    /// Nombres técnicos de IS-H cuyo valor identifica (o ayuda a identificar) al paciente en pantalla.
    /// La clave es el sufijo del id de SAP tras el guion: <c>RNPA1-PASSNR</c> → <c>PASSNR</c>.
    ///
    /// Es una lista de PARTIDA, no una verdad: cada instalación de IS-H tiene sus dynpros. Se puede
    /// ampliar sin recompilar (<see cref="GraphConfig.PatientFieldNames"/>). Que la lista no acierte
    /// no produce un falso «verificado»: produce «no encontré nada», que es un desenlace honesto.
    /// </summary>
    private static readonly Dictionary<string, (string Label, bool Sensitive)> KnownFields =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["PATNR"] = ("Nº de paciente", false),   // identificador interno de IS-H
            ["FALNR"] = ("Nº de caso", false),       // episodio/caso
            ["EINRI"] = ("Institución", false),
            ["PASSNR"] = ("Documento", true),
            ["GBDAT"] = ("Fecha de nacimiento", true),
            ["NNAME"] = ("Apellido", true),
            ["VNAME"] = ("Nombre", true),
            ["NAME1"] = ("Nombre", true),
        };

    private readonly IReadOnlyDictionary<string, (string Label, bool Sensitive)> _fields;

    public PatientGuard(IEnumerable<string>? extraFieldNames = null)
    {
        var merged = new Dictionary<string, (string, bool)>(KnownFields, StringComparer.OrdinalIgnoreCase);
        foreach (string name in extraFieldNames ?? Array.Empty<string>())
        {
            string key = (name ?? "").Trim();
            // Lo añadido a mano se marca SENSIBLE: no sabemos qué contiene, y un dato del paciente
            // filtrado al registro no se puede recoger. Prudencia por defecto.
            if (key.Length > 0 && !merged.ContainsKey(key)) merged[key] = (key, true);
        }
        _fields = merged;
    }

    /// <summary>
    /// El identificador del paciente EN EL HIS que trae el trabajo, o "" si no trae ninguno.
    ///
    /// Hoy devuelve siempre "": el contrato no lo transporta (ver el resumen de la clase). Está
    /// aislado en un método propio a propósito — es el ÚNICO punto que hay que tocar el día que
    /// Graph lo incluya, y tenerlo aquí hace que el resto de la compuerta ya esté escrita y probada
    /// para ese día. <c>patient_ref</c> NO sirve: es un uuid de Miracle, no un identificador del HIS,
    /// y compararlo con cualquier campo de SAP daría siempre «no coincide».
    /// </summary>
    public static string HisPatientIdFrom(ExportPayload? payload) => "";

    /// <summary>
    /// Mira la pantalla y recoge lo que diga de la identidad del paciente. No decide nada.
    /// Bloquea (COM): llamar desde un worker.
    /// </summary>
    public PatientInspection Inspect(SapGuiSurface sap, ExportPayload? payload)
    {
        string screen = "";
        try { screen = sap.Identity().Url; } catch { }

        var clues = new List<PatientClue>();
        try
        {
            foreach (DetectedField field in sap.ReadFields())
            {
                string value = (field.CurrentValue ?? "").Trim();
                if (value.Length == 0) continue;

                string technical = TechnicalName(field.Selector);
                if (technical.Length == 0) continue;
                if (!_fields.TryGetValue(technical, out var known)) continue;

                clues.Add(new PatientClue(technical, known.Label, value, known.Sensitive));
            }
        }
        catch { /* la pantalla puede cambiar bajo los pies; sin pistas es un desenlace válido */ }

        string expected = HisPatientIdFrom(payload);
        string auto = expected.Length == 0
            ? "" // no hay con qué comparar: lo normal hoy
            : clues.Any(c => Matches(c.Value, expected)) ? Verified
            : clues.Any(c => c.FieldId is "PATNR" or "FALNR") ? Contradicted
            : "";

        return new PatientInspection(clues, screen, auto);
    }

    /// <summary>El paciente de la pantalla ES el del trabajo, comprobado por máquina.</summary>
    public const string Verified = "verificado";

    /// <summary>
    /// La pantalla dice que es OTRO paciente. Es el freno duro: ninguna política lo salta, ninguna
    /// confirmación humana lo levanta. Si el sistema puede afirmar que el paciente no coincide,
    /// escribir sería el peor fallo posible de todo el producto.
    /// </summary>
    public const string Contradicted = "contradicho";

    /// <summary>
    /// Del <c>sap:wnd[0]/usr/subX/txtRNPA1-PASSNR</c> saca <c>PASSNR</c>: el nombre técnico del campo
    /// ABAP, que es lo estable. El prefijo del control (txt/ctxt/lbl) y la ruta del dynpro cambian
    /// entre pantallas; lo que va tras el guion, no.
    /// </summary>
    public static string TechnicalName(string selector)
    {
        string id = SapSelector.IdOf(selector ?? "");
        if (id.Length == 0) return "";
        string leaf = id[(id.LastIndexOf('/') + 1)..];
        int dash = leaf.LastIndexOf('-');
        return dash >= 0 && dash + 1 < leaf.Length ? leaf[(dash + 1)..] : "";
    }

    /// <summary>SAP rellena los numéricos con ceros a la izquierda: 0000012345 == 12345.</summary>
    public static bool Matches(string screenValue, string expected)
    {
        string a = screenValue.Trim().TrimStart('0');
        string b = expected.Trim().TrimStart('0');
        return a.Length > 0 && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
