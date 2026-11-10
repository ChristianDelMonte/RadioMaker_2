# ============================================================
# build-linux.ps1  (v2 - fuerza restore con RID)
# Compila RadioMaker + RMVoice + RMWave para Linux (self-contained)
# desde una máquina Windows y empaqueta como .tar.gz.
#
# Uso:
#   1. Copiá este archivo al directorio que contiene RadioMaker, RMVoice y RMWave
#   2. Abrí PowerShell en ese directorio
#   3. Ejecutá:  .\build-linux.ps1
#
# Requisitos:
#   - .NET 8 SDK (o superior) instalado
#   - Las librerías nativas de BASS para Linux (libbass.so, libbassmix.so,
#     libbass_fx.so) presentes en:
#      RadioMaker\src\RadioMaker.UI\runtimes\linux-x64\native\
#     Descargalas de: https://www.un4seen.com
# ============================================================

$ErrorActionPreference = "Stop"

# --- Configuración ---
$root          = $PSScriptRoot
$rmDeep        = Join-Path $root "RadioMaker"
$rmVoice       = Join-Path $root "RMVoice"
$rmWave        = Join-Path $root "RMWave"
$publishRoot   = Join-Path $root "publish"
$radioMakerOut = Join-Path $publishRoot "RadioMaker-linux"
$rmVoiceTemp   = Join-Path $publishRoot "RMVoice-temp"
$rmWaveTemp    = Join-Path $publishRoot "RMWave-temp"

$rid    = "linux-x64"
$config = "Release"

# --- Intro y Validaciones previas ---
Write-Host "*************************************************" -ForegroundColor Yellow
Write-Host "***     RADIOMAKER + Addons Compiler SHELL    ***" -ForegroundColor Yellow
Write-Host "*** Copyright (c) 2026 Christian A. Del Monte ***" -ForegroundColor Yellow
Write-Host "***  creadig@gmail.com / creadig@hotmail.com  ***" -ForegroundColor Yellow
Write-Host "*************************************************" -ForegroundColor Yellow
Write-Host "============= Validación de entorno =============" -ForegroundColor Cyan

$csprojRadioMaker = Join-Path $rmDeep "src\RadioMaker.UI\RadioMaker.UI.csproj"
$csprojRmVoice    = Join-Path $rmVoice "src\RMVoice.UI\RMVoice.UI.csproj"
$csprojRmWave     = Join-Path $rmWave "src\RMWave.UI\RMWave.UI.csproj"

if (-not (Test-Path $csprojRadioMaker)) { throw "No se encuentra: $csprojRadioMaker" }
if (-not (Test-Path $csprojRmVoice))    { throw "No se encuentra: $csprojRmVoice" }
if (-not (Test-Path $csprojRmWave))     { throw "No se encuentra: $csprojRmWave" }

# Verificar BASS Linux
$bassDir = Join-Path $rmDeep "src\RadioMaker.UI\runtimes\$rid\native"
if (-not (Test-Path $bassDir) -or -not (Test-Path (Join-Path $bassDir "libbass.so"))) {
    Write-Host "`nADVERTENCIA: no encontré '$bassDir\libbass.so'" -ForegroundColor Yellow
    Write-Host "Sin librerías nativas de BASS, el build compila pero falla al ejecutarse en Linux." -ForegroundColor Yellow
    Write-Host "Descargalas de https://www.un4seen.com" -ForegroundColor Yellow
    $continue = Read-Host "`n¿Continuar de todos modos? (s/n)"
    if ($continue -ne "s") { exit 1 }
}

# --- Limpieza previa ---
if (Test-Path $publishRoot) {
    Write-Host "`nLimpiando publicaciones previas..."
    Remove-Item -Recurse -Force $publishRoot
}

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

# --- 4. Reemplazar los Addons con las builds Linux ---
Write-Host "`n[4/7] Reemplazando Addons con builds nativas para Linux..." -ForegroundColor Yellow

$addonsRmVoice = Join-Path $radioMakerOut "Addons\RMVoice"
$addonsRmWave  = Join-Path $radioMakerOut "Addons\RMWave"

if (Test-Path $addonsRmVoice) { Remove-Item -Recurse -Force $addonsRmVoice }
if (Test-Path $addonsRmWave)  { Remove-Item -Recurse -Force $addonsRmWave }

Copy-Item -Recurse -Force $rmVoiceTemp $addonsRmVoice
Copy-Item -Recurse -Force $rmWaveTemp  $addonsRmWave

# --- 5. Limpieza de temporales ---
Remove-Item -Recurse -Force $rmVoiceTemp
Remove-Item -Recurse -Force $rmWaveTemp

# --- 6. Verificación de binarios ---
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

$libbassPath = Join-Path $radioMakerOut "runtimes\$rid\native\libbass.so"
if (Test-Path $libbassPath) {
    Write-Host "  OK    libbass.so presente en runtimes/" -ForegroundColor Green
} else {
    Write-Host "  FALTA libbass.so en runtimes/$rid/native/" -ForegroundColor Red
    $allOk = $false
}

