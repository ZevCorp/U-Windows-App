$src = @'
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;

[ComImport, Guid("aa509086-5ca9-4c25-8f95-589d3c07b48a")]
public class VirtualDesktopManagerCls { }

[ComImport, Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IVirtualDesktopManager
{
    [PreserveSig] int IsWindowOnCurrentVirtualDesktop(IntPtr hwnd, out int onCurrent);
    [PreserveSig] int GetWindowDesktopId(IntPtr hwnd, out Guid id);
    [PreserveSig] int MoveWindowToDesktop(IntPtr hwnd, ref Guid id);
}

public class Fila { public IntPtr Hwnd; public string Titulo; public string Proceso; public Guid Escritorio; public int Hr; public int Cloaked; public int EnActual; }

public static class Sonda
{
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int a, out int v, int cb);

    public static IVirtualDesktopManager M() { return (IVirtualDesktopManager)new VirtualDesktopManagerCls(); }

    public static List<Fila> Ventanas()
    {
        var m = M();
        var lista = new List<Fila>();
        EnumWindows(delegate(IntPtr h, IntPtr l)
        {
            if (!IsWindowVisible(h)) return true;
            var sb = new StringBuilder(256);
            GetWindowText(h, sb, 256);
            if (sb.Length == 0) return true;
            uint pid; GetWindowThreadProcessId(h, out pid);
            string proc = "?";
            try { proc = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; } catch { }
            var f = new Fila(); f.Hwnd = h; f.Titulo = sb.ToString(); f.Proceso = proc;
            Guid g; f.Hr = m.GetWindowDesktopId(h, out g); f.Escritorio = g;
            int c; if (DwmGetWindowAttribute(h, 14, out c, 4) == 0) f.Cloaked = c; else f.Cloaked = -1;
            int on; if (m.IsWindowOnCurrentVirtualDesktop(h, out on) == 0) f.EnActual = on; else f.EnActual = -1;
            lista.Add(f);
            return true;
        }, IntPtr.Zero);
        return lista;
    }

    public static int Mover(IntPtr h, Guid destino) { return M().MoveWindowToDesktop(h, ref destino); }
    public static Guid Donde(IntPtr h) { Guid g; M().GetWindowDesktopId(h, out g); return g; }
}
'@
Add-Type -TypeDefinition $src -Language CSharp

function Guids([byte[]]$b) { $r = @(); for ($i = 0; $i + 16 -le $b.Length; $i += 16) { $r += [Guid]::new([byte[]]$b[$i..($i+15)]) }; $r }

$k = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops'
$ids = Guids (Get-ItemProperty $k).VirtualDesktopIDs
"== escritorios en el registro ($k): $($ids.Count)"
$ids | ForEach-Object { "   $_" }
$cur = $null
try { $cur = [Guid]::new((Get-ItemProperty $k -ErrorAction Stop).CurrentVirtualDesktop); "   actual (misma clave): $cur" } catch { "   CurrentVirtualDesktop no esta en esa clave" }
Get-ChildItem 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\SessionInfo' -ErrorAction SilentlyContinue | ForEach-Object {
  $p = Join-Path $_.PSPath 'VirtualDesktops'
  try { $c = [Guid]::new((Get-ItemProperty $p -ErrorAction Stop).CurrentVirtualDesktop); "   actual (SessionInfo $($_.PSChildName)): $c"; if (-not $cur) { $cur = $c } } catch {}
}
foreach ($id in $ids) {
  $n = (Get-ItemProperty "$k\Desktops\$($id.ToString('B'))" -ErrorAction SilentlyContinue).Name
  "   $id nombre='$n'"
}

"== ventanas visibles con titulo, por escritorio"
$v = [Sonda]::Ventanas()
$v | Sort-Object Escritorio | ForEach-Object { "   hr=0x{0:X8} esc={1} actual={2} cloaked={3} {4,-14} {5}" -f $_.Hr, $_.Escritorio, $_.EnActual, $_.Cloaked, $_.Proceso, $_.Titulo.Substring(0, [Math]::Min(60, $_.Titulo.Length)) }

"== U.exe corriendo"
Get-Process U -ErrorAction SilentlyContinue | ForEach-Object { "   pid=$($_.Id) $($_.Path) hwnd=$($_.MainWindowHandle)" }

if ($ids.Count -ge 2 -and $cur) {
  $otro = ($ids | Where-Object { $_ -ne $cur } | Select-Object -First 1)
  "== prueba: mover una ventana AJENA (charmap.exe, Win32 clasico, otro proceso) del escritorio actual $cur al $otro"
  $p = Start-Process "$env:WINDIR\System32\charmap.exe" -PassThru
  $h = [IntPtr]::Zero
  for ($i = 0; $i -lt 50 -and $h -eq [IntPtr]::Zero; $i++) { Start-Sleep -Milliseconds 100; $p.Refresh(); $h = $p.MainWindowHandle }
  "   charmap pid=$($p.Id) hwnd=$h esta en $([Sonda]::Donde($h))"
  $hr = [Sonda]::Mover($h, $otro)
  Start-Sleep -Milliseconds 300
  "   MoveWindowToDesktop(ajena) -> hr=0x{0:X8}; ahora esta en {1}" -f $hr, [Sonda]::Donde($h)
  $f = [Sonda]::Ventanas() | Where-Object { $_.Hwnd -eq $h }
  "   mientras esta alla: IsWindowOnCurrentVirtualDesktop={0} cloaked={1} IsWindowVisible(por EnumWindows)=si" -f $f.EnActual, $f.Cloaked
  $hr2 = [Sonda]::Mover($h, $cur)
  Start-Sleep -Milliseconds 300
  "   de vuelta -> hr=0x{0:X8}; ahora esta en {1}" -f $hr2, [Sonda]::Donde($h)
  Stop-Process -Id $p.Id -Force
} else { "== solo hay un escritorio o no se leyo el actual: no se prueba mover" }
