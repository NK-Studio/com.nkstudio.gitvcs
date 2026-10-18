using System.Collections.Generic;
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
        [SerializeField] private bool _avatarsUseGitCredentials;
        [SerializeField] private string _myGitHubLogin = string.Empty;
        [SerializeField] private int _diffEngine;
        [SerializeField] private int _diffAlgorithm = (int)DiffAlgorithm.Histogram;
        [SerializeField] private int _diffWhitespace;
        [SerializeField] private int _diffContextLines = 3;
        [SerializeField] private int _diffLayout = (int)DiffLayout.SideBySide;
        [SerializeField] private string _difftoolName = string.Empty;
        [SerializeField] private string _customDiffArguments = ExternalDiffTool.DefaultCustomArguments;
        [SerializeField] private List<string> _diffToolPaths = new();
        [SerializeField] private float _gitWindowScale = 1f;
        [SerializeField] private float _diffWindowScale = 1f;
        [SerializeField] private int _theme = (int)GitThemeStyle.JetBrains;
        [SerializeField] private int _layout;

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

        /// <summary>
        /// 아바타를 찾을 때 git 에 저장된 GitHub 자격 증명(Git Credential Manager 등)으로 GitHub API 에 로그인할지.
        /// 비공개 저장소는 로그인하지 않으면 커밋의 작성자 계정을 알 수 없다. 저장된 비밀을 읽는 일이라 기본은 꺼 둔다.
        /// </summary>
        public bool AvatarsUseGitCredentials
        {
            get => _avatarsUseGitCredentials;
            set => SetAndSave(ref _avatarsUseGitCredentials, value);
        }

        /// <summary>
        /// 내 GitHub 아이디. 이 저장소의 git user.email 로 만든 커밋(내 커밋)에 이 계정의 프로필 사진을 쓴다.
        /// 아직 푸시하지 않은 커밋은 GitHub 가 작성자를 모르므로, 이 값이 없으면 내 커밋은 머리글자로 남는다(GitKraken 은 로그인한 계정 사진을 쓴다).
        /// </summary>
        public string MyGitHubLogin
        {
            get => _myGitHubLogin ?? string.Empty;
            set => SetAndSave(ref _myGitHubLogin, value?.Trim() ?? string.Empty);
        }

        /// <summary>
        /// 파일 diff 를 무엇으로 볼지. 기본은 Git 창 안의 내장 뷰어.
        /// </summary>
        public DiffEngine DiffEngine
        {
            get => (DiffEngine)_diffEngine;
            set
            {
                int index = (int)value;
                SetAndSave(ref _diffEngine, index);
            }
        }

        /// <summary>
        /// 내장 뷰어의 git diff 알고리즘. 기본은 histogram — 함수·블록이 옮겨진 코드에서 myers 보다 사람이 읽기 좋은 결과를 낸다.
        /// </summary>
        public DiffAlgorithm DiffAlgorithm
        {
            get => (DiffAlgorithm)_diffAlgorithm;
            set
            {
                int index = (int)value;
                SetAndSave(ref _diffAlgorithm, index);
            }
        }

        public DiffWhitespace DiffWhitespace
        {
            get => (DiffWhitespace)_diffWhitespace;
            set
            {
                int index = (int)value;
                SetAndSave(ref _diffWhitespace, index);
            }
        }

        /// <summary>
        /// 바뀐 줄 앞뒤로 보여 줄 줄 수(git 의 -U).
        /// </summary>
        public int DiffContextLines
        {
            get => _diffContextLines;
            set
            {
                int clamped = Mathf.Clamp(value, 0, GitDiffOptions.MaxContextLines);
                SetAndSave(ref _diffContextLines, clamped);
            }
        }

        public DiffLayout DiffLayout
        {
            get => (DiffLayout)_diffLayout;
            set
            {
                int index = (int)value;
                SetAndSave(ref _diffLayout, index);
            }
        }

        public GitDiffOptions DiffOptions => new(DiffAlgorithm, DiffWhitespace, DiffContextLines);

        /// <summary>
        /// <c>git difftool --tool=</c> 에 넘길 이름. 비우면 git 설정(diff.tool)을 따른다.
        /// </summary>
        public string DifftoolName
        {
            get => _difftoolName ?? string.Empty;
            set => SetAndSave(ref _difftoolName, value?.Trim() ?? string.Empty);
        }

        /// <summary>
        /// Custom 엔진의 인자 틀({LEFT} {RIGHT} {LEFT_TITLE} {RIGHT_TITLE}).
        /// </summary>
        public string CustomDiffArguments
        {
            get => string.IsNullOrWhiteSpace(_customDiffArguments) ? ExternalDiffTool.DefaultCustomArguments : _customDiffArguments;
            set => SetAndSave(ref _customDiffArguments, value);
        }

        /// <summary>
        /// 엔진별 프로그램 경로. 비어 있으면 흔한 설치 위치·PATH 에서 찾는다(Custom 은 반드시 정해야 한다).
        /// </summary>
        public string GetDiffToolPath(DiffEngine engine)
        {
            int index = (int)engine;
            return index < _diffToolPaths.Count ? _diffToolPaths[index] ?? string.Empty : string.Empty;
        }

        public void SetDiffToolPath(DiffEngine engine, string path)
        {
            int index = (int)engine;
            while (_diffToolPaths.Count <= index)
            {
                _diffToolPaths.Add(string.Empty);
            }
            string trimmed = path?.Trim() ?? string.Empty;
            if (_diffToolPaths[index] == trimmed)
            {
                return;
            }
            _diffToolPaths[index] = trimmed;
            Save(true);
        }

        /// <summary>
        /// Git 창 UI 배율(Ctrl+휠). 범위·단위는 <see cref="UiScaleController"/> 가 맞춘다.
        /// </summary>
        public float GitWindowScale
        {
            get => UiScaleController.Clamp(_gitWindowScale);
            set => SetAndSave(ref _gitWindowScale, UiScaleController.Clamp(value));
        }

        /// <summary>
        /// diff 뷰어 UI 배율. 코드는 Git 창과 따로 키워 보는 일이 많아 나눠 둔다.
        /// </summary>
        public float DiffWindowScale
        {
            get => UiScaleController.Clamp(_diffWindowScale);
            set => SetAndSave(ref _diffWindowScale, UiScaleController.Clamp(value));
        }

        /// <summary>
        /// 창 모양(JetBrains·Unity). 읽기·바꾸기는 <see cref="GitTheme"/> 를 통한다 — 바꾸면 열린 창이 다시 칠해야 해서.
        /// </summary>
        public GitThemeStyle Theme
        {
            get => (GitThemeStyle)_theme;
            set
            {
                int index = (int)value;
                SetAndSave(ref _theme, index);
            }
        }

        /// <summary>
        /// 변경 탭 배치(Unity VCS 체크박스 · Fork · GitKraken). 바꾸면 Git 창이 UI 를 다시 만든다.
        /// </summary>
        public GitLayoutStyle Layout
        {
            get => (GitLayoutStyle)_layout;
            set
            {
                int index = (int)value;
                SetAndSave(ref _layout, index);
            }
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
