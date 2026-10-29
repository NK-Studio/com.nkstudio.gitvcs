using System.Linq;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// GitKraken 의 "Create / Edit Pull Request Filter": 이름과 GitHub 검색 문법 조건을 받아 사이드바 Pull Requests 구역의 묶음을 만들거나 고친다.
    /// 아래에 쓸 수 있는 조건을 늘 보인다(GitKraken 과 같은 문법 안내).
    /// </summary>
    public sealed class PullRequestFilterDrawer : SideDrawer
    {
        private readonly TextField _name;
        private readonly TextField _query;
        private readonly Label _problem;
        private readonly Button _submit;

        /// <summary>고치는 묶음의 순번. 새로 만들면 -1.</summary>
        private int _editing = -1;

        public PullRequestFilterDrawer(VisualElement host, GitServices services)
            : base(host, services, ActionGlyphKind.PullRequest, GitText.Get("prFilter.createTitle"))
        {
            VisualElement page = Section(Body);
            _name = Field(page, GitText.Get("prFilter.name"), GitText.Get("prFilter.namePlaceholder"));
            _name.RegisterValueChangedCallback(_ => Validate());
            _query = Field(page, GitText.Get("prFilter.query"), GitText.Get("prFilter.queryPlaceholder"));
            _query.RegisterValueChangedCallback(_ => Validate());
            _problem = Problem(page);
            _submit = PrimaryButton(page, GitText.Get("prFilter.create"), Submit);

            VisualElement help = Section(Body);
            Caption(help, GitText.Get("prFilter.syntaxTitle"));
            Label syntax = new(GitText.Get("prFilter.syntax"));
            syntax.AddToClassList(Block + "__note");
            syntax.AddToClassList("pr-filter__syntax");
            syntax.selection.isSelectable = true;
            help.Add(syntax);
        }

        /// <summary>새 묶음을 만든다.</summary>
        public void OpenNew()
        {
            _editing = -1;
            Open();
        }

        /// <summary><paramref name="index"/> 번째 묶음을 고친다.</summary>
        public void OpenEdit(int index)
        {
            _editing = index;
            Open();
        }

        protected override void Reset()
        {
            bool editing = _editing >= 0;
            SetTitle(GitText.Get(editing ? "prFilter.editTitle" : "prFilter.createTitle"));
            _submit.text = GitText.Get(editing ? "prFilter.edit" : "prFilter.create");
            GitPullRequestFilter filter = editing ? GitPullRequestFilters.Load().ElementAtOrDefault(_editing) : null;
            _name.SetValueWithoutNotify(filter?.DisplayName ?? string.Empty);
            _query.SetValueWithoutNotify(filter?.Query ?? string.Empty);
        }

        protected override void FocusFirstField()
        {
            _name.Focus();
        }

        protected override void Validate()
        {
            bool hasName = string.IsNullOrWhiteSpace(_name.value) == false;
            Show(_problem, hasName ? null : GitText.Get("prFilter.nameRequired"));
            _submit.SetEnabled(hasName);
        }

        private void Submit()
        {
            string name = _name.value.Trim();
            if (name.Length == 0)
            {
                return;
            }
            GitPullRequestFilter filter = new() { Name = name, Query = (_query.value ?? string.Empty).Trim() };
            if (_editing >= 0)
            {
                // 기본 묶음 이름을 그대로 두었으면 언어를 계속 따라가게 번역 키를 남긴다.
                GitPullRequestFilter old = GitPullRequestFilters.Load().ElementAtOrDefault(_editing);
                if (GitPullRequestFilters.KeepsDefaultName(old, name))
                {
                    filter.NameKey = old.NameKey;
                    filter.Name = null;
                }
                GitPullRequestFilters.Replace(_editing, filter);
            }
            else
            {
                GitPullRequestFilters.Add(filter);
            }
            Close();
        }
    }
}
