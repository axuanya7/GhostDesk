using System;
using System.Collections.Generic;
using System.IO;

namespace GhostDesk
{
    /// <summary>扫描到的一个可添加项（快捷方式或可执行文件）。</summary>
    internal class ScannedShortcut
    {
        public string Name { get; set; }

        public string Path { get; set; }

        public string Target { get; set; }

        public string Source { get; set; }
    }

    /// <summary>
    /// 扫描开始菜单、桌面上的快捷方式，供「批量添加」列表使用。
    /// 只返回目标仍然存在的项，并按目标路径去重。
    /// </summary>
    internal static class AppScanner
    {
        private const int MaxDepth = 5;

        public static List<ScannedShortcut> Scan()
        {
            List<ScannedShortcut> results = new List<ScannedShortcut>();
            HashSet<string> seenTargets = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<string, string> root in GetRoots())
            {
                try
                {
                    if (!Directory.Exists(root.Key))
                    {
                        continue;
                    }

                    ScanDirectory(
                        root.Key,
                        root.Value,
                        results,
                        seenTargets,
                        0);
                }
                catch
                {
                }
            }

            results.Sort(delegate(ScannedShortcut left, ScannedShortcut right)
            {
                return string.Compare(
                    left.Name,
                    right.Name,
                    StringComparison.CurrentCultureIgnoreCase);
            });

            return results;
        }

        private static List<KeyValuePair<string, string>> GetRoots()
        {
            List<KeyValuePair<string, string>> roots =
                new List<KeyValuePair<string, string>>();

            AddRoot(
                roots,
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    @"Microsoft\Windows\Start Menu\Programs"),
                "开始菜单");
            AddRoot(
                roots,
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    @"Microsoft\Windows\Start Menu\Programs"),
                "开始菜单（公共）");
            AddRoot(
                roots,
                ItemStorage.ResolveDesktopDirectory(),
                "桌面");
            AddRoot(
                roots,
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                "公共桌面");

            return roots;
        }

        private static void AddRoot(
            List<KeyValuePair<string, string>> roots,
            string path,
            string label)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                string full = Path.GetFullPath(path);
                foreach (KeyValuePair<string, string> existing in roots)
                {
                    if (string.Equals(
                            existing.Key,
                            full,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }
                }

                roots.Add(new KeyValuePair<string, string>(full, label));
            }
            catch
            {
            }
        }

        private static void ScanDirectory(
            string directory,
            string source,
            List<ScannedShortcut> results,
            HashSet<string> seenTargets,
            int depth)
        {
            if (depth > MaxDepth)
            {
                return;
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(directory, "*.lnk");
            }
            catch
            {
                files = new string[0];
            }

            foreach (string file in files)
            {
                ScannedShortcut shortcut = CreateFromShortcut(file, source);
                if (shortcut == null)
                {
                    continue;
                }

                if (!seenTargets.Add(shortcut.Target))
                {
                    continue;
                }

                results.Add(shortcut);
            }

            string[] directories;
            try
            {
                directories = Directory.GetDirectories(directory);
            }
            catch
            {
                return;
            }

            foreach (string child in directories)
            {
                try
                {
                    FileAttributes attributes = File.GetAttributes(child);
                    if ((attributes & FileAttributes.Hidden) == FileAttributes.Hidden)
                    {
                        continue;
                    }
                }
                catch
                {
                    continue;
                }

                ScanDirectory(child, source, results, seenTargets, depth + 1);
            }
        }

        private static ScannedShortcut CreateFromShortcut(
            string shortcutPath,
            string source)
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
                if (!File.Exists(target))
                {
                    return null;
                }

                string name = Path.GetFileNameWithoutExtension(shortcutPath);
                if (IsUninstaller(name))
                {
                    return null;
                }

                ScannedShortcut shortcut = new ScannedShortcut();
                shortcut.Name = name;
                shortcut.Path = shortcutPath;
                shortcut.Target = target;
                shortcut.Source = source;
                return shortcut;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsUninstaller(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            return name.IndexOf("卸载", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("uninstall", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("unins", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
