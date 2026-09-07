namespace U.WindowsClient.Teach;

/// <summary>
/// CUÁNDO EMPIEZA DE VERDAD UNA DEMOSTRACIÓN. Promesa 137 (spec 009, fase 9).
/// </summary>
/// <remarks>
/// LO QUE PASÓ, dos veces seguidas el 2026-09-03:
///
///   18:46:20  voz-viva: micrófono abierto a 24000 Hz        ← 4 s después de pulsar 🎓
///   18:46:23  voz-viva: usuario dijo: ¿y tú me escuchas?
///   18:46:27  ✋ el primer plano sigue siendo Ü tras 4000 ms de gracia — NO se graba
///
/// El micrófono se abre solo al pulsar 🎓 desde ese mismo día, y con él Ü saluda. Eso metió una
/// CONVERSACIÓN encima de un plazo de siete segundos —tres de cuenta atrás más cuatro de gracia—
/// cuya única forma de cumplirse era cambiar de ventana. El humano estaba comprobando que Ü le oía,
/// que es exactamente lo que hay que hacer, y el reloj corría en su contra.
///
/// LO IMPORTANTE NO ES QUE EL PLAZO FUERA CORTO. Antes del micrófono automático nadie se paraba a
/// hablar y siete segundos sobraban: el cambio no rompió una pieza, cambió las condiciones bajo las
/// que esa pieza era suficiente. Alargar el plazo lo habría hecho fallar solo de vez en cuando —que
/// es peor, porque entonces ya no se diagnostica.
///
/// LA SEÑAL BUENA NO ES «PASARON N SEGUNDOS», ES «YA PUSISTE DELANTE LA APP». Eso se espera y se
/// dice mientras se espera; no se cronometra. Queda un techo, y solo por una razón: un 🎓 pulsado
/// sin querer no puede dejar un hilo mirando el escritorio para siempre.
///
/// LA COMPUERTA QUE HAY DEBAJO NO SE TOCA y sigue siendo la razón de todo esto: nadie puede enseñar
/// la ventana de Ü. Si se grabara con Ü delante, el detector elegiría UIA y una enseñanza de SAP por
/// UIA sale inservible —un paso por PULSACIÓN y clics sobre un Pane opaco, ya pasó con
/// wf_1785096110817 y wf_1785110820731—. Lo que cambia es qué se hace mientras eso no se cumple:
/// antes se abortaba, ahora se espera.
///
/// PURO: no mira ventanas ni duerme. Solo decide, para que el contrato pueda juzgar el caso exacto
/// que falló sin necesitar una pantalla.
/// </remarks>
public static class ElArranqueDeLaDemo
{
    /// <param name="Empezar">Ya se puede grabar: hay otra app delante.</param>
    /// <param name="Seguir">Todavía no, pero hay que seguir esperando.</param>
    /// <param name="Decir">Qué se le cuenta al humano. Nunca vacío, y nunca dice «grabando»
    /// mientras no lo esté: un mensaje que promete lo que no ocurre hace que alguien se ponga a
    /// hacer la tarea sin que nadie la mire.</param>
    public readonly record struct Paso(bool Empezar, bool Seguir, string Decir);

    /// <summary>Cuánto se espera como mucho a que el humano cambie de ventana.</summary>
    /// <remarks>
    /// Un minuto, y es generoso a propósito: el coste de esperar de más es un hilo sondeando cada
    /// 150 ms, y el de esperar de menos es tener que volver a empezar la demo entera — que es lo
    /// que pasó dos veces el 2026-09-03.
    /// </remarks>
    public const int TechoPorDefectoMs = 60000;

    /// <summary>
    /// ¿Se empieza, se sigue esperando, o se deja?
    /// </summary>
    /// <param name="primerPlanoEsNuestro">Si la ventana de delante es la de Ü.</param>
    /// <param name="esperadoMs">Cuánto se lleva esperando.</param>
    /// <param name="techoMs">Cuánto se espera como mucho.</param>
    /// <summary>¿Esa clase de ventana es el escritorio de Windows o su barra? Nadie enseña el escritorio.</summary>
    /// <remarks>«Progman» y «WorkerW» son el fondo del escritorio; «Shell_TrayWnd» la barra de tareas.
    /// Con cualquiera de ellas delante, el detector de superficie elige UIA y una demo de SAP sale
    /// inservible (cuarta prueba real, 2026-09-07).</remarks>
    public static bool EsEscritorio(string claseDeVentana) => (claseDeVentana ?? "").Trim() is "Progman" or "WorkerW" or "Shell_TrayWnd";

    /// <summary>Como <see cref="Juzgar(bool, int, int)"/>, pero el escritorio delante tampoco es «la app delante».</summary>
    public static Paso Juzgar(bool primerPlanoEsNuestro, bool primerPlanoEsElEscritorio, int esperadoMs, int techoMs)
    {
        if (!primerPlanoEsNuestro && primerPlanoEsElEscritorio)
        {
            if (esperadoMs >= techoMs)
                return new(false, false,
                    "Delante solo estuvo el escritorio, así que no grabé nada. Abre la aplicación que "
                    + "vas a enseñar, ponla delante y vuelve a pulsar 🎓.");
            return new(false, true, "Veo el escritorio. Pon delante la aplicación que vas a enseñar; te espero.");
        }
        return Juzgar(primerPlanoEsNuestro, esperadoMs, techoMs);
    }

    public static Paso Juzgar(bool primerPlanoEsNuestro, int esperadoMs, int techoMs)
    {
        if (!primerPlanoEsNuestro)
            return new(true, false, "Ya te veo. Enséñame: hago lo que hagas tú.");

        if (esperadoMs >= techoMs)
            return new(false, false,
                "No llegaste a poner delante la aplicación que ibas a enseñar, así que no grabé "
                + "nada. Ponla delante y vuelve a pulsar 🎓 — a mi propia ventana no puedo mirarla.");

        // ESPERANDO, Y SE DICE QUE SE ESPERA. Los primeros segundos el humano suele estar todavía
        // hablando con Ü, así que el mensaje no mete prisa: dice qué falta y que no hay reloj.
        return new(false, true, esperadoMs < 4000
            ? "Cuando quieras, pon delante la aplicación que vas a enseñar. Te espero."
            : "Sigo esperando a que pongas delante la aplicación que vas a enseñar. Sin prisa: "
              + "no empiezo hasta que la vea.");
    }
}
