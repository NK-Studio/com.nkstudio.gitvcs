using System.IO;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 서브모듈 편집 서랍(GitKraken 의 Edit Submodule). 사이드바 서브모듈 줄을 두 번 누르면 연다.
    /// 원격 주소 바꾸기, 저장소에 기록된 커밋과 맞는지, 지금 체크아웃된 커밋(시각·해시·메시지), 그 저장소 열기·서브모듈 지우기.
    /// </summary>
    public sealed class EditSubmoduleDrawer : SideDrawer
    {
        private const string Sm = "submodule-edit";

        private readonly TextField _url;
        private readonly TextField _path;
        private readonly Label _problem;
        private readonly Button _apply;
        private readonly Label _sync;
        private readonly Label _revisionCaption;
        private readonly VisualElement _revisionSkeleton;
        private readonly VisualElement _revision;
        private readonly Label _commit;
        private readonly Label _message;
        private readonly Button _open;
        private readonly Button _delete;

        private GitSubmoduleRef _submodule;
        private GitSubmoduleDetail _detail;

        /// <summary>주소를 바꾸거나 지웠을 때. 창이 사이드바를 다시 읽는다.</summary>
        public event System.Action Changed;

        public EditSubmoduleDrawer(VisualElement host, GitServices services)
            : base(host, services, ActionGlyphKind.Submodule, GitText.Get("submodule.editTitle"))
        {
            Root.AddToClassList(Sm);
            VisualElement page = Section(Body);
            _url = Field(page, GitText.Get("submodule.url"), null);
            _url.RegisterValueChangedCallback(_ => Validate());
            _path = Field(page, GitText.Get("submodule.path"), null);
            _path.isReadOnly = true;
            _path.AddToClassList(Sm + "__readonly");
            _problem = Problem(page);
            VisualElement applyRow = new();
            applyRow.AddToClassList(Block + "__buttons");
            _apply = SecondaryButton(applyRow, GitText.Get("submodule.edit"), () => _ = ApplyUrlAsync());
            page.Add(applyRow);

            VisualElement state = Section(Body);
            state.AddToClassList(Sm + "__state");
            _sync = new Label();
            _sync.AddToClassList(Sm + "__sync");
            state.Add(_sync);

            VisualElement revision = Section(Body);
            _revisionCaption = Caption(revision, GitText.Get("submodule.revision"));
            _revisionSkeleton = new VisualElement();
            _revisionSkeleton.AddToClassList(Sm + "__revision-skeleton");
            GitSkeleton.Bar(_revisionSkeleton, 0, 12, widthPercent: 70);
            GitSkeleton.Bar(_revisionSkeleton, 0, 12, widthPercent: 90).style.marginTop = 8;
            GitSkeleton.Bar(_revisionSkeleton, 0, 12, widthPercent: 55).style.marginTop = 8;
            GitSkeleton.Pulse(_revisionSkeleton);
            revision.Add(_revisionSkeleton);
            _revision = new VisualElement();
            _revision.AddToClassList(Sm + "__revision");
            _commit = Fact(_revision, GitText.Get("submodule.commit"));
            _commit.AddToClassList(Sm + "__hash");
            _message = Fact(_revision, GitText.Get("submodule.message"));
            revision.Add(_revision);

            VisualElement actions = new();
            actions.AddToClassList(Sm + "__actions");
            _open = IconButton(actions, ActionGlyphKind.Folder, GitText.Get("submodule.open"), OpenSubmoduleRepository);
            _open.AddToClassList(Sm + "__open");
            _delete = IconButton(actions, ActionGlyphKind.Trash, GitText.Get("submodule.delete"), () => _ = DeleteAsync());
            _delete.AddToClassList(Sm + "__delete");
            Body.Add(actions);
        }

        /// <summary>아이콘 + 글자 버튼(GitKraken 처럼 글자 왼쪽에 아이콘). 아이콘은 글자색을 따른다.</summary>
        private static Button IconButton(VisualElement parent, ActionGlyphKind icon, string text, System.Action clicked)
        {
            Button button = SecondaryButton(parent, string.Empty, clicked);
            button.AddToClassList(Sm + "__icon-button");
            ActionGlyph glyph = new(icon);
            glyph.AddToClassList(Sm + "__button-icon");
            button.Add(glyph);
            Label label = new(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList(Sm + "__button-label");
            button.Add(label);
            return button;
        }

        private static Label Fact(VisualElement parent, string name)
        {
            VisualElement row = new();
            row.AddToClassList(Sm + "__fact");
            Label caption = new(name);
            caption.AddToClassList(Sm + "__fact-name");
            row.Add(caption);
            Label value = new();
            value.AddToClassList(Sm + "__fact-value");
            value.selection.isSelectable = true;
            row.Add(value);
            parent.Add(row);
            return value;
        }

        public void OpenFor(GitSubmoduleRef submodule)
        {
            _submodule = submodule;
            if (IsOpen)
            {
                Close();
                OpenNextInstantly();
            }
            Open();
        }

        protected override void Reset()
        {
            _detail = null;
            _url.SetValueWithoutNotify(string.Empty);
            _path.SetValueWithoutNotify(_submodule?.Path ?? string.Empty);
            Show(_problem, null);
            ShowState();
            _revisionCaption.text = GitText.Get("submodule.revision");
            _revisionSkeleton.SetVisible(true);
            _revision.SetVisible(false);
            _commit.text = string.Empty;
            _message.text = string.Empty;
        }

        protected override async void LoadAfterOpen(int openCount)
        {
            GitSubmoduleRef submodule = _submodule;
            if (submodule == null)
            {
                return;
            }
            GitSubmoduleDetail detail = await GitSubmodules.ReadAsync(submodule);
            if (IsCurrent(openCount) == false || submodule != _submodule)
            {
                return;
            }
            _detail = detail;
            _url.SetValueWithoutNotify(detail.Url ?? string.Empty);
            _revisionCaption.text = detail.Date is { } date
                ? GitText.Format("submodule.revisionAt", date.ToLocalTime().ToString("yyyy-MM-dd HH:mm"))
                : GitText.Get("submodule.revision");
            _commit.text = detail.Commit ?? string.Empty;
            _message.text = string.IsNullOrEmpty(detail.Message) ? GitText.Get("submodule.noMessage") : detail.Message;
            _message.EnableInClassList(Sm + "__fact-value--empty", string.IsNullOrEmpty(detail.Message));
            _revisionSkeleton.SetVisible(false);
            _revision.SetVisible(true);
            Validate();
        }

        private void ShowState()
        {
            GitSubmoduleState state = _submodule?.State ?? GitSubmoduleState.UpToDate;
            string repository = Path.GetFileName((GitRepository.RepoRoot ?? GitRepository.ProjectRoot ?? string.Empty).TrimEnd('/', '\\'));
            _sync.text = GitText.Format("submodule.state" + state, repository);
            foreach (GitSubmoduleState each in (GitSubmoduleState[])System.Enum.GetValues(typeof(GitSubmoduleState)))
            {
                _sync.EnableInClassList(Sm + "__sync--" + each.ToString().ToLowerInvariant(), each == state);
            }
        }

        protected override void FocusFirstField()
        {
            _url.Focus();
        }

        protected override void Validate()
        {
            bool idle = Services.Runner.IsBusy == false;
            string url = _url.value?.Trim() ?? string.Empty;
            bool changed = _detail != null && url.Length > 0 && url != (_detail.Url ?? string.Empty);
            _apply.SetEnabled(idle && changed);
            _open.SetEnabled(_submodule != null && _submodule.State != GitSubmoduleState.NotInitialized);
            _delete.SetEnabled(idle && _submodule != null);
        }

        private async System.Threading.Tasks.Task ApplyUrlAsync()
        {
            if (Services.Runner.IsBusy || _submodule == null)
            {
                return;
            }
            string path = _submodule.Path;
            string url = _url.value.Trim();
            GitResult result = await Services.Runner.RunAsync(GitText.Format("op.setSubmoduleUrl", path), () => GitRepository.RunAsync(GitSubmodules.SetUrl(path, url)), refreshAssets: false);
            if (result.Success)
            {
                if (_detail != null)
                {
                    _detail.Url = url;
                }
                Validate();
                Changed?.Invoke();
            }
        }

        private void OpenSubmoduleRepository()
        {
            if (_submodule == null || _submodule.State == GitSubmoduleState.NotInitialized)
            {
                return;
            }
            string full = GitSubmodules.FullPath(_submodule.Path);
            Close();
            GitRepository.OpenRepository(full);
        }

        private async System.Threading.Tasks.Task DeleteAsync()
        {
            if (Services.Runner.IsBusy || _submodule == null)
            {
                return;
            }
            string path = _submodule.Path;
            string name = _detail?.Name;
            if (await Services.Dialogs.ConfirmAsync(GitText.Get("submodule.deleteTitle"), GitText.Format("submodule.deleteConfirm", path), GitText.Get("common.delete"),
                    danger: true, icon: ActionGlyphKind.Submodule) == false)
            {
                return;
            }
            GitResult result = await Services.Runner.RunAsync(GitText.Format("op.removeSubmodule", path), () => GitSubmodules.RemoveAsync(path, name), refreshAssets: true);
            if (result.Success)
            {
                Close();
                Changed?.Invoke();
            }
        }
    }
}
