using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 글 하나를 받는 팝업(이름 바꾸기·태그 이름·업스트림·커밋 메시지 다시 쓰기 등). 따로 뜨는 창 대신 Git 창 위에 덮어 띄운다
    /// (<see cref="GitBranchDialog"/> 와 같은 막·카드).
    /// 쓰는 동안 <c>validate</c> 로 검사해 문제를 카드 안에 한 줄로 보이고, 문제가 있으면 확인을 끈다 — 잘못 써도 닫히지 않고 쓴 글이 남는다.
    /// 한 줄: Enter 확인. 여러 줄: Enter 는 줄바꿈, Cmd/Ctrl+Enter 확인. Esc·× 는 취소.
    /// </summary>
    public sealed class GitPromptDialog
    {
        private readonly TaskCompletionSource<string> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly VisualElement _overlay;
        private readonly TextField _input;
        private readonly Label _problem;
        private readonly Button _ok;
        private readonly Func<string, string> _validate;
        private readonly Focusable _restoreFocus;

        /// <summary>
        /// 팝업을 띄우고 고를 때까지 기다린다. 취소하면 null.
        /// </summary>
        /// <param name="validate">글을 받아 문제 설명(없으면 null)을 돌려준다. null 이면 빈 글만 막는다.</param>
        public static Task<string> ShowAsync(VisualElement host, ActionGlyphKind icon, string title, string message, string initialValue, bool multiline,
            Func<string, string> validate)
        {
            return GitPopupHosts.ShowAsync(host, resolved => new GitPromptDialog(resolved, icon, title, message, initialValue, multiline, validate)._done.Task, null);
        }

        private GitPromptDialog(VisualElement host, ActionGlyphKind icon, string title, string message, string initialValue, bool multiline, Func<string, string> validate)
        {
            _validate = validate;
            _overlay = new VisualElement();
            _overlay.AddToClassList("branch-popup");
            VisualElement card = new();
            card.AddToClassList("branch-dialog");
            card.AddToClassList("prompt-dialog");
            card.EnableInClassList("prompt-dialog--multiline", multiline);
            GitShadow.Add(card, GitShadowSize.Large);
            _overlay.Add(card);

            Button close = new(Cancel) { text = "×", tooltip = GitText.Get("common.cancel") };
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
            VisualElement titles = new();
            titles.AddToClassList("branch-dialog__titles");
            titles.Add(Text(title, "branch-dialog__title"));
            if (string.IsNullOrEmpty(message) == false)
            {
                titles.Add(Text(message, "branch-dialog__hint"));
            }
            header.Add(titles);
            card.Add(header);

            VisualElement field = new();
            field.AddToClassList("branch-dialog__field");
            field.AddToClassList("prompt-dialog__field");
            _input = new TextField { multiline = multiline, value = initialValue ?? string.Empty };
            _input.AddToClassList("branch-dialog__input");
            _input.AddToClassList("prompt-dialog__input");
            _input.RegisterValueChangedCallback(_ => Validate());
            _input.RegisterCallback<FocusInEvent>(_ => field.AddToClassList("branch-dialog__field--focused"));
            _input.RegisterCallback<FocusOutEvent>(_ => field.RemoveFromClassList("branch-dialog__field--focused"));
            field.RegisterCallback<PointerDownEvent>(_ => _input.Focus());
            field.Add(_input);
            card.Add(field);

            // 문제가 없으면 자리만 지켜 카드가 흔들리지 않게 한다.
            _problem = Text(string.Empty, "branch-dialog__problem");
            _problem.AddToClassList("prompt-dialog__problem");
            card.Add(_problem);

            VisualElement buttons = new();
            buttons.AddToClassList("branch-dialog__buttons");
            _ok = new Button(Confirm) { text = GitText.Get("common.ok") };
            _ok.AddToClassList("branch-dialog__create");
            Button cancel = new(Cancel) { text = GitText.Get("common.cancel") };
            cancel.AddToClassList("branch-dialog__cancel");
            buttons.Add(_ok);
            buttons.Add(cancel);
            card.Add(buttons);

            _overlay.RegisterCallback<KeyDownEvent>(evt =>
            {
                bool enter = evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter;
                if (enter && (multiline == false || evt.actionKey))
                {
                    Confirm();
                    evt.StopPropagation();
                }
                else if (evt.keyCode == KeyCode.Escape)
                {
                    Cancel();
                    evt.StopPropagation();
                }
            }, TrickleDown.TrickleDown);
            _overlay.RegisterCallback<DetachFromPanelEvent>(_ => _done.TrySetResult(null));

            _restoreFocus = host.focusController?.focusedElement;
            host.Add(_overlay);
            Validate();
            _input.schedule.Execute(() =>
            {
                _input.Focus();
                if (multiline == false)
                {
                    _input.SelectAll();
                }
            });
        }

        private string Value => _input.value ?? string.Empty;

        /// <summary>지금 글의 문제. 없으면 null. 빈 글은 설명 없이 확인만 끈다.</summary>
        private string Problem(out bool empty)
        {
            empty = string.IsNullOrWhiteSpace(Value);
            return empty ? null : _validate?.Invoke(Value);
        }

        private void Validate()
        {
            string problem = Problem(out bool empty);
            _ok.SetEnabled(empty == false && problem == null);
            _problem.text = problem ?? string.Empty;
        }

        private void Confirm()
        {
            if (Problem(out bool empty) != null || empty)
            {
                Validate();
                return;
            }
            Finish(Value);
        }

        private void Cancel()
        {
            Finish(null);
        }

        private void Finish(string result)
        {
            _done.TrySetResult(result);
            GitPopupHosts.Close(_overlay, _restoreFocus);
        }

        private static Label Text(string text, string className)
        {
            Label label = new(text);
            label.AddToClassList(className);
            return label;
        }
    }
}
