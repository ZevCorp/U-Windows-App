using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace U.WindowsClient.Radicacion;

/// <summary>
/// Este equipo ya tiene un gremlin de red documentado: el DNS de la máquina puede resolver un host
/// solo por IPv6 y la conexión se cuelga o sale «host desconocido» aunque el resto del tráfico ande
/// bien (ver CLAUDE.md, aprendizaje del 2026-08-06, que golpeó a MaestroDeApps contra Gemini). Aquí
/// pegó igual contra generativelanguage.googleapis.com. La solución no es reintentar más —ya se
/// reintenta 4 veces con backoff en ClasificadorDocumento— es forzar IPv4 primero en la conexión:
/// SocketsHttpHandler.ConnectCallback deja elegir a qué dirección conectarse, en vez de dejárselo al
/// resolutor por defecto del proceso.
/// </summary>
public static class RedResiliente
{
    public static HttpClient ClienteHttp(TimeSpan timeout)
    {
        var handler = new SocketsHttpHandler { ConnectCallback = ConectarPrefiriendoIPv4 };
        return new HttpClient(handler) { Timeout = timeout };
    }

    private static async ValueTask<System.IO.Stream> ConectarPrefiriendoIPv4(
        SocketsHttpConnectionContext contexto, CancellationToken ct)
    {
        var direcciones = await Dns.GetHostAddressesAsync(contexto.DnsEndPoint.Host, ct);
        Array.Sort(direcciones, (a, b) =>
            (a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1)
                .CompareTo(b.AddressFamily == AddressFamily.InterNetwork ? 0 : 1));

        Exception? ultimoError = null;
        foreach (var direccion in direcciones)
        {
            var socket = new Socket(direccion.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(direccion, contexto.DnsEndPoint.Port, ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception e)
            {
                ultimoError = e;
                socket.Dispose();
            }
        }
        throw new HttpRequestException($"no se pudo conectar a {contexto.DnsEndPoint.Host}: {ultimoError?.Message}", ultimoError);
    }
}
