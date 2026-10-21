using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace GhostDesk
{
    internal static class ShortcutPathRepair
    {
        internal static bool RepairMissingShortcutItems(List<RegionConfig> regions)
        {
            if (regions == null)
            {
                return false;
            }

            List<string> usedPaths = CollectExistingShortcutPaths(regions);
            bool repaired = false;

            foreach (RegionConfig region in regions)
            {
                if (region == null)
                {
                    continue;
                }

                repaired |= RepairItems(region.Items, usedPaths);
                repaired |= RepairItems(region.LaunchItems, usedPaths);
            }

            return repaired;
        }

        private static bool RepairItems(
            List<DesktopItem> items,
            List<string> usedPaths)
        {
            if (items == null)
            {
                return false;
            }

            bool repaired = false;
            foreach (DesktopItem item in items)
            {
                string replacement = FindReplacement(item, usedPaths);
                if (string.IsNullOrEmpty(replacement))
                {
                    continue;
                }

                item.Path = replacement;
                item.Type = "shortcut";
                item.ExternalReference = false;
                usedPaths.Add(replacement);
                repaired = true;
            }

            return repaired;
        }

        private static List<string> CollectExistingShortcutPaths(
            List<RegionConfig> regions)
        {
            List<string> paths = new List<string>();
            foreach (RegionConfig region in regions)
            {
                if (region == null)
                {
                    continue;
                }

                AddExistingShortcutPaths(region.Items, paths);
                AddExistingShortcutPaths(region.LaunchItems, paths);
            }

            return paths;
        }

        private static void AddExistingShortcutPaths(
            List<DesktopItem> items,
            List<string> paths)
        {
            if (items == null)
            {
                return;
            }

            foreach (DesktopItem item in items)
            {
                if (item == null ||
                    string.IsNullOrWhiteSpace(item.Path) ||
                    !item.Path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ||
                    !File.Exists(item.Path))
                {
                    continue;
                }

                string normalized = NormalizePath(item.Path);
                if (!string.IsNullOrEmpty(normalized) &&
                    !ContainsPath(paths, normalized))
                {
                    paths.Add(normalized);
                }
            }
        }

        private static string FindReplacement(
            DesktopItem item,
            List<string> usedPaths)
        {
            if (item == null ||
                string.IsNullOrWhiteSpace(item.Path) ||
                !item.Path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ||
                File.Exists(item.Path))
            {
                return null;
            }

            string missingPath = NormalizePath(item.Path);
            if (string.IsNullOrEmpty(missingPath))
            {
                return null;
            }

            string directory = Path.GetDirectoryName(missingPath);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                return null;
            }

            string[] candidates;
            try
            {
                candidates = Directory.GetFiles(
                    directory,
                    "*.lnk",
                    SearchOption.TopDirectoryOnly);
            }
            catch
            {
                return null;
            }

            Array.Sort(candidates, StringComparer.OrdinalIgnoreCase);

            string bestPath = null;
            int bestScore = -1;
            bool bestTie = false;
            foreach (string candidate in candidates)
            {
                string normalizedCandidate = NormalizePath(candidate);
                if (string.IsNullOrEmpty(normalizedCandidate) ||
                    ContainsPath(usedPaths, normalizedCandidate))
                {
                    continue;
                }

                int score = GetNameScore(item, missingPath, normalizedCandidate);
                if (score > bestScore)
                {
                    bestPath = normalizedCandidate;
                    bestScore = score;
                    bestTie = false;
                }
                else if (score == bestScore)
                {
                    bestTie = true;
                }
            }

            if (bestScore > 0 && !bestTie)
            {
                return bestPath;
            }

            return null;
        }

        private static int GetNameScore(
            DesktopItem item,
            string missingPath,
            string candidatePath)
        {
            string desired = string.IsNullOrWhiteSpace(item.Name)
                ? Path.GetFileNameWithoutExtension(missingPath)
                : item.Name.Trim();
            string candidate = Path.GetFileNameWithoutExtension(candidatePath);
            string normalizedDesired = NormalizeName(desired);
            string normalizedCandidate = NormalizeName(candidate);

            if (string.IsNullOrEmpty(normalizedDesired) ||
                string.IsNullOrEmpty(normalizedCandidate))
            {
                return 0;
            }

            if (string.Equals(
                    normalizedDesired,
                    normalizedCandidate,
                    StringComparison.OrdinalIgnoreCase))
            {
                return 10000;
            }

            if (normalizedDesired.StartsWith(
                    normalizedCandidate,
                    StringComparison.OrdinalIgnoreCase) ||
                normalizedCandidate.StartsWith(
                    normalizedDesired,
                    StringComparison.OrdinalIgnoreCase))
            {
                return 5000 + Math.Min(
                    normalizedDesired.Length,
                    normalizedCandidate.Length);
            }

            if (normalizedDesired.IndexOf(
                    normalizedCandidate,
                    StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalizedCandidate.IndexOf(
                    normalizedDesired,
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 3000 + Math.Min(
                    normalizedDesired.Length,
                    normalizedCandidate.Length);
            }

            return 0;
        }

        private static string NormalizeName(string value)
        {
            StringBuilder result = new StringBuilder(value == null ? 0 : value.Length);
            if (!string.IsNullOrEmpty(value))
            {
                foreach (char character in value)
                {
                    if (char.IsLetterOrDigit(character))
                    {
                        result.Append(char.ToLowerInvariant(character));
                    }
                }
            }

            return result.ToString();
        }

        private static string NormalizePath(string path)
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

        private static bool ContainsPath(List<string> paths, string path)
        {
            foreach (string existing in paths)
            {
                if (string.Equals(existing, path, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
