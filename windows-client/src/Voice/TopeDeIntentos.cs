using System;
using System.Collections.Generic;
using U.WindowsClient.Navigation;

namespace U.WindowsClient.Voice;

/// <summary>
/// DOS INTENTOS Y NO TRES (promesa 204, spec 017): dentro de un mismo turno del usuario, la tercera
/// acción hacia un destino que ya falló dos veces no se ejecuta, y se contesta qué salió en cada una.
/// </summary>
/// <remarks>
/// Hasta el 2026-09-10 no había tope en el código: <c>EjecutarNucleoAsync</c> ejecutaba todo lo que
/// pedía el modelo, y la única regla era una frase del prompt que toleraba tres llamadas («más de dos
/// veces»). El 2026-08-09 fueron tres <c>map_take</c> idénticos a «Descargas», 6-7 s cada uno, y
/// ninguno llegó (u-20260809.log, 09:37:54-09:38:10). Una nota de voz de esa noche lo pidió en cifras:
/// «máximo dos intentos».
///
/// POR NOMBRE O POR SELECTOR ES EL MISMO SITIO. El segundo de aquellos tres intentos pedía
/// «uia:name=Descargas;ct=TabItem» y el tercero «Descargas»: si el tope comparara el texto crudo,
/// cambiar de forma de nombrar lo esquivaría — y eso es justo lo que el modelo hace cuando insiste
/// (aprendizaje nº16: dos lados de una comparación que salen de sitios distintos). Se aplana por el
/// MISMO <see cref="Nombres.Aplanar"/> con el que el ejecutor casa etiquetas.
///
/// SOLO CUENTA LO QUE SE SABE LEER SIN INTERPRETAR PROSA. <c>map_take</c> y <c>map_type</c> traen su
/// resultado estructurado (<c>SurfaceMapTools.UltimaMano</c>); el resto de acciones devuelven texto,
/// y decidir por el texto si algo falló es la conclusión disfrazada de hecho del aprendizaje nº2. Lo
/// que no se sabe leer no se cuenta como fallo: el tope nunca frena algo por adivinar.
///
/// Lo LOGRADO no cuenta: tres «Siguiente» que avanzan son trabajo, no insistencia. Y mirar tampoco:
/// mirar no es insistir.
/// </remarks>
public sealed class TopeDeIntentos
{
    /// <summary>Las acciones cuyo resultado trae si lo logró. Son las que el tope vigila.</summary>
    private static readonly HashSet<string> Vigiladas = new(StringComparer.Ordinal) { "map_take", "map_type" };

    /// <summary>Las herramientas que ACTÚAN sobre la pantalla. Mirar, situarse o señalar no están.</summary>
    public static readonly IReadOnlySet<string> Acciones = new HashSet<string>(StringComparer.Ordinal)
    {
        "map_take", "map_type", "map_go_to", "map_open_app", "map_unblock", "file_open",
    };

    private const int Maximo = 2;
    private readonly object _candado = new();
    private readonly Dictionary<string, List<string>> _fallidos = new(StringComparer.Ordinal);

    /// <summary>
    /// null si la acción puede ir; si no, el motivo ya redactado para devolvérselo al cerebro: que van
    /// dos, qué salió en cada una, y qué hacer en vez de insistir.
    /// </summary>
    public string? Rechazo(string herramienta, string destino)
    {
        if (!Vigiladas.Contains(herramienta)) return null;
        lock (_candado)
        {
            if (!_fallidos.TryGetValue(Clave(herramienta, destino), out var salio) || salio.Count < Maximo)
                return null;
            return $"no lo intento una tercera vez: «{Legible(destino)}» ya falló dos veces en este turno "
                 + $"— 1) {salio[0]} · 2) {salio[1]}. Por otro nombre o por su selector es el mismo sitio. "
                 + "Cambia de vía: mira la pantalla (map_look) y elige otro candidato con which, o dile al "
                 + "usuario qué está pasando.";
        }
    }

    /// <summary>Apunta cómo salió una acción. Lo logrado y lo que no es acción vigilada no se apuntan.</summary>
    public void Anota(string herramienta, string destino, bool logrado, string queSalio)
    {
        if (logrado || !Vigiladas.Contains(herramienta)) return;
        lock (_candado)
        {
            string clave = Clave(herramienta, destino);
            if (!_fallidos.TryGetValue(clave, out var salio)) _fallidos[clave] = salio = new List<string>();
            salio.Add(Recortar(queSalio));
        }
    }

    /// <summary>El usuario habló o escribió: lo de antes era otra petición.</summary>
    public void NuevoTurno()
    {
        lock (_candado) _fallidos.Clear();
    }

    /// <summary>
    /// El destino como se compara: el nombre de un selector UIA si lo es, y aplanado. Es público
    /// porque <see cref="CuentaDelTurno"/> lo usa para lo mismo, y dos criterios para decir «es el
    /// mismo sitio» acabarían discrepando.
    /// </summary>
    public static string Destino(string nombreOSelector)
    {
        string s = (nombreOSelector ?? "").Trim();
        // EL CANDIDATO ELEGIDO ES PARTE DEL DESTINO: el 1 y el 2 de una lista numerada son dos botones
        // distintos, y probar el otro es lo que el audio pide (crítico de la rama, 2026-09-11).
        string cual = "";
        int w = s.LastIndexOf("#which=", StringComparison.Ordinal);
        if (w >= 0) { cual = s[(w + 7)..].Trim(); s = s[..w]; }
        if (s.StartsWith("uia:", StringComparison.OrdinalIgnoreCase))
        {
            int i = s.IndexOf("name=", StringComparison.OrdinalIgnoreCase);
            if (i >= 0)
            {
                int f = s.IndexOf(';', i);
                s = f > 0 ? s[(i + 5)..f] : s[(i + 5)..];
            }
        }
        return Nombres.Aplanar(s) + (cual.Length > 0 ? "#" + cual : "");
    }

    /// <summary>De los argumentos de una llamada, el que dice A DÓNDE va. Vacío si no es una acción.</summary>
    public static string DestinoDe(string herramienta, IReadOnlyDictionary<string, string>? args)
    {
        string clave = herramienta switch
        {
            "map_take" => "exit",
            "map_type" => "target",
            "map_go_to" => "surface",
            "map_open_app" => "app",
            "map_unblock" => "choose",
            "file_open" => "path",
            _ => "",
        };
        if (clave.Length == 0 || args == null || !args.TryGetValue(clave, out var v) || string.IsNullOrWhiteSpace(v))
            return "";
        return args.TryGetValue("which", out var cual) && !string.IsNullOrWhiteSpace(cual)
            ? $"{v.Trim()}#which={cual.Trim()}"
            : v.Trim();
    }

    private static string Clave(string herramienta, string destino) => herramienta + "|" + Destino(destino);

    private static string Legible(string destino)
    {
        if (string.IsNullOrWhiteSpace(destino)) return "(el foco)";
        string d = destino.Trim();
        int w = d.LastIndexOf("#which=", StringComparison.Ordinal);
        return w < 0 ? d : $"{d[..w]} (which={d[(w + 7)..]})";
    }

    private static string Recortar(string s)
    {
        s = (s ?? "").Replace('\n', ' ').Trim();
        return s.Length <= 280 ? s : s[..279] + "…";
    }
}
