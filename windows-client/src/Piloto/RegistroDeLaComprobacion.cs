using U.WindowsClient.Diagnostics;
using U.WindowsClient.Navigation;
using U.WindowsClient.Teach;

namespace U.WindowsClient.Piloto;

/// <summary>Cómo le fue a un evento de la lección al comprobarlo.</summary>
public sealed record VeredictoDeEvento(int N, bool Aterrizo, string Esperada, string Real, string Motivo);

/// <summary>
/// EL JUEZ DE LA COMPROBACIÓN. Promesa 175 (spec 013, enmendada en la 014). La app juzga cada paso;
/// el piloto solo declara.
/// </summary>
/// <remarks>
/// EL PILOTO DICE «LLEGUÉ AL EVENTO N» Y LA APP MIRA. Es la misma separación de la promesa 121:
/// «terminé» no es un veredicto. Un evento que NAVEGA se juzga comparando la pantalla de AHORA con
/// la llegada que la lección grabó, con el mismo <see cref="ElRescate.Aterrizo"/> de siempre. Un
/// campo TECLEADO se juzga LEYENDO el campo: dice ahora lo que la demo tecleó, o no.
///
/// EL TOTAL ES EL PLAN, NO LO EJECUTADO (patrón nº10): cuenta lo que la lección enseña a hacer,
/// <see cref="EventosQueCuentan"/>. Un evento que ni navega ni teclea no puede juzgarse y no se
/// cuenta ni a favor ni en contra: se dice.
/// </remarks>
public sealed class RegistroDeLaComprobacion
{
    private readonly Leccion _leccion;
    private readonly Func<string, string?>? _valorActual;
    private readonly Dictionary<int, VeredictoDeEvento> _veredictos = new();
    private readonly object _candado = new();

    public RegistroDeLaComprobacion(Leccion leccion) { _leccion = leccion; }

    /// <param name="valorActual">Lee AHORA lo que dice un campo, por su selector; null si no se puede
    /// leer. Es lo que permite juzgar un campo tecleado sin creerle al modelo (promesa 175).</param>
    public RegistroDeLaComprobacion(Leccion leccion, Func<string, string?>? valorActual)
    {
        _leccion = leccion;
        _valorActual = valorActual;
    }

    /// <summary>Los eventos que navegan: los que tienen una llegada distinta de donde estaban.</summary>
    public static IReadOnlyList<EventoDeLaLeccion> EventosQueNavegan(Leccion leccion)
    {
        var salida = new List<EventoDeLaLeccion>();
        if (leccion == null) return salida;
        string anterior = leccion.Empezo ?? "";
        foreach (var e in leccion.Eventos)
        {
            string llegada = (e.Llegada ?? "").Trim();
            if (llegada.Length > 0 && !llegada.Equals(anterior, StringComparison.OrdinalIgnoreCase))
            {
                salida.Add(e);
                anterior = llegada;
            }
        }
        return salida;
    }

