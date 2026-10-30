using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// GitHub 연결 화면(<c>GitHubConnect.uxml</c>): 로그인 버튼 → 코드 표시(복사) → "다음" 으로 GitHub 페이지 열기 → 승인되면
    /// <see cref="GitHubAccount.Changed"/> 로 창이 본체로 바뀐다.
    /// 로그인 진행은 Preferences 와 같은 <see cref="GitHubDeviceLogin.Shared"/> 를 써서, 어느 쪽에서 시작해도 양쪽에 같은 상태가 보인다.
    /// 꾸밈: 뒤에 오로라와 흐르는 가지 선(<see cref="GitHubConnectBackdrop"/>), 그 위에 뒤를 흐리게 비치는 유리 카드(USS backdrop-filter),
    /// 카드와 안쪽 줄이 차례로 올라오며 나타나고, 코드는 글자마다 칸 상자로 보인다(복사하면 칸이 차례로 반짝인다).
    /// </summary>
    public sealed class GitHubConnectPanel : IDisposable
    {
        private readonly GitHubDeviceLogin _login = GitHubDeviceLogin.Shared;
        private readonly VisualElement _startStep;
        private readonly VisualElement _waitingStep;
        private readonly Label _waitingText;
        private readonly VisualElement _userCode;
        private readonly GitHubConnectBackdrop _backdrop;

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

        /// <summary>
        /// "복사됨" 을 잠깐 보였다가 원래 글자로 되돌리는 예약.
        /// </summary>
        private IVisualElementScheduledItem _copiedReset;

        /// <param name="skipped">"이번에는 건너뛰기" 를 눌렀을 때.</param>
        public GitHubConnectPanel(VisualElement root, Action skipped)
        {
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
            root.Require<Button>("skip-button").clicked += skipped;

            VisualElement connect = root.Q(className: Block);
            _backdrop = new GitHubConnectBackdrop();
            connect.Insert(0, _backdrop);
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

        private void Render()
        {
            bool isRunning = _login.IsRunning;
            bool hasCode = _login.Current == GitHubDeviceLogin.Step.WaitingForApproval;
            _startStep.SetVisible(isRunning == false);
            _waitingStep.SetVisible(isRunning);
            _waitingText.text = GitText.Get(hasCode == false ? "github.requesting" : _login.HasOpenedPage ? "github.enterCode" : "github.codeReady");
            _openPageButton.text = GitText.Get(_login.HasOpenedPage ? "github.openPage" : "github.next");
            ShowCode(hasCode ? _login.UserCode : string.Empty);
            _userCode.SetVisible(hasCode);
            // 승인을 기다리는 동안 배경 오로라 색이 천천히 돈다.
            _backdrop.Waiting = hasCode;
            // 코드를 받기 전에는 복사·페이지 열기가 쓸모없으니 취소만 남긴다.
            _copyCodeButton.SetVisible(hasCode);
            _openPageButton.SetVisible(hasCode);
            bool failed = _login.Current == GitHubDeviceLogin.Step.Failed;
            _errorText.text = failed ? GitText.Format("github.failed", _login.Error) : string.Empty;
            _errorText.SetVisible(failed);
        }
    }
}
