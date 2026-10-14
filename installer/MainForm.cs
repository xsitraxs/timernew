using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace TimerSetup;

/// <summary>
/// Графический установщик «Таймера».
///
/// Никаких .ps1-файлов больше не запускается (у Windows PowerShell 5.1 были
/// проблемы с кодировкой UTF-8 без BOM). Установка выполняется прямо здесь:
/// сертификат — через X509Store, пакеты — короткими ASCII-командами
/// Add-AppxPackage. В EXE встроен payload.zip (MSIX приложения, среда
/// выполнения, сертификат), который генерирует GitHub Actions.
/// </summary>
public sealed class MainForm : Form
{
    private static readonly Color Accent = Color.FromArgb(76, 194, 255);
    private static readonly Color Back = Color.FromArgb(31, 31, 31);
    private static readonly Color Panel = Color.FromArgb(24, 24, 24);
    private static readonly Color Gray = Color.FromArgb(160, 160, 160);

    private readonly Label _statusLabel;
    private readonly ComboBox _volumeComboBox;
    private readonly ProgressBar _progressBar;
    private readonly TextBox _logBox;
    private readonly Button _installButton;
    private readonly Button _launchButton;
    private readonly Button _closeButton;

    private bool _installing;
    private bool _installed;

    public MainForm()
    {
        Text = "Установка — Таймер";
        ClientSize = new Size(560, 470);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Back;
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10f);

        var iconLabel = new Label
        {
            Text = "⏱",
            Font = new Font("Segoe UI Symbol", 38f),
            ForeColor = Accent,
            Location = new Point(28, 22),
            Size = new Size(72, 72),
            TextAlign = ContentAlignment.MiddleCenter
        };

        var titleLabel = new Label
        {
            Text = "Таймер",
            Font = new Font("Segoe UI", 21f, FontStyle.Bold),
            Location = new Point(106, 30),
            Size = new Size(420, 40)
        };

        var subtitleLabel = new Label
        {
            Text = "Установка приложения для Windows 10/11",
            ForeColor = Gray,
            Location = new Point(108, 72),
            Size = new Size(420, 24)
        };

        var descriptionLabel = new Label
        {
            Text = "Нажмите «Установить». Установщик сам поставит сертификат, " +
                   "среду выполнения и приложение. Visual Studio не нужна. " +
                   "Можно выбрать диск: точную папку Windows не разрешает — " +
                   "приложения всегда ставятся в «<диск>:\\Program Files\\WindowsApps».",
            ForeColor = Gray,
            Location = new Point(32, 112),
            Size = new Size(496, 58)
        };

        var volumeLabel = new Label
        {
            Text = "Куда установить:",
            Location = new Point(32, 178),
            Size = new Size(132, 26),
            TextAlign = ContentAlignment.MiddleLeft
        };

