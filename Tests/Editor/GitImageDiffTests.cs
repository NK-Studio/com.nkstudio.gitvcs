using NUnit.Framework;
using UnityEngine;

namespace GitVersionControl.Tests
{
    /// <summary>
    /// 이미지 diff: 형식 판별, 픽셀 비교(허용 오차·크기 차이·한쪽 없음), 파일 크기 표기.
    /// </summary>
    public sealed class GitImageDiffTests
    {
        private static readonly Color32 Red = new(255, 0, 0, 255);
        private static readonly Color32 Blue = new(0, 0, 255, 255);

        private static Color32[] Filled(int width, int height, Color32 color)
        {
            Color32[] pixels = new Color32[width * height];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = color;
            }
            return pixels;
        }

        [TestCase("Assets/A.png")]
        [TestCase("Assets/A.PNG")]
        [TestCase("Assets/a.jpeg")]
        [TestCase("Assets/a.Jpg")]
        public void Test_CanDecodeDirectly_PngJpg(string path)
        {
            Assert.IsTrue(GitImageDiff.CanDecodeDirectly(path));
            Assert.IsTrue(GitImageDiff.IsImage(path));
        }

        [TestCase("Assets/A.tga")]
        [TestCase("Assets/A.PSD")]
        [TestCase("Assets/A.exr")]
        [TestCase("Assets/A.tif")]
        public void Test_IsImage_ImporterFormats_NotDirect(string path)
        {
            Assert.IsTrue(GitImageDiff.IsImage(path));
            Assert.IsFalse(GitImageDiff.CanDecodeDirectly(path));
        }

        [TestCase("Assets/A.cs")]
        [TestCase("Assets/A.png.meta")]
        [TestCase("Assets/png")]
        [TestCase("")]
        [TestCase(null)]
        public void Test_IsImage_OtherFiles_False(string path)
        {
            Assert.IsFalse(GitImageDiff.IsImage(path));
        }

        [Test]
        public void Test_Compare_SameImage_NoChanges()
        {
            GitImageDiffResult result = GitImageDiff.Compare(Filled(4, 3, Red), 4, 3, Filled(4, 3, Red), 4, 3, 0);

            Assert.AreEqual(0, result.ChangedPixels);
            Assert.AreEqual(12, result.TotalPixels);
            Assert.IsFalse(result.SizeChanged);
            Assert.AreEqual(4, result.Width);
            Assert.AreEqual(3, result.Height);
        }

        [Test]
        public void Test_Compare_OnePixelDifferent_MarksOnlyThatPixel()
        {
            Color32[] after = Filled(4, 4, Red);
            after[5] = Blue;

            GitImageDiffResult result = GitImageDiff.Compare(Filled(4, 4, Red), 4, 4, after, 4, 4, 0);

            Assert.AreEqual(1, result.ChangedPixels);
            Assert.IsTrue(result.Changed[5]);
            Assert.IsFalse(result.Changed[4]);
        }

        [Test]
        public void Test_Compare_Tolerance_IgnoresSmallDifferences()
        {
            Color32[] after = Filled(2, 2, new Color32(250, 0, 0, 255));

            Assert.AreEqual(0, GitImageDiff.Compare(Filled(2, 2, Red), 2, 2, after, 2, 2, 5).ChangedPixels);
            Assert.AreEqual(4, GitImageDiff.Compare(Filled(2, 2, Red), 2, 2, after, 2, 2, 4).ChangedPixels);
        }

        [Test]
        public void Test_Compare_BothFullyTransparent_SameEvenIfColorDiffers()
        {
            Color32[] before = Filled(2, 2, new Color32(255, 0, 0, 0));
            Color32[] after = Filled(2, 2, new Color32(0, 255, 0, 0));

            Assert.AreEqual(0, GitImageDiff.Compare(before, 2, 2, after, 2, 2, 0).ChangedPixels);
        }

