using System.Text;
using NUnit.Framework;

namespace GitVersionControl.Tests
{
    /// <summary>
    /// LFS 주소 파일 판별: 진짜 주소 파일, 실제 내용(PSD·PNG), 비슷하지만 아닌 글.
    /// </summary>
    public sealed class GitLfsTests
    {
        private const string Pointer =
            "version https://git-lfs.github.com/spec/v1\n" +
            "oid sha256:adf0a091c359d5afcd5c6e15c81203496807f760ee71a1bd02b59e0aff7132b1\n" +
            "size 2834512\n";

        [Test]
        public void Test_ParsePointerSize_Pointer_ReturnsSize()
        {
            Assert.AreEqual(2834512L, GitLfs.ParsePointerSize(Encoding.UTF8.GetBytes(Pointer)));
        }

        [Test]
        public void Test_ParsePointerSize_CrLf_ReturnsSize()
        {
            Assert.AreEqual(2834512L, GitLfs.ParsePointerSize(Encoding.UTF8.GetBytes(Pointer.Replace("\n", "\r\n"))));
        }

        [Test]
        public void Test_ParsePointerSize_RealFile_ReturnsNull()
        {
            Assert.IsNull(GitLfs.ParsePointerSize(new byte[] { (byte)'8', (byte)'B', (byte)'P', (byte)'S', 0, 1, 0, 0 }));
            Assert.IsNull(GitLfs.ParsePointerSize(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0xFF, 0xFE }));
        }

        [Test]
        public void Test_ParsePointerSize_NoOid_ReturnsNull()
        {
            Assert.IsNull(GitLfs.ParsePointerSize(Encoding.UTF8.GetBytes("version https://git-lfs.github.com/spec/v1\nsize 10\n")));
        }

        [Test]
        public void Test_ParsePointerSize_TooLarge_ReturnsNull()
        {
            string padded = Pointer + new string('x', GitLfs.MaxPointerBytes);
            Assert.IsNull(GitLfs.ParsePointerSize(Encoding.UTF8.GetBytes(padded)));
        }

        [Test]
        public void Test_ParsePointerSize_Empty_ReturnsNull()
        {
            Assert.IsNull(GitLfs.ParsePointerSize(new byte[0]));
            Assert.IsNull(GitLfs.ParsePointerSize(null));
        }
    }
}
