using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace U.Ciclo;

/// <summary>
/// Ü ENTERO, sin la voz: las dos herramientas de Luna —mirar y hacer— montadas sobre el ciclo. La voz y el
/// modo texto usan esto mismo: un solo camino, para que lo que se mide en uno valga para el otro.
/// </summary>
public sealed class Asistente : IDisposable
{
    private readonly LectorUia _lector = new();
    private readonly ClienteJev _jev;

    /// <summary>Todo lo que pasa, en una línea: la burbuja y el log lo escuchan.</summary>
    public Action<string> Log { get; set; } = _ => { };

    /// <summary>El freno: Escape, o que la persona lo pida.</summary>
    public Func<bool> HayQueParar { get; set; } = Raton.EscapePulsado;

    public int MaxPasosPorObjetivo { get; init; } = 8;

    public Asistente(string claveTypeSafe)
    {
        Raton.AsegurarDpi();
        _jev = new ClienteJev(claveTypeSafe) { Umbral = Jev.UmbralPorDefecto };
    }

    public long Calentar() => _jev.Calentar();

    /// <summary>La herramienta «mirar»: qué ventana, qué dice y qué se puede pulsar. Un ciclo sin decidir: ~40 ms.</summary>
    public string Mirar()
    {
        var r = Stopwatch.StartNew();
        var aqui = Donde.Ahora();
        if (aqui == null) return "No hay ninguna ventana delante.";
        var l = _lector.Leer(aqui.Ventana);
        var sb = new StringBuilder();
        sb.Append("Ventana delante: ").Append(aqui.Pantalla).Append('\n');
        if (l.Textos.Count > 0) sb.Append("Dice: ").Append(string.Join(" · ", l.Textos)).Append('\n');
        sb.Append($"Se puede pulsar ({l.Accionables.Count}): ");
        sb.Append(string.Join(", ", l.Accionables.Select(a => $"{a.Nombre} ({a.Tipo})")));
        Log($"👁 mirar en {r.ElapsedMilliseconds} ms · {aqui.Pantalla} · {l.Accionables.Count} accionables");
        return sb.ToString();
    }

    /// <summary>La herramienta «hacer»: el plan de Luna, ejecutado. Devuelve el relato para Luna.</summary>
    public string Hacer(IReadOnlyList<string> pasos)
    {
        Log($"📋 plan de {pasos.Count} paso(s): {string.Join(" → ", pasos)}");
        var motor = new Motor(Donde.Ahora, () => _lector.Leer(Donde.Ahora()?.Ventana ?? IntPtr.Zero), c => _jev.Decidir(c), Raton.Clic, HayQueParar)
        {
            AlTerminarVuelta = v => Log($"   ⏱ {v.Tiempos.Linea()} · {(v.Elegida.Length > 0 ? "pulsé " + v.Elegida : v.Resultado)}"),
        };
        var ejecutor = new Ejecutor(
            app => Apps.Abrir(app).Llego,
            texto => { Raton.Escribir(texto); Thread.Sleep(30); },
            tecla => { bool ok = Raton.Tecla(tecla); Thread.Sleep(60); return ok; },
            (objetivo, hecho) => motor.Objetivo(objetivo, MaxPasosPorObjetivo, hecho),
            HayQueParar)
        { AlTerminarPaso = l => Log("   " + l) };
        var r = ejecutor.Ejecutar(pasos);
        Log("↩ " + r.Resultado.Resumen);
        return r.Relato() + "\n\nAhora:\n" + Mirar();
    }

    /// <summary>Atiende una llamada de Luna por su nombre. Lo desconocido se dice, no se ignora.</summary>
    public string Atender(string nombre, string argumentos)
    {
        try
        {
            switch (nombre)
            {
                case "mirar": return Mirar();
                case "hacer":
                    using (var d = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentos) ? "{}" : argumentos))
                    {
                        if (!d.RootElement.TryGetProperty("pasos", out var p) || p.ValueKind != JsonValueKind.Array)
                            return "«hacer» necesita «pasos»: una lista de pasos.";
                        var pasos = p.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String)
                            .Select(x => x.GetString() ?? "").Where(x => x.Trim().Length > 0).ToList();
                        return pasos.Count == 0 ? "El plan llegó vacío: no hice nada." : Hacer(pasos);
                    }
                default: return $"No conozco la herramienta «{nombre}». Tengo «hacer» y «mirar».";
            }
        }
        catch (Exception e)
        {
            string causa = "";
            for (var x = e; x != null; x = x.InnerException) causa += (causa.Length > 0 ? " ← " : "") + $"{x.GetType().Name}: {x.Message}";
            Log("✘ " + causa);
            return "Falló al ejecutar: " + causa;
        }
    }

    public void Dispose() { _lector.Dispose(); _jev.Dispose(); }
}

/// <summary>
/// LUNA POR TEXTO, sin voz: la Responses API con las mismas dos herramientas. Sirve para probar el camino
/// entero —pedido → Luna → Jev— sin micrófono, y como respaldo cuando la voz no está.
/// </summary>
public sealed class LunaPorTexto : IDisposable
{
    private readonly HttpClient _http;
    public Action<string> Log { get; set; } = _ => { };

    public LunaPorTexto(string claveOpenAI)
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", claveOpenAI);
    }

    /// <summary>Hasta que Luna conteste con palabras, o 8 turnos de herramientas.</summary>
    public string Pedir(string pedido, Asistente ü)
    {
        string? anterior = null;
        object entrada = pedido;
        for (int turno = 0; turno < 8; turno++)
        {
            var cuerpo = new Dictionary<string, object?>
            {
                ["model"] = ProtocoloVivo.Luna,
                ["instructions"] = ProtocoloVivo.InstruccionesDeLuna,
                ["input"] = entrada,
                ["tools"] = ProtocoloVivo.Herramientas(),
                ["reasoning"] = new { effort = "low" },
            };
            if (anterior != null) cuerpo["previous_response_id"] = anterior;
            var r = Stopwatch.StartNew();
            using var res = _http.PostAsync("https://api.openai.com/v1/responses",
                new StringContent(JsonSerializer.Serialize(cuerpo), Encoding.UTF8, "application/json")).GetAwaiter().GetResult();
            string json = res.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!res.IsSuccessStatusCode) return $"Luna contestó HTTP {(int)res.StatusCode}: {(json.Length > 300 ? json[..300] : json)}";
            using var doc = JsonDocument.Parse(json);
            anterior = doc.RootElement.GetProperty("id").GetString();

            var salidas = new List<object>();
            string texto = "";
            foreach (var item in doc.RootElement.GetProperty("output").EnumerateArray())
            {
                string tipo = ProtocoloVivo.Texto(item, "type");
                if (tipo == "function_call")
                {
                    string nombre = ProtocoloVivo.Texto(item, "name"), args = ProtocoloVivo.Texto(item, "arguments");
                    Log($"🌙 Luna ({r.ElapsedMilliseconds} ms) → {nombre} {args}");
                    salidas.Add(new { type = "function_call_output", call_id = ProtocoloVivo.Texto(item, "call_id"), output = ParaLuna.Recortar(ü.Atender(nombre, args)) });
                }
                else if (tipo == "message")
                    foreach (var c in item.GetProperty("content").EnumerateArray())
                        texto += ProtocoloVivo.Texto(c, "text");
            }
            if (salidas.Count == 0) { Log($"🌙 Luna ({r.ElapsedMilliseconds} ms): {texto}"); return texto; }
            entrada = salidas;
        }
        return "Luna usó 8 turnos de herramientas sin terminar: paro.";
    }

    public void Dispose() => _http.Dispose();
}
