# TimerSetup — однофайловый установщик

Небольшая программа на Go (`main.go`, собирается только под Windows),
которая упаковывает всё приложение в **один `TimerSetup.exe`**:

- внутри EXE встроен `payload.zip` (пакет `TimerApp*.msix`, среда выполнения
  `Microsoft.WindowsAppRuntime*.msix`, сертификат `TimerApp.cer`,
  `install.ps1`, `Install.bat`, `INSTALL.md`);
- при запуске: запрос прав администратора (UAC) → распаковка во временную
  папку → запуск `install.ps1 -NoPause` → ожидание → удаление временных файлов.

`payload.zip` в репозитории **не хранится** — его генерирует GitHub Actions
(workflow `.github/workflows/build-msix.yml`) из только что собранного MSIX
перед командой `go build`.

Локальная сборка установщика (на Windows, при готовом MSIX):

```powershell
# payload\ — папка с TimerApp*.msix, Microsoft.WindowsAppRuntime*.msix,
#            TimerApp.cer, install.ps1, Install.bat, INSTALL.md
Compress-Archive -Path payload\* -DestinationPath installer\payload.zip -Force
cd installer
go build -trimpath -ldflags "-s -w" -o TimerSetup.exe .
```

Само приложение при этом остаётся MSIX-пакетом: пакетная идентичность нужна
ему для уведомлений Windows и хранилища настроек, поэтому «просто EXE вместо
MSIX» для самого таймера не подходит — EXE здесь именно установщик-обёртка.
