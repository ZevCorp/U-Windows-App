using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace U.WindowsClient.Clinical;

public sealed record DatoDelPapel(string Etiqueta, string Valor);
public sealed record MembreteDelPapel(string Nombre, IReadOnlyList<string> Lineas);
public sealed record FirmaDelPapel(string Nombre, IReadOnlyList<string> Lineas);

/// <summary>
/// Un bloque del papel. PLANO, como en la web: todos los campos siempre presentes (vacíos si no
/// aplican), para que compararlo con la web sea campo a campo.
/// </summary>
public sealed record BloqueDelPapel(string Tipo, string Titulo, string Texto, IReadOnlyList<string> Items, int Numero,
    string Nombre, IReadOnlyList<DatoDelPapel> Campos, string Nivel, IReadOnlyList<string> Columnas,
    IReadOnlyList<IReadOnlyList<string>> Filas);

public sealed record PapelDelPaciente(string Tipo, string Titulo, MembreteDelPapel Membrete, IReadOnlyList<DatoDelPapel> Datos,
    IReadOnlyList<BloqueDelPapel> Bloques, FirmaDelPapel Firma, IReadOnlyList<string> Sello, string Pie, bool Demo);

/// <summary>
/// LOS PAPELES DE LA CONSULTA —nota clínica, fórmula médica, indicaciones para el paciente— con el
/// MISMO modelo que la web. Promesas 475-478 (spec 059).
/// </summary>
/// <remarks>
/// PUERTO LÍNEA A LÍNEA de `lib/pdf/patient-documents.ts`. Aquí se decide QUÉ dice el papel; cómo se
/// ve lo decide <c>Ui.PapelesDelPaciente</c> (FlowDocument). El contrato compara el documento entero
/// con el de la web sobre los mismos casos: el paciente se lleva el mismo papel lo imprima quien lo
/// imprima.
///
/// La entrada es el MISMO JSON que recibe la web (`DocumentoInput`): tipo, fecha local
/// "AAAA-MM-DDTHH:MM", org, medico, paciente, nota {summary, sections, discharge}, codigos, adendas,
/// demo. Quien llama lo arma con lo que tiene.
///
/// LA FÓRMULA NUNCA INVENTA (Decreto 2200 de 2005): lo que la nota no trae va vacío, y el pintor lo
/// convierte en una raya para escribir a mano.
/// </remarks>
public static class DocumentosDelPaciente
{
    private static readonly Dictionary<string, string> Titulos = new(StringComparer.Ordinal)
    {
        ["nota"] = "Nota clínica",
        ["formula"] = "Fórmula médica",
        ["indicaciones"] = "Indicaciones para el paciente",
    };

    private static readonly string[] Meses =
    {
        "enero", "febrero", "marzo", "abril", "mayo", "junio",
        "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre",
    };

    private static readonly Regex FechaConHora = new(@"^(\d{4})-(\d{2})-(\d{2})(?:T(\d{2}):(\d{2}))?", RegexOptions.CultureInvariant);
    private static readonly Regex FechaDelSelloRe = new(@"^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})", RegexOptions.CultureInvariant);
    private static readonly Regex NumeroAlPrincipio = new(@"^(\d{1,6})(?!\d)", RegexOptions.CultureInvariant);

