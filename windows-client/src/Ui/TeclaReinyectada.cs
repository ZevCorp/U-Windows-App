using System.Runtime.InteropServices;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Ui;

/// <summary>
/// Devuelve al sistema una tecla que el gancho se tragó (spec 008: escribirle a la carita sin
/// botón). Es la MISMA tecla física —vk, scan y si era extendida— y no un carácter calculado, para
/// que Windows la traduzca con la distribución, el Shift y las teclas muertas de siempre. Lo que
/// <c>SendInput</c> inyecta lleva <c>LLKHF_INJECTED</c>, y el gancho deja pasar lo inyectado: no
/// hay bucle.
/// </summary>
/// <remarks>
/// Aparte de <c>InputExecutor</c> a propósito: aquel es la mano del AGENTE y pasa por el freno
/// (promesa 26: con el freno echado nada llega al teclado). Esto es la tecla del USUARIO, que ya la
/// pulsó; frenarla sería quitarle una letra que escribió él.
///
/// La estructura INPUT lleva la unión entera (ratón incluido) aunque solo se use el teclado: el
/// tamaño que se le pasa a SendInput tiene que ser el de la estructura real de Windows (40 bytes en
/// x64), y una versión «solo teclado» mide 32 y falla con ERROR_INVALID_PARAMETER sin decir por qué.
/// </remarks>
internal static class TeclaReinyectada
{
    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public InputUnion U; }
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001, KEYEVENTF_KEYUP = 0x0002;
    /// <summary>Bit de <c>KBDLLHOOKSTRUCT.flags</c>: la tecla es extendida (flechas, Supr, el Enter del numérico…).</summary>
    private const uint LLKHF_EXTENDED = 0x01;

    /// <summary>Pulsa y suelta la tecla tal como llegó al gancho.</summary>
    public static void Pulsar(uint vk, uint scan, uint flagsDelGancho)
    {
        uint ext = (flagsDelGancho & LLKHF_EXTENDED) != 0 ? KEYEVENTF_EXTENDEDKEY : 0;
        var pulsacion = new[] { Tecla(vk, scan, ext), Tecla(vk, scan, ext | KEYEVENTF_KEYUP) };
        uint enviadas = SendInput((uint)pulsacion.Length, pulsacion, Marshal.SizeOf<INPUT>());
        if (enviadas != pulsacion.Length)
            LogBus.Log("carita", $"reinyectar la tecla 0x{vk:X2} devolvió {enviadas}/2 (error Win32 {Marshal.GetLastWin32Error()})");
    }

    private static INPUT Tecla(uint vk, uint scan, uint flags) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion { ki = new KEYBDINPUT { wVk = (ushort)vk, wScan = (ushort)scan, dwFlags = flags } },
    };
}
