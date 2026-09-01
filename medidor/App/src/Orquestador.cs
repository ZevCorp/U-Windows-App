using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace Medidor.App;

/// <summary>
/// EL LATIDO del medidor: una vez por segundo junta lo que vieron la sonda de primer plano, los
/// ganchos y el hilo SAP, lo normaliza (aduana de privacidad), lo atribuye al encounter vigente y
/// lo deja caer en la cubeta correcta. También decide cuándo abrir/cerrar visitas SAP y cuándo
/// cerrar el turno.
///
/// Vive en un timer del hilo de UI (WPF DispatcherTimer se arma en Programa); aquí solo está la
/// lógica de un tick, para poder razonarla sin la app. Todo el conteo de duración pasa por el
/// Reloj: el orquestador nunca resta DateTime.Now a mano.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class Orquestador
{
    private readonly SondaPrimerPlano _sonda;
    private readonly Ganchos _ganchos;
    private readonly HiloSap _sap;
    private readonly Sesionizador _sesion;
    private readonly Cubetas _cubetas;
    private readonly Calidad _calidad;
    private readonly Viaje _viaje;
    private readonly Reloj _reloj;
    private readonly Func<ConfigDeMedicion> _config;
    private readonly Func<byte[]?> _claveDelDia;
    private readonly Action<string, DateTimeOffset, Guid?, string?, IReadOnlyDictionary<string, object?>?> _emitirEvento;
    private readonly Action<Guid, Visita, string?> _emitirVisita;

    private readonly Stopwatch _mono = Stopwatch.StartNew();
    private string? _encounterVigente;
    private string? _surfaceAnterior;
    private bool _pausado;

    public Orquestador(
        SondaPrimerPlano sonda, Ganchos ganchos, HiloSap sap, Sesionizador sesion,
        Cubetas cubetas, Calidad calidad, Viaje viaje,
        Func<ConfigDeMedicion> config, Func<byte[]?> claveDelDia,
        Action<string, DateTimeOffset, Guid?, string?, IReadOnlyDictionary<string, object?>?> emitirEvento,
        Action<Guid, Visita, string?> emitirVisita)
    {
        _sonda = sonda; _ganchos = ganchos; _sap = sap; _sesion = sesion;
        _cubetas = cubetas; _calidad = calidad; _viaje = viaje;
        _config = config; _claveDelDia = claveDelDia;
        _emitirEvento = emitirEvento; _emitirVisita = emitirVisita;
        _reloj = new Reloj(_mono.ElapsedMilliseconds, DateTimeOffset.UtcNow);
    }

    public bool Pausado => _pausado;
    public string? EncounterVigente => _encounterVigente;

    public void Pausar()
    {
        if (_pausado) return;
        _pausado = true;
        _emitirEvento("pausa_usuario", DateTimeOffset.UtcNow, _sesion.Abierto?.ShiftId, null, null);
    }

    public void Reanudar()
    {
        if (!_pausado) return;
        _pausado = false;
        _emitirEvento("reanudar_usuario", DateTimeOffset.UtcNow, _sesion.Abierto?.ShiftId, null, null);
    }

    /// <summary>Un tick. <paramref name="bloqueado"/> viene de Energia.</summary>
    public void Tick(bool bloqueado)
    {
        var ahoraMono = _mono.ElapsedMilliseconds;
        var pared = DateTimeOffset.Now; // LOCAL: el día operativo y las cubetas se anclan a la hora del hospital
        var tic = _reloj.Avanzar(ahoraMono, DateTimeOffset.UtcNow);
        if (tic.HuecoMs > 0) _calidad.Hueco(tic.HuecoMs);
        if (tic.DesfaseRelojMs != 0) _calidad.SaltoDeReloj();

        // El sesionizador puede cerrar el turno por inactividad o bloqueo prolongado.
        var input = _ganchos.Cosechar();
        var estadoPc = new EstadoDelPc(
            UltimoInputHaceMs: input.UltimoInputHaceMs,
            BloqueadoHaceMs: bloqueado ? BloqueadoHaceMs(ahoraMono) : null);
        var cierre = _sesion.Avanzar(pared, estadoPc);
        if (cierre != null) CerrarTurno(cierre);

        if (_pausado || _sesion.Abierto == null)
        {
            _surfaceAnterior = null; // al reanudar, el primer cambio no cuenta como «cambio de contexto»
            return;
        }

        if (_ganchos.Degradado) _calidad.GanchosDegradados();

        // ¿Dónde está el médico? SAP manda; si no hay SAP delante, la sonda de primer plano.
        var vistaSap = _sap.Ultima;
        if (vistaSap.EstabaOcupado) _calidad.TickSapSaltado();

        Superficie superficie;
        var cfg = _config();
        if (vistaSap.Surface != null)
        {
            superficie = new Superficie(Normalizador.AppSap, Normalizador.SinVista(vistaSap.Surface));
            ActualizarEncounter(vistaSap.Surface, cfg);
            // Fase 2: aquí el viaje toma StartRequest/EndRequest. En fase 1, visita gruesa por identidad.
            if (!cfg.SoloForeground)
            {
                var visitaCerrada = _viaje.AlCambiarSuperficie(ahoraMono, pared, vistaSap.Surface);
                if (visitaCerrada != null) _emitirVisita(_sesion.Abierto.ShiftId, visitaCerrada, _encounterVigente);
            }
        }
        else
        {
            var vp = _sonda.Mirar();
            if (vp == null) return;
            superficie = Normalizador.Normalizar(new EntradaDeSuperficie(vp.Proceso, null, vp.UrlNavegador, null), cfg);
            // Salió de SAP: cerrar la visita en curso, si la había.
            var visitaCerrada = _viaje.AlCambiarSuperficie(ahoraMono, pared, null);
            if (visitaCerrada != null) _emitirVisita(_sesion.Abierto.ShiftId, visitaCerrada, _encounterVigente);
        }

        // ¿Cambió el contexto respecto al tick anterior?
        var claveContexto = superficie.App + "|" + (superficie.Surface ?? "") + "|" + (_encounterVigente ?? "");
        int cambios = _surfaceAnterior != null && _surfaceAnterior != claveContexto ? 1 : 0;
        _surfaceAnterior = claveContexto;

        // Actividad y escritura del tick. La actividad se decide con el «hace cuánto» relativo,
        // que es comparable en cualquier reloj (los ganchos y este orquestador no comparten el suyo).
        bool activo = input.UltimoInputHaceMs <= Actividad.UmbralInactividadMs;
        int typingMs = (int)Escritura.MsDeRafagas(input.InstantesDeTecla);

        _cubetas.Registrar(pared, superficie, _encounterVigente, new Aportes(
            ForegroundMs: tic.AporteMs,
            ActiveMs: activo ? tic.AporteMs : 0,
            TypingMs: typingMs,
            Teclas: input.InstantesDeTecla.Count,
            Clics: input.Clics,
            Scroll: input.Scroll,
            CambiosDeContexto: cambios,
            SapRoundtrips: 0, // fase 2
            SapEsperaMs: 0)); // fase 2
    }

    private long? _bloqueadoDesdeMono;
    private long BloqueadoHaceMs(long ahoraMono)
    {
        _bloqueadoDesdeMono ??= ahoraMono;
        return ahoraMono - _bloqueadoDesdeMono.Value;
    }

    private void ActualizarEncounter(string surface, ConfigDeMedicion cfg)
    {
        var partes = Normalizador.PartesSap(Normalizador.SinVista(surface));
        if (partes == null) return;

        var clave = _claveDelDia();
        if (clave == null) return; // sin secreto no hay huella; el tiempo SAP igual se mide, sin paciente

        // Fase 1: la extracción es por TÍTULO SAP (una regla remota con regex). El campo por
        // selector queda cableado en HiloSap.LeerCampo para fase 2. El título SAP no se guarda:
        // entra a la regla, sale un grupo, se hashea y se suelta.
        var tituloSap = _sap.Ultima.TituloSap;
        var extraido = ReglasDeIdentidad.Extraer(cfg.Reglas(), partes.Value.Tcode, _sap.LeerCampo, tituloSap);
        if (extraido == null)
        {
            // No se encontró paciente en esta pantalla: el encounter vigente se conserva (venimos
            // de una pantalla del mismo paciente) hasta que otra pantalla dé una huella distinta.
            return;
        }

        var nuevo = Huella.DeIdentificador(clave, extraido.Value.IdNormalizado);
        if (nuevo == _encounterVigente) return;

        var shift = _sesion.Abierto!.ShiftId;
        if (_encounterVigente != null)
            _emitirEvento("encounter_exit", DateTimeOffset.UtcNow, shift, _encounterVigente, null);
        _encounterVigente = nuevo;
        _emitirEvento("encounter_enter", DateTimeOffset.UtcNow, shift, nuevo,
            new Dictionary<string, object?> { ["rule"] = extraido.Value.ReglaId });
    }

    private void CerrarTurno(CierreDeTurno cierre)
    {
        // Cerrar cosecha las cubetas en curso y emite el cierre con su causa.
        _encounterVigente = null;
        _emitirEvento("shift_end", cierre.CerradoEn, cierre.ShiftId, null,
            new Dictionary<string, object?> { ["reason"] = cierre.Causa });
    }

    public void CerrarAlApagar()
    {
        var cierre = _sesion.Cerrar(DateTimeOffset.Now, "apagado");
        if (cierre != null) CerrarTurno(cierre);
    }
}
