using System.Diagnostics;
using System.Globalization;

namespace U.Ciclo;

/// <summary>Lo que dejó la espera tras un clic.</summary>
public sealed record Asentamiento(bool Cambio, long Ms, int Lecturas);

/// <summary>
/// TRAS EL CLIC NO SE ESPERA A CIEGAS (promesa 436). Main sondeaba «dónde estoy» cada 120 ms con techo de
/// 1.800 y, como la ventana no cambia al navegar dentro de una app, se comía el techo entero en cada clic
/// (medido el 2026-09-24: 1.814 ms, 12 sondeos, «no cambió» aunque cambió). Aquí se relee lo que de verdad
/// cambia —los accionables— y se sale a la primera diferencia. Techo 150 ms: si no cambió en eso, el ciclo
/// siguiente lo verá igual, y Jev decide sobre lo que hay.
/// </summary>
public static class Asentado
{
    public const int TechoMs = 150;

    public static Asentamiento Esperar(Func<string> huellaAhora, string huellaAntes, int techoMs, Func<long> relojMs)
    {
        long inicio = relojMs();
        int lecturas = 0;
        while (true)
        {
            string h = huellaAhora();
            lecturas++;
            long pasado = relojMs() - inicio;
            if (!string.Equals(h, huellaAntes, StringComparison.Ordinal)) return new Asentamiento(true, pasado, lecturas);
            if (pasado >= techoMs) return new Asentamiento(false, pasado, lecturas);
        }
    }
}

/// <summary>Los cinco tiempos de un ciclo, en ms (promesa 437).</summary>
public sealed record Tiempos(double Donde, double Ver, double Decidir, double Pulsar, double Asentar)
{
    public double Total => Donde + Ver + Decidir + Pulsar + Asentar;
    public bool FueraDePresupuesto => Total > Ciclo.Presupuesto;

    public string Linea()
    {
        string F(double v) => v.ToString(v < 10 ? "0.0" : "0", CultureInfo.InvariantCulture);
        return $"dónde {F(Donde)} · ver {F(Ver)} · decidir {F(Decidir)} · pulsar {F(Pulsar)} · asentar {F(Asentar)} · total {F(Total)} ms"
             + (FueraDePresupuesto ? " · FUERA DE PRESUPUESTO" : "");
    }
}

/// <summary>Una vuelta del ciclo, con todo lo que hace falta para leerla después en el log.</summary>
public sealed record Vuelta(int Paso, Tiempos Tiempos, string Pantalla, int Accionables, string Elegida, string Resultado);

/// <summary>Lo que hizo el motor con un objetivo, y por qué paró.</summary>
public sealed record Recorrido(IReadOnlyList<Vuelta> Vueltas, string PorQueParo, bool Cumplido);

public static class Ciclo
{
    /// <summary>El techo de la v1. La meta es 200; por encima de 500 el ciclo se marca (promesa 437).</summary>
    public const double Presupuesto = 500;
}

/// <summary>
/// EL CICLO: dónde estoy → qué hay → Jev elige → ratón → se asienta → otra vez. Todo inyectado, para que
/// el contrato lo juzgue sin pantalla y la app lo corra sobre la de verdad.
/// </summary>
public sealed class Motor
{
    private readonly Func<Ubicacion?> _donde;
    private readonly Func<IReadOnlyList<Accionable>> _ver;
    private readonly Func<string, string, IReadOnlyList<Accionable>, Eleccion> _decidir;
    private readonly Action<Accionable> _pulsar;
    private readonly Func<bool> _hayQueParar;

    /// <summary>Cada vuelta, en cuanto termina: el log y la burbuja la ven en vivo.</summary>
    public Action<Vuelta>? AlTerminarVuelta { get; set; }

