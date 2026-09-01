using System.Runtime.Versioning;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace Medidor.App;

/// <summary>
/// LA RAÍZ DE COMPOSICIÓN del medidor. Arma las piezas, las cablea y las mueve con dos relojes: el
/// latido de medición (1 s, en el hilo de UI) y el latido de subida (1 min). Mutex de instancia
/// única: dos medidores sobre el mismo PC contarían doble.
///
/// Todo el estado con contenido (título SAP, id de paciente crudo) vive dentro de un tick y muere
/// ahí; lo que cruza a las cubetas y al spool ya pasó por la aduana del Normalizador y de Cable.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class Programa
{
    [STAThread]
    public static int Main()
    {
        using var mutex = new Mutex(true, "Global\\MedidorU-instancia-unica", out bool primera);
        if (!primera) return 0; // ya hay un medidor corriendo

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var programa = new Programa();
        app.Startup += (_, _) => programa.Arrancar(app);
        app.Exit += (_, _) => programa.Apagar();
        return app.Run();
    }

    private Bandeja _bandeja = null!;
    private Ajustes _ajustes = null!;
    private Identidad _identidad = null!;
    private ConfigDeMedicion _config = new();
    private byte[]? _secreto;

    private SondaPrimerPlano _sonda = null!;
    private Ganchos _ganchos = null!;
    private HiloSap _sap = null!;
    private Energia _energia = null!;
    private Sesionizador _sesion = null!;
    private Cubetas _cubetas = null!;
    private Calidad _calidad = null!;
    private Viaje _viaje = null!;
    private Orquestador _orquestador = null!;
    private SpoolSqlite _spool = null!;
    private ClienteGraph _cliente = null!;
    private Subidor _subidor = null!;

    private IReadOnlyList<MedicoDelRoster> _roster = Array.Empty<MedicoDelRoster>();
    private DispatcherTimer _latido = null!;
    private DispatcherTimer _subida = null!;

    private void Arrancar(Application app)
    {
        Registro.Anota("medidor", "arrancando");
        _bandeja = new Bandeja();
        _bandeja.PedirElegirMedico += ElegirMedico;
        _bandeja.PedirPausar += () => { _orquestador.Pausar(); PintarEstado(); };
        _bandeja.PedirReanudar += () => { _orquestador.Reanudar(); PintarEstado(); };
        _bandeja.PedirQueSeMide += Ventanas.QueSeMide;

        _ajustes = Ajustes.Cargar();
        _cliente = new ClienteGraph(_ajustes.BaseUrl, _ajustes.ApiKey ?? "");

        if (!CargarIdentidad())
        {
            if (!Enrolar())
            {
                _bandeja.Aviso("Medidor sin conectar", "No se pudo conectar. Se reintentará al reabrir.");
                app.Shutdown();
                return;
            }
        }

        _secreto = Secreto.Cargar();
        _spool = new SpoolSqlite(Rutas.ArchivoDelSpool);

        _sonda = new SondaPrimerPlano();
        _ganchos = new Ganchos();
        _ganchos.Enganchar();
        _sap = new HiloSap();
        _sap.Arrancar();
        _energia = new Energia();
        _energia.Escuchar();
        _energia.Bloqueo += () => _bandeja.MostrarEstado(Bandeja.EstadoUi.Pausado, MedicoActual);

        _sesion = new Sesionizador();
        _cubetas = new Cubetas();
        _calidad = new Calidad();
        _viaje = new Viaje();
        _orquestador = new Orquestador(
            _sonda, _ganchos, _sap, _sesion, _cubetas, _calidad, _viaje,
            () => _config, () => ClaveDelDiaDelTurno(),
            EmitirEvento, EmitirVisita);

        _subidor = new Subidor(_spool, _cliente, _identidad.DeviceId, _calidad);
        _subidor.ConfigVersionNueva += cv => { if (cv > _config.Version) _ = RefrescarConfigAsync(); };
        _subidor.DevicePausado += () => _bandeja.Aviso("Medidor pausado", "Este equipo fue pausado desde administración.");

        // Abrir el turno del arranque (anónimo si nadie eligió). El baseline no se pierde por eso.
        AbrirTurno(null, null);
        EmitirEvento("medidor_start", DateTimeOffset.UtcNow, _sesion.Abierto?.ShiftId, null, null);

        _latido = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _latido.Tick += (_, _) => { try { _orquestador.Tick(_energia.Bloqueado); CosecharYEncolar(cerrarTodo: false); PintarEstado(); } catch (Exception e) { Registro.Excepcion("tick", e); } };
        _latido.Start();

        _subida = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _subida.Tick += async (_, _) => { try { await _subidor.LatirAsync(); } catch (Exception e) { Registro.Excepcion("subida", e); } };
        _subida.Start();

        // Enrolar/roster ya trajo el roster; si está vacío, el turno queda anónimo.
        PintarEstado();
        _bandeja.Aviso("Medidor activo", "Elige tu nombre desde el icono al empezar tu turno.");
    }

    private string? MedicoActual => _sesion?.Abierto?.DoctorNombre;

    private void PintarEstado()
    {
        var estado = _orquestador.Pausado ? Bandeja.EstadoUi.Pausado
            : MedicoActual == null ? Bandeja.EstadoUi.SinMedico
            : Bandeja.EstadoUi.Midiendo;
        _bandeja.MostrarEstado(estado, MedicoActual);
    }

    private void ElegirMedico()
    {
        var elegido = Ventanas.ElegirMedico(_roster, _sesion.Abierto?.DoctorId);
        if (elegido == null) return;

        // Si hay turno abierto sin médico, se reasigna; si ya tenía otro médico, es un cambio de
        // turno (cierra el anterior y abre uno nuevo — dos médicos son dos turnos).
        if (_sesion.Abierto != null && _sesion.Abierto.DoctorId == null)
        {
            _sesion.Reasignar(elegido.Id, elegido.Nombre);
            EmitirEvento("doctor_prompted", DateTimeOffset.UtcNow, _sesion.Abierto.ShiftId, null,
                new Dictionary<string, object?> { ["reason"] = "reasignado" });
        }
        else
        {
            AbrirTurno(elegido.Id, elegido.Nombre);
        }
        PintarEstado();
    }

    private void AbrirTurno(string? doctorId, string? doctorNombre)
    {
        var (nuevo, cierreDelAnterior) = _sesion.Abrir(DateTimeOffset.Now, doctorId, doctorNombre, _identidad.HmacVersion);
        if (cierreDelAnterior != null)
        {
            CosecharYEncolar(cerrarTodo: true);
            EmitirEvento("shift_end", cierreDelAnterior.CerradoEn, cierreDelAnterior.ShiftId, null,
                new Dictionary<string, object?> { ["reason"] = cierreDelAnterior.Causa });
        }
        EmitirEvento("shift_start", nuevo.AbiertoEn, nuevo.ShiftId, null, null);
        // El turno viaja como fila propia (con su calidad) al cerrarse; su apertura queda como evento.
        _spool.Encolar("turnos", Cable.Turno(nuevo, null, null, _calidad));
    }

    private byte[]? ClaveDelDiaDelTurno()
    {
        if (_secreto == null || _sesion.Abierto == null) return null;
        return Huella.ClaveDelDia(_secreto, _sesion.Abierto.DiaOperativo);
    }

    private void CosecharYEncolar(bool cerrarTodo)
    {
        if (_sesion.Abierto == null && !cerrarTodo) return;
        var shift = _sesion.Abierto?.ShiftId ?? Guid.Empty;
        var muestras = cerrarTodo ? _cubetas.CosecharTodo() : _cubetas.Cosechar(DateTimeOffset.Now);
        foreach (var m in muestras)
            if (shift != Guid.Empty) _spool.Encolar("muestras", Cable.Muestra(shift, m));
    }

    private void EmitirEvento(string kind, DateTimeOffset cuando, Guid? shift, string? encounter, IReadOnlyDictionary<string, object?>? detail)
        => _spool.Encolar("eventos", Cable.Evento(kind, cuando, shift, encounter, detail));

    private void EmitirVisita(Guid shift, Visita v, string? encounter)
        => _spool.Encolar("visitas", Cable.Visita(shift, v, encounter));

    // ── Identidad y config ───────────────────────────────────────────────────

    private bool CargarIdentidad()
    {
        try
        {
            if (!File.Exists(Rutas.ArchivoDeEstado)) return false;
            var doc = JsonSerializer.Deserialize<EstadoEnDisco>(File.ReadAllText(Rutas.ArchivoDeEstado));
            if (doc?.Identidad == null || string.IsNullOrWhiteSpace(doc.Identidad.DeviceId)) return false;
            _identidad = doc.Identidad;
            _config = doc.Config ?? new ConfigDeMedicion();
            _roster = (doc.Roster ?? new List<MedicoDelRoster>());
            return true;
        }
        catch (Exception e) { Registro.Excepcion("estado", e); return false; }
    }

    private bool Enrolar()
    {
        var codigo = Ventanas.PedirCodigoDeEnrolamiento();
        if (codigo == null) return false;

        try
        {
            using var doc = _cliente.EnrolarAsync(codigo, Environment.MachineName,
                Environment.OSVersion.VersionString, VersionApp()).GetAwaiter().GetResult();
            if (doc == null) return false;

            var raiz = doc.RootElement;
            _identidad = new Identidad
            {
                DeviceId = raiz.GetProperty("device_id").GetString() ?? "",
                OrganizationId = raiz.GetProperty("organization_id").GetString() ?? "",
                OrgName = raiz.TryGetProperty("org_name", out var on) ? on.GetString() ?? "" : "",
                HmacVersion = raiz.TryGetProperty("hmac", out var h) && h.TryGetProperty("version", out var hv) ? hv.GetInt32() : 1,
                ConfigVersion = raiz.TryGetProperty("config_version", out var cvp) ? cvp.GetInt32() : 0,
            };
            if (raiz.TryGetProperty("hmac", out var hm) && hm.TryGetProperty("secret", out var sec))
                Secreto.Guardar(Convert.FromBase64String(sec.GetString() ?? ""));
            if (raiz.TryGetProperty("config", out var cfg))
                _config = JsonSerializer.Deserialize<ConfigDeMedicion>(cfg.GetRawText()) ?? new ConfigDeMedicion();
            _roster = LeerRoster(raiz);

            GuardarEstado();
            return true;
        }
        catch (Exception e) { Registro.Excepcion("enroll", e); return false; }
    }

    private async Task RefrescarConfigAsync()
    {
        try
        {
            using var doc = await _cliente.ConfigAsync(_identidad.DeviceId, _config.Version, _identidad.HmacVersion);
            if (doc == null) return;
            var raiz = doc.RootElement;
            if (raiz.TryGetProperty("unchanged", out var un) && un.GetBoolean()) return;
            if (raiz.TryGetProperty("config", out var cfg))
                _config = JsonSerializer.Deserialize<ConfigDeMedicion>(cfg.GetRawText()) ?? _config;
            if (raiz.TryGetProperty("roster", out _))
                _roster = LeerRoster(raiz);
            if (raiz.TryGetProperty("hmac", out var hm) && hm.TryGetProperty("secret", out var sec))
            {
                Secreto.Guardar(Convert.FromBase64String(sec.GetString() ?? ""));
                _secreto = Secreto.Cargar();
                if (hm.TryGetProperty("version", out var hv)) _identidad.HmacVersion = hv.GetInt32();
            }
            GuardarEstado();
            EmitirEvento("config_applied", DateTimeOffset.UtcNow, _sesion.Abierto?.ShiftId, null,
                new Dictionary<string, object?> { ["version"] = _config.Version });
        }
        catch (Exception e) { Registro.Excepcion("config", e); }
    }

    private static List<MedicoDelRoster> LeerRoster(JsonElement raiz)
    {
        var lista = new List<MedicoDelRoster>();
        if (raiz.TryGetProperty("roster", out var r) && r.ValueKind == JsonValueKind.Array)
            foreach (var m in r.EnumerateArray())
                lista.Add(new MedicoDelRoster(
                    m.GetProperty("id").GetString() ?? "",
                    m.GetProperty("display_name").GetString() ?? ""));
        return lista;
    }

    private void GuardarEstado()
    {
        try
        {
            var estado = new EstadoEnDisco { Identidad = _identidad, Config = _config, Roster = _roster.ToList() };
            File.WriteAllText(Rutas.ArchivoDeEstado, JsonSerializer.Serialize(estado));
        }
        catch (Exception e) { Registro.Excepcion("estado", e); }
    }

    private static string VersionApp()
        => System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";

    private void Apagar()
    {
        try
        {
            _latido?.Stop();
            _subida?.Stop();
            _orquestador?.CerrarAlApagar();
            CosecharYEncolar(cerrarTodo: true);
            EmitirEvento("medidor_stop", DateTimeOffset.UtcNow, null, null, null);
            // Un último intento de subida: si la red está, se va limpio; si no, el spool lo guarda.
            _subidor?.LatirAsync().GetAwaiter().GetResult();
            _sap?.Dispose();
            _ganchos?.Dispose();
            _energia?.Dispose();
            _spool?.Dispose();
            _bandeja?.Dispose();
        }
        catch (Exception e) { Registro.Excepcion("apagar", e); }
    }

    private sealed class EstadoEnDisco
    {
        public Identidad? Identidad { get; set; }
        public ConfigDeMedicion? Config { get; set; }
        public List<MedicoDelRoster>? Roster { get; set; }
    }
}
