using System.Collections.Generic;
using System.Text;
using NUnit.Framework;

namespace GitVersionControl.Tests
{
    /// <summary>
    /// <see cref="GitCommand.BuildArguments"/> 가 CommandLineToArgvW 규칙대로 인용하는지 확인한다.
    /// 기대 문자열 비교와 함께, 같은 규칙으로 다시 쪼갠 결과가 원래 인자와 같은지(왕복)도 본다.
    /// </summary>
    public sealed class GitCommandArgumentsTests
    {
        [Test]
        public void Test_BuildArguments_PlainArgsAreJoinedWithSpaces()
        {
            Assert.AreEqual("status --porcelain=v1 -z", GitCommand.BuildArguments(new[] { "status", "--porcelain=v1", "-z" }));
        }

        [Test]
        public void Test_BuildArguments_EmptyList_IsEmpty()
        {
            Assert.AreEqual(string.Empty, GitCommand.BuildArguments(new string[0]));
        }

        [TestCase("", "\"\"")]
        [TestCase("a b", "\"a b\"")]
        [TestCase("tab\there", "\"tab\there\"")]
        [TestCase("line\nbreak", "\"line\nbreak\"")]
        [TestCase("say \"hi\"", "\"say \\\"hi\\\"\"")]
        [TestCase("a\\\"b", "\"a\\\\\\\"b\"")]
        [TestCase("C:\\with space\\", "\"C:\\with space\\\\\"")]
        [TestCase("C:\\no\\space\\", "C:\\no\\space\\")]
        [TestCase("a\\\\b c", "\"a\\\\b c\"")]
        [TestCase("Assets/한글 파일.png", "\"Assets/한글 파일.png\"")]
        [TestCase("Assets/한글.png", "Assets/한글.png")]
        public void Test_BuildArguments_QuotesSingleArgument(string arg, string expected)
        {
            Assert.AreEqual(expected, GitCommand.BuildArguments(new[] { arg }));
        }

        [Test]
        public void Test_BuildArguments_RoundTripsThroughArgvRules()
        {
            string[] args =
            {
                "commit", "-m", "제목 \"따옴표\"\n\n본문 \\ 역슬래시", "--", "Assets/With Space/", "C:\\dir\\", "end\\\\", "\\\"", "", "plain",
            };

            List<string> parsed = ParseLikeCommandLineToArgvW(GitCommand.BuildArguments(args));

            CollectionAssert.AreEqual(args, parsed);
        }

        /// <summary>
        /// CommandLineToArgvW 의 인자 분리 규칙(첫 인자가 프로그램 이름이 아닌 경우).
        /// 따옴표 앞 역슬래시 2n 개 → n 개 + 따옴표 열기/닫기, 2n+1 개 → n 개 + 따옴표 글자, 그 밖의 역슬래시는 그대로.
        /// </summary>
        private static List<string> ParseLikeCommandLineToArgvW(string commandLine)
        {
            List<string> result = new();
            StringBuilder current = new();
            bool inQuotes = false;
            bool hasArg = false;
            int i = 0;
            while (i < commandLine.Length)
            {
                char c = commandLine[i];
                if (c == '\\')
                {
                    int count = 0;
                    while (i < commandLine.Length && commandLine[i] == '\\')
                    {
                        count++;
                        i++;
                    }
                    if (i < commandLine.Length && commandLine[i] == '"')
                    {
                        current.Append('\\', count / 2);
                        if (count % 2 == 1)
                        {
                            current.Append('"');
                            i++;
                        }
                    }
                    else
                    {
                        current.Append('\\', count);
                    }
                    hasArg = true;
                    continue;
                }
                if (c == '"')
                {
                    inQuotes = inQuotes == false;
                    hasArg = true;
                    i++;
                    continue;
                }
                if ((c == ' ' || c == '\t') && inQuotes == false)
                {
                    if (hasArg)
                    {
                        result.Add(current.ToString());
                        current.Clear();
                        hasArg = false;
                    }
                    i++;
                    continue;
                }
                current.Append(c);
                hasArg = true;
                i++;
            }
            if (hasArg)
            {
                result.Add(current.ToString());
            }
            return result;
        }
    }
}
