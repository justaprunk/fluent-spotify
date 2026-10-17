# Публикует Fluent Spotify (self-contained, win-x64) и собирает установщик Inno Setup.
# Запуск из любой папки:  powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1
# Результат: installer\Output\FluentSpotify-<версия>-Setup-x64.exe, рядом .sha256 и .sha256.sig —
# подпись .sha256 ключом из signing-key.ps1. Без .sig установленные копии не примут обновление.
# -Unsigned — сборка без ключа (для себя или форка); такой установщик не годится для релиза.

param(
    [string]$Iscc,
    [switch]$Unsigned
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'signing-key.ps1') -Library
# Ключ подписи нужен в конце, но проверяем его до долгой сборки
$signingKey = if ($Unsigned) { $null } else { Get-SigningKey }
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'fluent-spotify.csproj'
$publishDir = Join-Path $root 'publish\win-x64'

# Версии берём из csproj, чтобы они не расходились
[xml]$csproj = Get-Content $project
$props = $csproj.Project.PropertyGroup | Where-Object { $_.InformationalVersion } | Select-Object -First 1
$appVersion = $props.InformationalVersion
$fileVersion = $props.FileVersion
if (-not $appVersion -or -not $fileVersion) { throw 'В csproj не заданы InformationalVersion/FileVersion' }

if (-not $Iscc) {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    )
    $Iscc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $Iscc) { throw 'ISCC.exe не найден. Установите Inno Setup: winget install JRSoftware.InnoSetup' }
}

Write-Host "Fluent Spotify $appVersion ($fileVersion)"

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
dotnet publish $project -c Release -r win-x64 --self-contained true -p:Platform=x64 -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish завершился с кодом $LASTEXITCODE" }

& $Iscc "/DAppVersion=$appVersion" "/DAppFileVersion=$fileVersion" "/DPublishDir=$publishDir" (Join-Path $PSScriptRoot 'FluentSpotify.iss')
if ($LASTEXITCODE -ne 0) { throw "ISCC завершился с кодом $LASTEXITCODE" }

$setup = Join-Path $PSScriptRoot "Output\FluentSpotify-$appVersion-Setup-x64.exe"
$hash = (Get-FileHash $setup -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash *$(Split-Path -Leaf $setup)" | Set-Content -Encoding ascii "$setup.sha256"

# Подписываем .sha256, а не сам exe: в нём и хеш, и имя файла с версией, поэтому
# подписанный установщик старой версии нельзя выдать за новую
if ($signingKey) {
    $sig = New-FileSignature $signingKey "$setup.sha256"
    $signingKey.Dispose()
}
else {
    Remove-Item "$setup.sha256.sig" -ErrorAction SilentlyContinue
}

Write-Host "Готово: $setup"
Write-Host "SHA-256: $hash"
if ($signingKey) { Write-Host "Подпись: $sig (загрузите в релиз вместе с exe и .sha256)" }
else { Write-Warning 'Сборка без подписи (-Unsigned): установленные копии не примут её как обновление' }
