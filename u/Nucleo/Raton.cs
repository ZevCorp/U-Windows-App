using System.Runtime.InteropServices;

namespace U.Ciclo;

/// <summary>
/// EL RATÓN REAL, y nada más (promesa 433). Decisión del dueño, 2026-09-24: «pongámoslo a controlar el
/// mouse». Main tenía una escalera de gestos —patrón UIA, mensaje a la ventana, ratón suavizado— con
/// esperas fijas de 20-200 ms entre peldaños: 470-540 ms por clic. SetCursorPos + SendInput cuesta 0,46 ms.
/// </summary>
public static class Raton
{
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint n, INPUT[] entradas, int tam);
    [DllImport("user32.dll")] private static extern IntPtr SetProcessDpiAwarenessContext(IntPtr valor);

    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint Tipo; public UNION U; }
    [StructLayout(LayoutKind.Explicit)] private struct UNION { [FieldOffset(0)] public MOUSEINPUT M; [FieldOffset(0)] public KEYBDINPUT K; }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int Dx, Dy; public uint Datos, Flags, Tiempo; public IntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort Vk, Scan; public uint Flags, Tiempo; public IntPtr Extra; }

    private const uint IzquierdoAbajo = 0x0002, IzquierdoArriba = 0x0004;

    /// <summary>
    /// Coordenadas físicas en todo el proceso: UIA da la caja en píxeles físicos y SetCursorPos solo los
    /// entiende así si el proceso es consciente del DPI por monitor. Sin esto, en una pantalla al 150 % el
    /// clic cae desplazado — contención no es alineación (patrón nº7).
    /// </summary>
    public static void AsegurarDpi()
    {
        try { SetProcessDpiAwarenessContext(new IntPtr(-4) /* PER_MONITOR_AWARE_V2 */); } catch { }
    }

    public static (int X, int Y) Centro(Caja c) => (c.X + c.Ancho / 2, c.Y + c.Alto / 2);

    /// <summary>Lo que se va a hacer, dicho. Es lo que juzga el contrato; <see cref="Clic"/> lo ejecuta tal cual.</summary>
    public static IReadOnlyList<string> Gesto(int x, int y) =>
        new[] { $"mover {x},{y}", "izquierdo abajo", "izquierdo arriba" };

    public static void Clic(int x, int y)
    {
        SetCursorPos(x, y);
        var e = new INPUT[]
        {
            new() { Tipo = 0, U = new UNION { M = new MOUSEINPUT { Flags = IzquierdoAbajo } } },
            new() { Tipo = 0, U = new UNION { M = new MOUSEINPUT { Flags = IzquierdoArriba } } },
        };
        SendInput((uint)e.Length, e, Marshal.SizeOf<INPUT>());
    }

    public static void Clic(Accionable a) { var (x, y) = Centro(a.Caja); Clic(x, y); }

    /// <summary>Texto por teclado real, en Unicode: vale para cualquier distribución de teclado.</summary>
    /// <summary>
    /// Pausa entre letras al escribir, en ms (promesa 460). DE UN SOLO LOTE, el Bloc de notas de Windows 11 cambiaba
    /// letras por otras —«tercera prueba de la aaaaaaaaa»—: 4 de 5 bien sin pausa, 5 de 5 con 3 ms, y también bien
    /// con 2, 5 y 10 (medido en esta máquina el 2026-09-25, 05:38). Cuesta ~3 ms por letra. U_PAUSA_LETRAS la
    /// cambia, para volver a medir en otra máquina.
    /// </summary>
    public static int PausaEntreLetrasMs { get; set; } =
        int.TryParse(Environment.GetEnvironmentVariable("U_PAUSA_LETRAS"), out var p) && p > 0 ? p : 3;

    public static void Escribir(string texto)
    {
        var e = new List<INPUT>();
        foreach (char ch in texto ?? "")
        {
            var par = new[]
            {
                new INPUT { Tipo = 1, U = new UNION { K = new KEYBDINPUT { Scan = ch, Flags = 0x0004 } } },
                new INPUT { Tipo = 1, U = new UNION { K = new KEYBDINPUT { Scan = ch, Flags = 0x0004 | 0x0002 } } },
            };
            if (PausaEntreLetrasMs > 0) { SendInput(2, par, Marshal.SizeOf<INPUT>()); Thread.Sleep(PausaEntreLetrasMs); }
            else e.AddRange(par);
        }
        if (e.Count > 0) SendInput((uint)e.Count, e.ToArray(), Marshal.SizeOf<INPUT>());
    }

    /// <summary>Una tecla por su nombre: Enter, Escape, Tab, Abajo, Arriba, Borrar, F1…F12, o «Ctrl+L».</summary>
    public static bool Tecla(string nombre)
    {
        var eventos = Eventos(nombre);
        if (eventos == null) return false;
        if (eventos.Any(x => x.Vk == 0x1B)) _escapePropio = DateTime.Now;
        var e = eventos.Select(x => new INPUT { Tipo = 1, U = new UNION { K = new KEYBDINPUT { Vk = x.Vk, Scan = x.Scan, Flags = x.Flags } } }).ToArray();
        SendInput((uint)e.Length, e, Marshal.SizeOf<INPUT>());
        return true;
    }

    [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint code, uint tipo);

    /// <summary>
    /// Lo que se va a mandar, dicho (promesa 457): «vk scan abajo|arriba [ext]» por evento, en hexadecimal.
    /// </summary>
    /// <remarks>
    /// CON SU CÓDIGO DE EXPLORACIÓN. La primera versión mandaba solo la tecla virtual: el Bloc de notas de Windows 11
    /// (XAML) sigue los modificadores por el código de exploración, así que el Ctrl de «Ctrl+A» no se le soltaba y la
    /// «o» de «nocturna» se volvía Ctrl+O — el diálogo Abrir, cuatro veces en una ronda (2026-09-25, 02:21).
    /// </remarks>
    public static IReadOnlyList<string> EventosDeTecla(string nombre) =>
        (IReadOnlyList<string>?)Eventos(nombre)?.Select(x =>
            $"{x.Vk:X2} {x.Scan:X} {((x.Flags & 0x0002) != 0 ? "arriba" : "abajo")}{((x.Flags & 0x0001) != 0 ? " ext" : "")}").ToList()
        ?? Array.Empty<string>();

    private static readonly HashSet<ushort> Extendidas = new() { 0x25, 0x26, 0x27, 0x28, 0x2E, 0x24, 0x23, 0x5B, 0x21, 0x22, 0x2D };

    private static List<(ushort Vk, ushort Scan, uint Flags)>? Eventos(string nombre)
    {
        var partes = (nombre ?? "").Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var vks = new List<ushort>();
        foreach (var p in partes)
        {
            ushort vk = p.ToLowerInvariant() switch
            {
                "enter" or "intro" => 0x0D, "escape" or "esc" => 0x1B, "tab" => 0x09, "espacio" or "space" => 0x20,
                "borrar" or "backspace" => 0x08, "suprimir" or "delete" => 0x2E, "abajo" or "down" => 0x28,
                "arriba" or "up" => 0x26, "izquierda" or "left" => 0x25, "derecha" or "right" => 0x27,
                "ctrl" or "control" => 0x11, "alt" => 0x12, "shift" or "mayus" => 0x10, "win" => 0x5B,
                "inicio" or "home" => 0x24, "fin" or "end" => 0x23,
                _ when p.Length == 1 && char.IsLetterOrDigit(p[0]) => (ushort)char.ToUpperInvariant(p[0]),
                _ when p.Length >= 2 && (p[0] == 'F' || p[0] == 'f') && int.TryParse(p[1..], out int f) && f is >= 1 and <= 12 => (ushort)(0x6F + f),
                _ => 0,
            };
            if (vk == 0) return null;
            vks.Add(vk);
        }
        if (vks.Count == 0) return null;
        (ushort, ushort, uint) Ev(ushort vk, bool arriba) =>
            (vk, (ushort)MapVirtualKey(vk, 0 /* VK → código de exploración */),
             (arriba ? 0x0002u : 0u) | (Extendidas.Contains(vk) ? 0x0001u : 0u));
        var e = new List<(ushort, ushort, uint)>();
        foreach (var vk in vks) e.Add(Ev(vk, false));
        for (int i = vks.Count - 1; i >= 0; i--) e.Add(Ev(vks[i], true));
        return e;
    }

    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);

    private static DateTime? _escapePropio;

    /// <summary>
    /// ¿Se pulsó Escape? El freno de la promesa 441, pero SOLO el de la persona (promesa 450): en la batería
    /// del 2026-09-24 (23:09) Luna planeó «tecla: Escape» para limpiar la Calculadora y el motor lo tomó
    /// por «paren» — 1 de 5 pasos hechos, 4 omitidos, por un Escape que mandó Ü.
    /// </summary>
    public static bool EscapePulsado()
    {
        bool abajo = (GetAsyncKeyState(0x1B) & 0x0001) != 0 | (GetAsyncKeyState(0x1B) & 0x8000) != 0;
        return EsFrenoDeLaPersona(abajo, DateTime.Now, _escapePropio);
    }

    /// <summary>La regla, pura: un Escape frena salvo que Ü haya mandado el suyo en los últimos 500 ms.</summary>
    public static bool EsFrenoDeLaPersona(bool escapeAbajo, DateTime ahora, DateTime? escapePropio) =>
        escapeAbajo && !(escapePropio is { } p && (ahora - p).TotalMilliseconds is >= 0 and < 500);
}
