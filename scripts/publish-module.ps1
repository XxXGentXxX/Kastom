# Публікує модуль одним рухом: збирає, пакує, додає в каталог, підписує й перевіряє.
# Без -Publish нічого не відправляється в мережу і catalog\catalog.json не змінюється:
# новий підписаний каталог лягає в artifacts\modules\<id>\<версія>\catalog\ як попередній перегляд.
# З -Publish: перевірки git, збирання, підпис у тимчасовій теці, реліз GitHub, лише потім каталог у репозиторій.
# Запуск: .\scripts\publish-module.ps1 -Project modules\Kastom.Modules.Demo [-Publish] [-InitCatalog]
# Секретний ключ підпису лежить поза репозиторієм і поза Google Диском (типово %USERPROFILE%\.kastom\keys).
param(
    [Parameter(Mandatory = $true)][string]$Project,
    [string]$Key = "$env:USERPROFILE\.kastom\keys\catalog-signing.pem",
    [string]$Catalog,
    [string]$PublicKey,
    [string]$Repo = "XxXGentXxX/Kastom",
    [switch]$Publish,
    # Перший каталог без бази: потрібен, лише коли каталогу ще немає.
    [switch]$InitCatalog
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

# Повний шлях від поточної теки PowerShell (а не від теки процесу, яка може бути іншою).
function Get-FullPath([string]$Path) {
    return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
}

# Запускає зовнішню команду й зупиняє скрипт, якщо вона завершилась не з кодом 0.
# У Windows PowerShell 5.1 текст у stderr (git push пише туди звичайні повідомлення) при
# $ErrorActionPreference = "Stop" обривав би скрипт, тож рішення приймається лише за кодом виходу.
function Invoke-Step([string]$Name, [scriptblock]$Command) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try { & $Command }
    finally { $ErrorActionPreference = $previous }
    if ($LASTEXITCODE -ne 0) { throw "$Name завершився з кодом $LASTEXITCODE" }
}

# Те саме, але повертає вивід команди (stdout) як текст.
function Get-StepOutput([string]$Name, [scriptblock]$Command) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try { $output = & $Command }
    finally { $ErrorActionPreference = $previous }
    if ($LASTEXITCODE -ne 0) { throw "$Name завершився з кодом $LASTEXITCODE" }
    return (@($output) -join "`n").Trim()
}

# ---- Модуль: тека, .csproj, module.json ----

$projectPath = (Resolve-Path -LiteralPath (Get-FullPath $Project)).Path
if (Test-Path -LiteralPath $projectPath -PathType Leaf) {
    $csproj = $projectPath
    $moduleDir = Split-Path -Parent $projectPath
}
else {
    $moduleDir = $projectPath
    $found = @(Get-ChildItem -LiteralPath $moduleDir -Filter *.csproj -File)
    if ($found.Count -ne 1) { throw "У теці '$moduleDir' має бути рівно один .csproj, знайдено: $($found.Count)." }
    $csproj = $found[0].FullName
}

$manifestPath = Join-Path $moduleDir "module.json"
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw "У теці модуля немає module.json: $moduleDir" }
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$id = [string]$manifest.id
$version = [string]$manifest.version
$name = [string]$manifest.name
if ($id -notmatch '^[a-z0-9]+(-[a-z0-9]+)*$') { throw "Неправильний id у module.json: '$id'." }
# Версія стає назвою теки й тегу релізу, тож дозволено лише цифри, латиницю, крапки й дефіси (без build metadata після '+').
if ($version -notmatch '^[0-9A-Za-z.-]+$') { throw "Неправильна версія у module.json: '$version'. Дозволено лише [0-9A-Za-z.-], без '+'." }
$tag = "$id-v$version"

# ---- Каталог: наявність бази ----

$catalogGiven = $PSBoundParameters.ContainsKey("Catalog")
if (-not $Catalog) { $Catalog = Join-Path $root "catalog\catalog.json" }
$Catalog = Get-FullPath $Catalog
$catalogExists = Test-Path -LiteralPath $Catalog -PathType Leaf
if ($InitCatalog) {
    if ($catalogExists) { throw "Каталог уже існує ($Catalog): -InitCatalog потрібен лише для першого каталогу, інакше решту модулів буде втрачено." }
}
elseif (-not $catalogExists) {
    $which = if ($catalogGiven) { "Указаний каталог" } else { "Каталог" }
    throw "$which не знайдено: $Catalog. Перевірте шлях; для першого каталогу без бази додайте -InitCatalog."
}

