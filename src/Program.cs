using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using Microsoft.Win32;

namespace GhostDesk
{
    internal class AppController
    {
        private readonly AppConfig _config;
        private readonly StorageManifest _manifest;
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
        private ScheduledTaskRunner _scheduledTaskRunner;

        /// <summary>当前配置文件结构版本。旧版本升级时靠它触发一次性迁移。</summary>
        internal const int ConfigVersion = 2;

        public DesktopWindow Desktop
        {
            get { return _desktop; }
        }

        public AppController()
        {
            _config = ConfigStore.Load();
            _manifest = new StorageManifest(
                StorageManifest.DefaultPath(ItemStorage.StorageDirectory));
            _manifest.Load();
            // 升级迁移：旧版本把文件搬进 {exeDir}\Storage，这里放回原位置并改写为新语义
            bool legacyMigrated = ItemStorage.MigrateLegacyStorageToInPlace(_config);

            // 启动自愈：桌面上的分区项必须处于「收纳中」，否则旧版本留下的空标记会让图标全露出来
            bool flagsHealed = ItemStorage.RepairManagedFlags(_config);

            if (legacyMigrated ||
                flagsHealed ||
                !_config.ConfigVersion.HasValue ||
                _config.ConfigVersion.Value < ConfigVersion)
            {
                _config.ConfigVersion = ConfigVersion;
                ConfigStore.Save(_config);
            }

            // 启动：始终确保收纳项带隐藏属性；「显示隐藏文件」只切换资源管理器设置，不动文件属性
            ItemStorage.SynchronizeManagedVisibility(_config, false);

            if (_config.ShowHiddenFiles)
            {
                if (!_config.ExplorerHiddenOriginal.HasValue)
                {
                    _config.ExplorerHiddenOriginal = StorageSafety.ReadExplorerHidden();
                }

                StorageSafety.SetExplorerHidden(1);
            }
            ApplyAutoStartFromRegistry();

            _desktop = new DesktopWindow(_config, _manifest);
            _desktop.ConfigChanged += HandleConfigChanged;
            _desktop.Show();

            _scheduledTaskRunner = new ScheduledTaskRunner(_config, RunScheduledTask);
            _scheduledTaskRunner.Start();

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
            CheckStorageHealth();
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

            ToolStripMenuItem scheduledTasks = new ToolStripMenuItem("定时启动");
            scheduledTasks.Click += ScheduledTasks_Click;
            menu.Items.Add(scheduledTasks);

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
                                        // 自动更新后重启：不弹"是否恢复图标"
                                        CloseApplication(false);
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
            OpenSettings(null);
        }

        private void ScheduledTasks_Click(object sender, EventArgs e)
        {
            OpenSettings("schedule");
        }

        private void OpenSettings(string page)
        {
            SaveConfig();

            if (_settingsWindow != null)
            {
                if (string.Equals(page, "schedule", StringComparison.Ordinal))
                {
                    _settingsWindow.ShowSchedulePage();
                }
                else
                {
                    if (_settingsWindow.WindowState == WindowState.Minimized)
                    {
                        _settingsWindow.WindowState = WindowState.Normal;
                    }

                    _settingsWindow.Activate();
                    _settingsWindow.Focus();
                }
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
                },
                _scheduledTaskRunner);
            window.Closed += delegate
            {
                _settingsWindow = null;
                UpdateTrayForMode();
            };
            _settingsWindow = window;
            window.Show();

            if (string.Equals(page, "schedule", StringComparison.Ordinal))
            {
                window.ShowSchedulePage();
            }
        }

        private void RestartApplication()
        {
            Program.RestartRequested = true;
            DisposeScheduledTasks();

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
            CloseApplication(true);
        }

        private void CloseApplication(bool askRestoreIcons)
        {
            // 退出前询问：是否把收纳区图标恢复到桌面（配置保留，下次启动仍会按配置重新收纳）
            if (askRestoreIcons && _config != null)
            {
                int hiddenCount = ItemStorage.CollectHiddenPaths(_config).Count;
                if (hiddenCount > 0)
                {
                    ExitConfirmWindow prompt = new ExitConfirmWindow(hiddenCount, _desktop);
                    prompt.ShowDialog();
                    if (prompt.Choice == ExitConfirmWindow.ChoiceCancel)
                    {
                        return;
                    }

                    if (prompt.Choice == ExitConfirmWindow.ChoiceRestore)
                    {
                        ItemStorage.RestoreHiddenToDesktop(_config);
                    }
                }
            }

            // 退出时把资源管理器的「显示隐藏文件」恢复成开启前的值
            if (_config != null && _config.ShowHiddenFiles)
            {
                int original = 2;
                if (_config.ExplorerHiddenOriginal.HasValue &&
                    _config.ExplorerHiddenOriginal.Value != 0)
                {
                    original = _config.ExplorerHiddenOriginal.Value;
                }

                StorageSafety.SetExplorerHidden(original);
                _config.ShowHiddenFiles = false;
                _config.ExplorerHiddenOriginal = null;
            }

            SaveConfig();
            DisposeScheduledTasks();
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
            if (_tray != null && _launchMenu != null)
            {
                UpdateTrayForMode();
            }

            if (_settingsWindow != null)
            {
                _settingsWindow.RefreshLaunchPage();
                _settingsWindow.RefreshSchedulePage();
            }

            if (_scheduledTaskRunner != null)
            {
                _scheduledTaskRunner.CheckNow();
            }
        }

