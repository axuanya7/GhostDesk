using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;

namespace GhostDesk
{
    internal static class Updater
    {
        private const int Tls12Flag = 3072;

        public static bool Download(
            UpdateInfo update,
            Action<int> progress,
            out string zipPath,
            out string error)
        {
            zipPath = "";
            error = "";

            if (update == null || string.IsNullOrWhiteSpace(update.DownloadUrl))
            {
                error = "没有可用的更新下载地址。";
                return false;
            }

            try
            {
                ServicePointManager.SecurityProtocol =
                    (SecurityProtocolType)((int)ServicePointManager.SecurityProtocol | Tls12Flag);

                HttpWebRequest request =
                    (HttpWebRequest)WebRequest.Create(update.DownloadUrl);
                request.Method = "GET";
                request.UserAgent = "GhostDesk/" + AppInfo.Version;
                request.Timeout = 180000;
                request.ReadWriteTimeout = 180000;
                request.AllowAutoRedirect = true;
                request.AutomaticDecompression =
                    DecompressionMethods.GZip | DecompressionMethods.Deflate;

                string target = Path.Combine(
                    Path.GetTempPath(),
                    "GhostDesk-Update-" + Guid.NewGuid().ToString("N") + ".zip");

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream stream = response.GetResponseStream())
                using (FileStream file = new FileStream(
                    target,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None))
                {
                    long total = response.ContentLength;
                    long copied = 0;
                    byte[] buffer = new byte[64 * 1024];
                    int read;

                    ReportProgress(progress, 0);
                    while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        file.Write(buffer, 0, read);
                        copied += read;

                        if (total > 0)
                        {
                            int percent = (int)Math.Min(
                                100L,
                                copied * 100L / total);
                            ReportProgress(progress, percent);
                        }
                    }

                    ReportProgress(progress, 100);
                }

                if (!File.Exists(target) || new FileInfo(target).Length < 4096)
                {
                    try
                    {
                        if (File.Exists(target))
                        {
                            File.Delete(target);
                        }
                    }
                    catch
                    {
                    }

                    error = "下载到的更新包不完整。";
                    return false;
                }

                zipPath = target;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static void ReportProgress(Action<int> progress, int value)
        {
            if (progress == null)
            {
                return;
            }

            try
            {
                progress(value);
            }
            catch
            {
            }
        }

        public static void Launch(string zipPath, int processId)
        {
            if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath))
            {
                throw new InvalidOperationException("更新包不存在。");
            }

            string exePath = System.Reflection.Assembly.GetEntryAssembly().Location;
            string installDirectory = Path.GetDirectoryName(exePath);
            string scriptPath = CreateUpdaterScript();

            string systemDirectory =
                Environment.GetFolderPath(Environment.SpecialFolder.System);
            string powerShell = Path.Combine(
                systemDirectory,
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe");
            if (!File.Exists(powerShell))
            {
                powerShell = "powershell.exe";
            }

            string arguments =
                "-NoProfile -ExecutionPolicy Bypass -File " +
                Quote(scriptPath) +
                " -ZipPath " + Quote(zipPath) +
                " -InstallDir " + Quote(installDirectory) +
                " -ExePath " + Quote(exePath) +
                " -ProcessId " + processId.ToString(CultureInfo.InvariantCulture);

            Process.Start(new ProcessStartInfo
            {
                FileName = powerShell,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = installDirectory
            });
        }

