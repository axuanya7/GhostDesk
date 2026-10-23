using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace GhostDesk
{
    internal static class ItemStorage
    {
        internal static string LastMoveWarning { get; private set; }

        internal static string StorageDirectory
        {
            get
            {
                // 统一放在用户数据目录（%APPDATA%\GhostDesk），与 config.json 同处，
                // 避免程序目录不可写时出现"两份数据"。
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "GhostDesk",
                    "Storage");
            }
        }

        internal static void EnsureStorageDirectory()
        {
            Directory.CreateDirectory(StorageDirectory);
        }

        internal static bool IsManagedPath(string path)
        {
            string normalized = NormalizePath(path);
            if (string.IsNullOrEmpty(normalized))
            {
                return false;
            }

            string storage = NormalizePath(StorageDirectory);
            return string.Equals(
                       normalized,
                       storage,
                       StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith(
                       storage + Path.DirectorySeparatorChar,
                       StringComparison.OrdinalIgnoreCase);
        }

        internal static string MoveIntoStorage(
            string sourcePath,
            out string originalPath)
        {
            string normalizedSource = RequireExistingPath(sourcePath);
            if (IsManagedPath(normalizedSource))
            {
                originalPath = "";
                return normalizedSource;
            }

            EnsureStorageDirectory();
            string fileName = GetItemName(normalizedSource);
            string destination = GetUniqueDestination(
                StorageDirectory,
                fileName);

            MovePath(normalizedSource, destination);
            ClearHiddenSystemAttributes(destination);
            originalPath = normalizedSource;
            NotifyPathChanged(normalizedSource);
            NotifyPathChanged(destination);
            return destination;
        }

        internal static string RollbackMovedPath(
            string sourcePath,
            string originalPath)
        {
            string normalizedSource = RequireExistingPath(sourcePath);
            string destination = NormalizePath(originalPath);
            if (string.IsNullOrEmpty(destination))
            {
                throw new ArgumentException(
                    "原始路径为空。",
                    "originalPath");
            }

            string destinationDirectory = Path.GetDirectoryName(destination);
            if (string.IsNullOrWhiteSpace(destinationDirectory) ||
                !Directory.Exists(destinationDirectory))
            {
                throw new DirectoryNotFoundException(
                    "原始目录不存在：" + destinationDirectory);
            }

            if (Exists(destination))
            {
                destination = GetUniqueDestination(
                    destinationDirectory,
                    GetItemName(destination));
            }

            MovePath(normalizedSource, destination);
            ClearHiddenSystemAttributes(destination);
            NotifyPathChanged(normalizedSource);
            NotifyPathChanged(destination);
            return destination;
        }

        internal static string MoveToDesktop(DesktopItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Path))
            {
                throw new ArgumentException("项目路径为空。", "item");
            }

            string source = RequireExistingPath(item.Path);
            if (!IsManagedPath(source))
            {
                throw new InvalidOperationException(
                    "只有 Storage 中的项目才能移回桌面。");
            }

            string desktop = ResolveDesktopDirectory();
            if (string.IsNullOrWhiteSpace(desktop) ||
                !Directory.Exists(desktop))
            {
                throw new DirectoryNotFoundException(
                    "无法找到当前用户的桌面文件夹。");
            }

            string destination = GetUniqueDestination(
                desktop,
                GetItemName(source));
            MovePath(source, destination);
            ClearHiddenSystemAttributes(destination);
            NotifyPathChanged(source);
            NotifyPathChanged(destination);
            return destination;
        }

        internal static bool WasExternalMoveCommitted(DesktopItem item)
        {
            if (item == null ||
                string.IsNullOrWhiteSpace(item.Path) ||
                item.Path.StartsWith(
                    "shell:",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string sourcePath = NormalizePath(item.Path);
            return !string.IsNullOrEmpty(sourcePath) &&
                !File.Exists(sourcePath) &&
                !Directory.Exists(sourcePath);
        }

        /// <summary>
        /// 在目标所在目录创建指向它的 .lnk 快捷方式（已存在指向同一目标的则复用）。
        /// 分区引用快捷方式而不是原始文件，这样重命名/删除只影响快捷方式。
        /// </summary>
        internal static string CreateShortcutFor(string targetPath, out string error)
        {
            error = null;
            string normalizedTarget = NormalizePath(targetPath);
            if (string.IsNullOrEmpty(normalizedTarget) ||
                (!File.Exists(normalizedTarget) && !Directory.Exists(normalizedTarget)))
            {
                error = "目标不存在";
                return null;
            }

            try
            {
                bool isDirectory = Directory.Exists(normalizedTarget);
                string targetDirectory = Path.GetDirectoryName(normalizedTarget);
                if (string.IsNullOrEmpty(targetDirectory))
                {
                    error = "无法确定目标所在目录";
                    return null;
                }

                string baseName = Path.GetFileName(normalizedTarget.TrimEnd('\\'));
                if (!isDirectory)
                {
                    baseName = Path.GetFileNameWithoutExtension(baseName);
                }

                if (string.IsNullOrEmpty(baseName))
                {
                    baseName = "快捷方式";
                }

                string linkPath = Path.Combine(targetDirectory, baseName + ".lnk");
                int index = 2;
                while (File.Exists(linkPath))
                {
                    if (string.Equals(
                            ReadShortcutTarget(linkPath),
                            normalizedTarget,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return linkPath;
                    }

                    linkPath = Path.Combine(
                        targetDirectory,
                        baseName + " (" +
                        index.ToString(CultureInfo.InvariantCulture) + ").lnk");
                    index++;
                }

                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null)
                {
                    error = "系统未提供 WScript.Shell";
                    return null;
                }

                object shell = null;
                object shortcut = null;
                try
                {
                    shell = Activator.CreateInstance(shellType);
                    shortcut = shellType.InvokeMember(
                        "CreateShortcut",
                        BindingFlags.InvokeMethod,
                        null,
                        shell,
                        new object[] { linkPath });

                    Type shortcutType = shortcut.GetType();
                    shortcutType.InvokeMember(
                        "TargetPath",
                        BindingFlags.SetProperty,
                        null,
                        shortcut,
                        new object[] { normalizedTarget });
                    shortcutType.InvokeMember(
                        "WorkingDirectory",
                        BindingFlags.SetProperty,
                        null,
                        shortcut,
                        new object[] { targetDirectory });

                    if (!isDirectory)
                    {
                        shortcutType.InvokeMember(
                            "IconLocation",
                            BindingFlags.SetProperty,
                            null,
                            shortcut,
                            new object[] { normalizedTarget + ",0" });
                    }

                    shortcutType.InvokeMember(
                        "Save",
                        BindingFlags.InvokeMethod,
                        null,
                        shortcut,
                        null);
                }
                finally
                {
                    if (shortcut != null && Marshal.IsComObject(shortcut))
                    {
                        Marshal.ReleaseComObject(shortcut);
                    }

                    if (shell != null && Marshal.IsComObject(shell))
                    {
                        Marshal.ReleaseComObject(shell);
                    }
                }

                if (!File.Exists(linkPath))
                {
                    error = "快捷方式未创建成功";
                    return null;
                }

                return linkPath;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        /// <summary>目标路径的稳定短标识，用于同名快捷方式去重。</summary>
        private static string PathToken(string path)
        {
            try
            {
                using (MD5 md5 = MD5.Create())
                {
                    byte[] hash = md5.ComputeHash(
                        Encoding.UTF8.GetBytes((path ?? "").ToLowerInvariant()));
                    StringBuilder sb = new StringBuilder(8);
                    for (int index = 0; index < 4; index++)
                    {
                        sb.Append(hash[index].ToString("x2", CultureInfo.InvariantCulture));
                    }

                    return sb.ToString();
                }
            }
            catch
            {
                return "link";
            }
        }

        internal static string ReadShortcutTarget(string shortcutPath)        {
            try
            {
                if (string.IsNullOrEmpty(shortcutPath) || !File.Exists(shortcutPath))
                {
                    return null;
                }

                object target = DesktopItemControl.ReadWindowsShortcutProperty(
                    shortcutPath,
                    "TargetPath");
                string text = target as string;
                if (string.IsNullOrWhiteSpace(text))
                {
                    return null;
                }

                return NormalizePath(
                    Environment.ExpandEnvironmentVariables(text.Trim().Trim('"')));
            }
            catch
            {
                return null;
            }
        }

        internal static string CreateUniqueItemPath(
            string baseName,
            string extension,
            bool isFolder)
        {
            EnsureStorageDirectory();
            string safeBaseName = string.IsNullOrWhiteSpace(baseName)
                ? "新建项目"
                : baseName.Trim();
            string fileName = safeBaseName + (extension ?? "");
            string path = GetUniqueDestination(
                StorageDirectory,
                fileName);

            if (isFolder)
            {
                Directory.CreateDirectory(path);
            }
            else
            {
                using (FileStream stream = new FileStream(
                    path,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None))
                {
                }
            }

            return path;
        }

        internal static string GetUniqueDestination(
            string directory,
            string fileName)
        {
            string safeFileName = string.IsNullOrWhiteSpace(fileName)
                ? "未命名"
                : fileName;
            string candidate = Path.Combine(directory, safeFileName);
            if (!Exists(candidate))
            {
                return candidate;
            }

            string baseName = Path.GetFileNameWithoutExtension(safeFileName);
            string extension = Path.GetExtension(safeFileName);
            for (int index = 2; index < 10000; index++)
            {
                candidate = Path.Combine(
                    directory,
                    baseName + " (" + index + ")" + extension);
                if (!Exists(candidate))
                {
                    return candidate;
                }
            }

            return Path.Combine(
                directory,
                baseName + " (" + Guid.NewGuid().ToString("N") + ")" + extension);
        }

        internal static bool MigrateLegacyItems(AppConfig config)
        {
            if (config == null || config.Regions == null)
            {
                return false;
            }

            EnsureStorageDirectory();
            bool changed = false;
            Dictionary<string, string> movedPaths =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> originalPaths =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (RegionConfig region in config.Regions)
            {
                if (region == null)
                {
                    continue;
                }

                changed |= MigrateItems(
                    region.Items,
                    movedPaths,
                    originalPaths);
                changed |= MigrateItems(
                    region.LaunchItems,
                    movedPaths,
                    originalPaths);
            }

            return changed;
        }

        internal static void ClearHiddenSystemAttributes(string path)
        {
            if (string.IsNullOrWhiteSpace(path) ||
                (!File.Exists(path) && !Directory.Exists(path)))
            {
                return;
            }

            try
            {
                FileAttributes attributes = File.GetAttributes(path);
                FileAttributes visibleAttributes = attributes &
                    ~(FileAttributes.Hidden | FileAttributes.System);
                if (visibleAttributes != attributes)
                {
                    File.SetAttributes(path, visibleAttributes);
                }
            }
            catch
            {
            }
        }

        internal static bool IsHidden(string path)
        {
            if (string.IsNullOrWhiteSpace(path) ||
                (!File.Exists(path) && !Directory.Exists(path)))
            {
                return false;
            }

            try
            {
                return (File.GetAttributes(path) & FileAttributes.Hidden) != 0;
            }
            catch
            {
                return false;
            }
        }

        internal static bool SetHiddenByGhostDesk(string path, bool hidden)
        {
            if (string.IsNullOrWhiteSpace(path) ||
                path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!File.Exists(path) && !Directory.Exists(path))
            {
                return false;
            }

            bool changed;
            string reason;

            if (hidden)
            {
                // 只加 HIDDEN。绝不要加 SYSTEM：带 SYSTEM 的文件即使在「显示隐藏的项目」
                // 下也不会显示，用户会以为隐藏失效。
                changed = StorageSafety.SetHiddenAttribute(path, false, out reason);
            }
            else
            {
                changed = StorageSafety.ClearHiddenAttribute(path);
                reason = null;
            }

            if (changed)
            {
                NotifyPathChanged(path);
                StorageSafety.RefreshDesktop(false);
            }

            return changed;
        }

        internal static bool SynchronizeManagedVisibility(
            AppConfig config,
            bool showHiddenFiles)
        {
            if (config == null || config.Regions == null)
            {
                return true;
            }

            HashSet<string> managedPaths = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (RegionConfig region in config.Regions)
            {
                if (region == null)
                {
                    continue;
                }

                CollectManagedPaths(region.Items, managedPaths);
                CollectManagedPaths(region.LaunchItems, managedPaths);
            }

            bool allApplied = true;
            foreach (string path in managedPaths)
            {
                if (SetHiddenByGhostDesk(path, !showHiddenFiles))
                {
                    continue;
                }

                // 已经不存在的路径不算失败，只有真实存在却改不动才需要回滚
                if (File.Exists(path) || Directory.Exists(path))
                {
                    allApplied = false;
                }
            }

            // 批量改完统一强刷一次桌面，避免逐项刷新造成闪烁
            StorageSafety.RefreshDesktop(true);

            return allApplied;
        }

        internal static bool SynchronizeItemVisibility(
            AppConfig config,
            DesktopItem removedItem,
            bool showHiddenFiles)
        {
            if (removedItem == null ||
                string.IsNullOrWhiteSpace(removedItem.Path) ||
                removedItem.Path.StartsWith(
                    "shell:",
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string path = NormalizePath(removedItem.Path);
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            // 文件已经不在了（删除到回收站、或被外部移走）：没有可见性要恢复，视为成功
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                return true;
            }

            bool tracked = removedItem.HiddenByGhostDesk ||
                HasManagedReference(config, path);
            if (!tracked)
            {
                return true;
            }

            if (HasManagedReference(config, path))
            {
                return SetHiddenByGhostDesk(
                    path,
                    !showHiddenFiles);
            }

            if (removedItem.HiddenByGhostDesk)
            {
                // 收纳前本来就是隐藏的 → 放回桌面后保持隐藏
                if (removedItem.HiddenBeforeGhostDesk == true)
                {
                    return SetHiddenByGhostDesk(path, true);
                }

                return SetHiddenByGhostDesk(path, false);
            }

            return true;
        }

        /// <summary>旧版本把文件搬进 {exeDir}\Storage，新版不再搬运，这个路径只用于升级迁移。</summary>
        internal static string LegacyStorageDirectory
        {
            get
            {
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Storage");
            }
        }

        /// <summary>
        /// 升级迁移：把旧版本搬进 Storage 的文件放回原位置，并把配置改成新语义（原地引用）。
        /// 找不到原位置时放回桌面；重名自动加序号，绝不覆盖已有文件。
        /// </summary>
        internal static bool MigrateLegacyStorageToInPlace(AppConfig config)
        {
            if (config == null || config.Regions == null)
            {
                return false;
            }

            string legacyStorage = LegacyStorageDirectory;
            if (!Directory.Exists(legacyStorage))
            {
                return false;
            }

            string desktop = Environment.GetFolderPath(
                Environment.SpecialFolder.DesktopDirectory);
            bool changed = false;
            foreach (RegionConfig region in config.Regions)
            {
                if (region == null)
                {
                    continue;
                }

                changed |= MigrateLegacyItemsIn(region.Items, legacyStorage, desktop);
                changed |= MigrateLegacyItemsIn(region.LaunchItems, legacyStorage, desktop);
            }

            return changed;
        }

        private static bool MigrateLegacyItemsIn(
            List<DesktopItem> items,
            string legacyStorage,
            string desktop)
        {
            if (items == null)
            {
                return false;
            }

            bool changed = false;
            foreach (DesktopItem item in items)
            {
                if (item == null ||
                    string.IsNullOrWhiteSpace(item.Path) ||
                    item.Path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string current = NormalizePath(item.Path);
                string original = NormalizePath(item.OriginalPath);
                bool currentExists = !string.IsNullOrEmpty(current) &&
                    (File.Exists(current) || Directory.Exists(current));

                // ① 路径失效，但原位置已经有文件：直接改写回原位置
                if (!currentExists &&
                    !string.IsNullOrEmpty(original) &&
                    (File.Exists(original) || Directory.Exists(original)))
                {
                    item.Path = original;
                    item.OriginalPath = "";
                    item.HiddenByGhostDesk = false;
                    item.HiddenBeforeGhostDesk = false;
                    changed = true;
                    continue;
                }

                // ② 找出仍留在旧 Storage 里的那一份
                string source = null;
                if (currentExists && IsUnder(current, legacyStorage))
                {
                    source = current;
                }
                else if (!currentExists)
                {
                    string name = Path.GetFileName(
                        string.IsNullOrEmpty(current) ? (item.Name ?? "") : current);
                    if (string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(original))
                    {
                        name = Path.GetFileName(original.TrimEnd('\\'));
                    }

                    if (!string.IsNullOrEmpty(name))
                    {
                        string candidate = Path.Combine(legacyStorage, name);
                        if (File.Exists(candidate) || Directory.Exists(candidate))
                        {
                            source = candidate;
                        }
                    }
                }

                if (string.IsNullOrEmpty(source))
                {
                    continue;
                }

                string target = original;
                if (string.IsNullOrEmpty(target))
                {
                    target = Path.Combine(
                        desktop,
                        Path.GetFileName(source.TrimEnd('\\')));
                }

                try
                {
                    string directory = Path.GetDirectoryName(target);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    target = GetUniqueDestination(
                        directory ?? desktop,
                        Path.GetFileName(target));

                    if (Directory.Exists(source))
                    {
                        Directory.Move(source, target);
                    }
                    else
                    {
                        File.Move(source, target);
                    }
                }
                catch
                {
                    continue;
                }

                item.Path = target;
                item.OriginalPath = "";
                item.HiddenByGhostDesk = false;
                item.HiddenBeforeGhostDesk = false;
                changed = true;
            }

            return changed;
        }

        private static bool IsUnder(string path, string directory)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(directory))
            {
                return false;
            }

            string prefix = NormalizePath(directory);
            if (string.IsNullOrEmpty(prefix))
            {
                return false;
            }

            prefix = prefix.TrimEnd('\\') + "\\";
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 自愈：桌面上的项只要在分区里，就应该处于「收纳中」（HiddenByGhostDesk=true）。
        /// 旧版本退出/迁移可能把这个标记清掉，导致桌面图标全部露出来，这里补回。
        /// </summary>
        internal static bool RepairManagedFlags(AppConfig config)
        {
            if (config == null || config.Regions == null)
            {
                return false;
            }

            bool changed = false;
            foreach (RegionConfig region in config.Regions)
            {
                if (region == null)
                {
                    continue;
                }

                changed |= RepairManagedFlagsIn(region.Items);
                changed |= RepairManagedFlagsIn(region.LaunchItems);
            }

            return changed;
        }

        private static bool RepairManagedFlagsIn(List<DesktopItem> items)
        {
            if (items == null)
            {
                return false;
            }

            bool changed = false;
            foreach (DesktopItem item in items)
            {
                if (item == null ||
                    item.HiddenByGhostDesk ||
                    string.IsNullOrWhiteSpace(item.Path) ||
                    item.Path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string path = NormalizePath(item.Path);
                if (string.IsNullOrEmpty(path) || IsManagedPath(path))
                {
                    continue;
                }

                if (!StorageSafety.IsOnUserDesktop(path) &&
                    !StorageSafety.IsOnPublicDesktop(path))
                {
                    continue;
                }

                if (!File.Exists(path) && !Directory.Exists(path))
                {
                    continue;
                }

                item.HiddenByGhostDesk = true;
                if (!item.HiddenBeforeGhostDesk.HasValue)
                {
                    item.HiddenBeforeGhostDesk = false;
                }

                changed = true;
            }

            return changed;
        }

        /// <summary>当前配置里、仍带隐藏属性且真实存在的收纳项路径。</summary>
        internal static List<string> CollectHiddenPaths(AppConfig config)
        {
            HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (config != null && config.Regions != null)
            {
                foreach (RegionConfig region in config.Regions)
                {
                    if (region == null)
                    {
                        continue;
                    }

                    CollectManagedPaths(region.Items, paths);
                    CollectManagedPaths(region.LaunchItems, paths);
                }
            }

            List<string> result = new List<string>();
            foreach (string path in paths)
            {
                if (File.Exists(path) || Directory.Exists(path))
                {
                    result.Add(path);
                }
            }

            return result;
        }

        /// <summary>
        /// 退出前把收纳项恢复显示（配置文件里的收纳记录保留，下次启动仍会重新隐藏）。
        /// </summary>
        internal static int RestoreHiddenToDesktop(AppConfig config)
        {
            int restored = 0;
            foreach (string path in CollectHiddenPaths(config))
            {
                if (StorageSafety.ClearHiddenAttribute(path))
                {
                    restored++;
                }
            }

            StorageSafety.RefreshDesktop(true);
            return restored;
        }

        private static void CollectManagedPaths(
            List<DesktopItem> items,
            HashSet<string> paths)
        {
            if (items == null)
            {
                return;
            }

            foreach (DesktopItem item in items)
            {
                if (item == null ||
                    !item.HiddenByGhostDesk ||
                    string.IsNullOrWhiteSpace(item.Path) ||
                    item.Path.StartsWith(
                        "shell:",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string path = NormalizePath(item.Path);
                if (!string.IsNullOrEmpty(path))
                {
                    paths.Add(path);
                }
            }
        }

        private static bool HasManagedReference(
            AppConfig config,
            string path)
        {
            if (config == null || config.Regions == null)
            {
                return false;
            }

            foreach (RegionConfig region in config.Regions)
            {
                if (region == null)
                {
                    continue;
                }

                if (ContainsManagedReference(region.Items, path) ||
                    ContainsManagedReference(region.LaunchItems, path))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsManagedReference(
            List<DesktopItem> items,
            string path)
        {
            if (items == null)
            {
                return false;
            }

            foreach (DesktopItem item in items)
            {
                if (item == null || !item.HiddenByGhostDesk)
                {
                    continue;
                }

                string itemPath = NormalizePath(item.Path);
                if (string.Equals(
                        itemPath,
                        path,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        internal static string InferType(string path)
        {
            if (Directory.Exists(path))
            {
                return "folder";
            }

            if (string.Equals(
                    Path.GetExtension(path),
                    ".lnk",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "shortcut";
            }

            return "file";
        }

        internal static string GetDisplayNameForPath(string path)
        {
            if (Directory.Exists(path))
            {
                return Path.GetFileName(path.TrimEnd('\\'));
            }

            if (string.Equals(
                    Path.GetExtension(path),
                    ".lnk",
                    StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFileNameWithoutExtension(path);
            }

            return Path.GetFileName(path);
        }

        internal static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            try
            {
                return Path.GetFullPath(path).TrimEnd('\\');
            }
            catch
            {
                return null;
            }
        }

        private static bool MigrateItems(
            List<DesktopItem> items,
            Dictionary<string, string> movedPaths,
            Dictionary<string, string> originalPaths)
        {
            if (items == null)
            {
                return false;
            }

            bool changed = false;
            foreach (DesktopItem item in items)
            {
                if (item == null ||
                    string.IsNullOrWhiteSpace(item.Path) ||
                    item.Path.StartsWith(
                        "shell:",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string sourcePath = NormalizePath(item.Path);
                if (string.IsNullOrEmpty(sourcePath))
                {
                    continue;
                }

                if (IsManagedPath(sourcePath))
                {
                    if (item.ExternalReference)
                    {
                        item.ExternalReference = false;
                        changed = true;
                    }
                    if (item.OriginalPath == null)
                    {
                        item.OriginalPath = "";
                        changed = true;
                    }
                    continue;
                }

                string storedPath;
                string originalPath;
                if (movedPaths.TryGetValue(sourcePath, out storedPath))
                {
                    originalPaths.TryGetValue(sourcePath, out originalPath);
                }
                else
                {
                    if (!File.Exists(sourcePath) &&
                        !Directory.Exists(sourcePath))
                    {
                        item.ExternalReference = false;
                        if (string.IsNullOrEmpty(item.OriginalPath))
                        {
                            item.OriginalPath = sourcePath;
                            changed = true;
                        }
                        continue;
                    }

                    try
                    {
                        storedPath = MoveIntoStorage(
                            sourcePath,
                            out originalPath);
                    }
                    catch
                    {
                        item.ExternalReference = false;
                        if (string.IsNullOrEmpty(item.OriginalPath))
                        {
                            item.OriginalPath = sourcePath;
                            changed = true;
                        }
                        continue;
                    }

                    movedPaths[sourcePath] = storedPath;
                    originalPaths[sourcePath] = originalPath;
                }

                item.Path = storedPath;
                item.Type = InferType(storedPath);
                item.ExternalReference = false;
                item.OriginalPath = originalPath ?? sourcePath;
                if (string.IsNullOrWhiteSpace(item.Name))
                {
                    item.Name = GetDisplayNameForPath(storedPath);
                }
                changed = true;
            }

            return changed;
        }

        private static string RequireExistingPath(string path)
        {
            string normalized = NormalizePath(path);
            if (string.IsNullOrEmpty(normalized) ||
                (!File.Exists(normalized) && !Directory.Exists(normalized)))
            {
                throw new FileNotFoundException(
                    "源文件或文件夹不存在。",
                    path);
            }

            return normalized;
        }

        internal static string ResolveDesktopDirectory()
        {
            string overrideDirectory =
                Environment.GetEnvironmentVariable("GHOSTDESK_TEST_DESKTOP_DIR");
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

            return Environment.GetFolderPath(
                Environment.SpecialFolder.Desktop);
        }

        private static string GetItemName(string path)
        {
            string normalized = path.TrimEnd('\\');
            string name = Directory.Exists(normalized)
                ? new DirectoryInfo(normalized).Name
                : Path.GetFileName(normalized);
            return string.IsNullOrWhiteSpace(name) ? "未命名" : name;
        }

        private static bool Exists(string path)
        {
            return File.Exists(path) || Directory.Exists(path);
        }

        private static void MovePath(string sourcePath, string destinationPath)
        {
            LastMoveWarning = null;
            bool sourceIsDirectory = Directory.Exists(sourcePath);
            string destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (string.IsNullOrWhiteSpace(destinationDirectory))
            {
                throw new IOException("目标路径没有有效的父目录。");
            }

            Directory.CreateDirectory(destinationDirectory);
            if (!RequiresStagedCopy(sourcePath, destinationPath))
            {
                try
                {
                    if (sourceIsDirectory)
                    {
                        Directory.Move(sourcePath, destinationPath);
                    }
                    else
                    {
                        File.Move(sourcePath, destinationPath);
                    }
                    return;
                }
                catch (Exception ex)
                {
                    throw new IOException(
                        "同卷原子移动失败，源文件已保留。",
                        ex);
                }
            }

            CopyManifest manifest = CopyManifest.Capture(sourcePath);
            string temporaryPath = Path.Combine(
                destinationDirectory,
                ".ghostdesk-copy-" + Guid.NewGuid().ToString("N"));
            bool destinationCommitted = false;
            bool sourceIsolationStarted = false;
            try
            {
                int copiedFileCount = 0;
                if (sourceIsDirectory)
                {
                    CopyDirectory(
                        sourcePath,
                        temporaryPath,
                        ref copiedFileCount);
                }
                else
                {
                    File.Copy(sourcePath, temporaryPath, false);
                    copiedFileCount++;
                    MaybeFailTestCopy(copiedFileCount);
                    CopyFileMetadata(sourcePath, temporaryPath);
                }

                manifest.Verify(temporaryPath);
                if (Exists(destinationPath))
                {
                    throw new IOException("目标路径在复制期间已被占用。");
                }

                if (sourceIsDirectory)
                {
                    Directory.Move(temporaryPath, destinationPath);
                }
                else
                {
                    File.Move(temporaryPath, destinationPath);
                }
                destinationCommitted = true;

                manifest.Verify(destinationPath);
                CopyManifest currentSource = CopyManifest.Capture(sourcePath);
                if (!manifest.Equals(currentSource))
                {
                    throw new IOException(
                        "源文件在移动期间发生了变化，已取消删除源文件。");
                }

                string sourceDirectory = Path.GetDirectoryName(sourcePath);
                if (string.IsNullOrWhiteSpace(sourceDirectory))
                {
                    throw new IOException("源路径没有有效的父目录。");
                }

                string quarantinePath = GetUniqueDestination(
                    sourceDirectory,
                    ".ghostdesk-source-" + Guid.NewGuid().ToString("N"));
                if (sourceIsDirectory)
                {
                    Directory.Move(sourcePath, quarantinePath);
                }
                else
                {
                    File.Move(sourcePath, quarantinePath);
                }
                sourceIsolationStarted = true;
                HideQuarantinedSource(quarantinePath);
                if (!IsCleanupFailureSimulated())
                {
                    try
                    {
                        DeletePath(quarantinePath);
                    }
                    catch
                    {
                        TryDeletePath(quarantinePath);
                    }
                }

                if (Exists(quarantinePath))
                {
                    LastMoveWarning =
                        "源路径已隔离，但隔离副本未能删除。请保留此路径，重启后再清理：\n" +
                        quarantinePath;
                }
            }
            catch (Exception ex)
            {
                if (!destinationCommitted)
                {
                    TryDeletePath(temporaryPath);
                    throw new IOException(
                        "移动项目失败，源文件已保留，临时副本已清理。",
                        ex);
                }

                if (!sourceIsolationStarted)
                {
                    TryDeletePath(destinationPath);
                    throw new IOException(
                        "移动项目失败，源文件已保留，目标副本已回滚。",
                        ex);
                }

                throw new IOException(
                    "项目已完整复制并校验到目标路径，但源路径未能完全删除。请不要删除目标副本。\n目标：" +
                    destinationPath,
                    ex);
            }
        }

        private static bool RequiresStagedCopy(
            string sourcePath,
            string destinationPath)
        {
            if (string.Equals(
                    Environment.GetEnvironmentVariable(
                        "GHOSTDESK_TEST_FORCE_COPY_MOVE"),
                    "1",
                    StringComparison.Ordinal))
            {
                return true;
            }

            string sourceVolume = GetVolumePath(sourcePath);
            string destinationVolume = GetVolumePath(destinationPath);
            return string.IsNullOrEmpty(sourceVolume) ||
                string.IsNullOrEmpty(destinationVolume) ||
                !string.Equals(
                    sourceVolume,
                    destinationVolume,
                    StringComparison.OrdinalIgnoreCase);
        }

        private static string GetVolumePath(string path)
        {
            StringBuilder volume = new StringBuilder(1024);
            if (NativeMethods.GetVolumePathName(
                    path,
                    volume,
                    volume.Capacity))
            {
                return volume.ToString();
            }

            return Path.GetPathRoot(Path.GetFullPath(path));
        }

        private static void HideQuarantinedSource(string path)
        {
            try
            {
                FileAttributes attributes = File.GetAttributes(path);
                File.SetAttributes(
                    path,
                    attributes | FileAttributes.Hidden | FileAttributes.System);
            }
            catch
            {
            }
        }

        private static void CopyDirectory(
            string sourceDirectory,
            string destinationDirectory,
            ref int copiedFileCount)
        {
            Directory.CreateDirectory(destinationDirectory);
            foreach (string entry in Directory.GetFileSystemEntries(
                sourceDirectory))
            {
                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new NotSupportedException(
                        "暂不支持移动包含符号链接或目录联接的文件夹：" +
                        entry);
                }

                string destinationEntry = Path.Combine(
                    destinationDirectory,
                    Path.GetFileName(entry));
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    CopyDirectory(
                        entry,
                        destinationEntry,
                        ref copiedFileCount);
                    CopyDirectoryMetadata(entry, destinationEntry);
                }
                else
                {
                    File.Copy(entry, destinationEntry, false);
                    copiedFileCount++;
                    MaybeFailTestCopy(copiedFileCount);
                    CopyFileMetadata(entry, destinationEntry);
                }
            }

            CopyDirectoryMetadata(sourceDirectory, destinationDirectory);
        }

        private static void CopyFileMetadata(string source, string destination)
        {
            File.SetAttributes(destination, File.GetAttributes(source));
            File.SetCreationTimeUtc(
                destination,
                File.GetCreationTimeUtc(source));
            File.SetLastAccessTimeUtc(
                destination,
                File.GetLastAccessTimeUtc(source));
            File.SetLastWriteTimeUtc(
                destination,
                File.GetLastWriteTimeUtc(source));
        }

        private static void CopyDirectoryMetadata(
            string source,
            string destination)
        {
            FileAttributes attributes = File.GetAttributes(source) &
                ~FileAttributes.ReparsePoint;
            File.SetAttributes(destination, attributes);
            Directory.SetCreationTimeUtc(
                destination,
                Directory.GetCreationTimeUtc(source));
            Directory.SetLastAccessTimeUtc(
                destination,
                Directory.GetLastAccessTimeUtc(source));
            Directory.SetLastWriteTimeUtc(
                destination,
                Directory.GetLastWriteTimeUtc(source));
        }

        private static void MaybeFailTestCopy(int copiedFileCount)
        {
            string value = Environment.GetEnvironmentVariable(
                "GHOSTDESK_TEST_FAIL_COPY_AFTER_FILES");
            int failAfter;
            if (int.TryParse(
                    value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out failAfter) &&
                failAfter > 0 &&
                copiedFileCount >= failAfter)
            {
                throw new IOException(
                    "回归测试模拟复制中断。");
            }
        }

        private static bool IsCleanupFailureSimulated()
        {
            return string.Equals(
                Environment.GetEnvironmentVariable(
                    "GHOSTDESK_TEST_FAIL_SOURCE_CLEANUP"),
                "1",
                StringComparison.Ordinal);
        }

        private static void TryDeletePath(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    DeleteTree(path);
                }
                else if (File.Exists(path))
                {
                    File.SetAttributes(path, FileAttributes.Normal);
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        private static void DeletePath(string path)
        {
            if (Directory.Exists(path))
            {
                DeleteTree(path);
            }
            else if (File.Exists(path))
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
            }
        }

        private static void DeleteTree(string directory)
        {
            foreach (string entry in Directory.GetFileSystemEntries(directory))
            {
                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if ((attributes & FileAttributes.ReparsePoint) == 0)
                    {
                        DeleteTree(entry);
                    }
                    else
                    {
                        Directory.Delete(entry, false);
                    }
                }
                else
                {
                    File.SetAttributes(entry, FileAttributes.Normal);
                    File.Delete(entry);
                }
            }

            File.SetAttributes(directory, FileAttributes.Directory);
            Directory.Delete(directory, false);
        }

        private sealed class CopyManifest
        {
            private bool _isDirectory;
            private long _singleFileLength;
            private string _singleFileHash;
            private readonly HashSet<string> _directories =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, FileEntry> _files =
                new Dictionary<string, FileEntry>(
                    StringComparer.OrdinalIgnoreCase);

            internal static CopyManifest Capture(string path)
            {
                CopyManifest manifest = new CopyManifest();
                if (Directory.Exists(path))
                {
                    FileAttributes attributes = File.GetAttributes(path);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new NotSupportedException(
                            "暂不支持移动符号链接或目录联接：" + path);
                    }

                    manifest._isDirectory = true;
                    CopyManifest.CaptureDirectory(
                        path,
                        path,
                        manifest._directories,
                        manifest._files);
                    return manifest;
                }

                if (!File.Exists(path))
                {
                    throw new FileNotFoundException(
                        "源文件或文件夹不存在。",
                        path);
                }

                manifest._singleFileLength =
                    new FileInfo(path).Length;
                manifest._singleFileHash = ComputeFileHash(path);
                return manifest;
            }

            internal void Verify(string path)
            {
                if (_isDirectory)
                {
                    if (!Directory.Exists(path))
                    {
                        throw new IOException("校验失败：目标文件夹不存在。");
                    }

                    HashSet<string> directories =
                        new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    Dictionary<string, FileEntry> files =
                        new Dictionary<string, FileEntry>(
                            StringComparer.OrdinalIgnoreCase);
                    CopyManifest.CaptureDirectory(
                        path,
                        path,
                        directories,
                        files);
                    if (!_directories.SetEquals(directories) ||
                        !FilesEqual(_files, files))
                    {
                        throw new IOException(
                            "校验失败：目标文件夹内容与源文件夹不一致。");
                    }
                    return;
                }

                if (!File.Exists(path))
                {
                    throw new IOException("校验失败：目标文件不存在。");
                }

                FileInfo destination = new FileInfo(path);
                if (destination.Length != _singleFileLength ||
                    !string.Equals(
                        ComputeFileHash(path),
                        _singleFileHash,
                        StringComparison.Ordinal))
                {
                    throw new IOException(
                        "校验失败：目标文件内容与源文件不一致。");
                }
            }

            internal bool Equals(CopyManifest other)
            {
                if (other == null ||
                    _isDirectory != other._isDirectory)
                {
                    return false;
                }

                if (!_isDirectory)
                {
                    return _singleFileLength == other._singleFileLength &&
                        string.Equals(
                            _singleFileHash,
                            other._singleFileHash,
                            StringComparison.Ordinal);
                }

                return _directories.SetEquals(other._directories) &&
                    FilesEqual(_files, other._files);
            }

            private static void CaptureDirectory(
                string root,
                string current,
                HashSet<string> directories,
                Dictionary<string, FileEntry> files)
            {
                foreach (string entry in Directory.GetFileSystemEntries(current))
                {
                    FileAttributes attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new NotSupportedException(
                            "暂不支持移动包含符号链接或目录联接的文件夹：" +
                            entry);
                    }

                    string relative = GetRelativePath(root, entry);
                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        directories.Add(relative);
                        CaptureDirectory(
                            root,
                            entry,
                            directories,
                            files);
                    }
                    else
                    {
                        files[relative] = new FileEntry(
                            new FileInfo(entry).Length,
                            ComputeFileHash(entry));
                    }
                }
            }

            private static bool FilesEqual(
                Dictionary<string, FileEntry> expected,
                Dictionary<string, FileEntry> actual)
            {
                if (expected.Count != actual.Count)
                {
                    return false;
                }

                foreach (KeyValuePair<string, FileEntry> pair in expected)
                {
                    FileEntry value;
                    if (!actual.TryGetValue(pair.Key, out value) ||
                        pair.Value.Length != value.Length ||
                        !string.Equals(
                            pair.Value.Hash,
                            value.Hash,
                            StringComparison.Ordinal))
                    {
                        return false;
                    }
                }

                return true;
            }

            private static string ComputeFileHash(string path)
            {
                using (FileStream stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read))
                using (SHA256 sha256 = SHA256.Create())
                {
                    byte[] hash = sha256.ComputeHash(stream);
                    StringBuilder builder = new StringBuilder(hash.Length * 2);
                    foreach (byte value in hash)
                    {
                        builder.Append(
                            value.ToString(
                                "x2",
                                CultureInfo.InvariantCulture));
                    }
                    return builder.ToString();
                }
            }

            private static string GetRelativePath(string root, string path)
            {
                string fullRoot = Path.GetFullPath(root).TrimEnd('\\');
                string fullPath = Path.GetFullPath(path).TrimEnd('\\');
                if (!fullPath.StartsWith(
                        fullRoot + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException("路径不在预期目录中：" + path);
                }

                return fullPath.Substring(fullRoot.Length + 1)
                    .Replace(Path.DirectorySeparatorChar, '/');
            }

            private sealed class FileEntry
            {
                internal FileEntry(long length, string hash)
                {
                    Length = length;
                    Hash = hash;
                }

                internal long Length { get; private set; }

                internal string Hash { get; private set; }
            }
        }

        private static void NotifyPathChanged(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                NativeMethods.SHChangeNotify(
                    NativeMethods.SHCNE_UPDATEITEM,
                    NativeMethods.SHCNF_PATHW,
                    path,
                    IntPtr.Zero);
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    NativeMethods.SHChangeNotify(
                        NativeMethods.SHCNE_UPDATEDIR,
                        NativeMethods.SHCNF_PATHW,
                        directory,
                        IntPtr.Zero);
                }
            }
            catch
            {
            }
        }
    }
}
