using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

namespace GitVersionControl
{
    /// <summary>
    /// PSD/PSB 를 읽은 결과 이미지. 픽셀은 Unity 순서(아래 줄부터)라 <c>Texture2D.SetPixels32</c> 에 바로 넣는다.
    /// </summary>
    public sealed class GitPsdImage
    {
        public int Width;
        public int Height;
        public Color32[] Pixels;

        /// <summary>
        /// 레이어 수(그룹 포함). 합쳐진 이미지만 읽었으면 0.
        /// </summary>
        public int LayerCount;
    }

    /// <summary>
    /// 에셋 임포트 없이 PSD/PSB 파일을 바로 읽는다(com.unity.2d.psdimporter 에 의존하지 않고, AssetDatabase 도 건드리지 않는다).
    /// 파일 끝의 합쳐진 이미지(Maximize compatibility)를 쓰고, 없으면 보이는 레이어를 Normal 블렌딩으로 합친다.
    /// 지원: PSD·PSB, 1/8/16/32 비트, Grayscale·Indexed·RGB·CMYK·Duotone, Raw·RLE·ZIP·ZIP+예측.
    /// 레이어로 합칠 때는 마스크·클리핑·조정 레이어·효과·Normal 외 블렌드 모드를 무시한다.
    /// 메인 스레드가 아니어도 부를 수 있다.
    /// </summary>
    public static class GitPsd
    {
        private static readonly string[] Extensions = { ".psd", ".psb" };

