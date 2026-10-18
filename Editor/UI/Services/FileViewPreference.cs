using System;

namespace GitVersionControl
{
    public enum FileViewMode
    {
        /// <summary>전체 경로를 한 줄씩(지금까지의 보기).</summary>
        List,

        /// <summary>폴더 트리.</summary>
        Tree,
    }

    /// <summary>
    /// 파일 목록 보기 방식(목록/트리). 사용자 설정에 저장하고, 바뀌면 열린 모든 파일 목록이 함께 바뀐다.
    /// </summary>
    public sealed class FileViewPreference
    {
        public FileViewMode Mode
        {
            get => GitVcsSettings.instance.FileViewMode;
            set
            {
                if (Mode == value)
                {
                    return;
                }
                GitVcsSettings.instance.FileViewMode = value;
                Changed?.Invoke();
            }
        }

        public event Action Changed;
    }
}
