using U.WindowsClient.Navigation;
using U.WindowsClient.Teach;

namespace U.WindowsClient.Piloto;

/// <summary>
/// LA SKILL NACE DE LO VERIFICADO. Promesa 176 (spec 013). Puro.
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

        // LA LLEGADA VIAJA SOLO CON LOS QUE NAVEGAN (promesa 229, 2026-09-11): para un campo
        // tecleado, lo «real» del veredicto es el VALOR leído («75», «normal»), no una pantalla, y
        // el batch exige la llegada al escribir: el ✓ habría parado en el primer campo.
        var navegan = RegistroDeLaComprobacion.EventosQueNavegan(leccion).Select(e => e.N).ToHashSet();
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
            pasos.Add(new PasoEnsenado(exit, e.Texto, navegan.Contains(e.N) ? v.Real : "", NombreDelDato(e)));
        }
        if (pasos.Count == 0) return null;

        // COMPLETA sobre lo que CUENTA (promesa 175, enmendada el 2026-09-08): los que navegan y los
        // campos tecleados. Una skill que solo rellena un formulario también puede quedar comprobada.
        var todosLosQueCuentan = RegistroDeLaComprobacion.EventosQueCuentan(leccion);
        bool completa = todosLosQueCuentan.All(e => ok.ContainsKey(e.N));

        // DONDE TERMINA ES DONDE ACABÓ LA DEMO (promesas 140 y 204): la lección lo sabe. La llegada
        // del último paso puede ser vacía (un campo tecleado) o de un evento que no cuenta.
        string dondeTermina = !string.IsNullOrWhiteSpace(leccion.Termino) ? leccion.Termino.Trim()
            : pasos.LastOrDefault(p => p.Llegada.Length > 0)?.Llegada ?? "";
        var skill = SkillEnsenada.Empaquetar(nombre.Trim(), (descripcion ?? "").Trim(), leccion.Empezo, pasos, dondeTermina);
        // Y RECUERDA DE QUÉ LECCIÓN SALIÓ (promesa 198): es lo que le permite al panel enseñar
        // la pantalla de cada paso, que vive en los cuadros de esa demostración y en ningún otro sitio.
        return skill == null ? null : skill with { Comprobada = completa, DeLaLeccion = leccion.Id ?? "" };
    }

    /// <summary>
    /// EL NOMBRE DEL DATO de un evento (promesa 196). Es lo que viaja en «Dicho» del paso, que es de
    /// donde <see cref="SkillEnsenada"/> saca sus huecos (123): con nombre hay hueco; sin nombre, el
    /// valor es parte de la tarea.
    /// </summary>
    /// <remarks>
    /// LO MEDIDO EN LA DECIMOTERCERA LECCIÓN (2026-09-08) decide el orden: los 17 campos tecleados
    /// traían la ETIQUETA del campo («Peso», «Talla», «Apertura Ocular»), y lo que se decía mientras
    /// era ruido de la demo («Temperatura de 38 y mide 1.70» colgado de «Conducta»). Con la regla
    /// vieja —hueco solo donde se narró— 14 valores del paciente de prueba quedaban como pasos fijos:
    /// el «52» de peso escrito en la historia de otro, con cara de haber salido bien.
    ///
    /// Por eso TODO campo tecleado en un formulario es un hueco, con este nombre en orden: la
    /// etiqueta del campo; si no la hay, lo que se decía; si tampoco, el nombre técnico del campo
    /// (un hueco que la nota nunca casará queda en blanco, que es más seguro que reproducir la demo).
    /// LA EXCEPCIÓN es el campo de comandos: «nwp1» es la tarea, y como hueco dejó la skill sin
    /// arrancar el 2026-09-03 («hice 0 de 4»). Un evento sin texto no es un dato: conserva lo dicho
    /// como contexto, igual que antes.
    /// </remarks>
    public static string NombreDelDato(EventoDeLaLeccion e)
    {
        string dicho = e.Dicho != null && e.Dicho.Count > 0 ? (e.Dicho[0] ?? "").Trim() : "";
        if ((e.Texto ?? "").Length == 0) return dicho;
        if (EsElCampoDeComandos(e.Selector)) return "";
        if (!string.IsNullOrWhiteSpace(e.Etiqueta)) return e.Etiqueta.Trim();
        if (dicho.Length > 0) return dicho;
        string s = (e.Selector ?? "").Trim().TrimEnd('/');
        int corte = s.LastIndexOf('/');
        return corte >= 0 ? s[(corte + 1)..] : s;
    }

    /// <summary>El campo de comandos de SAP (okcd): lo tecleado ahí es la tarea, no un dato.</summary>
    public static bool EsElCampoDeComandos(string selector) =>
        (selector ?? "").Trim().TrimEnd('/').EndsWith("/okcd", StringComparison.OrdinalIgnoreCase);
}
