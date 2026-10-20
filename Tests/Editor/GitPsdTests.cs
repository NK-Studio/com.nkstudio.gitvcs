using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace GitVersionControl.Tests
{
    /// <summary>
    /// PSD 읽기: 형식 판별, 합쳐진 이미지(Raw·RLE·알파), 합쳐진 이미지가 없을 때 레이어 합치기, 망가진 파일.
    /// 메모리에서 아주 작은 PSD 를 직접 만들어 확인한다.
    /// </summary>
    public sealed class GitPsdTests
    {
        [TestCase("Assets/A.psd")]
        [TestCase("Assets/A.PSD")]
        [TestCase("Assets/A.psb")]
        public void Test_IsPsd_AndIsImage(string path)
        {
            Assert.IsTrue(GitPsd.IsPsd(path));
            Assert.IsTrue(GitImageDiff.IsImage(path));
            Assert.IsFalse(GitImageDiff.CanDecodeDirectly(path));
        }

        [TestCase("Assets/A.png")]
        [TestCase("Assets/A.psd.meta")]
        [TestCase(null)]
        public void Test_IsPsd_Others(string path)
        {
            Assert.IsFalse(GitPsd.IsPsd(path));
        }

        [Test]
        public void Test_Decode_RawComposite_FlipsToUnityOrder()
        {
            // 위 줄: 빨강·초록, 아래 줄: 파랑·흰색.
            byte[] psd = Build(2, 2, channels: 3, composite: w =>
            {
                w.U16(0);
                w.Bytes(255, 0, 0, 255);
                w.Bytes(0, 255, 0, 255);
                w.Bytes(0, 0, 255, 255);
            });

            GitPsdImage image = GitPsd.Decode(psd);

            Assert.AreEqual(2, image.Width);
            Assert.AreEqual(2, image.Height);
            Assert.AreEqual(0, image.LayerCount);
            // Unity 순서: 아래 줄이 먼저.
            Assert.AreEqual(new Color32(0, 0, 255, 255), image.Pixels[0]);
            Assert.AreEqual(new Color32(255, 255, 255, 255), image.Pixels[1]);
            Assert.AreEqual(new Color32(255, 0, 0, 255), image.Pixels[2]);
            Assert.AreEqual(new Color32(0, 255, 0, 255), image.Pixels[3]);
        }

        [Test]
        public void Test_Decode_RleComposite()
        {
            // 1×2, 채널마다 줄 하나를 "같은 값 2개" 로 압축(-1, 값).
            byte[] psd = Build(2, 1, channels: 3, composite: w =>
            {
                w.U16(1);
                w.U16(2);
                w.U16(2);
                w.U16(2);
                w.Bytes(0xFF, 10);
                w.Bytes(0xFF, 20);
                w.Bytes(0xFF, 30);
            });

            GitPsdImage image = GitPsd.Decode(psd);

            Assert.AreEqual(new Color32(10, 20, 30, 255), image.Pixels[0]);
            Assert.AreEqual(new Color32(10, 20, 30, 255), image.Pixels[1]);
        }

        [Test]
        public void Test_Decode_CompositeAlpha_RemovesWhiteMatte()
        {
            // 4번째 채널이 알파. 반투명 검정은 흰 바탕에 섞여 회색(128)으로 저장된다.
            byte[] psd = Build(2, 1, channels: 4, composite: w =>
            {
                w.U16(0);
                w.Bytes(128, 255);
                w.Bytes(128, 255);
                w.Bytes(128, 255);
                w.Bytes(127, 0);
            });

            GitPsdImage image = GitPsd.Decode(psd);

            Assert.AreEqual(new Color32(0, 0, 0, 127), image.Pixels[0]);
            Assert.AreEqual(new Color32(0, 0, 0, 0), image.Pixels[1]);
        }

        [Test]
        public void Test_Decode_NoComposite_FlattensVisibleLayers()
        {
            // 2×2 문서, 오른쪽 위 1×1 에만 빨간 레이어. 합쳐진 이미지는 없다.
            byte[] psd = Build(2, 2, channels: 3, layers: w =>
            {
                w.I16(1);
                w.I32(0);
                w.I32(1);
                w.I32(1);
                w.I32(2);
                w.U16(4);
                foreach (short id in new short[] { -1, 0, 1, 2 })
                {
                    w.I16(id);
                    w.U32(3); // 압축 2바이트 + 픽셀 1개
                }
                w.Ascii("8BIM");
                w.Ascii("norm");
                w.Bytes(255, 0, 0, 0); // 불투명도·클리핑·플래그(보임)·채움
                w.U32(12); // 추가 데이터: 마스크 0, 블렌딩 범위 0, 빈 이름(4바이트)
                w.U32(0);
                w.U32(0);
                w.Bytes(0, 0, 0, 0);
                foreach (byte value in new byte[] { 255, 255, 0, 0 })
                {
                    w.U16(0);
                    w.Bytes(value);
                }
            });

            GitPsdImage image = GitPsd.Decode(psd);

            Assert.AreEqual(1, image.LayerCount);
            // 위 줄(Unity 순서로 2·3번)의 오른쪽만 빨강.
            Assert.AreEqual(new Color32(255, 0, 0, 255), image.Pixels[3]);
            Assert.AreEqual(0, image.Pixels[2].a);
            Assert.AreEqual(0, image.Pixels[0].a);
            Assert.AreEqual(0, image.Pixels[1].a);
        }

        [Test]
        public void Test_Decode_NotPsd_Throws()
        {
            Assert.Throws<InvalidDataException>(() => GitPsd.Decode(Encoding.ASCII.GetBytes("\x89PNG not a psd")));
        }

        [Test]
        public void Test_Decode_Truncated_Throws()
        {
            byte[] psd = Build(2, 2, channels: 3, composite: w => w.U16(0));
            Assert.Throws<InvalidDataException>(() => GitPsd.Decode(psd));
        }

        [Test]
        public void Test_TryReadSize_ReadsHeaderOnly()
        {
            byte[] psd = Build(300, 200, channels: 3);
            Assert.IsTrue(GitPsd.TryReadSize(psd, out int width, out int height));
            Assert.AreEqual(300, width);
            Assert.AreEqual(200, height);
            Assert.IsFalse(GitPsd.TryReadSize(new byte[26], out _, out _));
        }

        [Test]
        public void Test_Downscale_KeepsAspect_AndIgnoresTransparentColor()
        {
            // 4×2: 왼쪽 2×2 는 빨강, 오른쪽 2×2 는 반은 투명(색은 초록이지만 무시돼야 한다)·반은 파랑.
            Color32 red = new(255, 0, 0, 255);
            Color32 clear = new(0, 255, 0, 0);
            Color32 blue = new(0, 0, 255, 255);
            Color32[] pixels = { red, red, clear, blue, red, red, blue, clear };

            (Color32[] small, int width, int height) = GitPsd.Downscale(pixels, 4, 2, 2);

            Assert.AreEqual(2, width);
            Assert.AreEqual(1, height);
            Assert.AreEqual(red, small[0]);
            Assert.AreEqual(new Color32(0, 0, 255, 127), small[1]);
        }

        [Test]
        public void Test_Downscale_SmallImage_Unchanged()
        {
            Color32[] pixels = new Color32[4];
            (Color32[] small, int width, int height) = GitPsd.Downscale(pixels, 2, 2, 64);
            Assert.AreSame(pixels, small);
            Assert.AreEqual(2, width);
            Assert.AreEqual(2, height);
        }

        private delegate void Section(Writer writer);

        /// <summary>
        /// 8비트 RGB PSD 를 만든다. 레이어 섹션·합쳐진 이미지는 넘긴 내용 그대로 쓴다(없으면 비운다).
        /// </summary>
        private static byte[] Build(int width, int height, int channels, Section composite = null, Section layers = null)
        {
            Writer w = new();
            w.Ascii("8BPS");
            w.U16(1);
            w.Bytes(0, 0, 0, 0, 0, 0);
            w.U16(channels);
            w.U32(height);
            w.U32(width);
            w.U16(8);
            w.U16(3);
            w.U32(0); // 색 모드 데이터
            w.U32(0); // 이미지 리소스
            if (layers == null)
            {
                w.U32(0);
            }
            else
            {
                Writer info = new();
                layers(info);
                byte[] infoBytes = info.ToArray();
                w.U32(4 + infoBytes.Length + 4);
                w.U32(infoBytes.Length);
                w.Bytes(infoBytes);
                w.U32(0); // 전역 레이어 마스크
            }
            composite?.Invoke(w);
            return w.ToArray();
        }

        private sealed class Writer
        {
            private readonly List<byte> _bytes = new();

            public void Bytes(params byte[] values)
            {
                _bytes.AddRange(values);
            }

            public void Ascii(string text)
            {
                _bytes.AddRange(Encoding.ASCII.GetBytes(text));
            }

            public void U16(int value)
            {
                _bytes.Add((byte)(value >> 8));
                _bytes.Add((byte)value);
            }

            public void I16(short value)
            {
                U16(value);
            }

            public void U32(int value)
            {
                _bytes.Add((byte)(value >> 24));
                _bytes.Add((byte)(value >> 16));
                _bytes.Add((byte)(value >> 8));
                _bytes.Add((byte)value);
            }

            public void I32(int value)
            {
                U32(value);
            }

            public byte[] ToArray()
            {
                return _bytes.ToArray();
            }
        }
    }
}
