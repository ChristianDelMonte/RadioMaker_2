# ============================================================
# build-mac.ps1
# Compila RadioMaker + RMVoice + RMWave para macOS (self-contained)
# desde una máquina Windows y empaqueta como .tar.gz.
#
# IMPORTANTE:
#   - Elegí el RID según el Mac de prueba:
#       osx-arm64  → Mac con Apple Silicon (M1/M2/M3/M4)
#       osx-x64    → Mac Intel
#   - Las librerías nativas de BASS para macOS (.dylib) deben
#     estar en: RadioMaker\src\RadioMaker.UI\runtimes\<rid>\native\
#     Descargalas de: https://www.un4seen.com
#   - Los binarios NO quedan firmados desde Windows. En el Mac
#     hay que correr "first-run.sh" una vez antes de ejecutar.
#
# Uso:
#   1. Copiá este archivo al directorio que contiene los 3 proyectos
#   2. Abrí PowerShell en ese directorio
#   3. Ejecutá:  .\build-mac.ps1
# ============================================================

$ErrorActionPreference = "Stop"

# --- Configuración ---
# Cambiar a "osx-x64" si el Mac de prueba es Intel.
$rid = "osx-arm64"

$root          = $PSScriptRoot
$rmDeep        = Join-Path $root "RadioMaker"
$rmVoice       = Join-Path $root "RMVoice"
$rmWave        = Join-Path $root "RMWave"
$publishRoot   = Join-Path $root "publish"
$outputFolder  = "RadioMaker-mac-$($rid -replace 'osx-','')"
$radioMakerOut = Join-Path $publishRoot $outputFolder
$rmVoiceTemp   = Join-Path $publishRoot "RMVoice-temp-$rid"
$rmWaveTemp    = Join-Path $publishRoot "RMWave-temp-$rid"

$config = "Release"

# --- Intro y Validaciones previas ---
Write-Host "*************************************************" -ForegroundColor Yellow
Write-Host "***     RADIOMAKER + Addons Compiler SHELL    ***" -ForegroundColor Yellow
Write-Host "*** Copyright (c) 2026 Christian A. Del Monte ***" -ForegroundColor Yellow
Write-Host "***  creadig@gmail.com / creadig@hotmail.com  ***" -ForegroundColor Yellow
Write-Host "*************************************************" -ForegroundColor Yellow
Write-Host "============= Validación de entorno =============" -ForegroundColor Cyan
Write-Host "Runtime Identifier: $rid"

$csprojRadioMaker = Join-Path $rmDeep "src\RadioMaker.UI\RadioMaker.UI.csproj"
$csprojRmVoice    = Join-Path $rmVoice "src\RMVoice.UI\RMVoice.UI.csproj"
$csprojRmWave     = Join-Path $rmWave "src\RMWave.UI\RMWave.UI.csproj"

if (-not (Test-Path $csprojRadioMaker)) { throw "No se encuentra: $csprojRadioMaker" }
if (-not (Test-Path $csprojRmVoice))    { throw "No se encuentra: $csprojRmVoice" }
if (-not (Test-Path $csprojRmWave))     { throw "No se encuentra: $csprojRmWave" }

# Verificar BASS macOS
$bassDir = Join-Path $rmDeep "src\RadioMaker.UI\runtimes\$rid\native"
if (-not (Test-Path $bassDir) -or -not (Test-Path (Join-Path $bassDir "libbass.dylib"))) {
    Write-Host "`nADVERTENCIA: no encontré '$bassDir\libbass.dylib'" -ForegroundColor Yellow
    Write-Host "Sin librerías nativas de BASS para macOS, el build compila pero falla al ejecutarse." -ForegroundColor Yellow
    Write-Host "Descargá el zip de macOS de https://www.un4seen.com y copiá los .dylib a:" -ForegroundColor Yellow
    Write-Host "  $bassDir" -ForegroundColor Yellow
    $continue = Read-Host "`n¿Continuar de todos modos? (s/n)"
    if ($continue -ne "s") { exit 1 }
}

