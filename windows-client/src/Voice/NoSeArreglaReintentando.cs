namespace U.WindowsClient.Voice;

/// <summary>
/// POR QUÉ UN FALLO DEL SERVIDOR DE VOZ NO SE ARREGLA RECONECTANDO (spec 018, promesa 223). Pura y sin estado.
/// </summary>
/// <remarks>
/// Nace del nivel 4 del 2026-09-12, con la cuenta sin crédito. GPT Realtime cerró con 1013
/// «insufficient_quota.credit_balance_exhausted» y la conversación reconectó cuatro veces con la misma cuenta
/// («reconectada SIN continuidad … intento 1..4», «se cayó 5 veces seguidas: se deja»). GPT-Live, que confirma la
/// apertura (la 49 de la voz), dijo la causa una vez. La misma clase de error, con dos tratamientos.
///
/// LOS CÓDIGOS SON LOS QUE CONTESTÓ EL SERVIDOR, no los de la documentación. Sonda del 2026-09-13, con .NET 8 y el
/// mismo ClientWebSocket de la app:
///  · sin crédito: credit_balance_exhausted (error de GPT-Live, 2026-09-12) e insufficient_quota.credit_balance_exhausted
///    (cierre 1013 de Realtime, 2026-09-12);
///  · la clave: invalid_api_key (error y cierre 3000 de Realtime) y HTTP 401 en el apretón de manos de GPT-Live, que no
///    llega a abrir el socket;
///  · el modelo: invalid_model (GPT-Live) y model_not_found (error y cierre 4004 de Realtime).
/// Las dos voces son de OpenAI y comparten estos códigos. La conversación no sabe de quién son: solo los pasa.
///
/// SE COMPARAN PALABRAS ENTERAS, separadas por punto o espacio. La descripción de un cierre es «type.code», y un
/// Contains leería causas dentro de la prosa —«The server returned status code '401' when…», que es una frase de .NET
/// y no un código—. El número del cierre tampoco es la causa: 1013 significa «vuelve a intentarlo» en el RFC 6455, y el
/// servidor lo usó para decir que no había crédito.
/// </remarks>
public static class NoSeArreglaReintentando
{
    // Una frase por causa, y ninguna nombra la palabra de otra: quien lea el log tiene que poder distinguirlas
    // (patrón nº2). «Cuenta» sale en dos, y por eso la causa se nombra con «crédito», «clave» y «modelo».
    private const string SinCredito = "la cuenta no tiene crédito";
    private const string ClaveQueNoVale = "la clave (OPENAI_API_KEY) no vale";
    private const string ModeloQueNoEsta = "el modelo no existe o esta cuenta no tiene acceso a él";

    private static readonly Dictionary<string, string> Causas = new(StringComparer.Ordinal)
    {
        ["credit_balance_exhausted"] = SinCredito,
        ["insufficient_quota"] = SinCredito,
        ["invalid_api_key"] = ClaveQueNoVale,
        ["401"] = ClaveQueNoVale,
        ["invalid_model"] = ModeloQueNoEsta,
        ["model_not_found"] = ModeloQueNoEsta,
    };

    /// <param name="codigo">
    /// El code de un error (<see cref="Voz.Realtime.Hecho.Falla.Codigo"/>), la descripción de un cierre del socket
    /// («type.code») o el estado HTTP con que se rechazó el apretón de manos.
    /// </param>
    /// <returns>La causa, dicha para una persona; vacía si puede ser un corte y volver a conectar tiene sentido.</returns>
    public static string PorQue(string codigo)
    {
        foreach (string palabra in (codigo ?? "").Split(new[] { '.', ' ' },
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (Causas.TryGetValue(palabra, out string? causa)) return causa;
        return "";
    }
}
