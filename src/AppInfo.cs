using System;

namespace GhostDesk
{
    internal static class AppInfo
    {
        public const string Version = "1.4.5";
        public const string GitHubOwner = "axuanya7";
        public const string GitHubRepo = "GhostDesk";

        public const string ReleasesUrl =
            "https://github.com/" + GitHubOwner + "/" + GitHubRepo + "/releases";

        public const string LatestReleasePageUrl =
            "https://github.com/" + GitHubOwner + "/" + GitHubRepo + "/releases/latest";

        public const string LatestReleaseApiUrl =
            "https://api.github.com/repos/" + GitHubOwner + "/" + GitHubRepo + "/releases/latest";

        /// <summary>版本列表接口：一次取回多个版本，用来汇总"从你当前版本之后的所有更新"。</summary>
        public const string ReleasesApiUrl =
            "https://api.github.com/repos/" + GitHubOwner + "/" + GitHubRepo + "/releases?per_page=30";
    }
}
