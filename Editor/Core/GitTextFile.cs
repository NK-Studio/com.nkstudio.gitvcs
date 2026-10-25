using System;
using System.Collections.Generic;
using System.Text;

namespace GitVersionControl
{
    /// <summary>
    /// diff 의 "이 파일 편집" 이 여는 작업 사본 글 파일. 읽을 때 인코딩·BOM·줄 끝(CRLF·LF·CR)을 기억해 두고,
    /// 저장할 때 그대로 되살려 고친 곳만 바뀌게 한다. 편집기에는 줄 끝을 모두 '\n' 으로 바꾼 글(<see cref="Text"/>)을 준다.
    /// 파일 끝 개행은 편집기에서 마지막 빈 줄로 보이므로 따로 기억하지 않아도 그대로 돌아간다.
    /// 디스크를 건드리지 않는 순수 코드만 둔다(읽고 쓰기는 부르는 쪽이 한다).
    /// </summary>
    public sealed class GitTextFile
    {
        /// <summary>
        /// 줄 끝을 '\n' 으로 맞춘 글.
        /// </summary>
        public string Text { get; }

        /// <summary>
        /// 원래 글의 줄들(줄 끝 없이). <see cref="Text"/> 를 '\n' 으로 나눈 것과 같다.
        /// </summary>
        public IReadOnlyList<string> Lines { get; }

        /// <summary>
        /// 줄 사이의 원래 줄 끝. <see cref="Lines"/> 보다 하나 적다.
        /// </summary>
        public IReadOnlyList<string> Separators { get; }

        /// <summary>
        /// 새로 생긴 줄에 쓸 줄 끝. 원래 파일에 가장 많이 쓰인 것(줄 끝이 하나도 없으면 "\n").
        /// </summary>
        public string DefaultSeparator { get; }

        /// <summary>
        /// 파일 맨 앞의 BOM 바이트(없으면 빈 배열). 저장할 때 그대로 앞에 쓴다.
        /// </summary>
        public byte[] Preamble { get; }

        /// <summary>
        /// 글 인코딩(BOM 을 내보내지 않고, 잘못된 바이트는 예외를 낸다).
        /// </summary>
        public Encoding Encoding { get; }

        private GitTextFile(string text, Encoding encoding, byte[] preamble)
        {
            Encoding = encoding;
            Preamble = preamble;
            List<string> lines = new();
            List<string> separators = new();
            Split(text, lines, separators);
            Lines = lines;
            Separators = separators;
            DefaultSeparator = MostCommon(separators);
            Text = string.Join("\n", lines);
        }

        /// <summary>
        /// 파일 바이트를 글로 푼다. BOM 이 있으면 그 인코딩(UTF-8·UTF-16·UTF-32), 없으면 UTF-8 로만 읽는다.
        /// UTF-8 로 풀리지 않거나(CP949 같은 옛 인코딩) NUL 글자가 든 파일(바이너리)은 되살려 쓸 수 없으니 null 을 돌려준다.
        /// </summary>
        public static GitTextFile TryDecode(byte[] bytes)
        {
            if (bytes == null)
            {
                return null;
            }
            (Encoding encoding, int bomLength) = DetectEncoding(bytes);
            string text;
            try
            {
                text = encoding.GetString(bytes, bomLength, bytes.Length - bomLength);
            }
            catch (DecoderFallbackException)
            {
                return null;
            }
            if (text.IndexOf('\0') >= 0)
            {
                return null;
            }
            byte[] preamble = new byte[bomLength];
            Array.Copy(bytes, preamble, bomLength);
            return new GitTextFile(text, encoding, preamble);
        }

        /// <summary>
        /// 편집한 글(줄 끝 '\n')을 원래 인코딩·BOM·줄 끝으로 바이트로 만든다. 고치지 않았으면 읽은 바이트와 똑같다.
        /// </summary>
        public byte[] Encode(string editedText)
        {
            string[] newLines = (editedText ?? string.Empty).Split('\n');
            List<string> separators = ComposeSeparators(Lines, Separators, newLines, DefaultSeparator);
            StringBuilder builder = new(editedText?.Length + newLines.Length ?? 0);
            for (int i = 0; i < newLines.Length; i++)
            {
                builder.Append(newLines[i]);
                if (i < separators.Count)
                {
                    builder.Append(separators[i]);
                }
            }
            byte[] body = Encoding.GetBytes(builder.ToString());
            byte[] result = new byte[Preamble.Length + body.Length];
            Array.Copy(Preamble, result, Preamble.Length);
            Array.Copy(body, 0, result, Preamble.Length, body.Length);
            return result;
        }

