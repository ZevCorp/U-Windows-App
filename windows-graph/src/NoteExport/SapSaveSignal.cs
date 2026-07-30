using U.Graph.Surfaces;

namespace U.Graph.NoteExport;

/// <summary>
/// Traduce lo que SAP dijo al terminar el guardado en el desenlace que exige el contrato.
///
/// Está separada del ejecutor y no toca COM a propósito: recibe los mensajes YA leídos, así que es
/// la única parte del camino «SAP → outcome» que se puede probar entera sin SAP, sin Windows y sin
/// red. Es también donde vive la regla que sostiene todo el producto: <c>ok</c> exige la señal de
/// éxito de SAP, y «no falló» no es esa señal.
/// </summary>
public static class SapSaveSignal
{
    // Tipos de mensaje de SAP. S=éxito · E=error de validación · A=aborto · W=advertencia · I=info.
    public const string Success = "S";
    public const string Error = "E";
    public const string Abort = "A";

    /// <summary>
    /// Decide el desenlace.
    ///
    /// <paramref name="baseline"/> es lo que la barra de estado mostraba ANTES de ejecutar. Sin esa
    /// comparación, un «Documento grabado» que llevaba una hora en pantalla se leería como el éxito
    /// de este guardado: el fallo más peligroso posible, porque produce un «exportada» falso y nadie
    /// vuelve a mirar. Comparar y descartar lo idéntico es barato.
    /// </summary>
    public static ExportDecision Classify(
        SapGuiSurface.SapStatusMessage? baseline,
        SapGuiSurface.SapStatusMessage? observed)
    {
        if (observed == null)
        {
            // Ausencia de señal NO es fallo de SAP ni éxito: es que no hay nada que verificar. Y sin
            // verificar no se reporta ok, que es justo el invariante del sistema.
            return ExportDecision.Fail(
                "SAP_SIN_CONFIRMACION",
                "El workflow terminó, pero SAP no publicó ningún mensaje en la barra de estado dentro " +
                "del plazo. Sin señal no se puede afirmar que la nota quedó guardada.");
        }

        if (baseline != null &&
            observed.Type == baseline.Type &&
            observed.Text == baseline.Text &&
            observed.Code == baseline.Code)
        {
            return ExportDecision.Fail(
                "SAP_SIN_CONFIRMACION",
                "El único mensaje de la barra de estado es el que ya estaba antes de ejecutar: es una " +
                "señal vieja y no confirma este guardado.",
                detailCode: "MENSAJE_PREVIO");
        }

        if (observed.Type == Success)
            return ExportDecision.Proceed(); // el caller construye el ok con su folio

        if (observed.Type == Error)
        {
            // Un mensaje E tras intentar guardar es, casi siempre, una validación de negocio: falta un
            // campo obligatorio, el caso no admite la nota, la fecha está fuera de rango. Eso no se
            // arregla reintentando lo mismo — lo tiene que resolver una persona. Por eso needs_doctor
            // y no error: en Miracle Notes el médico verá «Requiere acción» en vez de «reintenta».
            //
            // El TEXTO del mensaje no viaja: puede nombrar al paciente. Viaja el código (V1-311), que
            // identifica el mensaje sin su contenido y le sirve al implantador para saber cuál fue.
            string code = observed.Code.Length > 0 ? observed.Code : "sin código";
            return ExportDecision.NeedsDoctor(
                $"SAP rechazó el guardado con un mensaje de validación ({code}).",
                $"Validación del sistema hospitalario ({code})");
        }

        if (observed.Type == Abort)
        {
            return ExportDecision.Fail(
                "SAP_ABORTO",
                "SAP abortó la transacción. Es un fallo técnico, no una validación del contenido.",
                detailCode: observed.Code);
        }

        // W (advertencia) e I (información) no confirman un guardado. Tratarlos como éxito sería
        // exactamente el «parece que funcionó» que este sistema existe para evitar.
        return ExportDecision.Fail(
            "SAP_SIN_CONFIRMACION",
            $"SAP respondió con un mensaje de tipo «{observed.Type}», que no confirma que la nota se " +
            "haya guardado.",
            detailCode: observed.Code.Length > 0 ? observed.Code : observed.Type);
    }

    /// <summary>
    /// El folio dentro del mensaje de éxito: se queda con el token más largo que contenga dígitos
    /// («Documento 4711 grabado» → «4711»).
    ///
    /// Es TRAZABILIDAD, no la prueba del éxito — la prueba es el tipo S del mensaje. Miracle Notes
    /// lo pinta como «Exportada a la historia clínica · folio 4711», así que sirve para que un humano
    /// encuentre el registro después; si la heurística toma un token que no es el folio, el
    /// desenlace sigue siendo correcto y solo la etiqueta es menos útil. El texto completo se queda
    /// en el registro local, que es donde puede haber PHI.
    /// </summary>
    public static string FolioFrom(string text)
    {
        string best = "";
        foreach (string raw in (text ?? "").Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string token = raw.Trim('.', ',', ';', ':', '(', ')', '[', ']', '«', '»', '"', '\'');
            if (token.Length < 3 || token.Length > 40) continue;
            if (!token.Any(char.IsDigit)) continue;
            if (token.Length > best.Length) best = token;
        }
        return best;
    }
}
