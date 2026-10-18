using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// GitHub 연결 화면(<c>GitHubConnect.uxml</c>): 로그인 버튼 → 코드 표시(복사) → "다음" 으로 GitHub 페이지 열기 → 승인되면
    /// <see cref="GitHubAccount.Changed"/> 로 창이 본체로 바뀐다.
    /// 로그인 진행은 Preferences 와 같은 <see cref="GitHubDeviceLogin.Shared"/> 를 써서, 어느 쪽에서 시작해도 양쪽에 같은 상태가 보인다.
    /// </summary>
    public sealed class GitHubConnectPanel : IDisposable
    {
        private readonly GitHubDeviceLogin _login = GitHubDeviceLogin.Shared;
        private readonly VisualElement _startStep;
        private readonly VisualElement _waitingStep;
        private readonly Label _waitingText;
        private readonly Label _userCode;
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
            _userCode = root.Require<Label>("user-code");
            _copyCodeButton = root.Require<Button>("copy-code-button");
            _openPageButton = root.Require<Button>("open-page-button");
            _errorText = root.Require<Label>("error-text");

            root.Require<Button>("sign-in-button").clicked += _login.Start;
            _copyCodeButton.clicked += CopyCode;
            _openPageButton.clicked += _login.OpenVerificationPage;
            root.Require<Button>("cancel-button").clicked += _login.Cancel;
            root.Require<Button>("skip-button").clicked += skipped;

            _login.StateChanged += Render;
            Render();
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
        }

        private void Render()
        {
            bool isRunning = _login.IsRunning;
            bool hasCode = _login.Current == GitHubDeviceLogin.Step.WaitingForApproval;
            _startStep.SetVisible(isRunning == false);
            _waitingStep.SetVisible(isRunning);
            _waitingText.text = GitText.Get(hasCode == false ? "github.requesting" : _login.HasOpenedPage ? "github.enterCode" : "github.codeReady");
            _openPageButton.text = GitText.Get(_login.HasOpenedPage ? "github.openPage" : "github.next");
            _userCode.text = hasCode ? _login.UserCode : string.Empty;
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
