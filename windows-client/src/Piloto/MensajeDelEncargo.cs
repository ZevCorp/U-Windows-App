using System.IO;
using U.WindowsClient.Clinical;
using U.WindowsClient.Navigation;
using U.WindowsClient.Teach;

namespace U.WindowsClient.Piloto;

/// <summary>
/// EL ENCARGO DE LA NOTA COMO MENSAJE PARA EL PILOTO. Promesa 194 (spec 015). Puro.
/// </summary>
/// <remarks>
/// EL PILOTO ELIGE, decisión del dueño (2026-09-10): «el LLM que recibe la info del check debe ser
/// capaz de elegir la skill por su inteligencia e interpretación propia». Para elegir necesita dos
/// cosas y nada más: lo que la nota marcada dice, tal cual, y el catálogo con los DATOS que cada
/// skill necesita (193). Aquí no se decide cuál skill ni qué valor va en qué hueco: eso es suyo.
///
/// SOLO SE OFRECEN LAS COMPROBADAS: una skill sin comprobar no se puede correr (127), y ofrecerla
/// es tentar a elegirla para después negársela. Sin ninguna comprobada, el mensaje lo dice y el
/// piloto para: no hay tarea que inventar.
///
/// Mismo tipo de bloque que la lección (<see cref="Bloque"/>) y mismo sobre (<see cref="ElPiloto.Mensaje"/>):
/// un solo formato para hablarle al piloto.
/// </remarks>
public static class MensajeDelEncargo
{
    public static IReadOnlyList<Bloque> Armar(Encargo encargo, IReadOnlyList<SkillAnunciada> catalogo)
    {
        var bloques = new List<Bloque>();
        var listas = (catalogo ?? Array.Empty<SkillAnunciada>()).Where(c => c.Comprobada).ToList();
        bloques.Add(new Bloque("text",
            "ENCARGO: el médico marcó estas secciones de su nota clínica para llevarlas a SAP. Dicen, tal cual:\n\n"
            + (encargo?.Texto ?? "").Trim()));
        if (listas.Count == 0)
        {
            bloques.Add(new Bloque("text",
                "TAREAS comprobadas: ninguna. No hay skill que correr: dilo con voz_decir en una frase y para."));
            return bloques;
        }
        var lineas = listas.Select(c =>
            $"· «{c.Nombre}»{(c.Description.Length > 0 ? " — " + c.Description : "")}"
            + (c.Huecos.Count > 0 ? $"\n    datos que necesita: {string.Join(", ", c.Huecos)}" : "\n    no necesita datos"));
        bloques.Add(new Bloque("text",
            $"TAREAS que me enseñaron y están comprobadas ({listas.Count}). Elige UNA por tu criterio —qué hace y qué "
            + "datos pide, contra lo que la nota trae— y córrela con map_skill_run(nombre, datos), donde `datos` usa "
            + "EXACTAMENTE los nombres de «datos que necesita»:\n" + string.Join("\n", lineas)));
        return bloques;
    }

    /// <summary>Dónde se deja el mensaje de este encargo: una carpeta por envío, junto a las lecciones.</summary>
    public static string NuevaCarpeta()
    {
        string carpeta = Path.Combine(U.Graph.UserPaths.Local, "U", "encargos",
            "encargo_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(carpeta);
        return carpeta;
    }
}