# --- Limpieza de publicaciones previas para este RID ---
if (Test-Path $radioMakerOut) { Remove-Item -Recurse -Force $radioMakerOut }
if (Test-Path $rmVoiceTemp)   { Remove-Item -Recurse -Force $rmVoiceTemp }
if (Test-Path $rmWaveTemp)    { Remove-Item -Recurse -Force $rmWaveTemp }
New-Item -ItemType Directory -Force -Path $publishRoot | Out-Null

# --- Helper: restore + publish con RID ---
function Publish-Project {
    param(
        [string]$Csproj,
        [string]$OutputDir,
        [string]$Label
    )

    # Limpiar obj/ y bin/ del proyecto para forzar regeneración de project.assets.json
    $projDir = Split-Path $Csproj -Parent
    $objDir  = Join-Path $projDir "obj"
    $binDir  = Join-Path $projDir "bin"

    if (Test-Path $objDir) {
        Write-Host "  [clean] borrando $objDir" -ForegroundColor DarkGray
        Remove-Item -Recurse -Force $objDir
    }
    if (Test-Path $binDir) {
        Remove-Item -Recurse -Force $binDir
    }

    Write-Host "`n  [restore] $Label -r $rid --force" -ForegroundColor DarkGray
    dotnet restore $Csproj -r $rid --force
    if ($LASTEXITCODE -ne 0) { throw "Fallo el restore de $Label" }

    Write-Host "  [publish] $Label -c $config -r $rid --self-contained" -ForegroundColor DarkGray
    dotnet publish $Csproj `
        -c $config -r $rid --self-contained true `
        -p:PublishSingleFile=false `
        --no-restore `
        -o $OutputDir

    if ($LASTEXITCODE -ne 0) { throw "Fallo el publish de $Label" }
}

# --- 1. Publicar RadioMaker ---
Write-Host "`n[1/7] Publicando RadioMaker ($rid, self-contained)..." -ForegroundColor Cyan
Publish-Project -Csproj $csprojRadioMaker -OutputDir $radioMakerOut -Label "RadioMaker"

# --- 2. Publicar RMVoice ---
Write-Host "`n[2/7] Publicando RMVoice ($rid, self-contained)..." -ForegroundColor Cyan
Publish-Project -Csproj $csprojRmVoice -OutputDir $rmVoiceTemp -Label "RMVoice"

# --- 3. Publicar RMWave ---
Write-Host "`n[3/7] Publicando RMWave ($rid, self-contained)..." -ForegroundColor Cyan
Publish-Project -Csproj $csprojRmWave -OutputDir $rmWaveTemp -Label "RMWave"

# --- 4. Reemplazar los Addons ---
Write-Host "`n[4/7] Reemplazando Addons con builds nativas para macOS..." -ForegroundColor Yellow

$addonsRmVoice = Join-Path $radioMakerOut "Addons\RMVoice"
$addonsRmWave  = Join-Path $radioMakerOut "Addons\RMWave"

if (Test-Path $addonsRmVoice) { Remove-Item -Recurse -Force $addonsRmVoice }
if (Test-Path $addonsRmWave)  { Remove-Item -Recurse -Force $addonsRmWave }

Copy-Item -Recurse -Force $rmVoiceTemp $addonsRmVoice
Copy-Item -Recurse -Force $rmWaveTemp  $addonsRmWave

Remove-Item -Recurse -Force $rmVoiceTemp
Remove-Item -Recurse -Force $rmWaveTemp

# --- 5. Verificación de binarios ---
Write-Host "`n[5/7] Verificando binarios..." -ForegroundColor Cyan

$exes = @(
    @{ Name = "RadioMaker"; Path = (Join-Path $radioMakerOut "RadioMaker") },
    @{ Name = "RMVoice";    Path = (Join-Path $addonsRmVoice "RMVoice") },
    @{ Name = "RMWave";     Path = (Join-Path $addonsRmWave "RMWave") }
)

