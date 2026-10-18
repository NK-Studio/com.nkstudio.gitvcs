using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 창 위쪽 막대(<c>.git-toolbar</c>): 현재 브랜치(누르면 Branches 탭), 진행 상태, Fetch·Pull·Push·새로고침.
    /// </summary>
    public sealed class GitToolbar
    {
        private readonly GitServices _services;
        private readonly ToolbarButton _branchButton;
        private readonly Label _status;
        private readonly ToolbarButton _fetchButton;
        private readonly ToolbarButton _pullButton;
        private readonly ToolbarButton _pushButton;
        private readonly ToolbarButton _refreshButton;

        public GitToolbar(VisualElement root, GitServices services, ITabNavigator navigator)
        {
            _services = services;
            _branchButton = root.Require<ToolbarButton>("branch-button");
            _status = root.Require<Label>("status-label");
            _fetchButton = root.Require<ToolbarButton>("fetch-button");
            _pullButton = root.Require<ToolbarButton>("pull-button");
            _pushButton = root.Require<ToolbarButton>("push-button");
            _refreshButton = root.Require<ToolbarButton>("refresh-button");
            ToolbarIcons.SetRefreshIcon(_refreshButton);

            _branchButton.clicked += navigator.SelectTab<BranchesView>;
            _fetchButton.clicked += () => _ = services.Runner.RunAsync("Fetch", services.Commands.FetchAsync, refreshAssets: false);
            _pullButton.clicked += () => _ = services.Runner.RunAsync("Pull", services.Commands.PullAsync, refreshAssets: true);
            _pushButton.clicked += () =>
            {
                GitBranchInfo branch = services.Repository.Branch;
                _ = services.Runner.RunAsync("Push", () => services.Commands.PushAsync(branch), refreshAssets: false);
            };
            _refreshButton.clicked += () => services.Repository.RequestRefresh(immediate: true);
        }

        public void Render()
        {
            _branchButton.text = GitDisplay.FormatBranch(_services.Repository.Branch);
            _status.text = _services.Runner.StatusText;

            bool idle = _services.Runner.IsBusy == false;
            _fetchButton.SetEnabled(idle);
            _pullButton.SetEnabled(idle);
            _pushButton.SetEnabled(idle);
            _refreshButton.SetEnabled(_services.Repository.IsRefreshing == false);
        }
    }
}
