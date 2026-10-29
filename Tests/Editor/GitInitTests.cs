using System;
using System.IO;
using NUnit.Framework;

namespace GitVersionControl.Tests
{
    /// <summary>
    /// git init 때 Unity 용 .gitignore 를 만드는지(<see cref="GitInit"/>). 이미 있는 .gitignore 는 덮어쓰지 않는다.
    /// </summary>
    public sealed class GitInitTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "GitVcsInitTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        [Test]
        public void Test_WriteGitignoreIfMissing_WritesUnityTemplate()
        {
            Assert.IsTrue(GitInit.WriteGitignoreIfMissing(_root));

            string text = File.ReadAllText(Path.Combine(_root, ".gitignore"));
            StringAssert.Contains("/[Ll]ibrary/", text);
            StringAssert.Contains("/[Tt]emp/", text);
            StringAssert.Contains("/[Uu]ser[Ss]ettings/", text);
            StringAssert.Contains("*.csproj", text);
            StringAssert.DoesNotContain("\r", text, "줄 끝은 LF(autocrlf 가 알아서 바꾼다)");
        }

        [Test]
        public void Test_WriteGitignoreIfMissing_KeepsExistingFile()
        {
            string path = Path.Combine(_root, ".gitignore");
            File.WriteAllText(path, "mine\n");

            Assert.IsFalse(GitInit.WriteGitignoreIfMissing(_root));
            Assert.AreEqual("mine\n", File.ReadAllText(path));
        }

        [Test]
        public void Test_UnityGitignore_KeepsProjectFilesTracked()
        {
            // Assets·Packages·ProjectSettings 는 저장소에 올라가야 한다.
            foreach (string line in GitInit.UnityGitignore.Split('\n'))
            {
                string rule = line.Trim();
                Assert.IsFalse(rule == "/[Aa]ssets/" || rule == "/[Pp]ackages/" || rule == "/[Pp]roject[Ss]ettings/", rule);
            }
        }
    }
}