$allOk = $true
foreach ($exe in $exes) {
    if (Test-Path $exe.Path) {
        $size = [math]::Round((Get-Item $exe.Path).Length / 1MB, 2)
        Write-Host ("  OK    {0,-12} {1,7} MB" -f $exe.Name, $size) -ForegroundColor Green
    } else {
        Write-Host ("  FALTA {0,-12}" -f $exe.Name) -ForegroundColor Red
        $allOk = $false
    }
}

$libbassPath = Join-Path $radioMakerOut "runtimes\$rid\native\libbass.dylib"
if (Test-Path $libbassPath) {
    Write-Host "  OK    libbass.dylib presente en runtimes/" -ForegroundColor Green
} else {
    Write-Host "  FALTA libbass.dylib en runtimes/$rid/native/" -ForegroundColor Red
    $allOk = $false
}

# --- 6. Generar first-run.sh y run.sh ---
Write-Host "`n[6/7] Generando scripts de lanzamiento..." -ForegroundColor Cyan

# first-run.sh: chmod + codesign (ad-hoc). Ejecutar UNA VEZ en el Mac.
$firstRunPath = Join-Path $radioMakerOut "first-run.sh"
$firstRunLines = @(
    '#!/bin/bash'
    '# first-run.sh - Configuracion inicial en macOS'
    '#'
    '# Ejecutar UNA SOLA VEZ despues de extraer el tar.gz.'
    '# Hace:'
    '#   1. chmod +x sobre los 3 ejecutables'
    '#   2. codesign ad-hoc (obligatorio en Apple Silicon;'
    '#      recomendado en Intel para evitar advertencias de Gatekeeper)'
    '#   3. Limpia el atributo de cuarentena por las dudas'
    '#'
    '# Despues de correr esto, usar ./run.sh para lanzar la app.'
    'set -e'
    'DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"'
    'cd "$DIR"'
    ''
    'echo "== 1/3 Permisos de ejecucion =="'
    'chmod +x RadioMaker'
    '[ -f "Addons/RMVoice/RMVoice" ] && chmod +x Addons/RMVoice/RMVoice || true'
    '[ -f "Addons/RMWave/RMWave" ]   && chmod +x Addons/RMWave/RMWave   || true'
    ''
    'echo "== 2/3 Firma ad-hoc (codesign) =="'
    'if ! command -v codesign >/dev/null 2>&1; then'
    '    echo "  ERROR: codesign no disponible. Esto no es macOS?"'
    '    exit 1'
    'fi'
    'codesign --force --sign - RadioMaker'
    '[ -f "Addons/RMVoice/RMVoice" ] && codesign --force --sign - Addons/RMVoice/RMVoice || true'
    '[ -f "Addons/RMWave/RMWave" ]   && codesign --force --sign - Addons/RMWave/RMWave   || true'
    'find runtimes -name "*.dylib" -exec codesign --force --sign - {} \; 2>/dev/null || true'
    ''
    'echo "== 3/3 Limpieza de cuarentena =="'
    'xattr -d com.apple.quarantine RadioMaker 2>/dev/null || true'
    '[ -f "Addons/RMVoice/RMVoice" ] && xattr -d com.apple.quarantine Addons/RMVoice/RMVoice 2>/dev/null || true'
    '[ -f "Addons/RMWave/RMWave" ]   && xattr -d com.apple.quarantine Addons/RMWave/RMWave 2>/dev/null || true'
    ''
    'echo ""'
    'echo "=========================================="'
    'echo "  Configuracion completada."'
    'echo "  Ahora podes ejecutar:  ./run.sh"'
    'echo "=========================================="'
)
$firstRunContent = ($firstRunLines -join "`n") + "`n"
[System.IO.File]::WriteAllText($firstRunPath, $firstRunContent, (New-Object System.Text.UTF8Encoding $false))

