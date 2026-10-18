# Ключ подписи обновлений Fluent Spotify (ECDSA P-256). Секретная часть хранится вне репозитория:
# %USERPROFILE%\.fluent-spotify\update-signing-key.dpapi — зашифрована DPAPI текущего пользователя
# Windows, NTFS-доступ только у него. Открытый ключ встроен в приложение (Core/Updates/UpdateSignature.cs).
#
#   powershell -ExecutionPolicy Bypass -File installer\signing-key.ps1 -New
#       создать ключ (если его ещё нет) и вывести открытый ключ для UpdateSignature.cs
#   powershell -ExecutionPolicy Bypass -File installer\signing-key.ps1
#       вывести открытый ключ существующего ключа
#   pwsh -File installer\signing-key.ps1 -Export D:\backup\fluent-spotify-signing.pem
#       резервная копия: PKCS#8 PEM, зашифрованный паролем (нужен PowerShell 7)
#   pwsh -File installer\signing-key.ps1 -Import D:\backup\fluent-spotify-signing.pem
#       восстановить ключ из резервной копии на этом или другом ПК
#   pwsh -File installer\signing-key.ps1 -NewRecovery E:\fluent-spotify-recovery.pem
#       создать резервный ключ сразу в зашифрованном файле на флешке (на ПК не сохраняется)
#   pwsh -File installer\signing-key.ps1 -SignWith E:\fluent-spotify-recovery.pem -File <установщик>.sha256
#       подписать резервным ключом, если основной утерян или скомпрометирован
#
# Без этого ключа выпустить обновление, которое примут уже установленные копии, нельзя:
# пользователям придётся переустановить приложение вручную. Храните резервную копию офлайн.
#
# Скрипт также подключается из build-installer.ps1 (dot-source с -Library) ради функций ниже.

param(
    [switch]$New,
    [string]$Export,
    [string]$Import,
    [string]$NewRecovery,
    [string]$SignWith,
    [string]$File,
    [switch]$Library
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security

$script:SigningKeyPath = Join-Path $env:USERPROFILE '.fluent-spotify\update-signing-key.dpapi'
# Дополнительная энтропия DPAPI: блоб нельзя расшифровать «по ошибке» другим кодом этого пользователя
$script:SigningKeyEntropy = [Text.Encoding]::ASCII.GetBytes('FluentSpotify.UpdateSigningKey.v1')
# Заголовок SubjectPublicKeyInfo для P-256 (id-ecPublicKey + prime256v1), дальше 04 || X || Y
$script:SpkiPrefix = [byte[]](0x30,0x59,0x30,0x13,0x06,0x07,0x2a,0x86,0x48,0xce,0x3d,0x02,0x01,0x06,0x08,0x2a,0x86,0x48,0xce,0x3d,0x03,0x01,0x07,0x03,0x42,0x00,0x04)

function Get-SigningKey {
    # Блоб: D || X || Y по 32 байта. ECParameters, а не PKCS#8 — так ключ читается
    # и в Windows PowerShell 5.1 (.NET Framework), и в PowerShell 7
    if (-not (Test-Path $script:SigningKeyPath)) {
        throw "Ключ подписи не найден: $($script:SigningKeyPath). Создайте его (signing-key.ps1 -New) или восстановите из резервной копии (-Import)."
    }
    $raw = [Security.Cryptography.ProtectedData]::Unprotect(
        [IO.File]::ReadAllBytes($script:SigningKeyPath), $script:SigningKeyEntropy, 'CurrentUser')
    try {
        if ($raw.Length -ne 96) { throw 'Файл ключа подписи повреждён' }
        $p = New-Object Security.Cryptography.ECParameters
        $p.Curve = [Security.Cryptography.ECCurve]::CreateFromFriendlyName('nistP256')
        $p.D = $raw[0..31]
        $q = New-Object Security.Cryptography.ECPoint
        $q.X = $raw[32..63]
        $q.Y = $raw[64..95]
        $p.Q = $q
        return [Security.Cryptography.ECDsa]::Create($p)
    }
    finally {
        [Array]::Clear($raw, 0, $raw.Length)
    }
}

function Save-SigningKey([Security.Cryptography.ECDsa]$Key) {
    $p = $Key.ExportParameters($true)
    $raw = [byte[]]($p.D + $p.Q.X + $p.Q.Y)
    try {
        if ($raw.Length -ne 96) { throw 'Ожидался ключ P-256' }
        $blob = [Security.Cryptography.ProtectedData]::Protect($raw, $script:SigningKeyEntropy, 'CurrentUser')
    }
    finally {
        [Array]::Clear($raw, 0, $raw.Length)
        if ($p.D) { [Array]::Clear($p.D, 0, $p.D.Length) }
    }

    $dir = Split-Path -Parent $script:SigningKeyPath
    New-Item -ItemType Directory -Force $dir | Out-Null
    # Доступ к папке только у текущего пользователя, без наследования от профиля
    $me = [Security.Principal.WindowsIdentity]::GetCurrent().User
    $acl = New-Object Security.AccessControl.DirectorySecurity
    $acl.SetAccessRuleProtection($true, $false)
    $acl.SetOwner($me)
    $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule(
        $me, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')))
    Set-Acl -Path $dir -AclObject $acl

    [IO.File]::WriteAllBytes($script:SigningKeyPath, $blob)
    $fileAcl = New-Object Security.AccessControl.FileSecurity
    $fileAcl.SetAccessRuleProtection($true, $false)
    $fileAcl.SetOwner($me)
    $fileAcl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($me, 'FullControl', 'Allow')))
    Set-Acl -Path $script:SigningKeyPath -AclObject $fileAcl
}

