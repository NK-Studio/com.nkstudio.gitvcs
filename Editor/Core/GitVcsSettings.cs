using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GitVersionControl
{
    /// <summary>
    /// 커밋·브랜치 우클릭 메뉴를 어느 도구의 순서·이름으로 보일지.
    /// </summary>
    public enum GitMenuStyle
    {
        /// <summary>
        /// Fork·GitKraken: git 용어(Checkout·Reset·Interactive Rebase)와 브랜치 이름이 들어간 항목.
        /// </summary>
        Fork,

        /// <summary>
        /// Unity Version Control(Plastic SCM)의 체인지 세트·브랜치 메뉴 순서.
        /// </summary>
        UnityVersionControl,
    }

    /// <summary>
    /// 작업 실패 팝업(<see cref="GitErrorDialog"/>)이 무엇을 먼저 보일지.
    /// </summary>
    public enum ErrorDetailMode
    {
        /// <summary>
        /// 원인과 해결 방법을 쉽게 풀어 보인다(<see cref="GitErrorAdvice"/>).
        /// </summary>
        Friendly,

        /// <summary>
        /// git 이 남긴 글을 그대로 보인다.
        /// </summary>
        Developer,
    }

    /// <summary>
    /// 사용자·머신별 설정. 저장소에 올라가지 않는 UserSettings 에 두고, Preferences > Git 의 개인 설정(언어·테마·git 경로·Diff 등)은
    /// EditorPrefs 에도 두어 이 PC 의 모든 프로젝트가 같은 값을 쓴다(<see cref="OnEnable"/>).
    /// </summary>
    [FilePath("UserSettings/GitVersionControl.asset", FilePathAttribute.Location.ProjectFolder)]
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public sealed class GitVcsSettings : ScriptableSingleton<GitVcsSettings>
    {
        [SerializeField] private string _gitExecutable = "git";
        [SerializeField] private bool _showOverlays = true;
        [SerializeField] private bool _refreshOnFocus = true;
        [SerializeField] private bool _watchFileSystem = true;
        [SerializeField] private bool _autoFetch = true;
        [SerializeField] private int _autoFetchMinutes = GitAutoFetch.DefaultIntervalMinutes;
        [SerializeField] private bool _stopOnRebaseConflict;
        [SerializeField] private bool _stageMetaPairs;
        [SerializeField] private bool _groupMetaPairs = true;
        [SerializeField] private int _accountSource;
        [SerializeField] private string _accountName = string.Empty;
        [SerializeField] private bool _checkoutNewBranch;
        [SerializeField] private int _language = -1;
        [SerializeField] private int _fileViewMode;
        [SerializeField] private bool _showAvatars = true;
        [SerializeField] private int _diffEngine;
        [SerializeField] private int _diffAlgorithm = (int)DiffAlgorithm.Histogram;
        [SerializeField] private int _diffWhitespace;
        [SerializeField] private int _diffContextLines = 3;
        [SerializeField] private int _diffLayout = (int)DiffLayout.SideBySide;
        [SerializeField] private bool _diffFileView;
        [SerializeField] private bool _diffShowWhitespace;
        [SerializeField] private bool _diffWordWrap;
        [SerializeField] private int _imageDiffMode = (int)ImageDiffMode.TwoUp;
        [SerializeField] private bool _imageDiffShowProperties;
        [SerializeField] private float _imageDiffPropertiesHeight = 90f;
        [SerializeField] private string _difftoolName = string.Empty;
        [SerializeField] private string _customDiffArguments = ExternalDiffTool.DefaultCustomArguments;
        [SerializeField] private List<string> _diffToolPaths = new();
        [SerializeField] private float _gitWindowScale = 1f;
        // 예전 _theme(JetBrains·Unity 모양)과 이름을 달리해, 남아 있던 값이 밝기로 잘못 읽히지 않고 System 에서 시작하게 한다.
        [SerializeField] private int _themeMode = (int)GitThemeMode.System;
        [SerializeField] private int _layout;
        [SerializeField] private bool _showInMainToolbar = true;
        [SerializeField] private int _menuStyle;
        [SerializeField] private int _errorDetailMode;
        [SerializeField] private float _graphRefsColumnWidth;
        [SerializeField] private float _graphColumnWidth;
        [SerializeField] private float _graphMessageColumnWidth;
        [SerializeField] private string _graphColumnOrder = string.Empty;

        /// <summary>
        /// 내장 diff 의 git 옵션(알고리즘·공백·문맥 줄 수)이 바뀌었을 때. 열려 있는 diff 가 다시 읽는다 — 설정 화면에서만 바꾼다.
        /// </summary>
        public static event System.Action DiffOptionsChanged;

        /// <summary>
        /// "에셋과 .meta 를 한 줄로 묶어 보기"(<see cref="GroupMetaPairs"/>)를 바꿨을 때. 변경 목록이 다시 그린다.
        /// </summary>
        public static event System.Action MetaGroupingChanged;

        /// <summary>비워 두면 PATH 의 git 을 쓴다.</summary>
        public string GitExecutable
        {
            get => string.IsNullOrWhiteSpace(_gitExecutable) ? "git" : _gitExecutable.Trim();
            set => SetAndSave(ref _gitExecutable, value, nameof(GitExecutable));
        }

        public bool ShowOverlays
        {
            get => _showOverlays;
            set => SetAndSave(ref _showOverlays, value, nameof(ShowOverlays));
        }

        public bool RefreshOnFocus
        {
            get => _refreshOnFocus;
            set => SetAndSave(ref _refreshOnFocus, value, nameof(RefreshOnFocus));
        }

        /// <summary>
        /// 저장소 폴더를 감시해 바깥(터미널·IDE)에서 생긴 변경을 바로 반영할지(<see cref="GitFileWatcher"/>).
        /// </summary>
        public bool WatchFileSystem
        {
            get => _watchFileSystem;
            set => SetAndSave(ref _watchFileSystem, value, nameof(WatchFileSystem));
        }

        /// <summary>
        /// 원격을 주기적으로 fetch 해 다른 곳에서 푸시한 커밋을 Pull 전에 보여 줄지(<see cref="GitAutoFetch"/>).
        /// </summary>
        public bool AutoFetch
        {
            get => _autoFetch;
            set => SetAndSave(ref _autoFetch, value, nameof(AutoFetch));
        }

        /// <summary>자동 fetch 간격(분). <see cref="GitAutoFetch.MinIntervalMinutes"/>~<see cref="GitAutoFetch.MaxIntervalMinutes"/>.</summary>
        public int AutoFetchMinutes
        {
            get => Math.Clamp(_autoFetchMinutes, GitAutoFetch.MinIntervalMinutes, GitAutoFetch.MaxIntervalMinutes);
            set => SetAndSave(ref _autoFetchMinutes, Math.Clamp(value, GitAutoFetch.MinIntervalMinutes, GitAutoFetch.MaxIntervalMinutes), nameof(AutoFetchMinutes));
        }

        /// <summary>
        /// Git 창에서 시작한 rebase(브랜치 rebase·커밋 빼기)가 충돌하면 멈춰 두고 창 안에서 풀게 할지. 기본은 끔 —
        /// 바로 취소해 시작 전 상태로 돌린다(<see cref="GitUndo.RebaseAsync"/>). 켜면 Git 창 위쪽 배너에서 충돌을 풀고 이어가거나 취소한다(<see cref="GitInProgress"/>).
        /// </summary>
        public bool StopOnRebaseConflict
        {
            get => _stopOnRebaseConflict;
            set => SetAndSave(ref _stopOnRebaseConflict, value, nameof(StopOnRebaseConflict));
        }

        /// <summary>
        /// Stage·Unstage·변경 버리기 때 같은 목록의 .meta 짝(A.cs ↔ A.cs.meta)을 같이 옮길지. 기본은 끔 —
        /// Fork·GitKraken 처럼 고른 파일만 옮긴다. 켜면 한쪽만 커밋돼 GUID 가 어긋나는 일을 막는다(<see cref="GitStaging.WithMetaPairs"/>).
        /// </summary>
        public bool StageMetaPairs
        {
            get => _stageMetaPairs;
            set => SetAndSave(ref _stageMetaPairs, value, nameof(StageMetaPairs));
        }

        /// <summary>
        /// 변경 목록에서 에셋과 그 .meta 를 한 줄("A.png +meta")로 묶어 보일지. 기본은 켬(Unity Version Control 과 같다).
        /// 켜면 그 줄의 Stage·Unstage·변경 버리기·체크는 짝까지 함께 하고, 한쪽만 하려면 우클릭의 "에셋만"·".meta 만" 을 쓴다.
        /// </summary>
        public bool GroupMetaPairs
        {
            get => _groupMetaPairs;
            set
            {
                if (_groupMetaPairs == value)
                {
                    return;
                }
                SetAndSave(ref _groupMetaPairs, value, nameof(GroupMetaPairs));
                MetaGroupingChanged?.Invoke();
            }
        }

        /// <summary>
        /// 이 프로젝트 저장소의 GitHub 원격에 쓸 토큰을 어디서 가져올지(<c>GitAccounts</c>). 0 = git 의 자격 증명 도우미 그대로,
        /// 1 = gh 의 <see cref="AccountName"/> 계정, 2 = 패키지 로그인(Preferences > Git). 계정 접근 실패 팝업이나 Git 창 ⋮ 메뉴에서 고른다.
        /// </summary>
        public int AccountSource
        {
            get => _accountSource;
            set => SetAndSave(ref _accountSource, value);
        }

        /// <summary>
        /// <see cref="AccountSource"/> 가 gh 일 때 그 계정 이름(<c>gh auth token --user</c>).
        /// </summary>
        public string AccountName
        {
            get => _accountName ?? string.Empty;
            set => SetAndSave(ref _accountName, value ?? string.Empty);
        }

        /// <summary>
        /// Stage·Unstage·변경 버리기에 같은 목록의 .meta 짝을 더할지. 묶어 보기를 켰거나 "짝 함께 옮기기" 를 켰을 때.
        /// </summary>
        public bool MovesMetaPairs => _groupMetaPairs || _stageMetaPairs;

        /// <summary>
        /// 브랜치 만들기 창의 "만든 뒤 체크아웃" 을 마지막으로 고른 값(<see cref="GitBranchDialog"/>). 기본은 끔(Fork 와 같다).
        /// </summary>
        public bool CheckoutNewBranch
        {
            get => _checkoutNewBranch;
            set => SetAndSave(ref _checkoutNewBranch, value);
        }

        /// <summary>화면 언어. 고른 적이 없으면(-1) 시스템 언어를 따른다. 읽기·바꾸기는 <see cref="GitText"/> 를 통한다.</summary>
        public GitLanguage Language
        {
            get => _language < 0 ? GitText.SystemDefault : (GitLanguage)_language;
            set
            {
                int index = (int)value;
                SetAndSave(ref _language, index, nameof(Language));
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
            set => SetAndSave(ref _showAvatars, value, nameof(ShowAvatars));
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
                SetAndSave(ref _diffEngine, index, nameof(DiffEngine));
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
                if (SetAndSave(ref _diffAlgorithm, index, nameof(DiffAlgorithm)))
                {
                    DiffOptionsChanged?.Invoke();
                }
            }
        }

        public DiffWhitespace DiffWhitespace
        {
            get => (DiffWhitespace)_diffWhitespace;
            set
            {
                int index = (int)value;
                if (SetAndSave(ref _diffWhitespace, index, nameof(DiffWhitespace)))
                {
                    DiffOptionsChanged?.Invoke();
                }
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
                if (SetAndSave(ref _diffContextLines, clamped, nameof(DiffContextLines)))
                {
                    DiffOptionsChanged?.Invoke();
                }
            }
        }

        public DiffLayout DiffLayout
        {
            get => (DiffLayout)_diffLayout;
            set
            {
                int index = (int)value;
                SetAndSave(ref _diffLayout, index, nameof(DiffLayout));
            }
        }

        /// <summary>
        /// diff 대신 바뀐 뒤 파일 전체를 본다(GitKraken 의 File View). 끄면 <see cref="DiffLayout"/> 대로 diff 를 본다.
        /// </summary>
        public bool DiffFileView
        {
            get => _diffFileView;
            set => SetAndSave(ref _diffFileView, value);
        }

        /// <summary>
        /// 코드 줄의 공백·탭을 흐린 · → 로 보인다.
        /// </summary>
        public bool DiffShowWhitespace
        {
            get => _diffShowWhitespace;
            set => SetAndSave(ref _diffShowWhitespace, value);
        }

        /// <summary>
        /// 긴 줄을 창 폭에서 접어 보인다. 끄면 넘치는 부분이 잘린다.
        /// </summary>
        public bool DiffWordWrap
        {
            get => _diffWordWrap;
            set => SetAndSave(ref _diffWordWrap, value);
        }

        /// <summary>
        /// 이미지 diff 아래에 양쪽 판의 속성(크기·형식·해상도)을 보일지. 마지막으로 고른 값을 다음 이미지에도 쓴다.
        /// </summary>
        public bool ImageDiffShowProperties
        {
            get => _imageDiffShowProperties;
            set => SetAndSave(ref _imageDiffShowProperties, value);
        }

        /// <summary>
        /// 이미지 diff 속성 칸의 높이(위 경계를 끌어 바꾼다).
        /// </summary>
        public float ImageDiffPropertiesHeight
        {
            get => _imageDiffPropertiesHeight;
            set => SetAndSave(ref _imageDiffPropertiesHeight, value);
        }

        /// <summary>
        /// 이미지 diff 보기 방식(나란히·스와이프·어니언 스킨·차이). 마지막으로 고른 방식을 다음 이미지에도 쓴다.
        /// </summary>
        public ImageDiffMode ImageDiffMode
        {
            get => (ImageDiffMode)_imageDiffMode;
            set
            {
                int index = (int)value;
                SetAndSave(ref _imageDiffMode, index);
            }
        }

        public GitDiffOptions DiffOptions => new(DiffAlgorithm, DiffWhitespace, DiffContextLines);

        /// <summary>
        /// <c>git difftool --tool=</c> 에 넘길 이름. 비우면 git 설정(diff.tool)을 따른다.
        /// </summary>
        public string DifftoolName
        {
            get => _difftoolName ?? string.Empty;
            set => SetAndSave(ref _difftoolName, value?.Trim() ?? string.Empty, nameof(DifftoolName));
        }

        /// <summary>
        /// Custom 엔진의 인자 틀({LEFT} {RIGHT} {LEFT_TITLE} {RIGHT_TITLE}).
        /// </summary>
        public string CustomDiffArguments
        {
            get => string.IsNullOrWhiteSpace(_customDiffArguments) ? ExternalDiffTool.DefaultCustomArguments : _customDiffArguments;
            set => SetAndSave(ref _customDiffArguments, value, nameof(CustomDiffArguments));
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
            EditorPrefs.SetString(PrefsPrefix + DiffToolPathsKey, string.Join("\n", _diffToolPaths));
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
        /// 창 밝기(System·Dark·Light). 읽기·바꾸기는 <see cref="GitTheme"/> 를 통한다 — 바꾸면 열린 창이 다시 칠해야 해서.
        /// </summary>
        public GitThemeMode ThemeMode
        {
            get => (GitThemeMode)_themeMode;
            set
            {
                int index = (int)value;
                SetAndSave(ref _themeMode, index, nameof(ThemeMode));
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
                SetAndSave(ref _layout, index, nameof(Layout));
            }
        }

        /// <summary>
        /// 에디터 메인 도구 막대에 Git 버튼을 둘지. 바꾸기는 <see cref="GitMainToolbar.SetShown"/> 를 통한다 — 도구 막대를 다시 그려야 해서.
        /// </summary>
        public bool ShowInMainToolbar
        {
            get => _showInMainToolbar;
            set => SetAndSave(ref _showInMainToolbar, value, nameof(ShowInMainToolbar));
        }

        /// <summary>
        /// 커밋·브랜치 우클릭 메뉴 구성. 기본은 git 클라이언트(Fork·GitKraken) 식.
        /// </summary>
        public GitMenuStyle MenuStyle
        {
            get => (GitMenuStyle)_menuStyle;
            set
            {
                int index = (int)value;
                SetAndSave(ref _menuStyle, index, nameof(MenuStyle));
            }
        }

        /// <summary>오류 팝업이 먼저 보일 내용. 팝업에서 바꾸면 기억해 다음에도 그대로 연다.</summary>
        public ErrorDetailMode ErrorDetailMode
        {
            get => (ErrorDetailMode)_errorDetailMode;
            set
            {
                int index = (int)value;
                SetAndSave(ref _errorDetailMode, index, nameof(ErrorDetailMode));
            }
        }

        /// <summary>
        /// 세로 커밋 그래프의 브랜치/태그 칸 폭(머리 줄 경계를 끌어 바꾼다). 0 이면 기본 폭.
        /// </summary>
        public float GraphRefsColumnWidth
        {
            get => _graphRefsColumnWidth;
            set => SetAndSave(ref _graphRefsColumnWidth, value);
        }

        /// <summary>
        /// 세로 커밋 그래프의 그래프 칸 폭. 0 이면 가지 수에 맞춘다.
        /// </summary>
        public float GraphColumnWidth
        {
            get => _graphColumnWidth;
            set => SetAndSave(ref _graphColumnWidth, value);
        }

        /// <summary>
        /// 세로 커밋 그래프의 메시지 칸 폭. 메시지 칸이 맨 오른쪽이 아닐 때만 쓴다(맨 오른쪽 칸은 남은 폭을 갖는다). 0 이면 기본 폭.
        /// </summary>
        public float GraphMessageColumnWidth
        {
            get => _graphMessageColumnWidth;
            set => SetAndSave(ref _graphMessageColumnWidth, value);
        }

        /// <summary>
        /// 세로 커밋 그래프의 칸 순서(머리 줄 칸을 끌어 바꾼다). <see cref="CommitGraphColumn"/> 번호를 쉼표로 이은 것, 비우면 기본 순서.
        /// </summary>
        public string GraphColumnOrder
        {
            get => _graphColumnOrder ?? string.Empty;
            set => SetAndSave(ref _graphColumnOrder, value ?? string.Empty);
        }

        /// <summary>
        /// Preferences > Git 에 있는 개인 설정을 EditorPrefs 에 두는 키 앞부분. 이 PC 의 모든 프로젝트가 같은 값을 쓴다.
        /// </summary>
        private const string PrefsPrefix = "GitVersionControl.";

        private const string DiffToolPathsKey = "DiffToolPaths";

        /// <summary>
        /// 개인 설정을 EditorPrefs 에서 읽어 필드에 채운다. git 은 백그라운드에서도 설정을 읽으므로(EditorPrefs 는 메인 스레드 전용)
        /// 필드를 그대로 캐시로 쓴다. EditorPrefs 에 아직 없으면 예전 UserSettings 값을 옮겨 적는다(처음 연 프로젝트의 값이 기준이 된다).
        /// </summary>
        private void OnEnable()
        {
            SyncPref(nameof(GitExecutable), ref _gitExecutable);
            SyncPref(nameof(ShowOverlays), ref _showOverlays);
            SyncPref(nameof(RefreshOnFocus), ref _refreshOnFocus);
            SyncPref(nameof(WatchFileSystem), ref _watchFileSystem);
            SyncPref(nameof(Language), ref _language);
            SyncPref(nameof(ShowAvatars), ref _showAvatars);
            SyncPref(nameof(DiffEngine), ref _diffEngine);
            SyncPref(nameof(DiffAlgorithm), ref _diffAlgorithm);
            SyncPref(nameof(DiffWhitespace), ref _diffWhitespace);
            SyncPref(nameof(DiffContextLines), ref _diffContextLines);
            SyncPref(nameof(DiffLayout), ref _diffLayout);
            SyncPref(nameof(DifftoolName), ref _difftoolName);
            SyncPref(nameof(CustomDiffArguments), ref _customDiffArguments);
            SyncPref(nameof(ThemeMode), ref _themeMode);
            SyncPref(nameof(Layout), ref _layout);
            SyncPref(nameof(ShowInMainToolbar), ref _showInMainToolbar);
            SyncPref(nameof(MenuStyle), ref _menuStyle);
            SyncPref(nameof(ErrorDetailMode), ref _errorDetailMode);
            SyncPref(nameof(AutoFetch), ref _autoFetch);
            SyncPref(nameof(AutoFetchMinutes), ref _autoFetchMinutes);
            SyncPref(nameof(StopOnRebaseConflict), ref _stopOnRebaseConflict);
            SyncPref(nameof(StageMetaPairs), ref _stageMetaPairs);
            SyncPref(nameof(GroupMetaPairs), ref _groupMetaPairs);

            string paths = string.Join("\n", _diffToolPaths);
            SyncPref(DiffToolPathsKey, ref paths);
            _diffToolPaths = new List<string>(paths.Length == 0 ? Array.Empty<string>() : paths.Split('\n'));
        }

        private static void SyncPref(string key, ref string field)
        {
            key = PrefsPrefix + key;
            if (EditorPrefs.HasKey(key))
            {
                field = EditorPrefs.GetString(key, field);
            }
            else
            {
                EditorPrefs.SetString(key, field ?? string.Empty);
            }
        }

        private static void SyncPref(string key, ref bool field)
        {
            key = PrefsPrefix + key;
            if (EditorPrefs.HasKey(key))
            {
                field = EditorPrefs.GetBool(key, field);
            }
            else
            {
                EditorPrefs.SetBool(key, field);
            }
        }

        private static void SyncPref(string key, ref int field)
        {
            key = PrefsPrefix + key;
            if (EditorPrefs.HasKey(key))
            {
                field = EditorPrefs.GetInt(key, field);
            }
            else
            {
                EditorPrefs.SetInt(key, field);
            }
        }

        /// <summary>
        /// 개인 설정을 바꾼다. UserSettings 에도 남기지만 다음 실행 때는 EditorPrefs 값이 이긴다.
        /// </summary>
        /// <returns>값이 실제로 바뀌었으면 true.</returns>
        private bool SetAndSave<T>(ref T field, T value, string prefKey)
        {
            if (SetAndSave(ref field, value) == false)
            {
                return false;
            }
            string key = PrefsPrefix + prefKey;
            switch (field)
            {
                case bool flag:
                    EditorPrefs.SetBool(key, flag);
                    break;
                case int number:
                    EditorPrefs.SetInt(key, number);
                    break;
                default:
                    EditorPrefs.SetString(key, field?.ToString() ?? string.Empty);
                    break;
            }
            return true;
        }

        /// <returns>값이 실제로 바뀌었으면 true.</returns>
        private bool SetAndSave<T>(ref T field, T value)
        {
            if (Equals(field, value))
            {
                return false;
            }
            field = value;
            Save(true);
            return true;
        }
    }
}
