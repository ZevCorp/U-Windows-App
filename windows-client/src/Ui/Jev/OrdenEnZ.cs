using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Ui.Jev;

/// <summary>Las capas de Ü que no pueden quedarse debajo de otras aplicaciones. Su orden lo da <see cref="OrdenEnZ.Capas"/>.</summary>
public enum Capa
{
    /// <summary>Las cajas de Jev: una ventana por monitor (379).</summary>
    Overlays,

    /// <summary>El muelle, que es el anfitrión de la carita (promesa 272).</summary>
    Carita,

    /// <summary>El notch, arriba al centro.</summary>
    Notch,

    /// <summary>El panel de Jev.</summary>
    PanelDeJev,

    /// <summary>La flecha que vuela a lo pulsado.</summary>
    Flecha,
}

/// <summary>
/// EL ORDEN EN Z DE LAS CAPAS DE Ü, de abajo arriba. Promesa 378 (spec 049).
/// </summary>
/// <remarks>
/// LAS CAJAS VAN DEBAJO DEL PANEL, al revés que TipTour y a propósito (spec 049 §Diseño): el panel es opaco y lo que
/// tapa no se pulsa, así que una caja pintada encima de él señalaría algo que no se puede tocar. Si el dueño quiere
/// el calco del vídeo, es mover un elemento de esta lista.
///
/// LA FLECHA, ARRIBA DEL TODO: es lo único que se mueve, y una flecha que cruza por detrás del panel o del notch
/// camino de lo pulsado desaparece a mitad de vuelo.
/// </remarks>
public static class OrdenEnZ
{
    public static IReadOnlyList<Capa> Capas { get; } =
        Array.AsReadOnly(new[] { Capa.Overlays, Capa.Carita, Capa.Notch, Capa.PanelDeJev, Capa.Flecha });
}

/// <summary>
/// UN SOLO VIGILANTE PARA TODAS LAS CAPAS DE Ü. Promesa 378 (spec 049): sube las ventanas del grupo en el orden de
/// <see cref="OrdenEnZ.Capas"/>, una vez por ventana visible, y reordena en el acto cuando una del grupo se muestra.
/// </summary>
/// <remarks>
/// POR QUÉ UNO Y NO UNO POR VENTANA. Hasta la 049 cada capa que no podía quedarse debajo —el muelle y el notch— llevaba
/// su propio reloj de 3 s que la volvía a subir (2 sitios en todo el cliente: los constructores de <c>Muelle</c> y de
/// <c>PanelDeAcciones</c>).
/// <c>HWND_TOPMOST</c> pone la ventana ARRIBA de la banda topmost, así que con un reloj por ventana el orden entre las
/// nuestras lo decide cuál tocó la última; con overlay, panel y flecha serían cinco relojes, y el panel de Jev quedaría
/// debajo de sus propias cajas un rato sí y otro no. Subirlas todas en un recorrido, de abajo arriba, deja la última
/// encima y fija el orden entero.
///
/// PURO, CON EL SUBIR INYECTADO: el contrato lo juzga con handles de mentira y un subir que anota; el
/// <c>SetWindowPos</c> y el reloj los pone <see cref="SiempreDelante"/>.
///
/// LO QUE FALLA EN UNA VENTANA NO PARA A LAS DEMÁS, Y SE DICE. El vigilante de antes se tragaba el fallo con un catch
/// mudo (patrón nº3). Aquí va al log con su tipo, su mensaje, el paso —preguntar si se ve o subirla—, la capa y el
/// handle, y UNA vez mientras falle igual: el reloj pasa cada 3 s, y un fallo que se repite serían 1.200 líneas por
/// hora tapando el resto del log. Cuando vuelve a subir, la siguiente vez que falle se dice otra vez.
///
/// UN SOLO HILO. Lo llaman el reloj del despachador y los avisos de las ventanas, todos en el hilo de la interfaz: no
/// hay candado. Se recorre una copia del grupo para que una ventana que entre o salga desde dentro de un subir no rompa
/// el recorrido.
/// </remarks>
public sealed class VigilanteEnOrden
{
    private sealed class Miembro
    {
        public Miembro(IntPtr handle, Capa capa, Func<bool> visible) { Handle = handle; Capa = capa; Visible = visible; }

