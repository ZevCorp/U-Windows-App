using System.Security.Cryptography;

namespace Omi;

/// <summary>
/// El código que ata el teléfono de un médico a SU pantalla, y el enlace que lo lleva.
///
/// El problema que resuelve: la app de Omi manda el audio a una dirección que el médico pega a
/// mano. Esa dirección tiene que decir a quién pertenece el audio, porque el servidor la recibe de
/// un teléfono cualquiera de internet y tiene que saber en qué consulta ponerlo. Y el médico no
/// puede llevar su credencial de la web dentro de un ajuste de una app de terceros: se copia, se
/// comparte por WhatsApp y no caduca. Por eso va un código de un solo uso y vida corta, igual que
/// el puente con el portal clínico.
///
/// EL ENLACE VA A VIAJAR MAL, y el diseño lo asume: se pega en el teléfono equivocado, se copia a
/// medias, y dos médicos del mismo servicio reutilizan el de uno. Por eso el rechazo tiene que
/// DISTINGUIR SUS CAUSAS —no venía código, el código no cuadra, el código caducó— en vez de decir
/// «error». Es el aprendizaje nº2, que este repo ya incumplió una vez al escribirlo.
///
/// Sólo el JUICIO vive aquí. Recibir el WebSocket es del servidor y pintar el enlace es de la
/// ventana; las dos cosas preguntan a esta clase y ninguna decide por su cuenta.
/// </summary>
public static class Emparejamiento
{
    /// <summary>
    /// Cuánto vive un código. Ocho horas, que es el turno, y el mismo número que el puente con el
    /// portal clínico: un médico enlaza al empezar y no vuelve a pensarlo hasta el día siguiente.
    ///
    /// No es más largo a propósito. El código es lo único que separa el audio de una consulta de
    /// cualquiera que tenga la dirección, y un código de días es un código que acaba en una libreta.
    /// </summary>
    public const long VidaMs = 8L * 60L * 60L * 1000L;

    /// <summary>
    /// Alfabeto del código: sin las letras y números que se confunden al leerlos en voz alta o al
    /// copiarlos a mano de una pantalla a un teléfono — nada de O/0, I/1/l, S/5, B/8.
    /// </summary>
    private const string Alfabeto = "ACDEFGHJKMNPQRTUVWXY34679";

    /// <summary>Un código nuevo, con azar criptográfico. Ocho caracteres del alfabeto de arriba.</summary>
    public static string Nuevo(int largo = 8)
    {
        var salida = new char[largo];
        Span<byte> bytes = stackalloc byte[largo];
        RandomNumberGenerator.Fill(bytes);
        for (int i = 0; i < largo; i++) salida[i] = Alfabeto[bytes[i] % Alfabeto.Length];
        return new string(salida);
    }

    /// <summary>
    /// El enlace que el médico copia y pega en la app de Omi.
    ///
    /// El código va en la consulta y no en la ruta porque Omi CONSERVA los parámetros que ya trae la
    /// URL y añade los suyos detrás — medido el 2026-09-01: llegó intacto junto a los
    /// <c>sample_rate</c> y <c>uid</c> que pone Omi. Una ruta con el código dentro también valdría,
    /// pero entonces cada cambio de forma de la ruta rompería los enlaces ya pegados en teléfonos.
    /// </summary>
    public static string Enlace(string baseUrl, string codigo)
    {
        var limpio = (baseUrl ?? "").TrimEnd('?', '&');
        var separador = limpio.Contains('?') ? "&" : "?";
        return $"{limpio}{separador}code={codigo}";
    }

    /// <summary>
    /// Por qué se rechaza un código, o cadena vacía si no hay nada que rechazar.
    ///
    /// Cada causa tiene su frase, y son distintas entre sí a propósito: «no venía ninguno» manda a
    /// mirar si el enlace se pegó entero, y «no cuadra» manda a mirar de qué PC salió. Un mensaje
    /// que cubriera las dos mandaría la mitad de las investigaciones al sitio equivocado.
    /// </summary>
    public static string Motivo(string esperado, string recibido)
    {
        if (string.IsNullOrWhiteSpace(recibido))
            return "no venía ningún código en la dirección: el enlace se pegó incompleto";

        if (!string.Equals(esperado, recibido, StringComparison.Ordinal))
            return $"el código «{Recorta(recibido)}» no cuadra con el de esta consulta: "
                 + "o el enlace es de otro computador, o se copió a medias";

        return "";
    }

    /// <summary>
    /// Qué contestarle a Omi según la edad del código: 200 mientras viva, 410 cuando caduque.
    ///
    /// EL 410 NO ES COSMÉTICO Y ESTO SE MIDIÓ. Omi apaga el envío del usuario tras CIEN respuestas
    /// seguidas que no sean 2xx, y lo hace sin avisar a nadie. Un código caducado que conteste 200
    /// deja al teléfono mandando audio a un destino muerto indefinidamente; uno que conteste 410
    /// gasta ese presupuesto a propósito, y el envío se apaga solo — que es exactamente lo que
    /// queremos cuando el código ya no vale.
    /// </summary>
    public static int Estado(long emitidoMs, long ahoraMs)
        => ahoraMs - emitidoMs < VidaMs ? 200 : 410;

    /// <summary>Vivo o no, dicho en una palabra para quien no quiera hablar en códigos HTTP.</summary>
    public static bool Vivo(long emitidoMs, long ahoraMs) => Estado(emitidoMs, ahoraMs) == 200;

    /// <summary>Cuánto le queda de vida, en minutos, para poder decírselo al médico.</summary>
    public static int MinutosDeVida(long emitidoMs, long ahoraMs)
    {
        var resto = VidaMs - (ahoraMs - emitidoMs);
        return resto <= 0 ? 0 : (int)(resto / 60000L);
    }

    /// <summary>Un código que se recorta antes de escribirlo en un log: es una credencial, corta.</summary>
    private static string Recorta(string s) => s.Length <= 4 ? s : s[..4] + "…";
}
