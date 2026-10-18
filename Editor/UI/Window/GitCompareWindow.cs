using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 커밋 비교 창: 위는 바뀐 파일, 아래는 고른 파일의 diff. Branch Explorer 커밋 메뉴의 "비교" 항목이 연다.
    /// <see cref="_from"/> 이 비어 있으면 커밋 하나의 변경(첫 부모 ↔ 커밋), 있으면 두 커밋을 그대로 비교한다(<c>From ↔ To</c>).
    /// </summary>
    public sealed class GitCompareWindow : GitToolWindowBase
    {
        [SerializeField] private string _from;
        [SerializeField] private string _to;

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

        private static void Open(string from, string to, string title)
        {
            GitCompareWindow window = CreateInstance<GitCompareWindow>();
            window._from = from;
            window._to = to;
            window.titleContent = TitleWithIcon(title);
            window.Show();
            window.Load();
        }

        private void CreateGUI()
        {
            _services = GitServices.CreateDefault();
            CreateContent();

            VisualElement toolbar = new();
            toolbar.style.flexDirection = FlexDirection.Row;
            toolbar.style.height = 22;
            _summary = new Label();
            _summary.AddToClassList("view__toolbar-label");
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
            return string.IsNullOrEmpty(_from)
                ? GitDiffTarget.ForCommit(_to, change)
                : GitDiffTarget.ForRange(_from, _to, change);
        }

        private async void Load()
        {
            if (_files == null || string.IsNullOrEmpty(_to))
            {
                return;
            }
            _summary.text = GitText.Get("common.loading");

            List<GitFileChange> files;
            string error = null;
            if (string.IsNullOrEmpty(_from))
            {
                files = await _services.History.CommitFilesAsync(_to);
            }
            else
            {
                (files, error) = await _services.History.ChangedBetweenAsync(_from, _to);
            }

            _summary.text = error ?? (string.IsNullOrEmpty(_from)
                ? GitText.Format("compare.summaryCommit", GitDisplay.ShortRevision(_to), files.Count)
                : GitText.Format("compare.summaryRange", GitDisplay.ShortRevision(_from), GitDisplay.ShortRevision(_to), files.Count));
            _files.SetItems(files.Select(change => FileListItem.From(change)));
            _diff.SetTarget(null);
        }
    }
}