        public IntPtr Handle { get; }
        public Capa Capa { get; set; }
        public Func<bool> Visible { get; set; }

        /// <summary>El último fallo que se dijo en el log, para no repetirlo en cada tick.</summary>
        public string? FalloDicho { get; set; }
    }

    private readonly Action<IntPtr> _subir;
    private readonly List<Miembro> _grupo = new();

    /// <param name="subir">Lo que pone una ventana arriba de la banda topmost: <c>SetWindowPos</c> en la app.</param>
    public VigilanteEnOrden(Action<IntPtr> subir) => _subir = subir ?? throw new ArgumentNullException(nameof(subir));

    /// <summary>
    /// Mete una ventana en el grupo, en su capa. La misma ventana dos veces es una: se queda en su sitio con la capa y
    /// la <paramref name="visible"/> de la última vez, y no se sube dos veces por tick.
    /// </summary>
    public void Registra(IntPtr handle, Capa capa, Func<bool> visible)
    {
        ArgumentNullException.ThrowIfNull(visible);
        var ya = _grupo.Find(m => m.Handle == handle);
        if (ya != null) { ya.Capa = capa; ya.Visible = visible; return; }
        _grupo.Add(new Miembro(handle, capa, visible));
    }

    /// <summary>
    /// Saca una ventana del grupo: la que se cierra. Sin esto, cada vez que Jev se apaga y se enciende (383) quedarían
    /// tres entradas más sujetando ventanas cerradas, y el reloj preguntaría por ellas para siempre.
    /// </summary>
    /// <returns>Si estaba.</returns>
    public bool Sale(IntPtr handle) => _grupo.RemoveAll(m => m.Handle == handle) > 0;

    /// <summary>Lo que hace el reloj: sube todo el grupo en orden.</summary>
    public void Tick() => SubirEnOrden();

    /// <summary>
    /// Una ventana se acaba de mostrar: si es del grupo, se reordena en el acto, sin esperar al reloj. Una que no es
    /// del grupo —o que ya salió— no mueve nada.
    /// </summary>
    public void AlMostrar(IntPtr handle)
    {
        if (!_grupo.Exists(m => m.Handle == handle)) return;
        SubirEnOrden();
    }

    private void SubirEnOrden()
    {
        var copia = _grupo.ToList();
        foreach (var capa in OrdenEnZ.Capas)
            foreach (var m in copia)
            {
                if (m.Capa != capa) continue;
                string paso = "preguntar si se ve";
                try
                {
                    // Escondida no se toca: subir una ventana invisible no arregla nada y en cambio puede sacarla
                    // del orden en que la dejó quien la escondió.
                    if (!m.Visible()) continue;
                    paso = "subirla";
                    _subir(m.Handle);
                    m.FalloDicho = null;
                }
                catch (Exception e)
                {
                    // LA CADENA ENTERA (patrón nº3), y el paso que falló: no es lo mismo no poder preguntar si se ve
                    // que no poder subirla.
                    string fallo = paso + ": " + Cadena(e);
                    if (fallo == m.FalloDicho) continue;
                    m.FalloDicho = fallo;
                    LogBus.Log("orden-z", $"✘ la ventana 0x{m.Handle.ToInt64():X} ({m.Capa}) no se pudo {fallo}. "
                        + "Las demás capas suben igual; mientras falle así no se repite esta línea.");
                }
            }
    }

    private static string Cadena(Exception e)
    {
        string causa = "";
        for (var x = e; x != null; x = x.InnerException)
            causa += $"{x.GetType().Name}: {x.Message}" + (x.InnerException != null ? " ← " : "");
        return causa;
    }
}
