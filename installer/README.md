# TimerSetup — графический установщик

Небольшое приложение на C# / WinForms (.NET 8, `installer/TimerSetup.csproj`),
которое упаковывает всё приложение в **один `TimerSetup.exe`**:

- внутри EXE встроен `payload.zip` (пакет `TimerApp*.msix`, среда выполнения
  `Microsoft.WindowsAppRuntime*.msix`, сертификат `TimerApp.cer`);
- окно установщика: заголовок, описание, кнопка «Установить», прогресс-бар,
  журнал и кнопка «Запустить Таймер» после успеха;
- установка выполняется прямо в коде: сертификат — через `X509Store`,
  пакеты — короткими ASCII-командами `Add-AppxPackage`.

  > Никаких `.ps1`-файлов установщик не запускает специально: у Windows
  > PowerShell 5.1 был баг с чтением UTF-8 без BOM (русский текст превращался
  > в «кашу» и ломал синтаксис). В GUI-установщике этой цепочки нет вообще
  > (`install.ps1` дополнительно сохранён с BOM для старого ручного способа).
- манифест требует права администратора, поэтому Windows один раз покажет UAC.

`payload.zip` в репозитории **не хранится** — его генерирует GitHub Actions
(workflow `.github/workflows/build-msix.yml`) из только что собранного MSIX
перед публикацией установщика:

```powershell
dotnet publish installer\TimerSetup.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true -o package
```

Само приложение при этом остаётся MSIX-пакетом: пакетная идентичность нужна
ему для уведомлений Windows и хранилища настроек, поэтому «просто EXE вместо
MSIX» для самого таймера не подходит — EXE здесь именно установщик-обёртка.
