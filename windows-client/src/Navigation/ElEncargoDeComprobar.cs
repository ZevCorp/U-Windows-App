namespace U.WindowsClient.Navigation;

/// <summary>
/// EL ENCARGO QUE RECIBE EL PILOTO AL COMPROBAR UNA SKILL. Promesa 139 (spec 009, fase 10).
/// </summary>
/// <remarks>
/// LA FRASE DEL DUEÑO (2026-09-03): «siento que todavía estamos tratando la enseñanza como si
/// fueran workflows». Y era verdad: <c>InstanciarSkill.Pasos → batch</c> reproduce paso a paso lo
/// que las manos hicieron, y todo lo que no hicieron las manos —el scroll que pidió por voz, el
/// «baja hasta el final», lo que el video entendió— se tiraba al empaquetar. Un guion con el
/// 30 % de la lección.
///
/// LO QUE SE QUIERE: que Ü, con el OBJETIVO y todo el contexto de la demo, vaya hacia el objetivo
/// por su cuenta, narrando lo que entendió («lo primero que me dijiste fue escribir el código
/// aquí…»), y cuelgue un recuerdo de cada elemento que use. Los pasos observados son pistas, no
/// obligaciones: el batch alcanza una fila conocida por identidad aunque esté desplazada (promesa
/// 80), así que el scroll que la demo no grabó deja de importar.
///
/// LA ASIMETRÍA QUE HACE ESTO SEGURO, y es la única regla dura de este archivo:
///
///   EL MODELO PUEDE NOMBRAR UN DESTINO; NUNCA UN VALOR.
///
/// Nombrar un destino que no existe cuesta una parada honesta de la compuerta («no lo veo aquí»).
/// Nombrar un campo con un valor que la demo tecleó costaría el «70» del paciente de prueba en la
/// historia de otro. Por eso el encargo lleva selectores y lo que se DIJO sobre ellos, y los
/// valores tecleados NO viajan — ni como ejemplo.
///
/// PURO: solo redacta. Quien lo ejecuta es el mismo piloto de siempre (<c>AgentLoop</c>), con la
/// misma compuerta de superficie y las mismas herramientas; comprobar no estrena ningún ejecutor.
/// </remarks>
public static class ElEncargoDeComprobar
{
    /// <summary>A dónde tiene que llegar: donde acabó la demo, o —si no se guardó— la última
    /// llegada observada. Vacío si no se sabe nada, y entonces el encargo lo dice.</summary>
    public static string Destino(SkillEnsenada skill)
    {
        if (skill == null) return "";
        if (!string.IsNullOrWhiteSpace(skill.DondeTermina)) return skill.DondeTermina.Trim();
        for (int i = skill.Pasos.Count - 1; i >= 0; i--)
            if (!string.IsNullOrWhiteSpace(skill.Pasos[i].Llegada)) return skill.Pasos[i].Llegada.Trim();
        return "";
    }