    /// <summary>
    /// LO QUE LA LECCIÓN ENSEÑA A HACER, y por tanto lo que hay que repetir para darla por comprobada:
    /// los eventos que navegan MÁS los campos tecleados, uno por campo con su último valor. Promesa
    /// 175, enmendada el 2026-09-08.
    /// </summary>
    /// <remarks>
    /// POR QUÉ SE ENMENDÓ. Nació con «el total = eventos que navegan», que es un denominador sin
    /// ruido: navegar es inequívoco. Pero una demo que solo rellena el triage no navega, y daba
    /// «0 de 0: sigue pendiente» habiendo hecho los 14 pasos. El dueño: «habrá enseñanzas que no
    /// naveguen». Lo tecleado también es inequívoco —el grabador publica un valor por campo— y se
    /// puede juzgar leyendo el campo. Lo que NO cuenta: los clics que solo abren un campo, y los que
    /// ni navegan ni teclean (un botón de opción, una fila que se marca): no hay nada que leer para
    /// juzgarlos, y contarlos sería contar ruido.
    ///
    /// UNO POR CAMPO, EL ÚLTIMO: la persona puede teclear 80, corregir a 82 y seguir. Lo que la
    /// pantalla tiene que decir al final es 82; exigir que también «pase por 80» sería pedir repetir
    /// el error.
    /// </remarks>
    public static IReadOnlyList<EventoDeLaLeccion> EventosQueCuentan(Leccion leccion)
    {
        if (leccion == null) return Array.Empty<EventoDeLaLeccion>();
        var porN = new Dictionary<int, EventoDeLaLeccion>();
        foreach (var e in EventosQueNavegan(leccion)) porN[e.N] = e;

        var ultimoPorCampo = new Dictionary<string, EventoDeLaLeccion>(StringComparer.Ordinal);
        foreach (var e in leccion.Eventos)
        {
            if (porN.ContainsKey(e.N)) continue;                       // ya cuenta por navegar
            if ((e.Texto ?? "").Length == 0 || (e.Selector ?? "").Length == 0) continue;
            ultimoPorCampo[e.Selector] = e;                             // el último gana
        }
        foreach (var e in ultimoPorCampo.Values) porN[e.N] = e;
        return porN.Values.OrderBy(e => e.N).ToList();
    }

    /// <summary>
    /// ¿Dice el campo lo mismo que se tecleó? Un número es el mismo número aunque SAP lo haya
    /// formateado al viajar («80» y «80,000»); un texto no distingue mayúsculas ni espacios de más.
    /// </summary>
    public static bool LoMismoTecleado(string tecleado, string enElCampo)
    {
        string a = (tecleado ?? "").Trim(), b = (enElCampo ?? "").Trim();
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
        return Numero(a) is { } x && Numero(b) is { } y && x == y;

        static decimal? Numero(string v)
        {
            if (v.Length == 0) return null;
            string n = v.Replace(',', '.');
            if (n.Count(c => c == '.') > 1) return null;   // «1.000,50» y parecidos: no se adivina
            return decimal.TryParse(n, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out decimal d) ? d : null;
        }
    }

    public int Total => EventosQueCuentan(_leccion).Count;

    /// <summary>Cuántos de los que CUENTAN están hechos. Mismo denominador que <see cref="Total"/>.</summary>
    /// <remarks>La primera prueba real dijo «5/2 aterrizados» (2026-09-07): esto contaba todos los veredictos
    /// buenos y el total solo los que navegan. Dos denominadores en una frase es el patrón nº10 con
    /// otra cara. Ahora los dos cuentan lo mismo: el plan.</remarks>
    public int Hechos
    {
        get
        {
            var cuentan = EventosQueCuentan(_leccion);
            lock (_candado) return cuentan.Count(e => _veredictos.TryGetValue(e.N, out var v) && v.Aterrizo);
        }
    }

    public IReadOnlyList<VeredictoDeEvento> Veredictos { get { lock (_candado) return _veredictos.Values.OrderBy(v => v.N).ToList(); } }

