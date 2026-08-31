# LA COMPUERTA: lo que tiene que ser cierto para que algo entre a main.
#
#   .\scripts\verificar.ps1                       # niveles 1-2 (compila + contrato)
#   .\scripts\verificar.ps1 -Escenarios           # + nivel 3: el CI del terreno sobre el escritorio real
#   .\scripts\verificar.ps1 -PermitirPendientes   # verificacion de FASE INTERMEDIA, nunca para main
#
# Por que existe: main roto bloquea a los tres, y hasta hoy la unica forma de saber si una rama
# estaba lista era acordarse de correr tres scripts en el orden correcto y leer bien sus salidas.
# Esto los corre en orden de coste (lo de treinta segundos antes que lo de veinte minutos) y deja
# la TABLA DE EVIDENCIA que exige el PR en out\evidencia.md.
#
# Lo que este script NO puede hacer, y por eso lo marca "NO CORRIDO" en vez de verde: el nivel 4,
# la corrida a mano sobre >=2 pantallas. Ningun script sabe si miraste. Un OK que no se gano es peor
# que un hueco: invita a confiar (aprendizaje n.4 del repo).
#
# Sin acentos ni simbolos raros a proposito: PowerShell 5.1 lee los .ps1 con la codificacion ANSI
# del sistema, y un texto que se lee mal es un texto que no se lee (medido el 2026-08-08 en el
# popup del guardian del nucleo).

[CmdletBinding()]
param(
  [switch]$PermitirPendientes,   # deja pasar promesas PENDIENTE: solo para verificar una fase
  [switch]$Escenarios,           # incluye ci-terreno.ps1 (abre la app en tu escritorio)
  [switch]$SinCompilar,          # el contrato ya compila; salta el nivel 1 si acabas de hacerlo
  [string]$Salida = ""           # donde dejar la evidencia (por defecto out\evidencia.md)
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$psExe = (Get-Process -Id $PID).Path      # el mismo motor que ya esta corriendo (5.1 o pwsh)
if (-not $Salida) { $Salida = Join-Path $repo "out\evidencia.md" }

$niveles = @()   # cada fila: Nivel, Estado (OK/FALLO/NO CORRIDO), Detalle
$bloquea = $false
function Anotar($nivel, $estado, $detalle) {
  $script:niveles += [pscustomobject]@{ Nivel = $nivel; Estado = $estado; Detalle = $detalle }
  $color = switch ($estado) { "OK" { "Green" } "FALLO" { "Red" } default { "DarkYellow" } }
  Write-Host ("  {0,-11} {1,-22} {2}" -f $estado, $nivel, $detalle) -ForegroundColor $color
}

Write-Host "COMPUERTA A MAIN" -ForegroundColor Cyan

# --- Nivel 0: el estado del arbol --------------------------------------------
# No es una prueba, es lo que hace que las pruebas signifiquen algo: si hay cambios sin commitear,
# lo verificado no es lo que se va a mergear.
$rama = (git -C $repo rev-parse --abbrev-ref HEAD 2>$null)
$sucio = @(git -C $repo status --porcelain 2>$null)
Write-Host "`n0. de que rama estamos hablando" -ForegroundColor Cyan
if ($rama -eq "main") {
  Anotar "Rama" "FALLO" "estas en main. main cambia SOLO por merge de un PR (git stash; git checkout -b <persona>/<que-hace>; git stash pop)"
  $bloquea = $true
} elseif ($rama -notmatch '^(jero|jose|pipe)/[a-z0-9.-]+$') {
  Anotar "Rama" "NO CORRIDO" "'$rama' no sigue <persona>/<que-hace>. No bloquea (hay ramas historicas), pero las nuevas si"
} else {
  Anotar "Rama" "OK" $rama
}
if ($sucio.Count -gt 0) {
  Anotar "Arbol limpio" "FALLO" ("{0} archivo(s) sin commitear: lo verificado no seria lo que se mergea" -f $sucio.Count)
  $bloquea = $true
} else {
  Anotar "Arbol limpio" "OK" "nada sin commitear"
}

# Lo que jamas debe viajar en un commit. El hash del candado es personal de cada maquina y los
# binarios/artefactos ya estan en .gitignore: si aparecen aqui, alguien uso `git add -f`.
$prohibido = @('nucleo-clave.hash', '.env', '/bin/', '/obj/', 'out/', 'graphify-out/20')
$tocados = @(git -C $repo diff --name-only origin/main...HEAD 2>$null)
if ($LASTEXITCODE -ne 0 -or $tocados.Count -eq 0) {
  Anotar "Diff vs main" "NO CORRIDO" "no se pudo comparar con origin/main (haz git fetch origin)"
} else {
  # EL PATRON ES DE SEGMENTO DE RUTA, NO DE SUBCADENA. Estos patrones estan copiados del
  # .gitignore, donde «out/» significa «una carpeta llamada out»; con -like «*out/*» significa
  # «cualquier ruta que contenga esas letras», y eso caza tambien «graphify-out/» — que en este
  # repo SI esta versionado (214 archivos en main; el .gitignore solo aparta las instantaneas con
  # fecha, graphify-out/20*/). Como el hook de graphify regenera esa carpeta en cada commit, la
  # compuerta declaraba «se cuelan 24 archivos» en CUALQUIER rama y no habia forma de pasarla
  # (2026-08-11, tropezado en el primer PR que la uso).
  #
  # Se ancla a «/» delante: un segmento empieza al principio de la ruta o despues de una barra.
  # Asi «out/» sigue cazando out\evidencia.md y deja pasar graphify-out\graph.json.
  $colados = @($tocados | Where-Object {
    $f = "/" + ($_ -replace '\\', '/')
    @($prohibido | Where-Object { $f -like "*/$($_.TrimStart('/'))*" }).Count -gt 0
  })
  if ($colados.Count -gt 0) {
    Anotar "Nada colado" "FALLO" ("se cuelan: {0}" -f ($colados -join ", "))
    $bloquea = $true
  } else {
    Anotar "Nada colado" "OK" ("{0} archivo(s) cambiados, ninguno prohibido" -f $tocados.Count)
  }
}

# --- Nivel 1: compila ---------------------------------------------------------
# Release y no Debug: se juzga el binario que se distribuye, no una variante (y con Debug el
# contrato ni siquiera puede correr en una maquina con Smart App Control).
if ($SinCompilar) {
  Anotar "Compila" "NO CORRIDO" "-SinCompilar; el nivel 2 compila igualmente"
} else {
  Write-Host "`n1. compila (Release)" -ForegroundColor Cyan
  $binTmp = Join-Path $env:TEMP "u-verificar-bin"
  dotnet build (Join-Path $repo "windows-client\WindowsClient.csproj") -c Release -o $binTmp `
    --nologo -v quiet -nodeReuse:false | Out-Null
  if ($LASTEXITCODE -ne 0) {
    Anotar "Compila" "FALLO" "windows-client no compila en Release (codigo $LASTEXITCODE)"
    Write-Host "`nNo se sigue: sin binario, los demas niveles no juzgan nada." -ForegroundColor Red
    $bloquea = $true
  } else {
    Anotar "Compila" "OK" "windows-client + windows-graph"
  }
}

