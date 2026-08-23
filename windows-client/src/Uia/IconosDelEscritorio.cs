using System.Runtime.InteropServices;
using System.Text;
using U.WindowsClient.Actions;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Uia;

/// <summary>
/// LOS ICONOS DEL ESCRITORIO: cuáles hay, cómo se llaman, dónde están exactamente y cómo se mueven.
/// </summary>
/// <remarks>
/// NO SE LEEN POR UIA, y no por gusto: en esta máquina el <c>FolderView</c> del escritorio contesta
/// que tiene CERO hijos —ni con Children ni con Descendants— mientras el propio control declara
/// nueve iconos (comprobado el 2026-08-16). El árbol de accesibilidad del escritorio está vacío, así
/// que la vía que usa el resto del mapeador aquí no devuelve nada que mover.
///
/// La alternativa NO son las coordenadas de una captura. El control mismo sabe cómo se llama cada
/// icono y en qué píxel está: <c>LVM_GETITEMTEXT</c> y <c>LVM_GETITEMPOSITION</c>. Eso es identidad y
/// es exacto — mejor que UIA, no un apaño por debajo. El precio es que las estructuras tienen que
/// vivir en la memoria de explorer.exe (el control es suyo), y de ahí el VirtualAllocEx de abajo.
///
/// Y aun teniendo el píxel exacto, ANTES DE APRETAR SE COMPRUEBA QUE DEBAJO ESTÉ EL ESCRITORIO
/// (<see cref="Mover"/> mira WindowFromPoint). Sin eso, una ventana encima convierte el arrastre en
/// un clic dentro de otra app, que es exactamente el modo de fallo que la regla de no accionar por
/// coordenadas existe para prohibir: nadie se entera y todo el mundo contesta que sí.
/// </remarks>
public static class IconosDelEscritorio
{
    /// <summary>Un icono tal y como el escritorio lo declara: su nombre y su esquina superior izquierda.</summary>
    public sealed record Icono(int Indice, string Nombre, int X, int Y);

    /// <summary>La cuadrícula real sobre la que Windows encaja los iconos, medida y no supuesta.</summary>
    public sealed record Rejilla(int OrigenX, int OrigenY, int Ancho, int Alto, int Columnas, int Filas)
    {
        public int XDeColumna(int c) => OrigenX + c * Ancho;
        public int YDeFila(int f) => OrigenY + f * Alto;
        public int Huecos => Columnas * Filas;
    }