    public static PapelDelPaciente Construir(JsonElement entrada)
    {
        string tipo = Cad(entrada, "tipo");
        if (!Titulos.ContainsKey(tipo)) tipo = "nota";
        string fecha = Cad(entrada, "fecha");
        var org = Obj(entrada, "org");
        var med = Obj(entrada, "medico");
        var pac = Obj(entrada, "paciente");
        var nota = Obj(entrada, "nota");
        var eg = EgresoDeLaNota.Leer(nota);

        string lugar = string.Join(" · ", new[] { Cad(org, "address"), Cad(org, "city") }.Where(x => x.Length > 0));
        var lineasOrg = new[]
        {
            Cad(org, "nit").Length > 0 ? $"NIT {Cad(org, "nit")}" : "",
            lugar,
            Cad(org, "phone").Length > 0 ? $"Tel. {Cad(org, "phone")}" : "",
        }.Where(x => x.Length > 0).ToList();

        string edad = pac.ValueKind == JsonValueKind.Object && pac.TryGetProperty("edad", out var ed)
                      && ed.ValueKind == JsonValueKind.Number && ed.GetDouble() > 0
            ? $"{ed.GetDouble().ToString(CultureInfo.InvariantCulture)} años" : "";
        string sexoCrudo = Cad(pac, "sexo");
        string sexo = sexoCrudo == "F" ? "Femenino" : sexoCrudo == "M" ? "Masculino" : sexoCrudo;
        var datos = new List<DatoDelPapel>
        {
            new("Paciente", Cad(pac, "nombre") is { Length: > 0 } nombre ? nombre : "Paciente sin identificar"),
            new("Documento", Cad(pac, "documento")),
            new("Edad y sexo", string.Join(" · ", new[] { edad, sexo }.Where(x => x.Length > 0))),
            new("EPS", Cad(pac, "eps")),
            new("Fecha", FechaLarga(fecha)),
            new("Lugar", Cad(org, "city")),
            new("Profesional", Cad(med, "nombre")),
            new("Especialidad", Cad(med, "especialidad")),
        }.Where(d => d.Valor.Length > 0).ToList();

        var bloques = tipo switch
        {
            "formula" => BloquesDeLaFormula(eg),
            "indicaciones" => BloquesDeLasIndicaciones(eg),
            _ => BloquesDeLaNota(entrada, nota, eg),
        };

        var firma = new FirmaDelPapel(Cad(med, "nombre"), new[]
        {
            Cad(med, "especialidad"),
            Cad(med, "documento").Length > 0 ? $"CC {Cad(med, "documento")}" : "",
            Cad(med, "registro").Length > 0 ? $"Registro médico {Cad(med, "registro")}" : "",
        }.Where(x => x.Length > 0).ToList());

        // El sello del sistema del hospital: solo en la nota y solo con los cuatro datos personales.
        var sello = tipo == "nota" && Cad(med, "honorifico").Length > 0 && Cad(med, "responsable").Length > 0
                    && Cad(med, "documento").Length > 0 && Cad(med, "registro").Length > 0
            ? new List<string>
            {
                $"Nota realizada por: {Cad(med, "honorifico")}. {Cad(med, "nombre")}"
                    + (Cad(org, "name").Length > 0 ? $" Empresa: {Cad(org, "name")}" : "")
                    + $" Fecha y hora: {FechaDelSello(fecha)}",
                $"Responsable: {Cad(med, "responsable")}",
                $"Identificación: CC{Cad(med, "documento")}",
                $"Reg. Med.: {Cad(med, "registro")}",
                $"Especialidad: {ConceptosClinicos.SinTildes(Cad(med, "especialidad")).ToUpperInvariant()}",
            }
            : new List<string>();

        string nombreOrg = Cad(org, "name");
        return new PapelDelPaciente(tipo, Titulos[tipo], new MembreteDelPapel(nombreOrg, lineasOrg), datos, bloques, firma, sello,
            (nombreOrg.Length > 0 ? $"{nombreOrg} · " : "")
            + "Documento generado con asistencia de IA y revisado por el profesional de salud. Generado con Miracle.",
            entrada.ValueKind == JsonValueKind.Object && entrada.TryGetProperty("demo", out var demo) && demo.ValueKind == JsonValueKind.True);
    }

    // ── los tres papeles ─────────────────────────────────────────────────────

    private static List<BloqueDelPapel> BloquesDeLaNota(JsonElement entrada, JsonElement nota, Egreso eg)
    {
        var fuera = new List<BloqueDelPapel>();
        string resumen = Cad(nota, "summary");
        if (resumen.Length > 0) fuera.Add(Nuevo("parrafo", titulo: "Resumen", texto: resumen));
        foreach (var s in Arreglo(nota, "sections"))
        {
            string texto = Cad(s, "content");
            if (texto.Length > 0) fuera.Add(Nuevo("parrafo", titulo: Cad(s, "label") is { Length: > 0 } l ? l : "Sección", texto: texto));
        }
        var meds = eg.Medicamentos.Select(LineaDeMedicamento).Where(x => x.Length > 0).ToList();
        if (meds.Count > 0) fuera.Add(Nuevo("lista", titulo: "Plan farmacológico", items: meds));
        foreach (var (titulo, items) in new[]
                 {
                     ("Medidas no farmacológicas", Textos(eg.NoFarmacologicas)),
                     ("Seguimiento", Textos(eg.Seguimiento)),
                     ("Recomendaciones", Textos(eg.Recomendaciones)),
                 })
            if (items.Count > 0) fuera.Add(Nuevo("lista", titulo: titulo, items: items));
        var alarmas = Textos(eg.SignosDeAlarma.Select(a => a.Texto));
        if (alarmas.Count > 0) fuera.Add(Nuevo("lista", titulo: "Signos de alarma", items: alarmas));

        var codigos = Arreglo(entrada, "codigos").ToList();
        if (codigos.Count > 0)
            fuera.Add(Nuevo("tabla", titulo: "Codificación", columnas: new[] { "Sistema", "Código", "Descripción" },
                filas: codigos.Select(c => (IReadOnlyList<string>)new[] { Cad(c, "sistema"), Cad(c, "codigo"), Cad(c, "descripcion") }).ToList()));
        foreach (var a in Arreglo(entrada, "adendas"))
            fuera.Add(Nuevo("parrafo", titulo: $"Adenda · {Cad(a, "autor")} · {FechaLarga(Cad(a, "fecha"))}", texto: Cad(a, "contenido")));
        return fuera;
    }

