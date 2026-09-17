using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace U.Graph.Surfaces;

/// <summary>
/// LA API DE ESCRITORIOS VIRTUALES DE WINDOWS, la pública y solo la pública. Promesa 270 (spec 031).
/// </summary>
/// <remarks>
/// LO QUE WINDOWS DA CON API DOCUMENTADA es <c>IVirtualDesktopManager</c> (shell32, desde Windows 10
/// 1607, mismos GUID en todas las compilaciones): en qué escritorio está una ventana, si está en el
/// que se ve, y mover una ventana PROPIA a otro escritorio. Medido el 2026-09-17 con
/// <c>scripts/sonda-escritorios*.ps1</c>:
///
///   - mover una ventana propia: S_OK en 5 ms; la ventana queda cloaked = 2 (DWM_CLOAKED_SHELL),
///     IsWindowVisible sigue diciendo que sí, y el escritorio a la vista NO cambia;
///   - mover una ventana AJENA (charmap.exe): E_ACCESSDENIED (0x80070005). Ü no muda ventanas de
///     otros, y no es una decisión: es lo que la API deja;
///   - <c>GetWindowDesktopId</c> contesta para cualquier ventana, propia o ajena; para las que no
///     son de nadie (Program Manager) devuelve TYPE_E_ELEMENTNOTFOUND y aquí sale <see cref="Guid.Empty"/>.
///
/// LO QUE NO DA: enumerar los escritorios, saber cuál está a la vista, cambiar de escritorio, y
/// fijar una ventana en todos. Enumerar y saber el actual se lee del registro
/// (<see cref="ReglaDelEscritorio"/>); cambiar es el atajo del sistema (<c>Win+Ctrl+←/→</c>); fijar
/// es la interfaz interna, que vive aparte y con respaldo (fase 5). Nada de eso está aquí para que
/// esta clase no dependa jamás de una compilación concreta de Windows.
///
/// EL OBJETO COM SE CREA POR LLAMADA, no se guarda: se llama desde el hilo de la interfaz y desde
/// el del servidor MCP, y un puntero COM cacheado en un hilo y usado en otro es exactamente la
/// clase de fallo que no deja rastro.
/// </remarks>
public static class EscritorioVirtual
{
    [ComImport, Guid("aa509086-5ca9-4c25-8f95-589d3c07b48a")]
    private class VirtualDesktopManagerCls { }

    [ComImport, Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManager
    {
        [PreserveSig] int IsWindowOnCurrentVirtualDesktop(IntPtr hwnd, out int onCurrent);
        [PreserveSig] int GetWindowDesktopId(IntPtr hwnd, out Guid id);
        [PreserveSig] int MoveWindowToDesktop(IntPtr hwnd, ref Guid id);
    }

    private const string Clave = @"Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops";

    private static IVirtualDesktopManager? Manager()
    {
        try { return (IVirtualDesktopManager)new VirtualDesktopManagerCls(); }
        catch (COMException e) { UiaSurface.LogGlobal?.Invoke($"escritorio: sin IVirtualDesktopManager ({e.HResult:X8})"); return null; }
    }

    /// <summary>En qué escritorio está la ventana; vacío si el sistema no la ubica o la API no está.</summary>
    public static Guid EscritorioDe(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return Guid.Empty;
        var m = Manager();
        if (m == null) return Guid.Empty;
        return m.GetWindowDesktopId(hwnd, out var id) == 0 ? id : Guid.Empty;
    }

    /// <summary>¿La ventana está en el escritorio que se ve? Sin API, se supone que sí (es lo de siempre).</summary>
    public static bool EstaEnElActual(IntPtr hwnd)
    {
        var m = Manager();
        if (m == null || hwnd == IntPtr.Zero) return true;
        return m.IsWindowOnCurrentVirtualDesktop(hwnd, out int on) != 0 || on != 0;
    }

    /// <summary>Mueve una ventana PROPIA a un escritorio. Devuelve el HRESULT: 0 si se movió; 0x80070005 si es ajena.</summary>
    public static int Mover(IntPtr hwnd, Guid destino)
    {
        var m = Manager();
        if (m == null) return unchecked((int)0x80004005);   // E_FAIL: no hay API
        return m.MoveWindowToDesktop(hwnd, ref destino);
    }

    /// <summary>El escritorio a la vista, según el registro; vacío si no se puede leer.</summary>
    public static Guid Actual()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(Clave);
            return k?.GetValue("CurrentVirtualDesktop") is byte[] b && b.Length == 16 ? new Guid(b) : Guid.Empty;
        }
        catch (Exception e) { UiaSurface.LogGlobal?.Invoke($"escritorio: no se pudo leer el actual: {e.GetType().Name}: {e.Message}"); return Guid.Empty; }
    }

    /// <summary>Todos los escritorios, en el orden de la vista de tareas; vacío si no se puede leer.</summary>
    public static Guid[] Orden()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(Clave);
            return k?.GetValue("VirtualDesktopIDs") is byte[] b ? ReglaDelEscritorio.Orden(b) : Array.Empty<Guid>();
        }
        catch (Exception e) { UiaSurface.LogGlobal?.Invoke($"escritorio: no se pudo leer la lista: {e.GetType().Name}: {e.Message}"); return Array.Empty<Guid>(); }
    }

    /// <summary>El nombre que la persona le puso a un escritorio, o vacío si no le puso ninguno.</summary>
    public static string NombrePuesto(Guid id)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(Clave + @"\Desktops\" + id.ToString("B"));
            return (k?.GetValue("Name") as string ?? "").Trim();
        }
        catch { return ""; }
    }

    /// <summary>Cómo se llama para la persona: su nombre, o «Escritorio N» por su posición.</summary>
    public static string Nombre(Guid id) => Nombre(id, Orden());

    public static string Nombre(Guid id, Guid[] orden) => ReglaDelEscritorio.NombreDe(id, orden, NombrePuesto(id));
}
