using System;
using System.Collections.Generic;
using System.IO;

namespace GitVersionControl
{
    /// <summary>
    /// 편집기의 간단한 문법 색칠이 아는 말 갈래.
    /// </summary>
    public enum GitSyntaxLanguage
    {
        /// <summary>색칠하지 않는다.</summary>
        None,
        /// <summary>C#·셰이더·JS 처럼 // · /* */ 주석을 쓰는 말.</summary>
        CLike,
        /// <summary>USS·CSS(/* */ 주석, 낱말 대신 속성 이름).</summary>
        Css,
        Json,
        /// <summary>YAML 과 Unity 의 텍스트 에셋(.asset·.prefab·.unity·.meta 등).</summary>
        Yaml,
        /// <summary>XML·UXML(&lt;!-- --&gt; 주석, 태그 이름).</summary>
        Xml,
    }

    /// <summary>
    /// 색칠할 조각의 갈래. 색은 테마 토큰(<c>--git-code-*</c>)에서 온다.
    /// </summary>
    public enum GitSyntaxKind
    {
        Plain,
        Keyword,
        String,
        Number,
        Comment,
        /// <summary>JSON·YAML 의 키, CSS 속성 이름, XML 태그 이름.</summary>
        Key,
    }

    /// <summary>
    /// 한 줄 안의 색칠 조각 [<see cref="Start"/>, Start + <see cref="Length"/>).
    /// </summary>
    public readonly struct GitSyntaxSpan
    {
        public readonly int Start;
        public readonly int Length;
        public readonly GitSyntaxKind Kind;

        public GitSyntaxSpan(int start, int length, GitSyntaxKind kind)
        {
            Start = start;
            Length = length;
            Kind = kind;
        }
    }

    /// <summary>
    /// 편집기용 가벼운 줄 단위 문법 색칠(문자열·숫자·주석·낱말). 정확한 파서가 아니라 타자 칠 때 느려지지 않게 한 줄씩 훑는다.
    /// 여러 줄에 걸치는 것은 블록 주석뿐이며, 줄 끝 상태(<see cref="Tokenize"/> 의 반환값)를 다음 줄에 넘긴다.
    /// </summary>
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public static class GitSyntax
    {
        /// <summary>
        /// 줄 시작 상태: 보통.
        /// </summary>
        public const int StateNormal = 0;

        /// <summary>
        /// 줄 시작 상태: 블록 주석(/* */·&lt;!-- --&gt;) 안.
        /// </summary>
        public const int StateBlockComment = 1;

        private static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal)
        {
            "abstract", "as", "async", "await", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const", "continue",
            "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float",
            "for", "foreach", "get", "goto", "if", "implicit", "in", "init", "int", "interface", "internal", "is", "let", "lock", "long", "nameof", "namespace",
            "new", "null", "object", "operator", "out", "override", "params", "partial", "private", "protected", "public", "readonly", "record", "ref",
            "return", "sbyte", "sealed", "set", "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try",
            "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "value", "var", "virtual", "void", "volatile", "when", "where", "while", "yield",
            // 셰이더·C·JS 에 흔한 낱말.
            "float2", "float3", "float4", "float4x4", "half", "half2", "half3", "half4", "fixed2", "fixed3", "fixed4", "int2", "int3", "int4",
            "sampler2D", "uniform", "inout", "function", "let", "export", "import", "from", "undefined", "unsigned", "signed", "auto", "include", "define",
        };

        private static readonly HashSet<string> LiteralKeywords = new(StringComparer.Ordinal)
        {
            "true", "false", "null", "True", "False", "Null", "NULL", "yes", "no", "on", "off", "~",
        };

