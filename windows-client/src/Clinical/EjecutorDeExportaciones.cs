using System.Net.Http;
using System.Text;
using System.Text.Json;
using U.Graph;
using U.Graph.Surfaces;
using U.WindowsClient.Diagnostics;
using U.WindowsClient.Navigation;
using U.WindowsClient.Uia;

namespace U.WindowsClient.Clinical;

/// <summary>
/// EL EJECUTOR DE OPERATIONS. El médico pulsa «Exportar a HC» en la web y esto lo escribe en SAP.
/// </summary>
/// <remarks>
/// OCUPA UN SITIO QUE YA ESTABA RESERVADO. El backend tiene la cola construida y documentada desde
/// antes: «un ejecutor (hoy el simulador, mañana el cliente Windows) reclama el trabajo, lo escribe
/// en el HIS y reporta» (src/application/use-cases/NoteExportService.js). Aquí no se inventa ningún
/// canal: se habla EXACTAMENTE el contrato que ya habla el simulador.
///
/// SE TRABAJA POR PULL, y eso es lo que hace que desaparezca el código de emparejamiento de ocho
/// caracteres. Antes había que teclear un código porque el portal no sabía a qué máquina mandar los
/// datos; ahora la máquina pregunta, y se identifica con la API key que ya tiene. Nadie teclea nada,
/// no hay puerto abierto, y funciona igual detrás del firewall de un hospital.
///
/// EL REPARTO, y cada mitad está probada por separado:
///   · El WORKFLOW navega —transacción, paciente, pestaña de triage— y no rellena nada.
///   · El RELLENADOR escribe los campos con la nota, verificando cada uno por relectura.
/// Los tres datos que llenaba el flujo viejo eran del puente clínico, no del workflow. Componerlos
/// es lo que convierte «llega a la pantalla» en «la pantalla queda llena».
///
/// UN 'ok' ES LO ÚNICO QUE MARCA LA CONSULTA COMO EXPORTADA, y el backend lo repite: encolar no es
/// exportar, reclamar no es exportar, «no falló» no es exportar. Por eso aquí no se contesta 'ok'
/// más que cuando los campos se releyeron escritos.
/// </remarks>
public sealed class EjecutorDeExportaciones : IDisposable
{
    private readonly GraphConfig _config;
    private readonly RellenadorSap _rellenador;
    private readonly Func<string> _donde;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private CancellationTokenSource? _vida;
    private Task? _bucle;

    /// <summary>
    /// Cada cuánto se pregunta si hay trabajo. Tres segundos: lo bastante para que al médico le
    /// parezca instantáneo, y lo bastante espaciado para no castigar al backend con una máquina
    /// preguntando sin parar durante toda una jornada.
    /// </summary>
    private static readonly TimeSpan Ritmo = TimeSpan.FromSeconds(3);

    public EjecutorDeExportaciones(GraphConfig config, RellenadorSap rellenador, Func<string> donde)
    {
        _config = config;
        _rellenador = rellenador;
        _donde = donde;
    }

    /// <summary>Qué está pasando, para pintarlo donde se vea.</summary>
    public event Action<string>? Cuenta;

    public bool Encendido => _bucle is { IsCompleted: false };

    /// <summary>Quién dice ser esta máquina. Queda auditado en `claimed_by` del trabajo.</summary>
    private string Device =>
        $"{_config.AppId}@{Environment.MachineName}".Replace(" ", "-");

    public void Arrancar()
    {
        if (Encendido) return;
        if (!_config.IsConfigured) { LogBus.Log("exportar", "sin clave de Graph: el ejecutor no arranca"); return; }

        _vida = new CancellationTokenSource();
        _bucle = Task.Run(() => BucleAsync(_vida.Token));
        LogBus.Log("exportar", $"ejecutor de exportaciones en marcha como «{Device}» · pregunta cada {Ritmo.TotalSeconds:N0} s");
    }

    public void Parar()
    {
        try { _vida?.Cancel(); } catch { }
        _vida = null;
        _bucle = null;
        LogBus.Log("exportar", "ejecutor de exportaciones parado");
    }

