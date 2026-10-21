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
        public string Name { get; set; }

        [DataMember]
        public string Aliases { get; set; }

        [DataMember]
        public string IconPath { get; set; }

        [DataMember]
        public bool ExternalReference { get; set; }
    }

    [DataContract]
    public class RegionConfig
    {
        public const string DockNone = "";
        public const string DockTop = "top";
        public const string DockBottom = "bottom";
        public const string DockLeft = "left";
        public const string DockRight = "right";

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
        public bool AutoStart { get; set; }

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

        public static void Save(AppConfig config)
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
            sb.AppendLine("  \"AutoStart\": " + (config.AutoStart ? "true" : "false") + ",");
            sb.AppendLine("  \"PauseInFullscreen\": " + (config.PauseInFullscreen ? "true" : "false") + ",");
            sb.AppendLine("  \"BackupIntervalHours\": " + Inv(config.BackupIntervalHours) + ",");
            sb.AppendLine("  \"BackupMaxCount\": " + config.BackupMaxCount.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"CheckForUpdates\": " + (config.CheckForUpdates != false ? "true" : "false") + ",");
            sb.AppendLine("  \"LastUpdateCheck\": \"" + Escape(config.LastUpdateCheck) + "\",");
            sb.AppendLine("  \"SkippedVersion\": \"" + Escape(config.SkippedVersion) + "\",");
            sb.AppendLine("  \"EnableSnap\": " + (config.EnableSnap == true ? "true" : "false") + ",");
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
                sb.AppendLine("      \"DockOrder\": " + region.DockOrder.ToString(CultureInfo.InvariantCulture) + ",");
                sb.AppendLine("      \"ShowExtensions\": \"" + Escape(region.ShowExtensions) + "\",");
                sb.AppendLine("      \"Items\": [");

                for (int j = 0; j < region.Items.Count; j++)
                {
                    DesktopItem item = region.Items[j];
                    sb.Append("        { \"Type\": \"" + Escape(item.Type) + "\", ");
                    sb.Append("\"Path\": \"" + Escape(item.Path) + "\", ");
                    sb.Append("\"Name\": \"" + Escape(item.Name) + "\", ");
                    sb.Append("\"Aliases\": \"" + Escape(item.Aliases) + "\", ");
                    sb.Append("\"IconPath\": \"" + Escape(item.IconPath) + "\", ");
                    sb.Append("\"ExternalReference\": " + (item.ExternalReference ? "true" : "false") + " }");
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
                    sb.Append("\"Name\": \"" + Escape(item.Name) + "\", ");
                    sb.Append("\"Aliases\": \"" + Escape(item.Aliases) + "\", ");
                    sb.Append("\"IconPath\": \"" + Escape(item.IconPath) + "\", ");
                    sb.Append("\"ExternalReference\": " + (item.ExternalReference ? "true" : "false") + " }");
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
        }

        private static void AtomicWrite(string path, string content)
        {
            lock (AtomicWriteLock)
            {
                try
                {
                    string directory = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                }
                catch
                {
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
                catch
                {
                    replaced = false;
                }
                finally
                {
                    if (!replaced)
                    {
                        try
                        {
                            File.WriteAllText(path, content, new UTF8Encoding(false));
                        }
                        catch
                        {
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
            config.AutoStart = false;
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

                    if (MigrateExternalReference(item))
                    {
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

                    if (MigrateExternalReference(item))
                    {
                        configurationMigrated = true;
                    }
                }
            }

            if (ShortcutPathRepair.RepairMissingShortcutItems(config.Regions))
            {
                configurationMigrated = true;
            }

            NormalizeDockOrders(config.Regions);
            return configurationMigrated;
        }

        private static bool MigrateExternalReference(DesktopItem item)
        {
            if (item == null ||
                string.IsNullOrWhiteSpace(item.Path) ||
                item.Path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (string.Equals(
                    Path.GetExtension(item.Path),
                    ".lnk",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (!item.ExternalReference)
                {
                    return false;
                }

                item.ExternalReference = false;
                return true;
            }

            if (item.ExternalReference)
            {
                return false;
            }

            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            if (string.IsNullOrEmpty(desktop))
            {
                return false;
            }

            string normalizedPath = item.Path.TrimEnd('\\');
            string normalizedDesktop = desktop.TrimEnd('\\');
            if (normalizedPath.StartsWith(
                normalizedDesktop + "\\",
                StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            item.ExternalReference = true;
            return true;
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
