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
        /// 캔버스 크기의 차이 이미지(Unity 순서: 아래 줄부터). 바뀐 픽셀은 <see cref="GitImageDiff.ChangedColor"/>, 나머지는 이후 이미지를 흐린 회색조로.
        /// </summary>
        public Color32[] Mask;

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
        public static readonly Color32 ChangedColor = new(255, 0, 255, 255);

        /// <summary>
        /// <c>Texture2D.LoadImage</c> 로 바로 읽는 형식.
        /// </summary>
        private static readonly string[] DirectExtensions = { ".png", ".jpg", ".jpeg" };

        /// <summary>
        /// Unity 텍스처 임포터로 읽는 형식.
        /// </summary>
        private static readonly string[] ImporterExtensions = { ".tga", ".psd", ".exr", ".tif", ".tiff", ".bmp", ".gif", ".hdr", ".iff", ".pict" };

        public static bool IsImage(string path)
        {
            return CanDecodeDirectly(path) || HasExtension(path, ImporterExtensions);
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
                        result.Mask[canvasRow + x] = ChangedColor;
                    }
                    else
                    {
                        result.Mask[canvasRow + x] = Faded(hasRight ? after : before);
                    }
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
        /// 바뀌지 않은 픽셀을 어둡고 흐린 회색조로. 강조색이 눈에 띄게 하면서 이미지 윤곽은 남긴다.
        /// </summary>
        private static Color32 Faded(Color32 color)
        {
            int luminance = (color.r * 299 + color.g * 587 + color.b * 114) / 1000;
            byte gray = (byte)(48 + luminance * 80 / 255);
            return new Color32(gray, gray, gray, (byte)(color.a / 2));
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