        /// <summary>
        /// 파일 확장자로 말 갈래를 고른다.
        /// </summary>
        public static GitSyntaxLanguage LanguageOf(string path)
        {
            string extension = Path.GetExtension(path ?? string.Empty).ToLowerInvariant();
            switch (extension)
            {
                case ".cs":
                case ".shader":
                case ".hlsl":
                case ".cginc":
                case ".compute":
                case ".glsl":
                case ".js":
                case ".ts":
                case ".java":
                case ".c":
                case ".h":
                case ".cpp":
                case ".hpp":
                case ".mm":
                case ".m":
                case ".rs":
                case ".go":
                case ".kt":
                case ".swift":
                case ".jslib":
                case ".jspre":
                    return GitSyntaxLanguage.CLike;
                case ".uss":
                case ".tss":
                case ".css":
                case ".scss":
                    return GitSyntaxLanguage.Css;
                case ".json":
                case ".asmdef":
                case ".asmref":
                case ".inputactions":
                case ".shadergraph":
                case ".shadersubgraph":
                case ".vfx":
                    return GitSyntaxLanguage.Json;
                case ".yaml":
                case ".yml":
                case ".asset":
                case ".prefab":
                case ".unity":
                case ".meta":
                case ".mat":
                case ".anim":
                case ".controller":
                case ".overridecontroller":
                case ".mask":
                case ".physicmaterial":
                case ".lighting":
                case ".playable":
                case ".signal":
                case ".preset":
                case ".spriteatlas":
                case ".spriteatlasv2":
                    return GitSyntaxLanguage.Yaml;
                case ".xml":
                case ".uxml":
                case ".csproj":
                case ".props":
                case ".targets":
                case ".xaml":
                case ".html":
                case ".htm":
                case ".svg":
                    return GitSyntaxLanguage.Xml;
                default:
                    return GitSyntaxLanguage.None;
            }
        }

        /// <summary>
        /// 한 줄을 훑어 색칠 조각을 <paramref name="spans"/> 에 더한다(Plain 은 넣지 않는다).
        /// </summary>
        /// <param name="state">줄 시작 상태(앞 줄의 반환값, 첫 줄은 <see cref="StateNormal"/>).</param>
        /// <returns>줄 끝 상태.</returns>
        public static int Tokenize(string line, GitSyntaxLanguage language, int state, List<GitSyntaxSpan> spans)
        {
            switch (language)
            {
                case GitSyntaxLanguage.CLike:
                case GitSyntaxLanguage.Css:
                    return TokenizeCLike(line, language == GitSyntaxLanguage.Css, state, spans);
                case GitSyntaxLanguage.Json:
                    TokenizeJson(line, spans);
                    return StateNormal;
                case GitSyntaxLanguage.Yaml:
                    TokenizeYaml(line, spans);
                    return StateNormal;
                case GitSyntaxLanguage.Xml:
                    return TokenizeXml(line, state, spans);
                default:
                    return StateNormal;
            }
        }