# --- 7. Empaquetar como .tar.gz ---
Write-Host "`n[6/7] Preparando lanzador run.sh..." -ForegroundColor Cyan

$runShPath = Join-Path $radioMakerOut "run.sh"
$runShLines = @(
    '#!/bin/bash'
    '# Lanzador de RadioMaker para Linux.'
    '# Da permisos de ejecucion a los binarios y arranca la app.'
    '# Necesario porque los permisos Unix se pierden al pasar el .tar.gz'
    '# por un pendrive formateado en FAT32/exFAT.'
    'set -e'
    'DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"'
    'cd "$DIR"'
    'chmod +x RadioMaker 2>/dev/null || true'
    '[ -f "Addons/RMVoice/RMVoice" ] && chmod +x Addons/RMVoice/RMVoice || true'
    '[ -f "Addons/RMWave/RMWave" ]   && chmod +x Addons/RMWave/RMWave   || true'
    './RadioMaker "$@"'
)
$runShContent = ($runShLines -join "`n") + "`n"
[System.IO.File]::WriteAllText($runShPath, $runShContent, (New-Object System.Text.UTF8Encoding $false))
Write-Host "  run.sh creado" -ForegroundColor Green

# 7a-bis. Copiar README al publish
$readmeCandidates = @(
    (Join-Path $root "distribution\README-LINUX.md"),
    (Join-Path $root "README-LINUX.md")
)
$readmeCopied = $false
foreach ($candidate in $readmeCandidates) {
    if (Test-Path $candidate) {
        Copy-Item $candidate (Join-Path $radioMakerOut "README-LINUX.md") -Force
        $readmeCopied = $true
        Write-Host "  README-LINUX.md copiado" -ForegroundColor Green
        break
    }
}
if (-not $readmeCopied) {
    Write-Host "  ADVERTENCIA: no se encontró README-LINUX.md (se omite)" -ForegroundColor Yellow
}

# 7b. Empaquetar con --mode=0755 para forzar bit de ejecucion
Write-Host "`n[7/7] Empaquetando como .tar.gz..." -ForegroundColor Cyan

$tarPath = Join-Path $publishRoot "RadioMaker-linux.tar.gz"
$tarSuccess = $false

try {
    & tar.exe --mode=0755 -czf $tarPath -C $publishRoot "RadioMaker-linux" 2>$null
    if ($LASTEXITCODE -eq 0 -and (Test-Path $tarPath)) {
        $tarSuccess = $true
        Write-Host "  tar.gz creado con --mode=0755 (permisos preservados)" -ForegroundColor Green
    }
} catch { }

if (-not $tarSuccess) {
    Write-Host "  --mode no soportado por este tar.exe, usando fallback..." -ForegroundColor Yellow
    & tar.exe -czf $tarPath -C $publishRoot "RadioMaker-linux"
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  ERROR: no se pudo crear el tar.gz" -ForegroundColor Red
    } else {
        $tarSuccess = $true
        Write-Host "  tar.gz creado (sin --mode, requiere run.sh o chmod en Linux)" -ForegroundColor Yellow
    }
}

if ($tarSuccess) {
    $tarListing = & tar.exe -tvzf $tarPath 2>$null
    $exeLine = $tarListing | Where-Object { $_ -match "RadioMaker-linux/RadioMaker$" } | Select-Object -First 1
    if ($exeLine -match "^-rwx") {
        Write-Host "  Verificacion: RadioMaker tiene +x en el tar" -ForegroundColor Green
    } elseif ($exeLine) {
        Write-Host "  Verificacion: RadioMaker NO tiene +x (usar run.sh en Linux)" -ForegroundColor Yellow
    }

    $tarSize = [math]::Round((Get-Item $tarPath).Length / 1MB, 1)
    Write-Host ("  Tamaño del tar.gz: {0} MB" -f $tarSize) -ForegroundColor Cyan
}

# --- Resumen final ---
Write-Host "`n=== Build completado ===" -ForegroundColor Green
Write-Host "Carpeta: $radioMakerOut"
Write-Host "Tar.gz : $tarPath`n"

$totalSize = (Get-ChildItem -Recurse $radioMakerOut | Measure-Object -Property Length -Sum).Sum
Write-Host ("Tamaño de la carpeta descomprimida: {0:N0} MB" -f ($totalSize / 1MB)) -ForegroundColor Cyan

if ($allOk -and $tarSuccess) {
    Write-Host "`nTodo listo. Copiá el .tar.gz al pendrive." -ForegroundColor Green
    Write-Host "En Linux:" -ForegroundColor Green
    Write-Host "  tar -xzf RadioMaker-linux.tar.gz" -ForegroundColor White
    Write-Host "  cd RadioMaker-linux" -ForegroundColor White
    Write-Host "  ./run.sh" -ForegroundColor White
} else {
    Write-Host "`nRevisá los errores de arriba." -ForegroundColor Red
    exit 1
}
