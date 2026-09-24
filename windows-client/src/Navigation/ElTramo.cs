using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace U.WindowsClient.Navigation;

/// <summary>
/// EL TRAMO: muchos clics de una llamada, corriendo por detrás. Promesas 291-295 (spec 037).
/// </summary>
/// <remarks>
/// POR QUÉ DESPRENDIDO Y NO UNA LLAMADA LARGA. Con una llamada a herramienta pendiente, GPT-Live no
/// produce respuestas: <c>function_call_outputs_required</c>, y «cada response.create de la sesión
/// falló» (medido el 2026-09-12, <c>ProtocoloGptLive.cs</c>). Un tramo de diez clics como llamada
/// dejaría a la voz muda diez segundos: sin poder hablar contigo ni frenar. Así que <see cref="Arrancar"/>
/// devuelve al instante y el bucle corre en su propia tarea; la voz se entera al parar por
/// <c>AvisarALaVoz</c>, o preguntando por <c>Estado</c>.
///
/// UN TRAMO A LA VEZ: dos bucles pulsando la misma pantalla es un desastre garantizado.
///
/// PARA SOLO, Y DICE POR CUÁL (292): Jev cree que ya está (386), tope, el decisor no se atreve, la mano no pudo, el
/// freno, o la misma puerta tres veces sin que cambie la pantalla —el detector de bucle que el diagnóstico
/// pide desde los 80 taps en (330,222)—. Cada paso cuenta, hecho o no (patrón nº10).
///
/// ESTA CLASE NO LEE LA PANTALLA NI PULSA: lo hace el paso que le dan (<see cref="Manos.Paso"/>), que es el
/// mismo de <c>map_decidir</c>. Así el contrato la juzga entera con delegados falsos, sin pantalla.
/// </remarks>
public sealed class ElTramo
{
    /// <summary>Cuántos pasos como mucho si no se pide otra cosa. Diez clics es el escenario del plan; quince deja margen.</summary>
    public const int TopePorDefecto = 15;

    /// <summary>La misma puerta, sin que cambie la pantalla, estas veces seguidas: se para.</summary>
    public const int RepeticionesQueParan = 3;

    /// <summary>Lo que pasó en un paso del tramo. Lo produce el mapa; el tramo solo lo lee.</summary>
    public readonly record struct Paso(
        bool Actuo, bool Termino, bool Cambio, string Selector, string Etiqueta, string Numero, double Confianza, string Cuenta, string Porque, bool Cumplido,
        string Tiempos = "");

    /// <summary>Las manos del tramo, todas inyectables.</summary>
    public sealed record Manos(
        Func<string> Donde,
        Func<string, Paso> Paso,
        Func<bool> HayQueParar,
        Action<string> Progreso,
        Func<string> Inventario,
        Action<string>? AvisarALaVoz,
        Action<string> Log,
        Action<string>? AlEmpezar = null,
        Action? AlTerminar = null);

    private readonly Manos _manos;
    private readonly object _candado = new();
    private Task? _trabajo;
    private volatile bool _alto;
    private string _porqueAlto = "";
    private string _objetivo = "";
    private int _tope;
    private int _paso;
    private string _ultimaPuerta = "";
    private string _estado = "no hay ningún tramo en marcha ni terminado.";
    private readonly StringBuilder _pasos = new();

    public ElTramo(Manos manos) => _manos = manos ?? throw new ArgumentNullException(nameof(manos));

    /// <summary>Si el bucle está corriendo ahora.</summary>
    public bool EnMarcha { get { lock (_candado) return _trabajo != null && !_trabajo.IsCompleted; } }

    /// <summary>La cuenta: en marcha, dice por dónde va; terminado, dice qué hizo y por qué paró.</summary>
    public string Estado
    {
        get
        {
            lock (_candado)
            {
                if (_trabajo != null && !_trabajo.IsCompleted)
                    return $"tramo en marcha: «{_objetivo}», paso {_paso} de hasta {_tope}"
                         + (_ultimaPuerta.Length > 0 ? $", última puerta «{_ultimaPuerta}»." : ".");
                return _estado;
            }
        }
    }

    /// <summary>
    /// Arranca y devuelve al instante. Si ya hay uno corriendo, no arranca otro y dice cuál corre.
    /// </summary>
    public string Arrancar(string objetivo, int tope)
    {
        if (string.IsNullOrWhiteSpace(objetivo)) return "falta `objetivo`: qué se quiere conseguir, para que el tramo sepa hacia dónde ir";
        lock (_candado)
        {
            if (_trabajo != null && !_trabajo.IsCompleted)
                return $"ya hay un tramo en marcha («{_objetivo}», paso {_paso} de hasta {_tope}): pídeme map_alto si quieres cambiarlo.";
            _objetivo = objetivo.Trim();
            _tope = tope > 0 ? tope : TopePorDefecto;
            _paso = 0;
            _alto = false;
            _porqueAlto = "";
            _ultimaPuerta = "";
            _pasos.Clear();
            _trabajo = Task.Run(Bucle);
        }
        return $"tramo en marcha: «{_objetivo}», hasta {_tope} paso(s). Sigo hablando contigo mientras tanto; te cuento cuando pare, y map_tramo_estado dice por dónde voy.";
    }

    /// <summary>Pide parar en el paso en curso. No espera al siguiente.</summary>
    public string Parar(string porque)
    {
        lock (_candado)
        {
            if (_trabajo == null || _trabajo.IsCompleted) return "no hay ningún tramo en marcha que parar.";
            _alto = true;
            _porqueAlto = porque;
            return $"paré el tramo «{_objetivo}» en el paso {_paso}: quedó en «{Seguro(_manos.Donde)}»"
                 + (_ultimaPuerta.Length > 0 ? $", tras «{_ultimaPuerta}»." : ".");
        }
    }