    public Motor(Func<Ubicacion?> donde, Func<IReadOnlyList<Accionable>> ver,
        Func<string, string, IReadOnlyList<Accionable>, Eleccion> decidir, Action<Accionable> pulsar, Func<bool> hayQueParar)
    {
        _donde = donde; _ver = ver; _decidir = decidir; _pulsar = pulsar; _hayQueParar = hayQueParar;
    }

    public Recorrido Objetivo(string objetivo, int maxPasos)
    {
        var vueltas = new List<Vuelta>();
        var reloj = Stopwatch.StartNew();
        int numeroPrevio = -1, repeticiones = 0;
        IReadOnlyList<Accionable>? yaLeidos = null;   // lo que leyó el asentado: el ciclo siguiente no lo relee

        for (int paso = 1; paso <= maxPasos; paso++)
        {
            if (_hayQueParar()) return new Recorrido(vueltas, "Escape: paré sin pulsar nada más", false);

            var r = Stopwatch.StartNew();
            var aqui = _donde();
            double tDonde = r.Elapsed.TotalMilliseconds;
            if (aqui == null) return new Recorrido(vueltas, "no sé dónde estoy: no hay ninguna ventana delante", false);

            r.Restart();
            var lista = yaLeidos ?? _ver();
            yaLeidos = null;
            double tVer = r.Elapsed.TotalMilliseconds;

            r.Restart();
            var e = _decidir(aqui.Pantalla, objetivo, lista);
            double tDecidir = r.Elapsed.TotalMilliseconds;

            if (e.Cumplido >= Jev.CumplidoMinimo)
            {
                Anota(vueltas, new Vuelta(paso, new Tiempos(tDonde, tVer, tDecidir, 0, 0), aqui.Pantalla, lista.Count, "", "cumplido"));
                return new Recorrido(vueltas, "cumplido: " + e.Porque, true);
            }
            if (!e.Pulsar)
            {
                Anota(vueltas, new Vuelta(paso, new Tiempos(tDonde, tVer, tDecidir, 0, 0), aqui.Pantalla, lista.Count, "", e.Porque));
                return new Recorrido(vueltas, "no pulso: " + e.Porque, false);
            }
            var a = lista.FirstOrDefault(x => x.Numero == e.Numero);
            if (a == null) return new Recorrido(vueltas, $"el {e.Numero} no está en la lista de este ciclo", false);

            // El freno se mira OTRA VEZ justo antes de tocar nada: Jev tarda 200 ms y Escape pudo llegar entretanto.
            if (_hayQueParar()) return new Recorrido(vueltas, "Escape: paré sin pulsar nada más", false);

            string antes = Accionables.Huella(lista);
            r.Restart();
            _pulsar(a);
            double tPulsar = r.Elapsed.TotalMilliseconds;

            r.Restart();
            IReadOnlyList<Accionable>? ultima = null;
            var asentado = Asentado.Esperar(() => Accionables.Huella(ultima = _ver()), antes, Asentado.TechoMs, () => reloj.ElapsedMilliseconds);
            double tAsentar = r.Elapsed.TotalMilliseconds;
            yaLeidos = ultima;

            Anota(vueltas, new Vuelta(paso, new Tiempos(tDonde, tVer, tDecidir, tPulsar, tAsentar), aqui.Pantalla, lista.Count,
                a.Id, asentado.Cambio ? "cambió" : "no cambió"));

            repeticiones = asentado.Cambio ? 0 : (e.Numero == numeroPrevio ? repeticiones + 1 : 1);
            numeroPrevio = e.Numero;
            if (repeticiones >= 3)
                return new Recorrido(vueltas, $"Jev repite «{a.Nombre}» tres veces y la pantalla no cambia: paro", false);
        }
        return new Recorrido(vueltas, $"tope de {maxPasos} pasos sin «cumplido»", false);
    }

    private void Anota(List<Vuelta> vueltas, Vuelta v)
    {
        vueltas.Add(v);
        try { AlTerminarVuelta?.Invoke(v); } catch { /* quien mira no frena el ciclo */ }
    }
}
