using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using Microsoft.Win32;

namespace GhostDesk
{
    internal class AppController
    {
        private readonly AppConfig _config;
        private readonly DesktopWindow _desktop;
        private NotifyIcon _tray;
        private Icon _trayIcon;
        private Bitmap _trayBitmap;
        private ToolStripMenuItem _addRegionMenu;
        private ToolStripMenuItem _launchMenu;
        private string _pendingUpdateUrl;
        private bool _updateInProgress;
        private bool _launchInProgress;
        private SettingsWindow _settingsWindow;

        public DesktopWindow Desktop
        {
            get { return _desktop; }
        }

        public AppController()
        {
            _config = ConfigStore.Load();
            ApplyAutoStartFromRegistry();

            _desktop = new DesktopWindow(_config);
            _desktop.ConfigChanged += HandleConfigChanged;
            _desktop.Show();

            CreateTray();
            _desktop.LaunchRequested += LaunchRegionById;
            UpdateTrayForMode();
            if (ConfigStore.IsFirstRun &&
                System.Windows.Application.Current != null)
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(
                    new Action(delegate
                    {
                        Settings_Click(null, EventArgs.Empty);
                    }),
                    System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
            StartUpdateCheck();
        }

        private void CreateTray()
        {
            CreateTrayIcon();

            ContextMenuStrip menu = new ContextMenuStrip();

            ToolStripMenuItem settings = new ToolStripMenuItem("系统设置...");
            settings.Click += Settings_Click;
            menu.Items.Add(settings);

            ToolStripMenuItem checkUpdate = new ToolStripMenuItem("检查更新...");
            checkUpdate.Click += CheckUpdate_Click;
            menu.Items.Add(checkUpdate);

            menu.Items.Add(new ToolStripSeparator());

            _addRegionMenu = new ToolStripMenuItem("新建空白分区");
            _addRegionMenu.Click += AddRegion_Click;
            menu.Items.Add(_addRegionMenu);

            ToolStripMenuItem pause = new ToolStripMenuItem("暂停自动隐藏");
            pause.Checked = _desktop.Paused;
            pause.CheckOnClick = false;
            pause.Click += Pause_Click;
            menu.Items.Add(pause);

            _launchMenu = new ToolStripMenuItem("一键启动");
            menu.Items.Add(_launchMenu);

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem exit = new ToolStripMenuItem("退出");
            exit.Click += Exit_Click;
            menu.Items.Add(exit);

            TrayMenuTheme.Apply(menu);

            _tray = new NotifyIcon();
            _tray.Icon = _trayIcon;
            _tray.Text = "GhostDesk";
            _tray.Visible = true;
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += delegate { _desktop.Paused = !_desktop.Paused; };
            _tray.BalloonTipClicked += UpdateBalloon_Clicked;
        }

        private void CreateTrayIcon()
        {
            try
            {
                string executable = System.Reflection.Assembly.GetEntryAssembly().Location;
                Icon applicationIcon = Icon.ExtractAssociatedIcon(executable);
                if (applicationIcon != null)
                {
                    _trayIcon = applicationIcon;
                    return;
                }
            }
            catch
            {
            }

            _trayBitmap = new Bitmap(32, 32);
            using (Graphics graphics = Graphics.FromImage(_trayBitmap))
            {
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);

                using (SolidBrush background = new SolidBrush(Color.FromArgb(245, 52, 64, 86)))
                {
                    graphics.FillEllipse(background, 1, 1, 30, 30);
                }

                using (Pen border = new Pen(Color.FromArgb(245, 164, 190, 214), 2))
                {
                    graphics.DrawEllipse(border, 1, 1, 30, 30);
                }

                using (SolidBrush tile = new SolidBrush(Color.FromArgb(255, 236, 241, 248)))
                {
                    graphics.FillRectangle(tile, 8, 8, 16, 5);
                    graphics.FillRectangle(tile, 8, 18, 7, 6);
                    graphics.FillRectangle(tile, 19, 18, 5, 6);
                }
            }

            IntPtr hIcon = _trayBitmap.GetHicon();
            _trayIcon = Icon.FromHandle(hIcon);
        }

        private void AddRegion_Click(object sender, EventArgs e)
        {
            double width = 360;
            double height = 180;
            double left = SystemParameters.WorkArea.Left;
            double top = SystemParameters.WorkArea.Top;
            double x = left + ((SystemParameters.WorkArea.Width - width) / 2.0);
            double y = top + ((SystemParameters.WorkArea.Height - height) / 2.0);

            RegionConfig region = new RegionConfig();
            region.Id = Guid.NewGuid().ToString("N");
            region.Name = "新分区";
            region.X = x;
            region.Y = y;
            region.Width = width;
            region.Height = height;
            region.ShowExtensions = AreAllRegionsShowingExtensions() ? "true" : "false";
            region.Items = new System.Collections.Generic.List<DesktopItem>();
            region.LaunchItems = new System.Collections.Generic.List<DesktopItem>();

            _config.Regions.Add(region);
            _desktop.AddRegion(region);
            SaveConfig();
        }

