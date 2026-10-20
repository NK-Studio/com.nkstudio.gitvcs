using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GitVersionControl
{
    /// <summary>충돌 한 덩어리에서 고를 수 있는 쪽. A = 우리 쪽(<c>&lt;&lt;&lt;&lt;&lt;&lt;&lt;</c> 아래), B = 들어오는 쪽(<c>=======</c> 아래).</summary>
    public enum GitMergeSide
    {
        A,
        B,
    }

    /// <summary>
    /// 충돌 파일의 한 조각: 양쪽이 같은 줄들(<see cref="IsConflict"/> = false) 또는 충돌 한 덩어리(양쪽 줄).
    /// </summary>
    public sealed class GitMergeSegment
    {
        public bool IsConflict;

        /// <summary>같은 줄들. 충돌이면 비어 있다.</summary>
        public List<string> Common = new();

        public List<string> A = new();
        public List<string> B = new();

        /// <summary>diff3 형식(<c>|||||||</c>)이면 공통 조상 줄. 보이기만 하고 출력에는 쓰지 않는다.</summary>
        public List<string> Base = new();

        public List<string> Lines(GitMergeSide side) => side == GitMergeSide.A ? A : B;
    }

    /// <summary>
    /// 충돌 한 덩어리에서 무엇을 골랐는지. 고른 쪽을 고른 차례대로 출력에 잇고(GitKraken 처럼 B 를 먼저 고르면 B 가 위),
    /// 그 쪽 안에서는 켜 둔 줄만 넣는다.
    /// </summary>
    public sealed class GitMergeChoice
    {
        public readonly List<GitMergeSide> Order = new();
        public readonly bool[] ALines;
        public readonly bool[] BLines;

        public GitMergeChoice(GitMergeSegment segment)
        {
            ALines = new bool[segment.A.Count];
            BLines = new bool[segment.B.Count];
        }

        /// <summary>어느 쪽이든 하나라도 골랐는지. 다 고르기 전에는 저장하지 않는다.</summary>
        public bool IsDecided => Order.Count > 0;

        public bool IsChosen(GitMergeSide side) => Order.Contains(side);

        public bool[] LinesOf(GitMergeSide side) => side == GitMergeSide.A ? ALines : BLines;

        /// <summary>그 쪽 전체를 고르거나(뒤에 잇는다) 뺀다.</summary>
        public void SetSide(GitMergeSide side, bool chosen)
        {
            bool[] lines = LinesOf(side);
            for (int i = 0; i < lines.Length; i++)
            {
                lines[i] = chosen;
            }
            Order.Remove(side);
            if (chosen)
            {
                Order.Add(side);
            }
        }

        /// <summary>
        /// 그 쪽의 한 줄을 켜고 끈다. 아직 고르지 않은 쪽이면 그 줄만 켠 채로 고른 쪽에 넣고, 마지막 줄을 끄면 그 쪽을 뺀다.
        /// </summary>
        public void ToggleLine(GitMergeSide side, int line)
        {
            bool[] lines = LinesOf(side);
            if (line < 0 || line >= lines.Length)
            {
                return;
            }
            if (IsChosen(side) == false)
            {
                Order.Add(side);
                lines[line] = true;
                return;
            }
            lines[line] = lines[line] == false;
            if (lines.Any(on => on) == false)
            {
                Order.Remove(side);
            }
        }

        public void Clear()
        {
            Order.Clear();
            Array.Clear(ALines, 0, ALines.Length);
            Array.Clear(BLines, 0, BLines.Length);
        }
    }

    /// <summary>
    /// 작업 사본에 git 이 남긴 충돌 표시(<c>&lt;&lt;&lt;&lt;&lt;&lt;&lt; A … ||||||| base … ======= … &gt;&gt;&gt;&gt;&gt;&gt;&gt; B</c>)를 읽어 조각으로 나누고,
    /// 덩어리마다 고른 대로 출력 글을 만든다. 줄바꿈(CRLF·LF)과 끝 줄바꿈은 원래 파일을 따른다.
    /// </summary>
    public sealed class GitConflictFile
    {
        public readonly List<GitMergeSegment> Segments = new();

        /// <summary>표시 줄의 이름표(<c>&lt;&lt;&lt;&lt;&lt;&lt;&lt; HEAD</c> 의 HEAD, <c>&gt;&gt;&gt;&gt;&gt;&gt;&gt; origin/develop</c> 의 origin/develop). 없으면 빈 글.</summary>
        public string ALabel = string.Empty;
        public string BLabel = string.Empty;

        public string NewLine = "\n";
        public bool EndsWithNewLine;

        public IEnumerable<GitMergeSegment> Conflicts => Segments.Where(s => s.IsConflict);
        public int ConflictCount => Segments.Count(s => s.IsConflict);

        /// <summary>
        /// 충돌 표시가 있는 글을 나눈다. 짝이 맞는 충돌 표시가 하나도 없으면 null.
        /// </summary>
        public static GitConflictFile Parse(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }
            GitConflictFile file = new();
            int crlf = CountOf(text, "\r\n");
            int lf = CountOf(text, "\n");
            file.NewLine = crlf > 0 && crlf * 2 >= lf ? "\r\n" : "\n";
            file.EndsWithNewLine = text.EndsWith("\n", StringComparison.Ordinal);

            List<string> lines = text.Split('\n').Select(line => line.EndsWith("\r", StringComparison.Ordinal) ? line.Substring(0, line.Length - 1) : line).ToList();
            if (file.EndsWithNewLine)
            {
                lines.RemoveAt(lines.Count - 1);
            }

            GitMergeSegment common = null;
            GitMergeSegment conflict = null;
            // 0 = 바깥, 1 = A, 2 = base, 3 = B
            int part = 0;
            List<string> pending = new();
            foreach (string line in lines)
            {
                if (part == 0 && IsMarker(line, '<'))
                {
                    conflict = new GitMergeSegment { IsConflict = true };
                    pending.Clear();
                    pending.Add(line);
                    if (file.ALabel.Length == 0)
                    {
                        file.ALabel = MarkerLabel(line);
                    }
                    part = 1;
                    continue;
                }
                if (part != 0)
                {
                    pending.Add(line);
                    if ((part == 1) && IsMarker(line, '|'))
                    {
                        part = 2;
                        continue;
                    }
                    if ((part == 1 || part == 2) && IsSeparator(line))
                    {
                        part = 3;
                        continue;
                    }
                    if (part == 3 && IsMarker(line, '>'))
                    {
                        if (file.BLabel.Length == 0)
                        {
                            file.BLabel = MarkerLabel(line);
                        }
                        file.Segments.Add(conflict);
                        conflict = null;
                        common = null;
                        part = 0;
                        continue;
                    }
                    (part == 1 ? conflict.A : part == 2 ? conflict.Base : conflict.B).Add(line);
                    continue;
                }
                if (common == null)
                {
                    common = new GitMergeSegment();
                    file.Segments.Add(common);
                }
                common.Common.Add(line);
            }
            // 짝이 맞지 않는 표시(끝이 없는 <<<<<<<)는 그냥 글로 둔다.
            if (part != 0)
            {
                if (common == null)
                {
                    common = new GitMergeSegment();
                    file.Segments.Add(common);
                }
                common.Common.AddRange(pending);
            }
            return file.ConflictCount > 0 ? file : null;
        }

        /// <summary>
        /// 고른 대로 출력 글을 만든다. <paramref name="choices"/> 는 충돌 덩어리 차례(<see cref="Conflicts"/>)와 같다.
        /// </summary>
        public string Build(IReadOnlyList<GitMergeChoice> choices)
        {
            List<string> lines = new();
            int index = 0;
            foreach (GitMergeSegment segment in Segments)
            {
                if (segment.IsConflict == false)
                {
                    lines.AddRange(segment.Common);
                    continue;
                }
                lines.AddRange(ChosenLines(segment, choices[index++]));
            }
            StringBuilder builder = new();
            for (int i = 0; i < lines.Count; i++)
            {
                builder.Append(lines[i]);
                if (i < lines.Count - 1 || EndsWithNewLine)
                {
                    builder.Append(NewLine);
                }
            }
            return builder.ToString();
        }

        /// <summary>한 덩어리에서 고른 줄(고른 쪽 차례대로, 켜 둔 줄만)과 그 줄이 어느 쪽에서 왔는지.</summary>
        public static IEnumerable<string> ChosenLines(GitMergeSegment segment, GitMergeChoice choice)
        {
            foreach ((GitMergeSide _, int _, string line) in ChosenLinesWithOrigin(segment, choice))
            {
                yield return line;
            }
        }

        public static IEnumerable<(GitMergeSide side, int index, string line)> ChosenLinesWithOrigin(GitMergeSegment segment, GitMergeChoice choice)
        {
            foreach (GitMergeSide side in choice.Order)
            {
                List<string> source = segment.Lines(side);
                bool[] on = choice.LinesOf(side);
                for (int i = 0; i < source.Count; i++)
                {
                    if (on[i])
                    {
                        yield return (side, i, source[i]);
                    }
                }
            }
        }

        /// <summary><c>&lt;&lt;&lt;&lt;&lt;&lt;&lt;</c> 처럼 같은 글자 일곱 개로 시작하고, 끝나거나 빈칸이 뒤따르는 줄.</summary>
        private static bool IsMarker(string line, char c)
        {
            if (line.Length < 7)
            {
                return false;
            }
            for (int i = 0; i < 7; i++)
            {
                if (line[i] != c)
                {
                    return false;
                }
            }
            return line.Length == 7 || line[7] == ' ';
        }

        private static bool IsSeparator(string line) => line == "=======";

        private static string MarkerLabel(string line) => line.Length > 8 ? line.Substring(8).Trim() : string.Empty;

        private static int CountOf(string text, string value)
        {
            int count = 0;
            int at = 0;
            while ((at = text.IndexOf(value, at, StringComparison.Ordinal)) >= 0)
            {
                count++;
                at += value.Length;
            }
            return count;
        }
    }
}
