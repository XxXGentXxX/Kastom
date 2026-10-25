# Збирає лаунчер і пакує його Velopack-ом у Releases\.
# Один раз встановіть інструмент: dotnet tool install -g vpk
# Запуск: .\scripts\pack-launcher.ps1 [-Version 0.1.0]
param(
    [string]$Version = "0.1.0",
    [string]$Dotnet = "dotnet"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\Kastom.Launcher\Kastom.Launcher.csproj"
$publish = Join-Path $root "artifacts\publish"
$releases = Join-Path $root "Releases"

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }

& $Dotnet publish $project -c Release -r win-x64 --self-contained true -p:Version=$Version -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish завершився з кодом $LASTEXITCODE" }

vpk pack --packId Kastom --packVersion $Version --packDir $publish --mainExe Kastom.exe --packTitle Kastom --outputDir $releases
if ($LASTEXITCODE -ne 0) { throw "vpk pack завершився з кодом $LASTEXITCODE" }

Write-Host "Готово: $releases"
