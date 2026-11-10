# ============================================================
# build-windows.ps1
# Compila RadioMaker + RMVoice + RMWave para Windows (self-contained)
# y empaqueta como .zip.
#
# Uso:
#   1. Copiá este archivo al directorio que contiene RM-Deep, RMVoice y RMWave
#   2. Abrí PowerShell en ese directorio
#   3. Ejecutá:  .\build-windows.ps1
#
# Requisitos:
#   - .NET 8 SDK (o superior) instalado
#   - Las librerías nativas de BASS para Windows (bass.dll, bassmix.dll,
#     bass_fx.dll) presentes en:
#       RM-Deep\src\RmsAudioPlayer.UI\runtimes\win-x64\native\
#     Descargalas de: https://www.un4seen.com
# ============================================================

$ErrorActionPreference = "Stop"

# --- Configuración ---
$rid    = "win-x64"
$config = "Release"

$root          = $PSScriptRoot
$rmDeep        = Join-Path $root "RadioMaker"
$rmVoice       = Join-Path $root "RMVoice"
$rmWave        = Join-Path $root "RMWave"
$publishRoot   = Join-Path $root "publish"
$outputFolder  = "RadioMaker-windows-x64"
$radioMakerOut = Join-Path $publishRoot $outputFolder
$rmVoiceTemp   = Join-Path $publishRoot "RMVoice-temp-win"
$rmWaveTemp    = Join-Path $publishRoot "RMWave-temp-win"

# --- Validaciones previas ---
Write-Host "=== Validación de entorno ===" -ForegroundColor Cyan
Write-Host "Runtime Identifier: $rid"

$csprojRadioMaker = Join-Path $rmDeep "src\RadioMaker.UI\RadioMaker.UI.csproj"
$csprojRmVoice    = Join-Path $rmVoice "src\RMVoice.UI\RMVoice.UI.csproj"
$csprojRmWave     = Join-Path $rmWave "src\RMWave.UI\RMWave.UI.csproj"

if (-not (Test-Path $csprojRadioMaker)) { throw "No se encuentra: $csprojRadioMaker" }
if (-not (Test-Path $csprojRmVoice))    { throw "No se encuentra: $csprojRmVoice" }
if (-not (Test-Path $csprojRmWave))     { throw "No se encuentra: $csprojRmWave" }

# Verificar BASS Windows
$bassDir = Join-Path $rmDeep "src\RadioMaker.UI\runtimes\$rid\native"
if (-not (Test-Path $bassDir) -or -not (Test-Path (Join-Path $bassDir "bass.dll"))) {
    Write-Host "`nADVERTENCIA: no encontré '$bassDir\bass.dll'" -ForegroundColor Yellow
    Write-Host "Sin librerías nativas de BASS, el build compila pero falla al ejecutarse." -ForegroundColor Yellow
    Write-Host "Descargalas de https://www.un4seen.com" -ForegroundColor Yellow
    $continue = Read-Host "`n¿Continuar de todos modos? (s/n)"
    if ($continue -ne "s") { exit 1 }
}

# --- Limpieza de publicaciones previas ---
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
Write-Host "`n[4/7] Reemplazando Addons con builds nativas..." -ForegroundColor Yellow

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
    @{ Name = "RadioMaker.exe"; Path = (Join-Path $radioMakerOut "RadioMaker.exe") },
    @{ Name = "RMVoice.exe";    Path = (Join-Path $addonsRmVoice "RMVoice.exe") },
    @{ Name = "RMWave.exe";     Path = (Join-Path $addonsRmWave "RMWave.exe") }
)

$allOk = $true
foreach ($exe in $exes) {
    if (Test-Path $exe.Path) {
        $size = [math]::Round((Get-Item $exe.Path).Length / 1MB, 2)
        Write-Host ("  OK    {0,-15} {1,7} MB" -f $exe.Name, $size) -ForegroundColor Green
    } else {
        Write-Host ("  FALTA {0,-15}" -f $exe.Name) -ForegroundColor Red
        $allOk = $false
    }
}

$bassPath = Join-Path $radioMakerOut "runtimes\$rid\native\bass.dll"
if (Test-Path $bassPath) {
    Write-Host "  OK    bass.dll presente en runtimes/" -ForegroundColor Green
} else {
    Write-Host "  FALTA bass.dll en runtimes/$rid/native/" -ForegroundColor Red
    $allOk = $false
}

# --- 6. Copiar README ---
Write-Host "`n[6/7] Copiando README..." -ForegroundColor Cyan

$readmeCandidates = @(
    (Join-Path $root "distribution\README-WINDOWS.md"),
    (Join-Path $root "README-WINDOWS.md")
)
$readmeCopied = $false
foreach ($candidate in $readmeCandidates) {
    if (Test-Path $candidate) {
        Copy-Item $candidate (Join-Path $radioMakerOut "README-WINDOWS.md") -Force
        Write-Host "  README-WINDOWS.md copiado" -ForegroundColor Green
        $readmeCopied = $true
        break
    }
}
if (-not $readmeCopied) {
    Write-Host "  ADVERTENCIA: no se encontró README-WINDOWS.md (se omite)" -ForegroundColor Yellow
}

# --- 7. Empaquetar como .zip ---
Write-Host "`n[7/7] Empaquetando como .zip..." -ForegroundColor Cyan

$zipPath = Join-Path $publishRoot "$outputFolder.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

try {
    Compress-Archive -Path (Join-Path $publishRoot $outputFolder) -DestinationPath $zipPath -CompressionLevel Optimal
    $zipSize = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
    Write-Host ("  zip creado: {0} MB" -f $zipSize) -ForegroundColor Green
} catch {
    Write-Host "  ERROR al crear el .zip: $_" -ForegroundColor Red
    $allOk = $false
}

# --- Resumen final ---
Write-Host "`n=== Build completado ($rid) ===" -ForegroundColor Green
Write-Host "Carpeta: $radioMakerOut"
Write-Host "Zip    : $zipPath`n"

$totalSize = (Get-ChildItem -Recurse $radioMakerOut | Measure-Object -Property Length -Sum).Sum
Write-Host ("Tamaño descomprimido: {0:N0} MB" -f ($totalSize / 1MB)) -ForegroundColor Cyan

if ($allOk) {
    Write-Host "`nTodo listo. Distribuí el .zip a los usuarios de Windows." -ForegroundColor Green
    Write-Host ""
    Write-Host "Flujo del usuario final:" -ForegroundColor Green
    Write-Host "  1. Extraer el .zip en una carpeta local (ej: C:\RadioMaker\)" -ForegroundColor White
    Write-Host "  2. Ejecutar RadioMaker.exe" -ForegroundColor White
    Write-Host "  3. SmartScreen → 'Más información' → 'Ejecutar de todas formas'" -ForegroundColor White
} else {
    Write-Host "`nRevisá los errores de arriba." -ForegroundColor Red
    exit 1
}