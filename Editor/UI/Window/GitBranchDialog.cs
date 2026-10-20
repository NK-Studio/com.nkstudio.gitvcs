using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 브랜치 만들기 창에서 고른 것.
    /// </summary>
    public sealed class GitBranchDialogResult
    {
        public string Name;

        /// <summary>만든 뒤 바로 그 브랜치로 전환한다.</summary>
        public bool Checkout;
    }

    /// <summary>
    /// Fork 의 "Create Branch" 와 같은 모양의 브랜치 만들기 팝업. 따로 창(모달 EditorWindow)을 띄우지 않고 Git 창 위에 덮어 띄운다 —
    /// 뒤를 흐리게 덮고 가운데에 카드를 둔다(<see cref="GitServices.PopupHost"/>). 같은 패널이라 테마·UI 배율·마우스 커서가 Git 창과 같다.
    /// 카드: 아이콘·제목·"/ 로 폴더" 안내, 어디서 만드는지, 이름 입력란, "만든 뒤 체크아웃", Create·Cancel.
    /// 이름은 쓰는 동안 바로 검사해서(<see cref="GitBranchNames"/>) 문제를 아래에 보이고 Create 를 끈다.
    /// Enter 는 만들기, Esc·× 는 닫기. 체크아웃 선택은 다음에도 기억한다(<see cref="GitVcsSettings.CheckoutNewBranch"/>).
    /// </summary>
    public sealed class GitBranchDialog
    {
        private readonly List<string> _existing;
        private readonly TaskCompletionSource<GitBranchDialogResult> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly VisualElement _overlay;
        private readonly TextField _name;
        private readonly Label _problem;
        private readonly Toggle _checkout;
        private readonly Button _create;

        /// <summary>
        /// 로컬 브랜치 목록을 읽은 뒤(같은 이름 검사용) 팝업을 띄우고, 고를 때까지 기다린다. 닫거나 취소하면 null.
        /// 띄울 Git 창이 없으면(<see cref="GitServices.PopupHost"/> 가 없음) 한 줄 입력 창으로 묻고, 체크아웃은 지난 선택을 따른다.
        /// </summary>
        /// <param name="startLabel">어디서 만드는지(브랜치 이름, 또는 "짧은 해시 제목").</param>
        public static async Task<GitBranchDialogResult> ShowAsync(GitServices services, string startLabel)
        {
            (List<GitBranchRef> branches, _) = await GitHistory.BranchesAsync();
            List<string> existing = branches?.Where(b => b.IsRemote == false).Select(b => b.Name).ToList() ?? new List<string>();

            VisualElement host = services.PopupHost;
            if (host == null || host.panel == null)
            {
                string name = GitTextPromptWindow.Ask(GitText.Get("branches.createTitle"), GitText.Format("actionbar.newBranchPrompt", startLabel), string.Empty)?.Trim();
                return string.IsNullOrEmpty(name) ? null : new GitBranchDialogResult { Name = name, Checkout = GitVcsSettings.instance.CheckoutNewBranch };
            }
            GitBranchDialog dialog = new(host, startLabel, existing);
            return await dialog._done.Task;
        }

        private GitBranchDialog(VisualElement host, string startLabel, List<string> existing)
        {
            _existing = existing;

            // 뒤를 덮는 막. 밑의 목록·버튼을 누르지 못하게 포인터를 받는다(막을 눌러도 닫지 않는다 — 실수로 쓰던 이름을 잃지 않게).
            _overlay = new VisualElement();
            _overlay.AddToClassList("branch-popup");
            VisualElement card = new();
            card.AddToClassList("branch-dialog");
            _overlay.Add(card);

            Button close = new(Cancel) { text = "×", tooltip = GitText.Get("common.cancel") };
            close.AddToClassList("branch-dialog__close");
            card.Add(close);

            // 머리: 브랜치 아이콘 원 + 제목 + 안내.
            VisualElement header = new();
            header.AddToClassList("branch-dialog__header");
            VisualElement badge = new();
            badge.AddToClassList("branch-dialog__badge");
            ActionGlyph badgeGlyph = new(ActionGlyphKind.Branch);
            badgeGlyph.AddToClassList("branch-dialog__badge-glyph");
            badge.Add(badgeGlyph);
            header.Add(badge);
            VisualElement titles = new();
            titles.AddToClassList("branch-dialog__titles");
            titles.Add(Text(GitText.Get("branches.createTitle"), "branch-dialog__title"));
            titles.Add(Text(GitText.Get("branchDialog.hint"), "branch-dialog__hint"));
            header.Add(titles);
            card.Add(header);

            // 어디서 만드는지.
            VisualElement start = Row(GitText.Get("branchDialog.startAt"));
            VisualElement startValue = new();
            startValue.AddToClassList("branch-dialog__start");
            startValue.Add(Glyph());
            startValue.Add(Text(GitDisplay.Nfc(startLabel), "branch-dialog__start-label"));
            start.Add(startValue);
            card.Add(start);

            // 이름 입력란: 안쪽 왼쪽에 브랜치 아이콘, 비어 있으면 자리표시. 상자 어디를 눌러도 입력란에 포커스한다.
            VisualElement nameRow = Row(GitText.Get("branchDialog.name"));
            VisualElement field = new();
            field.AddToClassList("branch-dialog__field");
            field.Add(Glyph());
            _name = new TextField();
            _name.AddToClassList("branch-dialog__input");
            _name.textEdition.placeholder = GitText.Get("branchDialog.placeholder");
            _name.textEdition.hidePlaceholderOnFocus = false;
            _name.RegisterValueChangedCallback(_ => Validate());
            _name.RegisterCallback<FocusInEvent>(_ => field.AddToClassList("branch-dialog__field--focused"));
            _name.RegisterCallback<FocusOutEvent>(_ => field.RemoveFromClassList("branch-dialog__field--focused"));
            field.RegisterCallback<PointerDownEvent>(_ => _name.Focus());
            field.Add(_name);
            nameRow.Add(field);
            card.Add(nameRow);

            // 이름의 문제(쓰는 동안 바로). 문제가 없으면 자리만 지켜 카드가 흔들리지 않게 한다.
            VisualElement problemRow = Row(string.Empty);
            _problem = Text(string.Empty, "branch-dialog__problem");
            problemRow.Add(_problem);
            card.Add(problemRow);

            VisualElement checkoutRow = Row(string.Empty);
            _checkout = new Toggle(GitText.Get("branchDialog.checkout")) { value = GitVcsSettings.instance.CheckoutNewBranch };
            _checkout.AddToClassList("branch-dialog__checkout");
            checkoutRow.Add(_checkout);
            card.Add(checkoutRow);

            VisualElement buttons = new();
            buttons.AddToClassList("branch-dialog__buttons");
            _create = new Button(Confirm) { text = GitText.Get("branchDialog.create") };
            _create.AddToClassList("branch-dialog__create");
            Button cancel = new(Cancel) { text = GitText.Get("common.cancel") };
            cancel.AddToClassList("branch-dialog__cancel");
            buttons.Add(_create);
            buttons.Add(cancel);
            card.Add(buttons);

            // Enter = 만들기(이름이 맞을 때만), Esc = 닫기. 팝업 안 어디에 포커스가 있든 받고, 밑의 창으로는 넘기지 않는다.
            _overlay.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
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
            // 창이 닫히거나 다시 만들어지면(도메인 리로드·언어 전환) 기다리는 쪽을 풀어 준다.
            _overlay.RegisterCallback<DetachFromPanelEvent>(_ => _done.TrySetResult(null));

            host.Add(_overlay);
            Validate();
            _name.schedule.Execute(() => _name.Focus());
        }

        private string NameValue => (_name.value ?? string.Empty).Trim();

        private void Validate()
        {
            BranchNameProblem problem = GitBranchNames.Check(NameValue, _existing);
            _create.SetEnabled(problem == BranchNameProblem.None);
            _problem.text = problem is BranchNameProblem.None or BranchNameProblem.Empty ? string.Empty : GitText.Get("branchDialog.problem" + problem);
        }

        private void Confirm()
        {
            if (GitBranchNames.Check(NameValue, _existing) != BranchNameProblem.None)
            {
                return;
            }
            GitVcsSettings.instance.CheckoutNewBranch = _checkout.value;
            Finish(new GitBranchDialogResult { Name = NameValue, Checkout = _checkout.value });
        }

        private void Cancel()
        {
            Finish(null);
        }

        private void Finish(GitBranchDialogResult result)
        {
            _done.TrySetResult(result);
            _overlay.RemoveFromHierarchy();
        }

        /// <summary>
        /// 왼쪽에 오른쪽 맞춤 이름표(Fork 처럼 "Branch name:"), 오른쪽에 내용이 오는 한 줄.
        /// </summary>
        private static VisualElement Row(string label)
        {
            VisualElement row = new();
            row.AddToClassList("branch-dialog__row");
            row.Add(Text(label, "branch-dialog__label"));
            return row;
        }

        private static Label Text(string text, string className)
        {
            Label label = new(text);
            label.AddToClassList(className);
            return label;
        }

        private static ActionGlyph Glyph()
        {
            ActionGlyph glyph = new(ActionGlyphKind.Branch);
            glyph.AddToClassList("branch-dialog__glyph");
            return glyph;
        }
    }
}