        /// <summary>
        /// 새 줄들 사이에 쓸 줄 끝. 앞에서부터 같은 줄들과 뒤에서부터 같은 줄들은 원래 자리의 줄 끝을 그대로 쓰고,
        /// 그 사이(고친 곳)에 새로 생긴 줄 끝만 <paramref name="defaultSeparator"/> 다. 그래서 CRLF·LF 가 섞인 파일도 고치지 않은 줄은 그대로다.
        /// </summary>
        public static List<string> ComposeSeparators(IReadOnlyList<string> oldLines, IReadOnlyList<string> oldSeparators, IReadOnlyList<string> newLines, string defaultSeparator)
        {
            int oldCount = oldLines.Count;
            int newCount = newLines.Count;
            int prefix = 0;
            int limit = Math.Min(oldCount, newCount);
            while (prefix < limit && oldLines[prefix] == newLines[prefix])
            {
                prefix++;
            }
            int suffix = 0;
            while (suffix < limit - prefix && oldLines[oldCount - 1 - suffix] == newLines[newCount - 1 - suffix])
            {
                suffix++;
            }
            List<string> separators = new(Math.Max(0, newCount - 1));
            int shift = newCount - oldCount;
            for (int j = 0; j < newCount - 1; j++)
            {
                string separator = null;
                if (j < prefix && j < oldSeparators.Count)
                {
                    // 앞쪽의 같은 줄 j 뒤 줄 끝.
                    separator = oldSeparators[j];
                }
                else if (j + 1 >= newCount - suffix)
                {
                    // 다음 줄(j + 1)이 뒤쪽의 같은 줄이면 원래 그 줄 앞의 줄 끝.
                    int oldIndex = j - shift;
                    if (oldIndex >= 0 && oldIndex < oldSeparators.Count)
                    {
                        separator = oldSeparators[oldIndex];
                    }
                }
                separators.Add(separator ?? defaultSeparator);
            }
            return separators;
        }

        /// <summary>
        /// BOM 으로 인코딩을 고른다. 없으면 UTF-8.
        /// </summary>
        private static (Encoding encoding, int bomLength) DetectEncoding(byte[] bytes)
        {
            if (StartsWith(bytes, 0xEF, 0xBB, 0xBF))
            {
                return (new UTF8Encoding(false, true), 3);
            }
            if (StartsWith(bytes, 0xFF, 0xFE, 0x00, 0x00))
            {
                return (new UTF32Encoding(false, false, true), 4);
            }
            if (StartsWith(bytes, 0x00, 0x00, 0xFE, 0xFF))
            {
                return (new UTF32Encoding(true, false, true), 4);
            }
            if (StartsWith(bytes, 0xFF, 0xFE))
            {
                return (new UnicodeEncoding(false, false, true), 2);
            }
            if (StartsWith(bytes, 0xFE, 0xFF))
            {
                return (new UnicodeEncoding(true, false, true), 2);
            }
            return (new UTF8Encoding(false, true), 0);
        }

        private static bool StartsWith(byte[] bytes, params byte[] prefix)
        {
            if (bytes.Length < prefix.Length)
            {
                return false;
            }
            for (int i = 0; i < prefix.Length; i++)
            {
                if (bytes[i] != prefix[i])
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 글을 줄과 줄 끝("\r\n"·"\n"·"\r")으로 나눈다. 줄은 늘 줄 끝보다 하나 많다.
        /// </summary>
        private static void Split(string text, List<string> lines, List<string> separators)
        {
            int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c != '\n' && c != '\r')
                {
                    continue;
                }
                lines.Add(text.Substring(start, i - start));
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    separators.Add("\r\n");
                    i++;
                }
                else
                {
                    separators.Add(c == '\r' ? "\r" : "\n");
                }
                start = i + 1;
            }
            lines.Add(text.Substring(start));
        }

        private static string MostCommon(List<string> separators)
        {
            int crlf = 0;
            int lf = 0;
            int cr = 0;
            foreach (string separator in separators)
            {
                switch (separator)
                {
                    case "\r\n":
                        crlf++;
                        break;
                    case "\n":
                        lf++;
                        break;
                    default:
                        cr++;
                        break;
                }
            }
            if (crlf == 0 && lf == 0 && cr == 0)
            {
                return "\n";
            }
            if (crlf >= lf && crlf >= cr)
            {
                return "\r\n";
            }
            return lf >= cr ? "\n" : "\r";
        }
    }
}
