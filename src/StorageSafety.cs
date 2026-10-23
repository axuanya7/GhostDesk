using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace GhostDesk
{
    /// <summary>
    /// 收纳落盘的安全网：Storage 目录可用性、文件占用探测、公共桌面权限、
    /// 属性隐藏提权、以及独立于 config.json 的「隐藏清单（manifest）」与孤儿恢复。
    /// 全部为 .NET Framework 4.0 / C# 5.0 语法。
    /// </summary>
    internal static class StorageSafety
    {
        // ---------- Storage 目录可用性 ----------

        /// <summary>危险目标：把收纳目录放在这些位置会毁掉数据。</summary>
        internal static bool IsDangerousDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return true;
            }

            string full;
            try
            {
                full = Path.GetFullPath(path).TrimEnd('\\');
            }
            catch
            {
                return true;
            }

            // 根目录（C:\、D:\ ...）
            if (full.Length <= 3)
            {
                return true;
            }

            if (string.Equals(full, Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(full, Environment.GetFolderPath(Environment.SpecialFolder.System).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(full, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // 桌面本身（把桌面收进桌面会递归）
            foreach (string desktop in GetDesktopDirectories())
            {
                if (string.Equals(full, desktop.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>确认目录可创建且可写；失败给出可读原因。</summary>
        internal static bool ValidateStorageDirectory(string path, out string reason)
        {
            reason = null;

            if (IsDangerousDirectory(path))
            {
                reason = "收纳目录不能是磁盘根目录、Windows 目录、用户目录或桌面本身。";
                return false;
            }

            try
            {
                Directory.CreateDirectory(path);
            }
            catch (UnauthorizedAccessException)
            {
                reason = "没有写权限，无法创建收纳目录：" + path + "（若程序装在 Program Files，请把收纳目录改到用户目录）";
                return false;
            }
            catch (Exception ex)
            {
                reason = "无法创建收纳目录：" + path + " — " + ex.Message;
                return false;
            }

            try
            {
                string probe = Path.Combine(path, ".write-probe");
                File.WriteAllText(probe, "ok", new UTF8Encoding(false));
                File.Delete(probe);
                return true;
            }
            catch (Exception ex)
            {
                reason = "收纳目录不可写：" + path + " — " + ex.Message;
                return false;
            }
        }

        /// <summary>建议的回退目录（%LOCALAPPDATA%\GhostDesk\Storage）。</summary>
        internal static string SuggestedFallbackDirectory()
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(root, "GhostDesk", "Storage");
        }

        // ---------- Explorer「显示隐藏文件」开关 ----------

        private const string ExplorerAdvancedKey =
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

        /// <summary>读资源管理器「隐藏文件和文件夹」设置：1=显示, 2=不显示（默认）。</summary>
        internal static int ReadExplorerHidden()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(ExplorerAdvancedKey))
                {
                    if (key == null)
                    {
                        return 2;
                    }

                    object value = key.GetValue("Hidden");
                    if (value is int)
                    {
                        return (int)value;
                    }

                    return 2;
                }
            }
            catch
            {
                return 2;
            }
        }

        /// <summary>
        /// 改资源管理器「隐藏文件和文件夹」设置并刷新桌面。
        /// 只动系统设置，不动任何文件属性，因此隐藏项会以半透明样式出现。
        /// </summary>
        internal static bool SetExplorerHidden(int value)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(ExplorerAdvancedKey))
                {
                    if (key == null)
                    {
                        return false;
                    }

                    key.SetValue("Hidden", value, RegistryValueKind.DWord);
                }

                RefreshDesktop(true);
                return ReadExplorerHidden() == value;
            }
            catch
            {
                return false;
            }
        }

        // ---------- 桌面刷新 ----------

        private const uint SHCNE_ASSOCCHANGED = 0x08000000;
        private const uint SHCNE_UPDATEDIR = 0x00001000;
        private const uint SHCNF_IDLIST = 0x0000;
        private const uint SHCNF_PATHW = 0x0005;
        private const int WM_COMMAND = 0x0111;
        private const int DESKTOP_REFRESH_COMMAND = 0x7103;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern void SHChangeNotify(uint wEventId, uint uFlags, string dwItem1, IntPtr dwItem2);

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindowW(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindowExW(IntPtr hWndParent, IntPtr hWndChildAfter, string lpszClass, string lpszWindow);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostMessageW(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private static readonly object RefreshLock = new object();
        private static DateTime _lastDesktopRefresh = DateTime.MinValue;

        /// <summary>
        /// 让桌面立刻反映属性变化。
        /// SHCNE_UPDATEITEM 对桌面图标无效，必须用 ASSOCCHANGED + 直接刷新 SHELLDLL_DefView。
        /// force=false 时 400ms 内只刷一次，避免批量操作闪烁。
        /// </summary>
        internal static void RefreshDesktop(bool force)
        {
            lock (RefreshLock)
            {
                DateTime now = DateTime.Now;
                if (!force &&
                    (now - _lastDesktopRefresh).TotalMilliseconds < 400.0)
                {
                    return;
                }

                _lastDesktopRefresh = now;
            }

            try
            {
                SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);

                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (!string.IsNullOrEmpty(desktop))
                {
                    SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_PATHW, desktop, IntPtr.Zero);
                }

                IntPtr progman = FindWindowW("Progman", null);
                if (progman == IntPtr.Zero)
                {
                    return;
                }

                IntPtr defView = FindWindowExW(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (defView == IntPtr.Zero)
                {
                    // Win10/11 桌面图标可能挂在 WorkerW 下
                    IntPtr workerW = IntPtr.Zero;
                    while (true)
                    {
                        workerW = FindWindowExW(IntPtr.Zero, workerW, "WorkerW", null);
                        if (workerW == IntPtr.Zero)
                        {
                            break;
                        }

                        defView = FindWindowExW(workerW, IntPtr.Zero, "SHELLDLL_DefView", null);
                        if (defView != IntPtr.Zero)
                        {
                            break;
                        }
                    }
                }

                if (defView != IntPtr.Zero)
                {
                    PostMessageW(defView, WM_COMMAND, new IntPtr(DESKTOP_REFRESH_COMMAND), IntPtr.Zero);
                }
            }
            catch
            {
            }
        }

        // ---------- 桌面路径 ----------

        internal static List<string> GetDesktopDirectories()
        {
            List<string> result = new List<string>();

            string user = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (!string.IsNullOrEmpty(user))
            {
                result.Add(user);
            }

            string common = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
            if (!string.IsNullOrEmpty(common))
            {
                result.Add(common);
            }

            return result;
        }

        /// <summary>该路径是否位于「公共桌面」（普通用户通常无权修改）。</summary>
        internal static bool IsOnPublicDesktop(string path)
        {
            string common = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
            if (string.IsNullOrEmpty(common) || string.IsNullOrEmpty(path))
            {
                return false;
            }

            string full = Path.GetFullPath(path);
            string root = Path.GetFullPath(common).TrimEnd('\\') + Path.DirectorySeparatorChar;
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>该路径是否位于用户桌面。</summary>
        internal static bool IsOnUserDesktop(string path)
        {
            string desktop = ItemStorage.ResolveDesktopDirectory();
            if (string.IsNullOrEmpty(desktop) || string.IsNullOrEmpty(path))
            {
                return false;
            }

            string full = Path.GetFullPath(path);
            string root = Path.GetFullPath(desktop).TrimEnd('\\') + Path.DirectorySeparatorChar;
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }

        // ---------- 占用探测 ----------

        /// <summary>
        /// 移动前探测占用：能独占打开说明空闲；否则多半被编辑器/预览/索引/杀软占用。
        /// 目录只做浅层抽样（最多 20 个文件）。
        /// </summary>
        internal static bool TryProbeFree(string path, out string reason)
        {
            reason = null;

            try
            {
                if (Directory.Exists(path))
                {
                    string[] files = Directory.GetFiles(path);
                    int probed = 0;
                    foreach (string file in files)
                    {
                        if (probed >= 20)
                        {
                            break;
                        }

                        probed++;
                        string innerReason;
                        if (!TryProbeFreeFile(file, out innerReason))
                        {
                            reason = "目录内文件被占用：" + Path.GetFileName(file);
                            return false;
                        }
                    }

                    return true;
                }

                if (!File.Exists(path))
                {
                    reason = "文件不存在。";
                    return false;
                }

                if (Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    return true;   // 快捷方式几乎不会被独占
                }

                return TryProbeFreeFile(path, out reason);
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                return false;
            }
        }

        private static bool TryProbeFreeFile(string path, out string reason)
        {
            reason = null;

            try
            {
                using (FileStream stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    1,
                    FileOptions.None))
                {
                    stream.ReadByte();
                }

                return true;
            }
            catch (UnauthorizedAccessException)
            {
                reason = "没有权限访问。";
                return false;
            }
            catch (IOException)
            {
                reason = "正被其它程序占用。";
                return false;
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                return false;
            }
        }

        // ---------- 属性隐藏（提权 + HIDDEN|SYSTEM） ----------

        private const uint FILE_ATTRIBUTE_HIDDEN = 0x2;
        private const uint FILE_ATTRIBUTE_SYSTEM = 0x4;
        private const uint INVALID_FILE_ATTRIBUTES = 0xFFFFFFFF;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFileAttributesW(string lpFileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetFileAttributesW(string lpFileName, uint dwFileAttributes);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool LookupPrivilegeValueW(string systemName, string name, out long luid);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AdjustTokenPrivileges(
            IntPtr tokenHandle,
            [MarshalAs(UnmanagedType.Bool)] bool disableAllPrivileges,
            ref TOKEN_PRIVILEGES newState,
            int bufferLength,
            IntPtr previousState,
            IntPtr returnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [StructLayout(LayoutKind.Sequential)]
        private struct TOKEN_PRIVILEGES
        {
            public int PrivilegeCount;
            public long Luid;
            public int Attributes;
        }

        private const uint TOKEN_ADJUST_PRIVILEGES = 0x20;
        private const uint TOKEN_QUERY = 0x8;
        private const int SE_PRIVILEGE_ENABLED = 0x2;
        private const string SE_RESTORE_NAME = "SeRestorePrivilege";

        /// <summary>启用 SeRestorePrivilege，用于给文件加 SYSTEM 属性。</summary>
        internal static bool EnableRestorePrivilege()
        {
            IntPtr token = IntPtr.Zero;

            try
            {
                if (!OpenProcessToken(
                    GetCurrentProcess(),
                    TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY,
                    out token))
                {
                    return false;
                }

                long luid;
                if (!LookupPrivilegeValueW(null, SE_RESTORE_NAME, out luid))
                {
                    return false;
                }

                TOKEN_PRIVILEGES privileges = new TOKEN_PRIVILEGES();
                privileges.PrivilegeCount = 1;
                privileges.Luid = luid;
                privileges.Attributes = SE_PRIVILEGE_ENABLED;

                return AdjustTokenPrivileges(
                    token,
                    false,
                    ref privileges,
                    0,
                    IntPtr.Zero,
                    IntPtr.Zero);
            }
            catch
            {
                return false;
            }
            finally
            {
                if (token != IntPtr.Zero)
                {
                    CloseHandle(token);
                }
            }
        }

        /// <summary>
        /// 给文件/目录加 隐藏(HIDDEN)+可选 系统(SYSTEM)。
        /// 与 File.SetAttributes 的区别：能写 SYSTEM 位、失败时返回原因，且不抛异常。
        /// </summary>
        internal static bool SetHiddenAttribute(string path, bool includeSystem, out string reason)
        {
            reason = null;

            try
            {
                uint attributes = GetFileAttributesW(path);
                if (attributes == INVALID_FILE_ATTRIBUTES)
                {
                    reason = "读取属性失败（Win32 错误 " + Marshal.GetLastWin32Error().ToString(CultureInfo.InvariantCulture) + "）。";
                    return false;
                }

                uint wanted = attributes | FILE_ATTRIBUTE_HIDDEN;
                if (includeSystem)
                {
                    wanted |= FILE_ATTRIBUTE_SYSTEM;
                    EnableRestorePrivilege();
                }
                else
                {
                    // v1.2.1 及更早版本会用 HIDDEN+SYSTEM。带 SYSTEM 的文件即使勾了
                    // 「显示隐藏的项目」也不会显示，会让「显示隐藏文件」开关失效，所以要清掉。
                    wanted &= ~FILE_ATTRIBUTE_SYSTEM;
                }

                if (wanted == attributes)
                {
                    return true;
                }

                if (!SetFileAttributesW(path, wanted))
                {
                    int error = Marshal.GetLastWin32Error();

                    // SYSTEM 位写不进去时退一步只写 HIDDEN
                    if (includeSystem && (attributes | FILE_ATTRIBUTE_HIDDEN) != attributes)
                    {
                        if (SetFileAttributesW(path, attributes | FILE_ATTRIBUTE_HIDDEN))
                        {
                            reason = "已隐藏（SYSTEM 属性未写入，Win32 错误 " + error.ToString(CultureInfo.InvariantCulture) + "）。";
                            return true;
                        }
                    }

                    reason = "设置隐藏属性失败（Win32 错误 " + error.ToString(CultureInfo.InvariantCulture) + "）。";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                return false;
            }
        }

        /// <summary>清除 隐藏/系统 属性（放回桌面时用）。</summary>
        internal static bool ClearHiddenAttribute(string path)
        {
            try
            {
                uint attributes = GetFileAttributesW(path);
                if (attributes == INVALID_FILE_ATTRIBUTES)
                {
                    return false;
                }

                uint cleaned = attributes & ~(FILE_ATTRIBUTE_HIDDEN | FILE_ATTRIBUTE_SYSTEM);
                if (cleaned == attributes)
                {
                    return true;
                }

                return SetFileAttributesW(path, cleaned);
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// 独立于 config.json 的「收纳清单」：记录 Storage 里的路径 ↔ 原桌面路径。
    /// config.json 损坏或被重置时，靠它仍能把文件认领回桌面。
    /// </summary>
    internal sealed class StorageManifest
    {
        internal sealed class Entry
        {
            public string StoredPath;
            public string OriginalPath;
            public bool IsDirectory;
            public DateTime Time;
        }

        private readonly object _lock = new object();
        private readonly Dictionary<string, Entry> _entries =
            new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private readonly string _manifestPath;
        private bool _loaded;

        internal StorageManifest(string manifestPath)
        {
            _manifestPath = manifestPath;
        }

        internal static string DefaultPath(string storageDirectory)
        {
            return Path.Combine(
                Path.GetDirectoryName(storageDirectory.TrimEnd('\\')),
                "storage-manifest.tsv");
        }

        internal void Load()
        {
            lock (_lock)
            {
                if (_loaded)
                {
                    return;
                }

                _loaded = true;
                _entries.Clear();

                if (string.IsNullOrEmpty(_manifestPath) || !File.Exists(_manifestPath))
                {
                    return;
                }

                try
                {
                    foreach (string line in File.ReadAllLines(_manifestPath, Encoding.UTF8))
                    {
                        if (string.IsNullOrWhiteSpace(line))
                        {
                            continue;
                        }

                        string[] parts = line.Split('\t');
                        if (parts.Length < 4)
                        {
                            continue;
                        }

                        Entry entry = new Entry();
                        entry.StoredPath = parts[0];
                        entry.OriginalPath = parts[1];
                        entry.IsDirectory = parts[2] == "1";

                        long ticks;
                        if (long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out ticks))
                        {
                            entry.Time = new DateTime(ticks);
                        }

                        if (!string.IsNullOrEmpty(entry.StoredPath))
                        {
                            _entries[entry.StoredPath] = entry;
                        }
                    }
                }
                catch
                {
                    // 清单损坏不影响主流程
                }
            }
        }

        internal void Add(string storedPath, string originalPath, bool isDirectory)
        {
            lock (_lock)
            {
                Load();

                Entry entry = new Entry();
                entry.StoredPath = storedPath;
                entry.OriginalPath = originalPath;
                entry.IsDirectory = isDirectory;
                entry.Time = DateTime.Now;
                _entries[storedPath] = entry;
                Save_NoLock();
            }
        }

        internal void Remove(string storedPath)
        {
            lock (_lock)
            {
                Load();

                if (_entries.Remove(storedPath))
                {
                    Save_NoLock();
                }
            }
        }

        internal Entry FindByStored(string storedPath)
        {
            lock (_lock)
            {
                Load();

                Entry entry;
                if (_entries.TryGetValue(storedPath, out entry))
                {
                    return entry;
                }

                return null;
            }
        }

        internal List<Entry> Snapshot()
        {
            lock (_lock)
            {
                Load();

                List<Entry> list = new List<Entry>();
                foreach (KeyValuePair<string, Entry> pair in _entries)
                {
                    list.Add(pair.Value);
                }

                return list;
            }
        }

        /// <summary>
        /// 找出 Storage 里「有文件但清单没记录」的孤儿条目。
        /// knownPaths 传入当前 config 中所有已收纳路径（Storage 路径）。
        /// </summary>
        internal List<Entry> FindOrphans(string storageDirectory, ICollection<string> knownPaths)
        {
            List<Entry> orphans = new List<Entry>();

            if (string.IsNullOrEmpty(storageDirectory) || !Directory.Exists(storageDirectory))
            {
                return orphans;
            }

            lock (_lock)
            {
                Load();
            }

            try
            {
                foreach (string path in Directory.GetFileSystemEntries(storageDirectory))
                {
                    bool isDirectory = Directory.Exists(path);

                    if (knownPaths != null && knownPaths.Contains(path))
                    {
                        continue;
                    }

                    Entry entry;
                    if (!_entries.TryGetValue(path, out entry))
                    {
                        entry = new Entry();
                        entry.StoredPath = path;
                        entry.IsDirectory = isDirectory;
                        entry.Time = File.GetLastWriteTime(path);
                    }

                    orphans.Add(entry);
                }
            }
            catch
            {
            }

            return orphans;
        }

        private void Save_NoLock()
        {
            if (string.IsNullOrEmpty(_manifestPath))
            {
                return;
            }

            try
            {
                string directory = Path.GetDirectoryName(_manifestPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                StringBuilder builder = new StringBuilder();
                foreach (KeyValuePair<string, Entry> pair in _entries)
                {
                    Entry entry = pair.Value;
                    builder.Append(entry.StoredPath);
                    builder.Append('\t');
                    builder.Append(entry.OriginalPath);
                    builder.Append('\t');
                    builder.Append(entry.IsDirectory ? "1" : "0");
                    builder.Append('\t');
                    builder.Append(entry.Time.Ticks.ToString(CultureInfo.InvariantCulture));
                    builder.Append("\r\n");
                }

                string temporary = _manifestPath + ".tmp";
                File.WriteAllText(temporary, builder.ToString(), new UTF8Encoding(false));

                if (File.Exists(_manifestPath))
                {
                    File.Delete(_manifestPath);
                }

                File.Move(temporary, _manifestPath);
            }
            catch
            {
            }
        }
    }
}
