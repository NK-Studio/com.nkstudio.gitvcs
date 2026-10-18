using UnityEditor;
using UnityEngine;

namespace GitVersionControl
{
    /// <summary>
    /// 사용자·머신별 설정. git 경로는 머신마다 다르므로 저장소에 올라가지 않는 UserSettings 에 둔다.
    /// </summary>
    [FilePath("UserSettings/GitVersionControl.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class GitVcsSettings : ScriptableSingleton<GitVcsSettings>
    {
        [SerializeField] private string _gitExecutable = "git";
        [SerializeField] private bool _showOverlays = true;
        [SerializeField] private bool _refreshOnFocus = true;
        [SerializeField] private int _language = -1;
        [SerializeField] private int _fileViewMode;
        [SerializeField] private bool _showAvatars;

        /// <summary>비워 두면 PATH 의 git 을 쓴다.</summary>
        public string GitExecutable
        {
            get => string.IsNullOrWhiteSpace(_gitExecutable) ? "git" : _gitExecutable.Trim();
            set => SetAndSave(ref _gitExecutable, value);
        }

        public bool ShowOverlays
        {
            get => _showOverlays;
            set => SetAndSave(ref _showOverlays, value);
        }

        public bool RefreshOnFocus
        {
            get => _refreshOnFocus;
            set => SetAndSave(ref _refreshOnFocus, value);
        }

        /// <summary>화면 언어. 고른 적이 없으면(-1) 시스템 언어를 따른다. 읽기·바꾸기는 <see cref="GitText"/> 를 통한다.</summary>
        public GitLanguage Language
        {
            get => _language < 0 ? GitText.SystemDefault : (GitLanguage)_language;
            set
            {
                int index = (int)value;
                SetAndSave(ref _language, index);
            }
        }

        /// <summary>파일 목록 보기 방식(목록/트리). 읽기·바꾸기는 <see cref="FileViewPreference"/> 를 통한다.</summary>
        public FileViewMode FileViewMode
        {
            get => (FileViewMode)_fileViewMode;
            set
            {
                int index = (int)value;
                SetAndSave(ref _fileViewMode, index);
            }
        }

        /// <summary>
        /// 브랜치 탐색기에 작성자 아바타를 보일지. 작성자 이메일(해시)·커밋 해시를 GitHub·Gravatar 로 보내므로 기본은 꺼 둔다.
        /// </summary>
        public bool ShowAvatars
        {
            get => _showAvatars;
            set => SetAndSave(ref _showAvatars, value);
        }

        private void SetAndSave<T>(ref T field, T value)
        {
            if (Equals(field, value))
            {
                return;
            }
            field = value;
            Save(true);
        }
    }
}
