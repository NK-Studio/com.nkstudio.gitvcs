namespace GitVersionControl
{
    /// <summary>
    /// 뷰가 생성자로 받는 의존성 묶음. 창(합성 루트)이 한 번 만들어 모든 탭에 같은 인스턴스를 나눠 준다.
    /// </summary>
    public sealed class GitServices
    {
        public readonly IGitRepositoryState Repository;
        public readonly IGitHistoryService History;
        public readonly IGitCommandService Commands;
        public readonly IGitOperationRunner Runner;
        public readonly IDialogService Dialogs;
        public readonly IAssetNavigator Assets;
        public readonly IFileIconProvider Icons;
        public readonly FileViewPreference FileView;
        public readonly IAvatarProvider Avatars;
        public readonly IGitDiffService Diff;

        /// <summary>
        /// GitKraken 레이아웃에서 브랜치 이름을 대화상자 대신 그래프 줄에서 받는 곳. 창이 탭을 만든 뒤 넣는다. null 이면 늘 대화상자로 묻는다.
        /// </summary>
        public IInlineBranchCreator InlineBranches { get; set; }

        /// <summary>
        /// 창 안에 덮어 띄우는 팝업(브랜치 만들기 등)을 얹을 요소. Git 창이 자기 본체를 넣는다 — 테마·UI 배율이 같이 적용된다.
        /// null 이면(Git 창 밖) 따로 뜨는 입력 창으로 묻는다.
        /// </summary>
        public UnityEngine.UIElements.VisualElement PopupHost { get; set; }

        public GitServices(IGitRepositoryState repository, IGitHistoryService history, IGitCommandService commands,
            IGitOperationRunner runner, IDialogService dialogs, IAssetNavigator assets, IFileIconProvider icons, FileViewPreference fileView, IAvatarProvider avatars, IGitDiffService diff)
        {
            Repository = repository;
            History = history;
            Commands = commands;
            Runner = runner;
            Dialogs = dialogs;
            Assets = assets;
            Icons = icons;
            FileView = fileView;
            Avatars = avatars;
            Diff = diff;
        }

        /// <summary>아바타는 받은 그림을 창을 다시 만들어도(언어 전환 등) 버리지 않도록 에디터 세션 동안 하나를 같이 쓴다.</summary>
        private static readonly GitHubAvatarProvider AvatarProvider = new();

        /// <summary>에디터에서 쓰는 실제 구현으로 묶는다.</summary>
        public static GitServices CreateDefault()
        {
            GitRepositoryState repository = new();
            EditorDialogService dialogs = new();
            return new GitServices(repository, new GitHistoryService(), new GitCommandService(),
                new GitOperationRunner(repository, dialogs), dialogs, new EditorAssetNavigator(), new EditorFileIconProvider(), new FileViewPreference(), AvatarProvider, new GitDiffService(dialogs));
        }
    }

    /// <summary>다른 탭으로 이동시키는 능력만 뷰에 넘긴다(창 전체를 넘기지 않는다, ISP).</summary>
    public interface ITabNavigator
    {
        /// <summary>
        /// 탭을 열고 그 뷰를 돌려준다(없으면 null). 다른 탭에 할 일을 넘길 때 쓴다(예: Branch Explorer 에서 커밋 보이기).
        /// </summary>
        T SelectTab<T>() where T : GitView;

        /// <summary>
        /// 사이드바 참조 트리의 필터에 포커스하고 로컬 구역을 펼친다(도구 막대의 브랜치 버튼).
        /// </summary>
        void FocusRefs();
    }

    /// <summary>
    /// GitKraken 처럼 브랜치 이름을 그 커밋 줄의 브랜치/태그 칸에서 바로 받고, Enter 면 만들고 전환한다(묻는 창 없음).
    /// Graph 탭이 없는 레이아웃(Unity VCS·Fork)은 받지 않아서 기존 대화상자로 묻는다.
    /// </summary>
    public interface IInlineBranchCreator
    {
        /// <param name="startPoint">새 브랜치가 가리킬 커밋 해시.</param>
        /// <param name="revealGraph">true 면 Graph 탭을 열어서 받는다(도구 막대·사이드바). false 면 Graph 탭을 보고 있을 때만 받는다(탭 안 우클릭 메뉴).</param>
        /// <returns>그래프에서 이름을 받기 시작했으면 true. false 면 부른 쪽이 대화상자로 묻는다.</returns>
        bool TryBeginCreateBranch(string startPoint, bool revealGraph);
    }

    /// <summary>
    /// 커밋 하나를 골라 보이는 탭(Graph·Branch Explorer·Changesets). 사이드바 참조 트리에서 브랜치·태그를 고르면 보고 있는 탭에 넘긴다.
    /// </summary>
    public interface ICommitRevealer
    {
        /// <returns>보일 수 있으면(또는 읽은 뒤 보일 예정이면) true. 이 탭에 없는 커밋이면 false.</returns>
        bool RevealCommit(string hash);
    }
}
