using System;
using System.Linq;

namespace GitVersionControl
{
    /// <summary>
    /// 작업 트리의 변경이 덮어써질까 봐 git 이 작업(Pull·Merge·전환·체리픽 등)을 거부했는지 알아본다.
    /// 이때는 변경을 stash 해 두고 다시 하면 된다.
    /// </summary>
    public static class GitLocalChanges
    {
        /// <summary>
        /// git 이 남기는 글. 영어와, git 이 한국어로 설정돼 있을 때의 번역("다음 파일의 로컬 변경 사항을 병합 때문에 덮어 쓰게 됩니다").
        /// 추적하지 않는 새 파일이 덮어써지는 경우도 같다 — stash 는 새 파일도 넣는다.
        /// </summary>
        private static readonly string[] Patterns =
        {
            "would be overwritten by",
            "덮어 쓰게 됩니다",
            "덮어쓰게 됩니다",
        };

        public static bool WouldOverwrite(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return false;
            }
            return Patterns.Any(pattern => message.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
