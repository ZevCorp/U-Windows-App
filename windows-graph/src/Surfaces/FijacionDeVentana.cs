using System.Runtime.InteropServices;

namespace U.Graph.Surfaces;

/// <summary>
/// FIJAR UNA VENTANA EN TODOS LOS ESCRITORIOS durante un viaje, y soltarla al llegar. Promesa 274 (spec 031).
/// </summary>
/// <remarks>
/// ES LA ÚNICA PIEZA QUE USA UNA INTERFAZ INTERNA DE WINDOWS, y por eso vive sola, con su respaldo
/// escrito. La vista de tareas ofrece «mostrar esta ventana en todos los escritorios»: eso es fijar,
/// y es exactamente lo que el dueño pidió para el viaje —«la aplicación quedándose enfrente mío
/// mientras el escritorio cambia por detrás»—. Windows no lo expone con API documentada; lo hace
/// por <c>IVirtualDesktopPinnedApps</c>, un servicio del shell inmersivo cuyos GUID llevan iguales
/// desde Windows 10, a diferencia de <c>IVirtualDesktopManagerInternal</c>, que cambia de
/// compilación en compilación y aquí no se toca.
///
/// SI NO RESPONDE, SE DICE Y NO SE FINGE: <see cref="Fijar"/> devuelve falso, el viaje sigue por el
/// camino público (mover y cambiar) y el log deja escrito que la ventana faltó un instante. Cada
/// llamada se comprueba por su HRESULT antes de la siguiente, para no llamar a ciegas a un método
/// que quizá no es el que creemos.
/// </remarks>
public static class FijacionDeVentana
{
    [ComImport, Guid("C2F03A33-21F5-47FA-B4BB-156362A2F239")]
    private class ImmersiveShellCls { }

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        [PreserveSig] int QueryService(ref Guid servicio, ref Guid iid, out IntPtr objeto);
    }

    [ComImport, Guid("1841C6D7-4F9D-42C0-AF41-8747538F10E5"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationViewCollection
    {
        [PreserveSig] int GetViews(out IntPtr lista);
        [PreserveSig] int GetViewsByZOrder(out IntPtr lista);
        [PreserveSig] int GetViewsByAppUserModelId([MarshalAs(UnmanagedType.LPWStr)] string id, out IntPtr lista);
        [PreserveSig] int GetViewForHwnd(IntPtr hwnd, out IntPtr vista);
    }

    [ComImport, Guid("4CE81583-1E4C-4632-A621-07A53543148F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopPinnedApps
    {
        [PreserveSig] int IsAppIdPinned([MarshalAs(UnmanagedType.LPWStr)] string appId, out int fijada);
        [PreserveSig] int PinAppID([MarshalAs(UnmanagedType.LPWStr)] string appId);
        [PreserveSig] int UnpinAppID([MarshalAs(UnmanagedType.LPWStr)] string appId);
        [PreserveSig] int IsViewPinned(IntPtr vista, out int fijada);
        [PreserveSig] int PinView(IntPtr vista);
        [PreserveSig] int UnpinView(IntPtr vista);
    }

    private static readonly Guid IidVistas = new("1841C6D7-4F9D-42C0-AF41-8747538F10E5");
    private static readonly Guid IidFijadas = new("4CE81583-1E4C-4632-A621-07A53543148F");

    /// <summary>Cuál fue la última respuesta, para el log: vacío si nunca se intentó.</summary>
    public static string UltimaRespuesta { get; private set; } = "";

    /// <summary>Fija la ventana en todos los escritorios. Falso si el sistema no respondió, y dice por qué.</summary>
    public static bool Fijar(IntPtr hwnd) => Cambiar(hwnd, fijar: true);

    /// <summary>Suelta la fijación. Falso si no respondió.</summary>
    public static bool Soltar(IntPtr hwnd) => Cambiar(hwnd, fijar: false);

    private static bool Cambiar(IntPtr hwnd, bool fijar)
    {
        if (hwnd == IntPtr.Zero) return false;
        IntPtr vistas = IntPtr.Zero, fijadas = IntPtr.Zero, vista = IntPtr.Zero;
        try
        {
            var shell = (IServiceProvider)new ImmersiveShellCls();
            Guid s1 = IidVistas, i1 = IidVistas;
            int hr = shell.QueryService(ref s1, ref i1, out vistas);
            if (hr != 0 || vistas == IntPtr.Zero) return No($"sin IApplicationViewCollection: 0x{hr:X8}");
            Guid s2 = IidFijadas, i2 = IidFijadas;
            hr = shell.QueryService(ref s2, ref i2, out fijadas);
            if (hr != 0 || fijadas == IntPtr.Zero) return No($"sin IVirtualDesktopPinnedApps: 0x{hr:X8}");

            var coleccion = (IApplicationViewCollection)Marshal.GetObjectForIUnknown(vistas);
            hr = coleccion.GetViewForHwnd(hwnd, out vista);
            if (hr != 0 || vista == IntPtr.Zero) return No($"la ventana {hwnd} no tiene vista: 0x{hr:X8}");

            var pinned = (IVirtualDesktopPinnedApps)Marshal.GetObjectForIUnknown(fijadas);
            // Se pregunta antes de tocar: un IsViewPinned que no contesta S_OK es una interfaz que no es la que creemos.
            hr = pinned.IsViewPinned(vista, out int ya);
            if (hr != 0) return No($"IsViewPinned no contestó: 0x{hr:X8}");
            if (fijar && ya != 0) { UltimaRespuesta = "ya estaba fijada"; return true; }
            if (!fijar && ya == 0) { UltimaRespuesta = "ya estaba suelta"; return true; }
            hr = fijar ? pinned.PinView(vista) : pinned.UnpinView(vista);
            if (hr != 0) return No($"{(fijar ? "PinView" : "UnpinView")}: 0x{hr:X8}");
            UltimaRespuesta = fijar ? "fijada" : "suelta";
            return true;
        }
        catch (Exception e)
        {
            return No($"{e.GetType().Name}: {e.Message}");
        }
        finally
        {
            if (vista != IntPtr.Zero) Marshal.Release(vista);
            if (fijadas != IntPtr.Zero) Marshal.Release(fijadas);
            if (vistas != IntPtr.Zero) Marshal.Release(vistas);
        }
    }

    private static bool No(string porque)
    {
        UltimaRespuesta = porque;
        UiaSurface.LogGlobal?.Invoke($"escritorio: fijar no responde en esta compilación de Windows — {porque}");
        return false;
    }
}
