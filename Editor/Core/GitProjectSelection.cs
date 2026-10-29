using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GitVersionControl
{
    /// <summary>
    /// Project 창에서 고른 에셋·폴더를 git 변경 목록의 항목으로 바꾼다(<see cref="GitProjectMenu"/>).
    /// 고른 에셋의 .meta, 고른 폴더 아래 전부와 폴더 자신의 .meta 까지 한 묶음으로 본다 — Project 창에는 .meta 가 보이지 않으니 따로 고를 수 없다.
    /// </summary>
    public static class GitProjectSelection
    {
        private static StringComparison PathComparison =>
            Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        /// <summary>
        /// 에셋 경로("Assets/A.png", "Packages/com.x/B.cs")를 저장소 기준 경로로. 저장소 밖(PackageCache 등)이면 null.
        /// </summary>
        public static string RepoPathOf(string assetPath, string projectRoot, string repoRoot)
        {
            if (string.IsNullOrEmpty(assetPath) || string.IsNullOrEmpty(projectRoot) || string.IsNullOrEmpty(repoRoot))
            {
                return null;
            }
            string full = Path.GetFullPath(Path.Combine(projectRoot, assetPath)).Replace('\\', '/').TrimEnd('/');
            string root = Path.GetFullPath(repoRoot).Replace('\\', '/').TrimEnd('/');
            if (full.StartsWith(root + "/", PathComparison) == false)
            {
                return null;
            }
            return full.Substring(root.Length + 1);
        }

        /// <summary>
        /// 고른 경로(저장소 기준)에 걸리는 변경 항목: 그 파일, 그 .meta, 폴더면 그 아래 전부.
        /// </summary>
        public static List<GitStatusEntry> EntriesUnder(IEnumerable<string> repoPaths, IEnumerable<GitStatusEntry> entries)
        {
            List<string> picked = repoPaths.Where(p => string.IsNullOrEmpty(p) == false).Select(p => p.TrimEnd('/')).Distinct().ToList();
            return entries.Where(entry => picked.Any(p => Covers(p, entry.RepoPath))).ToList();
        }

        private static bool Covers(string picked, string entryPath)
        {
            string path = (entryPath ?? string.Empty).TrimEnd('/');
            return path.Equals(picked, PathComparison)
                   || path.Equals(picked + ".meta", PathComparison)
                   || path.StartsWith(picked + "/", PathComparison)
                   // 새 폴더를 status 가 "folder/" 한 줄로 줄 때, 그 안의 파일을 고른 경우.
                   || picked.StartsWith(path + "/", PathComparison);
        }

        /// <summary>
        /// .gitignore 에 넣을 줄. 파일이면 그 파일과 .meta(/Assets/A.psd, /Assets/A.psd.meta),
        /// 폴더면 그 아래 전부와 폴더의 .meta(/Assets/Art/, /Assets/Art.meta). .meta 를 남기면 Unity 가 짝 없는 .meta 로 계속 변경에 올린다.
        /// </summary>
        public static IReadOnlyList<string> IgnorePatternsOf(string repoPath, bool isFolder)
        {
            string path = repoPath.TrimEnd('/');
            string main = isFolder ? GitIgnorePatterns.ForFolder(path) : GitIgnorePatterns.ForFile(path);
            return new[] { main, GitIgnorePatterns.ForFile(path + ".meta") };
        }
    }
}
