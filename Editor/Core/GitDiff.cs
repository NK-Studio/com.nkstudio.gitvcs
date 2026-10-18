using System;
using System.Collections.Generic;
using System.Globalization;

namespace GitVersionControl
{
    /// <summary>
    /// diff 를 무엇으로 보여 줄지. 내장 뷰어 외에는 바뀐 두 판을 임시 파일로 꺼내 외부 프로그램에 넘긴다(git difftool 은 git 이 직접 꺼낸다).
    /// 순서는 설정 파일에 숫자로 저장되므로 끝에만 추가한다.
    /// </summary>
    public enum DiffEngine
    {
        BuiltIn,
        GitDifftool,
        VisualStudioCode,
        Rider,
        BeyondCompare,
        P4Merge,
        WinMerge,
        Custom,
    }

    /// <summary>
    /// git 의 <c>--diff-algorithm</c>. 설정 파일에 숫자로 저장되므로 끝에만 추가한다.
    /// </summary>
    public enum DiffAlgorithm
    {
        Myers,
        Minimal,
        Patience,
        Histogram,
    }

    /// <summary>
    /// 공백 차이를 어떻게 볼지. IgnoreChange = <c>-b</c>(공백 양만 다른 것 무시), IgnoreAll = <c>-w</c>(모든 공백 무시).
    /// </summary>
    public enum DiffWhitespace
    {
        Show,
        IgnoreChange,
        IgnoreAll,
    }

    public enum DiffLayout
    {
        Unified,
        SideBySide,
    }

    /// <summary>
    /// 내장 뷰어가 git diff 에 넘기는 옵션.
    /// </summary>
    public readonly struct GitDiffOptions
    {
        /// <summary>
        /// 바뀐 줄 앞뒤로 보여 줄 줄 수의 상한. 이보다 크면 사실상 "파일 전체"라 git 에 그대로 넘겨도 비용이 같다.
        /// </summary>
        public const int MaxContextLines = 99999;

        public readonly DiffAlgorithm Algorithm;
        public readonly DiffWhitespace Whitespace;
        public readonly int ContextLines;

        public GitDiffOptions(DiffAlgorithm algorithm, DiffWhitespace whitespace, int contextLines)
        {
            Algorithm = algorithm;
            Whitespace = whitespace;
            ContextLines = Math.Max(0, Math.Min(contextLines, MaxContextLines));
        }
    }

    /// <summary>
    /// 무엇과 무엇을 비교하는지. 파일 목록의 세 출처에 하나씩 대응한다:
    /// 작업 트리(Pending: HEAD ↔ 디스크), 커밋(Changesets·탐색기: 첫 부모 ↔ 그 커밋), 병합 기준(Incoming: 공통 조상 ↔ 원격).
    /// </summary>
    public enum GitDiffSource
    {
        WorkingTree,
        Commit,
        MergeBaseRange,

        /// <summary>
        /// 인덱스에 올린 변경(HEAD ↔ 인덱스). Fork·GitKraken 레이아웃의 Staged 목록.
        /// </summary>
        Staged,

        /// <summary>
        /// 아직 올리지 않은 변경(인덱스 ↔ 작업 트리). Fork·GitKraken 레이아웃의 Unstaged 목록.
        /// </summary>
        Unstaged,

        /// <summary>
        /// 두 커밋을 그대로 비교(<c>From ↔ To</c>). Branch Explorer 의 "다른 커밋과 비교".
        /// </summary>
        Range,
    }

    [Serializable]
    public sealed class GitDiffTarget
    {
        public GitDiffSource Source;

        /// <summary>
        /// 저장소 루트 기준 경로.
        /// </summary>
        public string RepoPath;

        /// <summary>
        /// 이름이 바뀌었으면 원래 경로. 아니면 null.
        /// </summary>
        public string OriginalRepoPath;

        public GitFileState State;

        /// <summary>
        /// <see cref="GitDiffSource.Commit"/> 의 커밋.
        /// </summary>
        public string Commit;