        _volumeComboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(170, 178),
            Size = new Size(358, 28)
        };
        PopulateVolumeComboBox();

        _statusLabel = new Label
        {
            Text = "Готово к установке.",
            Location = new Point(32, 216),
            Size = new Size(496, 24)
        };

        _progressBar = new ProgressBar
        {
            Location = new Point(32, 246),
            Size = new Size(496, 16),
            Minimum = 0,
            Maximum = 100,
            Style = ProgressBarStyle.Continuous
        };

        _logBox = new TextBox
        {
            Location = new Point(32, 276),
            Size = new Size(496, 96),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Panel,
            ForeColor = Color.Gainsboro,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 8.5f)
        };

        _installButton = new Button
        {
            Text = "Установить",
            Location = new Point(32, 392),
            Size = new Size(180, 42),
            FlatStyle = FlatStyle.Flat,
            BackColor = Accent,
            ForeColor = Color.Black,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold)
        };
        _installButton.FlatAppearance.BorderSize = 0;
        _installButton.Click += InstallButton_Click;

        _launchButton = new Button
        {
            Text = "Запустить Таймер",
            Location = new Point(224, 392),
            Size = new Size(180, 42),
            FlatStyle = FlatStyle.Flat,
            Visible = false
        };
        _launchButton.FlatAppearance.BorderSize = 1;
        _launchButton.Click += LaunchButton_Click;

        _closeButton = new Button
        {
            Text = "Закрыть",
            Location = new Point(424, 392),
            Size = new Size(104, 42),
            FlatStyle = FlatStyle.Flat
        };
        _closeButton.FlatAppearance.BorderSize = 1;
        _closeButton.Click += (_, _) => Close();

        Controls.AddRange(new Control[]
        {
            iconLabel, titleLabel, subtitleLabel, descriptionLabel,
            volumeLabel, _volumeComboBox,
            _statusLabel, _progressBar, _logBox,
            _installButton, _launchButton, _closeButton
        });

        FormClosing += (_, e) =>
        {
            if (_installing)
            {
                e.Cancel = true;
                MessageBox.Show(
                    "Установка ещё идёт. Подождите, пожалуйста, до её завершения.",
                    "Таймер", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        };
    }

    private void PopulateVolumeComboBox()
    {
        _volumeComboBox.Items.Add(new VolumeOption(
            null, "По умолчанию — куда ставит Windows"));

        foreach (var drive in DriveInfo.GetDrives()
                     .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
                     .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
        {
            double freeGb = drive.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0;
            string display = $"{drive.Name} — свободно {freeGb:F0} ГБ";
            _volumeComboBox.Items.Add(new VolumeOption(drive.RootDirectory.FullName, display));
        }

        _volumeComboBox.SelectedIndex = 0;
    }

    private sealed class VolumeOption
    {
        public VolumeOption(string? driveRoot, string display)
        {
            DriveRoot = driveRoot;
            Display = display;
        }

        public string? DriveRoot { get; }
        public string Display { get; }
        public override string ToString() => Display;
    }

    // ------------------------------------------------------------ установка

    private async void InstallButton_Click(object? sender, EventArgs e)
    {
        if (_installing)
            return;

        _installing = true;
        _installButton.Enabled = false;
        _volumeComboBox.Enabled = false;

        string? selectedDrive = (_volumeComboBox.SelectedItem as VolumeOption)?.DriveRoot;
        string systemRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? string.Empty;
        if (selectedDrive is not null &&
            string.Equals(selectedDrive, systemRoot, StringComparison.OrdinalIgnoreCase))
        {
            // Системный диск = поведение по умолчанию, -Volume не нужен.
            selectedDrive = null;
        }
        _launchButton.Visible = false;
        _logBox.Clear();

        var progress = new Progress<InstallProgress>(OnProgress);

        try
        {
            string driveForInstall = selectedDrive ?? string.Empty;
            await Task.Run(() => InstallCore(progress, driveForInstall));

            _installed = true;
            _progressBar.Value = 100;
            _statusLabel.Text = "Готово! «Таймер» установлен.";
            _launchButton.Visible = true;
            AppendLog("Установка успешно завершена.");
            AppendLog("Приложение можно открыть через меню «Пуск» → Таймер.");
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Установка не удалась. Подробности — в журнале ниже.";
            AppendLog("ОШИБКА: " + ex.Message);
            AppendLog("");
            AppendLog("Что можно попробовать:");
            AppendLog("1. Запустите установщик ещё раз и разрешите права администратора.");
            AppendLog("2. Включите режим разработчика: Параметры → Для разработчиков.");
            AppendLog("3. При предупреждении SmartScreen: «Подробнее» → «Выполнить в любом случае».");
            MessageBox.Show(
                "Установка не удалась.\n\n" + ex.Message,
                "Таймер", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _installing = false;
            _installButton.Enabled = true;
            _volumeComboBox.Enabled = true;
            _installButton.Text = _installed ? "Установить заново" : "Повторить установку";
        }
    }

    private static void InstallCore(IProgress<InstallProgress> progress, string targetDriveRoot)
    {
        string tempDir = Path.Combine(
            Path.GetTempPath(), "TimerSetup-" + Guid.NewGuid().ToString("N"));

        try
        {
            progress.Report(new(5, "Распаковываю файлы установщика...", null));
            ExtractPayload(tempDir);
            progress.Report(new(15, "Файлы распакованы.", "Временная папка: " + tempDir));

            progress.Report(new(25, "Устанавливаю сертификат...", null));
            InstallCertificate(tempDir, progress);

            progress.Report(new(45, "Проверяю среду выполнения Windows...", null));
            InstallRuntimeIfPresent(tempDir, progress);

            progress.Report(new(70, "Устанавливаю приложение «Таймер»...", null));
            InstallApplication(tempDir, progress, targetDriveRoot);

            progress.Report(new(100, "Приложение установлено.", "Пакет TimerApp успешно установлен."));
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
                // Временные файлы не критичны: Windows почистит %TEMP% сама.
            }
        }
    }

    private static void ExtractPayload(string tempDir)
    {
        Directory.CreateDirectory(tempDir);

        using Stream? resource = typeof(MainForm).Assembly
            .GetManifestResourceStream("TimerSetup.payload.zip");

        if (resource is null)
        {
            throw new InvalidOperationException(
                "В этот установщик не встроен пакет приложения (payload.zip). " +
                "Скачайте TimerSetup.exe из официального релиза на GitHub.");
        }

        using var archive = new ZipArchive(resource, ZipArchiveMode.Read);
        string root = Path.GetFullPath(tempDir);

        foreach (var entry in archive.Entries)
        {
            string target = Path.GetFullPath(Path.Combine(root, entry.FullName));

            if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(target, root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Небезопасный путь в архиве установщика: " + entry.FullName);
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    private static void InstallCertificate(string tempDir, IProgress<InstallProgress> progress)
    {
        string? cer = Directory.GetFiles(tempDir, "*.cer").FirstOrDefault();
        if (cer is null)
        {
            progress.Report(new(30, "Сертификат не найден — пропускаю.", "Файл .cer отсутствует в пакете."));
            return;
        }

        using var certificate = new X509Certificate2(File.ReadAllBytes(cer));

        AddToStore(certificate, StoreName.TrustedPeople);
        AddToStore(certificate, StoreName.Root);

        progress.Report(new(35, "Сертификат установлен.", "Сертификат: " + certificate.Subject));
    }

    private static void AddToStore(X509Certificate2 certificate, StoreName storeName)
    {
        using var store = new X509Store(storeName, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadWrite);

        bool exists = store.Certificates
            .Find(X509FindType.FindByThumbprint, certificate.Thumbprint, validOnly: false)
            .Count > 0;

        if (!exists)
            store.Add(certificate);
    }

    private static void InstallRuntimeIfPresent(string tempDir, IProgress<InstallProgress> progress)
    {
        var runtimes = Directory.GetFiles(tempDir, "Microsoft.WindowsAppRuntime*.msix");

        foreach (string runtime in runtimes)
        {
            var result = RunPowerShell($"Add-AppxPackage -Path '{EscapeSingleQuotes(runtime)}'");

            progress.Report(result.ExitCode == 0
                ? new(55, "Среда выполнения установлена.", "Установлено: " + Path.GetFileName(runtime))
                : new(55, "Среда выполнения уже есть в системе.", "Пропущено (возможно, уже установлена): " + Path.GetFileName(runtime)));
        }

        if (runtimes.Length == 0)
            progress.Report(new(55, "Среда выполнения не требуется.", "Файл среды выполнения в пакет не входит."));
    }

    private static void InstallApplication(
        string tempDir, IProgress<InstallProgress> progress, string targetDriveRoot)
    {
        string? package = Directory.GetFiles(tempDir)
            .Where(f => f.Contains("TimerApp", StringComparison.OrdinalIgnoreCase))
            .Where(f => f.EndsWith(".msixbundle", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".msix", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".appxbundle", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".appx", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.EndsWith(".msixbundle", StringComparison.OrdinalIgnoreCase) ||
                          f.EndsWith(".appxbundle", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .FirstOrDefault();

        if (package is null)
        {
            throw new InvalidOperationException(
                "В пакете установщика не найден файл приложения TimerApp (.msix).");
        }

        progress.Report(new(75, "Ставлю пакет " + Path.GetFileName(package) + "...", null));

        PowerShellResult result;

        if (string.IsNullOrWhiteSpace(targetDriveRoot))
        {
            result = RunPowerShell(
                $"Add-AppxPackage -Path '{EscapeSingleQuotes(package)}' -ForceApplicationShutdown");
        }
        else
        {
            // MSIX нельзя поставить в произвольную папку, но можно выбрать диск:
            // Windows положит приложение в "<диск>:\\Program Files\\WindowsApps".
            // Если диск ещё не подготовлен как AppxVolume — подготавливаем.
            string drivePath = EscapeSingleQuotes(
                targetDriveRoot.TrimEnd('\\') + "\\");

            progress.Report(new(78,
                "Ставлю приложение на диск " + targetDriveRoot + "...",
                "Целевой диск: " + targetDriveRoot));

            string volumeCommand =
                "$drivePath = '" + drivePath + "'; " +
                "$vol = Get-AppxVolume -Path $drivePath -ErrorAction SilentlyContinue; " +
                "if (-not $vol) { Add-AppxVolume -Path $drivePath | Out-Null; " +
                "$vol = Get-AppxVolume -Path $drivePath -ErrorAction SilentlyContinue }; " +
                "if (-not $vol) { throw 'Failed to prepare the selected drive for Windows apps.' }; " +
                "Add-AppxPackage -Path '" + EscapeSingleQuotes(package) +
                "' -Volume $vol -ForceApplicationShutdown";

            result = RunPowerShell(volumeCommand);
        }

        if (result.ExitCode != 0)
        {
            string details = string.Join("\n",
                new[] { result.StandardOutput, result.StandardError }
                    .Where(s => !string.IsNullOrWhiteSpace(s)));

            throw new InvalidOperationException(
                "Windows не смогла установить пакет приложения.\n" + details.Trim());
        }

        progress.Report(new(90, "Приложение установлено в систему.", "Пакет: " + Path.GetFileName(package)));
    }

    // ---------------------------------------------------------------- запуск

    private void LaunchButton_Click(object? sender, EventArgs e)
    {
        try
        {
            var result = RunPowerShell("(Get-AppxPackage -Name 'TimerApp').PackageFamilyName");
            string familyName = result.StandardOutput.Trim().Split('\n').Last().Trim();

            if (string.IsNullOrWhiteSpace(familyName))
                throw new InvalidOperationException("Не удалось найти установленное приложение.");

            Process.Start(new ProcessStartInfo
            {
                FileName = $"shell:AppsFolder\\{familyName}!App",
                UseShellExecute = true
            });

            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Не удалось запустить приложение автоматически.\n" +
                "Откройте меню «Пуск» и введите «Таймер».\n\n" + ex.Message,
                "Таймер", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    // ------------------------------------------------------------- помощники

    private void OnProgress(InstallProgress item)
    {
        _progressBar.Value = Math.Clamp(item.Percent, 0, 100);
        _statusLabel.Text = item.Status;

        if (!string.IsNullOrWhiteSpace(item.Log))
            AppendLog(item.Log);
    }

    private void AppendLog(string text)
    {
        _logBox.AppendText(text + Environment.NewLine);
    }

    private static string EscapeSingleQuotes(string value) => value.Replace("'", "''");

    private static PowerShellResult RunPowerShell(string command)
    {
        // Команды состоят только из ASCII-текста и путей к файлам: никаких
        // .ps1 с русской кодировкой, поэтому баг Windows PowerShell 5.1
        // с UTF-8 без BOM здесь невозможен в принципе.
        string fullCommand =
            "[Console]::OutputEncoding=[System.Text.Encoding]::UTF8; " + command;

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{fullCommand}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Не удалось запустить PowerShell.");

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        process.WaitForExit();

        return new PowerShellResult(
            process.ExitCode,
            outputTask.GetAwaiter().GetResult(),
            errorTask.GetAwaiter().GetResult());
    }

    private sealed record InstallProgress(int Percent, string Status, string? Log);

    private sealed record PowerShellResult(int ExitCode, string StandardOutput, string StandardError);
}
