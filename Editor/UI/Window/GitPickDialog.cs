using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 여러 개 중 하나를 고르는 팝업(어느 원격에 올릴지 등). <see cref="GitConfirmDialog"/> 와 같은 막·카드에 고를 것을 한 줄씩 놓는다.
    /// ↑↓ 로 옮기고 Enter 로 고른다. Esc·× 는 취소(-1).
    /// </summary>
    public sealed class GitPickDialog
    {
        private readonly TaskCompletionSource<int> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly VisualElement _overlay;
        private readonly List<Button> _options = new();
        private readonly Focusable _restoreFocus;
        private int _selected;

        /// <returns>고른 항목의 번호. 취소면 -1.</returns>
        public static Task<int> ShowAsync(VisualElement host, ActionGlyphKind icon, string title, string message, IReadOnlyList<string> options, int selected)
        {
            return GitPopupHosts.ShowAsync(host, resolved => new GitPickDialog(resolved, icon, title, message, options, selected)._done.Task, -1);
        }

        private GitPickDialog(VisualElement host, ActionGlyphKind icon, string title, string message, IReadOnlyList<string> options, int selected)
        {
            _overlay = new VisualElement();
            _overlay.AddToClassList("branch-popup");
            VisualElement card = new();
            card.AddToClassList("branch-dialog");
            card.AddToClassList("confirm-dialog");
            card.AddToClassList("pick-dialog");
            GitShadow.Add(card, GitShadowSize.Large);
            _overlay.Add(card);

            Button close = new(() => Finish(-1)) { text = "×", tooltip = GitText.Get("common.cancel") };
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

            if (string.IsNullOrEmpty(message) == false)
            {
                Label messageLabel = new(message);
                messageLabel.AddToClassList("confirm-dialog__message");
                card.Add(messageLabel);
            }

            VisualElement list = new();
            list.AddToClassList("pick-dialog__list");
            for (int i = 0; i < options.Count; i++)
            {
                int index = i;
                Button option = new(() => Finish(index)) { text = options[i] };
                option.AddToClassList("pick-dialog__option");
                _options.Add(option);
                list.Add(option);
            }
            card.Add(list);

            VisualElement buttons = new();
            buttons.AddToClassList("branch-dialog__buttons");
            Button cancel = new(() => Finish(-1)) { text = GitText.Get("common.cancel") };
            cancel.AddToClassList("branch-dialog__cancel");
            buttons.Add(cancel);
            card.Add(buttons);

            _overlay.RegisterCallback<KeyDownEvent>(evt =>
            {
                switch (evt.keyCode)
                {
                    case KeyCode.UpArrow:
                        Select(_selected - 1);
                        break;
                    case KeyCode.DownArrow:
                        Select(_selected + 1);
                        break;
                    case KeyCode.Return:
                    case KeyCode.KeypadEnter:
                        Finish(_selected);
                        break;
                    case KeyCode.Escape:
                        Finish(-1);
                        break;
                    default:
                        return;
                }
                evt.StopPropagation();
            }, TrickleDown.TrickleDown);
            _overlay.RegisterCallback<DetachFromPanelEvent>(_ => _done.TrySetResult(-1));

            _restoreFocus = host.focusController?.focusedElement;
            host.Add(_overlay);
            Select(Mathf.Clamp(selected, 0, Mathf.Max(0, options.Count - 1)));
        }

        private void Select(int index)
        {
            if (_options.Count == 0)
            {
                return;
            }
            _selected = Mathf.Clamp(index, 0, _options.Count - 1);
            for (int i = 0; i < _options.Count; i++)
            {
                _options[i].EnableInClassList("pick-dialog__option--selected", i == _selected);
            }
            Button target = _options[_selected];
            target.schedule.Execute(() => target.Focus());
        }

        private void Finish(int result)
        {
            if (result >= _options.Count)
            {
                result = -1;
            }
            _done.TrySetResult(result);
            GitPopupHosts.Close(_overlay, _restoreFocus);
        }
    }
}
