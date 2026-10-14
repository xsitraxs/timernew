# Установка приложения «Таймер» в один клик.
# Запускается двойным щелчком по Install.bat (или вручную из PowerShell).
# Сам запрашивает права администратора, ставит сертификат и MSIX-пакет.

param([switch]$NoPause)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

function Pause-And-Exit([int]$code) {
    if (-not $NoPause) {
        Write-Host ""
        Read-Host "Нажмите Enter, чтобы закрыть окно"
    }
    exit $code
}

try {
    # --- Права администратора (нужны для установки сертификата) ---
    $isAdmin = ([Security.Principal.WindowsPrincipal] `
        [Security.Principal.WindowsIdentity]::GetCurrent()
    ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

    if (-not $isAdmin) {
        Write-Host "Запрашиваю права администратора..."
        Start-Process powershell -Verb RunAs -ArgumentList `
            "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
        exit 0
    }

    Set-Location -Path $PSScriptRoot
    Write-Host "=== Установка приложения «Таймер» ===" -ForegroundColor Cyan
    Write-Host ""

    # --- 1. Сертификат ---
    $certFile = Get-ChildItem -Path $PSScriptRoot -Filter *.cer -File |
        Select-Object -First 1
    if ($certFile) {
        Write-Host "1/3. Устанавливаю сертификат $($certFile.Name)..."
        Import-Certificate -FilePath $certFile.FullName `
            -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
        Import-Certificate -FilePath $certFile.FullName `
            -CertStoreLocation Cert:\LocalMachine\Root | Out-Null
        Write-Host "     Сертификат установлен." -ForegroundColor Green
    } else {
        Write-Host "1/3. Файл сертификата (.cer) не найден рядом — пропускаю." -ForegroundColor Yellow
        Write-Host "     Если Windows не даст установить пакет, положите .cer рядом с этим скриптом." -ForegroundColor Yellow
    }

    # --- 2. Пакет приложения ---
    # Сначала ставим среду выполнения Windows App Runtime, если она лежит рядом
    # (без неё MSIX может не установиться на "чистой" Windows).
    $dependencies = Get-ChildItem -Path $PSScriptRoot -File |
        Where-Object { $_.Name -like 'Microsoft.WindowsAppRuntime*.msix' }
    foreach ($dependency in $dependencies) {
        try {
            Write-Host "     Устанавливаю среду выполнения $($dependency.Name)..."
            Add-AppxPackage -Path $dependency.FullName -ErrorAction Stop
        } catch {
            Write-Host "     Среда выполнения уже установлена или не требуется." -ForegroundColor DarkGray
        }
    }

    # Главный пакет ищем СТРОГО по имени TimerApp*, иначе можно случайно
    # поставить файл среды выполнения вместо самого приложения.
    $package = Get-ChildItem -Path $PSScriptRoot -File |
        Where-Object {
            $_.Name -like 'TimerApp*' -and
            $_.Extension -in '.msixbundle', '.msix', '.appxbundle', '.appx'
        } |
        Sort-Object @{ Expression = { if ($_.Extension -in '.msixbundle', '.appxbundle') { 0 } else { 1 } } }, Length -Descending |
        Select-Object -First 1

    if (-not $package) {
        throw "Пакет приложения TimerApp (.msix) не найден рядом со скриптом. Скачайте все файлы установщика в одну папку."
    }

    Write-Host "2/3. Устанавливаю пакет $($package.Name)..."
    Add-AppxPackage -Path $package.FullName
    Write-Host "     Пакет установлен." -ForegroundColor Green

    # --- 3. Готово ---
    Write-Host "3/3. Готово!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Приложение «Таймер» установлено." -ForegroundColor Cyan
    Write-Host "Откройте меню «Пуск» и введите: Таймер"
    Write-Host ""
    Write-Host "Если Windows заблокирует установку, включите один раз:"
    Write-Host "Параметры -> Конфиденциальность и защита -> Для разработчиков -> Режим разработчика."

    Pause-And-Exit 0
}
catch {
    Write-Host ""
    Write-Host "ОШИБКА УСТАНОВКИ:" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host ""
    Write-Host "Что можно попробовать:"
    Write-Host "  1. Запустите Install.bat правой кнопкой -> «Запуск от имени администратора»."
    Write-Host "  2. Убедитесь, что все файлы (.msix, .cer, Install.bat, install.ps1) лежат в одной папке."
    Write-Host "  3. Включите режим разработчика (Параметры -> Для разработчиков) и повторите."
    Pause-And-Exit 1
}
