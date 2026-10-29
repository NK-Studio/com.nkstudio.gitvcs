using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 작업 실패 알림. 운영체제 대화상자(<see cref="IDialogService.Alert"/>) 대신 Git 창 위에 덮어 띄운다(<see cref="GitConfirmDialog"/> 와 같은 막·카드).
    /// 두 가지로 보인다(<see cref="ErrorDetailMode"/>): "설명" 은 원인·해결 한 줄씩(<see cref="GitErrorAdvice"/>),
    /// "git 원문" 은 git 이 남긴 글 그대로. 제목 아래 전환 버튼으로 바꾸고, 고른 쪽을 기억한다. Enter·Esc·× 는 닫기.
    /// </summary>
    public sealed class GitErrorDialog
    {
        /// <summary>"설명" 이 알아보지 못한 오류일 때 함께 보일 원문 줄 수.</summary>
        private const int UnknownPreviewLines = 3;

        /// <summary>복사 버튼이 "복사됨" 으로 있다가 돌아오기까지(ms).</summary>
        private const long CopiedFeedbackMs = 1500;

        private readonly TaskCompletionSource<bool> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly VisualElement _overlay;
        private readonly string _message;
        private readonly GitErrorAdvice _advice;
        private readonly Button _friendlyTab;
        private readonly Button _developerTab;
        private readonly VisualElement _stack;
        private readonly VisualElement _friendlyView;
        private readonly VisualElement _developerView;
        private readonly Button _copyAll;

        /// <summary>
        /// 팝업을 띄우고 닫을 때까지 기다린다.
        /// 띄울 곳은 <see cref="GitServices.PopupHost"/>, 없으면 <see cref="GitPopupHosts"/> 가 찾거나 Git 창을 연다.
        /// </summary>
        public static async Task ShowAsync(GitServices services, string title, string message)
        {
            // 충돌은 실패가 아니라 해결을 기다리는 중간 단계다: 빨간 오류 대신 안내와 "충돌 보기" 를 보인다.
            GitErrorAdvice advice = GitErrorAdvice.Explain(message, GitRepository.Branch?.Name);
            if (advice.Kind == GitErrorKind.Conflict)
            {
                IReadOnlyList<string> conflicted = advice.Files.Count > 0 ? advice.Files : await ConflictedPathsAsync();
                string files = conflicted.Count == 0 ? string.Empty : "\n\n" + string.Join("\n", conflicted.Take(10));
                bool show = await GitConfirmDialog.ConfirmAsync(services.PopupHost, ActionGlyphKind.Branch, GitText.Get("conflict.title"),
                    GitText.Format("conflict.message", conflicted.Count) + files, GitText.Get("conflict.show"), cancel: GitText.Get("common.close"));
                if (show)
                {
                    services.ShowConflictsRequested?.Invoke();
                }
                return;
            }
            await GitPopupHosts.ShowAsync(services.PopupHost, host => new GitErrorDialog(host, title, message)._done.Task, false);
        }

        /// <summary>
        /// 지금 충돌 중인 파일. git 출력에 "Merge conflict in" 줄이 없을 때(rebase·autostash 출력 등) 저장소에 직접 묻는다.
        /// </summary>
        private static async Task<IReadOnlyList<string>> ConflictedPathsAsync()
        {
            GitResult result = await GitRepository.RunAsync("diff", "--name-only", "--diff-filter=U");
            if (result.Success == false)
            {
                return System.Array.Empty<string>();
            }
            return result.Output.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToList();
        }

        private GitErrorDialog(VisualElement host, string title, string message)
        {
            _message = (message ?? string.Empty).Trim();
            _advice = GitErrorAdvice.Explain(_message, GitRepository.Branch?.Name);

            _overlay = new VisualElement();
            _overlay.AddToClassList("branch-popup");
            VisualElement card = new();
            card.AddToClassList("branch-dialog");
            GitShadow.Add(card, GitShadowSize.Large);
            card.AddToClassList("confirm-dialog");
            card.AddToClassList("error-dialog");
            _overlay.Add(card);

            Button close = new(Finish) { text = "×", tooltip = GitText.Get("common.close") };
            close.AddToClassList("branch-dialog__close");
            card.Add(close);

            VisualElement header = new();
            header.AddToClassList("branch-dialog__header");
            VisualElement badge = new();
            badge.AddToClassList("branch-dialog__badge");
            badge.AddToClassList("error-dialog__badge");
            Label mark = new("!");
            mark.AddToClassList("error-dialog__mark");
            badge.Add(mark);
            header.Add(badge);
            Label titleLabel = new(title);
            titleLabel.AddToClassList("branch-dialog__title");
            titleLabel.AddToClassList("confirm-dialog__title");
            header.Add(titleLabel);
            card.Add(header);

            VisualElement modes = new();
            modes.AddToClassList("error-dialog__modes");
            _friendlyTab = new Button(() => SetMode(ErrorDetailMode.Friendly)) { text = GitText.Get("error.modeFriendly") };
            _developerTab = new Button(() => SetMode(ErrorDetailMode.Developer)) { text = GitText.Get("error.modeDeveloper") };
            foreach (Button tab in new[] { _friendlyTab, _developerTab })
            {
                tab.AddToClassList("error-dialog__mode");
                modes.Add(tab);
            }
            card.Add(modes);

            // 두 보기를 같은 칸에 겹쳐 두고 높은 쪽에 맞춘다 — 바꿀 때 카드 높이가 오르내리지 않게.
            ScrollView body = new(ScrollViewMode.Vertical);
            body.AddToClassList("error-dialog__body");
            _stack = new VisualElement();
            _stack.AddToClassList("error-dialog__stack");
            _friendlyView = BuildFriendly();
            _developerView = new VisualElement();
            _developerView.Add(Raw(_message));
            foreach (VisualElement view in new[] { _friendlyView, _developerView })
            {
                view.AddToClassList("error-dialog__view");
                view.RegisterCallback<GeometryChangedEvent>(_ => FitStack());
                _stack.Add(view);
            }
            body.Add(_stack);
            card.Add(body);

            VisualElement buttons = new();
            buttons.AddToClassList("branch-dialog__buttons");
            _copyAll = new Button(() => Copy(_copyAll, _message, GitText.Get("error.copyAll"))) { text = GitText.Get("error.copyAll") };
            _copyAll.AddToClassList("branch-dialog__cancel");
            buttons.Add(_copyAll);
            Button okButton = new(Finish) { text = GitText.Get("common.ok") };
            okButton.AddToClassList("branch-dialog__create");
            buttons.Add(okButton);
            card.Add(buttons);

            _overlay.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter || evt.keyCode == KeyCode.Escape)
                {
                    Finish();
                    evt.StopPropagation();
                }
            }, TrickleDown.TrickleDown);
            _overlay.RegisterCallback<DetachFromPanelEvent>(_ => _done.TrySetResult(true));

            Show(GitVcsSettings.instance.ErrorDetailMode);
            host.Add(_overlay);
            okButton.schedule.Execute(() => okButton.Focus());
        }

        /// <summary>전환 버튼: 보이는 내용을 바꾸고 다음에도 그쪽으로 열도록 기억한다.</summary>
        private void SetMode(ErrorDetailMode mode)
        {
            GitVcsSettings.instance.ErrorDetailMode = mode;
            Show(mode);
        }

        private void Show(ErrorDetailMode mode)
        {
            bool friendly = mode == ErrorDetailMode.Friendly;
            _friendlyTab.EnableInClassList("error-dialog__mode--on", friendly);
            _developerTab.EnableInClassList("error-dialog__mode--on", friendly == false);
            _friendlyView.EnableInClassList("error-dialog__view--hidden", friendly == false);
            _developerView.EnableInClassList("error-dialog__view--hidden", friendly);
            // 버튼 줄도 자리를 지킨다(숨겨도 폭을 차지).
            _copyAll.style.visibility = friendly ? Visibility.Hidden : Visibility.Visible;
        }

        /// <summary>
        /// 겹친 칸의 높이를 두 보기 중 높은 쪽으로. 숨긴 보기도 같은 폭으로 배치되어 있어 높이를 잴 수 있다.
        /// </summary>
        private void FitStack()
        {
            float height = Mathf.Max(_friendlyView.layout.height, _developerView.layout.height);
            if (float.IsNaN(height) == false && Mathf.Approximately(_stack.resolvedStyle.minHeight.value, height) == false)
            {
                _stack.style.minHeight = height;
            }
        }

        /// <summary>원인 한 줄(굵게)과 해결 한 줄.</summary>
        private VisualElement BuildFriendly()
        {
            VisualElement view = new();
            Label cause = new(_advice.Cause);
            cause.AddToClassList("error-dialog__cause");
            cause.selection.isSelectable = true;
            view.Add(cause);

            // 알아보지 못한 오류는 git 이 남긴 앞쪽 몇 줄을 함께 보인다.
            if (_advice.Kind == GitErrorKind.Unknown)
            {
                string preview = string.Join("\n", _message.Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0).Take(UnknownPreviewLines));
                view.Add(Raw(preview));
            }

            Label fix = new(_advice.Fix);
            fix.AddToClassList("error-dialog__fix");
            fix.selection.isSelectable = true;
            view.Add(fix);
            return view;
        }

        /// <summary>git 글을 고정폭 글꼴의 테두리 칸에. 골라서 복사할 수 있다.</summary>
        private static Label Raw(string text)
        {
            Label raw = new(text);
            raw.AddToClassList("error-dialog__raw");
            raw.selection.isSelectable = true;
            return raw;
        }

        private static void Copy(Button button, string text, string label)
        {
            GUIUtility.systemCopyBuffer = text;
            button.text = GitText.Get("error.copied");
            button.schedule.Execute(() => button.text = label).StartingIn(CopiedFeedbackMs);
        }

        private void Finish()
        {
            _done.TrySetResult(true);
            GitPopupHosts.Close(_overlay, null);
        }
    }
}