        private static string CreateUpdaterScript()
        {
            string scriptPath = Path.Combine(
                Path.GetTempPath(),
                "GhostDesk-Updater-" + Guid.NewGuid().ToString("N") + ".ps1");

            string script = @"
param(
    [string]$ZipPath,
    [string]$InstallDir,
    [string]$ExePath,
    [int]$ProcessId
)

$ErrorActionPreference = 'Stop'
$log = Join-Path $env:TEMP 'GhostDesk-update.log'

function Write-UpdaterLog {
    param([string]$Message)

    try {
        $line = '[' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss.fff') + '] ' + $Message
        Add-Content -LiteralPath $log -Value $line -Encoding UTF8
    } catch {
    }
}

function Wait-ForProcessExit {
    param(
        [int]$TargetProcessId,
        [int]$TimeoutSeconds
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $process = Get-Process -Id $TargetProcessId -ErrorAction SilentlyContinue
        if ($null -eq $process) {
            return $true
        }

        Start-Sleep -Milliseconds 200
    }

    return $false
}

function Stop-TargetInstallProcesses {
    param(
        [string]$TargetExePath
    )

    $processes = Get-Process -Name 'GhostDesk' -ErrorAction SilentlyContinue
    foreach ($process in $processes) {
        try {
            if ([string]::Equals(
                [string]$process.Path,
                $TargetExePath,
                [System.StringComparison]::OrdinalIgnoreCase)) {
                Stop-Process -Id $process.Id -Force
            }
        } catch {
        }
    }
}

function Move-FileWithRetry {
    param(
        [string]$Source,
        [string]$Destination,
        [int]$TimeoutSeconds
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $lastError = $null
    while ((Get-Date) -lt $deadline) {
        try {
            Move-Item -LiteralPath $Source -Destination $Destination -Force
            return
        } catch {
            $lastError = $_
            Start-Sleep -Milliseconds 250
        }
    }

    if ($null -ne $lastError) {
        throw $lastError
    }

    throw 'Timed out while moving files.'
}

$updateSucceeded = $false
$staging = $null
$backup = $null
$newExe = $null

try {
    Write-UpdaterLog ('Updater started. Current PID=' + $ProcessId + '; target=' + $ExePath)

    if (-not (Test-Path -LiteralPath $ZipPath)) {
        throw 'Update package does not exist.'
    }

    if (-not (Test-Path -LiteralPath $ExePath)) {
        throw 'Installed executable does not exist.'
    }

    $staging = Join-Path $env:TEMP ('GhostDesk-update-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $staging | Out-Null

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::ExtractToDirectory($ZipPath, $staging)

    $stagedExe = Join-Path $staging 'GhostDesk.exe'
    if (-not (Test-Path -LiteralPath $stagedExe)) {
        $foundExe = Get-ChildItem -LiteralPath $staging -Filter 'GhostDesk.exe' -Recurse |
            Select-Object -First 1
        if ($null -ne $foundExe) {
            $stagedExe = $foundExe.FullName
        }
    }

    if (-not (Test-Path -LiteralPath $stagedExe)) {
        throw 'GhostDesk.exe was not found in the update package.'
    }

    if ((Get-Item -LiteralPath $stagedExe).Length -lt 4096) {
        throw 'GhostDesk.exe in the update package is invalid.'
    }

    Write-UpdaterLog 'Update package extracted successfully.'

    if (-not (Wait-ForProcessExit -TargetProcessId $ProcessId -TimeoutSeconds 30)) {
        Write-UpdaterLog 'GhostDesk did not exit in time. Stopping the target process.'
        Stop-Process -Id $ProcessId -Force -ErrorAction SilentlyContinue
        if (-not (Wait-ForProcessExit -TargetProcessId $ProcessId -TimeoutSeconds 10)) {
            throw 'GhostDesk process did not exit.'
        }
    }

    Stop-TargetInstallProcesses -TargetExePath $ExePath
    Start-Sleep -Milliseconds 300

    $backup = Join-Path $InstallDir (
        'GhostDesk.exe.bak-' + (Get-Date -Format 'yyyyMMddHHmmss'))
    $newExe = Join-Path $InstallDir (
        'GhostDesk.exe.new-' + [guid]::NewGuid().ToString('N'))

    Copy-Item -LiteralPath $stagedExe -Destination $newExe -Force
    if ((Get-Item -LiteralPath $newExe).Length -ne (Get-Item -LiteralPath $stagedExe).Length) {
        throw 'Copied GhostDesk.exe is incomplete.'
    }

    Move-FileWithRetry -Source $ExePath -Destination $backup -TimeoutSeconds 30
    Write-UpdaterLog ('Old executable moved to backup: ' + $backup)

    try {
        Move-Item -LiteralPath $newExe -Destination $ExePath -Force
        Write-UpdaterLog ('New executable installed: ' + $ExePath)

        $readme = Join-Path $staging 'README.md'
        if (Test-Path -LiteralPath $readme) {
            try {
                Copy-Item -LiteralPath $readme -Destination (Join-Path $InstallDir 'README.md') -Force
            } catch {
                Write-UpdaterLog ('README update skipped: ' + $_.Exception.Message)
            }
        }

        $updateSucceeded = $true
    } catch {
        try {
            if (Test-Path -LiteralPath $ExePath) {
                Remove-Item -LiteralPath $ExePath -Force
            }
            if ((Test-Path -LiteralPath $backup) -and -not (Test-Path -LiteralPath $ExePath)) {
                Move-Item -LiteralPath $backup -Destination $ExePath -Force
            }
        } catch {
            Write-UpdaterLog ('Rollback failed: ' + $_.Exception.ToString())
        }

        throw
    }
}
catch {
    Write-UpdaterLog ('Update failed: ' + $_.Exception.ToString())
}
finally {
    try {
        if (Test-Path -LiteralPath $newExe) {
            Remove-Item -LiteralPath $newExe -Force
        }
    } catch {
    }

    try {
        if (Test-Path -LiteralPath $staging) {
            Remove-Item -LiteralPath $staging -Recurse -Force
        }
    } catch {
    }

    try {
        if ($updateSucceeded -and (Test-Path -LiteralPath $ZipPath)) {
            Remove-Item -LiteralPath $ZipPath -Force
        }
    } catch {
    }

    if (Test-Path -LiteralPath $ExePath) {
        try {
            Start-Process -FilePath $ExePath -WorkingDirectory $InstallDir
            Write-UpdaterLog ('GhostDesk restarted: ' + $ExePath)
        } catch {
            Write-UpdaterLog ('GhostDesk restart failed: ' + $_.Exception.ToString())
        }
    }

    try {
        Remove-Item -LiteralPath $MyInvocation.MyCommand.Path -Force
    } catch {
    }
}
";

            File.WriteAllText(scriptPath, script, new UTF8Encoding(true));
            return scriptPath;
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}
