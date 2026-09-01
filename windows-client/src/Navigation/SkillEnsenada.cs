using System.IO;
using System.Text.Json;

namespace U.WindowsClient.Navigation;

/// <summary>Un paso tal como la DEMOSTRACIÓN lo vio: qué se tocó (o tecleó), a dónde llegó, y qué
/// estaba diciendo el humano mientras. Promesas 85 y 88 (spec 004).</summary>
public sealed record PasoEnsenado(string Exit, string Texto = "", string Llegada = "", string Dicho = "");

/// <summary>Lo que un catálogo anuncia de una skill: lo justo para pedirla sin abrirla.</summary>
public sealed record SkillAnunciada(string Nombre, string Description, string Archivo);

/// <summary>
/// UNA SKILL ENSEÑADA: el artefacto que sale de una demostración humana. Promesas 85 y 89.
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
    /// <summary>La carpeta por defecto donde viven las skills de esta máquina.</summary>
    public static string CarpetaPorDefecto => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "U", "skills");

    /// <summary>
    /// De la sesión a la skill. Devuelve null si no hay nada que empaquetar: una skill sin pasos
    /// no enseña nada, y empaquetar el vacío sería anunciarle al cerebro una mentira con nombre.
    /// </summary>
    public static SkillEnsenada? Empaquetar(
        string nombre, string description, string dondeEmpieza, IReadOnlyList<PasoEnsenado> observados)
    {
        if (string.IsNullOrWhiteSpace(nombre) || string.IsNullOrWhiteSpace(dondeEmpieza)) return null;
        // Un paso vale si toca algo o escribe algo; el resto es ruido de sesión.
        var pasos = observados.Where(p => p.Exit.Length > 0 || p.Texto.Length > 0).ToList();
        if (pasos.Count == 0) return null;
        return new(nombre.Trim(), (description ?? "").Trim(), dondeEmpieza.Trim(), pasos);
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
    /// Lo que hay para anunciar (promesa 89): nombre y CUÁNDO usarla, por skill guardada. Con cero
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
            if (s != null) lista.Add(new(s.Nombre, s.Description, f));
        }
        return lista;
    }
}
