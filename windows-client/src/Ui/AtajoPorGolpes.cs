using System.Runtime.InteropServices;
using System.Windows.Input;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Ui;

/// <summary>
/// Atajos que son SOLO modificadores: pulsar Ctrl+Shift dos veces seguidas, por ejemplo.
/// </summary>
/// <remarks>
/// <c>RegisterHotKey</c> —lo que usa <see cref="GlobalHotkeys"/>— exige modificador MÁS tecla, así
/// que no sirve para esto. Y un atajo de solo modificadores tiene una ventaja que ninguna
/// combinación con letra puede dar: no le quita esa letra a nadie. En un teclado es-LA eso importa
/// más de lo que parece —AltGr es Ctrl+Alt, así que Ctrl+Alt+E le quitaría el € a todo el sistema—,
/// y ese cuidado ya está escrito en GlobalHotkeys.
///
/// UN GOLPE ES PULSAR Y SOLTAR SIN ESCRIBIR NADA. Ctrl+Shift se pulsa constantemente como parte de
/// otras cosas (Ctrl+Shift+V, seleccionar palabras...), así que contar «los dos están pulsados»
/// dispararía el atajo mientras alguien pega texto. Solo cuenta si entre pulsar y soltar no se tocó
/// ninguna otra tecla: eso ya no es un atajo de otra cosa, es un gesto deliberado.
///
/// Y DOS GOLPES Y NO UNO, por lo mismo: un golpe suelto ocurre sin querer al soltar un Ctrl+Shift+X
/// en mal orden. Dos seguidos, no.
///
/// El gancho se registra una vez y vive lo que la aplicación. Es de bajo nivel y global, como los
/// que ya hay para el ratón y para la rueda, y como ellos: si falla, se anota y se sigue — que no
/// haya atajo no puede impedir que la aplicación arranque.
/// </remarks>
public sealed class AtajoPorGolpes : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;

    /// <summary>Cuánto puede tardar el segundo golpe. Más y se cuela un Ctrl+Shift de otra cosa.</summary>
    private static readonly TimeSpan Seguidos = TimeSpan.FromMilliseconds(600);

    private delegate IntPtr Gancho(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, Gancho fn, IntPtr mod, uint hilo);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint Vk, Scan, Flags, Time;
        public IntPtr Extra;
    }

    private readonly Action _soloCtrl;
    private readonly Action _ctrlShift;
    private Gancho? _fn;             // referencia viva: si se la lleva el recolector, Windows cae
    private IntPtr _h;

    private bool _acordeLimpio;      // desde que se pulsó el acorde no se ha tocado otra tecla
    private bool _huboShift;         // ...y si en ese acorde llegó a haber Shift
    private DateTime _ultimoSolo = DateTime.MinValue, _ultimoConShift = DateTime.MinValue;

    /// <param name="soloCtrl">Doble Ctrl. Es el gesto de Apple para dictar —allí doble Fn—, y aquí
    /// tiene que ser Ctrl porque la Fn NO LLEGA al software: la resuelve el teclado y Windows no la
    /// ve nunca. Medido en la máquina del usuario: pulsando Ctrl+Fn 59 veces solo llegó el Ctrl
    /// (vk=0xA2), ni una vez la Fn (2026-08-06).</param>
    /// <param name="ctrlShift">Doble Ctrl+Shift.</param>
    public AtajoPorGolpes(Action soloCtrl, Action ctrlShift)
    {
        _soloCtrl = soloCtrl;
        _ctrlShift = ctrlShift;
        _fn = Teclado;
        _h = SetWindowsHookEx(WH_KEYBOARD_LL, _fn, IntPtr.Zero, 0);
        LogBus.Log("atajo", _h != IntPtr.Zero
            ? "doble Ctrl (voz) y doble Ctrl+Shift (panel): activos"
            : "doble Ctrl / Ctrl+Shift: NO se pudo enganchar el teclado");
    }

    private static bool Pulsada(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;
    private static bool Ctrl => Pulsada(0x11);
    private static bool Shift => Pulsada(0x10);

    private IntPtr Teclado(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            int msg = (int)(IntPtr.Size == 8 ? wParam.ToInt64() : wParam.ToInt32());
            var k = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            bool esModificador = k.Vk is 0x10 or 0x11 or 0xA0 or 0xA1 or 0xA2 or 0xA3;

            if (msg is WM_KEYDOWN or WM_SYSKEYDOWN)
            {
                // Cualquier tecla que no sea modificador ensucia el acorde: ya es otro atajo.
                if (!esModificador) { _acordeLimpio = false; _huboShift = false; }
                else if (Ctrl)
                {
                    _acordeLimpio = true;
                    if (Shift) _huboShift = true;   // el acorde ya no es «Ctrl solo»
                }
            }
            else if (msg is WM_KEYUP or WM_SYSKEYUP && esModificador)
            {
                // Se cuenta al soltar, cuando ya no queda Ctrl pulsado: ese es el final del gesto.
                if (_acordeLimpio && !Ctrl)
                {
                    bool conShift = _huboShift;
                    _acordeLimpio = false;
                    _huboShift = false;

                    var ahora = DateTime.UtcNow;
                    // Cada gesto lleva su propia cuenta: si compartieran una, un Ctrl+Shift seguido
                    // de un Ctrl a secas contaría como pareja y dispararía lo que no toca.
                    ref var ultimo = ref (conShift ? ref _ultimoConShift : ref _ultimoSolo);
                    if (ahora - ultimo <= Seguidos)
                    {
                        ultimo = DateTime.MinValue;   // dos golpes son UN gesto, no tres
                        _ultimoSolo = _ultimoConShift = DateTime.MinValue;
                        try { (conShift ? _ctrlShift : _soloCtrl)(); }
                        catch (Exception e) { LogBus.Log("atajo", $"falló: {e.Message}"); }
                    }
                    else
                    {
                        ultimo = ahora;
                        // Empezar un gesto invalida el otro: no se está a medias de los dos.
                        if (conShift) _ultimoSolo = DateTime.MinValue;
                        else _ultimoConShift = DateTime.MinValue;
                    }
                }
            }
        }
        return CallNextHookEx(_h, code, wParam, lParam);
    }

    public void Dispose()
    {
        if (_h == IntPtr.Zero) return;
        UnhookWindowsHookEx(_h);
        _h = IntPtr.Zero;
        _fn = null;
    }
}