        private void DisposeScheduledTasks()
        {
            if (_scheduledTaskRunner == null)
            {
                return;
            }

            _scheduledTaskRunner.Dispose();
            _scheduledTaskRunner = null;
        }

        private bool RunScheduledTask(ScheduledTaskConfig task)
        {
            if (task == null)
            {
                return false;
            }

            if (string.Equals(
                    task.TaskType,
                    ScheduledTaskConfig.TypeShutdown,
                    StringComparison.Ordinal))
            {
                try
                {
                    ProcessStartInfo shutdown =
                        new ProcessStartInfo("shutdown.exe", "/s /t 0");
                    shutdown.UseShellExecute = false;
                    shutdown.CreateNoWindow = true;
                    Process.Start(shutdown);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            string target = (task.Target ?? "").Trim();
            if (target.Length == 0)
            {
                return false;
            }

            try
            {
                if (string.Equals(
                        task.TaskType,
                        ScheduledTaskConfig.TypeUrl,
                        StringComparison.Ordinal))
                {
                    ProcessStartInfo browser = new ProcessStartInfo(target);
                    browser.UseShellExecute = true;
                    Process.Start(browser);
                }
                else
                {
                    Process.Start(CreateLaunchStartInfo(target));
                }

                return true;
            }
            catch
            {
                return false;
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
            if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                ProcessStartInfo shortcutStartInfo =
                    CreateShortcutStartInfo(path);
                if (shortcutStartInfo != null)
                {
                    return shortcutStartInfo;
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

        private static ProcessStartInfo CreateShortcutStartInfo(
            string shortcutPath)
        {
            try
            {
                string target =
                    DesktopItemControl.ReadWindowsShortcutProperty(
                        shortcutPath,
                        "TargetPath") as string;
                if (string.IsNullOrWhiteSpace(target))
                {
                    return null;
                }

                target = Environment.ExpandEnvironmentVariables(
                    target.Trim().Trim('"'));
                if (!System.IO.File.Exists(target) ||
                    !target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                string arguments =
                    DesktopItemControl.ReadWindowsShortcutProperty(
                        shortcutPath,
                        "Arguments") as string;
                string workingDirectory =
                    DesktopItemControl.ReadWindowsShortcutProperty(
                        shortcutPath,
                        "WorkingDirectory") as string;

                ProcessStartInfo startInfo = new ProcessStartInfo(target);
                startInfo.Arguments = arguments ?? "";
                startInfo.UseShellExecute = false;
                if (string.IsNullOrWhiteSpace(workingDirectory) ||
                    !System.IO.Directory.Exists(workingDirectory))
                {
                    workingDirectory = GetLaunchWorkingDirectory(target);
                }

                if (!string.IsNullOrWhiteSpace(workingDirectory))
                {
                    startInfo.WorkingDirectory = workingDirectory;
                }

                return startInfo;
            }
            catch
            {
                return null;
            }
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

        private void CheckStorageHealth()
        {
            // 新语义：收纳不再创建 Storage 目录。
            // 只有历史版本留下的 Storage 才做健康检查与孤儿扫描。
            if (!Directory.Exists(ItemStorage.StorageDirectory))
            {
                return;
            }

            string reason;
            if (!StorageSafety.ValidateStorageDirectory(
                    ItemStorage.StorageDirectory,
                    out reason))
            {
                ShowStorageWarning(
                    "历史收纳目录不可用：" + reason);
            }

            HashSet<string> known =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (RegionConfig region in _config.Regions)
            {
                if (region == null)
                {
                    continue;
                }

                CollectItemPaths(region.Items, known);
                CollectItemPaths(region.LaunchItems, known);
            }

            List<StorageManifest.Entry> orphans =
                _manifest.FindOrphans(
                    ItemStorage.StorageDirectory,
                    known);
            if (orphans.Count > 0)
            {
                ShowStorageWarning(
                    string.Format(
                        "收纳目录中有 {0} 个文件未被任何分区引用（可能是配置丢失）。可在设置中恢复或清理。",
                        orphans.Count));
            }
        }

        private static void CollectItemPaths(
            List<DesktopItem> items,
            HashSet<string> paths)
        {
            if (items == null)
            {
                return;
            }

            foreach (DesktopItem item in items)
            {
                if (item != null &&
                    !string.IsNullOrWhiteSpace(item.Path))
                {
                    paths.Add(item.Path);
                }
            }
        }

        private void ShowStorageWarning(string message)
        {
            if (_tray != null)
            {
                _tray.ShowBalloonTip(
                    5000,
                    "GhostDesk 收纳安全提示",
                    message,
                    ToolTipIcon.Warning);
                return;
            }

            System.Windows.MessageBox.Show(
                message,
                "GhostDesk",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
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
