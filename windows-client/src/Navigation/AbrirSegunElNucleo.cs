using Mapeador;

namespace U.WindowsClient.Navigation;

/// <summary>
/// ABRIR: traer al frente lo que se pida, y decir dónde quedamos.
/// </summary>
/// <remarks>
/// La tercera de las cinco capacidades que la voz usa de verdad: 92 veces en 26 días.
///
/// LO QUE AQUÍ NO SE DECIDE, y es la mitad del arreglo: CÓMO se llega. Un programa se lanza, una
/// página se alcanza por su pestaña, y una sesión de SAP no es ninguna de las dos. Eso ya lo sabe
/// <see cref="ComoMePongoDelante"/>, que vive en el mapeador y tiene sus propias promesas —incluida
/// la 22, «lo que no se reconoce se dice: NUNCA se abre algo al azar»—. Volver a decidirlo aquí
/// habría creado una segunda opinión sobre la misma pregunta, y dos opiniones sobre un mismo hecho
/// acaban discrepando el día que alguien afina una.
///
/// LO QUE SÍ SE DECIDE AQUÍ son las tres cosas que se hacían mal:
///
///   · SI YA ESTÁS DELANTE, NO SE ABRE NADA. Relanzar lo que ya está deja dos ventanas de lo mismo
///     y pierde lo que hubiera a medias en la primera. «Abre Chrome» estando en Chrome tiene que ser
///     una respuesta, no una acción.
///   · SE COMPRUEBA POR CONSECUENCIA. Lanzar es una petición, no una llegada: se vuelve a mirar
///     dónde estamos y se contesta con eso. Un «lo abrí» sin mirar es la clase de éxito declarado
///     que este proyecto lleva meses quitándose.
///   · SI NO SE PUDO, SE DICE QUÉ HAY AHORA. Un «no pude» pelado deja a quien lo lee sin saber si
///     está donde creía. Medido el 2026-08-21: pedir SAP con la sesión ya delante daba 14 s de
///     espera y terminaba en «no pude abrir «saplogon» ni traerla al frente; ahora hay «saplogon»»,
///     un mensaje que se desmiente a sí mismo en la misma línea.
/// </remarks>
public sealed class AbrirSegunElNucleo
{
    private readonly Func<string> _donde;
    private readonly Func<ComoMePongoDelante.Plan, bool> _traerAlFrente;
    private readonly Func<string, string> _dominioQueSuena;

    /// <param name="dominioQueSuena">Si lo pedido suena a un sitio web que ya está abierto, su
    /// dominio; si no, vacío. Lo contesta la memoria de pestañas, que es quien sabe de eso.</param>
    public AbrirSegunElNucleo(Func<string> donde,
        Func<ComoMePongoDelante.Plan, bool> traerAlFrente,
        Func<string, string> dominioQueSuena)
    {
        _donde = donde;
        _traerAlFrente = traerAlFrente;
        _dominioQueSuena = dominioQueSuena;
    }

    /// <summary>
    /// DE UN NOMBRE HABLADO A UN SITIO. Nadie dice «uia://chrome.exe»: dice «chrome», o «github».
    /// La única pregunta que hay que hacer para distinguirlos es si eso suena a una web ABIERTA, y
    /// esa la contesta la memoria de pestañas. Lo demás se trata como programa.
    ///
    /// Ir a un sitio y abrir otra copia del sitio no son la misma acción: la segunda deja dos
    /// estados de la misma página y pierde lo que hubiera a medias en la primera. Por eso preguntar
    /// primero por la pestaña no es una optimización, es la diferencia entre las dos.
    /// </summary>
    private string ComoSeLlamaEsoDeVerdad(string pedido)
    {
        string dominio = _dominioQueSuena(pedido) ?? "";
        if (dominio.Length > 0) return "web://" + dominio;
        if (pedido.Contains("://")) return pedido;               // ya venía con su esquema
        string proc = pedido.Replace(".exe", "", StringComparison.OrdinalIgnoreCase).Trim();
        return proc.Length == 0 ? "" : "uia://" + proc + ".exe";
    }

    public string Abrir(string pedido)
    {
        string que = (pedido ?? "").Trim();
        if (que.Length == 0) return "falta decir QUÉ abrir.";

        var plan = ComoMePongoDelante.De(ComoSeLlamaEsoDeVerdad(que));
        if (plan.Via == ComoMePongoDelante.Via.NoSe)
            return $"no reconozco «{que}»: no sé si es un programa, una página o un sistema. "
                 + "Dímelo con su dominio si es una web.";

        // ¿YA ESTAMOS? Se pregunta ANTES de tocar nada.
        string antes = _donde() ?? "";
        if (YaEstamos(que, plan, antes))
            return $"ya estás en «{antes}» — no hace falta abrir nada.";

        if (!_traerAlFrente(plan))
        {
            string ahora = _donde() ?? "";
            return ahora.Length > 0
                ? $"no pude traer «{que}» al frente. Ahora mismo estás en «{ahora}»."
                : $"no pude traer «{que}» al frente, y tampoco sé dónde estamos.";
        }

        string despues = _donde() ?? "";
        return despues.Length > 0
            ? $"«{que}» está delante. Estás en «{despues}»."
            : $"«{que}» está delante, pero todavía no sé identificar la pantalla.";
    }

    /// <summary>
    /// ¿Lo que se pide es donde ya estamos? Para lo web basta con estar EN EL SITIO aunque la
    /// página sea otra —pedir «abre github» estando en una página de GitHub no es pedir nada—, y de
    /// eso ya sabe el mapeador. Para lo demás, se compara la app.
    /// </summary>
    private static bool YaEstamos(string pedido, ComoMePongoDelante.Plan plan, string donde)
    {
        if (donde.Length == 0) return false;
        if (ComoMePongoDelante.EstarEnElSitioBasta(pedido, donde)) return true;
        // SE COMPARAN LAS DOS FORMAS DE NOMBRAR LO MISMO, no dos cadenas. `AppDe` devuelve
        // «chrome.exe» y el plan dice «chrome»: comparar tal cual daba false SIEMPRE, así que abrir
        // relanzaba lo que ya estaba delante. Es la misma familia de fallos que costó la tarde del
        // 2026-08-21 con Chrome, GitHub y SAP —confundir dos maneras de escribir la misma app— y
        // por eso se normaliza aquí, en un solo sitio.
        return plan.Via == ComoMePongoDelante.Via.Proceso
            && SinExe(global::Nucleo.Grafo.AppDe(donde)).Equals(SinExe(plan.Que), StringComparison.OrdinalIgnoreCase);
    }

    private static string SinExe(string s)
    {
        s = (s ?? "").Trim();
        return s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? s[..^4] : s;
    }
}
