using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace GhostDesk
{
    internal sealed class UpdateInfo
    {
        public string Version { get; set; }

        public string Name { get; set; }

        public string Url { get; set; }

        public string DownloadUrl { get; set; }

        public string ReleaseNotes { get; set; }

        /// <summary>本次汇总覆盖的版本数（例如从 1.2.0 直接升上来可能是 3 个）。</summary>
        public int ReleaseCount { get; set; }

        /// <summary>汇总提示语，例如「你当前是 1.2.0，下面是从 1.3.0 到 1.4.0 共 3 个版本的更新内容。」</summary>
        public string NotesIntro { get; set; }
    }

    internal sealed class UpdateCheckResult
    {
        public UpdateInfo Update { get; set; }

        public Exception Error { get; set; }
    }

    internal static class UpdateChecker
    {
        private const int Tls12Flag = 3072;
        private const int MaxDownloadAttempts = 3;

        public static bool ShouldCheck(AppConfig config)
        {
            if (config == null || config.CheckForUpdates == false)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(config.LastUpdateCheck))
            {
                return true;
            }

            DateTime lastCheck;
            if (!DateTime.TryParseExact(
                config.LastUpdateCheck,
                "yyyyMMdd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out lastCheck))
            {
                return true;
            }

            return lastCheck.Date < DateTime.Now.Date;
        }

        public static void CheckInBackground(Action<UpdateCheckResult> callback)
        {
            if (callback == null)
            {
                return;
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                UpdateCheckResult result = CheckNow();
                callback(result);
            });
        }

        public static UpdateCheckResult CheckNow()
        {
            Exception lastError = null;
            for (int attempt = 1; attempt <= MaxDownloadAttempts; attempt++)
            {
                try
                {
                    return CheckOnce();
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    if (attempt < MaxDownloadAttempts)
                    {
                        Thread.Sleep(attempt == 1 ? 350 : 900);
                    }
                }
            }

            return new UpdateCheckResult
            {
                Error = lastError ?? new InvalidOperationException("GitHub Releases 请求失败。")
            };
        }

        /// <summary>
        /// 取回版本列表，汇总「当前版本之后所有版本」的更新说明；
        /// 列表接口不可用时退回「只看最新一版」的老逻辑。
        /// </summary>
        private static UpdateCheckResult CheckOnce()
        {
            List<ReleaseInfo> releases = null;
            try
            {
                releases = DownloadReleaseList();
            }
            catch
            {
                releases = null;
            }

            if (releases == null || releases.Count == 0)
            {
                return CheckLatestOnly();
            }

            List<KeyValuePair<string, ReleaseInfo>> ordered =
                new List<KeyValuePair<string, ReleaseInfo>>();
            foreach (ReleaseInfo release in releases)
            {
                if (release == null ||
                    release.Draft ||
                    release.Prerelease ||
                    string.IsNullOrWhiteSpace(release.TagName))
                {
                    continue;
                }

                string version = NormalizeVersion(release.TagName);
                if (string.IsNullOrEmpty(version) || version == "0")
                {
                    continue;
                }

                ordered.Add(new KeyValuePair<string, ReleaseInfo>(version, release));
            }

            if (ordered.Count == 0)
            {
                return new UpdateCheckResult();
            }

            ordered.Sort(
                delegate(
                    KeyValuePair<string, ReleaseInfo> left,
                    KeyValuePair<string, ReleaseInfo> right)
                {
                    return CompareVersions(right.Key, left.Key);
                });

            string latestVersion = ordered[0].Key;
            if (CompareVersions(latestVersion, AppInfo.Version) <= 0)
            {
                return new UpdateCheckResult();
            }

            List<KeyValuePair<string, ReleaseInfo>> newer =
                new List<KeyValuePair<string, ReleaseInfo>>();
            foreach (KeyValuePair<string, ReleaseInfo> entry in ordered)
            {
                if (CompareVersions(entry.Key, AppInfo.Version) > 0)
                {
                    newer.Add(entry);
                }
            }

            if (newer.Count == 0)
            {
                return new UpdateCheckResult();
            }

            ReleaseInfo latest = newer[0].Value;
            StringBuilder notes = new StringBuilder();
            foreach (KeyValuePair<string, ReleaseInfo> entry in newer)
            {
                notes.Append("──── GhostDesk ")
                    .Append(entry.Key)
                    .AppendLine(" ────");
                notes.AppendLine(StripLeadingTitle(NormalizeNotes(entry.Value.Body), entry.Key));
                notes.AppendLine();
            }

            string oldest = newer[newer.Count - 1].Key;
            string intro = newer.Count == 1
                ? "下面是 GhostDesk " + latestVersion + " 的更新内容。"
                : "你当前是 " + AppInfo.Version + "，下面是从 " + oldest + " 到 " + latestVersion +
                  " 共 " + newer.Count.ToString() + " 个版本的更新内容（新的在上）。";

            return new UpdateCheckResult
            {
                Update = new UpdateInfo
                {
                    Version = latestVersion,
                    Name = "GhostDesk " + latestVersion,
                    Url = string.IsNullOrWhiteSpace(latest.HtmlUrl)
                        ? AppInfo.ReleasesUrl
                        : latest.HtmlUrl,
                    DownloadUrl = BuildDownloadUrl(latest.TagName, latestVersion),
                    ReleaseNotes = notes.ToString().Trim(),
                    ReleaseCount = newer.Count,
                    NotesIntro = intro
                }
            };
        }

        /// <summary>兜底：拿不到版本列表时只看最新一版。</summary>
        private static UpdateCheckResult CheckLatestOnly()
        {
            ReleaseInfo release = DownloadLatestRelease();
            if (release == null || string.IsNullOrWhiteSpace(release.TagName))
            {
                throw new InvalidOperationException("未能识别 GitHub 最新版本。");
            }

            string remoteVersion = NormalizeVersion(release.TagName);
            if (CompareVersions(remoteVersion, AppInfo.Version) <= 0)
            {
                return new UpdateCheckResult();
            }

            return new UpdateCheckResult
            {
                Update = new UpdateInfo
                {
                    Version = remoteVersion,
                    Name = "GhostDesk " + remoteVersion,
                    Url = string.IsNullOrWhiteSpace(release.HtmlUrl)
                        ? AppInfo.ReleasesUrl
                        : release.HtmlUrl,
                    DownloadUrl = BuildDownloadUrl(release.TagName, remoteVersion),
                    ReleaseNotes = NormalizeNotes(release.Body),
                    ReleaseCount = 1,
                    NotesIntro = "下面是 GhostDesk " + remoteVersion + " 的更新内容。"
                }
            };
        }

        /// <summary>去掉正文里与版本号重复的首行标题（发布说明第一行通常是「GhostDesk 1.4.0」）。</summary>
        private static string StripLeadingTitle(string body, string version)
        {
            if (string.IsNullOrEmpty(body))
            {
                return "";
            }

            string[] lines = body.Split('\n');
            if (lines.Length == 0)
            {
                return body;
            }

            string first = lines[0].Trim();
            if (string.Equals(first, "GhostDesk " + version, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(first, "GhostDesk v" + version, StringComparison.OrdinalIgnoreCase))
            {
                return string.Join("\n", lines, 1, lines.Length - 1).Trim();
            }

            return body;
        }
        private static string BuildDownloadUrl(string tagName, string version)
        {
            string tag = string.IsNullOrWhiteSpace(tagName) ? "v" + version : tagName.Trim();
            string fileName = "GhostDesk-v" + version + "-win-x64.zip";
            return AppInfo.ReleasesUrl + "/download/" + tag + "/" + fileName;
        }

        private static string NormalizeNotes(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            return value.Replace("\r\n", "\n").Trim();
        }

        private static List<ReleaseInfo> DownloadReleaseList()
        {
            ServicePointManager.SecurityProtocol =
                (SecurityProtocolType)((int)ServicePointManager.SecurityProtocol | Tls12Flag);

            HttpWebRequest request =
                (HttpWebRequest)WebRequest.Create(AppInfo.ReleasesApiUrl);
            request.Method = "GET";
            request.UserAgent = "GhostDesk/" + AppInfo.Version;
            request.Accept = "application/vnd.github+json";
            request.Timeout = 12000;
            request.ReadWriteTimeout = 12000;
            request.AllowAutoRedirect = true;

            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            {
                using (System.IO.Stream stream = response.GetResponseStream())
                {
                    if (stream == null)
                    {
                        throw new InvalidOperationException("GitHub 没有返回版本列表。");
                    }

                    using (System.IO.StreamReader reader = new System.IO.StreamReader(stream))
                    {
                        JavaScriptSerializer serializer = new JavaScriptSerializer();
                        serializer.MaxJsonLength = 8 * 1024 * 1024;
                        GitHubRelease[] releases =
                            serializer.Deserialize<GitHubRelease[]>(reader.ReadToEnd());
                        if (releases == null || releases.Length == 0)
                        {
                            throw new InvalidOperationException("GitHub 版本列表为空。");
                        }

                        List<ReleaseInfo> result = new List<ReleaseInfo>();
                        foreach (GitHubRelease release in releases)
                        {
                            if (release == null)
                            {
                                continue;
                            }

                            ReleaseInfo info = new ReleaseInfo();
                            info.TagName = release.tag_name;
                            info.HtmlUrl = string.IsNullOrWhiteSpace(release.html_url)
                                ? AppInfo.ReleasesUrl
                                : release.html_url;
                            info.Body = release.body;
                            info.Assets = release.assets;
                            info.Draft = release.draft;
                            info.Prerelease = release.prerelease;
                            result.Add(info);
                        }

                        return result;
                    }
                }
            }
        }

        private static ReleaseInfo DownloadLatestRelease()
        {
            ServicePointManager.SecurityProtocol =
                (SecurityProtocolType)((int)ServicePointManager.SecurityProtocol | Tls12Flag);

            HttpWebRequest request =
                (HttpWebRequest)WebRequest.Create(AppInfo.LatestReleaseApiUrl);
            request.Method = "GET";
            request.UserAgent = "GhostDesk/" + AppInfo.Version;
            request.Accept = "application/vnd.github+json";
            request.Timeout = 12000;
            request.ReadWriteTimeout = 12000;
            request.AllowAutoRedirect = true;

            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            {
                using (System.IO.Stream stream = response.GetResponseStream())
                {
                    if (stream == null)
                    {
                        throw new InvalidOperationException("GitHub 没有返回版本数据。");
                    }

                    using (System.IO.StreamReader reader = new System.IO.StreamReader(stream))
                    {
                        JavaScriptSerializer serializer = new JavaScriptSerializer();
                        serializer.MaxJsonLength = 4 * 1024 * 1024;
                        GitHubRelease release = serializer.Deserialize<GitHubRelease>(reader.ReadToEnd());
                        if (release == null)
                        {
                            throw new InvalidOperationException("GitHub 版本数据解析失败。");
                        }

                        ReleaseInfo result = new ReleaseInfo();
                        result.TagName = release.tag_name;
                        result.HtmlUrl = string.IsNullOrWhiteSpace(release.html_url)
                            ? AppInfo.ReleasesUrl
                            : release.html_url;
                        result.Body = release.body;
                        result.Assets = release.assets;
                        return result;
                    }
                }
            }
        }

        private static string ParseTagFromUrl(string value)
        {
            Uri url;
            if (string.IsNullOrWhiteSpace(value) ||
                !Uri.TryCreate(value, UriKind.Absolute, out url))
            {
                return "";
            }

            string path = url.AbsolutePath;
            int marker = path.IndexOf("/releases/tag/", StringComparison.OrdinalIgnoreCase);
            if (marker < 0)
            {
                return "";
            }

            string tag = Uri.UnescapeDataString(
                path.Substring(marker + "/releases/tag/".Length));
            int query = tag.IndexOf('?');
            if (query >= 0)
            {
                tag = tag.Substring(0, query);
            }

            return tag.Trim();
        }

        private static string NormalizeVersion(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "0";
            }

            value = value.Trim();
            if (value[0] == 'v' || value[0] == 'V')
            {
                value = value.Substring(1);
            }

            int dash = value.IndexOf('-');
            if (dash >= 0)
            {
                value = value.Substring(0, dash);
            }

            int plus = value.IndexOf('+');
            if (plus >= 0)
            {
                value = value.Substring(0, plus);
            }

            return value.Trim();
        }

        private static int CompareVersions(string left, string right)
        {
            int[] leftParts = ParseVersion(left);
            int[] rightParts = ParseVersion(right);

            for (int i = 0; i < Math.Max(leftParts.Length, rightParts.Length); i++)
            {
                int a = i < leftParts.Length ? leftParts[i] : 0;
                int b = i < rightParts.Length ? rightParts[i] : 0;
                if (a != b)
                {
                    return a.CompareTo(b);
                }
            }

            return 0;
        }

        private static int[] ParseVersion(string value)
        {
            value = NormalizeVersion(value);
            string[] pieces = value.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            int[] result = new int[Math.Max(1, pieces.Length)];

            for (int i = 0; i < pieces.Length; i++)
            {
                int parsed;
                if (int.TryParse(pieces[i], NumberStyles.None, CultureInfo.InvariantCulture, out parsed))
                {
                    result[i] = parsed;
                }
            }

            return result;
        }

        private sealed class ReleaseInfo
        {
            public string TagName { get; set; }

            public string HtmlUrl { get; set; }

            public string Body { get; set; }

            public GitHubAsset[] Assets { get; set; }

            public bool Draft { get; set; }

            public bool Prerelease { get; set; }
        }

        private sealed class GitHubRelease
        {
            public string tag_name { get; set; }

            public string html_url { get; set; }

            public string body { get; set; }

            public GitHubAsset[] assets { get; set; }

            public bool draft { get; set; }

            public bool prerelease { get; set; }
        }

        private sealed class GitHubAsset
        {
            public string name { get; set; }

            public string browser_download_url { get; set; }
        }
    }
}
