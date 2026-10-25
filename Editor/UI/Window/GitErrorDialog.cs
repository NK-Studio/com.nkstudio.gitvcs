using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 작업 실패 알림. 운영체제 대화상자(<see cref="IDialogService.Alert"/>) 대신 Git 창 위에 덮어 띄운다(<see cref="GitConfirmDialog"/> 와 같은 막·카드).
    /// 카드: 빨간 경고 배지·제목, git 오류 글(골라 복사할 수 있고 길면 스크롤), 확인 하나. Enter·Esc·× 는 닫기.
    /// </summary>
    public sealed class GitErrorDialog
    {
        private readonly TaskCompletionSource<bool> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly VisualElement _overlay;

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

            // git 오류는 여러 줄(fatal: … / hint: …)이고 길 수 있어 스크롤 안에 두고, 골라서 복사할 수 있게 한다.
            ScrollView scroll = new(ScrollViewMode.Vertical);
            scroll.AddToClassList("error-dialog__scroll");
            Label messageLabel = new((message ?? string.Empty).Trim());
            messageLabel.AddToClassList("error-dialog__message");
            messageLabel.selection.isSelectable = true;
            scroll.Add(messageLabel);
            card.Add(scroll);

            VisualElement buttons = new();
            buttons.AddToClassList("branch-dialog__buttons");
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

            host.Add(_overlay);
            okButton.schedule.Execute(() => okButton.Focus());
        }

        private void Finish()
        {
            _done.TrySetResult(true);
            _overlay.RemoveFromHierarchy();
        }
    }
}
