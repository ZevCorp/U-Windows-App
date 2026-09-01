using System.Runtime.Versioning;

namespace Medidor.App;

/// <summary>
/// EL SUBIDOR: cada minuto arma un lote desde el spool y lo manda a Graph. Un latido por minuto por
/// PC (15 PCs = 15 req/min) cabe holgado en el presupuesto del NAT del hospital (Graph limita a 120
/// req/min por IP, y U.exe ya gasta con su telemetría cada 2 s + su pull de exportaciones cada 3 s).
///
/// El orden es aceptar-y-verificar (aprendizaje nº19): solo tras un 200 se toca el spool
/// —confirmar lo aceptado, envenenar lo rechazado—; ante cualquier fallo, el spool se queda intacto
/// y se reintenta al siguiente latido. Un lote vacío se manda igual: es el heartbeat que distingue
/// «medidor vivo, PC quieto» de «medidor muerto» (el servidor detecta el silencio por dispositivo).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class Subidor
{
    private readonly SpoolSqlite _spool;
    private readonly ClienteGraph _cliente;
    private readonly string _deviceId;
    private readonly Calidad _calidad;
    private DateTime _proximoPermitido = DateTime.MinValue;

    public event Action<int>? ConfigVersionNueva;
    public event Action? DevicePausado;

    public Subidor(SpoolSqlite spool, ClienteGraph cliente, string deviceId, Calidad calidad)
    {
        _spool = spool; _cliente = cliente; _deviceId = deviceId; _calidad = calidad;
    }

    public async Task LatirAsync()
    {
        if (DateTime.UtcNow < _proximoPermitido) return; // respetando un Retry-After previo

        // Sincroniza los contadores de calidad del spool (descartes) a la calidad del turno.
        _calidad.Descartes(_spool.DescartesAcumulados);

        var lote = _spool.Tomar(new LimitesDeLote());
        var cuerpo = Lote.Serializar(_deviceId, Guid.NewGuid().ToString(), DateTimeOffset.UtcNow, lote);

        var resp = await _cliente.EnviarLoteAsync(cuerpo, lote);
        if (!resp.Ok)
        {
            if (resp.Codigo == 403) { DevicePausado?.Invoke(); return; }
            if (resp.RetryAfterS is int s) _proximoPermitido = DateTime.UtcNow.AddSeconds(s);
            return; // el spool intacto: se reintenta
        }

        foreach (var (col, seq) in resp.Veneno)
        {
            _spool.Envenenar(col, seq);
            Registro.Anota("subidor", $"veneno sacado: {col}#{seq}");
        }
        // Confirmar lo aceptado agrupado por colección.
        _spool.Confirmar(new LoteTomado(
            FiltrarPorColeccion(lote.Turnos, resp.Confirmar, "turnos"),
            FiltrarPorColeccion(lote.Muestras, resp.Confirmar, "muestras"),
            FiltrarPorColeccion(lote.Eventos, resp.Confirmar, "eventos"),
            FiltrarPorColeccion(lote.Visitas, resp.Confirmar, "visitas")));

        if (resp.ConfigVersion is int cv) ConfigVersionNueva?.Invoke(cv);
    }

    private static IReadOnlyList<FilaDelSpool> FiltrarPorColeccion(
        IReadOnlyList<FilaDelSpool> filas, IReadOnlyList<(string Coleccion, long Seq)> confirmar, string coleccion)
    {
        var seqs = confirmar.Where(c => c.Coleccion == coleccion).Select(c => c.Seq).ToHashSet();
        return filas.Where(f => seqs.Contains(f.Seq)).ToList();
    }
}
