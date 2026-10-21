using System;

namespace GhostDesk
{
    internal static class AppInfo
    {
        public const string Version = "1.2.1";
        public const string GitHubOwner = "axuanya7";
        public const string GitHubRepo = "GhostDesk";

        public const string ReleasesUrl =
            "https://github.com/" + GitHubOwner + "/" + GitHubRepo + "/releases";

        public const string LatestReleasePageUrl =
            "https://github.com/" + GitHubOwner + "/" + GitHubRepo + "/releases/latest";

        public const string LatestReleaseApiUrl =
            "https://api.github.com/repos/" + GitHubOwner + "/" + GitHubRepo + "/releases/latest";
    }
}
