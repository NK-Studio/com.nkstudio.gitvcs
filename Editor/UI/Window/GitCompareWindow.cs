using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 커밋 비교 창: 위는 바뀐 파일, 아래는 고른 파일의 diff. Branch Explorer·Branches 탭 메뉴의 "비교" 항목이 연다.
    /// 세 가지로 연다: 커밋 하나의 변경(첫 부모 ↔ 커밋), 두 리비전 그대로(<c>From ↔ To</c>),
    /// 브랜치가 갈라진 뒤 바꾼 것(<c>From...To</c>: 공통 조상 ↔ To — Unity VC 의 "브랜치 비교").
    /// </summary>
    public sealed class GitCompareWindow : GitToolWindowBase
    {
        [SerializeField] private string _from;
        [SerializeField] private string _to;
        [SerializeField] private bool _sinceMergeBase;
        [SerializeField] private bool _workingTree;

        private GitServices _services;
        private FileBrowser _files;
        private GitDiffPanel _diff;
        private Label _summary;

        /// <summary>
        /// 커밋이 바꾼 내용(첫 부모 대비, 최초 커밋은 빈 트리 대비).
        /// </summary>
        public static void OpenCommit(string commit)
        {
            Open(null, commit, GitText.Format("compare.windowTitleCommit", GitDisplay.ShortRevision(commit)));
        }

        /// <summary>
        /// <paramref name="from"/> 의 내용 ↔ <paramref name="to"/> 의 내용.
        /// </summary>
        public static void OpenRange(string from, string to)
        {
            Open(from, to, GitText.Format("compare.windowTitleRange", GitDisplay.ShortRevision(from), GitDisplay.ShortRevision(to)));
        }

        /// <summary>
        /// <paramref name="to"/> 가 <paramref name="from"/> 에서 갈라진 뒤 바꾼 것(공통 조상 ↔ <paramref name="to"/>).
        /// </summary>
        public static void OpenSinceMergeBase(string from, string to)
        {
            Open(from, to, GitText.Format("compare.windowTitleMergeBase", GitDisplay.ShortRevision(from), GitDisplay.ShortRevision(to)), sinceMergeBase: true);
        }

        /// <summary>
        /// <paramref name="commit"/> 의 내용 ↔ 지금 작업 트리(Fork 의 "Compare to Local Changes"). 새 파일(untracked)은 git diff 처럼 빠진다.
        /// </summary>
        public static void OpenWorkingTree(string commit)
        {
            Open(commit, null, GitText.Format("compare.windowTitleWorkingTree", GitDisplay.ShortRevision(commit)), workingTree: true);
        }

        private static void Open(string from, string to, string title, bool sinceMergeBase = false, bool workingTree = false)
        {
            GitCompareWindow window = CreateInstance<GitCompareWindow>();
            window._from = from;
            window._to = to;
            window._sinceMergeBase = sinceMergeBase;
            window._workingTree = workingTree;
            window.titleContent = TitleWithIcon(title);
            window.Show();
            window.position = GitWindowPlacement.CenteredOnMain(new UnityEngine.Vector2(1200, 860));
            window.Load();
        }

        private void CreateGUI()
        {
            _services = GitServices.CreateDefault();
            CreateContent();

            VisualElement toolbar = new();
            toolbar.AddToClassList("compare__toolbar");
            toolbar.style.flexDirection = FlexDirection.Row;
            toolbar.style.height = 22;
            _summary = new Label();
            _summary.AddToClassList("view__toolbar-label");
            _summary.AddToClassList("compare__summary");
            _summary.style.flexGrow = 1;
            toolbar.Add(_summary);
            VisualElement modeSwitchHost = new();
            toolbar.Add(modeSwitchHost);
            Content.Add(toolbar);

            TwoPaneSplitView split = new(1, 360, TwoPaneSplitViewOrientation.Vertical);
            split.AddToClassList("view__split");
            Content.Add(split);

            VisualElement top = new();
            top.AddToClassList("view__pane");
            VisualElement fileHost = new();
            fileHost.style.flexGrow = 1;
            top.Add(fileHost);
            split.Add(top);

            VisualElement bottom = new();
            bottom.AddToClassList("view__pane");
            _diff = new GitDiffPanel(_services.Diff);
            _diff.Root.style.flexGrow = 1;
            bottom.Add(_diff.Root);
            split.Add(bottom);
            SplitPaneClamp.AttachAll(Content);

            _files = new FileBrowser(fileHost, modeSwitchHost, _services)
            {
                DiffTargetOf = item => TargetOf((GitFileChange)item.Source),
            };
            _files.SelectionChanged += item => _diff.SetTarget(item == null ? null : TargetOf((GitFileChange)item.Source));
            Load();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            _files?.Dispose();
        }

        private GitDiffTarget TargetOf(GitFileChange change)
        {
            if (string.IsNullOrEmpty(_from))
            {
                return GitDiffTarget.ForCommit(_to, change);
            }
            return _sinceMergeBase ? GitDiffTarget.ForMergeBase(_from, _to, change) : GitDiffTarget.ForRange(_from, _to, change);
        }

        private string SummaryOf(int fileCount)
        {
            string from = GitDisplay.ShortRevision(_from);
            string to = GitDisplay.ShortRevision(_to);
            if (_workingTree)
            {
                return GitText.Format("compare.summaryWorkingTree", from, fileCount);
            }
            if (string.IsNullOrEmpty(_from))
            {
                return GitText.Format("compare.summaryCommit", to, fileCount);
            }
            return GitText.Format(_sinceMergeBase ? "compare.summaryMergeBase" : "compare.summaryRange", from, to, fileCount);
        }

        private async void Load()
        {
            if (_files == null || (string.IsNullOrEmpty(_to) && _workingTree == false))
            {
                return;
            }
            _summary.text = GitText.Get("common.loading");

            List<GitFileChange> files;
            string error = null;
            if (_workingTree)
            {
                (files, error) = await _services.History.ChangedBetweenAsync(_from, null);
            }
            else if (string.IsNullOrEmpty(_from))
            {
                files = await _services.History.CommitFilesAsync(_to);
            }
            else if (_sinceMergeBase)
            {
                files = await _services.History.ChangedSinceMergeBaseAsync(_from, _to);
            }
            else
            {
                (files, error) = await _services.History.ChangedBetweenAsync(_from, _to);
            }

            _summary.text = error ?? SummaryOf(files.Count);
            _files.SetItems(FileListItem.FromChanges(files));
            _diff.SetTarget(null);
        }
    }
}
