using System.Runtime.Versioning;
using U.Graph.Surfaces;

namespace Medidor.App;

/// <summary>Lo que el hilo SAP averiguó en un tick: la identidad de pantalla (o null si no hay SAP
/// delante), si estaba ocupado (tick saltado), y el usuario SAP para validar el médico. Nada de
/// contenido: ni títulos, ni campos, ni valores.</summary>
/// <param name="TituloSap">El título de la ventana SAP (session.FindById("wnd[0]").Text). Vive en
/// memoria un solo tick, para pasarlo a la regla de extracción del ID de paciente — NUNCA se
/// serializa, ni se loguea, ni entra en una cubeta. Es la fuente de la huella cuando la regla es
/// «titulo_sap».</param>
public sealed record VistaSap(string? Surface, bool EstabaOcupado, string? SapUser, string? TituloSap);

/// <summary>
/// EL HILO SAP: la capa COM de SAP GUI Scripting exige STA (un hilo con bomba de mensajes), y
/// tocarla desde el thread-pool devuelve null con SAP delante (incidente documentado en
/// EjecutorDeExportaciones). Así que el medidor tiene su propio STA, dueño de un SapGuiSurface, y
/// todo lo que pregunta a SAP pasa por aquí.
///
/// Disciplina Busy, no negociable: con Busy=true cualquier llamada al scripting se bloquea SIN
/// retorno y cuelga el hilo para siempre. Se pregunta IsBusy() primero y, si está ocupado, se salta
/// el tick (se cuenta en la calidad). Cadencia 1-2 s: presupuesto de 5-8 llamadas COM por tick,
/// muy por debajo de lo que degrada SAP.
///
/// Fase 1: SOLO identidad (y usuario). Los eventos COM StartRequest/EndRequest (la latencia de
/// round-trip) son fase 2 — el gancho está marcado abajo.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class HiloSap : IDisposable
{
    private readonly Thread _hilo;
    private readonly ManualResetEventSlim _pedido = new(false);
    private volatile bool _vivo = true;
    private volatile VistaSap _ultima = new(null, false, null);
    private SapGuiSurface? _sap;
    private Func<string, string?>? _leerCampoPendiente;
    private volatile int _saltadosPorBusy;

    public HiloSap()
    {
        _hilo = new Thread(Bucle) { IsBackground = true, Name = "medidor-sap" };
        _hilo.SetApartmentState(ApartmentState.STA);
    }

    public void Arrancar() => _hilo.Start();

    public VistaSap Ultima => _ultima;
    public int SaltadosPorBusy => _saltadosPorBusy;

    /// <summary>Lee UN campo SAP por selector, en el hilo STA, para la extracción del ID de
    /// paciente. Devuelve el valor crudo; el que llama lo hashea y lo suelta de inmediato. Es la
    /// ÚNICA lectura de contenido que el medidor hace, y solo por una regla de la config remota.</summary>
    public string? LeerCampo(string selector)
    {
        // Se resuelve en el hilo STA vía el surface; aquí solo se delega si el surface existe.
        // Para fase 1 el LectorSap usa el título, así que este camino queda listo pero poco usado.
        try { return _sap?.ValorActual(selector); }
        catch (Exception e) { Registro.Excepcion("sap", e); return null; }
    }

    private void Bucle()
    {
        try { _sap = new SapGuiSurface(); }
        catch (Exception e) { Registro.Excepcion("sap", e); return; }

        while (_vivo)
        {
            try
            {
                var disp = _sap.Check();
                if (!disp.Available)
                {
                    _ultima = new VistaSap(null, false, null, null);
                }
                else if (_sap.IsBusy())
                {
                    // Ocupado: NO se llama a Identity (colgaría). Se salta el tick y se cuenta.
                    _saltadosPorBusy++;
                    _ultima = _ultima with { EstabaOcupado = true };
                }
                else
                {
                    var id = _sap.Identity();
                    var surface = id.Url == SurfaceIdentity.Unknown.Url ? null : id.Url;
                    // El usuario SAP (session.Info.User) es señal SECUNDARIA de validación del
                    // médico. Leerlo pide un método nuevo en SapGuiSurface (windows-graph, que en
                    // fase 1 solo se referencia y trae su propia promesa): queda para fase 2. El
                    // selector primario del médico basta para el baseline.
                    _ultima = new VistaSap(surface, false, null, id.Title);
                }
            }
            catch (Exception e) { Registro.Excepcion("sap", e); _ultima = new VistaSap(null, false, null, null); }

            Thread.Sleep(1500);
        }
        _sap?.Dispose();
    }

    public void Dispose()
    {
        _vivo = false;
        _pedido.Set();
        if (_hilo.IsAlive) _hilo.Join(TimeSpan.FromSeconds(3));
    }
}
