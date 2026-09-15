namespace U.WindowsClient.Navigation;

/// <summary>
/// LA VENTANA EN LA QUE Ü TRABAJA, distinta de la que la persona mira. Promesa 233 (spec 020).
/// </summary>
/// <remarks>
/// HASTA EL 2026-09-14 HABÍA UNA SOLA IDEA DE «DÓNDE ESTOY», y servía para dos cosas incompatibles.
/// La primera es aprender de la persona: el vigía atribuye cada clic humano al cambio de pantalla
/// que produjo, y ahí la pantalla es la que la persona mira. La segunda es ejecutar: buscar el
/// elemento, pulsarlo, comprobar la consecuencia, y ahí la pantalla tiene que ser la que Ü opera.
/// En cuanto la persona sigue trabajando dejan de coincidir, y Ü buscaba «Elipse» en la Vista de
/// tareas y «Cerrar» en Meet: «no pude pulsar «Elipse». Estás en «uia://explorer.exe/vista-de-tareas»».
///
/// ESTA CLASE ES LA SEGUNDA IDEA. Se fija cuando Ü abre o trae una app, cuando va a una pantalla, o
/// cuando un paso suyo cambió de pantalla y el sistema activó la nueva. Todo lo que Ü ejecuta se
/// resuelve respecto a ella. Sin ninguna fijada, Ü trabaja sobre el foco de la persona, que es
/// exactamente lo de siempre. Y si la que tenía desaparece —se cerró la Tienda—, lo dice UNA vez
/// por su nombre y vuelve al foco de la persona, en vez de seguir describiendo una pantalla cerrada.
///
/// Es también la base del escritorio virtual: allí la ventana de Ü nunca es la de delante, y un
/// diseño basado en el foco fracasa por construcción. Este no.
/// </remarks>
public sealed class VentanaDeTrabajo
{
    /// <summary>Dónde está Ü para ejecutar, y el aviso si su ventana acaba de desaparecer.</summary>
    public readonly record struct Donde(string Id, string Aviso);

    public IntPtr Hwnd { get; private set; }
    public string Id { get; private set; } = "";
    public bool Hay => Hwnd != IntPtr.Zero;

    private string _pendiente = "";

    public void Fijar(IntPtr hwnd, string id)
    {
        if (hwnd == IntPtr.Zero || string.IsNullOrWhiteSpace(id)) return;
        Hwnd = hwnd;
        Id = id.Trim();
    }

    public void Soltar()
    {
        Hwnd = IntPtr.Zero;
        Id = "";
    }

    /// <param name="existe">¿La ventana sigue existiendo y visible? Lo contesta Win32, inyectado.</param>
    /// <param name="focoDeLaPersona">La ubicación de la ventana que la persona mira, calculada ahora.</param>
    public Donde Resolver(Func<IntPtr, bool> existe, Func<string> focoDeLaPersona)
    {
        if (!Hay) return new(focoDeLaPersona() ?? "", "");
        bool sigue;
        try { sigue = existe(Hwnd); } catch { sigue = false; }
        if (sigue) return new(Id, "");
        string aviso = $"la ventana en la que trabajaba («{Id}») ya no existe";
        Soltar();
        _pendiente = aviso;   // para quien cuenta el paso, aunque la pregunta de «dónde» se repita mientras
        return new(focoDeLaPersona() ?? "", aviso);
    }

    /// <summary>El aviso de la última ventana perdida, y se retira: se cuenta una vez.</summary>
    public string TomarAviso()
    {
        string a = _pendiente;
        _pendiente = "";
        return a;
    }
}
