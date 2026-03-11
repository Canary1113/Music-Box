using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace 音乐魔盒.Services
{
    public sealed class AppBuildInfo
    {
        public string VersionDisplay { get; init; } = string.Empty;
        public string VersionCode { get; init; } = string.Empty;
        public string BuildNumber { get; init; } = string.Empty;
        public string CommitHash { get; init; } = string.Empty;
    }

    public static class AppBuildInfoService
    {
        public const string VersionDisplay = "26H2";
        public const string VersionCode = "262001";

        public static AppBuildInfo GetCurrent()
        {
            string? repoRoot = TryFindRepoRoot();
            string commitDate = GetFallbackCommitDate();
            string commitCount = "0";
            string commitHash = string.Empty;

            if (!string.IsNullOrWhiteSpace(repoRoot))
            {
                commitDate = RunGit(repoRoot, "log", "-1", "--date=format:%m%d", "--format=%cd") ?? commitDate;
                commitCount = RunGit(repoRoot, "rev-list", "--count", "HEAD") ?? commitCount;
                commitHash = RunGit(repoRoot, "rev-parse", "--short=8", "HEAD") ?? string.Empty;
            }

            commitDate = NormalizeCommitDate(commitDate);
            commitCount = NormalizeCommitCount(commitCount);
            string buildNumber = $"{VersionCode}.{commitDate}{commitCount}";

            return new AppBuildInfo
            {
                VersionDisplay = VersionDisplay,
                VersionCode = VersionCode,
                BuildNumber = buildNumber,
                CommitHash = commitHash
            };
        }

        private static string? TryFindRepoRoot()
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string candidate in GetCandidateDirectories())
            {
                var current = new DirectoryInfo(candidate);
                while (current != null)
                {
                    if (!visited.Add(current.FullName))
                    {
                        current = current.Parent;
                        continue;
                    }

                    string dotGitPath = Path.Combine(current.FullName, ".git");
                    if (Directory.Exists(dotGitPath) || File.Exists(dotGitPath))
                    {
                        return current.FullName;
                    }

                    current = current.Parent;
                }
            }

            return null;
        }

        private static IEnumerable<string> GetCandidateDirectories()
        {
            if (!string.IsNullOrWhiteSpace(AppContext.BaseDirectory))
            {
                yield return AppContext.BaseDirectory;
            }

            if (!string.IsNullOrWhiteSpace(Environment.CurrentDirectory))
            {
                yield return Environment.CurrentDirectory;
            }
        }

        private static string? RunGit(string repoRoot, params string[] arguments)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    WorkingDirectory = repoRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                foreach (string argument in arguments)
                {
                    startInfo.ArgumentList.Add(argument);
                }

                using Process? process = Process.Start(startInfo);
                if (process == null)
                {
                    return null;
                }

                if (!process.WaitForExit(2000))
                {
                    try
                    {
                        process.Kill();
                    }
                    catch
                    {
                    }

                    return null;
                }

                string output = process.StandardOutput.ReadToEnd().Trim();
                if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
                {
                    return null;
                }

                return output;
            }
            catch
            {
                return null;
            }
        }

        private static string GetFallbackCommitDate()
        {
            try
            {
                string assemblyPath = typeof(AppBuildInfoService).Assembly.Location;
                return File.GetLastWriteTime(assemblyPath).ToString("MMdd");
            }
            catch
            {
                return DateTime.Now.ToString("MMdd");
            }
        }

        private static string NormalizeCommitDate(string raw)
        {
            string digits = new string((raw ?? string.Empty).Where(char.IsDigit).ToArray());
            if (digits.Length >= 4)
            {
                return digits.Substring(0, 4);
            }

            return GetFallbackCommitDate();
        }

        private static string NormalizeCommitCount(string raw)
        {
            string digits = new string((raw ?? string.Empty).Where(char.IsDigit).ToArray());
            return string.IsNullOrWhiteSpace(digits) ? "0" : digits;
        }
    }
}
