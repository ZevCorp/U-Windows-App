using U.WindowsClient.Navigation;
using U.WindowsClient.Teach;

namespace U.WindowsClient.Piloto;

/// <summary>
/// LA SKILL NACE DE LO VERIFICADO. Promesa 176 (spec 012). Puro.
/// </summary>
/// <remarks>
/// SOLO ENTRA EL PASO QUE ATERRIZÓ. Lo que la demo mostró es una pista; lo que el piloto hizo y la
/// app juzgó es un hecho. Un paso que no aterrizó no entra —ni marcado, ni «pendiente»—, porque
/// una skill con un paso que nunca se pudo dar es una skill que va a fallar en el hospital con
/// cara de funcionar. Lo que el modelo dijo entender ya vive en el grafo como recuerdos (fase 3);
/// aquí no se guarda prosa, se guardan pasos.
///
/// LA LLEGADA ES LA REAL, la que la app leyó al comprobar, no la grabada: si difieren, la que vale
/// es la que se acaba de ver funcionar.
/// </remarks>
public static class SkillDeLoVerificado
{
    public static SkillEnsenada? Empaquetar(Leccion leccion, IReadOnlyList<VeredictoDeEvento> veredictos,
        string nombre, string descripcion)
    {
        if (leccion == null || string.IsNullOrWhiteSpace(nombre)) return null;
        var ok = (veredictos ?? Array.Empty<VeredictoDeEvento>()).Where(v => v.Aterrizo).ToDictionary(v => v.N, v => v);

        var pasos = new List<PasoEnsenado>();
        foreach (var e in leccion.Eventos)
        {
            if (!ok.TryGetValue(e.N, out var v)) continue;
            // LA IDENTIDAD ES LA PUERTA POR SU NOMBRE: es lo que map_take y el batch entienden. El selector
            // crudo es el respaldo. Segunda prueba real (2026-09-07): un evento aterrizó con puerta y sin
            // selector, y la skill no se guardó por exigir el selector.
            string identidad = e.Etiqueta.Length > 0 ? e.Etiqueta : e.Selector;
            if (string.IsNullOrWhiteSpace(identidad)) continue; // sin identidad no hay forma de repetirlo por identidad
            string exit = identidad;
            pasos.Add(new PasoEnsenado(exit, e.Texto, v.Real, e.Dicho.Count > 0 ? e.Dicho[0] : ""));
        }
        if (pasos.Count == 0) return null;

        var todosLosQueNavegan = RegistroDeLaComprobacion.EventosQueNavegan(leccion);
        bool completa = todosLosQueNavegan.All(e => ok.ContainsKey(e.N));

        var skill = SkillEnsenada.Empaquetar(nombre.Trim(), (descripcion ?? "").Trim(), leccion.Empezo, pasos, pasos[^1].Llegada);
        return skill == null ? null : skill with { Comprobada = completa };
    }
}