        [Test]
        public void Test_Compare_DifferentSize_AlignsTopLeft()
        {
            // 이전 2×1(빨강), 이후 2×2(빨강). 왼쪽 위에 맞추면 위 줄은 같고 아래 줄은 이후에만 있다.
            GitImageDiffResult result = GitImageDiff.Compare(Filled(2, 1, Red), 2, 1, Filled(2, 2, Red), 2, 2, 0);

            Assert.IsTrue(result.SizeChanged);
            Assert.AreEqual(2, result.ChangedPixels);
            // Unity 순서는 아래 줄부터: 0·1 이 아래 줄(이후에만 있음), 2·3 이 위 줄.
            Assert.IsTrue(result.Changed[0]);
            Assert.IsTrue(result.Changed[1]);
            Assert.IsFalse(result.Changed[2]);
            Assert.IsFalse(result.Changed[3]);
        }

        [Test]
        public void Test_Compare_DifferentSize_ComparesMatchingRowsByTop()
        {
            // 이전 1×2: 위=파랑, 아래=빨강 (배열은 아래부터: [빨강, 파랑]). 이후 1×1: 파랑.
            // 위에 맞추면 이후의 파랑은 이전의 위(파랑)와 겹치므로 같다.
            Color32[] before = { Red, Blue };
            GitImageDiffResult result = GitImageDiff.Compare(before, 1, 2, new[] { Blue }, 1, 1, 0);

            Assert.AreEqual(1, result.ChangedPixels);
            Assert.IsTrue(result.Changed[0]);
            Assert.IsFalse(result.Changed[1]);
        }

        [Test]
        public void Test_Compare_EmptyCorners_NotCounted()
        {
            // 이전 2×1, 이후 1×2. 오른쪽 아래 모서리는 어느 쪽에도 없다.
            GitImageDiffResult result = GitImageDiff.Compare(Filled(2, 1, Red), 2, 1, Filled(1, 2, Red), 1, 2, 0);

            Assert.AreEqual(4, result.TotalPixels);
            // 위 줄 오른쪽(이전에만) + 아래 줄 왼쪽(이후에만).
            Assert.AreEqual(2, result.ChangedPixels);
            Assert.AreEqual(0, result.Mask[1].a);
        }

        [Test]
        public void Test_Compare_AddedFile_AllChanged()
        {
            GitImageDiffResult result = GitImageDiff.Compare(null, 0, 0, Filled(3, 2, Red), 3, 2, 0);

            Assert.AreEqual(6, result.ChangedPixels);
            Assert.AreEqual(1f, result.ChangedRatio);
            Assert.IsTrue(result.SizeChanged);
        }

        [Test]
        public void Test_Compare_DeletedFile_AllChanged()
        {
            GitImageDiffResult result = GitImageDiff.Compare(Filled(2, 2, Red), 2, 2, null, 99, 99, 0);

            Assert.AreEqual(2, result.Width);
            Assert.AreEqual(4, result.ChangedPixels);
        }

        [TestCase(0, "0 B")]
        [TestCase(1023, "1023 B")]
        [TestCase(1024, "1 KB")]
        [TestCase(35021, "34.2 KB")]
        [TestCase(1572864, "1.5 MB")]
        public void Test_FormatBytes(long bytes, string expected)
        {
            Assert.AreEqual(expected, GitImageDiff.FormatBytes(bytes));
        }

        [Test]
        public void Test_InvertedDifference_SamePixel_White()
        {
            Color32 brown = new(140, 90, 50, 255);

            Assert.AreEqual(new Color32(255, 255, 255, 255), GitImageDiff.InvertedDifference(brown, brown));
        }

        [Test]
        public void Test_InvertedDifference_OnlyBefore_ComplementColor()
        {
            Color32 brown = new(140, 90, 50, 255);

            Assert.AreEqual(new Color32(115, 165, 205, 255), GitImageDiff.InvertedDifference(brown, default));
        }

        [Test]
        public void Test_InvertedDifference_Overlap_SignedAndAmplified()
        {
            // 갈색 → 분홍: 빨강·파랑은 늘어(음수) 흰쪽으로, 초록은 줄어(+26 × 8) 0 으로 — 자주색.
            Color32 brown = new(197, 153, 89, 255);
            Color32 pink = new(255, 127, 127, 255);

            Assert.AreEqual(new Color32(255, 47, 255, 255), GitImageDiff.InvertedDifference(brown, pink));
        }

        [Test]
        public void Test_InvertedDifference_BothTransparent_Transparent()
        {
            Assert.AreEqual(0, GitImageDiff.InvertedDifference(new Color32(255, 0, 0, 0), new Color32(0, 255, 0, 0)).a);
        }
    }
}