        private bool AreAllRegionsShowingExtensions()
        {
            if (_config.Regions == null || _config.Regions.Count == 0)
            {
                return true;
            }

            foreach (RegionConfig region in _config.Regions)
            {
                if (region != null &&
                    string.Equals(region.ShowExtensions, "false", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        private void Pause_Click(object sender, EventArgs e)
        {
            _desktop.Paused = !_desktop.Paused;
            ToolStripMenuItem item = sender as ToolStripMenuItem;
            if (item != null)
            {
                item.Checked = _desktop.Paused;
            }
        }

        private void StartUpdateCheck()
        {
            if (!UpdateChecker.ShouldCheck(_config))
            {
                return;
            }

            UpdateChecker.CheckInBackground(delegate(UpdateCheckResult result)
            {
                HandleUpdateCheckResult(result, false);
            });
        }

        private void CheckUpdate_Click(object sender, EventArgs e)
        {
            UpdateChecker.CheckInBackground(delegate(UpdateCheckResult result)
            {
                HandleUpdateCheckResult(result, true);
            });
        }

        private void HandleUpdateCheckResult(UpdateCheckResult result, bool manual)
        {
            if (result == null)
            {
                return;
            }

            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(delegate
            {
                _config.LastUpdateCheck = DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
                SaveConfig();

                if (result.Error != null)
                {
                    if (manual)
                    {
                        UpdateNotification.Show(
                            "GhostDesk 检查更新失败",
                            "请稍后重试。\n\n" + result.Error.Message,
                            "",
                            null);
                    }
                    return;
                }

                if (result.Update == null)
                {
                    if (manual)
                    {
                        UpdateNotification.Show(
                            "GhostDesk",
                            "当前已是最新版本 " + AppInfo.Version + "。",
                            "",
                            null);
                    }
                    return;
                }

                if (string.Equals(
                    _config.SkippedVersion,
                    result.Update.Version,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                ShowUpdateNotification(result.Update);
            }));
        }

        private void ShowUpdateNotification(UpdateInfo update)
        {
            if (update == null)
            {
                return;
            }

            _pendingUpdateUrl = update.Url;
            string url = string.IsNullOrWhiteSpace(_pendingUpdateUrl)
                ? AppInfo.ReleasesUrl
                : _pendingUpdateUrl;

            UpdateNotification.ShowUpdateDownload(
                "GhostDesk 更新",
                "发现新版本 " + update.Version,
                "下载完成后会关闭 GhostDesk，安装更新并重新打开。",
                update.ReleaseNotes,
                "立即更新",
                delegate(Action<int> progress, Action<string> completed)
                {
                    BeginApplyUpdate(
                        update,
                        progress,
                        delegate(string error)
                        {
                            if (completed != null)
                            {
                                completed(error);
                            }

                            if (string.IsNullOrEmpty(error))
                            {
                                System.Windows.Application.Current.Dispatcher.BeginInvoke(
                                    new Action(delegate
                                    {
                                        CloseApplication();
                                    }));
                            }
                            else
                            {
                                _updateInProgress = false;
                            }
                        });
                });
        }

        private void BeginApplyUpdate(
            UpdateInfo update,
            Action<int> progress,
            Action<string> completed)
        {
            if (update == null)
            {
                if (completed != null)
                {
                    completed("没有可用的更新。");
                }
                return;
            }

            if (_updateInProgress)
            {
                if (completed != null)
                {
                    completed("已有更新任务正在进行。");
                }
                return;
            }

            _updateInProgress = true;
            SaveConfig();

            ThreadPool.QueueUserWorkItem(delegate
            {
                string zipPath;
                string error;
                if (!Updater.Download(update, progress, out zipPath, out error))
                {
                    if (completed != null)
                    {
                        completed(string.IsNullOrWhiteSpace(error)
                            ? "下载更新失败，请稍后重试。"
                            : error);
                    }
                    return;
                }

                try
                {
                    SaveConfig();
                    Updater.Launch(zipPath, Process.GetCurrentProcess().Id);
                }
                catch (Exception ex)
                {
                    if (completed != null)
                    {
                        completed("无法启动更新程序。\n\n" + ex.Message);
                    }
                    return;
                }

                if (completed != null)
                {
                    completed(null);
                }
            });
        }

        private void UpdateBalloon_Clicked(object sender, EventArgs e)
        {
            string url = string.IsNullOrWhiteSpace(_pendingUpdateUrl)
                ? AppInfo.ReleasesUrl
                : _pendingUpdateUrl;

            try
            {
                Process.Start(url);
            }
            catch
            {
            }
        }

        private void Settings_Click(object sender, EventArgs e)
        {
            SaveConfig();

            if (_settingsWindow != null)
            {
                _settingsWindow.Activate();
                return;
            }

            SettingsWindow window = new SettingsWindow(
                _config,
                _desktop,
                SetAutoStart,
                RestartApplication,
                delegate
                {
                    CheckUpdate_Click(null, null);
                });
            window.Closed += delegate
            {
                _settingsWindow = null;
                UpdateTrayForMode();
            };
            _settingsWindow = window;
            window.Show();
        }

        private void RestartApplication()
        {
            Program.RestartRequested = true;

            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
                _tray = null;
            }

            if (_desktop != null)
            {
                _desktop.Close();
            }

            System.Windows.Application.Current.Shutdown();
        }

        private void Exit_Click(object sender, EventArgs e)
        {
            CloseApplication();
        }

        private void CloseApplication()
        {
            SaveConfig();
            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
                _tray = null;
            }
            if (_desktop != null)
            {
                _desktop.Close();
            }
            System.Windows.Application.Current.Shutdown();
        }

        private void SaveConfig()
        {
            ConfigStore.Save(_config);
        }

        private void UpdateTrayForMode()
        {
            if (_addRegionMenu != null)
            {
                _addRegionMenu.Enabled = true;
            }

            if (_launchMenu != null)
            {
                _launchMenu.DropDownItems.Clear();
                _launchMenu.Enabled = true;

                if (_config.Regions == null || _config.Regions.Count == 0)
                {
                    ToolStripMenuItem empty = new ToolStripMenuItem("暂无分区");
                    empty.Enabled = false;
                    _launchMenu.DropDownItems.Add(empty);
                    return;
                }

                foreach (RegionConfig region in _config.Regions)
                {
                    ToolStripMenuItem item = new ToolStripMenuItem(region.Name ?? "新分区");
                    item.Tag = region.Id;
                    item.Enabled = region.HasValidLaunchItems();
                    item.Click += LaunchRegionMenu_Click;
                    _launchMenu.DropDownItems.Add(item);
                }
            }
        }

        private void LaunchRegionMenu_Click(object sender, EventArgs e)
        {
            ToolStripMenuItem item = sender as ToolStripMenuItem;
            if (item == null)
            {
                return;
            }

            string regionId = item.Tag as string;
            if (!string.IsNullOrEmpty(regionId))
            {
                LaunchRegionById(regionId);
            }
        }

        private void HandleConfigChanged()
        {
            SaveConfig();
            if (_tray != null && _launchMenu != null)
            {
                UpdateTrayForMode();
            }

            if (_settingsWindow != null)
            {
                _settingsWindow.RefreshLaunchPage();
            }
        }

        private void LaunchRegionById(string regionId)
        {
            if (string.IsNullOrEmpty(regionId) || _launchInProgress)
            {
                return;
            }

            RegionConfig region = null;
            if (_config.Regions != null)
            {
                foreach (RegionConfig candidate in _config.Regions)
                {
                    if (candidate != null &&
                        string.Equals(candidate.Id, regionId, StringComparison.Ordinal))
                    {
                        region = candidate;
                        break;
                    }
                }
            }

            if (region == null)
            {
                return;
            }

            List<DesktopItem> items = new List<DesktopItem>();
            if (region.LaunchItems != null)
            {
                foreach (DesktopItem item in region.LaunchItems)
                {
                    if (item != null && !string.IsNullOrEmpty(item.Path))
                    {
                        items.Add(item);
                    }
                }
            }

            if (items.Count == 0)
            {
                ShowLaunchResult(region.Name, 0, 0, true);
                return;
            }

            _launchInProgress = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                int started = 0;
                int skipped = 0;
                for (int i = 0; i < items.Count; i++)
                {
                    if (StartLaunchItem(items[i]))
                    {
                        started++;
                    }
                    else
                    {
                        skipped++;
                    }

                    if (i < items.Count - 1 && _config.LaunchIntervalMs > 0)
                    {
                        Thread.Sleep(_config.LaunchIntervalMs);
                    }
                }

                ShowLaunchResult(region.Name, started, skipped, false);
                _launchInProgress = false;
            });
        }

        private static bool StartLaunchItem(DesktopItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.Path))
            {
                return false;
            }

