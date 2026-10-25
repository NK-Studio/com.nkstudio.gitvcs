using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GitVersionControl
{
    /// <summary>
    /// diff 뷰어에서 고른 줄·변경 묶음(hunk)만 든 패치를 만든다(GitKraken 의 "이 줄 스테이지"·"hunk 스테이지").
    /// 만든 패치는 <c>git apply --cached</c>(Stage) 또는 <c>git apply --cached --reverse</c>(Unstage)로 인덱스에만 적용한다.
    ///
    /// <c>git add -p</c> 가 줄을 고를 때 쓰는 규칙과 같다:
    /// Stage 는 인덱스(옛 판)에 적용하므로 고르지 않은 '-' 줄은 그대로 남는 문맥 줄이 되고 고르지 않은 '+' 줄은 뺀다.
    /// Unstage 는 거꾸로 적용되어 인덱스가 새 판이므로 고르지 않은 '+' 줄이 문맥 줄이 되고 고르지 않은 '-' 줄은 뺀다.
    /// 줄 수가 달라졌으니 묶음 머리 줄의 줄 수는 다시 센다. 저장소를 건드리지 않는 순수 함수만 둔다.
    /// </summary>
    public static class GitPartialPatch
    {
        /// <summary>
        /// <c>@@ -oldStart,oldCount +newStart,newCount @@ 뒤 글</c> 을 뜯은 것.
        /// </summary>
        public readonly struct HunkHeader
        {
            public readonly int OldStart;
            public readonly int OldCount;
            public readonly int NewStart;
            public readonly int NewCount;

            /// <summary>
            /// 두 번째 <c>@@</c> 뒤의 글(함수 이름 등, 앞 공백 포함). 없으면 빈 문자열.
            /// </summary>
            public readonly string Trailer;

            public HunkHeader(int oldStart, int oldCount, int newStart, int newCount, string trailer)
            {
                OldStart = oldStart;
                OldCount = oldCount;
                NewStart = newStart;
                NewCount = newCount;
                Trailer = trailer ?? string.Empty;
            }

            public override string ToString()
            {
                return string.Format(CultureInfo.InvariantCulture, "@@ -{0},{1} +{2},{3} @@{4}", OldStart, OldCount, NewStart, NewCount, Trailer);
            }
        }

        /// <summary>
        /// 묶음 머리 줄을 뜯는다. 줄 수(",5")가 생략되면 1 이다.
        /// </summary>
        public static bool TryParseHunkHeader(string line, out HunkHeader header)
        {
            header = default;
            if (line == null || line.StartsWith("@@ -", StringComparison.Ordinal) == false)
            {
                return false;
            }
            int position = 4;
            if (TryReadRange(line, ref position, out int oldStart, out int oldCount) == false)
            {
                return false;
            }
            if (position + 2 > line.Length || line[position] != ' ' || line[position + 1] != '+')
            {
                return false;
            }
            position += 2;
            if (TryReadRange(line, ref position, out int newStart, out int newCount) == false)
            {
                return false;
            }
            int close = line.IndexOf("@@", position, StringComparison.Ordinal);
            if (close < 0)
            {
                return false;
            }
            header = new HunkHeader(oldStart, oldCount, newStart, newCount, line.Substring(close + 2));
            return true;
        }

        private static bool TryReadRange(string line, ref int position, out int start, out int count)
        {
            count = 1;
            if (TryReadNumber(line, ref position, out start) == false)
            {
                return false;
            }
            if (position < line.Length && line[position] == ',')
            {
                position++;
                return TryReadNumber(line, ref position, out count);
            }
            return true;
        }

        private static bool TryReadNumber(string line, ref int position, out int value)
        {
            int end = position;
            while (end < line.Length && char.IsDigit(line[end]))
            {
                end++;
            }
            bool parsed = int.TryParse(line.Substring(position, end - position), NumberStyles.None, CultureInfo.InvariantCulture, out value);
            position = end;
            return parsed;
        }

        /// <summary>
        /// 변경 묶음 하나의 바뀐 줄('+'·'-') 인덱스(<paramref name="lines"/> 기준). "hunk 스테이지" 버튼이 이걸 골라서 넘긴다.
        /// </summary>
        public static List<int> ChangedLinesOfHunk(IReadOnlyList<GitDiffLine> lines, int hunkIndex)
        {
            List<int> result = new();
            for (int i = 0; i < lines.Count; i++)
            {
                GitDiffLine line = lines[i];
                if (line.HunkIndex == hunkIndex && IsChange(line))
                {
                    result.Add(i);
                }
            }
            return result;
        }

        public static bool IsChange(GitDiffLine line)
        {
            return line != null && (line.Kind == GitDiffLineKind.Added || line.Kind == GitDiffLineKind.Removed);
        }

        /// <summary>
        /// 고른 줄만 든 패치 글. 고른 줄에 바뀐 줄이 하나도 없으면 null.
        /// </summary>
        /// <param name="lines"><see cref="GitDiff.Parse"/> 가 만든 줄 목록(머리 줄 포함).</param>
        /// <param name="selected">고른 줄의 인덱스(<paramref name="lines"/> 기준). 바뀐 줄이 아닌 인덱스는 무시한다.</param>
        /// <param name="repoPath">저장소 루트 기준 경로. 이름이 바뀐 파일도 인덱스에는 새 경로로 있으므로 새 경로만 쓴다.</param>
        /// <param name="reverse">true 면 Unstage 용(<c>git apply --reverse</c> 로 적용할 패치).</param>
        public static string Build(IReadOnlyList<GitDiffLine> lines, ICollection<int> selected, string repoPath, bool reverse)
        {
            if (lines == null || selected == null || selected.Count == 0)
            {
                return null;
            }
            HashSet<int> chosen = selected as HashSet<int> ?? new HashSet<int>(selected);
            StringBuilder body = new();
            // 앞서 내보낸 묶음들이 줄 수를 얼마나 바꿨는지(새 판 줄 수 − 옛 판 줄 수의 합). 다음 묶음의 시작 줄을 맞추는 데 쓴다.
            int delta = 0;
            bool anyHunk = false;

            int i = 0;
            while (i < lines.Count)
            {
                if (lines[i].Kind != GitDiffLineKind.Hunk)
                {
                    i++;
                    continue;
                }
                int headerIndex = i;
                int end = i + 1;
                while (end < lines.Count && lines[end].Kind != GitDiffLineKind.Hunk)
                {
                    end++;
                }
                i = end;
                if (TryParseHunkHeader(lines[headerIndex].Text, out HunkHeader original) == false)
                {
                    throw new FormatException("Invalid hunk header: " + lines[headerIndex].Text);
                }
                if (TryBuildHunk(lines, headerIndex + 1, end, chosen, reverse, original, ref delta, out string hunk) == false)
                {
                    continue;
                }
                anyHunk = true;
                body.Append(hunk);
            }

            if (anyHunk == false)
            {
                return null;
            }
            StringBuilder patch = new();
            patch.Append("diff --git ").Append(QuotePath("a/" + repoPath)).Append(' ').Append(QuotePath("b/" + repoPath)).Append('\n');
            patch.Append("--- ").Append(QuotePath("a/" + repoPath)).Append('\n');
            patch.Append("+++ ").Append(QuotePath("b/" + repoPath)).Append('\n');
            patch.Append(body);
            return patch.ToString();
        }

        /// <summary>
        /// 묶음 하나를 고른 줄만 남겨 다시 쓴다. 바뀐 줄이 하나도 남지 않으면 false(묶음을 통째로 뺀다).
        /// </summary>
        private static bool TryBuildHunk(IReadOnlyList<GitDiffLine> lines, int start, int end, HashSet<int> chosen, bool reverse, HunkHeader original, ref int delta, out string hunk)
        {
            hunk = null;
            StringBuilder body = new();
            int oldCount = 0;
            int newCount = 0;
            bool anyChange = false;
            // 바로 앞 줄을 내보냈는지. "\ No newline at end of file" 은 앞 줄에 딸린 표시라 앞 줄을 뺐으면 같이 뺀다.
            bool previousEmitted = false;

            for (int i = start; i < end; i++)
            {
                GitDiffLine line = lines[i];
                char marker;
                switch (line.Kind)
                {
                    case GitDiffLineKind.Context:
                        marker = ' ';
                        break;
                    case GitDiffLineKind.Added:
                        if (chosen.Contains(i))
                        {
                            marker = '+';
                        }
                        else if (reverse)
                        {
                            // 인덱스(새 판)에 이미 있는 줄 — 그대로 둔다.
                            marker = ' ';
                        }
                        else
                        {
                            previousEmitted = false;
                            continue;
                        }
                        break;
                    case GitDiffLineKind.Removed:
                        if (chosen.Contains(i))
                        {
                            marker = '-';
                        }
                        else if (reverse)
                        {
                            previousEmitted = false;
                            continue;
                        }
                        else
                        {
                            // 인덱스(옛 판)에 있고 지우지 않을 줄 — 문맥 줄로 둔다.
                            marker = ' ';
                        }
                        break;
                    case GitDiffLineKind.NoNewline:
                        if (previousEmitted)
                        {
                            body.Append(line.Text).Append('\n');
                        }
                        continue;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(lines), line.Kind, null);
                }

                previousEmitted = true;
                anyChange |= marker != ' ';
                if (marker != '+')
                {
                    oldCount++;
                }
                if (marker != '-')
                {
                    newCount++;
                }
                body.Append(marker).Append(line.Text ?? string.Empty);
                if (line.HasCarriageReturn)
                {
                    body.Append('\r');
                }
                body.Append('\n');
            }

            if (anyChange == false)
            {
                return false;
            }

            // Stage 는 옛 판(인덱스)에 적용하므로 옛 시작 줄은 원래 머리 줄 그대로이고 새 시작 줄만 앞 묶음들의 증감만큼 민다.
            // Unstage 는 거꾸로 적용되어 새 판이 인덱스이므로 반대다. 줄 수가 0 인 쪽은 git 처럼 "그 앞 줄" 번호를 쓴다.
            int oldStart;
            int newStart;
            if (reverse == false)
            {
                oldStart = original.OldStart;
                int position = original.OldCount > 0 ? original.OldStart : original.OldStart + 1;
                newStart = position + delta;
                if (newCount == 0)
                {
                    newStart--;
                }
            }
            else
            {
                newStart = original.NewStart;
                int position = original.NewCount > 0 ? original.NewStart : original.NewStart + 1;
                oldStart = position - delta;
                if (oldCount == 0)
                {
                    oldStart--;
                }
            }
            delta += newCount - oldCount;

            hunk = new HunkHeader(oldStart, oldCount, newStart, newCount, original.Trailer) + "\n" + body;
            return true;
        }

        /// <summary>
        /// git 이 <c>core.quotepath</c> 로 하는 것과 같은 C 식 따옴표 묶기. 한글·공백·제어 문자·따옴표·역슬래시가 있으면
        /// <c>"a/\355\225\234.txt"</c> 처럼 UTF-8 바이트를 8진수로 적는다. <c>git apply</c> 는 이 꼴을 풀어 읽는다. 필요 없으면 그대로.
        /// </summary>
        public static string QuotePath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return path;
            }
            byte[] bytes = Encoding.UTF8.GetBytes(path);
            bool needsQuote = false;
            foreach (byte b in bytes)
            {
                if (b < 0x20 || b >= 0x7f || b == (byte)'"' || b == (byte)'\\' || b == (byte)' ')
                {
                    needsQuote = true;
                    break;
                }
            }
            if (needsQuote == false)
            {
                return path;
            }
            StringBuilder quoted = new("\"");
            foreach (byte b in bytes)
            {
                switch (b)
                {
                    case (byte)'"':
                        quoted.Append("\\\"");
                        break;
                    case (byte)'\\':
                        quoted.Append("\\\\");
                        break;
                    case 7:
                        quoted.Append("\\a");
                        break;
                    case 8:
                        quoted.Append("\\b");
                        break;
                    case 9:
                        quoted.Append("\\t");
                        break;
                    case 10:
                        quoted.Append("\\n");
                        break;
                    case 11:
                        quoted.Append("\\v");
                        break;
                    case 12:
                        quoted.Append("\\f");
                        break;
                    case 13:
                        quoted.Append("\\r");
                        break;
                    default:
                        if (b < 0x20 || b >= 0x7f)
                        {
                            quoted.Append('\\').Append(Convert.ToString(b, 8).PadLeft(3, '0'));
                        }
                        else
                        {
                            quoted.Append((char)b);
                        }
                        break;
                }
            }
            return quoted.Append('"').ToString();
        }
    }
}