        public static bool IsPsd(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }
            string extension = Path.GetExtension(path);
            foreach (string candidate in Extensions)
            {
                if (string.Equals(extension, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        /// <exception cref="InvalidDataException">PSD 가 아니거나 읽을 수 없는 형식.</exception>
        /// <exception cref="NotSupportedException">지원하지 않는 압축·비트 깊이.</exception>
        public static GitPsdImage Decode(byte[] data)
        {
            Document document = Read(data, readLayers: false);
            Color32[] topDown = document.Composite;
            if (topDown == null)
            {
                // 합쳐진 이미지가 없다(Maximize compatibility 끔). 레이어를 읽어 직접 합친다.
                document = Read(data, readLayers: true);
                if (document.Layers.Count == 0)
                {
                    throw new InvalidDataException(document.Warnings.Count > 0 ? document.Warnings[0] : "PSD has neither a merged image nor layers.");
                }
                topDown = Flatten(document);
            }

            int width = document.Width;
            int height = document.Height;
            Color32[] pixels = new Color32[topDown.Length];
            for (int y = 0; y < height; y++)
            {
                Array.Copy(topDown, y * width, pixels, (height - 1 - y) * width, width);
            }
            return new GitPsdImage { Width = width, Height = height, Pixels = pixels, LayerCount = document.Layers.Count };
        }

        // ── 문서 구조 ──────────────────────────────────────────

        private enum ColorMode
        {
            Bitmap = 0,
            Grayscale = 1,
            Indexed = 2,
            RGB = 3,
            CMYK = 4,
            Multichannel = 7,
            Duotone = 8,
            Lab = 9,
        }

        private sealed class Layer
        {
            public string Name;

            /// <summary>
            /// 문서 좌표의 범위(왼쪽 위 기준, PSD 방식).
            /// </summary>
            public RectInt Rect;

            public byte Opacity;
            public bool Visible;
            public bool IsGroup;
            public Layer Parent;

            /// <summary>
            /// Rect 크기의 RGBA 픽셀(위 줄부터). 그룹·빈 레이어는 null.
            /// </summary>
            public Color32[] Pixels;

            public bool IsEffectivelyVisible
            {
                get
                {
                    for (Layer layer = this; layer != null; layer = layer.Parent)
                    {
                        if (layer.Visible == false)
                        {
                            return false;
                        }
                    }
                    return true;
                }
            }

            public float EffectiveOpacity
            {
                get
                {
                    float opacity = 1f;
                    for (Layer layer = this; layer != null; layer = layer.Parent)
                    {
                        opacity *= layer.Opacity / 255f;
                    }
                    return opacity;
                }
            }
        }

        private sealed class Document
        {
            public bool IsPsb;
            public int Width;
            public int Height;
            public int BitDepth;
            public int ChannelCount;
            public ColorMode ColorMode;

            /// <summary>
            /// 파일 끝의 합쳐진 이미지(위 줄부터). 없으면 null.
            /// </summary>
            public Color32[] Composite;

            /// <summary>
            /// Photoshop 레이어 패널 순서(위가 먼저). 그룹은 넣고 그룹 끝 표시는 뺀다.
            /// </summary>
            public readonly List<Layer> Layers = new();

            public readonly List<string> Warnings = new();
        }

        // ── 파일 읽기 ──────────────────────────────────────────

        private static Document Read(byte[] data, bool readLayers)
        {
            BigEndianReader reader = new(data);
            Document document = new();

            // 파일 헤더
            if (reader.Ascii(4) != "8BPS")
            {
                throw new InvalidDataException("Not a PSD file (missing 8BPS signature).");
            }
            ushort version = reader.U16();
            if (version != 1 && version != 2)
            {
                throw new InvalidDataException($"Unsupported PSD version {version}.");
            }
            document.IsPsb = version == 2;
            reader.Skip(6);
            document.ChannelCount = reader.U16();
            document.Height = (int)reader.U32();
            document.Width = (int)reader.U32();
            document.BitDepth = reader.U16();
            document.ColorMode = (ColorMode)reader.U16();
            if (document.BitDepth != 1 && document.BitDepth != 8 && document.BitDepth != 16 && document.BitDepth != 32)
            {
                throw new NotSupportedException($"Unsupported bit depth {document.BitDepth}.");
            }

            // 색 모드 데이터(Indexed 의 팔레트)
            long colorModeLength = reader.U32();
            long colorModeStart = reader.Position;
            byte[] palette = null;
            if (document.ColorMode == ColorMode.Indexed && colorModeLength >= 768)
            {
                palette = reader.Bytes(768);
            }
            reader.Position = colorModeStart + colorModeLength;

            // 이미지 리소스(쓰지 않는다)
            reader.Skip(reader.U32());

            // 레이어·마스크 정보
            long layerMaskLength = reader.Length(document.IsPsb);
            long layerMaskEnd = reader.Position + layerMaskLength;
            bool mergedHasAlpha = false;
            if (layerMaskLength > 0)
            {
                try
                {
                    ReadLayerAndMaskSection(reader, document, palette, layerMaskEnd, readLayers, ref mergedHasAlpha);
                }
                catch (Exception e) when (e is InvalidDataException || e is NotSupportedException || e is EndOfStreamException || e is IOException)
                {
                    document.Warnings.Add("Failed to read layers: " + e.Message);
                    document.Layers.Clear();
                }
            }
            reader.Position = layerMaskEnd;

            // 합쳐진 이미지
            try
            {
                if (document.Layers.Count == 0 && document.ChannelCount > ColorChannelCount(document.ColorMode))
                {
                    mergedHasAlpha = true;
                }
                document.Composite = ReadCompositeImage(reader, document, palette, mergedHasAlpha);
            }
            catch (Exception e) when (e is InvalidDataException || e is NotSupportedException || e is EndOfStreamException || e is IOException)
            {
                document.Warnings.Add("Failed to read composite image: " + e.Message);
            }
            return document;
        }

        private static int ColorChannelCount(ColorMode mode)
        {
            switch (mode)
            {
                case ColorMode.RGB:
                case ColorMode.Lab:
                    return 3;
                case ColorMode.CMYK:
                    return 4;
                default:
                    return 1;
            }
        }

        // ── 레이어 ─────────────────────────────────────────────

        private struct ChannelInfo
        {
            public short Id;
            public long Length;
        }

        private sealed class LayerRecord
        {
            public Layer Layer;
            public ChannelInfo[] Channels;

            /// <summary>
            /// 0 = 일반, 1·2 = 그룹(열림·닫힘), 3 = 그룹 끝 표시.
            /// </summary>
            public int SectionType;
        }

        /// <summary>
        /// PSB 에서 길이가 8바이트인 추가 레이어 정보 키.
        /// </summary>
        private static readonly HashSet<string> LongLengthKeys = new()
        {
            "LMsk", "Lr16", "Lr32", "Layr", "Mt16", "Mt32", "Mtrn", "Alph", "FMsk", "lnk2", "FEid", "FXid", "PxSD",
        };

        private static void ReadLayerAndMaskSection(BigEndianReader reader, Document document, byte[] palette, long sectionEnd, bool readLayers, ref bool mergedHasAlpha)
        {
            long layerInfoLength = reader.Length(document.IsPsb);
            long layerInfoEnd = reader.Position + layerInfoLength;
            if (layerInfoLength > 0)
            {
                ReadLayerInfo(reader, document, palette, layerInfoEnd, readLayers, ref mergedHasAlpha);
            }
            reader.Position = layerInfoEnd;

            if (reader.Position + 4 > sectionEnd)
            {
                return;
            }
            reader.Skip(reader.U32()); // 전역 레이어 마스크

            // 16·32 비트 문서는 레이어를 전역 "Lr16"·"Lr32" 블록에 둔다.
            while (reader.Position + 12 <= sectionEnd)
            {
                string signature = reader.Ascii(4);
                if (signature != "8BIM" && signature != "8B64")
                {
                    break;
                }
                string key = reader.Ascii(4);
                long length = document.IsPsb && LongLengthKeys.Contains(key) ? (long)reader.U64() : reader.U32();
                long start = reader.Position;
                if ((key == "Lr16" || key == "Lr32" || key == "Layr") && document.Layers.Count == 0 && length > 0)
                {
                    ReadLayerInfo(reader, document, palette, start + length, readLayers, ref mergedHasAlpha);
                }
                reader.Position = start + length;
                // 전역 추가 레이어 정보는 실제로 4바이트 단위로 채워져 있다.
                reader.Position += (4 - (reader.Position - start) % 4) % 4;
            }
        }

        private static void ReadLayerInfo(BigEndianReader reader, Document document, byte[] palette, long end, bool readLayers, ref bool mergedHasAlpha)
        {
            short layerCount = reader.I16();
            if (layerCount < 0)
            {
                // 음수면 합쳐진 이미지의 첫 알파 채널이 투명도다.
                mergedHasAlpha = true;
                layerCount = (short)-layerCount;
            }

            // 레코드는 아래 레이어부터 저장된다.
            List<LayerRecord> records = new(layerCount);
            for (int i = 0; i < layerCount; i++)
            {
                records.Add(ReadLayerRecord(reader, document.IsPsb));
            }

            // 채널 픽셀은 모든 레코드 뒤에 같은 순서로 이어진다.
            foreach (LayerRecord record in records)
            {
                RectInt rect = record.Layer.Rect;
                Dictionary<short, byte[]> planes = new();
                foreach (ChannelInfo channel in record.Channels)
                {
                    long channelStart = reader.Position;
                    // 마스크(-2, -3)는 범위가 따로라 읽지 않는다.
                    if (readLayers && channel.Id >= -1 && rect.width > 0 && rect.height > 0 && channel.Length >= 2)
                    {
                        try
                        {
                            ushort compression = reader.U16();
                            planes[channel.Id] = ReadPlane(reader, compression, rect.width, rect.height, document.BitDepth, document.IsPsb, channelStart + channel.Length);
                        }
                        catch (Exception e) when (e is InvalidDataException || e is NotSupportedException || e is EndOfStreamException || e is IOException)
                        {
                            document.Warnings.Add($"Layer '{record.Layer.Name}' channel {channel.Id}: {e.Message}");
                        }
                    }
                    reader.Position = channelStart + channel.Length;
                }

                if (readLayers && record.SectionType == 0 && planes.Count > 0)
                {
                    planes.TryGetValue(-1, out byte[] alpha);
                    record.Layer.Pixels = PlanesToColors(document.ColorMode, rect.width * rect.height, id => planes.TryGetValue((short)id, out byte[] plane) ? plane : null, alpha, palette);
                }
            }
            reader.Position = end;

            // 위 레이어부터(Photoshop 패널 순서) 걸으며 그룹 계층을 만든다.
            Stack<Layer> parents = new();
            for (int i = records.Count - 1; i >= 0; i--)
            {
                LayerRecord record = records[i];
                if (record.SectionType == 3)
                {
                    if (parents.Count > 0)
                    {
                        parents.Pop();
                    }
                    continue;
                }
                Layer layer = record.Layer;
                layer.Parent = parents.Count > 0 ? parents.Peek() : null;
                document.Layers.Add(layer);
                if (record.SectionType == 1 || record.SectionType == 2)
                {
                    layer.IsGroup = true;
                    parents.Push(layer);
                }
            }
        }

        private static LayerRecord ReadLayerRecord(BigEndianReader reader, bool isPsb)
        {
            int top = reader.I32();
            int left = reader.I32();
            int bottom = reader.I32();
            int right = reader.I32();
            Layer layer = new() { Rect = new RectInt(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top)) };

            int channelCount = reader.U16();
            ChannelInfo[] channels = new ChannelInfo[channelCount];
            for (int c = 0; c < channelCount; c++)
            {
                channels[c] = new ChannelInfo { Id = reader.I16(), Length = reader.Length(isPsb) };
            }

            reader.Ascii(4); // "8BIM"
            reader.Ascii(4); // 블렌드 모드
            layer.Opacity = reader.U8();
            reader.U8(); // 클리핑
            byte flags = reader.U8();
            layer.Visible = (flags & 0x02) == 0;
            reader.U8(); // 채움

            long extraLength = reader.U32();
            long extraEnd = reader.Position + extraLength;
            reader.Skip(reader.U32()); // 레이어 마스크
            reader.Skip(reader.U32()); // 블렌딩 범위

            // 파스칼 문자열. 길이 바이트를 포함해 4바이트 단위로 채워져 있다.
            int nameLength = reader.U8();
            layer.Name = Latin1(reader.Bytes(nameLength));
            reader.Skip((4 - (nameLength + 1) % 4) % 4);

            int sectionType = 0;
            while (reader.Position + 12 <= extraEnd)
            {
                string signature = reader.Ascii(4);
                if (signature != "8BIM" && signature != "8B64")
                {
                    break;
                }
                string key = reader.Ascii(4);
                long length = isPsb && LongLengthKeys.Contains(key) ? (long)reader.U64() : reader.U32();
                long start = reader.Position;
                switch (key)
                {
                    case "luni":
                        int charCount = (int)reader.U32();
                        layer.Name = Encoding.BigEndianUnicode.GetString(reader.Bytes(charCount * 2L)).TrimEnd('\0');
                        break;
                    case "lsct":
                    case "lsdk":
                        sectionType = (int)reader.U32();
                        break;
                }
                reader.Position = start + length;
            }
            reader.Position = extraEnd;

            return new LayerRecord { Layer = layer, Channels = channels, SectionType = sectionType };
        }

        // ── 합쳐진 이미지 ──────────────────────────────────────

        private static Color32[] ReadCompositeImage(BigEndianReader reader, Document document, byte[] palette, bool hasAlpha)
        {
            if (reader.Position + 2 > reader.Size)
            {
                return null;
            }

            int width = document.Width;
            int height = document.Height;
            int colorChannels = Math.Min(ColorChannelCount(document.ColorMode), document.ChannelCount);
            int needed = Math.Min(document.ChannelCount, colorChannels + (hasAlpha ? 1 : 0));
            ushort compression = reader.U16();

            byte[][] planes = new byte[needed][];
            if (compression == 1)
            {
                // RLE: 모든 채널의 줄마다 바이트 수가 먼저 온다.
                int totalRows = document.ChannelCount * height;
                int[] rowLengths = new int[totalRows];
                for (int i = 0; i < totalRows; i++)
                {
                    rowLengths[i] = document.IsPsb ? (int)reader.U32() : reader.U16();
                }
                for (int c = 0; c < needed; c++)
                {
                    planes[c] = DecodeRle(reader, rowLengths, c * height, width, height, document.BitDepth);
                }
            }
            else if (compression == 0)
            {
                for (int c = 0; c < needed; c++)
                {
                    planes[c] = To8Bit(reader.Bytes(RowBytes(width, document.BitDepth) * (long)height), width, height, document.BitDepth);
                }
            }
            else
            {
                throw new NotSupportedException($"Composite compression {compression} is not supported.");
            }

            byte[] alpha = hasAlpha && needed > colorChannels ? planes[colorChannels] : null;
            Color32[] colors = PlanesToColors(document.ColorMode, width * height, i => i < colorChannels ? planes[i] : null, alpha, palette);
            if (alpha != null)
            {
                RemoveWhiteMatte(colors);
            }
            return colors;
        }

        /// <summary>
        /// Photoshop 은 합쳐진 이미지의 색을 흰 바탕에 미리 섞어 둔다(저장값 = c × a + 255 × (1 − a)).
        /// 되돌리지 않으면 안티앨리어싱 가장자리에 흰 테두리가 남는다.
        /// </summary>
        private static void RemoveWhiteMatte(Color32[] colors)
        {
            for (int i = 0; i < colors.Length; i++)
            {
                Color32 color = colors[i];
                if (color.a == 255)
                {
                    continue;
                }
                if (color.a == 0)
                {
                    colors[i] = default;
                    continue;
                }
                int matte = 255 - color.a;
                colors[i] = new Color32(Unmatte(color.r, matte, color.a), Unmatte(color.g, matte, color.a), Unmatte(color.b, matte, color.a), color.a);
            }
        }

        private static byte Unmatte(byte stored, int matte, byte alpha)
        {
            int value = ((stored - matte) * 255 + alpha / 2) / alpha;
            return (byte)Math.Clamp(value, 0, 255);
        }

        // ── 레이어 합치기 ──────────────────────────────────────

        /// <summary>
        /// 보이는 레이어를 아래부터 Normal 블렌딩·레이어(그룹) 불투명도로 합친다. 결과는 위 줄부터.
        /// </summary>
        private static Color32[] Flatten(Document document)
        {
            int width = document.Width;
            int height = document.Height;
            Color[] destination = new Color[width * height];
            for (int i = document.Layers.Count - 1; i >= 0; i--)
            {
                Layer layer = document.Layers[i];
                if (layer.IsGroup || layer.Pixels == null || layer.IsEffectivelyVisible == false)
                {
                    continue;
                }
                BlendLayer(destination, width, height, layer, layer.EffectiveOpacity);
            }

            Color32[] result = new Color32[destination.Length];
            for (int i = 0; i < destination.Length; i++)
            {
                result[i] = destination[i];
            }
            return result;
        }

        private static void BlendLayer(Color[] destination, int width, int height, Layer layer, float opacity)
        {
            RectInt rect = layer.Rect;
            Color32[] source = layer.Pixels;
            int x0 = Math.Max(0, rect.x);
            int x1 = Math.Min(width, rect.xMax);
            int y0 = Math.Max(0, rect.y);
            int y1 = Math.Min(height, rect.yMax);
            for (int y = y0; y < y1; y++)
            {
                int sourceRow = (y - rect.y) * rect.width - rect.x;
                int destinationRow = y * width;
                for (int x = x0; x < x1; x++)
                {
                    Color s = source[sourceRow + x];
                    float sourceAlpha = s.a * opacity;
                    if (sourceAlpha <= 0f)
                    {
                        continue;
                    }
                    Color d = destination[destinationRow + x];
                    float outAlpha = sourceAlpha + d.a * (1f - sourceAlpha);
                    float inverse = 1f / outAlpha;
                    destination[destinationRow + x] = new Color(
                        (s.r * sourceAlpha + d.r * d.a * (1f - sourceAlpha)) * inverse,
                        (s.g * sourceAlpha + d.g * d.a * (1f - sourceAlpha)) * inverse,
                        (s.b * sourceAlpha + d.b * d.a * (1f - sourceAlpha)) * inverse,
                        outAlpha);
                }
            }
        }

        // ── 채널 디코딩 ────────────────────────────────────────

        private static int RowBytes(int width, int depth)
        {
            return (width * depth + 7) / 8;
        }

        private static byte[] ReadPlane(BigEndianReader reader, ushort compression, int width, int height, int depth, bool isPsb, long end)
        {
            switch (compression)
            {
                case 0:
                    return To8Bit(reader.Bytes(RowBytes(width, depth) * (long)height), width, height, depth);
                case 1:
                {
                    int[] rowLengths = new int[height];
                    for (int y = 0; y < height; y++)
                    {
                        rowLengths[y] = isPsb ? (int)reader.U32() : reader.U16();
                    }
                    return DecodeRle(reader, rowLengths, 0, width, height, depth);
                }
                case 2:
                case 3:
                {
                    byte[] raw = Inflate(reader.Bytes(end - reader.Position), RowBytes(width, depth) * height);
                    if (compression == 3)
                    {
                        Unpredict(raw, width, height, depth);
                    }
                    return To8Bit(raw, width, height, depth);
                }
                default:
                    throw new NotSupportedException($"Compression {compression} is not supported.");
            }
        }

        private static byte[] DecodeRle(BigEndianReader reader, int[] rowLengths, int rowOffset, int width, int height, int depth)
        {
            int rowBytes = RowBytes(width, depth);
            byte[] raw = new byte[rowBytes * height];
            for (int y = 0; y < height; y++)
            {
                int length = rowLengths[rowOffset + y];
                reader.Ensure(length);
                UnpackBits(reader.Buffer, (int)reader.Position, length, raw, y * rowBytes, rowBytes);
                reader.Position += length;
            }
            return To8Bit(raw, width, height, depth);
        }

        private static void UnpackBits(byte[] source, int sourcePosition, int sourceLength, byte[] destination, int destinationPosition, int destinationLength)
        {
            int sourceEnd = sourcePosition + sourceLength;
            int destinationEnd = destinationPosition + destinationLength;
            while (sourcePosition < sourceEnd && destinationPosition < destinationEnd)
            {
                int n = (sbyte)source[sourcePosition++];
                if (n >= 0)
                {
                    int count = Math.Min(n + 1, Math.Min(destinationEnd - destinationPosition, sourceEnd - sourcePosition));
                    Buffer.BlockCopy(source, sourcePosition, destination, destinationPosition, count);
                    sourcePosition += n + 1;
                    destinationPosition += count;
                }
                else if (n != -128 && sourcePosition < sourceEnd)
                {
                    int count = Math.Min(1 - n, destinationEnd - destinationPosition);
                    byte value = source[sourcePosition++];
                    for (int i = 0; i < count; i++)
                    {
                        destination[destinationPosition++] = value;
                    }
                }
            }
        }

        private static byte[] Inflate(byte[] zlib, int expectedLength)
        {
            if (zlib.Length < 2)
            {
                throw new InvalidDataException("ZIP channel data is too short.");
            }
            // 앞 2바이트 zlib 헤더를 건너뛴다. DeflateStream 은 순수 deflate 데이터를 받는다.
            using MemoryStream input = new(zlib, 2, zlib.Length - 2);
            using DeflateStream deflate = new(input, CompressionMode.Decompress);
            byte[] output = new byte[expectedLength];
            int read = 0;
            while (read < expectedLength)
            {
                int n = deflate.Read(output, read, expectedLength - read);
                if (n <= 0)
                {
                    break;
                }
                read += n;
            }
            return output;
        }

        private static void Unpredict(byte[] data, int width, int height, int depth)
        {
            int rowBytes = RowBytes(width, depth);
            if (depth == 8)
            {
                for (int y = 0; y < height; y++)
                {
                    int row = y * rowBytes;
                    for (int x = 1; x < width; x++)
                    {
                        data[row + x] += data[row + x - 1];
                    }
                }
            }
            else if (depth == 16)
            {
                for (int y = 0; y < height; y++)
                {
                    int row = y * rowBytes;
                    for (int x = 1; x < width; x++)
                    {
                        int i = row + x * 2;
                        int value = ((data[i] << 8) | data[i + 1]) + ((data[i - 2] << 8) | data[i - 1]);
                        data[i] = (byte)(value >> 8);
                        data[i + 1] = (byte)value;
                    }
                }
            }
            else if (depth == 32)
            {
                // 줄 전체를 바이트 단위 차분으로 되돌린 뒤, 바이트별로 갈라 둔 것(모든 MSB, 그다음 바이트…)을 다시 엮는다.
                byte[] temp = new byte[rowBytes];
                for (int y = 0; y < height; y++)
                {
                    int row = y * rowBytes;
                    for (int i = 1; i < rowBytes; i++)
                    {
                        data[row + i] += data[row + i - 1];
                    }
                    for (int x = 0; x < width; x++)
                    {
                        for (int b = 0; b < 4; b++)
                        {
                            temp[x * 4 + b] = data[row + b * width + x];
                        }
                    }
                    Buffer.BlockCopy(temp, 0, data, row, rowBytes);
                }
            }
        }

        private static byte[] To8Bit(byte[] raw, int width, int height, int depth)
        {
            switch (depth)
            {
                case 8:
                    return raw;
                case 16:
                {
                    byte[] output = new byte[width * height];
                    for (int i = 0; i < output.Length; i++)
                    {
                        output[i] = raw[i * 2]; // 빅 엔디언 ushort 의 상위 바이트
                    }
                    return output;
                }
                case 32:
                {
                    byte[] output = new byte[width * height];
                    byte[] bytes = new byte[4];
                    for (int i = 0; i < output.Length; i++)
                    {
                        bytes[0] = raw[i * 4 + 3];
                        bytes[1] = raw[i * 4 + 2];
                        bytes[2] = raw[i * 4 + 1];
                        bytes[3] = raw[i * 4];
                        float linear = BitConverter.ToSingle(bytes, 0);
                        // 선형 float → 8비트(감마 2.2 근사).
                        output[i] = (byte)Math.Round(Math.Pow(Math.Clamp(linear, 0f, 1f), 1.0 / 2.2) * 255.0);
                    }
                    return output;
                }
                case 1:
                {
                    int rowBytes = RowBytes(width, 1);
                    byte[] output = new byte[width * height];
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            // 비트맵은 1 이 검정이다.
                            output[y * width + x] = (raw[y * rowBytes + (x >> 3)] & (0x80 >> (x & 7))) != 0 ? (byte)0 : (byte)255;
                        }
                    }
                    return output;
                }
                default:
                    throw new NotSupportedException($"Unsupported bit depth {depth}.");
            }
        }

