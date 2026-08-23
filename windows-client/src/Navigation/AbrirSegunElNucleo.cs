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
    private readonly Func<IReadOnlyList<AppDelSistema>> _instaladas;
    private readonly Func<string, bool> _lanzar;

    /// <param name="dominioQueSuena">Si lo pedido suena a un sitio web que ya está abierto, su
    /// dominio; si no, vacío. Lo contesta la memoria de pestañas, que es quien sabe de eso.</param>
    public AbrirSegunElNucleo(Func<string> donde,
        Func<ComoMePongoDelante.Plan, bool> traerAlFrente,
        Func<string, string> dominioQueSuena,
        Func<IReadOnlyList<AppDelSistema>>? instaladas = null,
        Func<string, bool>? lanzar = null)
    {
        _donde = donde;
        _traerAlFrente = traerAlFrente;
        _dominioQueSuena = dominioQueSuena;
        // Sin catálogo se comporta como antes: una capacidad de menos, no una app rota.
        _instaladas = instaladas ?? (() => Array.Empty<AppDelSistema>());
        _lanzar = lanzar ?? (_ => false);
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

    /// <summary>Una app instalada, tal como la vería alguien en el menú Inicio.</summary>
    public readonly record struct AppDelSistema(string Nombre, string ComoSeLanza);

    /// <summary>
    /// CUÁL DE LAS INSTALADAS ES LA QUE ME PIDES. Null si ninguna; varias si de verdad hay empate.
    /// </summary>
    /// <remarks>
    /// Nadie dice «Microsoft.Todos_8wekyb3d8bbwe!App»: dice «microsoft to do», o «to do». Y hasta
    /// hoy abrir asumía que lo pedido era un ejecutable —«microsoft to do» se convertía en
    /// <c>uia://microsoft to do.exe</c>— así que las apps empaquetadas, que no tienen .exe ni
    /// acceso directo, NO SE PODÍAN ABRIR. Medido el 2026-08-23: el menú Inicio tiene 93 accesos
    /// directos y ni «To Do» ni «Claude» están entre ellos; en shell:AppsFolder hay 143 y las dos sí.
    /// El fallo tardaba 16 ms, que era la pista: no es que fallara al abrir, es que ni lo intentaba.
    ///
    /// EL EMPATE NO SE ROMPE ADIVINANDO. Pedir «to do» encaja con «Microsoft To Do» y con «Click to
    /// Do», y no hay forma honesta de saber cuál — así que se devuelven las dos y que pregunte quien
    /// habla. Elegir por longitud, por orden alfabético o por lo que sea es acertar la mitad de las
    /// veces y equivocarse en silencio la otra mitad.
    /// </remarks>
    public static IReadOnlyList<AppDelSistema> Emparejar(string pedido, IEnumerable<AppDelSistema> instaladas)
    {
        string q = Aplanar(pedido);
        if (q.Length == 0) return Array.Empty<AppDelSistema>();

        var todas = instaladas.ToList();

        var exactas = todas.Where(a => Aplanar(a.Nombre) == q).ToList();
        if (exactas.Count > 0) return exactas;

        var empiezan = todas.Where(a => Aplanar(a.Nombre).StartsWith(q, StringComparison.Ordinal)).ToList();
        if (empiezan.Count > 0) return empiezan;

        return todas.Where(a => Aplanar(a.Nombre).Contains(q, StringComparison.Ordinal)).ToList();
    }

    /// <summary>Sin mayúsculas, sin tildes y sin espacios de más: «Microsoft To Do» y «microsoft
    /// to  do» son la misma app, y quien habla no escribe los acentos.</summary>
    private static string Aplanar(string s)
    {
        s = (s ?? "").Trim().ToLowerInvariant();
        var sb = new System.Text.StringBuilder(s.Length);
        bool espacio = false;
        foreach (char c in s.Normalize(System.Text.NormalizationForm.FormD))
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
            if (char.IsWhiteSpace(c)) { if (!espacio && sb.Length > 0) { sb.Append(' '); espacio = true; } continue; }
            sb.Append(c); espacio = false;
        }
        return sb.ToString().TrimEnd();
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
            // NO TODO LO QUE SE ABRE ES UN .EXE. Si traer al frente no pudo, puede que lo pedido sea
            // una app empaquetada: no tiene ejecutable con ese nombre ni acceso directo, así que la
            // vía del proceso no la encuentra nunca. Se busca en el catálogo del sistema, que sí las
            // tiene todas (2026-08-23: 93 accesos directos frente a 143 apps de verdad).
            var candidatas = Emparejar(que, _instaladas());
            if (candidatas.Count == 1 && _lanzar(candidatas[0].ComoSeLanza))
            {
                string tras = EsperarACambiar(antes);
                return tras.Length > 0
                    ? $"«{candidatas[0].Nombre}» está delante. Estás en «{tras}»."
                    : $"abrí «{candidatas[0].Nombre}», pero todavía no sé identificar la pantalla.";
            }
            if (candidatas.Count > 1)
                return $"«{que}» puede ser {candidatas.Count} cosas: "
                     + string.Join(", ", candidatas.Select(c => $"«{c.Nombre}»"))
                     + ". ¿Cuál de ellas?";

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

    /// <summary>
    /// LANZAR NO ES HABER LLEGADO. Una app recién arrancada tarda en salir, y contestar «está
    /// delante» mirando en ese instante devuelve la ubicación de ANTES — que es exactamente el
    /// éxito declarado que estas promesas existen para evitar. Medido el 2026-08-23: pedir
    /// «microsoft to do» contestaba «está delante. Estás en uia://explorer.exe/ventana».
    ///
    /// Se espera a que CAMBIE, no un tiempo fijo: en frío una app tarda segundos y en caliente
    /// nada, y un reloj fijo se equivoca en los dos casos.
    /// </summary>
    private string EsperarACambiar(string antes, int topeMs = 5000)
    {
        for (int ido = 0; ido < topeMs; ido += 120)
        {
            string ahora = _donde() ?? "";
            if (ahora.Length > 0 && ahora != antes) return ahora;
            System.Threading.Thread.Sleep(120);
        }
        return _donde() ?? "";
    }
    private static string SinExe(string s)
    {
        s = (s ?? "").Trim();
        return s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? s[..^4] : s;
    }
}
