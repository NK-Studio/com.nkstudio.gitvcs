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
            if (content == null || content.Length == 0 || content.Length >= MaxPointerBytes)
            {
                return null;
            }
            string text;
            try
            {
                text = new UTF8Encoding(false, true).GetString(content);
            }
            catch (ArgumentException)
            {
                return null;
            }
            if (text.StartsWith(VersionPrefix, StringComparison.Ordinal) == false)
            {
                return null;
            }

            bool hasOid = false;
            long? size = null;
            foreach (string line in text.Split('\n'))
            {
                if (line.StartsWith("oid sha256:", StringComparison.Ordinal))
                {
                    hasOid = true;
                }
                else if (line.StartsWith("size ", StringComparison.Ordinal)
                         && long.TryParse(line.Substring(5).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out long parsed))
                {
                    size = parsed;
                }
            }
            return hasOid ? size : null;
        }

        /// <returns>파일이 LFS 주소 파일이면 원본 크기, 아니거나 읽을 수 없으면 null.</returns>
        public static long? ReadPointerSize(string path)
        {
            try
            {
                FileInfo info = new(path);
                if (info.Exists == false || info.Length == 0 || info.Length >= MaxPointerBytes)
                {
                    return null;
                }
                return ParsePointerSize(File.ReadAllBytes(path));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException)
            {
                return null;
            }
        }
    }
}
