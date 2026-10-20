using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace GitVersionControl
{
    /// <summary>
    /// Git LFS 주소 파일(pointer) 판별. 원본을 받지 않은 LFS 파일은 실제 내용 대신 이 작은 글 파일이 남는다:
    /// <code>
    /// version https://git-lfs.github.com/spec/v1
    /// oid sha256:…
    /// size 2834512
    /// </code>
    /// </summary>
    public static class GitLfs
    {
        /// <summary>
        /// 주소 파일은 이보다 작다(git-lfs 도 1024 바이트 미만만 주소 파일로 본다).
        /// </summary>
        public const int MaxPointerBytes = 1024;

        private const string VersionPrefix = "version https://git-lfs.github.com/spec/";

        /// <summary>
        /// 저장소 설정(<c>lfs.fetchexclude</c>·<c>lfs.fetchinclude</c>)을 이번 한 번만 비워 그 파일 원본도 받게 하는 git 인자.
        /// </summary>
        public static IReadOnlyList<string> FetchAllArguments { get; } = new[] { "-c", "lfs.fetchexclude=", "-c", "lfs.fetchinclude=" };

        /// <returns>LFS 주소 파일이면 원본 크기(바이트), 아니면 null.</returns>
        public static long? ParsePointerSize(byte[] content)
        {
            return TryParsePointer(content, out _, out long size) ? size : null;
        }

        /// <summary>
        /// LFS 주소 파일이면 원본의 sha256(소문자 16진수 64자)과 크기를 꺼낸다.
        /// </summary>
        public static bool TryParsePointer(byte[] content, out string oid, out long size)
        {
            oid = null;
            size = 0;
            if (content == null || content.Length == 0 || content.Length >= MaxPointerBytes)
            {
                return false;
            }
            string text;
            try
            {
                text = new UTF8Encoding(false, true).GetString(content);
            }
            catch (ArgumentException)
            {
                return false;
            }
            if (text.StartsWith(VersionPrefix, StringComparison.Ordinal) == false)
            {
                return false;
            }

            bool hasSize = false;
            foreach (string rawLine in text.Split('\n'))
            {
                string line = rawLine.TrimEnd('\r');
                if (line.StartsWith("oid sha256:", StringComparison.Ordinal))
                {
                    string value = line.Substring("oid sha256:".Length).Trim();
                    oid = IsSha256Hex(value) ? value : null;
                }
                else if (line.StartsWith("size ", StringComparison.Ordinal)
                         && long.TryParse(line.Substring(5).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out long parsed))
                {
                    size = parsed;
                    hasSize = true;
                }
            }
            return oid != null && hasSize;
        }

        private static bool IsSha256Hex(string value)
        {
            if (value.Length != 64)
            {
                return false;
            }
            foreach (char c in value)
            {
                if ((c >= '0' && c <= '9') == false && (c >= 'a' && c <= 'f') == false)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 이미 받아 둔 LFS 원본의 경로(<c>.git/lfs/objects/ab/cd/abcd…</c>). 없거나 크기가 다르면(받다 끊김) null.
        /// 저장소가 <c>lfs.fetchexclude</c> 로 막아 둔 경로는 원본이 이미 있어도 git-lfs 가 주소 파일만 내주므로 직접 찾아 쓴다.
        /// </summary>
        /// <param name="gitDir">.git 폴더. 작업 트리(worktree)의 git 폴더면 commondir 을 따라 본 저장소의 폴더를 쓴다.</param>
        public static string LocalObjectPath(string gitDir, string oid, long size)
        {
            if (string.IsNullOrEmpty(gitDir) || oid == null || IsSha256Hex(oid) == false)
            {
                return null;
            }
            try
            {
                string commonDir = gitDir;
                string commonDirFile = Path.Combine(gitDir, "commondir");
                if (File.Exists(commonDirFile))
                {
                    commonDir = Path.GetFullPath(Path.Combine(gitDir, File.ReadAllText(commonDirFile).Trim()));
                }
                string path = Path.Combine(commonDir, "lfs", "objects", oid.Substring(0, 2), oid.Substring(2, 2), oid);
                FileInfo info = new(path);
                return info.Exists && info.Length == size ? path : null;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException)
            {
                return null;
            }
        }

        /// <returns>파일이 LFS 주소 파일이면 (oid, 크기), 아니거나 읽을 수 없으면 null.</returns>
        public static (string oid, long size)? ReadPointer(string path)
        {
            try
            {
                FileInfo info = new(path);
                if (info.Exists == false || info.Length == 0 || info.Length >= MaxPointerBytes)
                {
                    return null;
                }
                return TryParsePointer(File.ReadAllBytes(path), out string oid, out long size) ? (oid, size) : null;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException)
            {
                return null;
            }
        }

        /// <returns>파일이 LFS 주소 파일이면 원본 크기, 아니거나 읽을 수 없으면 null.</returns>
        public static long? ReadPointerSize(string path)
        {
            return ReadPointer(path)?.size;
        }
    }
}