        /// <summary>
        /// <see cref="GitDiffSource.MergeBaseRange"/>(<c>From...To</c>)·<see cref="GitDiffSource.Range"/>(<c>From ↔ To</c>) 의 양 끝.
        /// </summary>
        public string From;
        public string To;

        public string LeftPath => OriginalRepoPath ?? RepoPath;

        public static GitDiffTarget ForWorkingTree(GitStatusEntry entry)
        {
            return new GitDiffTarget
            {
                Source = GitDiffSource.WorkingTree,
                RepoPath = entry.RepoPath,
                OriginalRepoPath = entry.OriginalRepoPath,
                State = entry.State,
            };
        }

        public static GitDiffTarget ForStaging(GitStatusEntry entry, bool staged)
        {
            return new GitDiffTarget
            {
                Source = staged ? GitDiffSource.Staged : GitDiffSource.Unstaged,
                RepoPath = entry.RepoPath,
                OriginalRepoPath = entry.OriginalRepoPath,
                State = entry.State,
            };
        }

        public static GitDiffTarget ForCommit(string commit, GitFileChange change)
        {
            return new GitDiffTarget
            {
                Source = GitDiffSource.Commit,
                RepoPath = change.RepoPath,
                OriginalRepoPath = change.OriginalRepoPath,
                State = change.State,
                Commit = commit,
            };
        }

        public static GitDiffTarget ForRange(string from, string to, GitFileChange change)
        {
            return new GitDiffTarget
            {
                Source = GitDiffSource.Range,
                RepoPath = change.RepoPath,
                OriginalRepoPath = change.OriginalRepoPath,
                State = change.State,
                From = from,
                To = to,
            };
        }

        public static GitDiffTarget ForMergeBase(string from, string to, GitFileChange change)
        {
            return new GitDiffTarget
            {
                Source = GitDiffSource.MergeBaseRange,
                RepoPath = change.RepoPath,
                OriginalRepoPath = change.OriginalRepoPath,
                State = change.State,
                From = from,
                To = to,
            };
        }
    }

    public enum GitDiffLineKind
    {
        Context,
        Added,
        Removed,

        /// <summary>
        /// <c>@@ -a,b +c,d @@</c> 머리 줄.
        /// </summary>
        Hunk,

        /// <summary>
        /// <c>\ No newline at end of file</c>.
        /// </summary>
        NoNewline,
    }

    public sealed class GitDiffLine
    {
        public GitDiffLineKind Kind;

        /// <summary>
        /// 옛 판의 줄 번호(1부터). 없는 줄이면 0.
        /// </summary>
        public int OldNumber;

        /// <summary>
        /// 새 판의 줄 번호(1부터). 없는 줄이면 0.
        /// </summary>
        public int NewNumber;

        /// <summary>
        /// 앞의 +/-/공백 표시를 뗀 내용.
        /// </summary>
        public string Text;

        /// <summary>
        /// 짝이 되는 반대편 줄과 다른 구간 [Start, Start+Length). 짝이 없거나 줄 전체가 다르면 Length 가 0.
        /// </summary>
        public int ChangeStart;
        public int ChangeLength;
    }

    /// <summary>
    /// 나란히 보기의 한 줄. 왼쪽(옛 판)·오른쪽(새 판) 중 한쪽이 비어 있을 수 있다. 머리 줄은 양쪽에 같은 줄이 들어간다.
    /// </summary>
    public readonly struct GitDiffRow
    {
        public readonly GitDiffLine Left;
        public readonly GitDiffLine Right;

        public GitDiffRow(GitDiffLine left, GitDiffLine right)
        {
            Left = left;
            Right = right;
        }
    }

    public sealed class GitDiffDocument
    {
        public readonly List<GitDiffLine> Lines = new();
        public bool IsBinary;
        public int Added;
        public int Removed;
        public int HunkCount;

        public bool IsEmpty => IsBinary == false && Lines.Count == 0;
    }

