using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using U.WindowsClient.Cuenta;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Clinical;

/// <summary>Una sección de la plantilla congelada del encounter, donde el médico puede escribir.</summary>
public sealed record SeccionDelBorrador(string Clave, string Titulo, int Orden);

/// <summary>
/// LO QUE EL MÉDICO ESCRIBE POR SECCIÓN MIENTRAS GRABA, y cómo llega a la nota. Promesas 468 y 471
/// (spec 057).
/// </summary>
/// <remarks>
/// PUERTO LÍNEA A LÍNEA de `lib/clinical/section-drafts.ts` de la web. El motor de notas solo recibe
/// la transcripción y el snapshot: no hay un tercer canal (D20 de la web). Por eso lo escrito viaja
/// como un bloque ROTULADO al final de la transcripción —el rótulo dice que se escribió y no se
/// habló, así el registro no miente sobre su origen— y no por `note-adjustment`, cuyo prompt prohíbe
/// justo esto: agregar datos clínicos nuevos.
///
/// EL MISMO TEXTO QUE LA WEB, carácter a carácter (vectores `borradores`): la nota tiene que salir
/// igual la genere U o la web, y el bloque es el prompt.
/// </remarks>
public static class BorradoresDeSeccion
{
    /// <summary>La cabecera del bloque. Se busca literal al quitarlo, así que es una constante.</summary>
    public const string Marca = "--- ANOTACIONES ESCRITAS POR EL MÉDICO DURANTE LA CONSULTA ---";

    private static readonly string Instrucciones = string.Join(" ",
        "Estas frases las escribió el médico durante la consulta; no se dijeron en voz alta.",
        "Son información explícita de la consulta: úsalas como cualquier otro dato dicho.",
        "Cada línea empieza con la sección a la que pertenece: redáctala DENTRO de esa sección, integrándola con lo que sí se habló.",
        "No las copies tal cual como una lista aparte, y no las lleves a otras secciones.");

    private static readonly Regex Saltos = new(@"\s*\n+\s*");
    private static readonly Regex Espacios = new(@"\s{2,}");

    /// <summary>Solo las secciones con texto de verdad, recortado, en el orden en que vinieron.</summary>
    public static Dictionary<string, string> Normalizar(IReadOnlyDictionary<string, string>? borradores)
    {
        var fuera = new Dictionary<string, string>(StringComparer.Ordinal);
        if (borradores == null) return fuera;
        foreach (var (clave, valor) in borradores)
        {
            string limpio = (valor ?? "").Trim();
            if (limpio.Length > 0) fuera[clave] = limpio;
        }
        return fuera;
    }

    /// <summary>Cuántas secciones llevan algo escrito.</summary>
    public static int Contar(IReadOnlyDictionary<string, string>? borradores) => Normalizar(borradores).Count;

    /// <summary>
    /// La transcripción más el bloque. Sin nada escrito devuelve la transcripción TAL CUAL: una
    /// consulta que no use esto se comporta exactamente como antes.
    /// </summary>
    public static string Bloque(string transcripcion, IReadOnlyDictionary<string, string>? borradores,
        IReadOnlyList<SeccionDelBorrador>? secciones)
    {
        var limpias = Normalizar(borradores);
        if (limpias.Count == 0) return transcripcion;

        // El orden de la plantilla, no el de escritura: así el bloque se lee como la nota. Estable,
        // como el sort de JavaScript.
        var ordenadas = (secciones ?? Array.Empty<SeccionDelBorrador>()).OrderBy(s => s.Orden).ToList();
        var lineas = new List<string>();
        var vistas = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in ordenadas)
        {
            if (!limpias.TryGetValue(s.Clave, out var texto)) continue;
            vistas.Add(s.Clave);
            lineas.Add($"[{s.Titulo}] {UnSoloParrafo(texto)}");
        }
        // Lo escrito en una sección que ya no está en la plantilla NO se tira: va con su clave.
        foreach (var (clave, texto) in limpias)
            if (!vistas.Contains(clave)) lineas.Add($"[{clave}] {UnSoloParrafo(texto)}");