        private static int TokenizeCLike(string line, bool isCss, int state, List<GitSyntaxSpan> spans)
        {
            int i = 0;
            int length = line.Length;
            if (state == StateBlockComment)
            {
                int close = line.IndexOf("*/", StringComparison.Ordinal);
                if (close < 0)
                {
                    Add(spans, 0, length, GitSyntaxKind.Comment);
                    return StateBlockComment;
                }
                Add(spans, 0, close + 2, GitSyntaxKind.Comment);
                i = close + 2;
            }
            // 줄 첫 글자가 #(전처리기·셰이더 #pragma)이면 그 낱말을 키워드 색으로.
            int firstNonSpace = FirstNonWhitespace(line);
            if (isCss == false && i <= firstNonSpace && firstNonSpace < length && line[firstNonSpace] == '#')
            {
                int end = firstNonSpace + 1;
                while (end < length && char.IsLetter(line[end]))
                {
                    end++;
                }
                Add(spans, firstNonSpace, end - firstNonSpace, GitSyntaxKind.Keyword);
                i = end;
            }
            while (i < length)
            {
                char c = line[i];
                if (c == '/' && i + 1 < length && line[i + 1] == '/' && isCss == false)
                {
                    Add(spans, i, length - i, GitSyntaxKind.Comment);
                    return StateNormal;
                }
                if (c == '/' && i + 1 < length && line[i + 1] == '*')
                {
                    int close = line.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    if (close < 0)
                    {
                        Add(spans, i, length - i, GitSyntaxKind.Comment);
                        return StateBlockComment;
                    }
                    Add(spans, i, close + 2 - i, GitSyntaxKind.Comment);
                    i = close + 2;
                    continue;
                }
                if (c == '"' || c == '\'')
                {
                    // C# 의 @"..." 는 \ 를 풀지 않는다.
                    bool verbatim = i > 0 && line[i - 1] == '@';
                    int end = StringEnd(line, i, c, verbatim == false);
                    int start = verbatim ? i - 1 : i;
                    Add(spans, start, end - start, GitSyntaxKind.String);
                    i = end;
                    continue;
                }
                if (char.IsDigit(c) && (i == 0 || IsWordChar(line[i - 1]) == false))
                {
                    int end = NumberEnd(line, i);
                    Add(spans, i, end - i, GitSyntaxKind.Number);
                    i = end;
                    continue;
                }
                if (IsWordStart(c))
                {
                    int end = i + 1;
                    while (end < length && (IsWordChar(line[end]) || (isCss && line[end] == '-')))
                    {
                        end++;
                    }
                    if (isCss)
                    {
                        // 속성 이름(color: …)만 키 색으로.
                        int next = SkipWhitespace(line, end);
                        if (next < length && line[next] == ':' && (i == 0 || line[i - 1] != '-' && line[i - 1] != '.'))
                        {
                            Add(spans, i, end - i, GitSyntaxKind.Key);
                        }
                    }
                    else if (CSharpKeywords.Contains(line.Substring(i, end - i)))
                    {
                        Add(spans, i, end - i, GitSyntaxKind.Keyword);
                    }
                    i = end;
                    continue;
                }
                if (isCss && c == '-' && i + 1 < length && line[i + 1] == '-')
                {
                    // 사용자 변수(--git-text)는 낱말 하나로 넘긴다.
                    int end = i + 2;
                    while (end < length && (IsWordChar(line[end]) || line[end] == '-'))
                    {
                        end++;
                    }
                    int next = SkipWhitespace(line, end);
                    if (next < length && line[next] == ':')
                    {
                        Add(spans, i, end - i, GitSyntaxKind.Key);
                    }
                    i = end;
                    continue;
                }
                if (isCss && c == '#' && i + 1 < length && IsHexDigit(line[i + 1]))
                {
                    int end = i + 1;
                    while (end < length && IsHexDigit(line[end]))
                    {
                        end++;
                    }
                    Add(spans, i, end - i, GitSyntaxKind.Number);
                    i = end;
                    continue;
                }
                i++;
            }
            return StateNormal;
        }

        private static void TokenizeJson(string line, List<GitSyntaxSpan> spans)
        {
            int i = 0;
            int length = line.Length;
            while (i < length)
            {
                char c = line[i];
                if (c == '/' && i + 1 < length && line[i + 1] == '/')
                {
                    Add(spans, i, length - i, GitSyntaxKind.Comment);
                    return;
                }
                if (c == '"')
                {
                    int end = StringEnd(line, i, '"', true);
                    int next = SkipWhitespace(line, end);
                    Add(spans, i, end - i, next < length && line[next] == ':' ? GitSyntaxKind.Key : GitSyntaxKind.String);
                    i = end;
                    continue;
                }
                if (char.IsDigit(c) || (c == '-' && i + 1 < length && char.IsDigit(line[i + 1])))
                {
                    int end = NumberEnd(line, c == '-' ? i + 1 : i);
                    Add(spans, i, end - i, GitSyntaxKind.Number);
                    i = end;
                    continue;
                }
                if (IsWordStart(c))
                {
                    int end = i + 1;
                    while (end < length && IsWordChar(line[end]))
                    {
                        end++;
                    }
                    if (LiteralKeywords.Contains(line.Substring(i, end - i)))
                    {
                        Add(spans, i, end - i, GitSyntaxKind.Keyword);
                    }
                    i = end;
                    continue;
                }
                i++;
            }
        }