    /// <summary>El piloto declara que acaba de hacer el evento N. Se juzga contra lo grabado.</summary>
    public VeredictoDeEvento Llegue(int n, string dondeEstoyAhora)
    {
        var e = _leccion.Eventos.FirstOrDefault(x => x.N == n);
        if (e == null)
        {
            var v0 = new VeredictoDeEvento(n, false, "", dondeEstoyAhora ?? "", $"la lección no tiene ningún evento {n}");
            lock (_candado) _veredictos[n] = v0;
            return v0;
        }

        // POR IDENTIDAD, no por número (promesa 175): el piloto puede decir «llegué» sobre el clic que
        // ABRIÓ el campo (sin texto) y no sobre el que lo tecleó. Los dos son el mismo campo; se juzga
        // el que cuenta.
        var cuentan = EventosQueCuentan(_leccion);
        if (!cuentan.Any(c => c.N == e.N) && (e.Selector ?? "").Length > 0)
        {
            var delMismoCampo = cuentan.LastOrDefault(c => c.Selector == e.Selector);
            if (delMismoCampo != null) { e = delMismoCampo; n = e.N; }
        }

        bool navega = EventosQueNavegan(_leccion).Any(x => x.N == e.N);
        if (!navega && (e.Texto ?? "").Length > 0 && (e.Selector ?? "").Length > 0)
        {
            // UN CAMPO TECLEADO SE JUZGA LEYÉNDOLO, no creyéndole a quien dice que lo escribió.
            string? ahora = null;
            try { ahora = _valorActual?.Invoke(e.Selector); } catch { }
            string que = e.Etiqueta.Length > 0 ? e.Etiqueta : e.Selector;
            VeredictoDeEvento vt = ahora == null
                ? new VeredictoDeEvento(n, false, e.Texto, "", $"no pude leer «{que}» para comprobar que dice «{e.Texto}»")
                : LoMismoTecleado(e.Texto, ahora)
                    ? new VeredictoDeEvento(n, true, e.Texto, ahora, $"«{que}» dice «{ahora}», lo que la demo tecleó")
                    : new VeredictoDeEvento(n, false, e.Texto, ahora, $"«{que}» dice «{ahora}» y la demo tecleó «{e.Texto}»");
            lock (_candado) _veredictos[n] = vt;
            LogBus.Log("comprobar", $"evento {n}: {(vt.Aterrizo ? "HECHO" : "NO hecho")} · {vt.Motivo}");
            return vt;
        }

        string esperada = (e.Llegada ?? "").Trim();
        if (esperada.Length == 0)
        {
            var v1 = new VeredictoDeEvento(n, false, "", dondeEstoyAhora ?? "",
                $"el evento {n} no tiene llegada grabada ni texto tecleado (no navegó o no se pudo leer): no se puede juzgar, sigue");
            lock (_candado) _veredictos[n] = v1;
            return v1;
        }
        var a = ElRescate.Aterrizo(esperada, dondeEstoyAhora ?? "");
        var v = new VeredictoDeEvento(n, a.Llego, esperada, dondeEstoyAhora ?? "", a.Llego ? "aterrizó" : a.Motivo);
        lock (_candado) _veredictos[n] = v;
        LogBus.Log("comprobar", $"evento {n}: {(a.Llego ? "ATERRIZÓ" : "NO aterrizó")} · {v.Motivo}");
        return v;
    }

    /// <summary>Lo que cuenta y todavía no está hecho, por su nombre: a eso van las manos.</summary>
    public IReadOnlyList<(int N, string Que, string Motivo)> Pendientes()
    {
        var salida = new List<(int, string, string)>();
        lock (_candado)
        {
            foreach (var e in EventosQueCuentan(_leccion))
            {
                if (_veredictos.TryGetValue(e.N, out var v) && v.Aterrizo) continue;
                string que = e.Etiqueta.Length > 0 ? e.Etiqueta : e.Selector.Length > 0 ? e.Selector : $"evento {e.N}";
                salida.Add((e.N, que, v != null ? v.Motivo : "sin juzgar todavía"));
            }
        }
        return salida;
    }

    /// <summary>El veredicto final, por la misma compuerta de la promesa 131.</summary>
    public LaComprobacion.Veredicto Final()
    {
        var cuentan = EventosQueCuentan(_leccion);
        int hechos; lock (_candado) hechos = cuentan.Count(e => _veredictos.TryGetValue(e.N, out var v) && v.Aterrizo);
        string relato;
        lock (_candado) relato = string.Join("; ", _veredictos.Values.OrderBy(v => v.N).Select(v => $"{v.N}: {(v.Aterrizo ? "ok" : v.Motivo)}"));
        return LaComprobacion.Juzgar(hechos, cuentan.Count, relato);
    }
}
