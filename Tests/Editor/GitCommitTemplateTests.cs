using NUnit.Framework;

namespace GitVersionControl.Tests
{
    /// <summary>
    /// 커밋 템플릿(commit.template)을 입력란에 나누어 채우고, 커밋할 때 '#' 안내 줄을 빼는 규칙.
    /// </summary>
    public sealed class GitCommitTemplateTests
    {
        private const string Template =
            "# <Prefix>: <간결한 한글 제목>\r\n" +
            "#\r\n" +
            "# (필요할 때만) 본문 — 왜/무엇을 바꿨는지 불릿으로\r\n" +
            "# -\r\n" +
            "#\r\n" +
            "# Feat      새로운 기능 추가\r\n" +
            "# Fix       버그 수정\r\n";

        [Test]
        public void Test_Split_FirstLineIsSubject_EvenIfComment()
        {
            (string subject, string description) = GitCommitTemplate.Split(Template);

            Assert.AreEqual("# <Prefix>: <간결한 한글 제목>", subject);
            StringAssert.StartsWith("#\n# (필요할 때만)", description);
            StringAssert.EndsWith("# Fix       버그 수정", description);
        }

        [Test]
        public void Test_StripComments_UntouchedSubject_IsEmpty()
        {
            Assert.AreEqual(string.Empty, GitCommitTemplate.StripComments(GitCommitTemplate.Split(Template).subject));
        }

        [Test]
        public void Test_Split_TextFirstLine_BecomesSubject()
        {
            (string subject, string description) = GitCommitTemplate.Split("Feat: \n\n# 안내\n");

            Assert.AreEqual("Feat:", subject);
            Assert.AreEqual("# 안내", description);
        }

        [Test]
        public void Test_StripComments_UntouchedTemplate_IsEmpty()
        {
            Assert.AreEqual(string.Empty, GitCommitTemplate.StripComments(Template));
        }

        [Test]
        public void Test_StripComments_KeepsWrittenMessage()
        {
            string message = CommitBox.JoinMessage("Feat: 커밋 템플릿 지원", "- 입력란에 안내문을 채움  \n\n\n- 커밋할 때 주석 줄을 뺌\n" + Template);

            Assert.AreEqual("Feat: 커밋 템플릿 지원\n\n- 입력란에 안내문을 채움\n\n- 커밋할 때 주석 줄을 뺌", GitCommitTemplate.StripComments(message));
        }

        [Test]
        public void Test_StripComments_OnlyLineStartIsComment()
        {
            Assert.AreEqual("Fix: 이슈 #12 수정\n\n #들여쓴 줄은 남김", GitCommitTemplate.StripComments("Fix: 이슈 #12 수정\n#지움\n\n #들여쓴 줄은 남김"));
        }

        [TestCase(".gitmessage.txt", "C:/repo", "C:/repo/.gitmessage.txt")]
        [TestCase("C:/templates/msg.txt", "C:/repo", "C:/templates/msg.txt")]
        public void Test_ResolvePath_RelativeToRepoRoot(string path, string repoRoot, string expected)
        {
            Assert.AreEqual(expected, GitCommitTemplate.ResolvePath(path, repoRoot).Replace('\\', '/'));
        }
    }
}
