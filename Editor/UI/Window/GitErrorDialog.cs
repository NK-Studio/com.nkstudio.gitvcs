using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 작업 실패 알림. 운영체제 대화상자(<see cref="IDialogService.Alert"/>) 대신 Git 창 위에 덮어 띄운다(<see cref="GitConfirmDialog"/> 와 같은 막·카드).
    /// 두 가지로 보인다(<see cref="ErrorDetailMode"/>): "쉬운 설명" 은 원인·해결 한 줄씩(<see cref="GitErrorAdvice"/>),
    /// "git 원문" 은 git 이 남긴 글 그대로. 제목 아래 전환 버튼으로 바꾸고, 고른 쪽을 기억한다. Enter·Esc·× 는 닫기.
    /// </summary>
    public sealed class GitErrorDialog
    {
        /// <summary>"쉬운 설명" 이 알아보지 못한 오류일 때 함께 보일 원문 줄 수.</summary>
        private const int UnknownPreviewLines = 3;

        /// <summary>복사 버튼이 "복사됨" 으로 있다가 돌아오기까지(ms).</summary>
        private const long CopiedFeedbackMs = 1500;

        private readonly TaskCompletionSource<bool> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly VisualElement _overlay;
        private readonly string _message;
        private readonly GitErrorAdvice _advice;
        private readonly Button _friendlyTab;
        private readonly Button _developerTab;
        private readonly ScrollView _body;
        private readonly Button _copyAll;

        /// <summary>
        /// 팝업을 띄우고 닫을 때까지 기다린다.
        /// 띄울 Git 창이 없으면(<see cref="GitServices.PopupHost"/> 가 없음) 운영체제 대화상자로 알린다.
        /// </summary>
        public static Task ShowAsync(GitServices services, string title, string message)
        {
            VisualElement host = services.PopupHost;
            if (host == null || host.panel == null)
            {
                services.Dialogs.Alert(title, message);
                return Task.CompletedTask;
            }
            GitErrorDialog dialog = new(host, title, message);
            return dialog._done.Task;
        }

        private GitErrorDialog(VisualElement host, string title, string message)
        {
            _message = (message ?? string.Empty).Trim();
            _advice = GitErrorAdvice.Explain(_message, GitRepository.Branch?.Name);

            _overlay = new VisualElement();
            _overlay.AddToClassList("branch-popup");
            VisualElement card = new();
            card.AddToClassList("branch-dialog");
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

            _body = new ScrollView(ScrollViewMode.Vertical);
            _body.AddToClassList("error-dialog__body");
            card.Add(_body);

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
            _copyAll.SetVisible(friendly == false);
            _body.Clear();
            if (friendly)
            {
                BuildFriendly();
            }
            else
            {
                _body.Add(Raw(_message));
            }
            _body.scrollOffset = Vector2.zero;
        }

        /// <summary>원인 한 줄(굵게)과 해결 한 줄.</summary>
        private void BuildFriendly()
        {
            Label cause = new(_advice.Cause);
            cause.AddToClassList("error-dialog__cause");
            cause.selection.isSelectable = true;
            _body.Add(cause);

            // 알아보지 못한 오류는 git 이 남긴 앞쪽 몇 줄을 함께 보인다.
            if (_advice.Kind == GitErrorKind.Unknown)
            {
                string preview = string.Join("\n", _message.Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0).Take(UnknownPreviewLines));
                _body.Add(Raw(preview));
            }

            Label fix = new(_advice.Fix);
            fix.AddToClassList("error-dialog__fix");
            fix.selection.isSelectable = true;
            _body.Add(fix);
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
            _overlay.RemoveFromHierarchy();
        }
    }
}