    /// <summary>
    /// git diff 인자 만들기와 출력 해석. 저장소를 건드리지 않는 순수 함수만 둔다.
    /// </summary>
    public static class GitDiff
    {
        /// <summary>
        /// 내장 뷰어용 git 인자. untracked 파일은 git 이 모르므로 <c>--no-index</c> 로 빈 파일과 비교한다.
        /// 사용자의 외부 diff 설정(diff.external, textconv)은 끄고, 색은 빼서 해석할 수 있는 출력을 받는다.
        /// </summary>
        public static List<string> BuildArguments(GitDiffTarget target, GitDiffOptions options)
        {
            List<string> args;
            switch (target.Source)
            {
                case GitDiffSource.Staged:
                    args = new List<string> { "diff", "--cached", "-M" };
                    AppendOptions(args, options);
                    break;
                case GitDiffSource.WorkingTree:
                case GitDiffSource.Unstaged:
                    if (target.State == GitFileState.Untracked)
                    {
                        args = new List<string> { "diff", "--no-index" };
                        AppendOptions(args, options);
                        // --no-index 의 /dev/null 은 Windows git 도 특별히 해석한다.
                        args.AddRange(new[] { "--", "/dev/null", target.RepoPath });
                        return args;
                    }
                    args = new List<string> { "diff", "-M" };
                    AppendOptions(args, options);
                    // 작업 트리 전체(HEAD 대비)와 아직 올리지 않은 쪽(인덱스 대비)은 비교 기준만 다르다.
                    if (target.Source == GitDiffSource.WorkingTree)
                    {
                        args.Add("HEAD");
                    }
                    break;
                case GitDiffSource.Commit:
                    // 커밋 파일 목록(CommitFilesAsync)과 같은 규칙: 첫 부모 대비, 최초 커밋은 빈 트리 대비.
                    args = new List<string> { "diff-tree", "-p", "-M", "--root", "--diff-merges=first-parent" };
                    AppendOptions(args, options);
                    args.Add(target.Commit);
                    break;
                case GitDiffSource.MergeBaseRange:
                    args = new List<string> { "diff", "-M" };
                    AppendOptions(args, options);
                    args.Add($"{target.From}...{target.To}");
                    break;
                case GitDiffSource.Range:
                    args = new List<string> { "diff", "-M" };
                    AppendOptions(args, options);
                    args.Add(target.From);
                    args.Add(target.To);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(target), target.Source, null);
            }
            args.Add("--");
            if (target.OriginalRepoPath != null)
            {
                args.Add(target.OriginalRepoPath);
            }
            args.Add(target.RepoPath);
            return args;
        }

        private static void AppendOptions(List<string> args, GitDiffOptions options)
        {
            args.Add("--no-color");
            args.Add("--no-ext-diff");
            args.Add("--no-textconv");
            args.Add("--diff-algorithm=" + options.Algorithm.ToString().ToLowerInvariant());
            args.Add("-U" + options.ContextLines.ToString(CultureInfo.InvariantCulture));
            switch (options.Whitespace)
            {
                case DiffWhitespace.IgnoreChange:
                    args.Add("-b");
                    break;
                case DiffWhitespace.IgnoreAll:
                    args.Add("-w");
                    break;
            }
        }

