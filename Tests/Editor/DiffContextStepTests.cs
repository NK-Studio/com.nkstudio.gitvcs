using NUnit.Framework;

namespace GitVersionControl.Tests
{
    /// <summary>
    /// diff 막대의 문맥 줄 −/+ 단계(<see cref="GitDiffPanel.NextContextStep"/>): 한 번에 한 칸, 끝에서는 그대로, 단계 밖 값은 바로 위·아래 단계로.
    /// </summary>
    public sealed class DiffContextStepTests
    {
        [TestCase(3, 1, 5)]
        [TestCase(5, 1, 8)]
        [TestCase(8, -1, 5)]
        [TestCase(3, -1, 2)]
        [TestCase(0, -1, 0)]
        [TestCase(100, 1, 100)]
        [TestCase(4, 1, 5)]
        [TestCase(4, -1, 3)]
        [TestCase(250, -1, 100)]
        [TestCase(250, 1, 250)]
        public void Test_NextContextStep(int current, int direction, int expected)
        {
            Assert.AreEqual(expected, GitDiffPanel.NextContextStep(current, direction));
        }
    }
}
