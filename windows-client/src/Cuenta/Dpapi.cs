using System.Runtime.InteropServices;

namespace U.WindowsClient.Cuenta;

/// <summary>
/// Cifrado atado a la cuenta de Windows del usuario (DPAPI). Lo que sale de aquí solo lo puede
/// volver a leer la MISMA persona en la MISMA máquina.
/// </summary>
/// <remarks>
/// POR P/INVOKE Y NO POR EL PAQUETE `System.Security.Cryptography.ProtectedData`, que hace justo
/// esto y en tres líneas. La razón está escrita en el csproj a propósito de Concentus: «una
/// dependencia nativa más en el instalador de Velopack es una forma nueva de que la app no arranque
/// en una máquina limpia». `crypt32.dll` viene con Windows desde siempre — no se instala, no se
/// versiona y no puede faltar. Cuarenta líneas se pagan una vez; un instalador que falla en casa de
/// un cliente se paga cada vez.
///
/// QUÉ PROTEGE Y QUÉ NO: protege el refresh token de quien pase por delante del ordenador o mire el
/// disco desde otra cuenta. No protege contra alguien que ya es ese usuario en esa máquina —DPAPI no
/// puede—, y por eso la promesa 86 exige que cerrar sesión BORRE el archivo en vez de confiar en el
/// cifrado: en un ordenador de hospital, el siguiente médico es «otro usuario» de verdad aunque
/// Windows crea que es el mismo.
/// </remarks>
internal static class Dpapi
{
    /// <summary>Sal fija de la aplicación: liga el secreto a Ü, no solo a la cuenta de Windows.</summary>
    private static readonly byte[] Entropia = System.Text.Encoding.UTF8.GetBytes("U.Miracle.Cuenta.v1");

    public static byte[] Proteger(byte[] claro) => Cruzar(claro, proteger: true);

    public static byte[] Revelar(byte[] cifrado) => Cruzar(cifrado, proteger: false);

    private static byte[] Cruzar(byte[] datos, bool proteger)
    {
        var entrada = new DataBlob();
        var entropia = new DataBlob();
        var salida = new DataBlob();
        try
        {
            entrada = Empaquetar(datos);
            entropia = Empaquetar(Entropia);

            bool ok = proteger
                ? CryptProtectData(ref entrada, null, ref entropia, IntPtr.Zero, IntPtr.Zero,
                    CRYPTPROTECT_UI_FORBIDDEN, ref salida)
                : CryptUnprotectData(ref entrada, IntPtr.Zero, ref entropia, IntPtr.Zero, IntPtr.Zero,
                    CRYPTPROTECT_UI_FORBIDDEN, ref salida);

            // SE DICE EL CÓDIGO DE WINDOWS, no «falló el cifrado». Un descifrado que falla porque el
            // archivo viene de otra máquina (NTE_BAD_KEY_STATE) y uno que falla porque el archivo
            // está corrupto se arreglan distinto, y sin el número no se distinguen (aprendizaje nº2).
            if (!ok)
            {
                int codigo = Marshal.GetLastWin32Error();
                throw new InvalidOperationException(
                    $"{(proteger ? "CryptProtectData" : "CryptUnprotectData")} falló con 0x{codigo:X8}");
            }

            var resultado = new byte[salida.cbData];
            Marshal.Copy(salida.pbData, resultado, 0, salida.cbData);
            return resultado;
        }
        finally
        {
            if (entrada.pbData != IntPtr.Zero) Marshal.FreeHGlobal(entrada.pbData);
            if (entropia.pbData != IntPtr.Zero) Marshal.FreeHGlobal(entropia.pbData);
            if (salida.pbData != IntPtr.Zero) LocalFree(salida.pbData);
        }
    }

    private static DataBlob Empaquetar(byte[] datos)
    {
        var blob = new DataBlob { cbData = datos.Length, pbData = Marshal.AllocHGlobal(datos.Length) };
        Marshal.Copy(datos, 0, blob.pbData, datos.Length);
        return blob;
    }

    private const int CRYPTPROTECT_UI_FORBIDDEN = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int cbData;
        public IntPtr pbData;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DataBlob entrada, string? descripcion,
        ref DataBlob entropia, IntPtr reservado, IntPtr prompt, int flags, ref DataBlob salida);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob entrada, IntPtr descripcion,
        ref DataBlob entropia, IntPtr reservado, IntPtr prompt, int flags, ref DataBlob salida);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr mem);
}