$rootPrefix = [System.IO.Path]::GetFullPath($root).TrimEnd('\') + '\'
if ($Publish -and -not $Catalog.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "З -Publish каталог має лежати в репозиторії ($root), бо його буде закомічено: $Catalog"
}

# ---- Ключ: не в репозиторії й не на Google Диску ----

$keyFull = Get-FullPath $Key
if ($keyFull.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Секретний ключ лежить у теці репозиторію ($keyFull). Перенесіть його, наприклад у %USERPROFILE%\.kastom\keys."
}
if ($keyFull -like '*Мій диск*' -or $keyFull -like '*Google Drive*') {
    throw "Секретний ключ лежить на Google Диску ($keyFull). Перенесіть його, наприклад у %USERPROFILE%\.kastom\keys."
}
if (-not (Test-Path -LiteralPath $keyFull -PathType Leaf)) { throw "Секретний ключ не знайдено: $keyFull" }

# Відкритий ключ — файл або сам base64; файл передається повним шляхом.
if ($PublicKey) {
    $publicFull = Get-FullPath $PublicKey
    if (Test-Path -LiteralPath $publicFull -PathType Leaf) { $PublicKey = $publicFull }
}

# ---- З -Publish: репозиторій має бути чистим і актуальним (до збирання) ----

$signaturePath = "$Catalog.sig"
if ($Publish) {
    Invoke-Step "git fetch" { git -C $root fetch origin }
    $branch = Get-StepOutput "git rev-parse (гілка)" { git -C $root rev-parse --abbrev-ref HEAD }
    if ($branch -ne "main") { throw "Публікувати можна лише з гілки main, зараз: '$branch'." }
    $head = Get-StepOutput "git rev-parse HEAD" { git -C $root rev-parse HEAD }
    $remoteHead = Get-StepOutput "git rev-parse origin/main" { git -C $root rev-parse origin/main }
    if ($head -ne $remoteHead) {
        throw "main відрізняється від origin/main (локально $head, на сервері $remoteHead). Зробіть git pull чи git push і повторіть."
    }
    $dirty = Get-StepOutput "git status" { git -C $root status --porcelain -- $Catalog $signaturePath }
    if ($dirty) { throw "Каталог у репозиторії має незакомічені зміни:`n$dirty`nЗакомітьте або відкотіть їх і повторіть." }
    # Пакет збирається з робочої теки: незакомічений код модуля потрапив би в реліз, якого немає в main.
    $moduleDirty = Get-StepOutput "git status (модуль)" { git -C $root status --porcelain -- $moduleDir }
    if ($moduleDirty) { throw "Тека модуля має незакомічені зміни:`n$moduleDirty`nСпершу закомітьте й запуште код модуля." }
    # Коміт каталогу не має прихопити чужі зміни, що вже лежать в індексі.
    $staged = Get-StepOutput "git diff --cached" { git -C $root diff --cached --name-only }
    if ($staged) { throw "В індексі git є підготовлені зміни:`n$staged`nЗакомітьте або приберіть їх з індексу й повторіть." }
}

# ---- Збирання інструмента й модуля ----

$tool = Join-Path $root "tools\Kastom.CatalogTool\bin\Release\net10.0-windows\kastom-catalog.exe"
$outDir = Join-Path $root "artifacts\modules\$id\$version"
$buildDir = Join-Path $outDir "build"
$zip = Join-Path $outDir "$id-$version.zip"

Invoke-Step "dotnet build (kastom-catalog)" { dotnet build (Join-Path $root "tools\Kastom.CatalogTool") -c Release }
if (Test-Path -LiteralPath $buildDir) { Remove-Item -LiteralPath $buildDir -Recurse -Force }
Invoke-Step "dotnet build (модуль)" { dotnet build $csproj -c Release -warnaserror -o $buildDir }
# -o кладе поруч і збірки проєктів-посилань. Kastom.Sdk модуль бере з лаунчера, тож у пакеті йому не місце.
Get-ChildItem -LiteralPath $buildDir -Filter "Kastom.Sdk.*" | Remove-Item -Force

# ---- Пакет і каталог ----

Invoke-Step "kastom-catalog pack" { & $tool pack --dir $buildDir --out $zip }

$work = Join-Path ([System.IO.Path]::GetTempPath()) ("kastom-publish-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $work | Out-Null
try {
    # У теці пакетів лежить лише цей zip: решту модулів каталог бере з бази (--base).
    $packages = Join-Path $work "packages"
    New-Item -ItemType Directory -Path $packages | Out-Null
    Copy-Item -LiteralPath $zip -Destination $packages

    $baseUrl = "https://github.com/$Repo/releases/download/$tag"
    $newCatalog = Join-Path $work "catalog.json"
    $catalogArgs = @("catalog", "--packages", $packages, "--base-url", $baseUrl, "--out", $newCatalog)
    if (-not $InitCatalog) { $catalogArgs += @("--base", $Catalog) }
    if ($PublicKey) { $catalogArgs += @("--public", $PublicKey) }
    Invoke-Step "kastom-catalog catalog" { & $tool @catalogArgs }

    # Підпис і перевірка — ще в тимчасовій теці: якщо щось упаде, опублікований каталог лишиться як був.
    Invoke-Step "kastom-catalog sign" { & $tool sign --key $keyFull --catalog $newCatalog }

    $verifyArgs = @("verify", "--catalog", $newCatalog)
    if ($PublicKey) { $verifyArgs += @("--public", $PublicKey) }
    Invoke-Step "kastom-catalog verify" { & $tool @verifyArgs }

    $title = "$name $version"
    $notes = "Модуль '$name' версії $version."

    if (-not $Publish) {
        # Попередній перегляд: catalog\ у репозиторії не чіпаємо.
        $previewDir = Join-Path $outDir "catalog"
        if (Test-Path -LiteralPath $previewDir) { Remove-Item -LiteralPath $previewDir -Recurse -Force }
        New-Item -ItemType Directory -Path $previewDir | Out-Null
        Copy-Item -LiteralPath "$newCatalog.sig" -Destination (Join-Path $previewDir "catalog.json.sig")
        Copy-Item -LiteralPath $newCatalog -Destination (Join-Path $previewDir "catalog.json")

        Write-Host ""
        Write-Host "Готово без публікації: у мережу нічого не відправлено, $Catalog не змінено."
        Write-Host "Пакет: $zip"
        Write-Host "Підписаний каталог (попередній перегляд): $previewDir"
        Write-Host "Щоб опублікувати, запустіть той самий скрипт з -Publish. Він зробить:"
        Write-Host "  gh release create $tag `"$zip`" --repo $Repo --title `"$title`" --notes `"$notes`""
        Write-Host "  потім перенесе каталог у `"$Catalog`" і виконає git add, git commit та git push."
        return
    }

    # ---- Публікація: спершу реліз, і лише потім каталог у репозиторії ----

    Invoke-Step "gh release create" { gh release create $tag $zip --repo $Repo --title $title --notes $notes }

    $catalogDir = Split-Path -Parent $Catalog
    if (-not (Test-Path -LiteralPath $catalogDir)) { New-Item -ItemType Directory -Path $catalogDir | Out-Null }
    # Підпис першим: обірване перенесення лишить старий каталог з новим підписом, і лаунчер його просто не прийме.
    Move-Item -LiteralPath "$newCatalog.sig" -Destination $signaturePath -Force
    Move-Item -LiteralPath $newCatalog -Destination $Catalog -Force

    Invoke-Step "git add" { git -C $root add $Catalog $signaturePath }
    # Повідомлення коміту — файл UTF-8 без BOM.
    $messageFile = Join-Path $work "commit-message.txt"
    $message = "Каталог: $id $version`n`nДодано модуль '$name' версії $version, реліз $tag.`n`nCo-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`n"
    [System.IO.File]::WriteAllText($messageFile, $message, (New-Object System.Text.UTF8Encoding($false)))
    # Лише файли каталогу, навіть якщо щось потрапило в індекс уже після перевірки.
    Invoke-Step "git commit" { git -C $root commit -F $messageFile -- $Catalog $signaturePath }
    Invoke-Step "git push" { git -C $root push }

    Write-Host "Опубліковано: $tag"
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
