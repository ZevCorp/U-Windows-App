using System.Windows;

namespace U.WindowsClient.Ui.Jev;

/// <summary>
/// QUÉ SE VE DE JEV AHORA MISMO, sin ninguna ventana: encendido, overlay, tramo, lo visible y dónde va el panel.
/// Promesas 379, 383, 384 y 385 (spec 049). Las ventanas (fase 8) solo enseñan y esconden lo que esto dice, y los
/// ganchos de Windows (Escape, soltar lo señalado, el botón de Jev) solo llaman a sus métodos.
/// </summary>
/// <remarks>
/// NADA DE JEV SE VE CON JEV APAGADO. La mano pulsa también cuando decide Luna, y <c>UiaSurface.Pulso</c> avisa
/// en cada clic: si una pulsación encendiera la flecha o el panel con Jev apagado, la flecha volaría a los clics de
/// Luna y el panel enseñaría una corrida que Jev no decidió. Por eso todo lo visible pasa por
/// <see cref="Encendida"/>, y solo el tramo se anota siempre —empezó aunque decidiera Luna, y si Jev se enciende a
/// mitad, los pasos que quedan los decide Jev—.
///
/// EL PANEL NO SE PONE A OJO. Su sitio lo calcula <see cref="DondeVaElPanel"/> (377) al encender y cada vez que se
/// conoce lo pulsado (385); sin área de trabajo ni tamaño no hay sitio que calcular y <see cref="RectDelPanel"/>
/// queda <c>null</c>: un rect por defecto diría «estoy donde no estorbo» sin haberlo mirado (aprendizaje nº4). Una
/// escala que no es escala la rechaza <see cref="DondeVaElPanel.Calcular"/> con su guarda, que es la misma de los
/// otros tres sitios que reciben una: aquí no se copia una cuarta.
///
/// VIVE EN EL HILO QUE PINTA. La llaman el pintor del conector y los ganchos de la ventana, todos en el hilo de la
/// interfaz; no es segura entre hilos y no lo intenta.
/// </remarks>
public sealed class MaquinaDeLaVista
{
    private bool _tramoEnMarcha;
    private bool _flechaVuela;
    private int _cajas;
    private string? _objetivo;

    /// <param name="configuracion">La de <see cref="ConfiguracionDeLaVista.Leer"/>: decide si hay overlay.</param>
    public MaquinaDeLaVista(ConfiguracionDeLaVista configuracion) =>
        Configuracion = configuracion ?? throw new ArgumentNullException(nameof(configuracion));

    /// <summary>Con qué se decidió el overlay.</summary>
    public ConfiguracionDeLaVista Configuracion { get; }

    /// <summary>Si Jev está encendido: lo pone <see cref="Encender"/> y lo quita <see cref="Apagar"/>.</summary>
    public bool Encendida { get; private set; }

    /// <summary>Si el overlay se enciende con Jev: lo dice la configuración, no el botón (379).</summary>
    public bool OverlayEncendido => Configuracion.OverlayEncendido;

    /// <summary>Lo que la línea de estado dice del overlay: «overlay: apagado (sin U_JEV_OVERLAY)» (379).</summary>
    public string Estado => $"overlay: {(OverlayEncendido ? "encendido" : "apagado")} ({Configuracion.Motivo})";

    /// <summary>
    /// Si corre un tramo CON JEV: el que <see cref="ReglaDeQuienVuela.LaCaritaViaja"/> recibe (384). Lo termina el
    /// tramo (<see cref="AlTerminarTramo"/>) o apagar Jev; soltar lo señalado no, porque el tramo sigue —Escape lo
    /// para él, y cuando para, avisa—.
    /// </summary>
    public bool EnTramo => Encendida && _tramoEnMarcha;

    /// <summary>Si la ventana del overlay se enseña: Jev encendido y el overlay encendido por configuración.</summary>
    public bool OverlayVisible => Encendida && OverlayEncendido;

    /// <summary>Si el panel se enseña: nace al encender Jev y muere al apagarlo (383, 385).</summary>
    public bool PanelVisible => Encendida;

    /// <summary>
    /// Si la flecha vuela o señala a lo pulsado. Solo con Jev encendido y con la app de trabajo delante (381); Escape,
    /// soltar y apagar la esconden. El modo «siguiendo» del plano (la flecha junto al ratón en reposo) no está aquí.
    /// </summary>
    public bool FlechaVisible => Encendida && _flechaVuela;

    /// <summary>Cuántas cajas pinta el overlay: 0 si el overlay no se ve, que tres cajas sobre una ventana que no existe mienten.</summary>
    public int CajasVisibles => OverlayVisible ? _cajas : 0;

    /// <summary>Si el panel enseña una corrida —su objetivo y cómo va o cómo acabó— en vez de estar en reposo.</summary>
    public bool PanelEnCorrida => PanelVisible && _objetivo != null;

