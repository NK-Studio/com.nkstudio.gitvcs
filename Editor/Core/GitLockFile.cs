using System;
using System.IO;
using System.Text.RegularExpressions;

namespace GitVersionControl
{
    /// <summary>
    /// git 이 남긴 잠금 파일(<c>.git/index.lock</c> 등). git 이 도중에 끝나면(강제 종료·시간 초과·에디터 종료) 지워지지 않고 남아,
    /// 그 뒤 모든 쓰기 작업이 "Unable to create '…/index.lock': File exists" 로 실패한다.
    /// </summary>
    public static class GitLockFile
    {
        private static readonly Regex LockError = new(@"Unable to create '(?<path>[^']+\.lock)': File exists", RegexOptions.Compiled);

        /// <summary>
        /// git 오류 글에서 남은 잠금 파일 경로를 찾는다. 저장소 git 폴더(<paramref name="gitDir"/>) 안의 .lock 파일일 때만 true — 엉뚱한 파일을 지우지 않게.
        /// </summary>
        public static bool TryFind(string message, string gitDir, out string lockPath)
        {
            lockPath = null;
            if (string.IsNullOrEmpty(message) || string.IsNullOrEmpty(gitDir))
            {
                return false;
            }
            Match match = LockError.Match(message);
            if (match.Success == false)
            {
                return false;
            }
            string path = Path.GetFullPath(match.Groups["path"].Value);
            string root = Path.GetFullPath(gitDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase) == false)
            {
                return false;
            }
            lockPath = path;
            return true;
        }
    }
}
