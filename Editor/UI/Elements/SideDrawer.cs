using System;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 본문 왼쪽 가장자리(사이드바 바로 옆)에서 오른쪽으로 밀려 나오는 서랍(GitKraken 의 Add Remote · Create Worktree · Create Pull Request · Add Submodule).
    /// 머리(아이콘 · 제목 · ✕), 밀려 나오고 들어가는 움직임, Esc 로 닫기, 입력 칸 만들기 도우미를 맡는다. 내용은 하위 클래스가 <see cref="Body"/> 에 채운다.
    /// 창(<see cref="GitWindow"/>)은 서랍을 한 번에 하나만 연다 — 열릴 때 <see cref="Opening"/> 으로 알린다.
    /// </summary>
    public abstract class SideDrawer
    {
        protected const string Block = "side-drawer";
        private const string OpenModifier = Block + "--open";
        private const string HiddenModifier = Block + "--hidden";

        protected readonly GitServices Services;

        /// <summary>
        /// 열 때마다 늘어난다. 기다린 뒤(await) 그사이 닫혔다 다시 열렸는지 비교한다.
        /// </summary>
        private int _openCount;

        public VisualElement Root { get; }

        /// <summary>
        /// 머리 아래 내용 자리.
        /// </summary>
        protected VisualElement Body { get; }

        public bool IsOpen => Root.ClassListContains(OpenModifier);

        /// <summary>
        /// 열리기 직전. 창이 다른 서랍을 닫는다.
        /// </summary>
        public event Action<SideDrawer> Opening;

        protected SideDrawer(VisualElement host, GitServices services, ActionGlyphKind icon, string title)
        {
            Services = services;

            Root = new VisualElement();
            Root.AddToClassList(Block);
            GitShadow.Add(Root, GitShadowSize.Large);
            Root.AddToClassList(HiddenModifier);
            // 닫는 움직임이 끝나면 화면 밖에 남은 서랍이 Tab 포커스·포인터를 받지 않게 숨긴다.
            Root.RegisterCallback<TransitionEndEvent>(_ =>
            {
                if (IsOpen == false)
                {
                    Root.AddToClassList(HiddenModifier);
                }
            });
            Root.RegisterCallback<KeyDownEvent>(evt =>
            {
                // 서랍 안 고르기 상자의 목록이 열려 있으면 그 Esc 는 목록을 닫는 데 쓴다(한 번에 하나만 닫는다).
                bool fromOpenSelect = IsInOpenSelect(evt.target as VisualElement);
                if (evt.keyCode == UnityEngine.KeyCode.Escape && fromOpenSelect == false)
                {
                    Close();
                    evt.StopPropagation();
                }
            }, TrickleDown.TrickleDown);

            VisualElement header = new();
            header.AddToClassList(Block + "__header");
            ActionGlyph headerGlyph = new(icon);
            headerGlyph.AddToClassList(Block + "__header-glyph");
            header.Add(headerGlyph);
            Label titleLabel = new(title);
            titleLabel.AddToClassList(Block + "__title");
            header.Add(titleLabel);
            _titleLabel = titleLabel;
            Button close = new(Close) { text = "✕", tooltip = GitText.Get("common.close") };
            close.AddToClassList(Block + "__close");
            header.Add(close);
            Root.Add(header);

            ScrollView body = new(ScrollViewMode.Vertical);
            body.AddToClassList(Block + "__body");
            // 여백은 이 스크롤 내용에만 준다(안쪽 목록 ListView 의 스크롤 내용까지 밀리지 않게 클래스로 고른다).
            body.contentContainer.AddToClassList(Block + "__content");
            Root.Add(body);
            Body = body;

            Services.Runner.StateChanged += Validate;
            Root.RegisterCallback<DetachFromPanelEvent>(_ => Services.Runner.StateChanged -= Validate);
            host.Add(Root);
        }

        private readonly Label _titleLabel;

        /// <summary>머리 제목을 바꾼다(같은 서랍으로 만들기·고치기를 할 때).</summary>
        protected void SetTitle(string title)
        {
            _titleLabel.text = title;
        }

        /// <summary>
        /// 빈 칸으로 다시 채우고 밀어 낸다. 이미 열려 있으면 첫 칸에 포커스만 옮긴다.
        /// </summary>
        public void Open()
        {
            if (IsOpen)
            {
                FocusFirstField();
                return;
            }
            Opening?.Invoke(this);
            bool instant = _openInstantly;
            _openInstantly = false;
            int openCount = ++_openCount;
            Reset();
            Validate();
            // 다른 서랍 위에 그려지게 맨 앞으로(목록상 앞쪽 서랍이 나가는 서랍 밑에 깔려 움직이지 않게).
            Root.BringToFront();
            Root.RemoveFromClassList(HiddenModifier);
            if (instant)
            {
                // 다른 서랍에서 바로 넘어왔다: 미끄러지지 않고 그 자리에 바로 나타난다(내용만 바뀌는 느낌).
                SetTransitions(false);
                Root.AddToClassList(OpenModifier);
                FocusFirstField();
                Root.schedule.Execute(() => SetTransitions(true));
            }
            else
            {
                // 숨김을 푼 다음 프레임에 열림을 붙여야 움직임(translate 전환)이 보인다.
                Root.schedule.Execute(() =>
                {
                    if (openCount == _openCount)
                    {
                        Root.AddToClassList(OpenModifier);
                        FocusFirstField();
                    }
                });
            }
            LoadAfterOpen(openCount);
        }

        public void Close()
        {
            _openCount++;
            OnClosing();
            Root.RemoveFromClassList(OpenModifier);
        }

        /// <summary>
        /// 다른 서랍으로 바꿀 때: 미끄러져 나가는 움직임 없이 바로 닫는다. 두 서랍이 같은 자리에서 0.4초 동안 겹쳐 움직이면 버벅여 보였다.
        /// </summary>
        public void CloseInstantly()
        {
            _openCount++;
            OnClosing();
            SetTransitions(false);
            Root.RemoveFromClassList(OpenModifier);
            Root.AddToClassList(HiddenModifier);
            Root.schedule.Execute(() => SetTransitions(true));
        }

        /// <summary>바로 다음 <see cref="Open"/> 을 움직임 없이 연다(다른 서랍에서 넘어올 때 창이 부른다).</summary>
        public void OpenNextInstantly()
        {
            _openInstantly = true;
        }

        private bool _openInstantly;

        private void SetTransitions(bool enabled)
        {
            Root.style.transitionDuration = enabled ? new StyleList<TimeValue>(StyleKeyword.Null) : new StyleList<TimeValue>(new System.Collections.Generic.List<TimeValue> { new(0) });
        }

        /// <summary>
        /// 이번에 연 서랍이 아직 열려 있는지(<see cref="LoadAfterOpen"/> 에서 기다린 뒤 확인).
        /// </summary>
        protected bool IsCurrent(int openCount)
        {
            return openCount == _openCount;
        }

        /// <summary>
        /// 열 때 칸을 비우고 기본값을 채운다.
        /// </summary>
        protected abstract void Reset();

        /// <summary>
        /// 연 다음 git·네트워크에서 읽을 것(원격 이름, 참조 목록…). 다 읽으면 <see cref="IsCurrent"/> 로 확인하고 채운다.
        /// </summary>
        protected virtual void LoadAfterOpen(int openCount)
        {
        }

        protected abstract void FocusFirstField();

        /// <summary>
        /// 입력·작업 상태가 바뀔 때 버튼을 켜고 끄고 문제를 보인다.
        /// </summary>
        protected abstract void Validate();

        protected virtual void OnClosing()
        {
        }

        // ── 만들기 도우미 ──

        protected static VisualElement Section(VisualElement parent)
        {
            VisualElement section = new();
            section.AddToClassList(Block + "__section");
            parent.Add(section);
            return section;
        }

        protected static Label Caption(VisualElement parent, string text)
        {
            Label caption = new(text);
            caption.AddToClassList(Block + "__caption");
            parent.Add(caption);
            return caption;
        }

        /// <summary>
        /// 이름(caption) 아래 입력란. help 를 주면 이름 오른쪽에 원 안의 i 를 두고, 마우스를 올리면 그 설명을 툴팁으로 보인다.
        /// </summary>
        protected static TextField Field(VisualElement parent, string caption, string placeholder, bool multiline = false, string help = null)
        {
            if (caption != null && help != null)
            {
                VisualElement row = new();
                row.AddToClassList(Block + "__caption-row");
                Caption(row, caption);
                // 아이콘은 보통 마우스를 받지 않는다(버튼이 받는다) — 툴팁을 보이려면 받아야 한다.
                ActionGlyph info = new(ActionGlyphKind.Info) { tooltip = help, pickingMode = PickingMode.Position };
                info.AddToClassList(Block + "__help");
                row.Add(info);
                parent.Add(row);
            }
            else if (caption != null)
            {
                Caption(parent, caption);
            }
            TextField field = new() { multiline = multiline };
            field.AddToClassList(Block + "__input");
            field.EnableInClassList(Block + "__input--multiline", multiline);
            if (placeholder != null)
            {
                field.textEdition.placeholder = placeholder;
                field.textEdition.hidePlaceholderOnFocus = false;
            }
            parent.Add(field);
            return field;
        }

        protected static Label Problem(VisualElement parent)
        {
            Label problem = new();
            problem.AddToClassList(Block + "__problem");
            problem.SetVisible(false);
            parent.Add(problem);
            return problem;
        }

        protected static Label Note(VisualElement parent)
        {
            Label note = new();
            note.AddToClassList(Block + "__note");
            note.SetVisible(false);
            parent.Add(note);
            return note;
        }

        /// <summary>
        /// 빈 글자면 숨긴다.
        /// </summary>
        protected static void Show(Label label, string text)
        {
            label.text = text ?? string.Empty;
            label.SetVisible(string.IsNullOrEmpty(text) == false);
        }

        /// <summary>
        /// 이름 검사 결과 문구. 비었을 때는 말하지 않는다(버튼만 꺼진다).
        /// </summary>
        protected static string NameProblemText(BranchNameProblem problem, string existsKey)
        {
            return problem switch
            {
                BranchNameProblem.None or BranchNameProblem.Empty => null,
                BranchNameProblem.Exists => GitText.Get(existsKey),
                _ => GitText.Get("branchDialog.problem" + problem),
            };
        }

        protected static Button PrimaryButton(VisualElement parent, string text, Action clicked)
        {
            Button button = new(clicked) { text = text };
            button.AddToClassList(Block + "__primary");
            parent.Add(button);
            return button;
        }

        protected static Button SecondaryButton(VisualElement parent, string text, Action clicked)
        {
            Button button = new(clicked) { text = text };
            button.AddToClassList(Block + "__secondary");
            parent.Add(button);
            return button;
        }

        /// <summary>
        /// GitHub 로그인이 필요한 탭·서랍에서 로그인하지 않았을 때 보이는 안내와 Preferences 열기 버튼.
        /// </summary>
        protected static VisualElement SignInPrompt(VisualElement parent, string message)
        {
            VisualElement prompt = new();
            Label text = new(message);
            text.AddToClassList(Block + "__message");
            prompt.Add(text);
            SecondaryButton(prompt, GitText.Get("remote.signIn"), () => UnityEditor.SettingsService.OpenUserPreferences(GitMainToolbarMenu.PreferencesPath));
            parent.Add(prompt);
            return prompt;
        }

        /// <summary>목록이 열린 고르기 상자(<see cref="SearchSelect{T}"/>) 안에서 온 입력인지.</summary>
        private static bool IsInOpenSelect(VisualElement element)
        {
            for (VisualElement current = element; current != null; current = current.parent)
            {
                if (current.ClassListContains(SearchSelectClasses.Open))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
