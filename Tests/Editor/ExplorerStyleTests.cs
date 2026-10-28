using NUnit.Framework;

namespace GitVersionControl.Tests
{
    /// <summary>
    /// 브랜치 탐색기 모양 규칙(<see cref="GitExplorerStyle"/>): GitKraken 배치는 가로 그래프 고정, Fork · Unity VCS 는 고른 대로.
    /// </summary>
    public sealed class ExplorerStyleTests
    {
        [TestCase(GitLayoutStyle.GitKraken, ExplorerStyle.CommitList, ExplorerStyle.BranchGraph)]
        [TestCase(GitLayoutStyle.GitKraken, ExplorerStyle.BranchGraph, ExplorerStyle.BranchGraph)]
        [TestCase(GitLayoutStyle.Fork, ExplorerStyle.CommitList, ExplorerStyle.CommitList)]
        [TestCase(GitLayoutStyle.Fork, ExplorerStyle.BranchGraph, ExplorerStyle.BranchGraph)]
        [TestCase(GitLayoutStyle.UnityVcs, ExplorerStyle.CommitList, ExplorerStyle.CommitList)]
        [TestCase(GitLayoutStyle.UnityVcs, ExplorerStyle.BranchGraph, ExplorerStyle.BranchGraph)]
        public void Test_EffectiveFor(GitLayoutStyle layout, ExplorerStyle chosen, ExplorerStyle expected)
        {
            Assert.AreEqual(expected, GitExplorerStyle.EffectiveFor(layout, chosen));
            Assert.AreEqual(layout != GitLayoutStyle.GitKraken, GitExplorerStyle.IsChoosableFor(layout));
        }
    }
}
