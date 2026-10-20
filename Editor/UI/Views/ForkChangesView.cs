using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// Fork 형태의 로컬 변경 탭: 가운데 Unstaged·Staged 목록, 오른쪽 위 고른 파일의 diff, 오른쪽 아래 커밋 입력란.
    /// 체크박스 대신 git 인덱스(Stage·Unstage)로 커밋할 파일을 정한다.
    /// </summary>
    public sealed class ForkChangesView : GitView
    {
        private readonly StagingLists _staging;
        private readonly GitDiffPanel _diff;
        private readonly CommitBox _commit;

        public override string Title => GitText.Get("tab.localChanges");
        public override string Badge => Services.Repository.Entries.Count > 0 ? Services.Repository.Entries.Count.ToString() : null;

        public ForkChangesView(GitServices services, StagingState state) : base(services, "ForkChangesView")
        {
            // Fork 처럼 머리 줄 버튼은 고른 파일만 옮기고, Shift 를 누르는 동안만 "Stage All"·"Unstage All" 이 된다.
            _staging = new StagingLists(Root.Require<VisualElement>("staging-host"), services) { ShiftForAll = true };
            _diff = new GitDiffPanel(services.Diff);
            _diff.Root.style.flexGrow = 1;
            _diff.ShowReloadButton(false);
            _diff.EnableMergeEditor(services);
            Root.Require<VisualElement>("diff-host").Add(_diff.Root);
            _commit = new CommitBox(Root.Require<VisualElement>("commit-host"), services, state, showPush: false);

            _staging.FileSelected += _diff.SetTarget;
            // 누르자마자 옮겨 보인 목록(짐작)에 맞춰 커밋 상자의 stage 개수도 바로 바꾼다.
            _staging.EntriesShown += () => _commit.SetStagedCount(_staging.StagedCount);
        }

        public override void OnRepositoryChanged()
        {
            // 보이지 않으면 Unstaged·Staged 목록을 다시 만들지 않는다 — 탭을 열 때 만든다. 탭 옆 개수는 바로 바꾼다.
            if (IsActive == false)
            {
                NotifyBadgeChanged();
                return;
            }
            RefreshWorkingTree();
        }

        protected override void OnActivated()
        {
            RefreshWorkingTree();
        }

        private void RefreshWorkingTree()
        {
            _staging.SetEntries(Services.Repository.Entries);
            _commit.SetStagedCount(_staging.StagedCount);
            // 고른 파일이 그대로 남아 있으면 그 diff 를 다시 읽는다(저장으로 내용이 바뀌었을 수 있다). 사라졌으면 StagingLists 가 null 을 알린다.
            if (_diff.Target != null)
            {
                _diff.Reload();
            }
            NotifyBadgeChanged();
        }

        public override void Reload()
        {
            if (_diff.Target != null)
            {
                _diff.Reload();
            }
        }

        public override void OnBusyChanged(bool isBusy)
        {
            _staging.OnBusyChanged();
            _commit.Render();
        }

        public override void Dispose()
        {
            _staging.Dispose();
        }
    }
}