    private static List<BloqueDelPapel> BloquesDeLaFormula(Egreso eg)
    {
        var meds = eg.Medicamentos.Where(m => m.Nombre.Trim().Length > 0).ToList();
        if (meds.Count == 0)
            return new List<BloqueDelPapel> { Nuevo("aviso", texto: "No hay medicamentos en el plan de esta consulta.") };
        return meds.Select((m, i) => Nuevo("medicamento", numero: i + 1, nombre: m.Nombre.Trim(), campos: new[]
        {
            new DatoDelPapel("Concentración y forma", m.Concentracion.Trim()),
            new DatoDelPapel("Dosis", m.Dosis.Trim()),
            new DatoDelPapel("Vía", m.Via.Trim()),
            new DatoDelPapel("Frecuencia", m.Frecuencia.Trim()),
            new DatoDelPapel("Duración del tratamiento", m.Duracion.Trim()),
            new DatoDelPapel("Cantidad total", CantidadEnNumerosYLetras(m.Cantidad)),
            new DatoDelPapel("Indicaciones", m.Indicaciones.Trim()),
        })).ToList();
    }

    private static List<BloqueDelPapel> BloquesDeLasIndicaciones(Egreso eg)
    {
        var fuera = new List<BloqueDelPapel>();
        var meds = eg.Medicamentos
            .Select(m => string.Join(" — ", new[] { LineaDeMedicamento(m), m.Indicaciones.Trim() }.Where(x => x.Length > 0)))
            .Where(x => x.Length > 0).ToList();
        if (meds.Count > 0) fuera.Add(Nuevo("lista", titulo: "Sus medicamentos", items: meds));
        foreach (var (titulo, items) in new[]
                 {
                     ("Recomendaciones", Textos(eg.Recomendaciones)),
                     ("Cuidados en casa", Textos(eg.NoFarmacologicas)),
                     ("Sus próximos controles", Textos(eg.Seguimiento)),
                 })
            if (items.Count > 0) fuera.Add(Nuevo("lista", titulo: titulo, items: items));
        fuera.AddRange(AlarmasPorNivel(eg));
        if (fuera.Count == 0) fuera.Add(Nuevo("aviso", texto: "Esta consulta no dejó indicaciones registradas."));
        return fuera;
    }

    private static readonly (string Nivel, string Titulo)[] Niveles =
    {
        ("emergency", "Acuda a urgencias de inmediato si presenta"),
        ("priority", "Consulte pronto, en las próximas 24 horas, si presenta"),
        ("monitor", "Esté atento y consulte si presenta"),
        ("", "Consulte si presenta"),
    };

    private static IEnumerable<BloqueDelPapel> AlarmasPorNivel(Egreso eg)
    {
        var conocidos = new HashSet<string>(StringComparer.Ordinal) { "emergency", "priority", "monitor" };
        foreach (var (nivel, titulo) in Niveles)
        {
            var items = eg.SignosDeAlarma
                .Where(a => nivel.Length == 0 ? !conocidos.Contains(a.Urgencia.Trim()) : a.Urgencia.Trim() == nivel)
                .Select(a => a.Texto.Trim()).Where(x => x.Length > 0).ToList();
            if (items.Count > 0) yield return Nuevo("alarma", titulo: titulo, items: items, nivel: nivel);
        }
    }

    // ── números, fechas y líneas ─────────────────────────────────────────────

    /// <summary>"Acetaminofén · 500 mg/tableta · 1 tableta · oral · cada 8 horas · 5 días".</summary>
    public static string LineaDeMedicamento(Medicamento m) =>
        string.Join(" · ", new[] { m.Nombre, m.Concentracion, m.Dosis, m.Via, m.Frecuencia, m.Duracion }
            .Select(x => x.Trim()).Where(x => x.Length > 0));

    /// <summary>"2026-09-26T14:30" → "26 de septiembre de 2026, 2:30 p. m.". Sin fecha válida, "".</summary>
    public static string FechaLarga(string fecha)
    {
        var m = FechaConHora.Match((fecha ?? "").Trim());
        if (!m.Success) return "";
        int mes = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        if (mes < 1 || mes > 12) return "";
        string dia = $"{int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture)} de {Meses[mes - 1]} de {m.Groups[1].Value}";
        if (!m.Groups[4].Success) return dia;
        int h24 = int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
        int h = h24 % 12 == 0 ? 12 : h24 % 12;
        return $"{dia}, {h}:{m.Groups[5].Value} {(h24 >= 12 ? "p. m." : "a. m.")}";
    }

