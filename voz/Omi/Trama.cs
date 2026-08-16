namespace Omi;

/// <summary>
/// Un paquete tal como sale de la notificación BLE: tres bytes de cabecera y el audio detrás.
///
/// Medido el 2026-08-13 sobre 1.555 paquetes del CV1: la cabecera es
/// <c>[num_lo, num_hi, indice]</c>, y el <c>indice</c> vino SIEMPRE a cero — con el MTU que negocia
/// Windows no hay fragmentación, así que cada notificación es una trama entera. Eso quita el
/// reensamblado, que era la parte molesta.
///
/// Ojo con el número de paquete: es contiguo A TRAVÉS del silencio (se vio un parón de 2,01 s sin
/// que el contador avanzara). Sirve para detectar pérdidas del enlace; NO sirve para saber cuánto
/// tiempo pasó. Para eso está <see cref="Reposicion"/>, que mira el reloj.
/// </summary>
public static class Trama
{
    public const int Cabecera = 3;

    /// <summary>
    /// El audio sin la cabecera, o <c>null</c> si el paquete no da para tanto.
    ///
    /// NULL Y NO EXCEPCIÓN, a propósito: esto se llama desde el callback de BLE, y una excepción ahí
    /// sube por el hilo del transporte y se lleva la sesión de voz por delante. Perder una trama de
    /// 20 ms no se nota; perder la sesión, sí. BLE puede entregar una notificación truncada al
    /// desconectar, y el paquete de sólo cabecera es un caso real cuando el collar cierra.
    /// </summary>
    public static byte[]? Payload(byte[]? paquete)
    {
        if (paquete == null || paquete.Length <= Cabecera) return null;
        var p = new byte[paquete.Length - Cabecera];
        Buffer.BlockCopy(paquete, Cabecera, p, 0, p.Length);
        return p;
    }

    /// <summary>El número de paquete, o -1 si no hay cabecera entera. Little-endian, 0–65535.</summary>
    public static int Numero(byte[]? paquete)
        => paquete == null || paquete.Length < Cabecera ? -1 : paquete[0] | (paquete[1] << 8);

    /// <summary>El índice dentro del paquete, o -1. Medido siempre 0; si algún día no lo es, hay fragmentación.</summary>
    public static int Indice(byte[]? paquete)
        => paquete == null || paquete.Length < Cabecera ? -1 : paquete[2];
}