    #region Win32

    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr p, IntPtr c, string? cls, string? n);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] private static extern uint GetDpiForSystem();
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int i);
    private const int SM_CXSCREEN = 0, SM_CYSCREEN = 1;
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint a, uint b, ref RECT r, uint c);
    private const uint SPI_GETWORKAREA = 0x0030;
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint acc, bool inh, uint pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll")] private static extern IntPtr VirtualAllocEx(IntPtr p, IntPtr a, IntPtr sz, uint t, uint pr);
    [DllImport("kernel32.dll")] private static extern bool VirtualFreeEx(IntPtr p, IntPtr a, IntPtr sz, uint t);
    [DllImport("kernel32.dll")] private static extern bool WriteProcessMemory(IntPtr p, IntPtr a, byte[] b, IntPtr sz, out IntPtr w);
    [DllImport("kernel32.dll")] private static extern bool ReadProcessMemory(IntPtr p, IntPtr a, byte[] b, IntPtr sz, out IntPtr r);

    private delegate bool EnumProc(IntPtr h, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }

    private const uint LVM_GETITEMCOUNT = 0x1004, LVM_GETITEMRECT = 0x100E, LVM_GETITEMPOSITION = 0x1010,
                       LVM_SETITEMSTATE = 0x102B, LVM_SETITEMPOSITION32 = 0x1031,
                       LVM_GETITEMSPACING = 0x1033, LVM_SETEXTENDEDLISTVIEWSTYLE = 0x1036,
                       LVM_GETEXTENDEDLISTVIEWSTYLE = 0x1037, LVM_GETITEMTEXTW = 0x1073;
    private const int LVS_EX_SNAPTOGRID = 0x00080000;
    private const uint PROCESO = 0x0008 | 0x0010 | 0x0020 | 0x0400;
    private const uint MEM_COMMIT = 0x1000, MEM_RESERVE = 0x2000, MEM_RELEASE = 0x8000, PAGE_RW = 0x04;
    private const int LVIF_TEXT = 0x0001, LVIF_STATE = 0x0008, LVIS_SELECTED = 0x0002;
    private const int LVIR_ICON = 1;
    private const int TAM_LVITEM = 88, TAM_TEXTO = 520;

    #endregion

    /// <summary>
    /// El <c>SysListView32</c> del escritorio. Se busca por ESTRUCTURA —quien tenga dentro un
    /// SHELLDLL_DefView— y no por el nombre de la clase de fuera, porque esa cambia: con fondo
    /// estático cuelga de Progman y con fondo dinámico de un WorkerW, y el día que Windows invente
    /// una tercera, esto la encuentra igual.
    /// </summary>
    public static IntPtr Lista()
    {
        IntPtr hallada = IntPtr.Zero;
        try
        {
            EnumWindows((h, _) =>
            {
                var vista = FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (vista == IntPtr.Zero) return true;
                var lv = FindWindowEx(vista, IntPtr.Zero, "SysListView32", null);
                if (lv == IntPtr.Zero) return true;
                hallada = lv;
                return false;
            }, IntPtr.Zero);
        }
        catch (Exception e) { LogBus.Log("escritorio", $"buscando la lista de iconos: {e.Message}"); }
        return hallada;
    }

    /// <summary>Qué iconos hay, cómo se llaman y dónde están. Lista vacía si no se pudo preguntar.</summary>
    public static IReadOnlyList<Icono> Leer()
    {
        var lv = Lista();
        if (lv == IntPtr.Zero) return Array.Empty<Icono>();

        int n = (int)SendMessage(lv, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
        if (n <= 0) return Array.Empty<Icono>();

        return EnMemoriaDeExplorer(lv, (proc, buf, bufTexto) =>
        {
            var lista = new List<Icono>(n);
            for (int i = 0; i < n; i++)
            {
                var item = new byte[TAM_LVITEM];
                BitConverter.GetBytes(LVIF_TEXT).CopyTo(item, 0);
                BitConverter.GetBytes(i).CopyTo(item, 4);
                BitConverter.GetBytes(bufTexto.ToInt64()).CopyTo(item, 24);
                BitConverter.GetBytes(260).CopyTo(item, 32);
                WriteProcessMemory(proc, buf, item, new IntPtr(TAM_LVITEM), out _);
                SendMessage(lv, LVM_GETITEMTEXTW, new IntPtr(i), buf);

                var texto = new byte[TAM_TEXTO];
                ReadProcessMemory(proc, bufTexto, texto, new IntPtr(TAM_TEXTO), out _);
                string nombre = Encoding.Unicode.GetString(texto);
                int fin = nombre.IndexOf('\0');
                if (fin >= 0) nombre = nombre[..fin];

                SendMessage(lv, LVM_GETITEMPOSITION, new IntPtr(i), buf);
                var pt = new byte[8];
                ReadProcessMemory(proc, buf, pt, new IntPtr(8), out _);
                lista.Add(new Icono(i, nombre, BitConverter.ToInt32(pt, 0), BitConverter.ToInt32(pt, 4)));
            }
            return (IReadOnlyList<Icono>)lista;
        }, Array.Empty<Icono>());
    }

    /// <summary>Dónde está AHORA un icono concreto, para comprobar si el arrastre lo llevó donde debía.</summary>
    public static (int X, int Y)? Posicion(int indice)
    {
        var lv = Lista();
        if (lv == IntPtr.Zero) return null;
        return EnMemoriaDeExplorer<(int, int)?>(lv, (proc, buf, _) =>
        {
            SendMessage(lv, LVM_GETITEMPOSITION, new IntPtr(indice), buf);
            var pt = new byte[8];
            ReadProcessMemory(proc, buf, pt, new IntPtr(8), out _);
            return (BitConverter.ToInt32(pt, 0), BitConverter.ToInt32(pt, 4));
        }, null);
    }

    /// <summary>
    /// La cuadrícula, MEDIDA sobre lo que ya hay. El tamaño de celda lo da el control
    /// (<c>LVM_GETITEMSPACING</c>), pero su ORIGEN no lo dice nadie: se deduce del resto que dejan
    /// los iconos actuales al dividirlos por la celda. Con «alinear a la cuadrícula» encendido todos
    /// dan el mismo resto —así se confirmó: 112 y 207 dan 17; 124, 246, 368, 490 y 612 dan 2— y ese
    /// resto ES el origen. Si no coinciden (cuadrícula apagada), se cae a la esquina de más arriba a
    /// la izquierda que ya ocupa alguien, que es un sitio donde consta que caben iconos.
    /// </summary>
    public static Rejilla MedirRejilla(IReadOnlyList<Icono> iconos)
    {
        var lv = Lista();
        long sp = SendMessage(lv, LVM_GETITEMSPACING, IntPtr.Zero, IntPtr.Zero).ToInt64();
        int ancho = (int)(sp & 0xFFFF), alto = (int)((sp >> 16) & 0xFFFF);
        if (ancho <= 0) ancho = 95;
        if (alto <= 0) alto = 122;

        int ox = 0, oy = 0;
        if (iconos.Count > 0)
        {
            var restosX = iconos.Select(i => ((i.X % ancho) + ancho) % ancho).Distinct().ToList();
            var restosY = iconos.Select(i => ((i.Y % alto) + alto) % alto).Distinct().ToList();
            if (restosX.Count == 1 && restosY.Count == 1)
            {
                ox = restosX[0];
                oy = restosY[0];
                _origenSabido = (ox, oy);   // cuadriculados: este resto ES el origen, y se recuerda
            }
            else if (_origenSabido is { } sabido)
            {
                (ox, oy) = sabido;          // en forma libre: el que se midió cuando sí lo estaban
            }
            else
            {
                ox = ((iconos.Min(i => i.X) % ancho) + ancho) % ancho;
                oy = ((iconos.Min(i => i.Y) % alto) + alto) % alto;
                LogBus.Log("escritorio", "los iconos no están cuadriculados y no tengo origen previo; lo deduzco del más alto");
            }
        }

        // HASTA DÓNDE SE PUEDE COLOCAR, MEDIDO CON LA MISMA REGLA CON LA QUE SE COLOCA.
        //
        // Aquí había un GetClientRect recortado contra el área de trabajo, y daba números de otro
        // mundo: el control decía medir 1920x1080 mientras las posiciones de los iconos —las que
        // devuelve y acepta LVM_GETITEMPOSITION— viven en un espacio de 1536x864. Con la pantalla al
        // 125%, esas dos cifras son la MISMA pantalla contada en píxeles físicos y en los del
        // control. Medir en una y colocar en la otra dejaba filas en y=856 sobre un escritorio que
        // acaba en 816, y convirtió una Ü en dos columnas estiradas (2026-08-16).
        //
        // La conversión buena es la que ya usa la carita para plantarse junto a un icono y acierta:
        // ClientToScreen/ScreenToClient entre el espacio del control y el de la pantalla. Así que el
        // borde del área de trabajo se TRAE a coordenadas del control en vez de comparar a ojo dos
        // magnitudes que solo se parecen.
        GetClientRect(lv, out var r);
        int derecha = r.Right, abajo = r.Bottom;

        // EL ESCALADO DE PANTALLA, que es lo que separa las dos reglas. A esta app —consciente del
        // DPI, como toda app WPF— GetClientRect y el área de trabajo le contestan en píxeles FÍSICOS
        // (1920x1080 con la pantalla al 125%), mientras que las posiciones que da y acepta el control
        // viven en el espacio lógico de 96 ppp (1536x864). No es una traslación, así que ni
        // ScreenToClient ni medir dos puntos con ClientToScreen lo revelan: los dos dan factor 1 y la
        // Ü acabó con su curva escondida detrás de la barra de tareas (2026-08-16).
        //
        // En una pantalla al 100% esto vale 1 y no cambia nada, que es como debe portarse un arreglo
        // de escalado: invisible donde no hay escalado.
        double factor = Math.Max(1.0, GetDpiForSystem() / 96.0);
        var cero = new POINT { X = 0, Y = 0 };
        ClientToScreen(lv, ref cero);

        // EL LÍMITE ES LA PANTALLA, NO EL ÁREA DE TRABAJO. Aquí se restaba la barra de tareas, y eso
        // descartaba la última fila entera: el usuario colocó a mano iconos en y=734 —que se ven
        // perfectamente, porque la barra solo tapa el final de la etiqueta— y esto los declaraba
        // invisibles (2026-08-16). Windows mismo deja poner iconos ahí. Lo que sí queda fuera es la
        // fila siguiente, y esa se descarta porque el dibujo del icono ya no cabría en la pantalla.
        int anchoUtil = (int)(GetSystemMetrics(SM_CXSCREEN) / factor) - cero.X;
        int altoUtil = (int)(GetSystemMetrics(SM_CYSCREEN) / factor) - cero.Y;
        if (anchoUtil > ancho) derecha = Math.Min(derecha, anchoUtil);
        if (altoUtil > alto) abajo = Math.Min(abajo, altoUtil);

        int columnas = Math.Max(1, (derecha - ox) / ancho);
        int filas = Math.Max(1, (abajo - oy) / alto);
        LogBus.Log("escritorio", $"rejilla: celda {ancho}x{alto}, origen {ox},{oy}, {columnas} col x {filas} filas "
            + $"(control {r.Right}x{r.Bottom}, factor pantalla/control {factor:0.###}, útil {derecha}x{abajo})");
        return new Rejilla(ox, oy, ancho, alto, columnas, filas);
    }

    /// <summary>
    /// La caja que ocuparía un icono situado en (x,y) de la lista, en píxeles de PANTALLA — que es
    /// como los quiere <see cref="Ui.Senalador"/> para llevar la carita a su lado y encender el
    /// recuadro. Sirve tanto para donde está como para donde va a estar.
    /// </summary>
    public static System.Windows.Rect? CajaEnPantalla(int xCliente, int yCliente, int ancho, int alto)
    {
        var lv = Lista();
        if (lv == IntPtr.Zero) return null;
        var p = new POINT { X = xCliente, Y = yCliente };
        if (!ClientToScreen(lv, ref p)) return null;
        return new System.Windows.Rect(p.X, p.Y, ancho, alto);
    }

    /// <summary>Cuántos fotogramas y cuánto dura cada uno al llevar un icono. Se sube para que se vea.</summary>
    public static int Fotogramas { get; set; } = 22;
    public static int MsPorFotograma { get; set; } = 26;

    /// <summary>
    /// «Alinear iconos a la cuadrícula». Encendida, Windows encaja en la celda más cercana TAMBIÉN lo
    /// que se coloca por API, no solo lo que se arrastra: un círculo pedido salía a escalones, dos
    /// iconos podían caer en la misma celda, y la comprobación posterior denunciaba ocho fallos
    /// cuando en realidad se habían movido todos (2026-08-16). Se apaga mientras se dibuja una forma
    /// libre y se vuelve a dejar como estaba.
    /// </summary>
    public static bool Cuadricula
    {
        get
        {
            var lv = Lista();
            if (lv == IntPtr.Zero) return true;
            return (SendMessage(lv, LVM_GETEXTENDEDLISTVIEWSTYLE, IntPtr.Zero, IntPtr.Zero).ToInt64() & LVS_EX_SNAPTOGRID) != 0;
        }
        set
        {
            var lv = Lista();
            if (lv == IntPtr.Zero) return;
            SendMessage(lv, LVM_SETEXTENDEDLISTVIEWSTYLE,
                new IntPtr(LVS_EX_SNAPTOGRID), new IntPtr(value ? LVS_EX_SNAPTOGRID : 0));
            LogBus.Log("escritorio", $"alinear a la cuadrícula: {(value ? "encendido" : "apagado")}");
        }
    }

    /// <summary>
    /// El origen de rejilla que se midió la última vez que los iconos SÍ estaban cuadriculados.
    /// Hace falta porque tras dibujar un círculo ya no hay resto común del que deducirlo, y sin esto
    /// las columnas siguientes arrancarían en un margen distinto cada vez.
    /// </summary>
    private static (int X, int Y)? _origenSabido;

    /// <summary>Deja de haber nada seleccionado. Arrastrar uno con varios marcados los mueve TODOS.</summary>
    public static void SoltarSeleccion()
    {
        var lv = Lista();
        if (lv == IntPtr.Zero) return;
        EnMemoriaDeExplorer<object?>(lv, (proc, buf, _) =>
        {
            var item = new byte[TAM_LVITEM];
            BitConverter.GetBytes(LVIF_STATE).CopyTo(item, 0);
            BitConverter.GetBytes(0).CopyTo(item, 12);            // state = nada
            BitConverter.GetBytes(LVIS_SELECTED).CopyTo(item, 16); // stateMask = solo la selección
            WriteProcessMemory(proc, buf, item, new IntPtr(TAM_LVITEM), out _);
            SendMessage(lv, LVM_SETITEMSTATE, new IntPtr(-1), buf); // -1 = todos
            return null;
        }, null);
    }

    /// <summary>Lo que pasó al intentar mover un icono. <see cref="Llego"/> se responde MIRANDO, no suponiendo.</summary>
    public sealed record Traslado(string Nombre, bool Llego, int XFinal, int YFinal, string Porque);

    /// <summary>
    /// LLEVA UN ICONO A SU SITIO, a la vista y sin tocar el botón del ratón.
    /// </summary>
    /// <remarks>
    /// ESTO ERA UN ARRASTRE DE VERDAD —botón abajo, mover, soltar— y borró un archivo del usuario.
    /// El 2026-08-16, mientras una vuelta de ordenar seguía corriendo, entró otra: sus eventos de
    /// ratón se entrelazaron, un botón que bajó sobre un icono se soltó en un punto que decidió la
    /// otra vuelta, y «Google Chrome» cayó encima de la Papelera. El escritorio pasó de nueve iconos
    /// a ocho. Nadie apretó nada raro: se pidió ordenar dos veces.
    ///
    /// LA LECCIÓN NO ES «FALTABA UN CANDADO». Un candado lo habría evitado ESE día. El problema es
    /// que el ratón es un recurso GLOBAL y COMPARTIDO —lo mueve el usuario, lo mueve la voz, lo mueve
    /// cualquier automatismo— y mientras el botón está abajo, el escritorio interpreta lo que pase
    /// como una operación de ARCHIVOS: soltar sobre una carpeta mete dentro, soltar sobre la Papelera
    /// borra. Un arrastre sintético es una operación destructiva disfrazada de arreglo visual, y
    /// ninguna comprobación previa cubre lo que ocurra entre el «abajo» y el «arriba».
    ///
    /// Así que el icono ya no se suelta: SE COLOCA (<c>LVM_SETITEMPOSITION32</c>), que es exacto y no
    /// puede disparar ninguna operación de archivos. Y para que siga viéndose —que era lo pedido: ver
    /// a Ü ponerse encima y llevarlo— el puntero viaja con él y la posición se va fijando por el
    /// camino. Se ve igual, y ya no existe un instante en el que un archivo esté en el aire.
    /// </remarks>
    public static Traslado Mover(Icono icono, int xDestino, int yDestino)
    {
        var lv = Lista();
        if (lv == IntPtr.Zero) return new Traslado(icono.Nombre, false, icono.X, icono.Y, "no encontré la lista del escritorio");

        var rect = RectanguloDelIcono(lv, icono.Indice);
        int anchoIcono = rect?.W ?? 48, altoIcono = rect?.H ?? 48;
        int desfaseX = (rect?.X ?? icono.X) + anchoIcono / 2 - icono.X;
        int desfaseY = (rect?.Y ?? icono.Y) + altoIcono / 2 - icono.Y;

        int fotogramas = Math.Max(2, Fotogramas);
        for (int f = 1; f <= fotogramas; f++)
        {
            // Arranca y frena suave (coseno): un movimiento a velocidad constante se lee como una
            // animación, y uno que acelera y frena se lee como alguien llevando algo.
            double t = (1 - Math.Cos(Math.PI * f / fotogramas)) / 2;
            int x = icono.X + (int)Math.Round((xDestino - icono.X) * t);
            int y = icono.Y + (int)Math.Round((yDestino - icono.Y) * t);
            Colocar(lv, icono.Indice, x, y);

            // El puntero acompaña al icono. Solo se mueve: nunca se pulsa, así que nada de lo que
            // pase por debajo puede convertirse en abrir, mover o borrar.
            var p = new POINT { X = x + desfaseX, Y = y + desfaseY };
            if (ClientToScreen(lv, ref p)) SetCursorPos(p.X, p.Y);
            Thread.Sleep(Math.Max(1, MsPorFotograma));
        }
        Colocar(lv, icono.Indice, xDestino, yDestino);

        var ahora = Posicion(icono.Indice);
        if (ahora == null) return new Traslado(icono.Nombre, false, icono.X, icono.Y, "no pude releer dónde quedó");

        bool llego = Math.Abs(ahora.Value.X - xDestino) <= 4 && Math.Abs(ahora.Value.Y - yDestino) <= 4;
        return new Traslado(icono.Nombre, llego, ahora.Value.X, ahora.Value.Y,
            llego ? "" : $"quedó en {ahora.Value.X},{ahora.Value.Y} y no en {xDestino},{yDestino}");
    }

    /// <summary>Fija la posición exacta de un icono. Es lo que sustituye a soltarlo, y no puede borrar nada.</summary>
    private static void Colocar(IntPtr lv, int indice, int x, int y)
    {
        EnMemoriaDeExplorer<object?>(lv, (proc, buf, _) =>
        {
            var pt = new byte[8];
            BitConverter.GetBytes(x).CopyTo(pt, 0);
            BitConverter.GetBytes(y).CopyTo(pt, 4);
            WriteProcessMemory(proc, buf, pt, new IntPtr(8), out _);
            SendMessage(lv, LVM_SETITEMPOSITION32, new IntPtr(indice), buf);
            return null;
        }, null);
    }

    public static (int X, int Y, int W, int H)? RectanguloDelIcono(IntPtr lv, int indice)
    {
        return EnMemoriaDeExplorer<(int, int, int, int)?>(lv, (proc, buf, _) =>
        {
            var r = new byte[16];
            BitConverter.GetBytes(LVIR_ICON).CopyTo(r, 0); // el código va en «left»
            WriteProcessMemory(proc, buf, r, new IntPtr(16), out _);
            if (SendMessage(lv, LVM_GETITEMRECT, new IntPtr(indice), buf) == IntPtr.Zero) return null;
            ReadProcessMemory(proc, buf, r, new IntPtr(16), out _);
            int l = BitConverter.ToInt32(r, 0), t = BitConverter.ToInt32(r, 4);
            int rr = BitConverter.ToInt32(r, 8), b = BitConverter.ToInt32(r, 12);
            return (l, t, rr - l, b - t);
        }, null);
    }

    /// <summary>
    /// Presta un trozo de memoria DENTRO de explorer.exe y lo devuelve pase lo que pase. Está aquí una
    /// sola vez porque cada quien reservando y liberando por su cuenta es como se filtra memoria en el
    /// proceso de otro, que además es el que dibuja el escritorio de la persona.
    /// </summary>
    private static T EnMemoriaDeExplorer<T>(IntPtr lv, Func<IntPtr, IntPtr, IntPtr, T> hacer, T siNo)
    {
        IntPtr proc = IntPtr.Zero, buf = IntPtr.Zero;
        try
        {
            GetWindowThreadProcessId(lv, out uint pid);
            proc = OpenProcess(PROCESO, false, pid);
            if (proc == IntPtr.Zero) { LogBus.Log("escritorio", $"no pude abrir explorer.exe (pid {pid})"); return siNo; }
            buf = VirtualAllocEx(proc, IntPtr.Zero, new IntPtr(TAM_LVITEM + TAM_TEXTO), MEM_COMMIT | MEM_RESERVE, PAGE_RW);
            if (buf == IntPtr.Zero) { LogBus.Log("escritorio", "no pude reservar memoria en explorer.exe"); return siNo; }
            return hacer(proc, buf, buf + TAM_LVITEM);
        }
        catch (Exception e)
        {
            LogBus.Log("escritorio", $"hablando con la lista de iconos: {e.Message}");
            return siNo;
        }
        finally
        {
            if (buf != IntPtr.Zero && proc != IntPtr.Zero) VirtualFreeEx(proc, buf, IntPtr.Zero, MEM_RELEASE);
            if (proc != IntPtr.Zero) CloseHandle(proc);
        }
    }
}
