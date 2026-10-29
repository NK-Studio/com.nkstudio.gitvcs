using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 확인 팝업. 운영체제 대화상자(<see cref="IDialogService.Confirm"/>) 대신 Git 창 위에 덮어 띄운다(<see cref="GitBranchDialog"/> 와 같은 막·카드).
    /// 같은 패널이라 테마·UI 배율·글꼴이 Git 창과 같다. 카드: 아이콘·제목, 안내 글, 확인(강조색)·취소.
    /// 세 갈래(<see cref="ChooseAsync"/>: 확인·다른 쪽·취소)와 알림(<see cref="AlertAsync"/>: 확인 하나)도 같은 카드로 묻는다.
    /// Enter 는 확인, Esc·× 는 취소. 막을 눌러도 닫지 않는다(실수로 고른 것으로 치지 않게).
    /// </summary>
    public sealed class GitConfirmDialog
    {
        /// <summary>
        /// 고른 것: <see cref="IDialogService.Choose"/> 와 같게 0 = 확인, 1 = 취소, 2 = 다른 쪽.
        /// </summary>
        private const int ResultOk = 0;
        private const int ResultCancel = 1;
        private const int ResultAlt = 2;

        /// <summary>
        /// 기다리던 쪽은 다음 틀에 이어 간다 — 바로 이어서 운영체제 대화상자(오류 알림 등)를 띄우면 닫히기 전의 팝업이 그 뒤에 남아 보인다.
        /// </summary>
        private readonly TaskCompletionSource<int> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly VisualElement _overlay;

        /// <summary>
        /// 팝업에 넣을 버튼들. 없는 버튼은 null(글자 대신).
        /// </summary>
        private sealed class Buttons
        {
            public string Ok;
            public bool OkDanger;
            public string Alt;
            public bool AltDanger;
            public string Cancel;
        }

        /// <summary>
        /// 팝업을 띄우고 고를 때까지 기다린다. 확인이면 true.
        /// 띄울 Git 창이 없으면(<see cref="GitServices.PopupHost"/> 가 없음) 운영체제 대화상자로 묻는다.
        /// </summary>
        /// <param name="cancel">null 이면 현재 언어의 "취소".</param>
        public static Task<bool> ShowAsync(GitServices services, ActionGlyphKind icon, string title, string message, string ok, string cancel = null)
        {
            return ConfirmAsync(services.PopupHost, services.Dialogs, icon, title, message, ok, okDanger: false, cancel);
        }

        /// <summary>
        /// Fork(macOS 경고창) 모양으로 묻는다: 좁은 둥근 카드, 위에 아이콘, 굵은 질문, 짧은 안내, 아래에 같은 폭의 취소(왼쪽)·확인(오른쪽, 강조색).
        /// × 는 없다(Esc 로 취소).
        /// </summary>
        /// <param name="cancel">null 이면 현재 언어의 "취소".</param>
        public static Task<bool> ShowAlertAsync(GitServices services, ActionGlyphKind icon, string title, string message, string ok, string cancel = null)
        {
            VisualElement host = services.PopupHost;
            if (CanShowOn(host) == false)
            {
                return Task.FromResult(services.Dialogs.Confirm(title, message, ok, cancel));
            }
            GitConfirmDialog dialog = new(host, icon, title, message, new Buttons { Ok = ok, Cancel = cancel ?? GitText.Get("common.cancel") }, alert: true);
            return IsOkAsync(dialog._done.Task);
        }

        /// <summary>
        /// <paramref name="host"/> 위에 확인·취소 팝업을 띄운다. 띄울 수 없으면 <paramref name="fallback"/>(운영체제 대화상자)으로 묻는다.
        /// </summary>
        /// <param name="okDanger">확인이 되돌릴 수 없는 일(버리기·덮어쓰기)이면 true. 강조색 대신 빨강.</param>
        /// <param name="cancel">null 이면 현재 언어의 "취소".</param>
        public static Task<bool> ConfirmAsync(VisualElement host, IDialogService fallback, ActionGlyphKind icon, string title, string message, string ok, bool okDanger = false, string cancel = null)
        {
            if (CanShowOn(host) == false)
            {
                return Task.FromResult(fallback.Confirm(title, message, ok, cancel));
            }
            GitConfirmDialog dialog = new(host, icon, title, message, new Buttons { Ok = ok, OkDanger = okDanger, Cancel = cancel ?? GitText.Get("common.cancel") }, alert: false);
            return IsOkAsync(dialog._done.Task);
        }

        /// <summary>
        /// 세 갈래로 묻는다: [확인][다른 쪽][취소]. <see cref="IDialogService.Choose"/> 와 같게 0 = 확인, 1 = 취소, 2 = 다른 쪽.
        /// 띄울 수 없으면 <paramref name="fallback"/> 으로 묻는다.
        /// </summary>
        /// <param name="okDanger">확인이 되돌릴 수 없는 일이면 true(빨강).</param>
        /// <param name="altDanger">다른 쪽이 되돌릴 수 없는 일(버리기)이면 true(빨강).</param>
        /// <param name="cancel">null 이면 현재 언어의 "취소".</param>
        public static Task<int> ChooseAsync(VisualElement host, IDialogService fallback, ActionGlyphKind icon, string title, string message, string ok, string alt,
            bool okDanger = false, bool altDanger = false, string cancel = null)
        {
            if (CanShowOn(host) == false)
            {
                return Task.FromResult(fallback.Choose(title, message, ok, alt, cancel));
            }
            GitConfirmDialog dialog = new(host, icon, title, message,
                new Buttons { Ok = ok, OkDanger = okDanger, Alt = alt, AltDanger = altDanger, Cancel = cancel ?? GitText.Get("common.cancel") }, alert: false);
            return dialog._done.Task;
        }

        /// <summary>
        /// 알림: 안내와 [확인] 하나. 띄울 수 없으면 <paramref name="fallback"/> 으로 알린다.
        /// </summary>
        public static Task AlertAsync(VisualElement host, IDialogService fallback, ActionGlyphKind icon, string title, string message)
        {
            if (CanShowOn(host) == false)
            {
                fallback.Alert(title, message);
                return Task.CompletedTask;
            }
            GitConfirmDialog dialog = new(host, icon, title, message, new Buttons { Ok = GitText.Get("common.ok") }, alert: false);
            return dialog._done.Task;
        }

        private static bool CanShowOn(VisualElement host)
        {
            return host != null && host.panel != null;
        }

        private static async Task<bool> IsOkAsync(Task<int> result)
        {
            return await result == ResultOk;
        }

        private GitConfirmDialog(VisualElement host, ActionGlyphKind icon, string title, string message, Buttons choices, bool alert)
        {
            _overlay = new VisualElement();
            _overlay.AddToClassList("branch-popup");
            VisualElement card = new();
            card.AddToClassList("branch-dialog");
            GitShadow.Add(card, GitShadowSize.Large);
            card.AddToClassList("confirm-dialog");
            card.EnableInClassList("confirm-dialog--alert", alert);
            _overlay.Add(card);

            if (alert == false)
            {
                Button close = new(() => Finish(ResultCancel)) { text = "×", tooltip = choices.Cancel ?? GitText.Get("common.close") };
                close.AddToClassList("branch-dialog__close");
                card.Add(close);
            }

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
            Button okButton = new(() => Finish(ResultOk)) { text = choices.Ok };
            okButton.AddToClassList("branch-dialog__create");
            okButton.EnableInClassList("confirm-dialog__button--danger", choices.OkDanger);
            Button altButton = null;
            if (choices.Alt != null)
            {
                // 다른 쪽은 취소와 같은 무채색 버튼(버리기면 빨강 테두리).
                altButton = new Button(() => Finish(ResultAlt)) { text = choices.Alt };
                altButton.AddToClassList("branch-dialog__cancel");
                altButton.EnableInClassList("confirm-dialog__button--danger-outline", choices.AltDanger);
            }
            Button cancelButton = null;
            if (choices.Cancel != null)
            {
                cancelButton = new Button(() => Finish(ResultCancel)) { text = choices.Cancel };
                cancelButton.AddToClassList("branch-dialog__cancel");
            }
            // 경고창은 macOS 처럼 취소가 왼쪽, 확인이 오른쪽.
            if (alert)
            {
                AddIfAny(buttons, cancelButton);
                AddIfAny(buttons, altButton);
                buttons.Add(okButton);
            }
            else
            {
                buttons.Add(okButton);
                AddIfAny(buttons, altButton);
                AddIfAny(buttons, cancelButton);
            }
            card.Add(buttons);

            // Enter = 확인, Esc = 취소. 팝업 안 어디에 포커스가 있든 받고, 밑의 창으로는 넘기지 않는다.
            _overlay.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    Finish(ResultOk);
                    evt.StopPropagation();
                }
                else if (evt.keyCode == KeyCode.Escape)
                {
                    Finish(ResultCancel);
                    evt.StopPropagation();
                }
            }, TrickleDown.TrickleDown);
            // 창이 닫히거나 다시 만들어지면(도메인 리로드·언어 전환) 취소로 풀어 준다.
            _overlay.RegisterCallback<DetachFromPanelEvent>(_ => _done.TrySetResult(ResultCancel));

            host.Add(_overlay);
            // 키를 바로 받도록 확인 버튼에 포커스한다.
            okButton.schedule.Execute(() => okButton.Focus());
        }

        private static void AddIfAny(VisualElement parent, Button button)
        {
            if (button != null)
            {
                parent.Add(button);
            }
        }

        private void Finish(int result)
        {
            _done.TrySetResult(result);
            _overlay.RemoveFromHierarchy();
        }
    }
}
