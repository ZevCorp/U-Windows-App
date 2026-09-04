namespace U.WindowsClient.Navigation;

/// <summary>Un recuerdo listo para colgar: dónde vive, de qué elemento, y qué significa.</summary>
public sealed record RecuerdoParaColgar(string Ubicacion, string Selector, string Significado, string DeDonde);

/// <summary>
/// LOS RECUERDOS QUE SALEN DE UNA DEMOSTRACIÓN, sin que nadie señale nada. Promesas 124, 125 y 128.
/// </summary>
/// <remarks>
/// LA EXPERIENCIA QUE ESTO ALIVIA, dicha por el dueño: enseñar señalando elemento por elemento es
/// tedioso. Y no hace falta — la demo YA oyó «aquí va el peso» mientras el operador lo teclaba, y
/// eso es exactamente un recuerdo. Los recuerdos a mano NO se retiran: siguen siendo la vía para
/// corregir y para enseñar algo suelto. Lo que cambia es que la demostración los rellene mejor que
/// nadie.
///
/// DÓNDE SE CUELGA UN RECUERDO, y es la decisión que más importa aquí. Un paso tiene DOS ubicaciones
/// posibles: la pantalla donde se dio y la pantalla a la que llegó. El recuerdo va en la PRIMERA,
/// porque es la pantalla a la que se vuelve: colgado en la llegada no se encontraría nunca al
/// regresar, y un recuerdo que no se encuentra es tiempo de alguien tirado a la basura.
///
/// EL GUARDARRAÍL DEL VIDEO (promesa 125), que es la parte que puede salir muy mal. El resumen del
/// video es prosa de un modelo, y la forma fácil de usarlo es dejar que diga a qué elemento se
/// refiere. Eso NO puede pasar: el selector lo pone el PASO —lo que la mano tocó— y el modelo solo
/// aporta el significado. Una sugerencia sobre un elemento que ningún paso tocó se descarta. Es la
/// misma disciplina que hizo funcionar el relleno el 2026-09-02: el dato va donde el recuerdo dice,
/// nunca donde el modelo elija.
///
/// PURO: ni red, ni grafo, ni pantalla. Quien cuelgue estos recuerdos usa la MISMA puerta que la voz
/// (<c>Ensenar</c>), con sus mismas reglas — escribir a mano no es un atajo para meter en el grafo
/// algo que la voz no habría podido meter.
/// </remarks>
public static class RecuerdosDeUnaSkill
{
    /// <summary>
    /// Los recuerdos que esta skill puede colgar. Uno por paso que sonaba, más los que el video
    /// sugiera SOBRE UN PASO REAL.
    /// </summary>
    /// <param name="resumenDelVideo">Lo que el LLM contó del video. Vacío si no se pudo procesar:
    /// entonces no aporta nada y se sigue con lo dicho.</param>
    /// <param name="sugerencias">(selector, significado) propuestos por el modelo a partir del
    /// video. Los que no casen con un paso se descartan.</param>
    public static List<RecuerdoParaColgar> De(
        SkillEnsenada skill, string resumenDelVideo,
        IReadOnlyList<(string Selector, string Significado)> sugerencias)
    {
        var salida = new List<RecuerdoParaColgar>();
        if (skill == null || skill.Pasos.Count == 0) return salida;

        // DÓNDE SE DIO CADA PASO: el primero, donde empieza la skill; los siguientes, donde llegó
        // el anterior. Es la cadena que la propia demo observó, sin inventar ninguna pantalla.
        string donde = skill.DondeEmpieza;
        var ubicacionDelPaso = new Dictionary<string, string>(StringComparer.Ordinal);
        var yaPuesto = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // LO QUE EL MODELO ENTENDIÓ, POR SELECTOR, listo para ganarle al balbuceo (promesa 143).
        // 2026-09-03: sobre el árbol de IS-H se colgó «Sí, perfecto. Entonces, um Entonces...hay
        // otra forma de entrar y es con esto de aquí, de ISH, um le das slowly click aquí» — y para
        // ESE MISMO elemento el modelo ya había entendido «Al hacer doble clic aquí, se ingresa
        // directamente a la misma pantalla que con el comando NWP1», guardado en la skill. Se colgó
        // el balbuceo y se ignoró la frase buena. Un recuerdo es lo que le va a servir a quien
        // llegue ahí dentro de tres meses: cómo se dijo no sirve para eso.
        var entendido = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in sugerencias ?? Array.Empty<(string, string)>())
        {
            string sel = (s.Selector ?? "").Trim(), que = (s.Significado ?? "").Trim();
            if (sel.Length > 0 && que.Length > 0) entendido[sel] = que;
        }

        foreach (var p in skill.Pasos)
        {
            if (p.Exit.Length > 0 && !ubicacionDelPaso.ContainsKey(p.Exit))
                ubicacionDelPaso[p.Exit] = donde;

            // LO DICHO ES EL SIGNIFICADO cuando nadie lo entendió mejor (promesa 124). Sin frase ni
            // significado no se inventa nada: un recuerdo fabricado sobre un paso mudo es una caja
            // que miente.
            string dicho = (p.Dicho ?? "").Trim();
            bool loEntendio = p.Exit.Length > 0 && entendido.TryGetValue(p.Exit, out var mejor);
            if (p.Exit.Length > 0 && (dicho.Length > 0 || loEntendio) && yaPuesto.Add(p.Exit))
                salida.Add(loEntendio
                    ? new RecuerdoParaColgar(donde, p.Exit, entendido[p.Exit], "el video")
                    : new RecuerdoParaColgar(donde, p.Exit, dicho, "lo dicho"));

            if (p.Llegada.Length > 0) donde = p.Llegada;
        }

        // LO QUE EL VIDEO SUGIERE, solo si su elemento fue TOCADO por algún paso (promesa 125).
        foreach (var s in sugerencias ?? Array.Empty<(string, string)>())
        {
            string sel = (s.Selector ?? "").Trim();
            string que = (s.Significado ?? "").Trim();
            if (sel.Length == 0 || que.Length == 0) continue;
            if (!ubicacionDelPaso.TryGetValue(sel, out var ubi)) continue;   // ningún paso lo tocó
            if (!yaPuesto.Add(sel)) continue;                                // lo dicho manda
            salida.Add(new RecuerdoParaColgar(ubi, sel, que, "el video"));
        }

        return salida;
    }

    /// <summary>
    /// La cuenta de la comprobación: de dónde salió cada recuerdo, y si el video llegó. Promesa 128.
    /// </summary>
    /// <remarks>
    /// EL VIDEO SE PROCESA CON IA por decisión del dueño (2026-09-03), y el riesgo conocido es el
    /// 504 de Vercel en flujos largos — pasó con seis pantallas y tres minutos. Un contexto que se
    /// pierde en silencio se parece demasiado a un contexto que no hacía falta, así que el hueco se
    /// dice. Y se distingue la procedencia para poder revisar de dónde salió un recuerdo raro.
    /// </remarks>
    public static string Cuenta(string resumenDelVideo, int deLoDicho, int delVideo)
    {
        bool hayVideo = !string.IsNullOrWhiteSpace(resumenDelVideo);
        string cuantos = $"{deLoDicho + delVideo} recuerdo(s): {deLoDicho} de lo que dijiste";

        return hayVideo
            ? $"{cuantos} y {delVideo} de lo que el video aportó."
            : $"{cuantos}. El video no se pudo procesar, así que no aportó ninguno — lo aprendido "
            + "sigue siendo lo que dijiste, y puedes enseñar el resto señalando.";
    }
}
