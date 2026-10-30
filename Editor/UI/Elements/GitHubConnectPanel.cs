using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// GitHub 연결 화면(<c>GitHubConnect.uxml</c>): 로그인 버튼 → 코드 표시(복사) → "다음" 으로 GitHub 페이지 열기 → 승인되면
    /// <see cref="GitHubAccount.Changed"/> 로 창이 본체로 바뀐다.
    /// 로그인 진행은 Preferences 와 같은 <see cref="GitHubDeviceLogin.Shared"/> 를 써서, 어느 쪽에서 시작해도 양쪽에 같은 상태가 보인다.
    /// 카드와 안쪽 줄이 차례로 올라오며 나타나고, 코드는 글자마다 칸 상자로 보인다(복사하면 칸이 차례로 반짝인다).
    /// 배경 오로라·흐름·유리(blur 필터)는 매 프레임 다시 그려 무겁고 보기에도 어수선해 쓰지 않는다.
    /// 이 프로젝트가 아직 저장소가 아니면 창이 먼저 <see cref="ShowInitStep"/> 로 같은 카드에서 git init 을 받고,
    /// 만든 뒤 로그인하지 않았으면 <see cref="ShowLoginStep"/> 로 GitHub 연결을 선택으로 권한다(로그인은 PC 마다 한 번, init 은 프로젝트마다 한 번).
    /// </summary>
    public sealed class GitHubConnectPanel : IDisposable
    {
        private readonly GitHubDeviceLogin _login = GitHubDeviceLogin.Shared;
        private readonly VisualElement _startStep;
        private readonly VisualElement _waitingStep;
        private readonly Label _waitingText;
        private readonly VisualElement _userCode;

        /// <summary>지금 칸 상자로 보이는 코드(바뀔 때만 다시 만든다).</summary>
        private string _shownCode;

        private const string Block = "github-connect";

        /// <summary>들어오는 연출: 카드가 먼저, 안쪽 줄은 이 간격(ms)으로 차례로.</summary>
        private const long RevealStartMs = 60;
        private const long RevealStepMs = 70;

        /// <summary>복사했을 때 코드 칸이 차례로 반짝이는 간격·반짝임이 남는 시간(ms).</summary>
        private const long FlashStepMs = 45;
        private const long FlashHoldMs = 240;
        private readonly Button _copyCodeButton;
        private readonly Button _openPageButton;
        private readonly Label _errorText;
        private readonly Label _title;
        private readonly Label _description;
        private readonly VisualElement _initStep;
        private readonly Label _initFolder;
        private readonly Label _initGitignore;
        private readonly Button _initButton;
        private readonly Button _skipButton;
        private readonly Action<bool> _finished;

        /// <summary>저장소 만들기 단계를 보이는 중(로그인 단계는 감춘다).</summary>
        private bool _initMode;

        /// <summary>
        /// "복사됨" 을 잠깐 보였다가 원래 글자로 되돌리는 예약.
        /// </summary>
        private IVisualElementScheduledItem _copiedReset;

        /// <param name="skipped">"이번에는 건너뛰기" 를 눌렀을 때.</param>
        /// <param name="finished">저장소 만들기 단계를 마쳤을 때. 만들었으면 true, "나중에" 면 false.</param>
        public GitHubConnectPanel(VisualElement root, Action skipped, Action<bool> finished)
        {
            _finished = finished;
            _title = root.Require<Label>("title");
            _description = root.Require<Label>("description");
            _initStep = root.Require<VisualElement>("init-step");
            _initFolder = root.Require<Label>("init-folder");
            _initGitignore = root.Require<Label>("init-gitignore");
            _initButton = root.Require<Button>("init-button");
            _skipButton = root.Require<Button>("skip-button");
            _initButton.clicked += () => _ = CreateRepositoryAsync();
            root.Require<Button>("init-later-button").clicked += () => _finished?.Invoke(false);
            _initStep.SetVisible(false);
            _startStep = root.Require<VisualElement>("start-step");
            _waitingStep = root.Require<VisualElement>("waiting-step");
            _waitingText = root.Require<Label>("waiting-text");
            _userCode = root.Require<VisualElement>("user-code");
            _copyCodeButton = root.Require<Button>("copy-code-button");
            _openPageButton = root.Require<Button>("open-page-button");
            _errorText = root.Require<Label>("error-text");

            root.Require<Button>("sign-in-button").clicked += _login.Start;
            _copyCodeButton.clicked += CopyCode;
            _openPageButton.clicked += _login.OpenVerificationPage;
            root.Require<Button>("cancel-button").clicked += _login.Cancel;
            _skipButton.clicked += skipped;

            VisualElement card = root.Q(className: Block + "__card");
            GitShadow.Add(card, GitShadowSize.Large);
            Reveal(card, root.Q(className: Block + "__title"), root.Q(className: Block + "__text"), _startStep, _waitingStep,
                root.Require<Button>("skip-button"));

            _login.StateChanged += Render;
            Render();
        }

        /// <summary>
        /// 카드와 안쪽 줄을 처음엔 숨겨 두고(조금 아래·투명) 차례로 제자리로 올리며 나타낸다(USS transition).
        /// 첫 배치가 끝난 뒤에 시작해야 transition 이 먹는다.
        /// </summary>
        private static void Reveal(params VisualElement[] elements)
        {
            foreach (VisualElement element in elements)
            {
                element.AddToClassList(Block + "__reveal");
            }
            elements[0].schedule.Execute(() =>
            {
                for (int i = 0; i < elements.Length; i++)
                {
                    VisualElement element = elements[i];
                    element.schedule.Execute(() => element.AddToClassList(Block + "__reveal--in")).StartingIn(i * RevealStepMs);
                }
            }).StartingIn(RevealStartMs);
        }

        public void Dispose()
        {
            _login.StateChanged -= Render;
        }

        /// <summary>
        /// 복사했다는 표시를 1.5초 보인다.
        /// </summary>
        private void CopyCode()
        {
            _login.CopyCode();
            _copyCodeButton.text = GitText.Get("github.copied");
            _copiedReset?.Pause();
            _copiedReset = _copyCodeButton.schedule.Execute(() => _copyCodeButton.text = GitText.Get("github.copyCode")).StartingIn(1500);
            FlashCode();
        }

        /// <summary>복사했다는 것을 코드 칸이 왼쪽부터 차례로 한 번 반짝여 보인다.</summary>
        private void FlashCode()
        {
            int index = 0;
            foreach (VisualElement cell in _userCode.Children())
            {
                if (cell.ClassListContains(Block + "__code-cell") == false)
                {
                    continue;
                }
                long delay = index++ * FlashStepMs;
                cell.schedule.Execute(() => cell.AddToClassList(Block + "__code-cell--flash")).StartingIn(delay);
                cell.schedule.Execute(() => cell.RemoveFromClassList(Block + "__code-cell--flash")).StartingIn(delay + FlashHoldMs);
            }
        }

        /// <summary>코드를 글자마다 칸 상자로(가운데 '-' 는 상자 없이 줄표로).</summary>
        private void ShowCode(string code)
        {
            if (code == _shownCode)
            {
                return;
            }
            _shownCode = code;
            _userCode.Clear();
            foreach (char c in code ?? string.Empty)
            {
                Label cell = new(c.ToString()) { pickingMode = PickingMode.Ignore };
                cell.AddToClassList(c == '-' ? Block + "__code-dash" : Block + "__code-cell");
                _userCode.Add(cell);
            }
        }

        /// <summary>
        /// 카드를 "Git 저장소 만들기" 단계로 바꾼다: 제목·설명을 바꾸고 로그인 단계를 감춘 뒤, 만들 폴더와 .gitignore 안내, 만들기 버튼을 보인다.
        /// 새 단계는 아래에서 살짝 올라오며 나타난다(처음 카드와 같은 연출).
        /// </summary>
        public void ShowInitStep()
        {
            _initMode = true;
            string root = GitRepository.ProjectRoot ?? string.Empty;
            _title.text = GitText.Get("connect.initTitle");
            _description.text = GitText.Get("connect.initDescription");
            _initFolder.text = root;
            _initGitignore.text = GitText.Get(GitInit.HasGitignore(root) ? "init.keepGitignore" : "init.addGitignore").Trim();
            _initButton.text = GitText.Get("connect.initButton");
            _initButton.SetEnabled(true);
            Render();
            RevealAgain(_description, _initStep);
        }

        /// <summary>저장소 만들기 단계에서 저장소를 만들었는지.</summary>
        public bool CreatedRepository { get; private set; }

        /// <summary>
        /// 저장소를 만든 뒤 같은 카드를 GitHub 연결 단계로 바꾼다. 연결은 선택이라 건너뛰기 버튼을 "나중에 연결" 로 보인다.
        /// </summary>
        public void ShowLoginStep()
        {
            _initMode = false;
            _title.text = GitText.Get("connect.initDoneTitle");
            _description.text = GitText.Get("connect.afterInitDescription");
            _skipButton.text = GitText.Get("connect.later");
            ShowError(null);
            Render();
            RevealAgain(_description, _startStep, _skipButton);
        }

        /// <summary>단계를 바꿀 때 새로 보이는 줄을 처음 카드처럼 아래에서 살짝 올라오며 차례로 나타낸다.</summary>
        private void RevealAgain(params VisualElement[] elements)
        {
            foreach (VisualElement element in elements)
            {
                element.RemoveFromClassList(Block + "__reveal--in");
                element.AddToClassList(Block + "__reveal");
            }
            elements[0].schedule.Execute(() =>
            {
                for (int i = 0; i < elements.Length; i++)
                {
                    VisualElement element = elements[i];
                    element.schedule.Execute(() => element.AddToClassList(Block + "__reveal--in")).StartingIn(i * RevealStepMs);
                }
            }).StartingIn(16);
        }

        private async System.Threading.Tasks.Task CreateRepositoryAsync()
        {
            _initButton.SetEnabled(false);
            _initButton.text = GitText.Get("connect.initCreating");
            ShowError(null);
            (GitResult result, string gitignoreError) = await GitInit.CreateRepositoryAsync(GitRepository.ProjectRoot);
            if (result.Success == false)
            {
                _initButton.SetEnabled(true);
                _initButton.text = GitText.Get("connect.initButton");
                ShowError(GitText.Get("init.failed") + "\n" + result.Message);
                return;
            }
            GitRepository.RequestRefresh(immediate: true);
            if (gitignoreError != null)
            {
                Debug.LogWarning(GitText.Format("init.gitignoreFailed", gitignoreError));
            }
            CreatedRepository = true;
            _finished?.Invoke(true);
        }

        private void ShowError(string message)
        {
            _errorText.text = message ?? string.Empty;
            _errorText.SetVisible(string.IsNullOrEmpty(message) == false);
        }

        private void Render()
        {
            if (_initMode)
            {
                _startStep.SetVisible(false);
                _waitingStep.SetVisible(false);
                _skipButton.SetVisible(false);
                _initStep.SetVisible(true);
                return;
            }
            // 저장소를 만든 뒤 로그인 단계로 돌아온 경우(ShowLoginStep), 저장소 만들기 줄을 감추고 건너뛰기를 다시 보인다.
            _initStep.SetVisible(false);
            _skipButton.SetVisible(true);
            bool isRunning = _login.IsRunning;
            bool hasCode = _login.Current == GitHubDeviceLogin.Step.WaitingForApproval;
            _startStep.SetVisible(isRunning == false);
            _waitingStep.SetVisible(isRunning);
            _waitingText.text = GitText.Get(hasCode == false ? "github.requesting" : _login.HasOpenedPage ? "github.enterCode" : "github.codeReady");
            _openPageButton.text = GitText.Get(_login.HasOpenedPage ? "github.openPage" : "github.next");
            ShowCode(hasCode ? _login.UserCode : string.Empty);
            _userCode.SetVisible(hasCode);
            // 코드를 받기 전에는 복사·페이지 열기가 쓸모없으니 취소만 남긴다.
            _copyCodeButton.SetVisible(hasCode);
            _openPageButton.SetVisible(hasCode);
            bool failed = _login.Current == GitHubDeviceLogin.Step.Failed;
            _errorText.text = failed ? GitText.Format("github.failed", _login.Error) : string.Empty;
            _errorText.SetVisible(failed);
        }
    }
}