# --- Nivel 2: el contrato del grafo -------------------------------------------
# Vigila las PROMESAS del nucleo y no toca la pantalla: es el mismo que corre en la nube en cada PR.
$fallos = -1; $pendientes = 0
if (-not $bloquea) {
  Write-Host "`n2. el contrato del grafo" -ForegroundColor Cyan
  $log = Join-Path $env:TEMP "u-verificar-contrato.txt"
  & $psExe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo "scripts\contrato-del-grafo.ps1") *>&1 |
    Tee-Object -FilePath $log
  $fallos = $LASTEXITCODE

  # El total sale del CODIGO FUENTE del contrato y no de su salida: contar sobre el texto impreso
  # depende de la codificacion con que se lea, y un recuento que puede encoger es exactamente el
  # vicio del aprendizaje n.10 (el denominador es el plan, nunca lo ejecutado).
  $total = @(Select-String -Path (Join-Path $repo "tests\ContratoDelGrafo\Contrato.cs") -Pattern '^\s*Prueba\("').Count
  # "PENDIENTE:" CON LOS DOS PUNTOS, y no es un detalle: sin ellos tambien engancha la linea resumen
  # "(N de ellas PENDIENTES: ...)" y el recuento se pasa por uno. Con eso, $regresiones sale NEGATIVO
  # y la compuerta rotula como FALLO lo que es un rojo intermedio legitimo — o sea, acusa de
  # regresion a la fase que va segun el plan. Latente aqui mientras el grafo tenga cero pendientes;
  # salio a la luz en el contrato de la voz el 2026-08-13.
  $pendientes = @(Select-String -Path $log -SimpleMatch "PENDIENTE:").Count

  if ($fallos -eq 0) {
    Anotar "Contrato" "OK" "$total/$total promesas, 0 pendientes"
  } elseif ($PermitirPendientes -and $fallos -eq $pendientes) {
    Anotar "Contrato" "NO CORRIDO" "$($total - $fallos)/$total verdes, $pendientes PENDIENTES declaradas (fase intermedia: NO puede ir a main)"
  } else {
    $regresiones = $fallos - $pendientes
    Anotar "Contrato" "FALLO" "$($total - $fallos)/$total verdes; $regresiones incumplida(s) con codigo y $pendientes pendiente(s)"
    $bloquea = $true
  }
}