        private static void TokenizeYaml(string line, List<GitSyntaxSpan> spans)
        {
            int length = line.Length;
            int i = FirstNonWhitespace(line);
            if (i >= length)
            {
                return;
            }
            if (line[i] == '#')
            {
                Add(spans, i, length - i, GitSyntaxKind.Comment);
                return;
            }
            // 문서 머리(--- !u!1 &123)·지시문(%YAML)은 키워드 색으로.
            if (line[i] == '%' || line.StartsWith("---", StringComparison.Ordinal))
            {
                Add(spans, i, length - i, GitSyntaxKind.Keyword);
                return;
            }
            if (line[i] == '-' && i + 1 < length && line[i + 1] == ' ')
            {
                i = SkipWhitespace(line, i + 1);
            }
            // 키: "이름:" 뒤가 공백·줄 끝.
            int colon = KeyColon(line, i);
            if (colon > i)
            {
                Add(spans, i, colon - i, GitSyntaxKind.Key);
                i = colon + 1;
            }
            TokenizeYamlValue(line, i, spans);
        }

        private static void TokenizeYamlValue(string line, int i, List<GitSyntaxSpan> spans)
        {
            int length = line.Length;
            while (i < length)
            {
                char c = line[i];
                if (c == '#' && (i == 0 || line[i - 1] == ' '))
                {
                    Add(spans, i, length - i, GitSyntaxKind.Comment);
                    return;
                }
                if (c == '"' || c == '\'')
                {
                    int end = StringEnd(line, i, c, c == '"');
                    Add(spans, i, end - i, GitSyntaxKind.String);
                    i = end;
                    continue;
                }
                if ((char.IsDigit(c) || (c == '-' && i + 1 < length && char.IsDigit(line[i + 1]))) && (i == 0 || IsWordChar(line[i - 1]) == false))
                {
                    int end = NumberEnd(line, c == '-' ? i + 1 : i);
                    // 숫자로 시작해도 낱말이 이어지면(guid 등) 숫자가 아니다.
                    if (end >= length || IsWordChar(line[end]) == false)
                    {
                        Add(spans, i, end - i, GitSyntaxKind.Number);
                    }
                    else
                    {
                        while (end < length && IsWordChar(line[end]))
                        {
                            end++;
                        }
                    }
                    i = end;
                    continue;
                }
                if (IsWordStart(c))
                {
                    int end = i + 1;
                    while (end < length && IsWordChar(line[end]))
                    {
                        end++;
                    }
                    if (LiteralKeywords.Contains(line.Substring(i, end - i)))
                    {
                        Add(spans, i, end - i, GitSyntaxKind.Keyword);
                    }
                    i = end;
                    continue;
                }
                i++;
            }
        }

        /// <summary>
        /// <paramref name="start"/> 부터 YAML 키의 ':' 자리. 키가 아니면 -1. 따옴표 키("a b": 1)도 받는다.
        /// </summary>
        private static int KeyColon(string line, int start)
        {
            int length = line.Length;
            int i = start;
            if (i < length && (line[i] == '"' || line[i] == '\''))
            {
                i = StringEnd(line, i, line[i], line[i] == '"');
            }
            for (; i < length; i++)
            {
                char c = line[i];
                if (c == ':' && (i + 1 == length || line[i + 1] == ' '))
                {
                    return i;
                }
                if (c == '#' || c == '{' || c == '[' || c == '"' || c == '\'')
                {
                    return -1;
                }
            }
            return -1;
        }

