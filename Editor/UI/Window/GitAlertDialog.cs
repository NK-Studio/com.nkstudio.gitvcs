using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 알림 팝업. 운영체제 대화상자(<see cref="IDialogService.Alert"/>) 대신 Git 창 위에 덮어 띄운다(<see cref="GitConfirmDialog"/> 와 같은 막·카드).
    /// 카드: 경고 배지·제목, 오류 글(길면 스크롤, 끌어서 골라 복사), 확인(강조색). 바로 가기 버튼(예: "Stash 하고 다시 Pull")을 하나 더 둘 수 있다.
    /// Enter·Esc 는 확인(닫기). 막을 눌러도 닫지 않는다.
    /// </summary>
    public sealed class GitAlertDialog
    {
        private readonly TaskCompletionSource<bool> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly VisualElement _overlay;

        /// <summary>
        /// 팝업을 띄울 수 있는지(Git 창이 열려 있고 그 본체가 패널에 붙어 있는지).
        /// </summary>
        public static bool CanShow(VisualElement host) => host != null && host.panel != null;

        /// <summary>
        /// 팝업을 띄우고 닫힐 때까지 기다린다. 바로 가기(<paramref name="action"/>)를 눌렀으면 true.
        /// 띄울 곳이 없으면 <paramref name="fallback"/> 으로 운영체제 대화상자를 띄운다 — 바로 가기는 그때 확인·취소로 묻는다.
        /// </summary>
        /// <param name="action">null 이면 확인 버튼만 둔다.</param>
        public static Task<bool> ShowAsync(VisualElement host, IDialogService fallback, string title, string message, string action = null)
        {
            if (CanShow(host) == false)
            {
                if (action == null)
                {
                    fallback.Alert(title, message);
                    return Task.FromResult(false);
                }
                return Task.FromResult(fallback.Confirm(title, message, action, GitText.Get("common.ok")));
            }
            GitAlertDialog dialog = new(host, title, message, action);
            return dialog._done.Task;
        }

        private GitAlertDialog(VisualElement host, string title, string message, string action)
        {
            _overlay = new VisualElement();
            _overlay.AddToClassList("branch-popup");
            VisualElement card = new();
            card.AddToClassList("branch-dialog");
            card.AddToClassList("alert-dialog");
            _overlay.Add(card);

            // Fork 의 Git Error 창처럼: 왼쪽에 노란 경고 삼각형, 오른쪽에 제목·한 줄 안내·"자세한 내용"·고정폭 글 상자·버튼.
            WarningGlyph glyph = new();
            glyph.AddToClassList("alert-dialog__icon");
            card.Add(glyph);

            VisualElement body = new();
            body.AddToClassList("alert-dialog__body");
            card.Add(body);

            Label titleLabel = new(title);
            titleLabel.AddToClassList("alert-dialog__title");
            body.Add(titleLabel);

            string text = (message ?? string.Empty).TrimEnd();
            Label messageLabel = new(text);
            messageLabel.selection.isSelectable = true;
            if (LooksLikeGitOutput(text))
            {
                // git 출력: 안내 한 줄 아래에 원문을 고정폭 글 상자에 담는다(파일 목록이 길면 스크롤).
                Label summary = new(GitText.Get("alert.gitSummary"));
                summary.AddToClassList("alert-dialog__summary");
                body.Add(summary);
                Label detailsTitle = new(GitText.Get("alert.details"));
                detailsTitle.AddToClassList("alert-dialog__details-title");
                body.Add(detailsTitle);
                ScrollView scroll = new(ScrollViewMode.Vertical);
                scroll.AddToClassList("alert-dialog__details");
                messageLabel.AddToClassList("alert-dialog__details-text");
                scroll.Add(messageLabel);
                body.Add(scroll);
            }
            else
            {
                // 짧은 안내(이름이 틀림 등)는 글 상자 없이 제목 아래에 그대로 보인다.
                messageLabel.AddToClassList("alert-dialog__summary");
                body.Add(messageLabel);
            }

            VisualElement buttons = new();
            buttons.AddToClassList("branch-dialog__buttons");
            if (action != null)
            {
                Button actionButton = new(() => Finish(true)) { text = action };
                actionButton.AddToClassList("branch-dialog__cancel");
                buttons.Add(actionButton);
            }
            Button okButton = new(() => Finish(false)) { text = GitText.Get(action != null ? "common.close" : "common.ok") };
            okButton.AddToClassList(action != null ? "branch-dialog__cancel" : "branch-dialog__create");
            buttons.Add(okButton);
            body.Add(buttons);

            // Enter·Esc = 닫기. 팝업 안 어디에 포커스가 있든 받고, 밑의 창으로는 넘기지 않는다.
            // 바로 가기 버튼에 포커스가 있을 때 Enter 는 그 버튼을 누른 것으로 친다(Tab 으로 옮겨 고른 경우).
            _overlay.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter || evt.keyCode == KeyCode.Escape)
                {
                    bool onAction = evt.keyCode != KeyCode.Escape && evt.target is Button pressed && pressed != okButton && buttons.Contains(pressed);
                    Finish(onAction);
                    evt.StopPropagation();
                }
            }, TrickleDown.TrickleDown);
            // 창이 닫히거나 다시 만들어지면(도메인 리로드·언어 전환) 닫은 것으로 풀어 준다.
            _overlay.RegisterCallback<DetachFromPanelEvent>(_ => _done.TrySetResult(false));

            host.Add(_overlay);
            okButton.schedule.Execute(() => okButton.Focus());
        }

        /// <summary>
        /// git 이 남긴 출력인지(오류·경고·원격 줄머리가 있는지). 그러면 고정폭 글 상자에 담는다.
        /// </summary>
        private static bool LooksLikeGitOutput(string text)
        {
            foreach (string prefix in new[] { "error:", "fatal:", "hint:", "warning:", "remote:", "error ", "!" })
            {
                if (text.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase) || text.Contains("\n" + prefix))
                {
                    return true;
                }
            }
            return text.Contains("\n");
        }

        private void Finish(bool action)
        {
            _done.TrySetResult(action);
            _overlay.RemoveFromHierarchy();
        }
    }

    /// <summary>
    /// 경고 표시: 둥근 모서리의 노란 삼각형 안에 어두운 "!". 채움색은 USS 의 color(경고색)를 따른다.
    /// </summary>
    internal sealed class WarningGlyph : VisualElement
    {
        public WarningGlyph()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        private void Draw(MeshGenerationContext context)
        {
            float w = contentRect.width;
            float h = contentRect.height;
            if (w <= 0f || h <= 0f)
            {
                return;
            }
            Painter2D painter = context.painter2D;
            float inset = w * 0.06f;
            painter.fillColor = resolvedStyle.color;
            painter.strokeColor = resolvedStyle.color;
            painter.lineWidth = inset * 2f;
            painter.lineJoin = LineJoin.Round;
            painter.BeginPath();
            painter.MoveTo(new Vector2(w * 0.5f, inset));
            painter.LineTo(new Vector2(w - inset, h - inset));
            painter.LineTo(new Vector2(inset, h - inset));
            painter.ClosePath();
            painter.Fill();
            painter.Stroke();

            Color mark = new(0.12f, 0.12f, 0.12f, 1f);
            painter.strokeColor = mark;
            painter.fillColor = mark;
            painter.lineWidth = w * 0.1f;
            painter.lineCap = LineCap.Round;
            painter.BeginPath();
            painter.MoveTo(new Vector2(w * 0.5f, h * 0.36f));
            painter.LineTo(new Vector2(w * 0.5f, h * 0.64f));
            painter.Stroke();
            painter.BeginPath();
            painter.Arc(new Vector2(w * 0.5f, h * 0.79f), w * 0.06f, 0f, 360f);
            painter.Fill();
        }
    }
}
