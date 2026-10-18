using System;

namespace GitVersionControl
{
    /// <summary>
    /// git 값을 화면에 보일 글자로 바꾸는 규칙. IMGUI(설정 창)와 UI Toolkit(Git 창)이 같은 표기를 쓰도록 한곳에 둔다.
    /// </summary>
    public static class GitDisplay
    {
        public static string FormatBranch(GitBranchInfo branch)
        {
            if (branch == null)
            {
                return "-";
            }
            if (branch.HasCommits == false)
            {
                return GitText.Format("display.noCommits", branch.Name);
            }
            string text = branch.Detached ? "HEAD (detached)" : branch.Name;
            if (string.IsNullOrEmpty(branch.Upstream) == false)
            {
                text += $" → {branch.Upstream}";
            }
            if (branch.Ahead > 0)
            {
                text += $"  ↑{branch.Ahead}";
            }
            if (branch.Behind > 0)
            {
                text += $"  ↓{branch.Behind}";
            }
            return text;
        }

        /// <summary><c>%(upstream:track)</c>("[ahead 1, behind 2]", "[gone]") 를 "origin/main  ↑1, ↓2" 처럼 바꾼다.</summary>
        public static string FormatTrack(GitBranchRef branch)
        {
            if (branch.IsRemote)
            {
                return string.Empty;
            }
            if (string.IsNullOrEmpty(branch.Upstream))
            {
                return GitText.Get("display.noUpstream");
            }
            string track = branch.Track.Trim('[', ']')
                .Replace("ahead ", "↑")
                .Replace("behind ", "↓")
                .Replace("gone", GitText.Get("display.gone"));
            return string.IsNullOrEmpty(track) ? branch.Upstream : $"{branch.Upstream}  {track}";
        }

        public static string FormatDate(DateTime date)
        {
            return date == DateTime.MinValue ? string.Empty : date.ToString("yyyy-MM-dd HH:mm");
        }

        public static string StateLetter(GitFileState state)
        {
            switch (state)
            {
                case GitFileState.Modified: return "M";
                case GitFileState.Added: return "A";
                case GitFileState.Deleted: return "D";
                case GitFileState.Renamed: return "R";
                // VS Code 와 같은 글자. "?" 는 깨진 아이콘처럼 보여서 쓰지 않는다.
                case GitFileState.Untracked: return "U";
                case GitFileState.Conflicted: return "C";
                default: return string.Empty;
            }
        }

        /// <summary>작성자 이름의 머리글자 두 개("Bae Nari" → "BN", "Bob" → "BO").</summary>
        public static string Initials(string author)
        {
            if (string.IsNullOrWhiteSpace(author))
            {
                return "?";
            }
            string[] words = author.Trim().Split(new[] { ' ', '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length >= 2)
            {
                return (words[0].Substring(0, 1) + words[1].Substring(0, 1)).ToUpperInvariant();
            }
            return words[0].Length >= 2 ? words[0].Substring(0, 2).ToUpperInvariant() : words[0].ToUpperInvariant();
        }

        public static string FirstLine(string text)
        {
            string trimmed = (text ?? string.Empty).Trim();
            int newline = trimmed.IndexOf('\n');
            return newline < 0 ? trimmed : trimmed.Substring(0, newline).Trim();
        }

        /// <summary>.meta 를 떼어 Project 창에 보이는 에셋 경로로 바꾼다.</summary>
        public static string AssetPathOf(string projectPath)
        {
            if (string.IsNullOrEmpty(projectPath))
            {
                return projectPath;
            }
            return projectPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)
                ? projectPath.Substring(0, projectPath.Length - 5)
                : projectPath;
        }
    }
}