        private static int TokenizeXml(string line, int state, List<GitSyntaxSpan> spans)
        {
            int i = 0;
            int length = line.Length;
            if (state == StateBlockComment)
            {
                int close = line.IndexOf("-->", StringComparison.Ordinal);
                if (close < 0)
                {
                    Add(spans, 0, length, GitSyntaxKind.Comment);
                    return StateBlockComment;
                }
                Add(spans, 0, close + 3, GitSyntaxKind.Comment);
                i = close + 3;
            }
            bool inTag = false;
            while (i < length)
            {
                char c = line[i];
                if (c == '<' && string.CompareOrdinal(line, i, "<!--", 0, 4) == 0)
                {
                    int close = line.IndexOf("-->", i + 4, StringComparison.Ordinal);
                    if (close < 0)
                    {
                        Add(spans, i, length - i, GitSyntaxKind.Comment);
                        return StateBlockComment;
                    }
                    Add(spans, i, close + 3 - i, GitSyntaxKind.Comment);
                    i = close + 3;
                    continue;
                }
                if (c == '<')
                {
                    int start = i + 1;
                    if (start < length && (line[start] == '/' || line[start] == '?' || line[start] == '!'))
                    {
                        start++;
                    }
                    int end = start;
                    while (end < length && (IsWordChar(line[end]) || line[end] == ':' || line[end] == '.' || line[end] == '-'))
                    {
                        end++;
                    }
                    Add(spans, start, end - start, GitSyntaxKind.Key);
                    inTag = true;
                    i = end;
                    continue;
                }
                if (c == '>')
                {
                    inTag = false;
                }
                else if (inTag && (c == '"' || c == '\''))
                {
                    int end = StringEnd(line, i, c, false);
                    Add(spans, i, end - i, GitSyntaxKind.String);
                    i = end;
                    continue;
                }
                i++;
            }
            return StateNormal;
        }

        /// <summary>
        /// <paramref name="start"/> 의 따옴표로 시작한 문자열이 끝난 바로 뒤 자리(닫지 않았으면 줄 끝).
        /// </summary>
        private static int StringEnd(string line, int start, char quote, bool backslashEscapes)
        {
            int i = start + 1;
            while (i < line.Length)
            {
                char c = line[i];
                if (backslashEscapes && c == '\\')
                {
                    i += 2;
                    continue;
                }
                if (c == quote)
                {
                    // @"..." 와 YAML '...' 는 따옴표 두 개가 따옴표 하나다.
                    if (backslashEscapes == false && i + 1 < line.Length && line[i + 1] == quote)
                    {
                        i += 2;
                        continue;
                    }
                    return i + 1;
                }
                i++;
            }
            return line.Length;
        }

        private static int NumberEnd(string line, int start)
        {
            int i = start;
            if (i + 1 < line.Length && line[i] == '0' && (line[i + 1] == 'x' || line[i + 1] == 'X'))
            {
                i += 2;
                while (i < line.Length && IsHexDigit(line[i]))
                {
                    i++;
                }
                return i;
            }
            while (i < line.Length && (char.IsDigit(line[i]) || line[i] == '.' || line[i] == '_'))
            {
                if (line[i] == '.' && (i + 1 >= line.Length || char.IsDigit(line[i + 1]) == false))
                {
                    break;
                }
                i++;
            }
            if (i < line.Length && (line[i] == 'e' || line[i] == 'E'))
            {
                int exponent = i + 1;
                if (exponent < line.Length && (line[exponent] == '+' || line[exponent] == '-'))
                {
                    exponent++;
                }
                if (exponent < line.Length && char.IsDigit(line[exponent]))
                {
                    i = exponent;
                    while (i < line.Length && char.IsDigit(line[i]))
                    {
                        i++;
                    }
                }
            }
            // 접미사(1f, 10UL, 2px).
            while (i < line.Length && char.IsLetter(line[i]) && i - start < 32)
            {
                i++;
            }
            return i;
        }

        private static int FirstNonWhitespace(string line)
        {
            return SkipWhitespace(line, 0);
        }

        private static int SkipWhitespace(string line, int start)
        {
            int i = start;
            while (i < line.Length && (line[i] == ' ' || line[i] == '\t'))
            {
                i++;
            }
            return i;
        }

        private static bool IsWordStart(char c)
        {
            return char.IsLetter(c) || c == '_';
        }

        private static bool IsWordChar(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_';
        }

        private static bool IsHexDigit(char c)
        {
            return char.IsDigit(c) || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
        }

        private static void Add(List<GitSyntaxSpan> spans, int start, int length, GitSyntaxKind kind)
        {
            if (length > 0)
            {
                spans.Add(new GitSyntaxSpan(start, length, kind));
            }
        }
    }
}