function Get-PublicKeyBase64([Security.Cryptography.ECDsa]$Key) {
    $q = $Key.ExportParameters($false).Q
    return [Convert]::ToBase64String([byte[]]($script:SpkiPrefix + $q.X + $q.Y))
}

# Подпись: ECDSA P-256 + SHA-256 над точными байтами файла, формат IEEE P1363 (r || s, 64 байта),
# в файле — одна строка base64. Так же проверяет UpdateSignature.Verify в приложении
function New-FileSignature([Security.Cryptography.ECDsa]$Key, [string]$Path) {
    $data = [IO.File]::ReadAllBytes($Path)
    $sig = $Key.SignData($data, [Security.Cryptography.HashAlgorithmName]::SHA256)
    if ($sig.Length -ne 64) { throw "Неожиданный формат подписи ($($sig.Length) байт)" }
    $sigPath = "$Path.sig"
    [Convert]::ToBase64String($sig) | Set-Content -Encoding ascii $sigPath
    if (-not $Key.VerifyData($data, $sig, [Security.Cryptography.HashAlgorithmName]::SHA256)) { throw 'Подпись не прошла самопроверку' }
    return $sigPath
}

if ($Library) { return }

function Read-Password([string]$Prompt) {
    $secure = Read-Host -AsSecureString $Prompt
    $ptr = [Runtime.InteropServices.Marshal]::SecureStringToGlobalAllocUnicode($secure)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringUni($ptr) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeGlobalAllocUnicode($ptr) }
}

function Assert-Pwsh([string]$What) {
    if ($PSVersionTable.PSEdition -ne 'Core') { throw "Для $What нужен PowerShell 7 (pwsh)" }
}

# Копию ключа нельзя случайно закоммитить или отправить в облако: только вне git и синхронизируемых папок
function Assert-OfflineLocation([string]$Path) {
    $dir = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($Path))
    if (-not (Test-Path $dir)) { throw "Папка не найдена: $dir" }
    if (Test-Path $Path) { throw "Файл уже есть: $Path — не перезаписываю" }
    if ((& git -C $dir rev-parse --is-inside-work-tree 2>$null) -eq 'true') { throw "Папка $dir внутри git-репозитория — выберите другое место (флешку)" }
    foreach ($synced in @($env:OneDrive, $env:OneDriveConsumer, $env:OneDriveCommercial, (Join-Path $env:USERPROFILE 'Dropbox'), (Join-Path $env:USERPROFILE 'Google Drive'))) {
        if ($synced -and $dir.StartsWith($synced, [StringComparison]::OrdinalIgnoreCase)) { throw "Папка $dir синхронизируется с облаком — выберите другое место (флешку)" }
    }
    # Облачные диски, подключённые буквой (Google Drive для ПК монтирует G:/E: и т. п.)
    $drive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($dir))
    if ($drive.VolumeLabel -match 'Google Drive|OneDrive|Dropbox|iCloud|Yandex|MEGA') {
        throw "Диск $($drive.Name) ($($drive.VolumeLabel)) похож на облачный — выберите флешку"
    }
    if ($drive.DriveType -ne 'Removable') {
        Write-Warning "Диск $($drive.Name) не съёмный. Надёжнее хранить ключ на флешке, которая большую часть времени отключена от ПК."
    }
}