        var partes = new List<string> { transcripcion.TrimEnd(), "", Marca, Instrucciones };
        partes.AddRange(lineas);
        return string.Join("\n", partes);
    }

    /// <summary>Quita el bloque. Hace falta al REGENERAR: si no, las anotaciones saldrían duplicadas.</summary>
    public static string Quitar(string transcripcion)
    {
        int i = transcripcion.IndexOf(Marca, StringComparison.Ordinal);
        return i < 0 ? transcripcion : transcripcion[..i].TrimEnd();
    }

    /// <summary>Dentro del bloque cada anotación ocupa UNA línea, o se rompe el emparejado con su sección.</summary>
    private static string UnSoloParrafo(string texto) => Espacios.Replace(Saltos.Replace(texto, " "), " ").Trim();

    /// <summary>
    /// Las secciones del <c>template_snapshot</c> congelado del encounter, en su orden. Las que no
    /// tienen clave no se ofrecen: lo escrito ahí no tendría dónde aterrizar.
    /// </summary>
    public static IReadOnlyList<SeccionDelBorrador> SeccionesDe(JsonElement snapshot)
    {
        var fuera = new List<SeccionDelBorrador>();
        if (snapshot.ValueKind != JsonValueKind.Object
            || !snapshot.TryGetProperty("sections", out var secciones) || secciones.ValueKind != JsonValueKind.Array)
            return fuera;
        foreach (var s in secciones.EnumerateArray())
        {
            if (s.ValueKind != JsonValueKind.Object) continue;
            string clave = s.TryGetProperty("key", out var k) && k.ValueKind == JsonValueKind.String ? (k.GetString() ?? "").Trim() : "";
            if (clave.Length == 0) continue;
            string titulo = s.TryGetProperty("label", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString() ?? "" : "";
            int orden = s.TryGetProperty("order", out var o) && o.ValueKind == JsonValueKind.Number && o.TryGetInt32(out var n) ? n : 0;
            fuera.Add(new SeccionDelBorrador(clave, titulo.Length > 0 ? titulo : clave, orden));
        }
        return fuera.OrderBy(s => s.Orden).ToList();
    }

    /// <summary>
    /// `normalize({ ...remoto, ...local })` de la web: la copia local gana sección por sección —es
    /// lo que pudo no haber alcanzado a subir— y la nube llena lo que falta.
    /// </summary>
    public static Dictionary<string, string> Combinar(IReadOnlyDictionary<string, string>? local,
        IReadOnlyDictionary<string, string>? remoto)
    {
        var junto = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (k, v) in Normalizar(remoto)) junto[k] = v;
        foreach (var (k, v) in Normalizar(local)) junto[k] = v;
        return junto;
    }
}

/// <summary>
/// LOS BORRADORES EN LA NUBE, en la MISMA tabla que la web (`encounter_section_drafts`). Promesa 469.
/// </summary>
/// <remarks>
/// SIN user_id NI doctor_id: la columna es <c>default auth.uid()</c> y la RLS exige que coincida. Con
/// el token del médico y la clave pública, como los atajos y las preferencias.
///
/// NUNCA LANZA: esto corre en mitad de una consulta. Un fallo se devuelve como «no guardado» para que
/// la pantalla lo diga y el siguiente guardado reintente; lo escrito sigue en pantalla y en la copia
/// local.
/// </remarks>
public static class BorradoresDelMedico
{
    private const int MaximoContenido = 20_000;