            try
            {
                if (item.Path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                {
                    Process.Start("explorer.exe", item.Path);
                }
                else
                {
                    Process.Start(CreateLaunchStartInfo(item.Path));
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static ProcessStartInfo CreateLaunchStartInfo(string path)
        {
            string launchPath = path;
            if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                string sourceDirectory = GetLaunchWorkingDirectory(path);
                string existingShortcut =
                    DesktopItemControl.FindExistingShortcutForTarget(path, sourceDirectory);
                if (!string.IsNullOrEmpty(existingShortcut))
                {
                    launchPath = existingShortcut;
                }
            }

            ProcessStartInfo startInfo = new ProcessStartInfo(launchPath);
            startInfo.UseShellExecute = true;

            if (!launchPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                string workingDirectory = GetLaunchWorkingDirectory(path);
                if (!string.IsNullOrEmpty(workingDirectory))
                {
                    startInfo.WorkingDirectory = workingDirectory;
                }
            }

            return startInfo;
        }

        private static string GetLaunchWorkingDirectory(string path)
        {
            try
            {
                string fullPath = System.IO.Path.GetFullPath(path);
                if (System.IO.Directory.Exists(fullPath))
                {
                    return fullPath;
                }

                return System.IO.Path.GetDirectoryName(fullPath);
            }
            catch
            {
                return null;
            }
        }

        private void ShowLaunchResult(string regionName, int started, int skipped, bool empty)
        {
            if (_tray == null)
            {
                return;
            }

            string message;
            if (empty)
            {
                message = "分区「" + regionName + "」还没有配置启动项。";
            }
            else if (skipped == 0)
            {
                message = "分区「" + regionName + "」已启动 " + started + " 个软件。";
            }
            else
            {
                message = "分区「" + regionName + "」已启动 " + started + " 个，跳过 " +
                    skipped + " 个。";
            }

            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(delegate
            {
                _tray.ShowBalloonTip(2200, "GhostDesk 一键启动", message, ToolTipIcon.Info);
            }));
        }

        private void ApplyAutoStartFromRegistry()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key == null)
                    {
                        return;
                    }