        private static Color32[] PlanesToColors(ColorMode mode, int pixelCount, Func<int, byte[]> plane, byte[] alpha, byte[] palette)
        {
            Color32[] colors = new Color32[pixelCount];
            byte[] p0 = plane(0);
            byte[] p1 = plane(1);
            byte[] p2 = plane(2);
            byte[] p3 = plane(3);
            for (int i = 0; i < pixelCount; i++)
            {
                byte a = alpha != null ? Get(alpha, i, 255) : (byte)255;
                switch (mode)
                {
                    case ColorMode.RGB:
                        colors[i] = new Color32(Get(p0, i), Get(p1, i), Get(p2, i), a);
                        break;
                    case ColorMode.CMYK:
                    {
                        // 뒤집혀 저장된다: 255 = 잉크 없음.
                        int k = Get(p3, i, 255);
                        colors[i] = new Color32((byte)(Get(p0, i) * k / 255), (byte)(Get(p1, i) * k / 255), (byte)(Get(p2, i) * k / 255), a);
                        break;
                    }
                    case ColorMode.Indexed when palette != null:
                    {
                        int index = Get(p0, i);
                        colors[i] = new Color32(palette[index], palette[256 + index], palette[512 + index], a);
                        break;
                    }
                    case ColorMode.Lab:
                        // 밝기(L)만 보인다.
                        colors[i] = new Color32(Get(p0, i), Get(p0, i), Get(p0, i), a);
                        break;
                    default: // Grayscale·Bitmap·Duotone·Multichannel
                    {
                        byte value = Get(p0, i);
                        colors[i] = new Color32(value, value, value, a);
                        break;
                    }
                }
            }
            return colors;
        }