    /// <summary>Para los jueces: espera a que el bucle termine.</summary>
    public bool Esperar(int ms)
    {
        Task? t; lock (_candado) t = _trabajo;
        if (t == null) return true;
        try { return t.Wait(ms); } catch (AggregateException) { return true; }
    }

    private void Bucle()
    {
        _manos.AlEmpezar?.Invoke($"tramo: {_objetivo}");
        int hechos = 0, repetidas = 0;
        string motivo;
        try
        {
            while (true)
            {
                if (_alto) { motivo = $"paraste: {_porqueAlto}"; break; }
                if (Seguro(_manos.HayQueParar)) { motivo = "paraste tú con Escape; no sigo."; break; }
                if (_paso >= _tope) { motivo = $"se agotó el tope de {_tope} paso(s)."; break; }

                int k;
                lock (_candado) k = ++_paso;
                Paso p;
                try { p = _manos.Paso(_objetivo); }
                catch (Exception e)
                {
                    motivo = $"el paso {k} lanzó {e.GetType().Name}: {e.Message}";
                    _manos.Log($"✘ {motivo}");
                    break;
                }

                if (!p.Actuo)
                {
                    // JEV CREE QUE YA ESTÁ, O NO SE ATREVE: las dos vienen del decisor, y se distinguen (386, spec 046).
                    // Hasta el 2026-09-22 la primera decía «el objetivo ya está cumplido:», y eso lo concluía un número
                    // que el modelo da sobre sí mismo (patrón nº2). El tramo no tiene destino esperado —map_tramo solo
                    // recibe el objetivo—, así que no puede declarar éxito: dice lo que Jev cree, con su porqué detrás,
                    // y la cuenta devuelve el turno con lo que hay delante para que lo compruebe la llegada o la persona.
                    motivo = p.Cumplido
                        ? $"Jev cree que ya está: {p.Porque}"
                        : $"el decisor no se atrevió: {p.Porque}";
                    Cuenta(k, p, hecho: false);
                    break;
                }

                hechos++;
                lock (_candado) _ultimaPuerta = p.Etiqueta;
                Cuenta(k, p, hecho: true);

                if (!p.Termino) { motivo = $"la mano no pudo: {p.Cuenta}"; break; }

                // EL DETECTOR DE BUCLE: la misma puerta, y la pantalla que no cambia. Tres seguidas paran.
                bool repite = p.Selector == _ultimoSelector && !p.Cambio;
                repetidas = repite ? repetidas + 1 : 1;
                _ultimoSelector = p.Selector;
                if (repetidas >= RepeticionesQueParan)
                {
                    motivo = $"pulsé la misma puerta «{p.Etiqueta}» tres veces y la pantalla no cambió: esto es un bucle, y no sigo.";
                    break;
                }
            }
        }
        finally { _manos.AlTerminar?.Invoke(); }

        string donde = Seguro(_manos.Donde);
        string inventario;
        try { inventario = _manos.Inventario() ?? ""; } catch (Exception e) { inventario = $"(no pude leer lo que hay delante: {e.Message})"; }
        string cuenta = $"tramo «{_objetivo}»: hice {hechos} paso(s) de hasta {_tope}; paré: {motivo} Estoy en «{donde}».\n"
                      + (_pasos.Length > 0 ? "pasos: " + _pasos.ToString().TrimEnd(' ', ',') + "\n" : "")
                      + (inventario.Length > 0 ? "\n" + inventario : "");
        lock (_candado) _estado = cuenta;
        _manos.Log($"← {cuenta.Split('\n')[0]}");
        try { _manos.Progreso($"tramo: {hechos} paso(s) · {motivo}"); } catch { }
        // LA VOZ SE ENTERA SIN PREGUNTAR (295), y una sola vez: la llamada de map_tramo ya se contestó, así que
        // el único canal que queda abierto es un mensaje nuevo.
        try { _manos.AvisarALaVoz?.Invoke(cuenta); }
        catch (Exception e) { _manos.Log($"no pude avisar a la voz: {e.GetType().Name}: {e.Message}"); }
    }

    private string _ultimoSelector = "";

    private void Cuenta(int k, Paso p, bool hecho)
    {
        string conf = p.Confianza.ToString("0.00", CultureInfo.InvariantCulture);
        string linea = hecho
            ? $"paso {k}: «{p.Etiqueta}» ({p.Numero}) conf {conf} · {(p.Termino ? (p.Cambio ? "cambió" : "no cambió") : "no pudo")}"
            : $"paso {k}: sin acción · {p.Porque}";
        // LOS TIEMPOS POR FASE VAN AL LOG Y NO AL NOTCH: son para medir la fase 4 del plan (esperar es
        // suscribirse), y en el notch serían ruido.
        _manos.Log(p.Tiempos.Length > 0 ? linea + " · " + p.Tiempos : linea);
        try { _manos.Progreso(linea); } catch { }
        lock (_candado) _pasos.Append(hecho ? $"«{p.Etiqueta}» ({p.Numero}) {(p.Termino ? "✓" : "✗")}, " : $"sin acción en el {k}, ");
    }

    private static string Seguro(Func<string> f) { try { return f() ?? ""; } catch { return ""; } }
    private static bool Seguro(Func<bool> f) { try { return f(); } catch { return false; } }
}
