using System.Linq;
using NUnit.Framework;

namespace GitVersionControl.Tests
{
    /// <summary>
    /// 변경 목록에서 에셋과 그 .meta 를 한 줄로 묶는 규칙(<see cref="GitStaging.GroupMetaPairs"/>).
    /// </summary>
    public sealed class GitMetaGroupTests
    {
        private static GitStatusEntry E(string path)
        {
            return new GitStatusEntry { RepoPath = path, State = GitFileState.Modified };
        }

        [Test]
        public void Test_Group_AssetAndMeta_OneRow()
        {
            var groups = GitStaging.GroupMetaPairs(new[] { E("A.png"), E("A.png.meta"), E("B.cs") });

            Assert.AreEqual(2, groups.Count);
            Assert.AreEqual("A.png", groups[0].Main.RepoPath);
            Assert.AreEqual("A.png.meta", groups[0].Meta.RepoPath);
            Assert.AreEqual("B.cs", groups[1].Main.RepoPath);
            Assert.IsNull(groups[1].Meta);
        }

        [Test]
        public void Test_Group_MetaBeforeAsset_StillPaired()
        {
            var groups = GitStaging.GroupMetaPairs(new[] { E("A.png.meta"), E("A.png") });

            Assert.AreEqual(1, groups.Count);
            Assert.AreEqual("A.png", groups[0].Main.RepoPath);
            Assert.AreEqual("A.png.meta", groups[0].Meta.RepoPath);
        }

        [Test]
        public void Test_Group_LoneMeta_OwnRow()
        {
            // 폴더 .meta 이거나 에셋은 그대로이고 .meta 만 바뀐 경우.
            var groups = GitStaging.GroupMetaPairs(new[] { E("Folder.meta"), E("C.png.meta") });

            CollectionAssert.AreEqual(new[] { "Folder.meta", "C.png.meta" }, groups.Select(g => g.Main.RepoPath).ToArray());
            Assert.IsTrue(groups.All(g => g.Meta == null));
        }
    }
}