# run.sh: lanzador normal (sin codesign).
$runShPath = Join-Path $radioMakerOut "run.sh"
$runShLines = @(
    '#!/bin/bash'
    '# Lanzador de RadioMaker para macOS.'
    'set -e'
    'DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"'
    'cd "$DIR"'
    './RadioMaker "$@"'
)
$runShContent = ($runShLines -join "`n") + "`n"
[System.IO.File]::WriteAllText($runShPath, $runShContent, (New-Object System.Text.UTF8Encoding $false))

Write-Host "  first-run.sh creado (ejecutar una vez en el Mac)" -ForegroundColor Green
Write-Host "  run.sh creado (lanzador normal)" -ForegroundColor Green

# 6b. Copiar README al publish
$readmeCandidates = @(
    (Join-Path $root "distribution\README-MAC.md"),
    (Join-Path $root "README-MAC.md")
)
$readmeCopied = $false
foreach ($candidate in $readmeCandidates) {
    if (Test-Path $candidate) {
        Copy-Item $candidate (Join-Path $radioMakerOut "README-MAC.md") -Force
        $readmeCopied = $true
        Write-Host "  README-MAC.md copiado" -ForegroundColor Green
        break
    }
}
if (-not $readmeCopied) {
    Write-Host "  ADVERTENCIA: no se encontró README-MAC.md (se omite)" -ForegroundColor Yellow
}

# --- 7. Empaquetar como .tar.gz ---
Write-Host "`n[7/7] Empaquetando como .tar.gz..." -ForegroundColor Cyan

$tarPath = Join-Path $publishRoot "$outputFolder.tar.gz"
$tarSuccess = $false

# En Windows el tar.exe es bsdtar y soporta --mode. Igual que en el script de
# Linux: forzamos 0755 para que los ejecutables tengan bit +x al extraer.
try {
    & tar.exe --mode=0755 -czf $tarPath -C $publishRoot $outputFolder 2>$null
    if ($LASTEXITCODE -eq 0 -and (Test-Path $tarPath)) {
        $tarSuccess = $true
        Write-Host "  tar.gz creado con --mode=0755" -ForegroundColor Green
    }
} catch { }

if (-not $tarSuccess) {
    Write-Host "  --mode no soportado, usando fallback..." -ForegroundColor Yellow
    & tar.exe -czf $tarPath -C $publishRoot $outputFolder
    if ($LASTEXITCODE -eq 0) { $tarSuccess = $true }
}

if ($tarSuccess) {
    $tarSize = [math]::Round((Get-Item $tarPath).Length / 1MB, 1)
    Write-Host ("  Tamaño del tar.gz: {0} MB" -f $tarSize) -ForegroundColor Cyan
}

# --- Resumen final ---
Write-Host "`n=== Build completado ($rid) ===" -ForegroundColor Green
Write-Host "Carpeta: $radioMakerOut"
Write-Host "Tar.gz : $tarPath`n"

$totalSize = (Get-ChildItem -Recurse $radioMakerOut | Measure-Object -Property Length -Sum).Sum
Write-Host ("Tamaño descomprimido: {0:N0} MB" -f ($totalSize / 1MB)) -ForegroundColor Cyan

if ($allOk -and $tarSuccess) {
    Write-Host "`nTodo listo. Copiá el .tar.gz al pendrive." -ForegroundColor Green
    Write-Host ""
    Write-Host "Flujo en el Mac:" -ForegroundColor Green
    Write-Host "  tar -xzf $outputFolder.tar.gz" -ForegroundColor White
    Write-Host "  cd $outputFolder" -ForegroundColor White
    Write-Host "  bash first-run.sh      # SOLO LA PRIMERA VEZ" -ForegroundColor White
    Write-Host "  ./run.sh               # lanzar la app" -ForegroundColor White
} else {
    Write-Host "`nRevisá los errores de arriba." -ForegroundColor Red
    exit 1
}