        /// <summary>
        /// unified diff 출력을 줄 목록으로 바꾼다. 파일 머리(diff --git, index, ---/+++)는 버리고 hunk 부터 담는다.
        /// 여러 파일이 섞여 있어도(이름 변경 등) 차례로 이어 붙인다.
        /// </summary>
        public static GitDiffDocument Parse(string output)
        {
            GitDiffDocument document = new();
            if (string.IsNullOrEmpty(output))
            {
                return document;
            }

            int oldNumber = 0;
            int newNumber = 0;
            bool inHunk = false;
            string[] lines = output.Replace("\r\n", "\n").Split('\n');
            // 출력 끝의 줄바꿈 뒤 빈 조각은 내용이 아니다.
            int count = lines.Length > 0 && lines[^1].Length == 0 ? lines.Length - 1 : lines.Length;

            for (int i = 0; i < count; i++)
            {
                string line = lines[i];
                if (line.StartsWith("diff --git ", StringComparison.Ordinal))
                {
                    inHunk = false;
                    continue;
                }
                if (line.StartsWith("@@", StringComparison.Ordinal))
                {
                    if (TryParseHunkHeader(line, out oldNumber, out newNumber) == false)
                    {
                        throw new FormatException("Invalid hunk header: " + line);
                    }
                    inHunk = true;
                    document.HunkCount++;
                    document.Lines.Add(new GitDiffLine { Kind = GitDiffLineKind.Hunk, Text = line });
                    continue;
                }
                if (inHunk == false)
                {
                    if (line.StartsWith("Binary files ", StringComparison.Ordinal) || line.StartsWith("GIT binary patch", StringComparison.Ordinal))
                    {
                        document.IsBinary = true;
                    }
                    continue;
                }
                if (line.Length == 0)
                {
                    // 일부 도구는 빈 문맥 줄의 앞 공백을 지운다. 빈 문맥 줄로 본다.
                    document.Lines.Add(new GitDiffLine { Kind = GitDiffLineKind.Context, OldNumber = oldNumber++, NewNumber = newNumber++, Text = string.Empty });
                    continue;
                }
                char marker = line[0];
                string text = line.Substring(1);
                switch (marker)
                {
                    case ' ':
                        document.Lines.Add(new GitDiffLine { Kind = GitDiffLineKind.Context, OldNumber = oldNumber++, NewNumber = newNumber++, Text = text });
                        break;
                    case '-':
                        document.Removed++;
                        document.Lines.Add(new GitDiffLine { Kind = GitDiffLineKind.Removed, OldNumber = oldNumber++, Text = text });
                        break;
                    case '+':
                        document.Added++;
                        document.Lines.Add(new GitDiffLine { Kind = GitDiffLineKind.Added, NewNumber = newNumber++, Text = text });
                        break;
                    case '\\':
                        document.Lines.Add(new GitDiffLine { Kind = GitDiffLineKind.NoNewline, Text = line });
                        break;
                    default:
                        // hunk 가 끝나고 다음 파일 머리가 시작됐다.
                        inHunk = false;
                        break;
                }
            }

            MarkIntraLineChanges(document.Lines);
            return document;
        }

        /// <summary>
        /// <c>@@ -12,5 +12,7 @@ 섹션</c> 에서 옛·새 시작 줄 번호를 읽는다. 줄 수(",5")는 생략될 수 있다.
        /// </summary>
        public static bool TryParseHunkHeader(string line, out int oldStart, out int newStart)
        {
            oldStart = 0;
            newStart = 0;
            int minus = line.IndexOf(" -", StringComparison.Ordinal);
            int plus = line.IndexOf(" +", minus + 2, StringComparison.Ordinal);
            if (minus < 0 || plus < 0)
            {
                return false;
            }
            return TryReadNumber(line, minus + 2, out oldStart) && TryReadNumber(line, plus + 2, out newStart);
        }