    private static string FechaDelSello(string fecha)
    {
        var m = FechaDelSelloRe.Match((fecha ?? "").Trim());
        if (!m.Success) return "";
        int h24 = int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
        string h = (h24 % 12 == 0 ? 12 : h24 % 12).ToString("00", CultureInfo.InvariantCulture);
        return $"{m.Groups[3].Value}/{m.Groups[2].Value}/{m.Groups[1].Value}, {h}:{m.Groups[5].Value} {(h24 >= 12 ? "p. m." : "a. m.")}";
    }

    private static readonly string[] Unidades =
    {
        "cero", "uno", "dos", "tres", "cuatro", "cinco", "seis", "siete", "ocho", "nueve",
        "diez", "once", "doce", "trece", "catorce", "quince", "dieciséis", "diecisiete",
        "dieciocho", "diecinueve", "veinte", "veintiuno", "veintidós", "veintitrés",
        "veinticuatro", "veinticinco", "veintiséis", "veintisiete", "veintiocho", "veintinueve",
    };
    private static readonly string[] Decenas = { "", "", "", "treinta", "cuarenta", "cincuenta", "sesenta", "setenta", "ochenta", "noventa" };
    private static readonly string[] Centenas =
    {
        "", "ciento", "doscientos", "trescientos", "cuatrocientos", "quinientos",
        "seiscientos", "setecientos", "ochocientos", "novecientos",
    };

    private static string MenorDeMil(long n)
    {
        if (n == 100) return "cien";
        long c = n / 100, resto = n % 100;
        var partes = new List<string>();
        if (c > 0) partes.Add(Centenas[c]);
        if (resto > 0)
        {
            if (resto < 30) partes.Add(Unidades[resto]);
            else
            {
                long d = resto / 10, u = resto % 10;
                partes.Add(u == 0 ? Decenas[d] : $"{Decenas[d]} y {Unidades[u]}");
            }
        }
        return string.Join(" ", partes);
    }

    /// <summary>Un entero de 0 a 999 999 en letras. Fuera de rango, "".</summary>
    public static string NumeroEnLetras(long n)
    {
        if (n < 0 || n > 999_999) return "";
        if (n < 1000) return n == 0 ? "cero" : MenorDeMil(n);
        long miles = n / 1000, resto = n % 1000;
        string cabeza = miles == 1 ? "mil" : $"{MenorDeMil(miles)} mil";
        return resto == 0 ? cabeza : $"{cabeza} {MenorDeMil(resto)}";
    }

    /// <summary>"15 tabletas" → "15 tabletas (quince)". Sin número al principio, tal cual.</summary>
    public static string CantidadEnNumerosYLetras(string? cantidad)
    {
        string texto = (cantidad ?? "").Trim();
        var m = NumeroAlPrincipio.Match(texto);
        if (!m.Success) return texto;
        string letras = NumeroEnLetras(long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
        return letras.Length > 0 ? $"{texto} ({letras})" : texto;
    }

    // ── la maquinaria ───────────────────────────────────────────────────────

    private static BloqueDelPapel Nuevo(string tipo, string titulo = "", string texto = "", IReadOnlyList<string>? items = null,
        int numero = 0, string nombre = "", IReadOnlyList<DatoDelPapel>? campos = null, string nivel = "",
        IReadOnlyList<string>? columnas = null, IReadOnlyList<IReadOnlyList<string>>? filas = null) =>
        new(tipo, titulo, texto, items ?? Array.Empty<string>(), numero, nombre, campos ?? Array.Empty<DatoDelPapel>(),
            nivel, columnas ?? Array.Empty<string>(), filas ?? Array.Empty<IReadOnlyList<string>>());

    private static List<string> Textos(IEnumerable<string> items) => items.Select(x => x.Trim()).Where(x => x.Length > 0).ToList();

    private static JsonElement Obj(JsonElement o, string campo) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(campo, out var v) && v.ValueKind == JsonValueKind.Object ? v : default;

    private static IEnumerable<JsonElement> Arreglo(JsonElement o, string campo) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(campo, out var a) && a.ValueKind == JsonValueKind.Array
            ? a.EnumerateArray().ToList() : Enumerable.Empty<JsonElement>();

    /// <summary>Un dato de texto recortado; nulo, ausente o no-texto como vacío. Nunca «null».</summary>
    private static string Cad(JsonElement o, string campo) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(campo, out var v) && v.ValueKind == JsonValueKind.String
            ? (v.GetString() ?? "").Trim() : "";
}
