using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 파일 히스토리: 위는 이 파일을 바꾼 커밋들(이름 변경을 따라간다), 아래는 고른 커밋에서 이 파일이 바뀐 내용.
    /// </summary>
    public sealed class GitFileHistoryWindow : GitToolWindowBase
    {
        /// <summary>
        /// 읽을 커밋 수의 상한. 오래 산 파일도 대개 이 안에 들고, 넘으면 맨 아래에 잘렸다고 적는다.
        /// </summary>
        private const int MaxCommits = 500;

        [SerializeField] private string _repoPath;

        private IGitDiffService _diffService;
        private CommitList _commits;
        private GitDiffPanel _diff;
        private Label _summary;
        private Dictionary<string, string> _pathByCommit = new();

        public static void Open(string repoPath, GitServices services)
        {
            GitFileHistoryWindow window = CreateInstance<GitFileHistoryWindow>();
            window._repoPath = repoPath;
            window._diffService = services.Diff;
            window.titleContent = TitleWithIcon(GitText.Format("history.windowTitle", System.IO.Path.GetFileName(repoPath)));
            window.Show();
            window.Load();
        }

        private void CreateGUI()
        {
            _diffService ??= new GitDiffService(new EditorDialogService());
            CreateContent();

            _summary = new Label();
            _summary.AddToClassList("view__toolbar-label");
            _summary.style.height = 22;
            Content.Add(_summary);

            TwoPaneSplitView split = new(1, 320, TwoPaneSplitViewOrientation.Vertical);
            split.AddToClassList("view__split");
            Content.Add(split);

            VisualElement top = new();
            top.AddToClassList("view__pane");
            VisualElement header = new();
            header.AddToClassList("list-header");
            foreach ((string key, string column) in new[] { ("column.hash", "column--hash"), ("column.message", "column--grow-3"), ("column.author", "column--author"), ("column.date", "column--date") })
            {
                Label cell = new(GitText.Get(key));
                cell.AddToClassList("list-header__cell");
                cell.AddToClassList(column);
                header.Add(cell);
            }
            top.Add(header);
            ListView list = new();
            list.AddToClassList("list");
            top.Add(list);
            split.Add(top);

            VisualElement bottom = new();
            bottom.AddToClassList("view__pane");
            _diff = new GitDiffPanel(_diffService);
            _diff.Root.style.flexGrow = 1;
            bottom.Add(_diff.Root);
            split.Add(bottom);
            SplitPaneClamp.AttachAll(Content);

            _commits = new CommitList(list, new EditorAssetNavigator());
            _commits.SelectionChanged += ShowCommit;
            Load();
        }

        private async void Load()
        {
            if (_commits == null || string.IsNullOrEmpty(_repoPath))
            {
                return;
            }
            _summary.text = GitText.Get("common.loading");
            (List<GitCommitInfo> commits, string error) = await GitFileOperations.FileLogAsync(_repoPath, MaxCommits);
            _pathByCommit = await GitFileOperations.FollowPathsAsync(_repoPath, MaxCommits);
            _summary.text = error ?? GitText.Format("history.summary", GitDisplay.Nfc(_repoPath), commits.Count)
                + (commits.Count >= MaxCommits ? GitText.Format("explorer.capped", MaxCommits) : string.Empty);
            _commits.SetItems(commits);
            if (commits.Count > 0)
            {
                _commits.Select(commits[0]);
            }
            else
            {
                _diff.SetTarget(null);
            }
        }

        private void ShowCommit(GitCommitInfo commit)
        {
            if (commit == null)
            {
                return;
            }
            string path = _pathByCommit.TryGetValue(commit.Hash, out string followed) ? followed : _repoPath;
            _diff.SetTarget(GitDiffTarget.ForCommit(commit.Hash, new GitFileChange { Status = 'M', RepoPath = path }));
        }
    }
}
