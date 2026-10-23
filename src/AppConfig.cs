using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace GhostDesk
{
    [DataContract]
    public class DesktopItem
    {
        [DataMember]
        public string Type { get; set; }

        [DataMember]
        public string Path { get; set; }

        [DataMember]
        public string OriginalPath { get; set; }

        [DataMember]
        public string Name { get; set; }

        [DataMember]
        public string Aliases { get; set; }

        [DataMember]
        public string IconPath { get; set; }

        [DataMember]
        public bool ExternalReference { get; set; }

        [DataMember]
        public bool HiddenByGhostDesk { get; set; }

        [DataMember]
        public bool? HiddenBeforeGhostDesk { get; set; }
    }

    [DataContract]
    public class ScheduledTaskConfig
    {
        public const string TypeApplication = "application";
        public const string TypeFile = "file";
        public const string TypeUrl = "url";
        public const string TypeShutdown = "shutdown";

        public const string ModeOnce = "once";
        public const string ModeDaily = "daily";
        public const string ModeWeekly = "weekly";
        public const string ModeInterval = "interval";

        [DataMember]
        public string Id { get; set; }

        [DataMember]
        public string Name { get; set; }

        [DataMember]
        public string TaskType { get; set; }

        [DataMember]
        public string Target { get; set; }

        [DataMember]
        public string ScheduleMode { get; set; }

        [DataMember]
        public string Time { get; set; }

        [DataMember]
        public int DayOfWeek { get; set; }

        [DataMember]
        public int IntervalMinutes { get; set; }

        [DataMember]
        public int CountdownSeconds { get; set; }

        [DataMember]
        public bool Enabled { get; set; }

        [DataMember]
        public string CreatedUtc { get; set; }

        [DataMember]
        public string FirstRunUtc { get; set; }

        [DataMember]
        public string LastRunUtc { get; set; }
    }

    [DataContract]
    public class RegionConfig
    {
        public const string DockNone = "";
        public const string DockTop = "top";
        public const string DockBottom = "bottom";
        public const string DockLeft = "left";
        public const string DockRight = "right";

        /// <summary>贴边方向由程序按松手位置自动判断。</summary>
        public const string DockAnchorAuto = "auto";

        /// <summary>该分区永不自动贴边，可以自由摆放。</summary>
        public const string DockAnchorFree = "free";

        [DataMember]
        public string Id { get; set; }

        [DataMember]
        public string Name { get; set; }

        [DataMember]
        public double X { get; set; }

        [DataMember]
        public double Y { get; set; }

        [DataMember]
        public double Width { get; set; }

        [DataMember]
        public double Height { get; set; }

        [DataMember]
        public string DockEdge { get; set; }

        /// <summary>是否允许自动贴边：auto（贴）/ free（自由摆放，不贴边）。</summary>
        [DataMember]
        public string DockAnchor { get; set; }

        /// <summary>标题朝向：空=跟随贴边方向，其余 top/bottom/left/right 只改标题栏方向，不改位置。</summary>
        [DataMember]
        public string TitleSide { get; set; }

        /// <summary>材质预设覆盖：空=跟随全局，glass/frosted/solid 见 AppConfig.Material* 常量。</summary>
        [DataMember]
        public string MaterialPreset { get; set; }

        /// <summary>背景不透明度覆盖（0~100），空=跟随全局。</summary>
        [DataMember]
        public int? MaterialOpacity { get; set; }

        /// <summary>底色深浅覆盖（0~100，越小越亮），空=跟随全局。</summary>
        [DataMember]
        public int? MaterialTint { get; set; }

        /// <summary>描边强度覆盖（0~100），空=跟随全局。</summary>
        [DataMember]
        public int? MaterialBorder { get; set; }

        /// <summary>阴影强度覆盖（0~100），空=跟随全局。</summary>
        [DataMember]
        public int? MaterialShadow { get; set; }

        [DataMember]
        public int DockOrder { get; set; }

        [DataMember]
        public string ShowExtensions { get; set; }

        [DataMember]
        public List<DesktopItem> Items { get; set; }

        [DataMember]
        public List<DesktopItem> LaunchItems { get; set; }

        public int GetValidLaunchItemCount()
        {
            if (LaunchItems == null)
            {
                return 0;
            }

            int count = 0;
            foreach (DesktopItem item in LaunchItems)
            {
                if (item != null && !string.IsNullOrWhiteSpace(item.Path))
                {
                    count++;
                }
            }

            return count;
        }

        public bool HasValidLaunchItems()
        {
            return GetValidLaunchItemCount() > 0;
        }
    }

    [DataContract]
    public class AppConfig
    {
        public const string ModeRegions = "regions";
        public const string CollapseHoverButton = "button";
        public const string CollapseHoverRow = "row";

        [DataMember]
        public double IdleSeconds { get; set; }

        [DataMember]
        public double FadeSeconds { get; set; }

        [DataMember]
        public int LaunchIntervalMs { get; set; }

        [DataMember]
        public List<ScheduledTaskConfig> ScheduledTasks { get; set; }

        [DataMember]
        public bool AutoStart { get; set; }

        [DataMember]
        public bool ShowHiddenFiles { get; set; }

        /// <summary>开启「显示隐藏文件」前，注册表 Explorer\Advanced\Hidden 的原值（1=显示,2=不显示）。</summary>
        [DataMember]
        public int? ExplorerHiddenOriginal { get; set; }

        /// <summary>配置文件结构版本，用于识别旧版本升级并执行迁移。</summary>
        [DataMember]
        public int? ConfigVersion { get; set; }

        [DataMember]
        public bool PauseInFullscreen { get; set; }

        [DataMember]
        public double BackupIntervalHours { get; set; }

        [DataMember]
        public int BackupMaxCount { get; set; }

        [DataMember]
        public bool? CheckForUpdates { get; set; }

        [DataMember]
        public string LastUpdateCheck { get; set; }

        [DataMember]
        public string SkippedVersion { get; set; }

        [DataMember]
        public bool? EnableSnap { get; set; }

        /// <summary>拖到屏幕边缘时是否自动贴边（关闭后分区可以自由摆放）。</summary>
        [DataMember]
        public bool? EnableEdgeDock { get; set; }

        /// <summary>拖动/缩放分区时是否显示对齐参考线。</summary>
        [DataMember]
        public bool? EnableGuides { get; set; }

        /// <summary>全局材质预设：glass（高透）/ frosted（磨砂）/ solid（纯色块）。</summary>
        [DataMember]
        public string MaterialPreset { get; set; }

        /// <summary>全局背景不透明度（0~100）。</summary>
        [DataMember]
        public int? MaterialOpacity { get; set; }

        /// <summary>全局底色深浅（0~100，越小越亮）。</summary>
        [DataMember]
        public int? MaterialTint { get; set; }

        /// <summary>全局描边强度（0~100）。</summary>
        [DataMember]
        public int? MaterialBorder { get; set; }

        /// <summary>全局阴影强度（0~100）。</summary>
        [DataMember]
        public int? MaterialShadow { get; set; }

        public const string MaterialGlass = "glass";
        public const string MaterialFrosted = "frosted";
        public const string MaterialSolid = "solid";

        public const int DefaultMaterialOpacity = 25;
        public const int DefaultMaterialTint = 30;
        public const int DefaultMaterialBorder = 60;
        public const int DefaultMaterialShadow = 32;

        /// <summary>贴边判定的边缘带宽度（像素），越大越容易贴边。</summary>
        [DataMember]
        public int? EdgeDockBand { get; set; }

        /// <summary>顶部安全边距（像素），用于让开 Rainmeter 时钟条一类的桌面挂件。</summary>
        [DataMember]
        public int? SafeInsetTop { get; set; }

        /// <summary>底部安全边距（像素），用于让开任务栏。</summary>
        [DataMember]
        public int? SafeInsetBottom { get; set; }

        /// <summary>左侧安全边距（像素）。</summary>
        [DataMember]
        public int? SafeInsetLeft { get; set; }

        /// <summary>右侧安全边距（像素）。</summary>
        [DataMember]
        public int? SafeInsetRight { get; set; }

        /// <summary>默认的贴边判定带宽，与旧版本行为一致（2 像素）。</summary>
        public const int DefaultEdgeDockBand = 2;

        public bool IsEdgeDockEnabled()
        {
            return EnableEdgeDock != false;
        }

        public double GetEdgeDockBand()
        {
            if (!EdgeDockBand.HasValue)
            {
                return DefaultEdgeDockBand;
            }

            return Math.Max(0, Math.Min(240, EdgeDockBand.Value));
        }

        /// <summary>取某一侧的安全边距，edge 用 RegionConfig.Dock* 常量。</summary>
        public double GetSafeInset(string edge)
        {
            int? value = null;
            if (string.Equals(edge, RegionConfig.DockTop, StringComparison.Ordinal))
            {
                value = SafeInsetTop;
            }
            else if (string.Equals(edge, RegionConfig.DockBottom, StringComparison.Ordinal))
            {
                value = SafeInsetBottom;
            }
            else if (string.Equals(edge, RegionConfig.DockLeft, StringComparison.Ordinal))
            {
                value = SafeInsetLeft;
            }
            else if (string.Equals(edge, RegionConfig.DockRight, StringComparison.Ordinal))
            {
                value = SafeInsetRight;
            }

            return value.HasValue && value.Value > 0 ? value.Value : 0.0;
        }

        [DataMember]
        public bool? EnableAutoReplace { get; set; }

        [DataMember]
        public string CollapseHoverTrigger { get; set; }

        [DataMember]
        public string Mode { get; set; }

        [DataMember]
        public List<RegionConfig> Regions { get; set; }

        public bool IsRegionsMode
        {
            get { return true; }
        }
    }

    public static class ConfigStore
    {
        private static readonly string DataDirectory = ResolveDataDirectory();
        private static readonly object AtomicWriteLock = new object();

        public static bool IsFirstRun { get; private set; }

        public static Exception LastSaveError { get; private set; }

        public static string ConfigPath
        {
            get
            {
                return Path.Combine(DataDirectory, "config.json");
            }
        }

        private static string RoamingConfigPath
        {
            get
            {
                string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(root, "GhostDesk", "config.json");
            }
        }

        private static string LegacyConfigPath
        {
            get
            {
                string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(root, "DesktopZen", "config.json");
            }
        }

        private static string LegacyPortableConfigPath
        {
            get
            {
                return Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "GhostDeskData",
                    "config.json");
            }
        }

        private static string ResolveDataDirectory()
        {
            string overrideDirectory =
                Environment.GetEnvironmentVariable("GHOSTDESK_DATA_DIR");
            if (!string.IsNullOrWhiteSpace(overrideDirectory))
            {
                try
                {
                    return Path.GetFullPath(overrideDirectory.Trim());
                }
                catch
                {
                }
            }

            string roaming = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "GhostDesk");
            return roaming;
        }

        private static readonly object BackupLock = new object();
        private static DateTime _lastBackupUtc = DateTime.MinValue;
        private static string _lastBackupContent = null;

        public static string BackupDirectory
        {
            get
            {
                return Path.Combine(
                    Path.GetDirectoryName(ConfigPath),
                    "backups");
            }
        }

        public static AppConfig Load()
        {
            AppConfig config = null;

            IsFirstRun =
                !File.Exists(ConfigPath) &&
                string.IsNullOrWhiteSpace(FindBestLegacyConfig());

            MigrateExistingConfig();

            if (!File.Exists(ConfigPath) && File.Exists(LegacyConfigPath))
            {
                try
                {
                    string directory = Path.GetDirectoryName(ConfigPath);
                    if (!Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    File.Copy(LegacyConfigPath, ConfigPath, false);
                }
                catch
                {
                }
            }

            if (File.Exists(ConfigPath))
            {
                try
                {
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(AppConfig));
                    using (FileStream stream = File.OpenRead(ConfigPath))
                    {
                        config = serializer.ReadObject(stream) as AppConfig;
                    }
                }
                catch
                {
                    TryBackupBrokenConfig();
                    config = null;
                }
            }

            if (config == null)
            {
                config = CreateDefault();
                Save(config);
            }

            InitializeAutomaticBackupState();
            bool normalized = Normalize(config);
            if (normalized || RemoveLegacyDefaultItems(config))
            {
                Save(config);
            }

            EnsureInitialBackup();
            return config;
        }

        private static void InitializeAutomaticBackupState()
        {
            lock (BackupLock)
            {
                _lastBackupUtc = DateTime.MinValue;
                _lastBackupContent = null;

                try
                {
                    if (!Directory.Exists(BackupDirectory))
                    {
                        return;
                    }

                    string[] backups = Directory.GetFiles(
                        BackupDirectory,
                        "config_*.json");
                    if (backups.Length == 0)
                    {
                        return;
                    }

                    Array.Sort(backups, StringComparer.OrdinalIgnoreCase);
                    string latest = backups[backups.Length - 1];
                    _lastBackupUtc = File.GetLastWriteTimeUtc(latest);
                    _lastBackupContent = File.ReadAllText(latest);
                }
                catch
                {
                }
            }
        }

        public static bool Save(AppConfig config)
        {
            LastSaveError = null;
            try
            {
                Normalize(config);
                string dir = Path.GetDirectoryName(ConfigPath);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine("  \"IdleSeconds\": " + Inv(config.IdleSeconds) + ",");
                sb.AppendLine("  \"FadeSeconds\": " + Inv(config.FadeSeconds) + ",");
                sb.AppendLine("  \"LaunchIntervalMs\": " + config.LaunchIntervalMs.ToString(CultureInfo.InvariantCulture) + ",");
                sb.AppendLine("  \"ScheduledTasks\": [");
                for (int i = 0; i < config.ScheduledTasks.Count; i++)
                {
                    ScheduledTaskConfig task = config.ScheduledTasks[i];
                    sb.AppendLine("    {");
                    sb.AppendLine("      \"Id\": \"" + Escape(task.Id) + "\",");
                    sb.AppendLine("      \"Name\": \"" + Escape(task.Name) + "\",");
                    sb.AppendLine("      \"TaskType\": \"" + Escape(task.TaskType) + "\",");
                    sb.AppendLine("      \"Target\": \"" + Escape(task.Target) + "\",");
                    sb.AppendLine("      \"ScheduleMode\": \"" + Escape(task.ScheduleMode) + "\",");
                    sb.AppendLine("      \"Time\": \"" + Escape(task.Time) + "\",");
                    sb.AppendLine("      \"DayOfWeek\": " + task.DayOfWeek.ToString(CultureInfo.InvariantCulture) + ",");
                    sb.AppendLine("      \"IntervalMinutes\": " + task.IntervalMinutes.ToString(CultureInfo.InvariantCulture) + ",");
                    sb.AppendLine("      \"CountdownSeconds\": " + task.CountdownSeconds.ToString(CultureInfo.InvariantCulture) + ",");
                    sb.AppendLine("      \"Enabled\": " + (task.Enabled ? "true" : "false") + ",");
                    sb.AppendLine("      \"CreatedUtc\": \"" + Escape(task.CreatedUtc) + "\",");
                    sb.AppendLine("      \"FirstRunUtc\": \"" + Escape(task.FirstRunUtc) + "\",");
                    sb.AppendLine("      \"LastRunUtc\": \"" + Escape(task.LastRunUtc) + "\"");
                    if (i < config.ScheduledTasks.Count - 1)
                    {
                        sb.AppendLine("    },");
                    }
                    else
                    {
                        sb.AppendLine("    }");
                    }
                }
                sb.AppendLine("  ],");
                sb.AppendLine("  \"AutoStart\": " + (config.AutoStart ? "true" : "false") + ",");
                sb.AppendLine("  \"ShowHiddenFiles\": " + (config.ShowHiddenFiles ? "true" : "false") + ",");
                sb.AppendLine("  \"ExplorerHiddenOriginal\": " + (config.ExplorerHiddenOriginal.HasValue
                    ? config.ExplorerHiddenOriginal.Value.ToString(CultureInfo.InvariantCulture)
                    : "null") + ",");
                sb.AppendLine("  \"ConfigVersion\": " + (config.ConfigVersion.HasValue
                    ? config.ConfigVersion.Value.ToString(CultureInfo.InvariantCulture)
                    : "0") + ",");
                sb.AppendLine("  \"PauseInFullscreen\": " + (config.PauseInFullscreen ? "true" : "false") + ",");
                sb.AppendLine("  \"BackupIntervalHours\": " + Inv(config.BackupIntervalHours) + ",");
                sb.AppendLine("  \"BackupMaxCount\": " + config.BackupMaxCount.ToString(CultureInfo.InvariantCulture) + ",");
                sb.AppendLine("  \"CheckForUpdates\": " + (config.CheckForUpdates != false ? "true" : "false") + ",");
                sb.AppendLine("  \"LastUpdateCheck\": \"" + Escape(config.LastUpdateCheck) + "\",");
                sb.AppendLine("  \"SkippedVersion\": \"" + Escape(config.SkippedVersion) + "\",");
                sb.AppendLine("  \"EnableSnap\": " + (config.EnableSnap == true ? "true" : "false") + ",");
                sb.AppendLine("  \"EnableEdgeDock\": " + (config.EnableEdgeDock != false ? "true" : "false") + ",");
                sb.AppendLine("  \"EnableGuides\": " + (config.EnableGuides != false ? "true" : "false") + ",");
                sb.AppendLine("  \"MaterialPreset\": \"" + Escape(config.MaterialPreset) + "\",");
                sb.AppendLine("  \"MaterialOpacity\": " + FormatInt(config.MaterialOpacity, AppConfig.DefaultMaterialOpacity) + ",");
                sb.AppendLine("  \"MaterialTint\": " + FormatInt(config.MaterialTint, AppConfig.DefaultMaterialTint) + ",");
                sb.AppendLine("  \"MaterialBorder\": " + FormatInt(config.MaterialBorder, AppConfig.DefaultMaterialBorder) + ",");
                sb.AppendLine("  \"MaterialShadow\": " + FormatInt(config.MaterialShadow, AppConfig.DefaultMaterialShadow) + ",");
                sb.AppendLine("  \"EdgeDockBand\": " + config.GetEdgeDockBand().ToString("0", CultureInfo.InvariantCulture) + ",");
                sb.AppendLine("  \"SafeInsetTop\": " + FormatInset(config.SafeInsetTop) + ",");
                sb.AppendLine("  \"SafeInsetBottom\": " + FormatInset(config.SafeInsetBottom) + ",");
                sb.AppendLine("  \"SafeInsetLeft\": " + FormatInset(config.SafeInsetLeft) + ",");
                sb.AppendLine("  \"SafeInsetRight\": " + FormatInset(config.SafeInsetRight) + ",");
                sb.AppendLine("  \"EnableAutoReplace\": " + (config.EnableAutoReplace == true ? "true" : "false") + ",");
                sb.AppendLine("  \"CollapseHoverTrigger\": \"" + Escape(config.CollapseHoverTrigger) + "\",");
                sb.AppendLine("  \"Mode\": \"" + Escape(config.Mode) + "\",");
                sb.AppendLine("  \"Regions\": [");

                for (int i = 0; i < config.Regions.Count; i++)
                {
                    RegionConfig region = config.Regions[i];
                    sb.AppendLine("    {");
                    sb.AppendLine("      \"Id\": \"" + Escape(region.Id) + "\",");
                    sb.AppendLine("      \"Name\": \"" + Escape(region.Name) + "\",");
                    sb.AppendLine("      \"X\": " + Inv(region.X) + ",");
                    sb.AppendLine("      \"Y\": " + Inv(region.Y) + ",");
                    sb.AppendLine("      \"Width\": " + Inv(region.Width) + ",");
                    sb.AppendLine("      \"Height\": " + Inv(region.Height) + ",");
                    sb.AppendLine("      \"DockEdge\": \"" + Escape(region.DockEdge) + "\",");
                    sb.AppendLine("      \"DockAnchor\": \"" + Escape(region.DockAnchor) + "\",");
                    sb.AppendLine("      \"TitleSide\": \"" + Escape(region.TitleSide) + "\",");
                    sb.AppendLine("      \"MaterialPreset\": \"" + Escape(region.MaterialPreset) + "\",");
                    sb.AppendLine("      \"MaterialOpacity\": " + FormatNullableInt(region.MaterialOpacity) + ",");
                    sb.AppendLine("      \"MaterialTint\": " + FormatNullableInt(region.MaterialTint) + ",");
                    sb.AppendLine("      \"MaterialBorder\": " + FormatNullableInt(region.MaterialBorder) + ",");
                    sb.AppendLine("      \"MaterialShadow\": " + FormatNullableInt(region.MaterialShadow) + ",");
                    sb.AppendLine("      \"DockOrder\": " + region.DockOrder.ToString(CultureInfo.InvariantCulture) + ",");
                    sb.AppendLine("      \"ShowExtensions\": \"" + Escape(region.ShowExtensions) + "\",");
                    sb.AppendLine("      \"Items\": [");

                    for (int j = 0; j < region.Items.Count; j++)
                    {
                        DesktopItem item = region.Items[j];
                        sb.Append("        { \"Type\": \"" + Escape(item.Type) + "\", ");
                        sb.Append("\"Path\": \"" + Escape(item.Path) + "\", ");
                        sb.Append("\"OriginalPath\": \"" + Escape(item.OriginalPath) + "\", ");
                        sb.Append("\"Name\": \"" + Escape(item.Name) + "\", ");
                        sb.Append("\"Aliases\": \"" + Escape(item.Aliases) + "\", ");
                        sb.Append("\"IconPath\": \"" + Escape(item.IconPath) + "\", ");
                        sb.Append("\"ExternalReference\": " + (item.ExternalReference ? "true" : "false") + ", ");
                        sb.Append("\"HiddenByGhostDesk\": " + (item.HiddenByGhostDesk ? "true" : "false") + ", ");
                        sb.Append("\"HiddenBeforeGhostDesk\": " + FormatNullableBool(item.HiddenBeforeGhostDesk) + " }");
                        if (j < region.Items.Count - 1)
                        {
                            sb.Append(",");
                        }
                        sb.AppendLine();
                    }

                    sb.AppendLine("      ],");
                    sb.AppendLine("      \"LaunchItems\": [");

                    for (int j = 0; j < region.LaunchItems.Count; j++)
                    {
                        DesktopItem item = region.LaunchItems[j];
                        sb.Append("        { \"Type\": \"" + Escape(item.Type) + "\", ");
                        sb.Append("\"Path\": \"" + Escape(item.Path) + "\", ");
                        sb.Append("\"OriginalPath\": \"" + Escape(item.OriginalPath) + "\", ");
                        sb.Append("\"Name\": \"" + Escape(item.Name) + "\", ");
                        sb.Append("\"Aliases\": \"" + Escape(item.Aliases) + "\", ");
                        sb.Append("\"IconPath\": \"" + Escape(item.IconPath) + "\", ");
                        sb.Append("\"ExternalReference\": " + (item.ExternalReference ? "true" : "false") + ", ");
                        sb.Append("\"HiddenByGhostDesk\": " + (item.HiddenByGhostDesk ? "true" : "false") + ", ");
                        sb.Append("\"HiddenBeforeGhostDesk\": " + FormatNullableBool(item.HiddenBeforeGhostDesk) + " }");
                        if (j < region.LaunchItems.Count - 1)
                        {
                            sb.Append(",");
                        }
                        sb.AppendLine();
                    }

                    sb.AppendLine("      ]");
                    if (i < config.Regions.Count - 1)
                    {
                        sb.AppendLine("    },");
                    }
                    else
                    {
                        sb.AppendLine("    }");
                    }
                }
                sb.AppendLine("  ]");
                sb.AppendLine("}");

                string content = sb.ToString();
                CreateAutomaticBackup(config, content);
                AtomicWrite(ConfigPath, content);
                return true;
            }
            catch (Exception ex)
            {
                LastSaveError = ex;
                return false;
            }
        }

        private static void AtomicWrite(string path, string content)
        {
            lock (AtomicWriteLock)
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) &&
                    !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (File.Exists(path))
                {
                    try
                    {
                        File.Copy(path, path + ".bak", true);
                    }
                    catch
                    {
                    }
                }

                string tempPath = path + ".tmp." +
                    Guid.NewGuid().ToString("N").Substring(0, 8);
                bool replaced = false;
                Exception writeError = null;
                try
                {
                    File.WriteAllText(tempPath, content, new UTF8Encoding(false));
                    if (File.Exists(path))
                    {
                        replaced = NativeMethods.MoveFileEx(
                            tempPath,
                            path,
                            NativeMethods.MOVEFILE_REPLACE_EXISTING |
                            NativeMethods.MOVEFILE_WRITE_THROUGH);
                    }
                    else
                    {
                        File.Move(tempPath, path);
                        replaced = true;
                    }
                }
                catch (Exception ex)
                {
                    writeError = ex;
                    replaced = false;
                }

                if (!replaced)
                {
                    try
                    {
                        File.WriteAllText(path, content, new UTF8Encoding(false));
                        replaced = true;
                    }
                    catch (Exception fallbackError)
                    {
                        if (File.Exists(tempPath))
                        {
                            try
                            {
                                File.Delete(tempPath);
                            }
                            catch
                            {
                            }
                        }

                        AggregateException causes = writeError == null
                            ? new AggregateException(fallbackError)
                            : new AggregateException(writeError, fallbackError);
                        throw new IOException(
                            "配置写入失败：" + path,
                            causes);
                    }
                }

                if (File.Exists(tempPath))
                {
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static void CreateAutomaticBackup(AppConfig config, string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                return;
            }

            lock (BackupLock)
            {
                if (_lastBackupUtc != DateTime.MinValue &&
                    string.Equals(content, _lastBackupContent, StringComparison.Ordinal))
                {
                    return;
                }

                DateTime now = DateTime.UtcNow;
                if (_lastBackupUtc != DateTime.MinValue &&
                    now.Subtract(_lastBackupUtc).TotalHours < Math.Max(1.0, config.BackupIntervalHours))
                {
                    return;
                }

                try
                {
                    if (!Directory.Exists(BackupDirectory))
                    {
                        Directory.CreateDirectory(BackupDirectory);
                    }

                    string name = "config_" +
                        now.ToLocalTime().ToString("yyyyMMdd_HHmmss_fff") +
                        ".json";
                    string path = Path.Combine(BackupDirectory, name);
                    File.WriteAllText(path, content, new UTF8Encoding(false));

                    string[] backups = Directory.GetFiles(
                        BackupDirectory,
                        "config_*.json");
                    Array.Sort(backups, StringComparer.OrdinalIgnoreCase);
                    int maxBackups = Math.Max(1, config.BackupMaxCount);
                    for (int i = 0; i < backups.Length - maxBackups; i++)
                    {
                        try
                        {
                            File.Delete(backups[i]);
                        }
                        catch
                        {
                        }
                    }

                    _lastBackupUtc = now;
                    _lastBackupContent = content;
                }
                catch
                {
                }
            }
        }

        private static void MigrateExistingConfig()
        {
            string oldConfigPath = FindBestLegacyConfig();
            if (string.IsNullOrWhiteSpace(oldConfigPath) || !File.Exists(oldConfigPath))
            {
                return;
            }

            try
            {
                if (File.Exists(ConfigPath) && !ShouldReplaceExistingConfig(oldConfigPath))
                {
                    return;
                }

                string directory = Path.GetDirectoryName(ConfigPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (File.Exists(ConfigPath))
                {
                    string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                    string existingBackup = ConfigPath + ".migration-" + stamp;
                    File.Copy(ConfigPath, existingBackup, true);
                    File.Delete(ConfigPath);
                }

                File.Copy(oldConfigPath, ConfigPath, false);

                string oldBackupDirectory = Path.Combine(
                    Path.GetDirectoryName(oldConfigPath),
                    "backups");
                if (Directory.Exists(oldBackupDirectory))
                {
                    if (!Directory.Exists(BackupDirectory))
                    {
                        Directory.CreateDirectory(BackupDirectory);
                    }

                    foreach (string source in Directory.GetFiles(oldBackupDirectory, "config_*.json"))
                    {
                        string name = Path.GetFileName(source);
                        string target = Path.Combine(BackupDirectory, name);
                        if (!File.Exists(target))
                        {
                            File.Copy(source, target, false);
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private static string FindBestLegacyConfig()
        {
            if (string.Equals(
                    Environment.GetEnvironmentVariable(
                        "GHOSTDESK_TEST_DISABLE_LEGACY_MIGRATION"),
                    "1",
                    StringComparison.Ordinal))
            {
                return null;
            }

            List<string> candidates = new List<string>();
            HashSet<string> seen = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

            AddLegacyCandidate(candidates, seen, LegacyPortableConfigPath);
            AddLegacyCandidate(candidates, seen, LegacyConfigPath);
            AddLegacyCandidate(candidates, seen, RoamingConfigPath);

            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string parentDirectory = GetParentDirectory(baseDirectory);
            if (!string.IsNullOrWhiteSpace(parentDirectory))
            {
                AddPortableSiblings(candidates, seen, parentDirectory);
            }

            string bestPath = null;
            int bestScore = -1;
            DateTime bestTime = DateTime.MinValue;

            foreach (string candidate in candidates)
            {
                AppConfig parsed = TryReadConfig(candidate);
                int score = ScoreConfig(parsed);
                DateTime modified;
                try
                {
                    modified = File.GetLastWriteTimeUtc(candidate);
                }
                catch
                {
                    modified = DateTime.MinValue;
                }

                if (score > bestScore ||
                    (score == bestScore && modified > bestTime))
                {
                    bestPath = candidate;
                    bestScore = score;
                    bestTime = modified;
                }
            }

            return bestPath;
        }

        private static void AddPortableSiblings(
            List<string> candidates,
            HashSet<string> seen,
            string parentDirectory)
        {
            try
            {
                foreach (string child in Directory.GetDirectories(parentDirectory))
                {
                    string name = Path.GetFileName(child);
                    if (!name.StartsWith("GhostDesk-", StringComparison.OrdinalIgnoreCase) &&
                        !name.StartsWith("GhostDesk_", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    AddLegacyCandidate(
                        candidates,
                        seen,
                        Path.Combine(child, "GhostDeskData", "config.json"));
                }
            }
            catch
            {
            }
        }

        private static void AddLegacyCandidate(
            List<string> candidates,
            HashSet<string> seen,
            string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            string key = Path.GetFullPath(path);
            if (!seen.Add(key))
            {
                return;
            }

            candidates.Add(key);
        }

        private static string GetParentDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            try
            {
                return Path.GetDirectoryName(path);
            }
            catch
            {
                return null;
            }
        }

        private static bool ShouldReplaceExistingConfig(string oldConfigPath)
        {
            try
            {
                AppConfig existing = TryReadConfig(ConfigPath);
                AppConfig candidate = TryReadConfig(oldConfigPath);
                return ScoreConfig(candidate) > ScoreConfig(existing);
            }
            catch
            {
                return false;
            }
        }

        private static int ScoreConfig(AppConfig config)
        {
            if (config == null)
            {
                return -1;
            }

            int score = 0;
            if (config.Regions != null)
            {
                score += config.Regions.Count * 100;
                foreach (RegionConfig region in config.Regions)
                {
                    if (region != null && region.Items != null)
                    {
                        score += region.Items.Count;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(config.Mode))
            {
                score += 1;
            }

            return score;
        }

        private static AppConfig TryReadConfig(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            try
            {
                DataContractJsonSerializer serializer =
                    new DataContractJsonSerializer(typeof(AppConfig));
                using (FileStream stream = File.OpenRead(path))
                {
                    return serializer.ReadObject(stream) as AppConfig;
                }
            }
            catch
            {
                return null;
            }
        }

        private static void EnsureInitialBackup()
        {
            lock (BackupLock)
            {
                try
                {
                    if (!File.Exists(ConfigPath))
                    {
                        return;
                    }

                    if (Directory.Exists(BackupDirectory) &&
                        Directory.GetFiles(BackupDirectory, "config_*.json").Length > 0)
                    {
                        return;
                    }

                    if (!Directory.Exists(BackupDirectory))
                    {
                        Directory.CreateDirectory(BackupDirectory);
                    }

                    string name = "config_" +
                        DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") +
                        "_startup.json";
                    File.Copy(ConfigPath, Path.Combine(BackupDirectory, name));
                }
                catch
                {
                }
            }
        }

        public static AppConfig CreateDefault()
        {
            AppConfig config = new AppConfig();
            config.IdleSeconds = 3.0;
            config.FadeSeconds = 0.8;
            config.LaunchIntervalMs = 400;
            config.ScheduledTasks = new List<ScheduledTaskConfig>();
            config.AutoStart = false;
            config.ShowHiddenFiles = false;
            config.PauseInFullscreen = true;
            config.BackupIntervalHours = 12.0;
            config.BackupMaxCount = 7;
            config.CheckForUpdates = true;
            config.LastUpdateCheck = "";
            config.SkippedVersion = "";
            config.EnableSnap = true;
            config.EnableAutoReplace = true;
            config.CollapseHoverTrigger = AppConfig.CollapseHoverButton;
            config.Mode = AppConfig.ModeRegions;
            config.Regions = new List<RegionConfig>();

            RegionConfig files = new RegionConfig();
            files.Id = Guid.NewGuid().ToString("N");
            files.Name = "分区 1";
            files.X = 48;
            files.Y = 48;
            files.Width = 430;
            files.Height = 190;
            files.DockEdge = RegionConfig.DockNone;
            files.DockOrder = 0;
            files.ShowExtensions = "true";
            files.Items = new List<DesktopItem>();
            files.LaunchItems = new List<DesktopItem>();
            config.Regions.Add(files);

            RegionConfig quick = new RegionConfig();
            quick.Id = Guid.NewGuid().ToString("N");
            quick.Name = "分区 2";
            quick.X = 520;
            quick.Y = 48;
            quick.Width = 360;
            quick.Height = 150;
            quick.DockEdge = RegionConfig.DockNone;
            quick.DockOrder = 0;
            quick.ShowExtensions = "true";
            quick.Items = new List<DesktopItem>();
            quick.LaunchItems = new List<DesktopItem>();
            config.Regions.Add(quick);

            return config;
        }

        private static bool Normalize(AppConfig config)
        {
            bool configurationMigrated = false;
            if (config.IdleSeconds < 0.0)
            {
                config.IdleSeconds = 3.0;
            }
            if (config.FadeSeconds < 0.05)
            {
                config.FadeSeconds = 0.8;
            }
            if (config.LaunchIntervalMs < 0)
            {
                config.LaunchIntervalMs = 400;
            }
            else if (config.LaunchIntervalMs > 5000)
            {
                config.LaunchIntervalMs = 5000;
            }
            if (NormalizeScheduledTasks(config))
            {
                configurationMigrated = true;
            }
            if (config.BackupIntervalHours < 1.0)
            {
                config.BackupIntervalHours = 12.0;
            }
            else if (config.BackupIntervalHours > 168.0)
            {
                config.BackupIntervalHours = 168.0;
            }
            if (config.BackupMaxCount < 1)
            {
                config.BackupMaxCount = 7;
            }
            else if (config.BackupMaxCount > 30)
            {
                config.BackupMaxCount = 30;
            }
            config.PauseInFullscreen = true;
            if (!config.CheckForUpdates.HasValue)
            {
                config.CheckForUpdates = true;
            }
            if (config.LastUpdateCheck == null)
            {
                config.LastUpdateCheck = "";
            }
            if (config.SkippedVersion == null)
            {
                config.SkippedVersion = "";
            }
            if (!config.EnableSnap.HasValue)
            {
                config.EnableSnap = true;
            }
            if (!config.EnableEdgeDock.HasValue)
            {
                config.EnableEdgeDock = true;
            }
            if (!config.EnableGuides.HasValue)
            {
                config.EnableGuides = true;
            }
            config.MaterialPreset = NormalizeMaterialPreset(config.MaterialPreset);
            if (string.IsNullOrEmpty(config.MaterialPreset))
            {
                config.MaterialPreset = AppConfig.MaterialGlass;
            }
            if (!config.MaterialOpacity.HasValue)
            {
                config.MaterialOpacity = AppConfig.DefaultMaterialOpacity;
            }
            if (!config.MaterialTint.HasValue)
            {
                config.MaterialTint = AppConfig.DefaultMaterialTint;
            }
            if (!config.MaterialBorder.HasValue)
            {
                config.MaterialBorder = AppConfig.DefaultMaterialBorder;
            }
            if (!config.MaterialShadow.HasValue)
            {
                config.MaterialShadow = AppConfig.DefaultMaterialShadow;
            }
            if (!config.EdgeDockBand.HasValue)
            {
                config.EdgeDockBand = AppConfig.DefaultEdgeDockBand;
            }
            else
            {
                config.EdgeDockBand = Math.Max(
                    0,
                    Math.Min(240, config.EdgeDockBand.Value));
            }
            if (!config.SafeInsetTop.HasValue)
            {
                config.SafeInsetTop = 0;
            }
            if (!config.SafeInsetBottom.HasValue)
            {
                config.SafeInsetBottom = 0;
            }
            if (!config.SafeInsetLeft.HasValue)
            {
                config.SafeInsetLeft = 0;
            }
            if (!config.SafeInsetRight.HasValue)
            {
                config.SafeInsetRight = 0;
            }
            if (!config.EnableAutoReplace.HasValue)
            {
                config.EnableAutoReplace = true;
                configurationMigrated = true;
            }
            if (!string.Equals(
                    config.CollapseHoverTrigger,
                    AppConfig.CollapseHoverButton,
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(
                    config.CollapseHoverTrigger,
                    AppConfig.CollapseHoverRow,
                    StringComparison.OrdinalIgnoreCase))
            {
                config.CollapseHoverTrigger = AppConfig.CollapseHoverButton;
            }
            else
            {
                config.CollapseHoverTrigger = config.CollapseHoverTrigger.ToLowerInvariant();
            }
            config.Mode = AppConfig.ModeRegions;
            if (config.Regions == null)
            {
                config.Regions = new List<RegionConfig>();
            }

            HashSet<string> regionIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (RegionConfig region in config.Regions)
            {
                if (string.IsNullOrEmpty(region.Id) || !regionIds.Add(region.Id))
                {
                    region.Id = Guid.NewGuid().ToString("N");
                    regionIds.Add(region.Id);
                }
                if (string.IsNullOrEmpty(region.Name))
                {
                    region.Name = "新分区";
                }
                if (region.Width < 96)
                {
                    region.Width = 96;
                }
                if (region.Height < 72)
                {
                    region.Height = 72;
                }
                region.DockEdge = NormalizeDockEdge(region.DockEdge);
                region.DockAnchor = NormalizeDockAnchor(region.DockAnchor);
                region.TitleSide = NormalizeTitleSide(region.TitleSide);
                region.MaterialPreset = NormalizeMaterialPreset(region.MaterialPreset);
                region.MaterialOpacity = ClampNullable(region.MaterialOpacity);
                region.MaterialTint = ClampNullable(region.MaterialTint);
                region.MaterialBorder = ClampNullable(region.MaterialBorder);
                region.MaterialShadow = ClampNullable(region.MaterialShadow);
                // 旧版本把「固定四边」写在 DockAnchor 里，这里迁移成标题朝向，位置交还给自动贴边
                if (region.DockAnchor != RegionConfig.DockAnchorFree &&
                    region.DockAnchor != RegionConfig.DockAnchorAuto)
                {
                    if (string.IsNullOrEmpty(region.TitleSide))
                    {
                        region.TitleSide = region.DockAnchor;
                    }

                    region.DockAnchor = RegionConfig.DockAnchorAuto;
                }
                if (string.IsNullOrEmpty(region.DockEdge))
                {
                    region.DockOrder = 0;
                }
                else if (region.DockOrder < 1)
                {
                    region.DockOrder = 1;
                }
                if (string.IsNullOrEmpty(region.ShowExtensions))
                {
                    region.ShowExtensions = "true";
                }
                else
                {
                    region.ShowExtensions = string.Equals(
                        region.ShowExtensions.Trim(),
                        "false",
                        StringComparison.OrdinalIgnoreCase) ? "false" : "true";
                }
                if (region.Items == null)
                {
                    region.Items = new List<DesktopItem>();
                }
                if (region.LaunchItems == null)
                {
                    region.LaunchItems = new List<DesktopItem>();
                }

                foreach (DesktopItem item in region.Items)
                {
                    if (item == null)
                    {
                        continue;
                    }

                    if (item.Aliases == null)
                    {
                        item.Aliases = "";
                    }

                    if (item.IconPath == null)
                    {
                        item.IconPath = "";
                    }

                    if (item.OriginalPath == null)
                    {
                        item.OriginalPath = "";
                        configurationMigrated = true;
                    }
                }

                foreach (DesktopItem item in region.LaunchItems)
                {
                    if (item == null)
                    {
                        continue;
                    }

                    if (item.Aliases == null)
                    {
                        item.Aliases = "";
                    }

                    if (item.IconPath == null)
                    {
                        item.IconPath = "";
                    }

                    if (item.OriginalPath == null)
                    {
                        item.OriginalPath = "";
                        configurationMigrated = true;
                    }
                }
            }

            NormalizeDockOrders(config.Regions);

            // 旧版就有的修复：分区的快捷方式被外部改名/重建后，按名称重新指向现存的那份
            if (ShortcutPathRepair.RepairMissingShortcutItems(config.Regions))
            {
                configurationMigrated = true;
            }

            return configurationMigrated;
        }

        private static bool NormalizeScheduledTasks(AppConfig config)
        {
            if (config.ScheduledTasks == null)
            {
                config.ScheduledTasks = new List<ScheduledTaskConfig>();
                return true;
            }

            bool changed = false;
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            for (int index = config.ScheduledTasks.Count - 1; index >= 0; index--)
            {
                if (config.ScheduledTasks[index] == null)
                {
                    config.ScheduledTasks.RemoveAt(index);
                    changed = true;
                }
            }

            string now = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            foreach (ScheduledTaskConfig task in config.ScheduledTasks)
            {
                if (string.IsNullOrWhiteSpace(task.Id) || !ids.Add(task.Id))
                {
                    task.Id = Guid.NewGuid().ToString("N");
                    ids.Add(task.Id);
                    changed = true;
                }

                if (string.IsNullOrWhiteSpace(task.Name))
                {
                    task.Name = "定时任务";
                    changed = true;
                }

                string type = (task.TaskType ?? "").Trim().ToLowerInvariant();
                if (type != ScheduledTaskConfig.TypeApplication &&
                    type != ScheduledTaskConfig.TypeFile &&
                    type != ScheduledTaskConfig.TypeUrl &&
                    type != ScheduledTaskConfig.TypeShutdown)
                {
                    type = ScheduledTaskConfig.TypeFile;
                    changed = true;
                }
                if (!string.Equals(type, task.TaskType, StringComparison.Ordinal))
                {
                    task.TaskType = type;
                    changed = true;
                }

                if (task.Target == null)
                {
                    task.Target = "";
                    changed = true;
                }

                string mode = (task.ScheduleMode ?? "").Trim().ToLowerInvariant();
                if (mode != ScheduledTaskConfig.ModeOnce &&
                    mode != ScheduledTaskConfig.ModeDaily &&
                    mode != ScheduledTaskConfig.ModeWeekly &&
                    mode != ScheduledTaskConfig.ModeInterval)
                {
                    mode = ScheduledTaskConfig.ModeOnce;
                    changed = true;
                }
                if (!string.Equals(mode, task.ScheduleMode, StringComparison.Ordinal))
                {
                    task.ScheduleMode = mode;
                    changed = true;
                }

                DateTime time;
                if (!DateTime.TryParseExact(
                        (task.Time ?? "").Trim(),
                        "HH:mm",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out time))
                {
                    task.Time = "09:00";
                    changed = true;
                }
                else
                {
                    string normalizedTime = time.ToString("HH:mm", CultureInfo.InvariantCulture);
                    if (!string.Equals(normalizedTime, task.Time, StringComparison.Ordinal))
                    {
                        task.Time = normalizedTime;
                        changed = true;
                    }
                }

                int day = Math.Max(0, Math.Min(6, task.DayOfWeek));
                if (day != task.DayOfWeek)
                {
                    task.DayOfWeek = day;
                    changed = true;
                }

                int interval = task.IntervalMinutes;
                if (interval < 1)
                {
                    interval = 60;
                }
                else if (interval > 10080)
                {
                    interval = 10080;
                }
                if (interval != task.IntervalMinutes)
                {
                    task.IntervalMinutes = interval;
                    changed = true;
                }

                int countdown = task.CountdownSeconds;
                if (countdown < 0)
                {
                    countdown = 0;
                }
                else if (countdown > 600)
                {
                    countdown = 600;
                }
                if (countdown != task.CountdownSeconds)
                {
                    task.CountdownSeconds = countdown;
                    changed = true;
                }

                DateTime created;
                if (!DateTime.TryParse(
                        task.CreatedUtc,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind,
                        out created))
                {
                    task.CreatedUtc = now;
                    changed = true;
                }
                else
                {
                    string normalizedCreated =
                        created.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
                    if (!string.Equals(
                            normalizedCreated,
                            task.CreatedUtc,
                            StringComparison.Ordinal))
                    {
                        task.CreatedUtc = normalizedCreated;
                        changed = true;
                    }
                }

                DateTime firstRun;
                if (!DateTime.TryParse(
                        task.FirstRunUtc,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind,
                        out firstRun))
                {
                    DateTime baseline;
                    if (!DateTime.TryParse(
                            task.CreatedUtc,
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.RoundtripKind,
                            out baseline))
                    {
                        baseline = DateTime.UtcNow;
                    }

                    if (string.Equals(
                            task.ScheduleMode,
                            ScheduledTaskConfig.ModeInterval,
                            StringComparison.Ordinal))
                    {
                        firstRun = baseline.ToUniversalTime();
                    }
                    else
                    {
                        DateTime localBaseline = baseline.ToLocalTime();
                        firstRun = localBaseline.Date.Add(time.TimeOfDay);
                        if (string.Equals(
                                task.ScheduleMode,
                                ScheduledTaskConfig.ModeWeekly,
                                StringComparison.Ordinal))
                        {
                            int delta =
                                (task.DayOfWeek - (int)localBaseline.DayOfWeek + 7) % 7;
                            firstRun = firstRun.AddDays(delta);
                        }

                        if (firstRun < localBaseline)
                        {
                            firstRun = firstRun.AddDays(
                                string.Equals(
                                    task.ScheduleMode,
                                    ScheduledTaskConfig.ModeWeekly,
                                    StringComparison.Ordinal)
                                    ? 7
                                    : 1);
                        }

                        firstRun = firstRun.ToUniversalTime();
                    }

                    task.FirstRunUtc = firstRun.ToString(
                        "o",
                        CultureInfo.InvariantCulture);
                    changed = true;
                }
                else
                {
                    string normalizedFirstRun =
                        firstRun.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
                    if (!string.Equals(
                            normalizedFirstRun,
                            task.FirstRunUtc,
                            StringComparison.Ordinal))
                    {
                        task.FirstRunUtc = normalizedFirstRun;
                        changed = true;
                    }
                }

                if (!string.IsNullOrWhiteSpace(task.LastRunUtc))
                {
                    DateTime lastRun;
                    if (!DateTime.TryParse(
                            task.LastRunUtc,
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.RoundtripKind,
                            out lastRun))
                    {
                        task.LastRunUtc = "";
                        changed = true;
                    }
                    else
                    {
                        string normalizedLast =
                            lastRun.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
                        if (!string.Equals(normalizedLast, task.LastRunUtc, StringComparison.Ordinal))
                        {
                            task.LastRunUtc = normalizedLast;
                            changed = true;
                        }
                    }
                }
                else if (task.LastRunUtc == null)
                {
                    task.LastRunUtc = "";
                    changed = true;
                }
            }

            return changed;
        }

        private static void NormalizeDockOrders(List<RegionConfig> regions)
        {
            Dictionary<string, HashSet<int>> usedOrders =
                new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
            Dictionary<string, int> nextOrders =
                new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (RegionConfig region in regions)
            {
                if (region == null || string.IsNullOrEmpty(region.DockEdge))
                {
                    continue;
                }

                HashSet<int> used;
                if (!usedOrders.TryGetValue(region.DockEdge, out used))
                {
                    used = new HashSet<int>();
                    usedOrders[region.DockEdge] = used;
                    nextOrders[region.DockEdge] = 1;
                }

                if (region.DockOrder < 1 || used.Contains(region.DockOrder))
                {
                    int next = nextOrders[region.DockEdge];
                    while (used.Contains(next))
                    {
                        next++;
                    }
                    region.DockOrder = next;
                }

                used.Add(region.DockOrder);
                if (region.DockOrder >= nextOrders[region.DockEdge])
                {
                    nextOrders[region.DockEdge] = region.DockOrder + 1;
                }
            }
        }

        private static string NormalizeDockEdge(string edge)
        {
            if (string.IsNullOrEmpty(edge))
            {
                return RegionConfig.DockNone;
            }

            string value = edge.Trim().ToLowerInvariant();
            if (value == RegionConfig.DockTop ||
                value == RegionConfig.DockBottom ||
                value == RegionConfig.DockLeft ||
                value == RegionConfig.DockRight)
            {
                return value;
            }

            return RegionConfig.DockNone;
        }

        /// <summary>把分区贴边朝向归一到 auto / top / bottom / left / right / free。</summary>
        internal static string NormalizeDockAnchor(string anchor)
        {
            if (string.IsNullOrWhiteSpace(anchor))
            {
                return RegionConfig.DockAnchorAuto;
            }

            string value = anchor.Trim().ToLowerInvariant();
            if (value == RegionConfig.DockAnchorAuto ||
                value == RegionConfig.DockTop ||
                value == RegionConfig.DockBottom ||
                value == RegionConfig.DockLeft ||
                value == RegionConfig.DockRight)
            {
                return value;
            }

            // 「不贴边」已取消，旧配置统一回到自动判断
            return RegionConfig.DockAnchorAuto;
        }

        /// <summary>标题朝向只允许空（自动）或 top/bottom/left/right。</summary>
        internal static string NormalizeTitleSide(string side)
        {
            if (string.IsNullOrWhiteSpace(side))
            {
                return "";
            }

            string value = side.Trim().ToLowerInvariant();
            if (value == RegionConfig.DockTop ||
                value == RegionConfig.DockBottom ||
                value == RegionConfig.DockLeft ||
                value == RegionConfig.DockRight)
            {
                return value;
            }

            return "";
        }

        /// <summary>材质预设允许 glass / frosted / solid，其它值一律视为空（跟随全局）。</summary>
        internal static string NormalizeMaterialPreset(string preset)
        {
            if (string.IsNullOrWhiteSpace(preset))
            {
                return "";
            }

            string value = preset.Trim().ToLowerInvariant();
            if (value == AppConfig.MaterialGlass ||
                value == AppConfig.MaterialFrosted ||
                value == AppConfig.MaterialSolid)
            {
                return value;
            }

            return "";
        }

        internal static int? ClampNullable(int? value)
        {
            if (!value.HasValue)
            {
                return null;
            }

            return Math.Max(0, Math.Min(100, value.Value));
        }

        internal static int ClampPercent(int? value, int fallback)
        {
            if (!value.HasValue)
            {
                return fallback;
            }

            return Math.Max(0, Math.Min(100, value.Value));
        }

        private static string FormatInt(int? value, int fallback)
        {
            int result = value.HasValue ? Math.Max(0, Math.Min(100, value.Value)) : fallback;
            return result.ToString(CultureInfo.InvariantCulture);
        }

        private static string FormatNullableInt(int? value)
        {
            return value.HasValue
                ? Math.Max(0, Math.Min(100, value.Value)).ToString(CultureInfo.InvariantCulture)
                : "null";
        }

        private static string FormatInset(int? value)
        {
            return value.HasValue && value.Value > 0
                ? value.Value.ToString(CultureInfo.InvariantCulture)
                : "0";
        }

        private static void TryBackupBrokenConfig()
        {
            try
            {
                string backup = ConfigPath + ".broken.json";
                if (File.Exists(backup))
                {
                    File.Delete(backup);
                }
                File.Copy(ConfigPath, backup);
            }
            catch
            {
            }
        }

        private static bool RemoveLegacyDefaultItems(AppConfig config)
        {
            bool removed = false;
            foreach (RegionConfig region in config.Regions)
            {
                if (region.Items == null)
                {
                    continue;
                }

                for (int i = region.Items.Count - 1; i >= 0; i--)
                {
                    DesktopItem item = region.Items[i];
                    if (IsLegacyDefaultItem(item))
                    {
                        region.Items.RemoveAt(i);
                        removed = true;
                    }
                }
            }

            return removed;
        }

        private static bool IsLegacyDefaultItem(DesktopItem item)
        {
            if (item == null)
            {
                return false;
            }

            if ((item.Type ?? "").Equals("shell", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string name = item.Name ?? "";
            if (name == "桌面" || name == "文档" || name == "图片" || name == "音乐")
            {
                if ((item.Type ?? "").Equals("folder", StringComparison.OrdinalIgnoreCase))
                {
                    string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                    string music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);

                    return SamePath(item.Path, desktop) ||
                           SamePath(item.Path, documents) ||
                           SamePath(item.Path, pictures) ||
                           SamePath(item.Path, music);
                }
            }

            return false;
        }

        private static bool SamePath(string left, string right)
        {
            if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
            {
                return false;
            }

            return string.Equals(left.TrimEnd('\\'), right.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }

        private static string Inv(double value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static string FormatNullableBool(bool? value)
        {
            if (!value.HasValue)
            {
                return "null";
            }

            return value.Value ? "true" : "false";
        }

        private static string Escape(string value)
        {
            if (value == null)
            {
                return "";
            }

            StringBuilder sb = new StringBuilder();
            foreach (char c in value)
            {
                switch (c)
                {
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '"':
                        sb.Append("\\\"");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    default:
                        if (c < 32)
                        {
                            sb.Append("\\u");
                            sb.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