        private static bool TryReadNumber(string line, int start, out int value)
        {
            int end = start;
            while (end < line.Length && char.IsDigit(line[end]))
            {
                end++;
            }
            return int.TryParse(line.Substring(start, end - start), NumberStyles.None, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>
        /// 연속한 "지운 줄 묶음 → 더한 줄 묶음"을 같은 순번끼리 짝지어, 짝마다 실제로 다른 구간을 표시한다.
        /// 예: "int count = 1;" ↔ "int count = 10;" 이면 각각 "1" / "10" 만 강조된다.
        /// </summary>
        public static void MarkIntraLineChanges(IReadOnlyList<GitDiffLine> lines)
        {
            int i = 0;
            while (i < lines.Count)
            {
                if (lines[i].Kind != GitDiffLineKind.Removed)
                {
                    i++;
                    continue;
                }
                int removedStart = i;
                while (i < lines.Count && lines[i].Kind == GitDiffLineKind.Removed)
                {
                    i++;
                }
                int addedStart = i;
                while (i < lines.Count && lines[i].Kind == GitDiffLineKind.Added)
                {
                    i++;
                }
                int pairs = Math.Min(addedStart - removedStart, i - addedStart);
                for (int k = 0; k < pairs; k++)
                {
                    MarkPair(lines[removedStart + k], lines[addedStart + k]);
                }
            }
        }

        /// <summary>
        /// 줄 안 강조를 하려면 두 줄이 이만큼 같아야 한다(공통 앞·뒤 글자 수 / 긴 줄 길이).
        /// 순번으로 짝지은 두 줄이 사실은 서로 다른 줄일 때(한 줄 요약을 세 줄로 나눈 경우 등) 줄 대부분이 강조돼 오히려 읽기 어려워지는 것을 막는다.
        /// 0.5 는 "한 단어·값 몇 개를 고친 줄"은 통과하고 "다시 쓴 줄"은 걸러지는 경험값이다.
        /// </summary>
        public const float MinIntraLineSimilarity = 0.5f;

        /// <summary>
        /// 두 줄의 공통 앞·뒤를 빼고 남는 가운데를 바뀐 구간으로 본다. 두 줄이 충분히 비슷하지 않으면(<see cref="MinIntraLineSimilarity"/>)
        /// 다시 쓴 줄로 보고 강조하지 않는다 — 줄 배경색만으로 충분하다.
        /// </summary>
        public static void MarkPair(GitDiffLine removed, GitDiffLine added)
        {
            string a = removed.Text;
            string b = added.Text;
            int prefix = 0;
            int maxPrefix = Math.Min(a.Length, b.Length);
            while (prefix < maxPrefix && a[prefix] == b[prefix])
            {
                prefix++;
            }
            int suffix = 0;
            int maxSuffix = maxPrefix - prefix;
            while (suffix < maxSuffix && a[a.Length - 1 - suffix] == b[b.Length - 1 - suffix])
            {
                suffix++;
            }
            int longer = Math.Max(a.Length, b.Length);
            if (longer == 0 || (prefix + suffix) < longer * MinIntraLineSimilarity)
            {
                return;
            }
            removed.ChangeStart = prefix;
            removed.ChangeLength = a.Length - prefix - suffix;
            added.ChangeStart = prefix;
            added.ChangeLength = b.Length - prefix - suffix;
        }

        /// <summary>
        /// 나란히 보기 줄로 바꾼다. 문맥 줄은 양쪽에, 지운·더한 묶음은 같은 순번끼리 한 줄에 놓고 남는 쪽은 반대편을 비운다.
        /// </summary>
        public static List<GitDiffRow> ToSideBySide(IReadOnlyList<GitDiffLine> lines)
        {
            List<GitDiffRow> rows = new();
            int i = 0;
            while (i < lines.Count)
            {
                GitDiffLine line = lines[i];
                if (line.Kind != GitDiffLineKind.Removed && line.Kind != GitDiffLineKind.Added)
                {
                    rows.Add(new GitDiffRow(line, line));
                    i++;
                    continue;
                }
                List<GitDiffLine> removed = new();
                while (i < lines.Count && lines[i].Kind == GitDiffLineKind.Removed)
                {
                    removed.Add(lines[i++]);
                }
                List<GitDiffLine> added = new();
                while (i < lines.Count && lines[i].Kind == GitDiffLineKind.Added)
                {
                    added.Add(lines[i++]);
                }
                int count = Math.Max(removed.Count, added.Count);
                for (int k = 0; k < count; k++)
                {
                    rows.Add(new GitDiffRow(k < removed.Count ? removed[k] : null, k < added.Count ? added[k] : null));
                }
            }
            return rows;
        }
    }
}
