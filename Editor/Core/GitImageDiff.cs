using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace GitVersionControl
{
    /// <summary>
    /// 이미지 diff 보기 방식.
    /// </summary>
    public enum ImageDiffMode
    {
        /// <summary>
        /// 이전·이후를 좌우로 나란히.
        /// </summary>
        TwoUp,

        /// <summary>
        /// 같은 자리에 겹쳐 두고 세로 경계선을 끌어 이전(왼쪽)·이후(오른쪽)를 가른다.
        /// </summary>
        Swipe,

        /// <summary>
        /// 이전 위에 이후를 반투명하게 겹친다.
        /// </summary>
        Onion,

        /// <summary>
        /// 바뀐 픽셀만 강조한 차이 이미지.
        /// </summary>
        Difference,
    }

    /// <summary>
    /// 두 이미지를 픽셀 단위로 비교한 결과. 크기는 두 이미지를 합친 캔버스(가장 넓은 너비 × 가장 높은 높이)다.
    /// </summary>
    public sealed class GitImageDiffResult
    {
        public int Width;
        public int Height;

        /// <summary>
        /// 캔버스 크기의 차이 이미지(Unity 순서: 아래 줄부터, <see cref="GitImageDiff.InvertedDifference"/>):
        /// 같은 곳은 흰색, 다른 곳은 차이를 뒤집은 색, 둘 다 투명한 곳은 투명(체커보드가 보인다).
        /// </summary>
        public Color32[] Mask;

        /// <summary>
        /// 픽셀마다 바뀌었는지(<see cref="Mask"/> 와 같은 순서).
        /// </summary>
        public bool[] Changed;

        public int ChangedPixels;
        public int TotalPixels;
        public bool SizeChanged;

        public float ChangedRatio => TotalPixels == 0 ? 0f : (float)ChangedPixels / TotalPixels;
    }

    /// <summary>
    /// 이미지 diff 의 판별·비교. 텍스처를 만들거나 파일을 읽지 않는 순수 함수만 둔다(디코딩은 <see cref="GitImageLoader"/>).
    /// </summary>
    public static class GitImageDiff
    {
        /// <summary>
        /// <c>Texture2D.LoadImage</c> 로 바로 읽는 형식.
        /// </summary>
        private static readonly string[] DirectExtensions = { ".png", ".jpg", ".jpeg" };

        /// <summary>
        /// Unity 텍스처 임포터로 읽는 형식. PSD·PSB 는 임포터 없이 <see cref="GitPsd"/> 로 읽는다.
        /// </summary>
        private static readonly string[] ImporterExtensions = { ".tga", ".exr", ".tif", ".tiff", ".bmp", ".gif", ".hdr", ".iff", ".pict" };

        public static bool IsImage(string path)
        {
            return CanDecodeDirectly(path) || GitPsd.IsPsd(path) || HasExtension(path, ImporterExtensions);
        }

        public static bool CanDecodeDirectly(string path)
        {
            return HasExtension(path, DirectExtensions);
        }

        private static bool HasExtension(string path, string[] extensions)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }
            string extension = Path.GetExtension(path);
            foreach (string candidate in extensions)
            {
                if (string.Equals(extension, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 두 이미지를 왼쪽 위에 맞춰 겹쳐 비교한다. 한쪽에만 있는 픽셀은 바뀐 것이고, 둘 다 완전히 투명하면 색이 달라도 같은 것으로 본다.
        /// </summary>
        /// <param name="left">이전 픽셀(Unity 순서: 아래 줄부터). 없으면(추가된 파일) null.</param>
        /// <param name="right">이후 픽셀. 없으면(삭제된 파일) null.</param>
        /// <param name="tolerance">채널(RGBA) 하나의 차이가 이 값보다 커야 바뀐 것으로 센다.</param>
        public static GitImageDiffResult Compare(Color32[] left, int leftWidth, int leftHeight, Color32[] right, int rightWidth, int rightHeight, int tolerance)
        {
            if (left == null)
            {
                leftWidth = 0;
                leftHeight = 0;
            }
            if (right == null)
            {
                rightWidth = 0;
                rightHeight = 0;
            }

            int width = Math.Max(leftWidth, rightWidth);
            int height = Math.Max(leftHeight, rightHeight);
            GitImageDiffResult result = new()
            {
                Width = width,
                Height = height,
                Mask = new Color32[width * height],
                Changed = new bool[width * height],
                TotalPixels = width * height,
                SizeChanged = leftWidth != rightWidth || leftHeight != rightHeight,
            };

            for (int top = 0; top < height; top++)
            {
                // 위에서 top 번째 줄. Unity 픽셀 배열은 아래 줄부터라 이미지마다 높이에 맞춰 줄 번호를 뒤집는다.
                int canvasRow = (height - 1 - top) * width;
                int leftRow = top < leftHeight ? (leftHeight - 1 - top) * leftWidth : -1;
                int rightRow = top < rightHeight ? (rightHeight - 1 - top) * rightWidth : -1;
                for (int x = 0; x < width; x++)
                {
                    bool hasLeft = leftRow >= 0 && x < leftWidth;
                    bool hasRight = rightRow >= 0 && x < rightWidth;
                    if (hasLeft == false && hasRight == false)
                    {
                        // 크기가 다른 두 이미지의 빈 모서리. 어느 쪽에도 없으므로 바뀌지 않았다.
                        continue;
                    }

                    Color32 before = hasLeft ? left[leftRow + x] : default;
                    Color32 after = hasRight ? right[rightRow + x] : default;
                    bool isChanged = hasLeft != hasRight || IsDifferent(before, after, tolerance);
                    if (isChanged)
                    {
                        result.ChangedPixels++;
                        result.Changed[canvasRow + x] = true;
                    }
                    // 한쪽에만 있는 픽셀은 없는 쪽을 투명으로 본다(default).
                    result.Mask[canvasRow + x] = InvertedDifference(before, after);
                }
            }
            return result;
        }

        private static bool IsDifferent(Color32 a, Color32 b, int tolerance)
        {
            if (a.a == 0 && b.a == 0)
            {
                return false;
            }
            return Math.Abs(a.r - b.r) > tolerance
                   || Math.Abs(a.g - b.g) > tolerance
                   || Math.Abs(a.b - b.b) > tolerance
                   || Math.Abs(a.a - b.a) > tolerance;
        }

        /// <summary>
        /// 두 쪽 다 그려진 픽셀의 차이를 키우는 배율. 작은 색 차이도 또렷한 색으로 보이게 한다.
        /// </summary>
        public const int OverlapGain = 8;

        /// <summary>
        /// 차이 이미지 한 픽셀(알파를 곱한 색으로 잰다). 같으면 흰색, 둘 다 투명하면 투명.
        /// <list type="bullet">
        /// <item>두 쪽 다 그려졌으면 <c>255 − 8 × (이전 − 이후)</c>: 부호를 살려 어느 채널이 늘고 줄었는지 색으로 보인다(갈색 → 분홍이면 자주색).</item>
        /// <item>한쪽에만 그려졌으면 <c>255 − |이전 − 이후|</c>: 있는 쪽 색의 보색(갈색이면 파랑) — 모양이 넓어지거나 줄어든 곳.</item>
        /// </list>
        /// </summary>
        public static Color32 InvertedDifference(Color32 before, Color32 after)
        {
            byte alpha = Math.Max(before.a, after.a);
            if (alpha == 0)
            {
                return default;
            }
            bool overlap = before.a > 0 && after.a > 0;
            return new Color32(
                Channel(before.r, before.a, after.r, after.a, overlap),
                Channel(before.g, before.a, after.g, after.a, overlap),
                Channel(before.b, before.a, after.b, after.a, overlap),
                alpha);

            static byte Channel(byte a, byte alphaA, byte b, byte alphaB, bool overlap)
            {
                int difference = a * alphaA / 255 - b * alphaB / 255;
                int value = overlap ? 255 - OverlapGain * difference : 255 - Math.Abs(difference);
                return (byte)Math.Clamp(value, 0, 255);
            }
        }

        /// <summary>
        /// 정보 줄에 쓸 파일 크기. 예: 512 B, 34.2 KB, 1.5 MB.
        /// </summary>
        public static string FormatBytes(long bytes)
        {
            if (bytes < 1024)
            {
                return bytes.ToString(CultureInfo.InvariantCulture) + " B";
            }
            if (bytes < 1024 * 1024)
            {
                return (bytes / 1024.0).ToString("0.#", CultureInfo.InvariantCulture) + " KB";
            }
            return (bytes / (1024.0 * 1024.0)).ToString("0.#", CultureInfo.InvariantCulture) + " MB";
        }
    }
}
