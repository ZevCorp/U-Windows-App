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

    // Las salidas que en este turno contestaron con una LISTA de homónimos, y esa lista. Solo con
    // lista `which` separa candidatos: el ejecutor lo ignora cuando no hay homónimos, y sin esta
    // condición which=1, 2, 3… eran claves nuevas del MISMO botón, sin límite (crítico final, 2026-09-11).
    private readonly Dictionary<string, (string Texto, IReadOnlyList<string> Candidatos)> _listas = new(StringComparer.Ordinal);

    // Los fallos por el BOTÓN QUE SE PULSÓ, además de por lo pedido. Es lo que consulta el ejecutor antes
    // de pulsar (AntesDePulsar): se pida como se pida, el mismo botón es el mismo botón (quinta pasada).
    private readonly Dictionary<string, List<string>> _pulsados = new(StringComparer.Ordinal);

    /// <summary>
    /// null si la acción puede ir; si no, el motivo ya redactado para devolvérselo al cerebro: que van
    /// dos, qué salió en cada una, y qué hacer en vez de insistir.
    /// </summary>
    public string? Rechazo(string herramienta, string destino)
    {
        if (!Vigiladas.Contains(herramienta)) return null;
        lock (_candado)
        {
            var (clave, lista) = ClaveYLista(herramienta, destino);
            if (!_fallidos.TryGetValue(clave, out var salio) || salio.Count < Maximo)
                return null;
            // `which` solo se sugiere si hubo lista: sin ella sería mandar al modelo a esquivar el tope.
            return $"no lo intento una tercera vez: «{Legible(lista != null ? destino : SinCual(destino))}» ya "
                 + $"falló dos veces en este turno — 1) {salio[0]} · 2) {salio[1]}. "
                 // «Es el mismo botón» solo se promete donde se cumple: al pulsar, el ejecutor consulta con lo
                 // que va a pulsar; al escribir, el campo cuenta tal como se pidió (sexta pasada del crítico).
                 + (herramienta == "map_take" ? "Pedirlo por otro nombre o por su selector es el mismo botón. " : "")
                 + (lista != null
                     ? $"De la lista, prueba OTRO candidato con which —el 1 y el 2 son botones distintos—: {lista} "
                       + "O dile al usuario qué está pasando."
                     : "Cambia de vía: mira la pantalla (map_look) y pulsa otra cosa, o dile al usuario qué está pasando.");
        }
    }

    /// <summary>Apunta cómo salió una acción. Lo logrado y lo que no es acción vigilada no se apuntan.</summary>
    public void Anota(string herramienta, string destino, bool logrado, string queSalio)
    {
        if (logrado || !Vigiladas.Contains(herramienta)) return;
        lock (_candado)
        {
            string clave = ClaveYLista(herramienta, destino).Clave;
            if (!_fallidos.TryGetValue(clave, out var salio)) _fallidos[clave] = salio = new List<string>();
            salio.Add(Recortar(queSalio));
        }
    }

    /// <summary>
    /// Lo que la voz hace DESPUÉS de cada herramienta, en un solo sitio que el contrato juzga (promesa
    /// 204). Una excepción es un intento que no se logró; la lista de homónimos no es un intento, pero
    /// se recuerda, porque es lo que hace que `which` separe candidatos; lo que no trae mano
    /// (<paramref name="intento"/> null) no se adivina.
    /// </summary>
    /// <remarks>Hasta el 2026-09-11 esta decisión vivía en <c>ConversacionEnVivo</c>, y cambiarla por
    /// «siempre logrado» dejaba el contrato INTACTO: un guardia que se cree puesto (aprendizaje nº18).</remarks>
    public void Despues(string herramienta, string destino, bool revento, bool? intento, bool? logro, string queSalio,
                        IReadOnlyList<string>? candidatos = null)
        => Despues(herramienta, destino, revento, intento, logro, queSalio, candidatos, null);

    /// <param name="pulsado">El selector que el ejecutor pulsó de verdad: sus fallos cuentan para ese
    /// botón, se pida como se pida la próxima vez (<see cref="AntesDePulsar"/>).</param>
    public void Despues(string herramienta, string destino, bool revento, bool? intento, bool? logro, string queSalio,
                        IReadOnlyList<string>? candidatos, string? pulsado)
    {
        if (!Vigiladas.Contains(herramienta)) return;
        if (!revento && intento == false)
        {
            lock (_candado)
                _listas[herramienta + "|" + Destino(SinCual(destino))] = (Recortar(queSalio), candidatos ?? Array.Empty<string>());
            return;
        }
        if (revento || (intento == true && logro == false))
        {
            Anota(herramienta, destino, false, queSalio);
            if (!string.IsNullOrWhiteSpace(pulsado))
                lock (_candado)
                {
                    string clave = herramienta + "|" + pulsado;
                    if (!_pulsados.TryGetValue(clave, out var salio)) _pulsados[clave] = salio = new List<string>();
                    salio.Add(Recortar(queSalio));
                }
        }
    }

    /// <summary>
    /// La consulta del ejecutor justo antes de pulsar, con el selector que VA a pulsar: null si puede; si
    /// ese mismo botón ya falló dos veces en el turno —pedido como fuera—, el motivo (promesa 204).
    /// </summary>
    public string? AntesDePulsar(string herramienta, string selector)
    {
        if (!Vigiladas.Contains(herramienta) || string.IsNullOrWhiteSpace(selector)) return null;
        lock (_candado)
        {
            if (!_pulsados.TryGetValue(herramienta + "|" + selector, out var salio) || salio.Count < Maximo) return null;
            // Si ese botón era de una lista del turno, se recuerda la lista: el «pruebo este otro botón» del
            // audio necesita saber cuál es el otro (sexta pasada del crítico, 2026-09-11).
            string? lista = null;
            foreach (var (claveLista, l) in _listas)
            {
                if (!claveLista.StartsWith(herramienta + "|", StringComparison.Ordinal)) continue;
                for (int i = 0; i < l.Candidatos.Count && lista == null; i++)
                    if (string.Equals(l.Candidatos[i], selector, StringComparison.Ordinal)) lista = l.Texto;
            }
            return $"no lo intento una tercera vez: «{selector}» ya falló dos veces en este turno —1) {salio[0]} · "
                 + $"2) {salio[1]}—, aunque se pidiera de otra forma: es el mismo botón. "
                 + (lista != null
                     ? $"De la lista, prueba OTRO candidato con which —el 1 y el 2 son botones distintos—: {lista} "
                       + "O dile al usuario qué está pasando."
                     : "Cambia de vía: mira la pantalla (map_look) y pulsa otra cosa, o dile al usuario qué está pasando.");
        }
    }

    /// <summary>El usuario habló o escribió: lo de antes era otra petición.</summary>
    public void NuevoTurno()
    {
        lock (_candado)
        {
            _fallidos.Clear();
            _listas.Clear();
            _pulsados.Clear();
        }
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
        if (w >= 0)
        {
            // El número como lo lee el ejecutor (int.TryParse): «02» y «+2» son el 2, y lo que no es un
            // número ≥ 1 no elige nada (crítico, tercera pasada, 2026-09-11).
            cual = int.TryParse(s[(w + 7)..].Trim(), out int n) && n >= 1 ? n.ToString() : "";
            s = s[..w];
        }
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

    /// <summary>La clave: el candidato, si se sabe cuál es —por su selector en cualquier lista del turno,
    /// o por su número en la lista de su nombre—; si no, el destino sin él. Con el candado puesto.</summary>
    private (string Clave, string? Lista) ClaveYLista(string herramienta, string destino)
    {
        // 1. EL SELECTOR EXACTO DE UN CANDIDATO ES ESE CANDIDATO, venga con `which` o sin él, y se busca
        //    en TODAS las listas del turno. Son las reglas del ejecutor —EsperarloVivo: «el selector
        //    exacto manda», y entonces `which` no se lee—, y un selector que no es `uia:name=` (los de
        //    SAP) no se aplana a la etiqueta de su lista. Hasta el 2026-09-11 el tope comparaba lo
        //    PEDIDO y el ejecutor decidía por sus reglas: la cuarta pasada del crítico contó con una
        //    sonda 6 toques al mismo botón con «selector#which=N», y 4 con selectores de SAP (nº16).
        string pedido = SinCual(destino).Trim();
        foreach (var (claveLista, lista) in _listas)
        {
            if (!claveLista.StartsWith(herramienta + "|", StringComparison.Ordinal)) continue;
            for (int i = 0; i < lista.Candidatos.Count; i++)
                if (string.Equals(lista.Candidatos[i], pedido, StringComparison.Ordinal))
                    return ($"{claveLista}#{i + 1}", lista.Texto);
        }
        // 2. POR NOMBRE: con lista de ese nombre, `which` elige el candidato; sin ella, no abre clave.
        string sinCual = herramienta + "|" + Destino(SinCual(destino));
        return _listas.TryGetValue(sinCual, out var suya)
            ? (herramienta + "|" + Destino(destino), suya.Texto)
            : (sinCual, null);
    }

    private static string SinCual(string destino)
    {
        string d = destino ?? "";
        int w = d.LastIndexOf("#which=", StringComparison.Ordinal);
        return w < 0 ? d : d[..w];
    }

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
