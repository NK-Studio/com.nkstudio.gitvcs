using System;

namespace GitVersionControl
{
    /// <summary>
    /// Git 창 첫 탭의 배치. 설정 파일에 숫자로 저장되므로 끝에만 추가한다.
    /// </summary>
    public enum GitLayoutStyle
    {
        /// <summary>
        /// Unity Version Control 처럼 체크박스로 고른 파일만 커밋한다(인덱스를 쓰지 않는다).
        /// </summary>
        UnityVcs,

        /// <summary>
        /// Fork 처럼 가운데 Unstaged·Staged, 오른쪽 diff·커밋.
        /// </summary>
        Fork,

        /// <summary>
        /// GitKraken 처럼 가운데 세로 커밋 그래프, 오른쪽 Unstaged·Staged·커밋·푸시.
        /// </summary>
        GitKraken,
    }

    /// <summary>
    /// 배치 고르기. 바꾸면 열린 Git 창이 UI 를 다시 만든다(탭 구성이 달라서 다시 칠하기로는 안 된다).
    /// </summary>
    public static class GitLayout
    {
        public static event Action Changed;

        public static GitLayoutStyle Style
        {
            get => GitVcsSettings.instance.Layout;
            set
            {
                if (GitVcsSettings.instance.Layout == value)
                {
                    return;
                }
                GitVcsSettings.instance.Layout = value;
                Changed?.Invoke();
            }
        }

        public static string DisplayName(GitLayoutStyle style)
        {
            return GitText.Get("layout." + style);
        }
    }
}
