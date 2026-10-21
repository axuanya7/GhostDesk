using System;
using System.Globalization;
using System.Net;
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
                            DownloadUrl = BuildDownloadUrl(
                                release.TagName,
                                remoteVersion),
                            ReleaseNotes = NormalizeNotes(release.Body)
                        }
                    };
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
        }

        private sealed class GitHubRelease
        {
            public string tag_name { get; set; }

            public string html_url { get; set; }

            public string body { get; set; }

            public GitHubAsset[] assets { get; set; }
        }

        private sealed class GitHubAsset
        {
            public string name { get; set; }

            public string browser_download_url { get; set; }
        }
    }
}
