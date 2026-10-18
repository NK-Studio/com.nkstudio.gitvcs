using System.Collections.Generic;
using NUnit.Framework;

namespace GitVersionControl.Tests
{
    /// <summary>
    /// GitKraken 형태 커밋 상세(<see cref="GraphCommitDetail"/>)의 메시지 나누기·공동 작성자 읽기.
    /// </summary>
    public sealed class GraphCommitDetailTests
    {
        [Test]
        public void Test_SplitMessage_SubjectAndBody()
        {
            (string subject, string body) = GraphCommitDetail.SplitMessage("Fix: 꼬리\r\n\r\n- 첫 줄\r\n- 둘째 줄\r\n");
            Assert.AreEqual("Fix: 꼬리", subject);
            Assert.AreEqual("- 첫 줄\n- 둘째 줄", body);
        }

        [Test]
        public void Test_SplitMessage_SubjectOnly()
        {
            Assert.AreEqual(("Only subject", string.Empty), GraphCommitDetail.SplitMessage("Only subject\n"));
        }

        [Test]
        public void Test_CoAuthors_ReadsTrailersOncePerEmail()
        {
            const string message = "Fix\n\nbody\n\nCo-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>\nco-authored-by: Kim <kim@x.com>\nCo-Authored-By: Claude <NOREPLY@anthropic.com>\n";
            List<(string name, string email)> coAuthors = GraphCommitDetail.CoAuthors(message);
            Assert.AreEqual(2, coAuthors.Count);
            Assert.AreEqual(("Claude Opus 5.5", "noreply@anthropic.com"), coAuthors[0]);
            Assert.AreEqual(("Kim", "kim@x.com"), coAuthors[1]);
        }

        [Test]
        public void Test_CoAuthors_NoneWhenNoTrailer()
        {
            Assert.AreEqual(0, GraphCommitDetail.CoAuthors("Just a message").Count);
        }
    }
}
