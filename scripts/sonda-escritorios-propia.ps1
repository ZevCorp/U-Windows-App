Add-Type -AssemblyName System.Windows.Forms
$src = @'
using System;
using System.Runtime.InteropServices;
[ComImport, Guid("aa509086-5ca9-4c25-8f95-589d3c07b48a")] public class VdmCls { }
[ComImport, Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IVdm {
    [PreserveSig] int IsWindowOnCurrentVirtualDesktop(IntPtr hwnd, out int onCurrent);
    [PreserveSig] int GetWindowDesktopId(IntPtr hwnd, out Guid id);
    [PreserveSig] int MoveWindowToDesktop(IntPtr hwnd, ref Guid id);
}
public static class S2 {
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int a, out int v, int cb);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    public static IVdm M() { return (IVdm)new VdmCls(); }
    public static int Mover(IntPtr h, Guid d) { return M().MoveWindowToDesktop(h, ref d); }
    public static Guid Donde(IntPtr h) { Guid g; M().GetWindowDesktopId(h, out g); return g; }
    public static int Cloaked(IntPtr h) { int c; return DwmGetWindowAttribute(h, 14, out c, 4) == 0 ? c : -1; }
    public static int EnActual(IntPtr h) { int o; return M().IsWindowOnCurrentVirtualDesktop(h, out o) == 0 ? o : -1; }
    public static bool Visible(IntPtr h) { return IsWindowVisible(h); }
    public static IntPtr Foco() { return GetForegroundWindow(); }
}
'@
Add-Type -TypeDefinition $src -Language CSharp

function Guids([byte[]]$b) { $r = @(); for ($i = 0; $i + 16 -le $b.Length; $i += 16) { $r += [Guid]::new([byte[]]$b[$i..($i+15)]) }; $r }
$k = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops'
$ids = Guids (Get-ItemProperty $k).VirtualDesktopIDs
$cur = [Guid]::new((Get-ItemProperty $k).CurrentVirtualDesktop)
$otro = ($ids | Where-Object { $_ -ne $cur } | Select-Object -First 1)

$f = New-Object System.Windows.Forms.Form
$f.Text = 'sonda propia'; $f.Width = 300; $f.Height = 120
$out = New-Object System.Collections.Generic.List[string]
$paso = 0
$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 600
$timer.Add_Tick({
  $script:paso++
  $h = $f.Handle
  switch ($script:paso) {
    1 { [S2]::ShowWindow($h, 5) | Out-Null; $out.Add("ventana propia hwnd=$h visible=$([S2]::Visible($h)) esta en $([S2]::Donde($h)) (actual=$cur)") }
    2 { $sw = [System.Diagnostics.Stopwatch]::StartNew(); $script:hr = [S2]::Mover($h, $otro); $script:t = $sw.ElapsedMilliseconds }
    3 { $out.Add(("MoveWindowToDesktop(propia) -> hr=0x{0:X8} en {1} ms; ahora esta en {2}; visible={3} cloaked={4} enActual={5}" -f $script:hr, $script:t, [S2]::Donde($h), [S2]::Visible($h), [S2]::Cloaked($h), [S2]::EnActual($h)))
        $out.Add("registro sigue diciendo actual=$([Guid]::new((Get-ItemProperty $k).CurrentVirtualDesktop)) (mover NO cambia de escritorio)") }
    4 { $script:hr2 = [S2]::Mover($h, $cur) }
    5 { $out.Add(("de vuelta -> hr=0x{0:X8}; esta en {1}; cloaked={2} enActual={3}" -f $script:hr2, [S2]::Donde($h), [S2]::Cloaked($h), [S2]::EnActual($h))); $timer.Stop(); $f.Close() }
  }
})
$f.Add_Shown({ $timer.Start() })
[System.Windows.Forms.Application]::Run($f)
$out