# PKCS#8 PEM, зашифрованный паролем (AES-256, PBKDF2-SHA256, 600 000 итераций)
function Write-EncryptedKey([Security.Cryptography.ECDsa]$Key, [string]$Path) {
    $password = Read-Password 'Пароль для файла ключа (не менее 12 символов)'
    if ($password.Length -lt 12) { throw 'Слишком короткий пароль' }
    if ($password -ne (Read-Password 'Повторите пароль')) { throw 'Пароли не совпадают' }
    $pbe = [Security.Cryptography.PbeParameters]::new('Aes256Cbc', 'SHA256', 600000)
    Set-Content -Encoding ascii -Path $Path -Value $Key.ExportEncryptedPkcs8PrivateKeyPem($password, $pbe)
}

function Read-EncryptedKey([string]$Path) {
    $key = [Security.Cryptography.ECDsa]::Create()
    $key.ImportFromEncryptedPem((Get-Content -Raw $Path), (Read-Password "Пароль файла $Path"))
    return $key
}

if ($Export) {
    Assert-Pwsh '-Export'
    Assert-OfflineLocation $Export
    Write-EncryptedKey (Get-SigningKey) $Export
    Write-Host "Резервная копия: $Export (зашифрована паролем). Храните её офлайн, отдельно от пароля."
}
elseif ($NewRecovery) {
    # Резервный ключ живёт только в зашифрованном файле на внешнем носителе: на этом ПК он не сохраняется.
    # Нужен, если основной ключ утерян или скомпрометирован: релиз, подписанный им, принимают все копии
    Assert-Pwsh '-NewRecovery'
    Assert-OfflineLocation $NewRecovery
    $key = [Security.Cryptography.ECDsa]::Create([Security.Cryptography.ECCurve]::CreateFromFriendlyName('nistP256'))
    Write-EncryptedKey $key $NewRecovery
    Write-Host "Резервный ключ записан: $NewRecovery. Храните носитель офлайн, пароль — отдельно."
    Write-Host "Открытый резервный ключ (для UpdateSignature.cs): $(Get-PublicKeyBase64 $key)"
    $key.Dispose()
}
elseif ($SignWith) {
    # Подпись резервным ключом вручную: pwsh -File installer\signing-key.ps1 -SignWith E:\recovery.pem -File <установщик>.sha256
    Assert-Pwsh '-SignWith'
    if (-not $File) { throw 'Укажите -File <установщик>.sha256' }
    $key = Read-EncryptedKey $SignWith
    Write-Host "Подпись: $(New-FileSignature $key $File)"
    Write-Host "Открытый ключ подписи: $(Get-PublicKeyBase64 $key)"
    $key.Dispose()
}
elseif ($Import) {
    Assert-Pwsh '-Import'
    if (Test-Path $script:SigningKeyPath) { throw "Ключ уже есть: $($script:SigningKeyPath). Удалите его вручную, если действительно хотите заменить." }
    $key = Read-EncryptedKey $Import
    Save-SigningKey $key
    Write-Host "Ключ восстановлен: $($script:SigningKeyPath)"
    Write-Host "Открытый ключ: $(Get-PublicKeyBase64 $key)"
}
elseif ($New) {
    if (Test-Path $script:SigningKeyPath) { throw "Ключ уже есть: $($script:SigningKeyPath). Новый ключ сделает невозможным обновление установленных копий." }
    $key = [Security.Cryptography.ECDsa]::Create([Security.Cryptography.ECCurve]::CreateFromFriendlyName('nistP256'))
    Save-SigningKey $key
    Write-Host "Ключ создан: $($script:SigningKeyPath)"
    Write-Host "Открытый ключ (вставьте в Core/Updates/UpdateSignature.cs): $(Get-PublicKeyBase64 $key)"
    Write-Host 'Сделайте резервную копию: pwsh -File installer\signing-key.ps1 -Export <путь>'
}
else {
    Write-Host "Открытый ключ: $(Get-PublicKeyBase64 (Get-SigningKey))"
}