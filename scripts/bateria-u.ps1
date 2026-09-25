# LA BATERÍA DE Ü DESDE CERO (spec 052, nivel 4 automatizado): pedidos reales, de punta a punta —Luna planea,
# Jev decide, el ratón pulsa— sobre el escritorio de esta máquina. Mueve el ratón DE VERDAD.
#
#   .\scripts\bateria-u.ps1                 # compila y corre todos
#   .\scripts\bateria-u.ps1 -SinCompilar
#
# Lo que mide sale del log de la app (%LOCALAPPDATA%\U-nuevo\logs), no de lo que el proceso diga al salir:
# cada «⏱» es una vuelta del ciclo con sus cinco tiempos.

param([switch]$SinCompilar, [string[]]$Pedidos)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$bin  = Join-Path $env:TEMP "u-nuevo-bin"
if (-not $SinCompilar) {
    dotnet build (Join-Path $repo "u\App\App.csproj") -c Release -o $bin --nologo -v quiet -nodeReuse:false | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Host "NO COMPILA" -ForegroundColor Red; exit 3 }
}
$exe = Join-Path $bin "U-nuevo.exe"
$log = Join-Path $env:LOCALAPPDATA ("U-nuevo\logs\u-" + (Get-Date -Format yyyyMMdd) + ".log")

if (-not $Pedidos) {
    $Pedidos = @(
        "abre la calculadora y calcula 45 por 12",
        "abre configuración y ve a Bluetooth y dispositivos",
        "abre el explorador de archivos y entra a la carpeta Descargas",
        "abre el bloc de notas y escribe: prueba nocturna de Ü",
        "abre configuración y ve a Sistema y después a Sonido",
        "en la calculadora, borra todo y calcula 1000 menos 1"
    )
}

$vueltas = @(); $filas = @()
foreach ($p in $Pedidos) {
    $antes = if (Test-Path $log) { (Get-Content $log -Encoding UTF8).Count } else { 0 }
    $reloj = [Diagnostics.Stopwatch]::StartNew()
    $proc = Start-Process $exe -ArgumentList '--hacer', ('"' + $p + '"') -PassThru -Wait
    $reloj.Stop()
    $nuevas = Get-Content $log -Encoding UTF8 | Select-Object -Skip $antes
    $mias = $nuevas | Where-Object { $_ -match '⏱' }
    foreach ($l in $mias) {
        if ($l -match 'total (\d+) ms') { $vueltas += [pscustomobject]@{ Pedido = $p; Total = [int]$Matches[1]; Clic = ($l -match 'pulsé'); Linea = $l } }
    }
    $final = ($nuevas | Where-Object { $_ -match '\] Ü: ' } | Select-Object -Last 1) -replace '^\[[^\]]+\] Ü: ', ''
    $pasos = ($nuevas | Where-Object { $_ -match '↩ ' }) -replace '^.*↩ ', ''
    $filas += [pscustomobject]@{ Pedido = $p; Segundos = [math]::Round($reloj.Elapsed.TotalSeconds, 1); Vueltas = $mias.Count; Planes = ($pasos -join ' | '); Dijo = $final }
    Write-Host ("`n=== " + $p) -ForegroundColor Cyan
    $nuevas | ForEach-Object { if ($_.Length -gt 220) { $_.Substring(0, 220) } else { $_ } }
}

Write-Host "`nRESUMEN" -ForegroundColor Yellow
$filas | Format-Table -Wrap -AutoSize Pedido, Segundos, Vueltas, Planes | Out-String -Width 220
$conClic = $vueltas | Where-Object Clic
function Med($xs) { $s = @($xs | Sort-Object); if ($s.Count -eq 0) { return 0 }; $s[[int][math]::Floor($s.Count / 2)] }
if ($conClic) {
    $fuera = @($vueltas | Where-Object { $_.Total -gt 500 }).Count
    "vueltas: $($vueltas.Count) · con clic: $($conClic.Count) · mediana con clic: $(Med ($conClic.Total)) ms · máx: $(($conClic.Total | Measure-Object -Maximum).Maximum) ms · fuera de presupuesto: $fuera de $($vueltas.Count)"
}
