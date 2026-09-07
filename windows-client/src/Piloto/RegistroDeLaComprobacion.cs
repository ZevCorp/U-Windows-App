using U.WindowsClient.Diagnostics;
using U.WindowsClient.Navigation;
using U.WindowsClient.Teach;

namespace U.WindowsClient.Piloto;

/// <summary>Cómo le fue a un evento de la lección al comprobarlo.</summary>
public sealed record VeredictoDeEvento(int N, bool Aterrizo, string Esperada, string Real, string Motivo);

/// <summary>
/// EL JUEZ DE LA COMPROBACIÓN. Promesa 175 (spec 012). La app juzga cada llegada; el piloto solo declara.
/// </summary>
/// <remarks>
/// EL PILOTO DICE «LLEGUÉ AL EVENTO N» Y LA APP MIRA. Es la misma separación de la promesa 121:
/// «terminé» no es un veredicto. Aquí el veredicto es comparar la pantalla de AHORA con la llegada
/// que la lección grabó para ese evento, con el mismo <see cref="ElRescate.Aterrizo"/> de siempre.
///
/// EL TOTAL ES EL PLAN, NO LO EJECUTADO (patrón nº10): cuenta cada evento que NAVEGA —el que tiene
/// una llegada distinta de donde estaba—. Un evento sin llegada grabada no puede juzgarse y no se
/// cuenta ni a favor ni en contra: se dice.
/// </remarks>
public sealed class RegistroDeLaComprobacion
{
    private readonly Leccion _leccion;
    private readonly Dictionary<int, VeredictoDeEvento> _veredictos = new();
    private readonly object _candado = new();

    public RegistroDeLaComprobacion(Leccion leccion) { _leccion = leccion; }

    /// <summary>Los eventos que navegan: los que hay que aterrizar para dar la skill por comprobada.</summary>
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

    public int Total => EventosQueNavegan(_leccion).Count;

    /// <summary>Cuántos de los que NAVEGAN aterrizaron. Mismo denominador que <see cref="Total"/>.</summary>
    /// <remarks>La primera prueba real dijo «5/2 aterrizados» (2026-09-07): esto contaba todos los veredictos
    /// buenos y el total solo los que navegan. Dos denominadores en una frase es el patrón nº10 con
    /// otra cara. Ahora los dos cuentan lo mismo: el plan.</remarks>
    public int Hechos
    {
        get
        {
            var navegan = EventosQueNavegan(_leccion);
            lock (_candado) return navegan.Count(e => _veredictos.TryGetValue(e.N, out var v) && v.Aterrizo);
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
        string esperada = (e.Llegada ?? "").Trim();
        if (esperada.Length == 0)
        {
            var v1 = new VeredictoDeEvento(n, false, "", dondeEstoyAhora ?? "",
                $"el evento {n} no tiene llegada grabada (no navegó o no se pudo leer): no se puede juzgar, sigue");
            lock (_candado) _veredictos[n] = v1;
            return v1;
        }
        var a = ElRescate.Aterrizo(esperada, dondeEstoyAhora ?? "");
        var v = new VeredictoDeEvento(n, a.Llego, esperada, dondeEstoyAhora ?? "", a.Llego ? "aterrizó" : a.Motivo);
        lock (_candado) _veredictos[n] = v;
        LogBus.Log("comprobar", $"evento {n}: {(a.Llego ? "ATERRIZÓ" : "NO aterrizó")} · {v.Motivo}");
        return v;
    }

    /// <summary>El veredicto final, por la misma compuerta de la promesa 131.</summary>
    public LaComprobacion.Veredicto Final()
    {
        var navegan = EventosQueNavegan(_leccion);
        int hechos; lock (_candado) hechos = navegan.Count(e => _veredictos.TryGetValue(e.N, out var v) && v.Aterrizo);
        string relato;
        lock (_candado) relato = string.Join("; ", _veredictos.Values.OrderBy(v => v.N).Select(v => $"{v.N}: {(v.Aterrizo ? "ok" : v.Motivo)}"));
        return LaComprobacion.Juzgar(hechos, navegan.Count, relato);
    }
}