# --- Nivel 2b: el contrato de la voz ------------------------------------------
# Las promesas de la parte que convierte tramas del collar Omi en muestras (spec 001).
#
# CORRE AUNQUE ALGO ANTERIOR BLOQUEE, y es a proposito: voz\Omi es net8.0 puro y no depende ni de
# U.dll ni de que windows-client compile, asi que puede dar su veredicto igual. Saltarselo solo
# conseguiria esconder informacion que ya esta pagada.
#
# Existe este bloque porque un juez que no esta en la compuerta es un juez que nadie convoca: los
# contratos del nucleo y del mapeador siguen fuera de aqui, y por eso pueden estar rojos mientras
# verificar.ps1 dice OK (aprendizaje n.18, anotado en la spec 001).
Write-Host "`n2b. el contrato de la voz" -ForegroundColor Cyan
$logVoz = Join-Path $env:TEMP "u-verificar-voz.txt"
& $psExe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo "scripts\contrato-de-la-voz.ps1") *>&1 |
  Tee-Object -FilePath $logVoz
$fallosVoz = $LASTEXITCODE

# El total sale del CODIGO FUENTE, no de la salida impresa: un denominador que puede encoger es el
# vicio del aprendizaje n.10.
$totalVoz = @(Select-String -Path (Join-Path $repo "voz\Contrato\Contrato.cs") -Pattern '^\s*Prueba\("').Count
$pendVoz  = @(Select-String -Path $logVoz -SimpleMatch "PENDIENTE:").Count

if ($fallosVoz -eq 0) {
  Anotar "Contrato voz" "OK" "$totalVoz/$totalVoz promesas, 0 pendientes"
} elseif ($PermitirPendientes -and $fallosVoz -eq $pendVoz) {
  Anotar "Contrato voz" "NO CORRIDO" "$($totalVoz - $fallosVoz)/$totalVoz verdes, $pendVoz PENDIENTES declaradas (fase intermedia: NO puede ir a main)"
} else {
  $regreVoz = $fallosVoz - $pendVoz
  Anotar "Contrato voz" "FALLO" "$($totalVoz - $fallosVoz)/$totalVoz verdes; $regreVoz incumplida(s) con codigo y $pendVoz pendiente(s)"
  $bloquea = $true
}

# --- Nivel 3: los escenarios sobre el terreno real -----------------------------
# El contrato vigila las promesas; esto vigila el RESULTADO sobre el escritorio de verdad. Ninguno
# sustituye al otro, y por eso este es local: un runner en la nube no tiene este escritorio.
if (-not $Escenarios) {
  Anotar "Escenarios" "NO CORRIDO" "sin -Escenarios. Obligatorio si tocaste mapeo, navegacion o UI"
} elseif ($bloquea) {
  Anotar "Escenarios" "NO CORRIDO" "no se corrieron: algo anterior ya bloquea"
} else {
  Write-Host "`n3. escenarios (CI del terreno; abre la app en tu escritorio)" -ForegroundColor Cyan
  & $psExe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo "scripts\ci-terreno.ps1")
  if ($LASTEXITCODE -eq 0) { Anotar "Escenarios" "OK" "ver salida de ci-terreno" }
  else { Anotar "Escenarios" "FALLO" "ci-terreno.ps1 dice que el terreno no contesta como promete"; $bloquea = $true }
}

# --- Nivel 4: la corrida a mano -----------------------------------------------
# Ningun script sabe si miraste. Se declara a mano en el PR, con los NOMBRES de las pantallas:
# una sola pantalla verificada es una apuesta a que las demas se comportan igual (aprendizaje n.9).
Anotar "A mano" "NO CORRIDO" "declara en el PR en cuantas pantallas probaste, con nombre"

# --- La evidencia --------------------------------------------------------------
$md = @()
$md += "<!-- generado por scripts\verificar.ps1 el $(Get-Date -Format 'yyyy-MM-dd HH:mm') sobre $rama -->"
$md += ""
$md += "| Nivel | Resultado | Detalle |"
$md += "|---|---|---|"
foreach ($n in $niveles) { $md += "| {0} | {1} | {2} |" -f $n.Nivel, $n.Estado, $n.Detalle }
$md += ""
$md += "Nivel 4 (a mano): _pantallas probadas, con nombre:_ "
$md += "Clase de error: _vivia en N sitios; N corregidos:_ "
New-Item -ItemType Directory -Force -Path (Split-Path $Salida) | Out-Null
Set-Content -Path $Salida -Value ($md -join "`r`n") -Encoding UTF8

Write-Host "`n================ VEREDICTO ================" -ForegroundColor Cyan
Write-Host ("evidencia en {0} (pegala entera en el PR, no la resumas)" -f $Salida) -ForegroundColor DarkGray
if ($bloquea) {
  Write-Host "NO PASA. Esto no entra a main todavia." -ForegroundColor Red
  exit 1
}
if ($fallos -ne 0) {
  Write-Host "FASE VERIFICADA, pero con promesas PENDIENTES: no es un paso a main." -ForegroundColor DarkYellow
  exit 2
}
Write-Host "PASA la compuerta automatica. Falta el nivel 4: >=2 pantallas, y contarlas en el PR." -ForegroundColor Green
exit 0