        private static string Latin1(byte[] bytes)
        {
            char[] chars = new char[bytes.Length];
            for (int i = 0; i < bytes.Length; i++)
            {
                chars[i] = (char)bytes[i];
            }
            return new string(chars);
        }

        private static byte Get(byte[] plane, int index, byte fallback = 0)
        {
            return plane != null && index < plane.Length ? plane[index] : fallback;
        }

        private sealed class BigEndianReader
        {
            public readonly byte[] Buffer;
            public long Position;

            public BigEndianReader(byte[] buffer)
            {
                Buffer = buffer;
            }

            public long Size => Buffer.LongLength;

            public void Ensure(long count)
            {
                if (count < 0 || Position < 0 || Position + count > Buffer.LongLength)
                {
                    throw new EndOfStreamException($"Unexpected end of file at {Position} (+{count}).");
                }
            }

            public byte U8()
            {
                Ensure(1);
                return Buffer[Position++];
            }

            public ushort U16()
            {
                Ensure(2);
                ushort value = (ushort)((Buffer[Position] << 8) | Buffer[Position + 1]);
                Position += 2;
                return value;
            }

            public short I16()
            {
                return (short)U16();
            }

            public uint U32()
            {
                Ensure(4);
                uint value = ((uint)Buffer[Position] << 24) | ((uint)Buffer[Position + 1] << 16) | ((uint)Buffer[Position + 2] << 8) | Buffer[Position + 3];
                Position += 4;
                return value;
            }

            public int I32()
            {
                return (int)U32();
            }

            public ulong U64()
            {
                ulong high = U32();
                return (high << 32) | U32();
            }

            public long Length(bool isPsb)
            {
                return isPsb ? (long)U64() : U32();
            }

            public void Skip(long count)
            {
                Ensure(count);
                Position += count;
            }

            public string Ascii(int count)
            {
                Ensure(count);
                string value = Encoding.ASCII.GetString(Buffer, (int)Position, count);
                Position += count;
                return value;
            }

            public byte[] Bytes(long count)
            {
                Ensure(count);
                byte[] bytes = new byte[count];
                Array.Copy(Buffer, Position, bytes, 0, count);
                Position += count;
                return bytes;
            }
        }
    }
}