    public static string Texto(SkillEnsenada skill)
    {
        if (skill == null) return "";
        var t = new System.Text.StringBuilder();

        t.AppendLine("Estás COMPROBANDO algo que te enseñaron hace un momento: vas a hacer tú la tarea, "
            + "desde donde estás, hasta llegar al objetivo. No es una orden nueva: es la lección, "
            + "puesta en práctica por ti.");
        t.AppendLine();
        t.AppendLine($"LA TAREA: {(skill.Description.Length > 0 ? skill.Description : skill.Nombre)}.");
        t.AppendLine($"EMPIEZA EN: {skill.DondeEmpieza}");
        string destino = Destino(skill);
        t.AppendLine(destino.Length > 0
            ? $"TIENES QUE ACABAR EN: {destino} — ahí es donde acabó quien te enseñó, y llegar ahí es "
              + "lo único que cuenta como haber terminado. map_where_am_i te lo dice; tu impresión no."
            : "NO SÉ DÓNDE ACABÓ quien te enseñó: dilo al terminar en vez de dar la tarea por hecha.");
        t.AppendLine();

        t.AppendLine("LO QUE HIZO Y DIJO QUIEN TE ENSEÑÓ, en orden. Son pistas para entender la tarea, no "
            + "un guion: si encuentras el mismo destino por otro camino que esté vivo, vale igual. "
            + "Los elementos van por su identidad exacta, y así puedes pedirlos por nombre:");
        int n = 0;
        foreach (var p in skill.Pasos)
        {
            n++;
            string que = p.Exit.StartsWith("key:", StringComparison.OrdinalIgnoreCase)
                ? $"pulsó la tecla «{p.Exit[4..]}»"
                : p.Texto.Length > 0 ? $"escribió algo en «{p.Exit}»"
                : $"tocó «{p.Exit}»";
            t.Append($"  {n}. {que}");
            if (p.Llegada.Length > 0) t.Append($" → llegó a {p.Llegada}");
            if (p.Dicho.Length > 0) t.Append($". Mientras, decía: «{p.Dicho}»");
            t.AppendLine(".");
        }
        t.AppendLine();

        if (skill.Huecos.Count > 0)
        {
            t.AppendLine("CAMPOS QUE LLEVAN UN DATO DE CADA CORRIDA (no lo escribas ahora: hoy no hay dato, "
                + "y el de la demo era de otro caso). Solo aprende qué va ahí:");
            foreach (var h in skill.Huecos)
                t.AppendLine($"  · «{h.Campo}»: {h.Significado}");
            t.AppendLine();
        }
        if (skill.Sugerencias.Count > 0)
        {
            t.AppendLine("LO QUE EL VIDEO DE LA DEMO DEJÓ CLARO sobre algunos elementos:");
            foreach (var s in skill.Sugerencias)
                t.AppendLine($"  · «{s.Campo}»: {s.Significado}");
            t.AppendLine();
        }

        t.AppendLine("CÓMO LO HACES, y esto es lo más importante del encargo:");
        t.AppendLine();
        t.AppendLine("DE UNO EN UNO. Un elemento cada vez, nunca varios de golpe. Aquí no vienes a "
            + "terminar rápido: vienes a APRENDER delante de quien te enseñó, y quien mira tiene que "
            + "poder oír lo que entendiste y corregirte antes de que sigas. Por cada elemento, estos "
            + "tres pasos SEGUIDOS y en este orden:");
        t.AppendLine();
        t.AppendLine("  1. DILO EN VOZ, en una frase corta y con tus palabras: qué entendiste que toca "
            + "ahora y por qué («me dijiste que desde aquí se entra al puesto de trabajo clínico»). "
            + "No leas lo que te dijeron: di lo que entendiste.");
        t.AppendLine("  2. HAZLO, con map_take y el nombre o el selector de ese elemento. Si la fila está "
            + "fuera de la vista, pídela por su identidad igual: seleccionarla la trae. No escribas "
            + "ningún valor que la demo tecleara.");
        t.AppendLine("  3. CUÉLGALE EL RECUERDO ahí mismo con map_esto_es, antes de pasar al siguiente: "
            + "qué ES ese elemento y cuándo se usa, EN TUS PALABRAS y en una frase — no la "
            + "transcripción de cómo te lo dijeron. Ese recuerdo es lo que hará que la próxima vez "
            + "la tarea se pueda planificar entera antes del primer clic.");
        t.AppendLine();
        t.AppendLine("Y luego el siguiente elemento, otra vez los tres. Si te saltas el 1 nadie puede "
            + "corregirte; si te saltas el 3, esta comprobación no deja nada y habrá que repetirla.");
        t.AppendLine("  · PARA ANTES de cualquier puerta peligrosa —Grabar, Guardar, Finalizar, Borrar, "
            + "Enviar, Firmar—: comprobar no graba nada. Llega hasta esa puerta, dilo, y no la pulses.");
        t.AppendLine("  · Al terminar, di dónde estás según map_where_am_i y hasta qué punto de la tarea "
            + "llegaste. Si no llegaste al destino, dilo tal cual: «terminé» solo vale si estás ahí.");

        return t.ToString().TrimEnd();
    }
}