    private async Task BucleAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var trabajo = await ReclamarAsync(ct);
                if (trabajo != null) await AtenderAsync(trabajo.Value, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception e)
            {
                // Un fallo de red no puede tumbar el bucle: el médico pulsará el botón otra vez y
                // esto tiene que seguir escuchando.
                LogBus.Log("exportar", $"la vuelta falló: {e.Message}");
            }
            try { await Task.Delay(Ritmo, ct); } catch { break; }
        }
    }

    /// <summary>Pregunta si hay trabajo. `null` cuando no lo hay: el backend contesta 204.</summary>
    private async Task<JsonElement?> ReclamarAsync(CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post,
            $"{_config.BaseUrl.TrimEnd('/')}/api/v1/operations/exports/claim")
        { Content = new StringContent(JsonSerializer.Serialize(new { device = Device }),
            Encoding.UTF8, "application/json") };
        Firmar(req);

        using var res = await Http.SendAsync(req, ct);
        if (res.StatusCode == System.Net.HttpStatusCode.NoContent) return null;
        if (!res.IsSuccessStatusCode)
        {
            LogBus.Log("exportar", $"el backend rechazó el claim: HTTP {(int)res.StatusCode}");
            return null;
        }
        return JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct)).RootElement.Clone();
    }

    private async Task AtenderAsync(JsonElement trabajo, CancellationToken ct)
    {
        string id = Ruta(trabajo, "export", "id");
        string workflow = Ruta(trabajo, "export", "workflow_id");
        string nota = Ruta(trabajo, "payload", "context");
        if (nota.Length == 0) nota = Ruta(trabajo, "payload", "rendered_text");

        LogBus.Log("exportar", $"trabajo {id} reclamado · workflow {workflow} · nota de {nota.Length} caracteres");
        Cuenta?.Invoke("Exportando a la historia clínica…");

        if (nota.Trim().Length == 0)
        {
            await ReportarAsync(id, "error", detalle: "PAYLOAD_SIN_NOTA", ct: ct);
            return;
        }

        // 1. LLEGAR. El workflow navega hasta la pantalla; enfocar SAP es parte de su trabajo y ya
        //    lo hace el alineador que el reproductor lleva dentro.
        string error = await NavegarAsync(workflow, ct);
        if (error.Length > 0)
        {
            LogBus.Log("exportar", $"trabajo {id}: no se pudo llegar a la pantalla — {error}");
            await ReportarAsync(id, "error", detalle: "NAVEGACION_FALLIDA", ct: ct);
            return;
        }

        // 2. COMPROBAR DÓNDE SE ACABÓ. El workflow puede decir que terminó y haber dejado otra
        //    pantalla delante; escribir ahí sería meter datos clínicos en el formulario de otro.
        string aqui = _donde();
        if (!RellenadorSap.EsLaPantallaDeTriage(aqui))
        {
            LogBus.Log("exportar", $"trabajo {id}: el workflow acabó en «{aqui}», que no es la pantalla de triage");
            await ReportarAsync(id, "error", detalle: "PANTALLA_INESPERADA", ct: ct);
            return;
        }

        // 3. LLENAR. Con verificación por relectura, campo a campo.
        var (escritos, sinLlenar) = await _rellenador.RellenarConNotaAsync(nota, ct);
        LogBus.Log("exportar", $"trabajo {id}: {escritos.Count} campo(s) escritos, {sinLlenar.Count} sin llenar");

        if (escritos.Count == 0)
        {
            // Ni un campo: eso no es un éxito parcial, es no haber hecho nada. Se devuelve al
            // médico con lo que faltó, que es información que él sí puede usar.
            await ReportarAsync(id, "needs_doctor", sinLlenar: sinLlenar, ct: ct);
            return;
        }

        Cuenta?.Invoke($"✓ {escritos.Count} campo(s) en la historia clínica.");
        await ReportarAsync(id, "ok", sinLlenar: sinLlenar, ct: ct);
    }

    /// <summary>Corre el workflow que lleva hasta la pantalla. Devuelve "" si llegó.</summary>
    private async Task<string> NavegarAsync(string workflowId, CancellationToken ct)
    {
        if (workflowId.Length == 0) return "el trabajo no trae workflow";
        try
        {
            var graph = new GraphClient(_config);
            var player = new WorkflowPlayer(graph, _config, new UiaSurface(), new SapGuiSurface())
            {
                Aligner = AppAligner.EnsureAsync,
                Log = s => LogBus.Log("exportar", s),
            };
            var r = await player.RunAsync(workflowId, null, strictSurface: true, ct);
            return r.Ok ? "" : (r.Error ?? "el workflow no terminó");
        }
        catch (Exception e) { return e.Message; }
    }

    /// <summary>
    /// Reporta el desenlace, INSISTIENDO hasta que el backend acuse recibo.
    /// </summary>
    /// <remarks>
    /// Esto no es best-effort y el backend lo dice explícitamente: es lo que decide si la consulta
    /// queda exportada. Si se escribió en SAP y el resultado se pierde por un corte de red, el
    /// médico ve «pendiente» sobre algo que ya está en la historia — y reintentaría, duplicándolo.
    /// Reenviar el mismo resultado es seguro: la ruta es idempotente.
    /// </remarks>
    private async Task ReportarAsync(string id, string desenlace,
        IReadOnlyList<string>? sinLlenar = null, string? detalle = null, CancellationToken ct = default)
    {
        var cuerpo = JsonSerializer.Serialize(new
        {
            device = Device,
            outcome = desenlace,
            unresolved_fields = sinLlenar is { Count: > 0 } ? sinLlenar : null,
            detail_code = detalle,
        }, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });

        for (int intento = 1; intento <= 5; intento++)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post,
                    $"{_config.BaseUrl.TrimEnd('/')}/api/v1/operations/exports/{id}/result")
                { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") };
                Firmar(req);

                using var res = await Http.SendAsync(req, ct);
                string texto = await res.Content.ReadAsStringAsync(ct);
                if (res.IsSuccessStatusCode)
                {
                    bool exportada = texto.Contains("\"consultation_exported\":true", StringComparison.Ordinal);
                    LogBus.Log("exportar", $"trabajo {id}: reportado «{desenlace}»"
                        + (exportada ? " · la consulta queda EXPORTADA" : ""));
                    return;
                }
                // Un rechazo del servidor no se reintenta: reintentar algo que ya se rechazó por
                // ser inválido solo hace ruido. Los cortes de red sí, que son los de arriba.
                LogBus.Log("exportar", $"trabajo {id}: el servidor rechazó el resultado (HTTP {(int)res.StatusCode})");
                return;
            }
            catch (Exception e) when (intento < 5)
            {
                LogBus.Log("exportar", $"trabajo {id}: no pude reportar (intento {intento}): {e.Message}");
                try { await Task.Delay(TimeSpan.FromSeconds(intento * 2), ct); } catch { return; }
            }
            catch (Exception e)
            {
                LogBus.Log("exportar", $"trabajo {id}: NO SE PUDO REPORTAR el resultado: {e.Message}. "
                    + "Lo escrito en SAP está, pero el médico verá el trabajo como pendiente.");
                return;
            }
        }
    }

    private void Firmar(HttpRequestMessage req)
    {
        req.Headers.Add("X-API-Key", _config.ApiKey ?? "");
        if (_config.OperatorEmail.Length > 0) req.Headers.Add("X-Miracle-User-Email", _config.OperatorEmail);
    }

    private static string Ruta(JsonElement raiz, string a, string b) =>
        raiz.TryGetProperty(a, out var uno) && uno.ValueKind == JsonValueKind.Object
        && uno.TryGetProperty(b, out var dos) && dos.ValueKind == JsonValueKind.String
            ? dos.GetString() ?? "" : "";

    public void Dispose() => Parar();
}