    /// <summary>
    /// Dónde va el panel, en físicos del escritorio virtual: lo que recibe <c>SetWindowPos</c>. <c>null</c> mientras
    /// no se sepa la pantalla o con Jev apagado.
    /// </summary>
    public Rect? RectDelPanel { get; private set; }

    /// <summary>El área de trabajo del monitor que manda, en físicos. Vacía hasta que la vista la ponga.</summary>
    public Rect RcWork { get; set; } = Rect.Empty;

    /// <summary>La escala de ese monitor (DPI / 96).</summary>
    public double Escala { get; set; }

    /// <summary>El panel en físicos: <see cref="MedidaDelPanelDeJev"/> por la escala. Vacío hasta que la vista lo ponga.</summary>
    public Size TamanoDelPanel { get; set; } = Size.Empty;

    /// <summary>Lo que sitúa el panel (la carita), en físicos.</summary>
    public Point Ancla { get; set; }

    /// <summary>Lo que el panel no cruza si alguna esquina cabe: el notch de <see cref="ReglaDeLaBandeja.ArribaAlCentro"/>, en físicos.</summary>
    public IReadOnlyList<Rect> Obstaculos { get; set; } = Array.Empty<Rect>();

    /// <summary>
    /// Se enciende Jev: el panel nace ya en su sitio y, si la configuración lo dice, el overlay también. Nace LIMPIO
    /// —sin cajas ni flecha de cuando decidía Luna— y con corrida solo si hay un tramo en marcha, que desde ahora
    /// decide Jev; uno que ya terminó no se enseña como si fuera suyo. Encender lo encendido no cambia nada.
    /// </summary>
    public void Encender()
    {
        if (Encendida) return;
        Encendida = true;
        _cajas = 0;
        _flechaVuela = false;
        if (!_tramoEnMarcha) _objetivo = null;
        Recolocar(objetivo: null);
    }

    /// <summary>
    /// Se apaga Jev: las tres ventanas se cierran, y no queda ni caja, ni flecha, ni corrida a la vista (383). El
    /// tramo sigue anotado —lo termina él—, por si Jev se vuelve a encender antes de que acabe.
    /// </summary>
    public void Apagar()
    {
        Encendida = false;
        _flechaVuela = false;
        _cajas = 0;
        RectDelPanel = null;
    }

    /// <summary>
    /// Escape (<c>Freno.SePulso</c>) o se soltó lo señalado (<c>Senalador.Suelta</c>): el overlay se vacía, la
    /// flecha se esconde y el panel queda sin corrida, pero sigue a la vista con Jev encendido (383).
    /// </summary>
    public void Suelta()
    {
        _cajas = 0;
        _flechaVuela = false;
        _objetivo = null;
    }

    /// <summary>Empieza un tramo. Se anota aunque Jev esté apagado: si se enciende a mitad, el tramo ya es suyo.</summary>
    public void AlEmpezarTramo(string objetivo)
    {
        _tramoEnMarcha = true;
        _objetivo = objetivo ?? "";
    }

    /// <summary>
    /// Termina el tramo. El panel se queda con la corrida —enseña cómo acabó, con el motivo tal cual (374)— hasta
    /// Escape o soltar.
    /// </summary>
    public void AlTerminarTramo() => _tramoEnMarcha = false;

    /// <summary>El overlay pintó estas cajas (<see cref="CajasDelOverlay.Cajas"/>).</summary>
    public void AlPintarCajas(int cajas) => _cajas = cajas;

    /// <summary>
    /// La mano dijo qué pulsó (<c>UiaSurface.Pulso</c>): la flecha vuela si la app de trabajo está delante, y el
    /// panel se aparta de lo pulsado en cuanto lo conoce (385). Con Jev apagado no pasa nada: pulsó Luna.
    /// </summary>
    /// <param name="caja">La caja de lo pulsado, en físicos.</param>
    /// <param name="appDelante">
    /// Si la ventana de trabajo es la de delante, preguntado a Windows (<c>GetForegroundWindow</c>, 381) por quien
    /// llama. Detrás de otra, no hay vuelo: la flecha cruzaría una ventana que no es la del clic.
    /// </param>
    public void AlConocerPulsada(Rect caja, bool appDelante)
    {
        if (!Encendida) return;
        _flechaVuela = appDelante;
        Recolocar(caja);
    }

    private void Recolocar(Rect? objetivo)
    {
        if (!Encendida || RcWork.IsEmpty || TamanoDelPanel.IsEmpty) { RectDelPanel = null; return; }
        RectDelPanel = DondeVaElPanel.Calcular(Ancla, TamanoDelPanel, RcWork, Escala, Obstaculos, objetivo).Rect;
    }
}
