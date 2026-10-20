using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>파일 목록 줄 앞에 붙일 파일 종류 아이콘(스크립트·프리팹·폴더 등).</summary>
    public interface IFileIconProvider
    {
        Texture GetIcon(string projectPath, string repoPath);

        /// <summary>
        /// <paramref name="image"/> 에 아이콘을 넣는다. 썸네일을 만들 수 있는 파일(프로젝트 밖 PSD 등)은 우선 종류 아이콘을 넣고,
        /// 썸네일이 준비되면 그 줄이 아직 같은 파일을 보이고 있을 때만 바꿔 끼운다(목록 줄은 스크롤하면 다른 파일에 다시 쓰인다).
        /// </summary>
        void BindIcon(Image image, string projectPath, string repoPath);
    }

    /// <summary>
    /// Project 창과 같은 아이콘을 쓴다. 프로젝트 안 에셋은 AssetDatabase 의 아이콘, 폴더(.meta 만 바뀐 새 폴더 포함)는 폴더 아이콘,
    /// 삭제됐거나 프로젝트 밖 파일(.gitignore 등)은 확장자로 정한다. 같은 경로는 다시 찾지 않도록 기억한다.
    /// Unity 가 임포트하지 않은 PSD·PSB(Assets 밖 아트 원본)는 합쳐진 이미지를 직접 읽어 작은 썸네일을 만든다(<see cref="BindIcon"/>).
    /// </summary>
    public sealed class EditorFileIconProvider : IFileIconProvider
    {
        /// <summary>
        /// 썸네일 긴 변(픽셀). 줄 아이콘은 16pt 라 Retina 에서도 넉넉하다.
        /// </summary>
        private const int ThumbnailSize = 64;

        /// <summary>
        /// 이보다 큰 파일은 썸네일을 만들지 않는다. 파일을 통째로 읽어야 해서 메모리를 그만큼 쓴다.
        /// </summary>
        private const long MaxThumbnailFileBytes = 512L * 1024 * 1024;

        /// <summary>
        /// 이보다 픽셀이 많으면(4096×4096 초과) 만들지 않는다. 해석하는 동안 픽셀 수 × 약 12바이트를 쓴다.
        /// </summary>
        private const long MaxThumbnailPixels = 4096L * 4096;

        /// <summary>
        /// 썸네일은 한 번에 하나씩 만든다. 큰 PSD 여러 개를 한꺼번에 풀면 메모리가 크게 튄다.
        /// </summary>
        private static readonly SemaphoreSlim ThumbnailGate = new(1, 1);

        private readonly Dictionary<string, Texture> _cache = new();
        private Texture _folderIcon;

        /// <summary>
        /// 경로·크기·수정 시각으로 기억한 썸네일. 만들 수 없었던 파일은 null 로 기억해 다시 시도하지 않는다.
        /// </summary>
        private readonly Dictionary<string, Texture2D> _thumbnails = new();

        /// <summary>
        /// 만드는 중인 썸네일을 기다리는 줄 아이콘.
        /// </summary>
        private readonly Dictionary<string, List<Image>> _pending = new();

        public void BindIcon(Image image, string projectPath, string repoPath)
        {
            Texture icon = GetIcon(projectPath, repoPath);
            image.image = icon;
            string fullPath = ThumbnailSource(projectPath, repoPath);
            string key = fullPath != null ? ThumbnailKey(fullPath) : null;
            image.userData = key;
            if (key == null)
            {
                return;
            }
            if (_thumbnails.TryGetValue(key, out Texture2D thumbnail))
            {
                if (thumbnail != null)
                {
                    image.image = thumbnail;
                }
                return;
            }
            if (_pending.TryGetValue(key, out List<Image> waiting))
            {
                waiting.Add(image);
                return;
            }
            _pending[key] = new List<Image> { image };
            LoadThumbnailAsync(key, fullPath, GitRepository.GitDir);
        }

        /// <summary>
        /// 썸네일을 만들 파일의 전체 경로. Unity 가 아이콘을 주는 에셋(Assets 안 PSD 는 임포터가 미리 보기를 만든다)이나 PSD 가 아니면 null.
        /// </summary>
        private static string ThumbnailSource(string projectPath, string repoPath)
        {
            string path = repoPath ?? projectPath;
            if (GitPsd.IsPsd(path) == false || string.IsNullOrEmpty(GitRepository.RepoRoot))
            {
                return null;
            }
            string assetPath = GitDisplay.AssetPathOf(projectPath);
            if (string.IsNullOrEmpty(assetPath) == false && string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(assetPath)) == false)
            {
                return null;
            }
            return Path.GetFullPath(Path.Combine(GitRepository.RepoRoot, path));
        }

        /// <returns>파일이 없으면 null.</returns>
        private static string ThumbnailKey(string fullPath)
        {
            try
            {
                FileInfo info = new(fullPath);
                return info.Exists ? $"{fullPath}|{info.Length}|{info.LastWriteTimeUtc.Ticks}" : null;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException)
            {
                return null;
            }
        }

        private async void LoadThumbnailAsync(string key, string fullPath, string gitDir)
        {
            (Color32[] pixels, int width, int height) = await Task.Run(async () =>
            {
                await ThumbnailGate.WaitAsync();
                try
                {
                    return DecodeThumbnail(fullPath, gitDir);
                }
                finally
                {
                    ThumbnailGate.Release();
                }
            });

            // 여기부터 메인 스레드(텍스처는 메인 스레드에서만 만든다).
            Texture2D texture = null;
            if (pixels != null)
            {
                texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };
                texture.SetPixels32(pixels);
                texture.Apply(false, true);
            }
            _thumbnails[key] = texture;
            if (_pending.Remove(key, out List<Image> waiting) && texture != null)
            {
                foreach (Image image in waiting)
                {
                    if (Equals(image.userData, key))
                    {
                        image.image = texture;
                    }
                }
            }
        }

        /// <summary>
        /// 백그라운드 스레드에서 PSD 의 합쳐진 이미지를 읽어 썸네일 픽셀(Unity 순서)을 만든다. 만들 수 없으면 pixels 가 null.
        /// 작업 사본이 LFS 주소 파일이면 이미 받아 둔 원본(.git/lfs)을 쓰고, 없으면 만들지 않는다(내려받지 않는다).
        /// </summary>
        private static (Color32[] pixels, int width, int height) DecodeThumbnail(string fullPath, string gitDir)
        {
            try
            {
                string source = fullPath;
                if (GitLfs.ReadPointer(fullPath) is (string oid, long size))
                {
                    source = GitLfs.LocalObjectPath(gitDir, oid, size);
                    if (source == null)
                    {
                        return (null, 0, 0);
                    }
                }
                FileInfo info = new(source);
                if (info.Exists == false || info.Length > MaxThumbnailFileBytes)
                {
                    return (null, 0, 0);
                }
                byte[] header = new byte[26];
                using (FileStream stream = File.OpenRead(source))
                {
                    if (stream.Read(header, 0, header.Length) != header.Length)
                    {
                        return (null, 0, 0);
                    }
                }
                if (GitPsd.TryReadSize(header, out int width, out int height) == false || (long)width * height > MaxThumbnailPixels)
                {
                    return (null, 0, 0);
                }
                GitPsdImage image = GitPsd.Decode(File.ReadAllBytes(source));
                return GitPsd.Downscale(image.Pixels, image.Width, image.Height, ThumbnailSize);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidDataException || e is NotSupportedException
                                      || e is IndexOutOfRangeException || e is OverflowException || e is OutOfMemoryException || e is ArgumentException)
            {
                return (null, 0, 0);
            }
        }

        public Texture GetIcon(string projectPath, string repoPath)
        {
            string key = projectPath ?? repoPath ?? string.Empty;
            if (_cache.TryGetValue(key, out Texture cached) && cached != null)
            {
                return cached;
            }

            Texture icon = Find(projectPath, repoPath);
            _cache[key] = icon;
            return icon;
        }

        private Texture Find(string projectPath, string repoPath)
        {
            string assetPath = GitDisplay.AssetPathOf(projectPath);
            if (string.IsNullOrEmpty(assetPath) == false)
            {
                if (AssetDatabase.IsValidFolder(assetPath))
                {
                    _folderIcon ??= EditorGUIUtility.IconContent("Folder Icon").image;
                    return _folderIcon;
                }
                Texture assetIcon = AssetDatabase.GetCachedIcon(assetPath);
                if (assetIcon != null)
                {
                    return assetIcon;
                }
            }
            return InternalEditorUtility.GetIconForFile(repoPath ?? projectPath ?? string.Empty);
        }
    }
}
