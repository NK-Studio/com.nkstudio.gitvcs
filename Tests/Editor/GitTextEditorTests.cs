using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace GitVersionControl.Tests
{
    /// <summary>
    /// diff 의 "이 파일 편집": 인코딩·BOM·줄 끝을 지키는 읽기·쓰기, 편집 글과 되돌리기, 문법 색칠.
    /// </summary>
    public sealed class GitTextEditorTests
    {
        private static byte[] Utf8(string text)
        {
            return new UTF8Encoding(false).GetBytes(text);
        }

        // ── GitTextFile ──

        [TestCase("a\r\nb\r\n")]
        [TestCase("a\nb\n")]
        [TestCase("a\nb")]
        [TestCase("a\r\nb\nc\rd")]
        [TestCase("")]
        [TestCase("\r\n\r\n")]
        [TestCase("한글 줄\r\n둘째")]
        public void Test_Encode_Unchanged_ReturnsSameBytes(string text)
        {
            byte[] bytes = Utf8(text);
            GitTextFile file = GitTextFile.TryDecode(bytes);

            CollectionAssert.AreEqual(bytes, file.Encode(file.Text));
        }

        [Test]
        public void Test_Decode_NormalizesLineEndingsToLf()
        {
            GitTextFile file = GitTextFile.TryDecode(Utf8("a\r\nb\rc\nd"));

            Assert.AreEqual("a\nb\nc\nd", file.Text);
        }

        [Test]
        public void Test_Encode_Utf8Bom_KeepsBom()
        {
            byte[] bytes = new UTF8Encoding(true).GetPreamble().Concat(Utf8("x\r\n")).ToArray();
            GitTextFile file = GitTextFile.TryDecode(bytes);

            byte[] saved = file.Encode("y\n");

            CollectionAssert.AreEqual(new byte[] { 0xEF, 0xBB, 0xBF, (byte)'y', (byte)'\r', (byte)'\n' }, saved);
        }

        [Test]
        public void Test_Encode_Utf16LittleEndian_RoundTrips()
        {
            Encoding utf16 = new UnicodeEncoding(false, true);
            byte[] bytes = utf16.GetPreamble().Concat(utf16.GetBytes("가\r\n나")).ToArray();
            GitTextFile file = GitTextFile.TryDecode(bytes);

            Assert.AreEqual("가\n나", file.Text);
            CollectionAssert.AreEqual(bytes, file.Encode(file.Text));
            CollectionAssert.AreEqual(utf16.GetPreamble().Concat(utf16.GetBytes("가\r\n다")).ToArray(), file.Encode("가\n다"));
        }

        [Test]
        public void Test_Decode_InvalidUtf8_ReturnsNull()
        {
            // CP949 의 "가"(B0 A1)는 UTF-8 로 풀리지 않는다.
            Assert.IsNull(GitTextFile.TryDecode(new byte[] { 0xB0, 0xA1, 0x0A }));
        }

        [Test]
        public void Test_Decode_NulByte_ReturnsNull()
        {
            Assert.IsNull(GitTextFile.TryDecode(new byte[] { 0x41, 0x00, 0x42 }));
        }

        [Test]
        public void Test_Encode_InsertedLineInCrlfFile_UsesCrlf()
        {
            GitTextFile file = GitTextFile.TryDecode(Utf8("a\r\nb\r\n"));

            Assert.AreEqual("a\r\nnew\r\nb\r\n", Encoding.UTF8.GetString(file.Encode("a\nnew\nb\n")));
        }

        [Test]
        public void Test_Encode_MixedEndings_KeepsUntouchedLines()
        {
            GitTextFile file = GitTextFile.TryDecode(Utf8("one\ntwo\r\nthree\nfour\r\n"));

            string saved = Encoding.UTF8.GetString(file.Encode("one\ntwo\r\nTHREE\nfour\n".Replace("\r", string.Empty)));

            Assert.AreEqual("one\ntwo\r\nTHREE\nfour\r\n", saved);
        }

        [Test]
        public void Test_Encode_NoFinalNewline_StaysWithout()
        {
            GitTextFile file = GitTextFile.TryDecode(Utf8("a\r\nb"));

            Assert.AreEqual("a\r\nb2", Encoding.UTF8.GetString(file.Encode("a\nb2")));
        }

        [Test]
        public void Test_Encode_EditLastLine_KeepsFinalNewline()
        {
            GitTextFile file = GitTextFile.TryDecode(Utf8("a\r\nb\r\n"));

            Assert.AreEqual("a\r\nb2\r\n", Encoding.UTF8.GetString(file.Encode("a\nb2\n")));
        }

        [Test]
        public void Test_Encode_DeletedLine_KeepsNeighbourEndings()
        {
            GitTextFile file = GitTextFile.TryDecode(Utf8("a\nb\r\nc\n"));

            Assert.AreEqual("a\nc\n", Encoding.UTF8.GetString(file.Encode("a\nc\n")));
        }

        [Test]
        public void Test_DefaultSeparator_IsMostCommon()
        {
            Assert.AreEqual("\r\n", GitTextFile.TryDecode(Utf8("a\r\nb\r\nc\n")).DefaultSeparator);
            Assert.AreEqual("\n", GitTextFile.TryDecode(Utf8("a\nb\nc\r\n")).DefaultSeparator);
            Assert.AreEqual("\n", GitTextFile.TryDecode(Utf8("single")).DefaultSeparator);
        }

        // ── GitTextBuffer ──

        private static GitTextPosition P(int line, int column)
        {
            return new GitTextPosition(line, column);
        }

        private static GitTextPosition Type(GitTextBuffer buffer, GitTextPosition at, string text)
        {
            return buffer.Replace(at, at, text, GitTextEditKind.Typing, at, at);
        }

        [Test]
        public void Test_Replace_MultiLineInsert_SplitsLines()
        {
            GitTextBuffer buffer = new("abc");

            GitTextPosition end = buffer.Replace(P(0, 1), P(0, 2), "X\nY\nZ", GitTextEditKind.Other, P(0, 1), P(0, 2));

            Assert.AreEqual("aX\nY\nZc", buffer.GetText());
            Assert.AreEqual(P(2, 1), end);
        }

        [Test]
        public void Test_Replace_AcrossLines_JoinsLines()
        {
            GitTextBuffer buffer = new("one\ntwo\nthree");

            GitTextPosition end = buffer.Replace(P(0, 2), P(2, 1), string.Empty, GitTextEditKind.Deleting, P(0, 2), P(2, 1));

            Assert.AreEqual("onhree", buffer.GetText());
            Assert.AreEqual(P(0, 2), end);
            Assert.AreEqual(0, buffer.FirstChangedLine);
        }

        [Test]
        public void Test_Replace_PastedCrlf_IsNormalized()
        {
            GitTextBuffer buffer = new(string.Empty);

            buffer.Replace(P(0, 0), P(0, 0), "a\r\nb\rc", GitTextEditKind.Other, P(0, 0), P(0, 0));

            Assert.AreEqual("a\nb\nc", buffer.GetText());
        }

        [Test]
        public void Test_GetText_Range_UsesLf()
        {
            GitTextBuffer buffer = new("one\ntwo\nthree");

            Assert.AreEqual("e\ntwo\nth", buffer.GetText(P(2, 2), P(0, 2)));
        }

        [Test]
        public void Test_Undo_MergesTypingIntoWords()
        {
            GitTextBuffer buffer = new(string.Empty);
            GitTextPosition at = P(0, 0);
            foreach (char c in "ab cd")
            {
                at = Type(buffer, at, c.ToString());
            }

            buffer.Undo();
            Assert.AreEqual("ab ", buffer.GetText());
            buffer.Undo();
            Assert.AreEqual(string.Empty, buffer.GetText());
            Assert.IsFalse(buffer.CanUndo);
        }

        [Test]
        public void Test_Undo_BreakMerge_SeparatesTyping()
        {
            GitTextBuffer buffer = new(string.Empty);
            GitTextPosition at = Type(buffer, P(0, 0), "a");
            buffer.BreakMerge();
            Type(buffer, at, "b");

            buffer.Undo();

            Assert.AreEqual("a", buffer.GetText());
        }

        [Test]
        public void Test_Undo_Backspaces_MergeAndRestoreCaret()
        {
            GitTextBuffer buffer = new("hello");
            buffer.Replace(P(0, 4), P(0, 5), string.Empty, GitTextEditKind.Deleting, P(0, 5), P(0, 5));
            buffer.Replace(P(0, 3), P(0, 4), string.Empty, GitTextEditKind.Deleting, P(0, 4), P(0, 4));

            (GitTextPosition anchor, GitTextPosition caret)? restored = buffer.Undo();

            Assert.AreEqual("hello", buffer.GetText());
            Assert.AreEqual(P(0, 5), restored.Value.caret);
            Assert.IsFalse(buffer.CanUndo);
        }

        [Test]
        public void Test_Redo_ReappliesEdit()
        {
            GitTextBuffer buffer = new("a\nb");
            buffer.Replace(P(0, 1), P(1, 0), "-", GitTextEditKind.Other, P(0, 1), P(1, 0));
            buffer.Undo();

            (GitTextPosition anchor, GitTextPosition caret)? redone = buffer.Redo();

            Assert.AreEqual("a-b", buffer.GetText());
            Assert.AreEqual(P(0, 2), redone.Value.caret);
            Assert.IsFalse(buffer.CanRedo);
        }

        [Test]
        public void Test_IsDirty_FollowsSavePoint()
        {
            GitTextBuffer buffer = new("x");
            Assert.IsFalse(buffer.IsDirty);

            Type(buffer, P(0, 1), "y");
            Assert.IsTrue(buffer.IsDirty);
            buffer.MarkSaved();
            Assert.IsFalse(buffer.IsDirty);

            // 저장한 뒤 친 글자는 저장 전 기록과 합치지 않는다 — 되돌리면 저장한 때로 돌아간다.
            Type(buffer, P(0, 2), "z");
            buffer.Undo();
            Assert.IsFalse(buffer.IsDirty);
            Assert.AreEqual("xy", buffer.GetText());

            buffer.Undo();
            Assert.IsTrue(buffer.IsDirty);
        }

        [Test]
        public void Test_IsDirty_NewEditAfterUndoPastSave_StaysDirty()
        {
            GitTextBuffer buffer = new("x");
            Type(buffer, P(0, 1), "y");
            buffer.MarkSaved();
            buffer.Undo();
            buffer.BreakMerge();
            Type(buffer, P(0, 1), "q");

            Assert.IsTrue(buffer.IsDirty);
            buffer.Undo();
            Assert.IsTrue(buffer.IsDirty, "저장한 판(xy)은 다시 갈 수 없다");
        }

        [Test]
        public void Test_EndOf_CountsLines()
        {
            Assert.AreEqual(P(3, 5), GitTextBuffer.EndOf(P(3, 2), "abc"));
            Assert.AreEqual(P(5, 1), GitTextBuffer.EndOf(P(3, 2), "a\nb\nc"));
        }

        // ── GitSyntax ──

        private static List<(string text, GitSyntaxKind kind)> Spans(string line, GitSyntaxLanguage language, int state = GitSyntax.StateNormal)
        {
            List<GitSyntaxSpan> spans = new();
            GitSyntax.Tokenize(line, language, state, spans);
            return spans.Select(span => (line.Substring(span.Start, span.Length), span.Kind)).ToList();
        }

        [Test]
        public void Test_Tokenize_CSharp_KeywordsStringsNumbersComments()
        {
            List<(string, GitSyntaxKind)> spans = Spans("public int x = 42; string s = \"a\\\"b\"; // done", GitSyntaxLanguage.CLike);

            CollectionAssert.AreEqual(new[]
            {
                ("public", GitSyntaxKind.Keyword),
                ("int", GitSyntaxKind.Keyword),
                ("42", GitSyntaxKind.Number),
                ("string", GitSyntaxKind.Keyword),
                ("\"a\\\"b\"", GitSyntaxKind.String),
                ("// done", GitSyntaxKind.Comment),
            }, spans);
        }

        [Test]
        public void Test_Tokenize_BlockComment_CarriesStateAcrossLines()
        {
            List<GitSyntaxSpan> spans = new();
            int state = GitSyntax.Tokenize("int a; /* start", GitSyntaxLanguage.CLike, GitSyntax.StateNormal, spans);
            Assert.AreEqual(GitSyntax.StateBlockComment, state);

            List<(string, GitSyntaxKind)> next = Spans("still */ return 1;", GitSyntaxLanguage.CLike, state);

            CollectionAssert.AreEqual(new[]
            {
                ("still */", GitSyntaxKind.Comment),
                ("return", GitSyntaxKind.Keyword),
                ("1", GitSyntaxKind.Number),
            }, next);
        }

        [Test]
        public void Test_Tokenize_Json_KeysValuesLiterals()
        {
            List<(string, GitSyntaxKind)> spans = Spans("\"com.unity.burst\": \"2.0.0\", \"n\": -1.5e3, \"b\": true", GitSyntaxLanguage.Json);

            CollectionAssert.AreEqual(new[]
            {
                ("\"com.unity.burst\"", GitSyntaxKind.Key),
                ("\"2.0.0\"", GitSyntaxKind.String),
                ("\"n\"", GitSyntaxKind.Key),
                ("-1.5e3", GitSyntaxKind.Number),
                ("\"b\"", GitSyntaxKind.Key),
                ("true", GitSyntaxKind.Keyword),
            }, spans);
        }

        [Test]
        public void Test_Tokenize_Yaml_KeyNumberComment()
        {
            CollectionAssert.AreEqual(new[]
            {
                ("m_Name", GitSyntaxKind.Key),
                ("12", GitSyntaxKind.Number),
                ("# note", GitSyntaxKind.Comment),
            }, Spans("  m_Name: 12 # note", GitSyntaxLanguage.Yaml));
            // guid 처럼 숫자로 시작하는 낱말은 숫자가 아니다.
            CollectionAssert.AreEqual(new[] { ("guid", GitSyntaxKind.Key) }, Spans("guid: 7a3e9f00b1", GitSyntaxLanguage.Yaml));
            CollectionAssert.AreEqual(new[] { ("--- !u!1 &100", GitSyntaxKind.Keyword) }, Spans("--- !u!1 &100", GitSyntaxLanguage.Yaml));
        }

        [Test]
        public void Test_Tokenize_Xml_TagsAttributesComments()
        {
            CollectionAssert.AreEqual(new[]
            {
                ("ui:Button", GitSyntaxKind.Key),
                ("\"go\"", GitSyntaxKind.String),
                ("<!-- c -->", GitSyntaxKind.Comment),
            }, Spans("<ui:Button name=\"go\" /> <!-- c -->", GitSyntaxLanguage.Xml));
        }

        [Test]
        public void Test_Tokenize_Html_TagsAttributesValuesPunctuation()
        {
            CollectionAssert.AreEqual(new[]
            {
                ("<", GitSyntaxKind.Punctuation),
                ("meta", GitSyntaxKind.Tag),
                ("charset", GitSyntaxKind.Attribute),
                ("=", GitSyntaxKind.Punctuation),
                ("\"UTF-8\"", GitSyntaxKind.String),
                (">", GitSyntaxKind.Punctuation),
                ("</", GitSyntaxKind.Punctuation),
                ("p", GitSyntaxKind.Tag),
                (">", GitSyntaxKind.Punctuation),
            }, Spans("    <meta charset=\"UTF-8\">text</p>", GitSyntaxLanguage.Html));

            CollectionAssert.AreEqual(new[]
            {
                ("<", GitSyntaxKind.Punctuation),
                ("!DOCTYPE", GitSyntaxKind.Tag),
                ("html", GitSyntaxKind.Attribute),
                (">", GitSyntaxKind.Punctuation),
            }, Spans("<!DOCTYPE html>", GitSyntaxLanguage.Html));
        }

        [Test]
        public void Test_Tokenize_Html_ScriptContentIsJavaScript()
        {
            List<GitSyntaxSpan> spans = new();
            int state = GitSyntax.Tokenize("<script type=\"text/javascript\">", GitSyntaxLanguage.Html, GitSyntax.StateNormal, spans);

            List<(string, GitSyntaxKind)> body = Spans("    var url = \"manual/index.html\"; // go", GitSyntaxLanguage.Html, state);
            CollectionAssert.AreEqual(new[]
            {
                ("var", GitSyntaxKind.Keyword),
                ("\"manual/index.html\"", GitSyntaxKind.String),
                ("// go", GitSyntaxKind.Comment),
            }, body);

            List<GitSyntaxSpan> closing = new();
            int after = GitSyntax.Tokenize("</script>", GitSyntaxLanguage.Html, state, closing);
            Assert.AreEqual(GitSyntax.StateNormal, after);
            Assert.AreEqual(GitSyntaxKind.Tag, closing[1].Kind);
        }

        [Test]
        public void Test_Tokenize_Html_TagSpanningLines()
        {
            List<GitSyntaxSpan> spans = new();
            int state = GitSyntax.Tokenize("<link rel=\"stylesheet\"", GitSyntaxLanguage.Html, GitSyntax.StateNormal, spans);

            CollectionAssert.AreEqual(new[]
            {
                ("href", GitSyntaxKind.Attribute),
                ("=", GitSyntaxKind.Punctuation),
                ("\"a.css\"", GitSyntaxKind.String),
                (">", GitSyntaxKind.Punctuation),
            }, Spans("      href=\"a.css\">", GitSyntaxLanguage.Html, state));
        }

        [Test]
        public void Test_Tokenize_Uss_UnityPropertiesAreKeys()
        {
            CollectionAssert.AreEqual(new[]
            {
                ("-unity-font-style", GitSyntaxKind.Key),
            }, Spans("    -unity-font-style: bold;", GitSyntaxLanguage.Css));

            CollectionAssert.AreEqual(new[]
            {
                ("-unity-slice-left", GitSyntaxKind.Key),
                ("4", GitSyntaxKind.Number),
            }, Spans("    -unity-slice-left: 4;", GitSyntaxLanguage.Css));

            CollectionAssert.AreEqual(new[]
            {
                ("--accent-color", GitSyntaxKind.Key),
                ("#ff5c6c", GitSyntaxKind.Number),
            }, Spans("    --accent-color: #ff5c6c;", GitSyntaxLanguage.Css));
        }

        [Test]
        public void Test_Tokenize_Uss_FunctionsUnitsAndAtRules()
        {
            CollectionAssert.AreEqual(new[]
            {
                ("background-image", GitSyntaxKind.Key),
                ("resource", GitSyntaxKind.Keyword),
                ("\"Icons/d_Trash\"", GitSyntaxKind.String),
            }, Spans("    background-image: resource(\"Icons/d_Trash\");", GitSyntaxLanguage.Css));

            CollectionAssert.AreEqual(new[]
            {
                ("-unity-font-definition", GitSyntaxKind.Key),
                ("url", GitSyntaxKind.Keyword),
                ("../Fonts/A.ttf", GitSyntaxKind.String),
            }, Spans("    -unity-font-definition: url(../Fonts/A.ttf);", GitSyntaxLanguage.Css));

            CollectionAssert.AreEqual(new[]
            {
                ("@import", GitSyntaxKind.Keyword),
                ("url", GitSyntaxKind.Keyword),
                ("\"unity-theme://default\"", GitSyntaxKind.String),
            }, Spans("@import url(\"unity-theme://default\");", GitSyntaxLanguage.Css));

            CollectionAssert.AreEqual(new[]
            {
                ("max-width", GitSyntaxKind.Key),
                ("50%", GitSyntaxKind.Number),
                ("-2px", GitSyntaxKind.Number),
            }, Spans("    max-width: 50% -2px;", GitSyntaxLanguage.Css));
        }

        [Test]
        public void Test_Tokenize_Uss_SelectorsAreNotProperties()
        {
            CollectionAssert.AreEqual(new[]
            {
                ("Button", GitSyntaxKind.Tag),
                (":hover", GitSyntaxKind.Keyword),
                (",", GitSyntaxKind.Punctuation),
            }, Spans("Button:hover,", GitSyntaxLanguage.Css));

            CollectionAssert.AreEqual(new[]
            {
                (".git-themed", GitSyntaxKind.Attribute),
                (".unity-scroller", GitSyntaxKind.Attribute),
                (">", GitSyntaxKind.Punctuation),
                (".unity-scroller__slider", GitSyntaxKind.Attribute),
                (":hover", GitSyntaxKind.Keyword),
                (":enabled", GitSyntaxKind.Keyword),
            }, Spans(".git-themed .unity-scroller > .unity-scroller__slider:hover:enabled {", GitSyntaxLanguage.Css));

            // #아이디는 16진 색이 아니다.
            CollectionAssert.AreEqual(new[] { ("#delete-test-footer", GitSyntaxKind.Attribute) }, Spans("#delete-test-footer {", GitSyntaxLanguage.Css));
            CollectionAssert.AreEqual(new[] { (":root", GitSyntaxKind.Keyword) }, Spans(":root {", GitSyntaxLanguage.Css));
        }

        [Test]
        public void Test_Tokenize_Uss_SingleLineRule()
        {
            CollectionAssert.AreEqual(new[]
            {
                (".state-badge--added", GitSyntaxKind.Attribute),
                ("background-color", GitSyntaxKind.Key),
                ("var", GitSyntaxKind.Keyword),
            }, Spans(".state-badge--added { background-color: var(--git-state-added); }", GitSyntaxLanguage.Css));
        }

        [Test]
        public void Test_LanguageOf_ByExtension()
        {
            Assert.AreEqual(GitSyntaxLanguage.CLike, GitSyntax.LanguageOf("Assets/A.cs"));
            Assert.AreEqual(GitSyntaxLanguage.Json, GitSyntax.LanguageOf("Packages/manifest.json"));
            Assert.AreEqual(GitSyntaxLanguage.Yaml, GitSyntax.LanguageOf("Assets/Scenes/A.unity"));
            Assert.AreEqual(GitSyntaxLanguage.Css, GitSyntax.LanguageOf("Editor/UI/Styles/GitDiff.uss"));
            Assert.AreEqual(GitSyntaxLanguage.Xml, GitSyntax.LanguageOf("A.uxml"));
            Assert.AreEqual(GitSyntaxLanguage.Html, GitSyntax.LanguageOf("Assets/Web/index.html"));
            Assert.AreEqual(GitSyntaxLanguage.Css, GitSyntax.LanguageOf("Assets/Web/style.css"));
            Assert.AreEqual(GitSyntaxLanguage.CLike, GitSyntax.LanguageOf("Assets/Web/app.js"));
            Assert.AreEqual(GitSyntaxLanguage.None, GitSyntax.LanguageOf("README.md"));
        }
    }
}
