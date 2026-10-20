using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GitVersionControl
{
    /// <summary>
    /// 이미지 diff 의 한쪽(이전 또는 이후). 텍스처는 이 객체가 가지며 <see cref="Dispose"/> 에서 지운다.
    /// </summary>
    public sealed class GitImageSide : IDisposable
    {
        /// <summary>
        /// 읽기 가능한 RGBA32 텍스처. 파일이 없는 쪽(추가·삭제)이면 null.
        /// </summary>
        public Texture2D Texture;

        public long ByteSize;

        /// <summary>
        /// 판 이름(커밋 앞 8자리·Staged·작업 사본).
        /// </summary>
        public string Label;

        /// <summary>
        /// 파일 확장자(".png"). 속성 보기(Show properties)에 쓴다.
        /// </summary>
        public string Format;

        /// <summary>
        /// PSD·PSB 의 레이어 수(그룹 포함). 다른 형식이면 null.
        /// </summary>
        public int? LayerCount;

        /// <summary>
        /// 이 판이 LFS 주소 파일뿐이면(원본을 받지 않았다) 원본 크기. 그러면 <see cref="Texture"/> 는 null 이다.
        /// </summary>
        public long? LfsPointerSize;

        public bool IsMissingLfs => LfsPointerSize != null;

        public bool Exists => Texture != null;
        public int Width => Texture != null ? Texture.width : 0;
        public int Height => Texture != null ? Texture.height : 0;

        public void Dispose()
        {
            if (Texture != null)
            {
                Object.DestroyImmediate(Texture);
                Texture = null;
            }
        }
    }

    public sealed class GitImagePair : IDisposable
    {
        public GitImageSide Left;
        public GitImageSide Right;

        /// <summary>
        /// 한쪽이라도 LFS 원본이 없어 비교할 수 없다.
        /// </summary>
        public bool IsMissingLfs => (Left?.IsMissingLfs ?? false) || (Right?.IsMissingLfs ?? false);

        /// <summary>
        /// 받아야 할 LFS 원본 크기의 합(바이트).
        /// </summary>
        public long MissingLfsBytes => (Left?.LfsPointerSize ?? 0) + (Right?.LfsPointerSize ?? 0);

        public void Dispose()
        {
            Left?.Dispose();
            Right?.Dispose();
        }
    }

    /// <summary>
    /// 이미지 파일을 메모리 텍스처로 읽는다. 메인 스레드에서만 부른다.
    /// PNG·JPG 는 <see cref="ImageConversion.LoadImage(Texture2D, byte[])"/> 로, PSD·PSB 는 <see cref="GitPsd"/> 로 바로 읽고, 그 외(TGA·EXR 등)는
    /// <see cref="TempFolder"/> 에 잠깐 복사해 Unity 텍스처 임포터로 읽은 뒤 바로 지운다. 프로젝트에 이미 임포트된 텍스처를 쓰지 않는 이유:
    /// 압축·최대 크기 설정이 원본 픽셀을 바꿔 공정한 비교가 안 된다.
    /// </summary>
    public static class GitImageLoader
    {
        public const string TempFolder = "Assets/GitVcsImageDiffTemp";

        /// <returns>파일이 없거나 비어 있으면 texture 도 error 도 null(그쪽은 "없음"). layerCount 는 PSD·PSB 일 때만 있다.</returns>
        public static (Texture2D texture, int? layerCount, string error) Load(string path)
        {
            if (File.Exists(path) == false || new FileInfo(path).Length == 0)
            {
                return (null, null, null);
            }
            try
            {
                if (GitPsd.IsPsd(path))
                {
                    return LoadPsd(path);
                }
                Texture2D texture = GitImageDiff.CanDecodeDirectly(path) ? LoadDirect(path) : LoadWithImporter(path);
                if (texture == null)
                {
                    return (null, null, GitText.Format("imageDiff.decodeFailed", Path.GetFileName(path)));
                }
                return (Prepare(texture), null, null);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
            {
                return (null, null, e.Message);
            }
        }

        private static Texture2D Prepare(Texture2D texture)
        {
            texture.hideFlags = HideFlags.HideAndDontSave;
            // 확대했을 때 픽셀 하나하나가 또렷하게 보이도록.
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            return texture;
        }

        /// <summary>
        /// PSD·PSB 는 임포터를 거치지 않고 직접 해석한다. 2D PSD Importer 가 깔린 프로젝트에서도 레이어를 펼친 원본 그대로 비교되고,
        /// 프로젝트에 임시 에셋을 만들지 않으며 PSB 도 읽는다.
        /// </summary>
        private static (Texture2D texture, int? layerCount, string error) LoadPsd(string path)
        {
            GitPsdImage image;
            try
            {
                image = GitPsd.Decode(File.ReadAllBytes(path));
            }
            catch (Exception e) when (e is InvalidDataException || e is NotSupportedException || e is IndexOutOfRangeException || e is OverflowException)
            {
                return (null, null, GitText.Format("imageDiff.decodeFailed", Path.GetFileName(path)) + " " + e.Message);
            }
            if (image.Width <= 0 || image.Height <= 0)
            {
                return (null, null, GitText.Format("imageDiff.decodeFailed", Path.GetFileName(path)));
            }
            if (image.Width > SystemInfo.maxTextureSize || image.Height > SystemInfo.maxTextureSize)
            {
                return (null, null, GitText.Format("imageDiff.tooLarge", Path.GetFileName(path), image.Width, image.Height, SystemInfo.maxTextureSize));
            }

            Texture2D texture = new(image.Width, image.Height, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            texture.SetPixels32(image.Pixels);
            texture.Apply(false, false);
            return (Prepare(texture), image.LayerCount, null);
        }

        private static Texture2D LoadDirect(string path)
        {
            Texture2D texture = new(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            if (texture.LoadImage(File.ReadAllBytes(path), markNonReadable: false))
            {
                return texture;
            }
            Object.DestroyImmediate(texture);
            return null;
        }

        private static Texture2D LoadWithImporter(string path)
        {
            try
            {
                if (AssetDatabase.IsValidFolder(TempFolder) == false)
                {
                    AssetDatabase.CreateFolder("Assets", Path.GetFileName(TempFolder));
                }
                string assetPath = $"{TempFolder}/{Guid.NewGuid():N}{Path.GetExtension(path).ToLowerInvariant()}";
                File.Copy(path, Path.Combine(GitRepository.ProjectRoot, assetPath));
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

                Texture2D imported = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                if (imported == null || imported.isReadable == false)
                {
                    return null;
                }
                // 임포트된 에셋은 곧 지우므로 픽셀을 메모리 텍스처로 옮긴다.
                Texture2D copy = new(imported.width, imported.height, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                copy.SetPixels32(imported.GetPixels32());
                copy.Apply(false, false);
                return copy;
            }
            finally
            {
                // 이 기능만 쓰는 폴더라 통째로 지운다(지난번에 지우다 실패해 남은 파일까지 함께 정리된다).
                AssetDatabase.DeleteAsset(TempFolder);
            }
        }

        /// <summary>
        /// 임시 폴더로 들어온 텍스처만 원본 픽셀 그대로(압축·밉맵·크기 조정 없이) 읽기 가능하게 임포트한다.
        /// </summary>
        internal sealed class TempImportProcessor : AssetPostprocessor
        {
            private void OnPreprocessTexture()
            {
                if (assetPath.StartsWith(TempFolder + "/", StringComparison.Ordinal) == false)
                {
                    return;
                }
                TextureImporter importer = (TextureImporter)assetImporter;
                importer.textureType = TextureImporterType.Default;
                importer.textureShape = TextureImporterShape.Texture2D;
                importer.isReadable = true;
                importer.mipmapEnabled = false;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 16384;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = false;
                importer.sRGBTexture = true;
            }
        }
    }
}
