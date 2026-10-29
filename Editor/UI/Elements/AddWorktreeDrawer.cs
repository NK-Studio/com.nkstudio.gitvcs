using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 워크트리 만들기 서랍(GitKraken 의 Create Worktree). 사이드바 워크트리 머리 줄의 + 로 연다.
    /// 체크아웃할 참조(로컬·원격 브랜치·태그)를 고르고, 새 브랜치 이름(비우면 그 참조 그대로)과 폴더를 정한다.
    /// 원격 브랜치를 고르면 같은 이름의 로컬 브랜치를 새로 만들도록 채우고, 폴더는 저장소 옆 "&lt;저장소&gt;-&lt;브랜치&gt;" 로 채운다(고치면 그대로 둔다).
    /// </summary>
    public sealed class AddWorktreeDrawer : SideDrawer
    {
        private readonly SearchSelect<GitRefChoice> _reference;
        private readonly TextField _branch;
        private readonly TextField _path;
        private readonly Label _problem;
        private readonly Button _create;

        private GitRefSet _refs = new();
        private HashSet<string> _localNames = new();
        private bool _branchEdited;
        private bool _pathEdited;

        public AddWorktreeDrawer(VisualElement host, GitServices services)
            : base(host, services, ActionGlyphKind.Worktree, GitText.Get("worktree.createTitle"))
        {
            VisualElement page = Section(Body);
            Caption(page, GitText.Get("worktree.reference"));
            _reference = new SearchSelect<GitRefChoice>(GitText.Get("drawer.select"), c => c.Name, c => GitText.Get("worktree.kind" + c.Kind));
            _reference.Changed += OnReferenceChosen;
            page.Add(_reference.Root);

            _branch = Field(page, GitText.Get("worktree.branch"), GitText.Get("worktree.branchPlaceholder"));
            _branch.RegisterValueChangedCallback(_ =>
            {
                _branchEdited = true;
                SuggestPath();
                Validate();
            });

            Caption(page, GitText.Get("worktree.path"));
            VisualElement pathRow = new();
            pathRow.AddToClassList(Block + "__row");
            _path = Field(pathRow, null, null);
            _path.AddToClassList(Block + "__grow");
            _path.RegisterValueChangedCallback(_ =>
            {
                _pathEdited = true;
                Validate();
            });
            SecondaryButton(pathRow, GitText.Get("worktree.browse"), Browse).AddToClassList(Block + "__browse");
            page.Add(pathRow);

            _problem = Problem(page);
            _create = PrimaryButton(page, GitText.Get("worktree.create"), Create);
        }

        protected override void Reset()
        {
            _reference.Select(null);
            _branch.SetValueWithoutNotify(string.Empty);
            _path.SetValueWithoutNotify(string.Empty);
            _branchEdited = false;
            _pathEdited = false;
        }

        protected override async void LoadAfterOpen(int openCount)
        {
            GitRefSet refs = await Services.History.RefsAsync();
            if (IsCurrent(openCount) == false)
            {
                return;
            }
            _refs = refs;
            _localNames = new HashSet<string>(refs.Branches.Where(b => b.IsRemote == false).Select(b => b.Name));
            List<GitRefChoice> choices = GitWorktreeForm.Choices(refs.Branches, refs.Tags);
            _reference.SetItems(choices);
            // PR 메뉴의 "Pull Request 에서 워크트리 만들기" 로 열었으면 그 브랜치를 골라 둔다.
            if (_pendingReference != null)
            {
                GitRefChoice pending = choices.FirstOrDefault(c => c.Name == _pendingReference);
                _pendingReference = null;
                if (pending != null)
                {
                    _reference.Select(pending);
                    OnReferenceChosen(pending);
                }
            }
            Validate();
        }

        private string _pendingReference;

        /// <summary>참조(원격 브랜치 이름 등)를 골라 둔 채로 연다.</summary>
        public void OpenFor(string reference)
        {
            _pendingReference = reference;
            Open();
        }

        protected override void FocusFirstField()
        {
            _reference.Focus();
        }

        private void OnReferenceChosen(GitRefChoice choice)
        {
            if (_branchEdited == false)
            {
                _branch.SetValueWithoutNotify(GitWorktreeForm.SuggestBranch(choice, _localNames) ?? string.Empty);
            }
            SuggestPath();
            Validate();
        }

        private void SuggestPath()
        {
            if (_pathEdited)
            {
                return;
            }
            string name = string.IsNullOrWhiteSpace(_branch.value) ? _reference.Selected?.Name : _branch.value;
            _path.SetValueWithoutNotify(GitWorktreeForm.SuggestPath(GitRepository.RepoRoot, name));
        }

        private void Browse()
        {
            string start = string.IsNullOrWhiteSpace(_path.value) ? Path.GetDirectoryName(GitRepository.RepoRoot ?? string.Empty) : _path.value;
            string chosen = EditorUtility.OpenFolderPanel(GitText.Get("worktree.path"), start, string.Empty);
            if (string.IsNullOrEmpty(chosen) == false)
            {
                _path.value = chosen.Replace('\\', '/');
            }
        }

        /// <summary>만드는 중. 그동안은 검사하지 않는다 — git 이 막 만든 폴더를 "비어 있지 않다" 로 잘못 알리지 않게.</summary>
        private bool _creating;

        protected override void Validate()
        {
            if (_creating)
            {
                _create.SetEnabled(false);
                return;
            }
            GitRefChoice choice = _reference.Selected;
            string branch = _branch.value?.Trim();
            string path = _path.value?.Trim();
            string problem = null;
            if (string.IsNullOrEmpty(branch) == false)
            {
                problem = NameProblemText(GitBranchNames.Check(branch, _localNames), "branchDialog.problemExists");
            }
            string usedBy = GitWorktreeForm.CheckedOutIn(choice, branch, _refs.Worktrees);
            if (problem == null && usedBy != null)
            {
                problem = GitText.Format("worktree.problemCheckedOut", choice.Name, usedBy);
            }
            if (problem == null && string.IsNullOrEmpty(path) == false && Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any())
            {
                problem = GitText.Get("worktree.problemNotEmpty");
            }
            if (problem == null && string.IsNullOrEmpty(path) == false && File.Exists(path))
            {
                problem = GitText.Get("worktree.problemNotEmpty");
            }
            Show(_problem, problem);
            _create.SetEnabled(Services.Runner.IsBusy == false && choice != null && string.IsNullOrEmpty(path) == false && problem == null);
        }

        private async void Create()
        {
            GitRefChoice choice = _reference.Selected;
            if (choice == null || Services.Runner.IsBusy)
            {
                return;
            }
            string branch = string.IsNullOrWhiteSpace(_branch.value) ? null : _branch.value.Trim();
            string path = _path.value.Trim();
            _creating = true;
            GitResult result;
            try
            {
                result = await Services.Runner.RunAsync(GitText.Format("op.addWorktree", branch ?? choice.Name),
                    () => Services.Commands.AddWorktreeAsync(path, choice.Name, branch), refreshAssets: false);
            }
            finally
            {
                _creating = false;
            }
            if (result.Success)
            {
                Close();
                return;
            }
            Validate();
        }
    }
}
