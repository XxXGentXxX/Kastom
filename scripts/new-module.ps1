# Створює новий модуль з шаблону templates\Kastom.Modules.Template.
# Запуск: .\scripts\new-module.ps1 -Id show-greeting -Name "Привітання" -ClassName ShowGreeting
#   -Id         ідентифікатор модуля: латиниця в нижньому регістрі, цифри, дефіси ("audio", "desktop-widgets")
#   -Name       назва для людей (можна українською)
#   -ClassName  ім'я в PascalCase: за ним називаються тека, проєкт, простір імен і клас (<ClassName>Module)
#   -OutputRoot куди покласти теку модуля (типово <репозиторій>\modules)
#   -NoSolution не додавати проєкт у Kastom.slnx
param(
    [Parameter(Mandatory = $true)][string]$Id,
    [Parameter(Mandatory = $true)][string]$Name,
    [Parameter(Mandatory = $true)][string]$ClassName,
    [string]$OutputRoot,
    [switch]$NoSolution,
    [string]$Dotnet = "dotnet"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$template = Join-Path $root "templates\Kastom.Modules.Template"
$sdkProject = Join-Path $root "src\Kastom.Sdk\Kastom.Sdk.csproj"
$solution = Join-Path $root "Kastom.slnx"
if (-not $OutputRoot) { $OutputRoot = Join-Path $root "modules" }

# Той самий шаблон id, що й у ядрі (Kastom.Core\Runtime\IdPattern.cs), плюс межі з ModuleManifest.IsValidId.
if ($Id.Length -eq 0 -or $Id.Length -gt 40 -or $Id -cnotmatch '^[a-z0-9]+(-[a-z0-9]+)*$') {
    throw "Неправильний id '$Id': латиниця в нижньому регістрі, цифри й дефіси між ними, до 40 символів."
}
if ($Id -eq "kastom") { throw "Id 'kastom' зарезервовано за самим лаунчером." }
if ($ClassName -cnotmatch '^[A-Z][A-Za-z0-9]*$') {
    throw "Неправильне ім'я '$ClassName': PascalCase, лише латиниця й цифри, першою йде велика літера."
}
if ($Name.Trim().Length -eq 0) { throw "Назва модуля не може бути порожньою." }
if (-not (Test-Path $template)) { throw "Не знайдено шаблон: $template" }

$projectName = "Kastom.Modules.$ClassName"
$target = Join-Path ([IO.Path]::GetFullPath($OutputRoot)) $projectName
if (Test-Path $target) { throw "Тека вже існує: $target" }

# Шлях до Kastom.Sdk відносно нової теки (Uri працює і в PowerShell 5.1, де немає Path.GetRelativePath).
$from = New-Object System.Uri(($target.TrimEnd('\') + '\'))
$to = New-Object System.Uri($sdkProject)
$sdkRelative = [Uri]::UnescapeDataString($from.MakeRelativeUri($to).ToString()).Replace('/', '\')

$utf8Bom = New-Object System.Text.UTF8Encoding($true)
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

New-Item -ItemType Directory -Path $target | Out-Null
try {
    $files = Get-ChildItem -Path $template -File | Where-Object { $_.Name -notlike "*.user" }
    foreach ($file in $files) {
        $text = [IO.File]::ReadAllText($file.FullName, [Text.Encoding]::UTF8)
        $isJson = $file.Extension -eq ".json"

        if ($isJson) {
            # module.json: id і назва — окремі значення, решта слів Template — це простір імен, клас і збірка.
            $text = $text.Replace('"id": "template"', '"id": "' + $Id + '"')
            $text = $text.Replace('"name": "Шаблон модуля"', '"name": "' + $Name.Trim().Replace('\', '\\').Replace('"', '\"') + '"')
        }
        if ($file.Extension -eq ".csproj") {
            $text = $text.Replace('..\..\src\Kastom.Sdk\Kastom.Sdk.csproj', $sdkRelative)
        }
        $text = $text.Replace("Template", $ClassName)

        $newName = $file.Name.Replace("Template", $ClassName)
        # module.json — без BOM, решта файлів — UTF-8 з BOM.
        $encoding = if ($isJson) { $utf8NoBom } else { $utf8Bom }
        [IO.File]::WriteAllText((Join-Path $target $newName), $text, $encoding)
    }
}
catch {
    Remove-Item $target -Recurse -Force -ErrorAction SilentlyContinue
    throw
}

Write-Host "Створено: $target"

if (-not $NoSolution) {
    if (-not (Test-Path $solution)) { throw "Не знайдено $solution" }
    Push-Location $root
    try {
        & $Dotnet sln $solution add (Join-Path $target "$projectName.csproj") --solution-folder modules
        if ($LASTEXITCODE -ne 0) { throw "dotnet sln add завершився з кодом $LASTEXITCODE" }
    }
    finally { Pop-Location }
}

Write-Host ""
Write-Host "Далі:"
Write-Host "  1. Відкрийте $target\module.json і напишіть опис (description)."
Write-Host "  2. Змініть налаштування, дію й віджет у ${ClassName}Module.cs."
Write-Host "  3. dotnet build $target"
Write-Host "  Докладніше: docs\MODULES.md"
