using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// GitHub 연결 화면(<c>GitHubConnect.uxml</c>): 로그인 버튼 → 브라우저에 넣을 코드 → 승인되면 <see cref="GitHubAccount.Changed"/> 로 창이 본체로 바뀐다.
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
            _copyCodeButton.clicked += () => GUIUtility.systemCopyBuffer = _login.UserCode;
            _openPageButton.clicked += () => Application.OpenURL(_login.VerificationUri);
            root.Require<Button>("cancel-button").clicked += _login.Cancel;
            root.Require<Button>("skip-button").clicked += skipped;

            _login.StateChanged += Render;
            Render();
        }

        public void Dispose()
        {
            _login.StateChanged -= Render;
        }

        private void Render()
        {
            bool isRunning = _login.IsRunning;
            bool hasCode = _login.Current == GitHubDeviceLogin.Step.WaitingForApproval;
            _startStep.SetVisible(isRunning == false);
            _waitingStep.SetVisible(isRunning);
            _waitingText.text = GitText.Get(hasCode ? "github.enterCode" : "github.requesting");
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
