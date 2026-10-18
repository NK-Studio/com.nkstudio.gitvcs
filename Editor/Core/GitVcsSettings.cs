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
        [SerializeField] private int _diffEngine;
        [SerializeField] private int _diffAlgorithm = (int)DiffAlgorithm.Histogram;
        [SerializeField] private int _diffWhitespace;
        [SerializeField] private int _diffContextLines = 3;
        [SerializeField] private int _diffLayout = (int)DiffLayout.SideBySide;
        [SerializeField] private string _difftoolName = string.Empty;
        [SerializeField] private string _customDiffArguments = ExternalDiffTool.DefaultCustomArguments;
        [SerializeField] private List<string> _diffToolPaths = new();

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
