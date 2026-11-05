using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

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
        /// <summary>HTML: 꺾쇠·태그 이름·속성 이름·값을 따로 칠하고, &lt;script&gt;·&lt;style&gt; 안은 JS·CSS 로 칠한다.</summary>
        Html,
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
        /// <summary>HTML 태그 이름(html·div·!DOCTYPE).</summary>
        Tag,
        /// <summary>HTML 속성 이름(charset·href).</summary>
        Attribute,
        /// <summary>HTML 꺾쇠와 기호(&lt; &lt;/ &gt; /&gt; =).</summary>
        Punctuation,
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

        // HTML 줄 끝 상태. <script>·<style> 안(그 안의 /* */ 주석 안), 여러 줄에 걸친 여는 태그 안.
        private const int StateHtmlScript = 2;
        private const int StateHtmlScriptComment = 3;
        private const int StateHtmlStyle = 4;
        private const int StateHtmlStyleComment = 5;
        private const int StateHtmlTag = 6;
        private const int StateHtmlScriptTag = 7;
        private const int StateHtmlStyleTag = 8;

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
            "instanceof", "delete", "of",
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
                case ".svg":
                    return GitSyntaxLanguage.Xml;
                case ".html":
                case ".htm":
                case ".xhtml":
                    return GitSyntaxLanguage.Html;
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
                    return TokenizeCLike(line, state, spans);
                case GitSyntaxLanguage.Css:
                    return TokenizeCss(line, state, spans);
                case GitSyntaxLanguage.Json:
                    TokenizeJson(line, spans);
                    return StateNormal;
                case GitSyntaxLanguage.Yaml:
                    TokenizeYaml(line, spans);
                    return StateNormal;
                case GitSyntaxLanguage.Xml:
                    return TokenizeXml(line, state, spans);
                case GitSyntaxLanguage.Html:
                    return TokenizeHtml(line, state, spans);
                default:
                    return StateNormal;
            }
        }

        private static int TokenizeCLike(string line, int state, List<GitSyntaxSpan> spans)
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
            if (i <= firstNonSpace && firstNonSpace < length && line[firstNonSpace] == '#')
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
                if (c == '/' && i + 1 < length && line[i + 1] == '/')
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
                    while (end < length && IsWordChar(line[end]))
                    {
                        end++;
                    }
                    if (CSharpKeywords.Contains(line.Substring(i, end - i)))
                    {
                        Add(spans, i, end - i, GitSyntaxKind.Keyword);
                    }
                    i = end;
                    continue;
                }
                i++;
            }
            return StateNormal;
        }

        /// <summary>
        /// CSS·USS·TSS 한 줄. 선택자 자리와 선언 자리를 나눠 칠한다.
        /// <list type="bullet">
        /// <item>선택자: 타입(Button) = 태그 색, .클래스·#아이디 = 속성 색, :hover·:root = 키워드 색, &gt; * = 기호 색.</item>
        /// <item>선언: 속성 이름(color·-unity-font-style·--변수) = 키 색, 숫자·단위·#색 = 숫자 색, url()·resource()·var() 같은 함수 이름 = 키워드 색.</item>
        /// <item>@import 같은 at-규칙 = 키워드 색.</item>
        /// </list>
        /// diff 는 묶음 중간(블록 안)부터 보이기도 하므로 {·} 깊이를 줄 사이에 넘기지 않고 줄 모양으로 고른다:
        /// { 앞은 선택자, { 뒤·} 앞은 선언, 괄호 없는 줄은 ", " 로 끝나고 ": " 가 없으면 선택자 목록의 한 줄, 아니면 선언.
        /// </summary>
        private static int TokenizeCss(string line, int state, List<GitSyntaxSpan> spans)
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
            bool selector = StartsInSelector(line, i);
            // 선언 자리에서 ':' 를 지났는지(값 자리). 값 자리의 낱말은 속성 이름이 아니다.
            bool inValue = false;
            while (i < length)
            {
                char c = line[i];
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
                    int end = StringEnd(line, i, c, true);
                    Add(spans, i, end - i, GitSyntaxKind.String);
                    i = end;
                    continue;
                }
                if (c == '{')
                {
                    selector = false;
                    inValue = false;
                    i++;
                    continue;
                }
                if (c == '}')
                {
                    selector = true;
                    i++;
                    continue;
                }
                if (c == '@' && i + 1 < length && IsWordStart(line[i + 1]))
                {
                    int end = CssIdentifierEnd(line, i + 1);
                    Add(spans, i, end - i, GitSyntaxKind.Keyword);
                    // @import url(...) 의 뒤는 값처럼 칠한다(함수 이름·문자열).
                    selector = false;
                    inValue = true;
                    i = end;
                    continue;
                }
                i = selector ? TokenizeCssSelector(line, i, spans) : TokenizeCssDeclaration(line, i, ref inValue, spans);
            }
            return StateNormal;
        }

        /// <summary>
        /// 줄의 <paramref name="start"/> 자리가 선택자 자리인지. 처음 만나는 { · } · ; 로 고르고, 없으면 줄 모양으로 고른다.
        /// </summary>
        private static bool StartsInSelector(string line, int start)
        {
            for (int i = start; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"' || c == '\'')
                {
                    i = StringEnd(line, i, c, true) - 1;
                    continue;
                }
                if (c == '/' && i + 1 < line.Length && line[i + 1] == '*')
                {
                    int close = line.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    if (close < 0)
                    {
                        break;
                    }
                    i = close + 1;
                    continue;
                }
                if (c == '{')
                {
                    return true;
                }
                if (c == '}' || c == ';')
                {
                    return false;
                }
            }
            // 괄호·; 가 없는 줄: "Button:hover," 처럼 , 로 끝나는 선택자 목록 줄. "a: b," 처럼 ": " 가 있으면 여러 줄 값.
            string trimmed = StripCssComments(line, start).TrimEnd();
            return trimmed.EndsWith(",", StringComparison.Ordinal) && trimmed.Contains(": ") == false;
        }

        private static string StripCssComments(string line, int start)
        {
            StringBuilder builder = new(line.Length);
            for (int i = start; i < line.Length; i++)
            {
                if (line[i] == '/' && i + 1 < line.Length && line[i + 1] == '*')
                {
                    int close = line.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    if (close < 0)
                    {
                        break;
                    }
                    i = close + 1;
                    continue;
                }
                builder.Append(line[i]);
            }
            return builder.ToString();
        }

        /// <summary>
        /// 선택자 조각 하나를 칠한다(공백·{·} 앞에서 멈춘다).
        /// </summary>
        /// <returns>다음에 볼 자리.</returns>
        private static int TokenizeCssSelector(string line, int i, List<GitSyntaxSpan> spans)
        {
            int length = line.Length;
            char c = line[i];
            if ((c == '.' || c == '#') && i + 1 < length && IsCssIdentifierStart(line[i + 1]))
            {
                int end = CssIdentifierEnd(line, i + 1);
                Add(spans, i, end - i, GitSyntaxKind.Attribute);
                return end;
            }
            if (c == ':' && i + 1 < length && (IsWordStart(line[i + 1]) || line[i + 1] == ':'))
            {
                int start = i;
                int end = i + 1;
                if (line[end] == ':')
                {
                    end++;
                }
                end = CssIdentifierEnd(line, end);
                Add(spans, start, end - start, GitSyntaxKind.Keyword);
                return end;
            }
            if (IsCssIdentifierStart(c))
            {
                int end = CssIdentifierEnd(line, i);
                Add(spans, i, end - i, GitSyntaxKind.Tag);
                return end;
            }
            if (c == '>' || c == '*' || c == '+' || c == '~' || c == ',')
            {
                Add(spans, i, 1, GitSyntaxKind.Punctuation);
            }
            return i + 1;
        }

        /// <summary>
        /// 선언 조각 하나를 칠한다: 속성 이름, 값 안의 숫자·#색·함수 이름·따옴표 없는 url(...).
        /// </summary>
        /// <returns>다음에 볼 자리.</returns>
        private static int TokenizeCssDeclaration(string line, int i, ref bool inValue, List<GitSyntaxSpan> spans)
        {
            int length = line.Length;
            char c = line[i];
            if (c == ':')
            {
                inValue = true;
                return i + 1;
            }
            if (c == ';')
            {
                inValue = false;
                return i + 1;
            }
            if (inValue == false && (IsCssIdentifierStart(c) || c == '-'))
            {
                int end = CssIdentifierEnd(line, i);
                int next = SkipWhitespace(line, end);
                if (end > i && next < length && line[next] == ':')
                {
                    // color · -unity-text-align · --git-text 모두 속성 이름.
                    Add(spans, i, end - i, GitSyntaxKind.Key);
                }
                return Math.Max(end, i + 1);
            }
            if (inValue == false)
            {
                return i + 1;
            }
            if (c == '#')
            {
                int end = i + 1;
                while (end < length && IsHexDigit(line[end]))
                {
                    end++;
                }
                int digits = end - i - 1;
                bool isColor = (digits == 3 || digits == 4 || digits == 6 || digits == 8) && (end >= length || IsCssIdentifierChar(line[end]) == false);
                if (isColor)
                {
                    Add(spans, i, end - i, GitSyntaxKind.Number);
                    return end;
                }
                return CssIdentifierEnd(line, i + 1);
            }
            bool signedNumber = (c == '-' || c == '+' || c == '.') && i + 1 < length && char.IsDigit(line[i + 1]);
            if ((char.IsDigit(c) || signedNumber) && (i == 0 || IsCssIdentifierChar(line[i - 1]) == false))
            {
                int end = NumberEnd(line, char.IsDigit(c) ? i : i + 1);
                if (end < length && line[end] == '%')
                {
                    end++;
                }
                Add(spans, i, end - i, GitSyntaxKind.Number);
                return end;
            }
            if (IsCssIdentifierStart(c) || c == '-')
            {
                int end = CssIdentifierEnd(line, i);
                if (end > i && end < length && line[end] == '(')
                {
                    // url() · resource() · var() · rgb() 같은 함수 이름. url·resource 의 따옴표 없는 인자는 문자열 색.
                    Add(spans, i, end - i, GitSyntaxKind.Keyword);
                    string name = line.Substring(i, end - i);
                    int argument = SkipWhitespace(line, end + 1);
                    bool isPath = string.Equals(name, "url", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "resource", StringComparison.OrdinalIgnoreCase);
                    if (isPath && argument < length && line[argument] != '"' && line[argument] != '\'' && line[argument] != ')')
                    {
                        int close = line.IndexOf(')', argument);
                        int stop = close < 0 ? length : close;
                        Add(spans, argument, stop - argument, GitSyntaxKind.String);
                        return stop;
                    }
                    return end + 1;
                }
                return Math.Max(end, i + 1);
            }
            return i + 1;
        }

        private static bool IsCssIdentifierStart(char c)
        {
            return char.IsLetter(c) || c == '_';
        }

        private static bool IsCssIdentifierChar(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_' || c == '-';
        }

        /// <summary>
        /// <paramref name="start"/> 부터 CSS 이름(-unity-font·--git-text·unity-scroller__slider)이 끝난 자리.
        /// </summary>
        private static int CssIdentifierEnd(string line, int start)
        {
            int i = start;
            while (i < line.Length && IsCssIdentifierChar(line[i]))
            {
                i++;
            }
            return i;
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
        /// HTML 한 줄. GitKraken 처럼 꺾쇠(&lt; &gt;)·태그 이름·속성 이름·속성 값을 따로 칠한다.
        /// &lt;script&gt;·&lt;style&gt; 의 내용은 닫는 태그까지 JS(<see cref="TokenizeCLike"/>)·CSS 로 칠한다.
        /// </summary>
        private static int TokenizeHtml(string line, int state, List<GitSyntaxSpan> spans)
        {
            int i = 0;
            int length = line.Length;
            while (i < length)
            {
                switch (state)
                {
                    case StateBlockComment:
                    {
                        int close = line.IndexOf("-->", i, StringComparison.Ordinal);
                        if (close < 0)
                        {
                            Add(spans, i, length - i, GitSyntaxKind.Comment);
                            return StateBlockComment;
                        }
                        Add(spans, i, close + 3 - i, GitSyntaxKind.Comment);
                        i = close + 3;
                        state = StateNormal;
                        continue;
                    }
                    case StateHtmlScript:
                    case StateHtmlScriptComment:
                    case StateHtmlStyle:
                    case StateHtmlStyleComment:
                    {
                        bool isScript = state == StateHtmlScript || state == StateHtmlScriptComment;
                        bool inComment = state == StateHtmlScriptComment || state == StateHtmlStyleComment;
                        int close = line.IndexOf(isScript ? "</script" : "</style", i, StringComparison.OrdinalIgnoreCase);
                        int end = close < 0 ? length : close;
                        int inner = TokenizeEmbedded(line, i, end, isScript == false, inComment ? StateBlockComment : StateNormal, spans);
                        if (close < 0)
                        {
                            bool endsInComment = inner == StateBlockComment;
                            return isScript ? (endsInComment ? StateHtmlScriptComment : StateHtmlScript) : (endsInComment ? StateHtmlStyleComment : StateHtmlStyle);
                        }
                        i = close;
                        state = StateNormal;
                        continue;
                    }
                    case StateHtmlTag:
                    case StateHtmlScriptTag:
                    case StateHtmlStyleTag:
                        i = TokenizeHtmlTagBody(line, i, ref state, spans);
                        continue;
                }
                // 보통 글: 다음 < 까지는 칠하지 않는다.
                int open = line.IndexOf('<', i);
                if (open < 0)
                {
                    return StateNormal;
                }
                i = open;
                if (string.CompareOrdinal(line, i, "<!--", 0, 4) == 0)
                {
                    state = StateBlockComment;
                    continue;
                }
                char after = i + 1 < length ? line[i + 1] : '\0';
                bool closing = after == '/';
                bool bang = after == '!';
                // </ 는 꺾쇠 색, <!DOCTYPE 의 ! 는 태그 이름 쪽에 붙여 칠한다(GitKraken 과 같다).
                int punctuationEnd = closing ? i + 2 : i + 1;
                int nameStart = closing || bang ? i + 2 : i + 1;
                int nameEnd = nameStart;
                while (nameEnd < length && IsHtmlNameChar(line[nameEnd]))
                {
                    nameEnd++;
                }
                if (nameEnd == nameStart)
                {
                    // "a < b" 처럼 태그가 아닌 < 는 그냥 글.
                    i++;
                    continue;
                }
                Add(spans, i, punctuationEnd - i, GitSyntaxKind.Punctuation);
                Add(spans, punctuationEnd, nameEnd - punctuationEnd, GitSyntaxKind.Tag);
                string name = line.Substring(nameStart, nameEnd - nameStart);
                state = closing ? StateHtmlTag
                    : string.Equals(name, "script", StringComparison.OrdinalIgnoreCase) ? StateHtmlScriptTag
                    : string.Equals(name, "style", StringComparison.OrdinalIgnoreCase) ? StateHtmlStyleTag
                    : StateHtmlTag;
                i = nameEnd;
            }
            return state;
        }

        /// <summary>
        /// 여는·닫는 태그의 이름 뒤(속성들). &gt; 를 만나면 태그가 끝나고, &lt;script&gt;·&lt;style&gt; 이면 그 내용 상태로 넘어간다.
        /// </summary>
        /// <returns>다음에 볼 자리.</returns>
        private static int TokenizeHtmlTagBody(string line, int i, ref int state, List<GitSyntaxSpan> spans)
        {
            int length = line.Length;
            while (i < length)
            {
                char c = line[i];
                if (c == ' ' || c == '\t')
                {
                    i++;
                    continue;
                }
                if (c == '>' || (c == '/' && i + 1 < length && line[i + 1] == '>'))
                {
                    bool selfClosing = c == '/';
                    int end = selfClosing ? i + 2 : i + 1;
                    Add(spans, i, end - i, GitSyntaxKind.Punctuation);
                    state = selfClosing ? StateNormal
                        : state == StateHtmlScriptTag ? StateHtmlScript
                        : state == StateHtmlStyleTag ? StateHtmlStyle
                        : StateNormal;
                    return end;
                }
                if (c == '=')
                {
                    Add(spans, i, 1, GitSyntaxKind.Punctuation);
                    i++;
                    continue;
                }
                if (c == '"' || c == '\'')
                {
                    int end = StringEnd(line, i, c, false);
                    Add(spans, i, end - i, GitSyntaxKind.String);
                    i = end;
                    continue;
                }
                int start = i;
                bool isValue = start > 0 && PreviousNonWhitespace(line, start) == '=';
                while (i < length && line[i] != ' ' && line[i] != '\t' && line[i] != '>' && line[i] != '=' && line[i] != '"' && line[i] != '\''
                       && (line[i] != '/' || i + 1 >= length || line[i + 1] != '>'))
                {
                    i++;
                }
                if (i == start)
                {
                    i++;
                    continue;
                }
                // 따옴표 없는 값(width=10)은 값 색, 나머지는 속성 이름(<!DOCTYPE html> 의 html 도).
                Add(spans, start, i - start, isValue ? GitSyntaxKind.String : GitSyntaxKind.Attribute);
            }
            return i;
        }

        /// <summary>
        /// 줄의 [<paramref name="from"/>, <paramref name="to"/>) 를 JS·CSS 로 칠한다(조각 자리는 줄 기준).
        /// </summary>
        /// <returns>그 구간 끝 상태(<see cref="StateNormal"/>·<see cref="StateBlockComment"/>).</returns>
        private static int TokenizeEmbedded(string line, int from, int to, bool isCss, int state, List<GitSyntaxSpan> spans)
        {
            if (to <= from)
            {
                return state;
            }
            List<GitSyntaxSpan> inner = new();
            string part = line.Substring(from, to - from);
            int end = isCss ? TokenizeCss(part, state, inner) : TokenizeCLike(part, state, inner);
            foreach (GitSyntaxSpan span in inner)
            {
                spans.Add(new GitSyntaxSpan(span.Start + from, span.Length, span.Kind));
            }
            return end;
        }

        private static char PreviousNonWhitespace(string line, int index)
        {
            for (int i = index - 1; i >= 0; i--)
            {
                if (line[i] != ' ' && line[i] != '\t')
                {
                    return line[i];
                }
            }
            return '\0';
        }

        private static bool IsHtmlNameChar(char c)
        {
            return char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == ':' || c == '.';
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
