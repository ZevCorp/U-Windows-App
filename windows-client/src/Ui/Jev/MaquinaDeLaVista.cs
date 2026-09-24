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
    private Rect? _pulsada;
    private string? _overlayImpedido;

    /// <param name="configuracion">La de <see cref="ConfiguracionDeLaVista.Leer"/>: decide si hay overlay.</param>
    public MaquinaDeLaVista(ConfiguracionDeLaVista configuracion) =>
        Configuracion = configuracion ?? throw new ArgumentNullException(nameof(configuracion));

    /// <summary>Con qué se decidió el overlay.</summary>
    public ConfiguracionDeLaVista Configuracion { get; }

    /// <summary>Si Jev está encendido: lo pone <see cref="Encender"/> y lo quita <see cref="Apagar"/>.</summary>
    public bool Encendida { get; private set; }

    /// <summary>Si el overlay se enciende con Jev: lo dice la configuración, no el botón (379).</summary>
    public bool OverlayEncendido => Configuracion.OverlayEncendido;

    /// <summary>
    /// Lo que la línea de estado dice del overlay: «overlay: apagado (sin U_JEV_OVERLAY)» (379), o «impedido» con
    /// quién lo impide (371): «encendido» con el inspector delante sería el estado que miente.
    /// </summary>
    public string Estado => !OverlayEncendido
        ? $"overlay: apagado ({Configuracion.Motivo})"
        : _overlayImpedido != null
            ? $"overlay: impedido ({Configuracion.Motivo}, pero {_overlayImpedido})"
            : $"overlay: encendido ({Configuracion.Motivo})";

    /// <summary>
    /// Si corre un tramo CON JEV: el que <see cref="ReglaDeQuienVuela.LaCaritaViaja"/> recibe (384). Lo termina el
    /// tramo (<see cref="AlTerminarTramo"/>) o apagar Jev; soltar lo señalado no, porque el tramo sigue —Escape lo
    /// para él, y cuando para, avisa—.
    /// </summary>
    public bool EnTramo => Encendida && _tramoEnMarcha;

    /// <summary>
    /// Si la ventana del overlay se enseña: Jev encendido, el overlay encendido por configuración y nada que lo
    /// impida (<see cref="ImpedirElOverlay"/>, 371).
    /// </summary>
    public bool OverlayVisible => Encendida && OverlayEncendido && _overlayImpedido == null;

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

    /// <summary>
    /// LA CARITA ENTERA, en físicos: lo que el panel no tapa (377, revisión del 2026-09-23). La vista pone el rect de
    /// la ventana de la carita, con su barra y su menú; con el centro solo, el panel caía encima de ella.
    /// </summary>
    public Rect Carita { get; set; } = new(0, 0, 0, 0);

    /// <summary>
    /// El centro de la carita. Ponerlo pone una carita de 0×0 en ese punto: el ancla-punto de
    /// <see cref="DondeVaElPanel.Calcular"/>, con la que el sitio sale igual que antes de <see cref="Carita"/>.
    /// </summary>
    public Point Ancla
    {
        get => new(Carita.X + Carita.Width / 2, Carita.Y + Carita.Height / 2);
        set => Carita = new Rect(value, new Size(0, 0));
    }

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
        _pulsada = null;
        _overlayImpedido = null;   // cada encendido vuelve a preguntar (371)
        if (!_tramoEnMarcha) _objetivo = null;
        Recolocar(objetivo: null);
    }

    /// <summary>
    /// EL OVERLAY NO SE ENCIENDE aunque la configuración lo pida: el inspector está encendido y comparten ámbar, verde
    /// y rosa con otro significado (371). Lo llama la vista al encender, después de preguntar a
    /// <see cref="ExclusionConElInspector"/>; hasta el siguiente <see cref="Encender"/>, el overlay no se da por
    /// visible ni cuenta cajas, y <see cref="Estado"/> dice quién lo impide.
    /// </summary>
    /// <exception cref="ArgumentException">Sin porqué: «impedido» a secas no dice por quién (patrón nº2).</exception>
    public void ImpedirElOverlay(string porque)
    {
        if (string.IsNullOrWhiteSpace(porque))
            throw new ArgumentException("impedir el overlay lleva su porqué: «impedido» a secas no dice por quién (patrón nº2)", nameof(porque));
        _overlayImpedido = porque.Trim();
        _cajas = 0;
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
        _pulsada = null;
        _overlayImpedido = null;
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
        _pulsada = null;
    }

    /// <summary>
    /// LO QUE EL PANEL PINTA DE ESTE CICLO, o <c>null</c> si nada (383, revisión del 2026-09-23). En corrida, el ciclo
    /// tal cual. Sin corrida —tras Escape o soltar, o antes del primer tramo— solo el motivo: la línea sola, sin
    /// objetivo, sin decisión y sin barras (plano, «Parado a mano»: se borran). Hasta el 2026-09-23 la vista pintaba
    /// todo lo que llegaba con Jev encendido, y la línea con que el tramo para —que llega DESPUÉS de Escape,
    /// <c>ElTramo.cs:191</c>— volvía a enseñar el objetivo y las cinco barras sobre un panel que la máquina decía sin
    /// corrida. Una decisión que llega tarde, tras Escape, tampoco se pinta.
    /// </summary>
    public CicloDeJev? QueSePinta(CicloDeJev ciclo)
    {
        ArgumentNullException.ThrowIfNull(ciclo);
        if (!Encendida) return null;
        if (PanelEnCorrida) return ciclo;
        return ciclo.Fase == FaseDelCiclo.Linea && !string.IsNullOrWhiteSpace(ciclo.Linea)
            ? new CicloDeJev { Fase = FaseDelCiclo.Linea, Linea = ciclo.Linea }
            : null;
    }

    /// <summary>Empieza un tramo. Se anota aunque Jev esté apagado: si se enciende a mitad, el tramo ya es suyo.</summary>
    public void AlEmpezarTramo(string objetivo)
    {
        _tramoEnMarcha = true;
        _objetivo = objetivo ?? "";
        _pulsada = null;
    }

    /// <summary>
    /// Termina el tramo. El panel se queda con la corrida —enseña cómo acabó, con el motivo tal cual (374)— hasta
    /// Escape o soltar.
    /// </summary>
    public void AlTerminarTramo() => _tramoEnMarcha = false;

    /// <summary>El overlay pintó estas cajas (<see cref="CajasDelOverlay.Cajas"/>).</summary>
    public void AlPintarCajas(int cajas) => _cajas = cajas;

    /// <summary>
    /// EL PANEL PINTA ESTAS BARRAS, y su sitio se calcula con el alto que tiene ahora (385, revisión del 2026-09-23):
    /// <see cref="MedidaDelPanelDeJev.AltoDe"/> por la escala, y recolocado con lo pulsado que ya se conocía. Hasta
    /// entonces el sitio se calculaba siempre con el alto del panel vacío (64) y el panel crecía a 198,6 hacia abajo:
    /// puesto encima de la carita, al crecer la tapaba, se salía del área de trabajo o caía sobre lo pulsado.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Con menos de 0 o más de 5 barras: lo lanza <see cref="MedidaDelPanelDeJev.AltoDe"/>.</exception>
    public void AlPintarBarras(int barras)
    {
        double alto = MedidaDelPanelDeJev.AltoDe(barras);
        // Sin escala no hay tamaño en físicos que calcular: se queda el que hubiera, y Recolocar da null sin pantalla.
        if (double.IsFinite(Escala) && Escala > 0)
            TamanoDelPanel = new Size(MedidaDelPanelDeJev.Ancho * Escala, alto * Escala);
        Recolocar(_pulsada);
    }

    /// <summary>
    /// La mano dijo qué pulsó (<c>UiaSurface.Pulso</c>): la flecha vuela si la app de trabajo está delante, y el
    /// panel se aparta de lo pulsado en cuanto lo conoce (385). Fuera de un tramo con Jev no pasa nada: pulsó Luna.
    /// </summary>
    /// <param name="caja">La caja de lo pulsado, en físicos.</param>
    /// <param name="appDelante">
    /// Si la ventana de trabajo es la de delante, preguntado a Windows (<c>GetForegroundWindow</c>, 381) por quien
    /// llama. Detrás de otra, no hay vuelo: la flecha cruzaría una ventana que no es la del clic.
    /// </param>
    public void AlConocerPulsada(Rect caja, bool appDelante)
    {
        // FUERA DE UN TRAMO CON JEV, LA PULSACIÓN ES DE LUNA (o del player), no de Jev: ni flecha, ni panel que se
        // aparte (384, revisión del 2026-09-23). Hasta entonces se miraba «encendida», y la carita —que mira «en
        // tramo»— viajaba al mismo clic al que volaba la flecha: dos cuerpos cruzando la pantalla por un clic.
        if (!EnTramo) return;
        _flechaVuela = appDelante;
        _pulsada = caja;
        Recolocar(caja);
    }

    private void Recolocar(Rect? objetivo)
    {
        if (!Encendida || RcWork.IsEmpty || TamanoDelPanel.IsEmpty) { RectDelPanel = null; return; }
        RectDelPanel = DondeVaElPanel.JuntoALaCarita(Carita, TamanoDelPanel, RcWork, Escala, Obstaculos, objetivo).Rect;
    }
}
