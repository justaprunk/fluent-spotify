# Публикует Fluent Spotify (self-contained, win-x64) и собирает установщик Inno Setup.
# Запуск из любой папки:  powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1
# Результат: installer\Output\FluentSpotify-<версия>-Setup-x64.exe и .sha256 рядом.

param(
    [string]$Iscc
)

$ErrorActionPreference = 'Stop'
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

Write-Host "Готово: $setup"
Write-Host "SHA-256: $hash"