                    object existingValue = key.GetValue("GhostDesk");
                    string existingPath = existingValue as string;
                    object legacyValue = key.GetValue("DesktopZen");
                    if (string.IsNullOrWhiteSpace(existingPath) ||
                        existingPath.IndexOf("DesktopZen.exe", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (legacyValue != null || !string.IsNullOrWhiteSpace(existingPath))
                        {
                            key.SetValue("GhostDesk", "\"" + System.Reflection.Assembly.GetEntryAssembly().Location + "\"");
                        }

                        if (legacyValue != null)
                        {
                            key.DeleteValue("DesktopZen", false);
                        }
                    }

                    object value = key.GetValue("GhostDesk");
                    _config.AutoStart = value != null;
                }
            }
            catch
            {
            }
        }

        private void SetAutoStart(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key == null)
                    {
                        return;
                    }

                    if (enabled)
                    {
                        string exe = System.Reflection.Assembly.GetEntryAssembly().Location;
                        key.SetValue("GhostDesk", "\"" + exe + "\"");
                    }
                    else
                    {
                        key.DeleteValue("GhostDesk", false);
                    }
                }

                _config.AutoStart = enabled;
                SaveConfig();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("开机自启设置失败：\n" + ex.Message, "GhostDesk", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    internal static class Program
    {
        internal static bool RestartRequested;

        [STAThread]
        private static void Main()
        {
            bool createdNew;
            using (Mutex mutex = new Mutex(true, "GhostDesk_SingleInstance_20261016", out createdNew))
            {
                if (!createdNew)
                {
                    try
                    {
                        createdNew = mutex.WaitOne(TimeSpan.FromSeconds(5), false);
                    }
                    catch
                    {
                    }

                    if (!createdNew)
                    {
                        try
                        {
                            uint activateMsg = NativeMethods.RegisterWindowMessage(
                                "GhostDesk_Activate_20261016");
                            NativeMethods.PostMessage(
                                new IntPtr(0xFFFF),
                                activateMsg,
                                IntPtr.Zero,
                                IntPtr.Zero);
                        }
                        catch
                        {
                        }
                        return;
                    }
                }

                System.Windows.Application app = new System.Windows.Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                AppController controller = new AppController();
                app.Run(controller.Desktop);
            }

            if (RestartRequested)
            {
                string exe = System.Reflection.Assembly.GetEntryAssembly().Location;
                try
                {
                    Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
                }
                catch
                {
                }
            }
        }
    }
}
