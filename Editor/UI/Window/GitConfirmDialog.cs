using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 확인 팝업. 운영체제 대화상자(<see cref="IDialogService.Confirm"/>) 대신 Git 창 위에 덮어 띄운다(<see cref="GitBranchDialog"/> 와 같은 막·카드).
    /// 같은 패널이라 테마·UI 배율·글꼴이 Git 창과 같다. 카드: 아이콘·제목, 안내 글, 확인(강조색)·취소.
    /// Enter 는 확인, Esc·× 는 취소. 막을 눌러도 닫지 않는다(실수로 고른 것으로 치지 않게).
    /// </summary>
    public sealed class GitConfirmDialog
    {
        private readonly TaskCompletionSource<bool> _done = new();
        private readonly VisualElement _overlay;

        /// <summary>
        /// 팝업을 띄우고 고를 때까지 기다린다. 확인이면 true.
        /// 띄울 Git 창이 없으면(<see cref="GitServices.PopupHost"/> 가 없음) 운영체제 대화상자로 묻는다.
        /// </summary>
        /// <param name="cancel">null 이면 현재 언어의 "취소".</param>
        public static Task<bool> ShowAsync(GitServices services, ActionGlyphKind icon, string title, string message, string ok, string cancel = null)
        {
            VisualElement host = services.PopupHost;
            if (host == null || host.panel == null)
            {
                return Task.FromResult(services.Dialogs.Confirm(title, message, ok, cancel));
            }
            GitConfirmDialog dialog = new(host, icon, title, message, ok, cancel ?? GitText.Get("common.cancel"));
            return dialog._done.Task;
        }

        private GitConfirmDialog(VisualElement host, ActionGlyphKind icon, string title, string message, string ok, string cancel)
        {
            _overlay = new VisualElement();
            _overlay.AddToClassList("branch-popup");
            VisualElement card = new();
            card.AddToClassList("branch-dialog");
            card.AddToClassList("confirm-dialog");
            _overlay.Add(card);

            Button close = new(() => Finish(false)) { text = "×", tooltip = cancel };
            close.AddToClassList("branch-dialog__close");
            card.Add(close);

            VisualElement header = new();
            header.AddToClassList("branch-dialog__header");
            VisualElement badge = new();
            badge.AddToClassList("branch-dialog__badge");
            ActionGlyph badgeGlyph = new(icon);
            badgeGlyph.AddToClassList("branch-dialog__badge-glyph");
            badge.Add(badgeGlyph);
            header.Add(badge);
            Label titleLabel = new(title);
            titleLabel.AddToClassList("branch-dialog__title");
            titleLabel.AddToClassList("confirm-dialog__title");
            header.Add(titleLabel);
            card.Add(header);

            Label messageLabel = new(message);
            messageLabel.AddToClassList("confirm-dialog__message");
            card.Add(messageLabel);

            VisualElement buttons = new();
            buttons.AddToClassList("branch-dialog__buttons");
            Button okButton = new(() => Finish(true)) { text = ok };
            okButton.AddToClassList("branch-dialog__create");
            Button cancelButton = new(() => Finish(false)) { text = cancel };
            cancelButton.AddToClassList("branch-dialog__cancel");
            buttons.Add(okButton);
            buttons.Add(cancelButton);
            card.Add(buttons);

            // Enter = 확인, Esc = 취소. 팝업 안 어디에 포커스가 있든 받고, 밑의 창으로는 넘기지 않는다.
            _overlay.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    Finish(true);
                    evt.StopPropagation();
                }
                else if (evt.keyCode == KeyCode.Escape)
                {
                    Finish(false);
                    evt.StopPropagation();
                }
            }, TrickleDown.TrickleDown);
            // 창이 닫히거나 다시 만들어지면(도메인 리로드·언어 전환) 취소로 풀어 준다.
            _overlay.RegisterCallback<DetachFromPanelEvent>(_ => _done.TrySetResult(false));

            host.Add(_overlay);
            // 키를 바로 받도록 확인 버튼에 포커스한다.
            okButton.schedule.Execute(() => okButton.Focus());
        }

        private void Finish(bool confirmed)
        {
            _done.TrySetResult(confirmed);
            _overlay.RemoveFromHierarchy();
        }
    }
}