    public static async Task<bool> GuardarAsync(SesionMiracle sesion, string encounterId, string clave, string contenido,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(encounterId) || string.IsNullOrWhiteSpace(clave)) return false;
        try
        {
            string texto = (contenido ?? "").Trim().Length == 0 ? "" : contenido!;
            int http;
            string cuerpo;
            if (texto.Length == 0)
            {
                // VACIAR ES BORRAR SU FILA, como la web: una fila vacía mandaría un bloque sin nada.
                (http, cuerpo) = await sesion.RestAsync(HttpMethod.Delete,
                    $"/rest/v1/encounter_section_drafts?encounter_id=eq.{Uri.EscapeDataString(encounterId)}"
                    + $"&section_key=eq.{Uri.EscapeDataString(clave)}", ct: ct);
            }
            else
            {
                var fila = new JsonObject
                {
                    ["encounter_id"] = encounterId,
                    ["section_key"] = clave,
                    ["content"] = texto.Length > MaximoContenido ? texto[..MaximoContenido] : texto,
                };
                (http, cuerpo) = await sesion.RestAsync(HttpMethod.Post,
                    "/rest/v1/encounter_section_drafts?on_conflict=encounter_id,section_key",
                    fila.ToJsonString(), "resolution=merge-duplicates,return=minimal", ct);
            }
            if (http is >= 200 and < 300) return true;
            // Sin el cuerpo del borrador en el log: es texto clínico.
            LogBus.Log("borradores", $"no se guardó el borrador de «{clave}» · HTTP {http} · {Recorte(cuerpo)}");
            return false;
        }
        catch (Exception e)
        {
            LogBus.Log("borradores", $"no se guardó el borrador de «{clave}»: {e.GetType().Name}: {e.Message}");
            return false;
        }
    }

    /// <summary>Los borradores de una consulta por su section_key. Vacío si no se pueden leer.</summary>
    public static async Task<Dictionary<string, string>> LeerAsync(SesionMiracle sesion, string encounterId,
        CancellationToken ct = default)
    {
        var fuera = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(encounterId)) return fuera;
        try
        {
            var (http, cuerpo) = await sesion.RestAsync(HttpMethod.Get,
                $"/rest/v1/encounter_section_drafts?select=section_key,content&encounter_id=eq.{Uri.EscapeDataString(encounterId)}", ct: ct);
            if (http is < 200 or >= 300)
            {
                LogBus.Log("borradores", $"no se pudieron leer los borradores · HTTP {http}");
                return fuera;
            }
            using var doc = JsonDocument.Parse(cuerpo);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return fuera;
            foreach (var f in doc.RootElement.EnumerateArray())
            {
                string clave = f.TryGetProperty("section_key", out var k) && k.ValueKind == JsonValueKind.String ? k.GetString() ?? "" : "";
                string texto = f.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() ?? "" : "";
                if (clave.Length > 0 && texto.Length > 0) fuera[clave] = texto;
            }
        }
        catch (Exception e)
        {
            LogBus.Log("borradores", $"no se pudieron leer los borradores: {e.GetType().Name}: {e.Message}");
        }
        return fuera;
    }

    private static string Recorte(string s) => s.Length <= 200 ? s : s[..200];
}

/// <summary>
/// LA COPIA LOCAL de los borradores, CIFRADA con DPAPI para el usuario de Windows.
/// </summary>
/// <remarks>
/// En la web es localStorage en claro; aquí es un archivo en disco con texto clínico, y eso no se
/// deja legible para cualquiera que abra la carpeta. Se borra al terminar la consulta: su único
/// trabajo es que un cierre brusco o una caída de red a mitad de consulta no se lleve lo escrito.
/// </remarks>
public static class CopiaDeBorradores
{
    private static string Ruta(string encounterId) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "U", "borradores",
            $"{PlantillaPredeterminada.NormalizarEspecialidad(encounterId)}.bin");

    public static void Guardar(string encounterId, IReadOnlyDictionary<string, string> borradores)
    {
        if (string.IsNullOrWhiteSpace(encounterId)) return;
        try
        {
            string ruta = Ruta(encounterId);
            Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
            byte[] claro = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(BorradoresDeSeccion.Normalizar(borradores)));
            File.WriteAllBytes(ruta, Dpapi.Proteger(claro));
        }
        catch (Exception e)
        {
            LogBus.Log("borradores", $"no se pudo guardar la copia local: {e.GetType().Name}: {e.Message}");
        }
    }

    public static Dictionary<string, string> Leer(string encounterId)
    {
        try
        {
            string ruta = Ruta(encounterId);
            if (!File.Exists(ruta)) return new();
            string json = Encoding.UTF8.GetString(Dpapi.Revelar(File.ReadAllBytes(ruta)));
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
        }
        catch (Exception e)
        {
            LogBus.Log("borradores", $"no se pudo leer la copia local: {e.GetType().Name}: {e.Message}");
            return new();
        }
    }

    public static void Borrar(string encounterId)
    {
        try { File.Delete(Ruta(encounterId)); }
        catch (Exception e) { LogBus.Log("borradores", $"no se pudo borrar la copia local: {e.GetType().Name}"); }
    }
}
