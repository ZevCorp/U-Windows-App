using U.Graph.Surfaces;

namespace U.WindowsClient.Navigation;

/// <summary>
/// EL DESPACHO ENTRE MUNDOS: cada superficie se observa por su propia puerta y se pulsa por su
/// propia mano. Hoy hay dos mundos —UIA y SAP— y la decisión de cuál toca es todo lo que vive aquí.
/// </summary>
/// <remarks>
/// EXISTE POR EL HUECO QUE DEJABA A SAP FUERA DEL TERRENO (T1 del plan terreno-profundo,
/// 2026-08-25): `MapaVivo` observaba SIEMPRE con el lector UIA, y dentro de una ventana SAP el
/// sistema operativo ve un Pane opaco — el grafo aprendía los 12 elementos del marco y ninguno de
/// la sesión. La Scripting API de SAP y su vocabulario de identidad (`sap:wnd[0]/…`,
/// <see cref="SapSelector"/>) ya estaban construidos y probados; faltaba el despacho.
///
/// PURO A PROPÓSITO: las dos clases reciben las puertas como delegados y solo DECIDEN. Ni COM ni
/// UIA aquí — así el contrato juzga la decisión con fakes (promesas 68 y 69) sin abrir pantalla.
/// </remarks>
public sealed class SentidoPorMundo
{
    private readonly Func<List<Nucleo.Elemento>> _uia;
    private readonly Func<List<Nucleo.Elemento>> _sap;

    public SentidoPorMundo(Func<List<Nucleo.Elemento>> uia, Func<List<Nucleo.Elemento>> sap)
    {
        _uia = uia;
        _sap = sap;
    }

    /// <summary>
    /// Lo visible en esta ubicación, mirando por la puerta del mundo al que pertenece.
    /// </summary>
    /// <remarks>
    /// LA UBICACIÓN DECIDE, no el proceso: el MARCO de SAP Logon es `uia://saplogon.exe/…` y se
    /// lee por UIA como cualquier ventana; solo DENTRO de una sesión (`sapgui://…`) manda el
    /// scripting. Decidir por proceso habría dejado ciego el Logon mismo.
    /// </remarks>
    public List<Nucleo.Elemento> Lee(string ubicacion) =>
        (ubicacion ?? "").StartsWith("sapgui://", StringComparison.OrdinalIgnoreCase)
            ? _sap()
            : _uia();
}

public sealed class ManoPorMundo
{
    private readonly Func<string, string, bool> _uia;
    private readonly Func<string, string, bool> _sap;

    public ManoPorMundo(Func<string, string, bool> uia, Func<string, string, bool> sap)
    {
        _uia = uia;
        _sap = sap;
    }

    /// <summary>
    /// Pulsar por la mano que entiende el selector. Devuelve si se pudo tocar.
    /// </summary>
    /// <remarks>
    /// EL SELECTOR DECIDE, no la ubicación: el selector ES la identidad y lleva escrito de qué
    /// mundo viene (<see cref="SapSelector.Owns"/> reconoce `sap:…`, con fragmento o sin él).
    /// Mandarle un selector SAP a la mano UIA sería pedirle a Windows algo que no ve — fallaría en
    /// silencio o, peor, acertaría sobre otra cosa.
    /// </remarks>
    public bool Pulsa(string selector, string etiqueta) =>
        SapSelector.Owns(selector ?? "")
            ? _sap(selector!, etiqueta)
            : _uia(selector ?? "", etiqueta);
}

/// <summary>
/// LA TRADUCCIÓN de lo que la Scripting API devuelve al vocabulario del núcleo. Es donde se decide
/// qué es PUERTA para el grafo — la misma pregunta que ParecePuerta contesta para la web.
/// </summary>
public static class SentidoSap
{
    /// <summary>
    /// Los tipos con los que un operador interactúa. Lo demás es decorado: un GuiLabel no se
    /// pulsa, y ofrecerlo como puerta sería la basura de la web otra vez (promesa 65).
    /// </summary>
    private static readonly HashSet<string> Puertas = new(StringComparer.OrdinalIgnoreCase)
    {
        "GuiTextField", "GuiCTextField", "GuiPasswordField", "GuiComboBox",
        "GuiCheckBox", "GuiRadioButton", "GuiButton", "GuiOkCodeField", "GuiTab",
    };

    public static List<Nucleo.Elemento> Traducir(IReadOnlyList<SapVisualElement> vistos)
    {
        var r = new List<Nucleo.Elemento>();
        foreach (var v in vistos ?? Array.Empty<SapVisualElement>())
        {
            if (v.Id.Length == 0) continue;

            // Una fila de árbol es puerta aunque su tipo sea el del shell: su identidad es el
            // árbol MÁS la clave del nodo, y el selector la lleva entera para ser autocontenido.
            if (v.IsNode && !string.IsNullOrEmpty(v.NodeKey))
            {
                r.Add(new Nucleo.Elemento(SapSelector.ByNode(v.Id, v.NodeKey!), v.Label, v.Type));
                continue;
            }

            if (!Puertas.Contains(v.Type)) continue;

            // EL CAMPO DE COMANDOS ENTRA CON NOMBRE. SAP no lo etiqueta (el describe-visual cae al
            // tipo, «GuiOkCodeField»), pero es la puerta a CUALQUIER transacción y el modelo tiene
            // que poder nombrarlo: «comando», que es lo que es para el operador.
            string etiqueta = v.Type.Equals("GuiOkCodeField", StringComparison.OrdinalIgnoreCase)
                ? "comando"
                : v.Label;

            r.Add(new Nucleo.Elemento(SapSelector.ById(v.Id), etiqueta, v.Type));
        }
        return r;
    }
}
