using System;
using System.Diagnostics;
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

        public static void Launch(string zipPath)
        {
            if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath))
            {
                throw new InvalidOperationException("更新包不存在。");
            }

            string exePath = System.Reflection.Assembly.GetEntryAssembly().Location;
            string installDirectory = Path.GetDirectoryName(exePath);
            string scriptPath = CreateUpdaterScript(zipPath, installDirectory, exePath);

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
                " -ExePath " + Quote(exePath);

            Process.Start(new ProcessStartInfo
            {
                FileName = powerShell,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = installDirectory
            });
        }

        private static string CreateUpdaterScript(
            string zipPath,
            string installDirectory,
            string exePath)
        {
            string scriptPath = Path.Combine(
                Path.GetTempPath(),
                "GhostDesk-Updater-" + Guid.NewGuid().ToString("N") + ".ps1");

            string script = @"
param(
    [string]$ZipPath,
    [string]$InstallDir,
    [string]$ExePath
)

$ErrorActionPreference = 'Stop'
$log = Join-Path $env:TEMP 'GhostDesk-update.log'
Start-Sleep -Seconds 2

try {
    $processes = Get-Process -Name 'GhostDesk' -ErrorAction SilentlyContinue
    foreach ($process in $processes) {
        try {
            Stop-Process -Id $process.Id -Force
        } catch {
        }
    }

    Start-Sleep -Seconds 1

    $backup = Join-Path $InstallDir ('GhostDesk.exe.bak-' + [DateTime]::UtcNow.ToString('yyyyMMddHHmmss'))
    if (Test-Path -LiteralPath $ExePath) {
        Copy-Item -LiteralPath $ExePath -Destination $backup -Force
    }

    $staging = Join-Path $env:TEMP ('GhostDesk-update-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $staging | Out-Null

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::ExtractToDirectory($ZipPath, $staging)

    Copy-Item -LiteralPath (Join-Path $staging 'GhostDesk.exe') -Destination $ExePath -Force

    $readme = Join-Path $staging 'README.md'
    if (Test-Path -LiteralPath $readme) {
        Copy-Item -LiteralPath $readme -Destination (Join-Path $InstallDir 'README.md') -Force
    }

    Remove-Item -LiteralPath $staging -Recurse -Force

    Start-Sleep -Seconds 1
    Start-Process -FilePath $ExePath
    Start-Sleep -Seconds 1

    if (Test-Path -LiteralPath $ZipPath) {
        Remove-Item -LiteralPath $ZipPath -Force
    }

    Remove-Item -LiteralPath $MyInvocation.MyCommand.Path -Force
}
catch {
    try {
        Add-Content -LiteralPath $log -Value ($_.Exception.ToString())
    } catch {
    }
    exit 1
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
