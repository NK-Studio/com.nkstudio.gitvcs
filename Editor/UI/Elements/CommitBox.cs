using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 인덱스 커밋 입력란(<c>CommitBox.uxml</c>): 제목·설명·Amend·커밋(GitKraken 레이아웃은 커밋 후 푸시도).
    /// 입력 중인 문구는 <see cref="StagingState"/> 에 남아 창을 다시 만들어도(언어 전환·도메인 리로드) 이어진다.
    /// 커밋 템플릿(<see cref="GitCommitTemplate"/>)이 있으면 빈 입력란에 채우고, 커밋할 때 '#' 주석 줄을 뺀다.
    /// </summary>
    public sealed class CommitBox
    {
        private readonly GitServices _services;
        private readonly StagingState _state;
        private readonly TextField _subject;
        private readonly TextField _description;
        private readonly Toggle _amend;
        private readonly VisualElement _authorNotice;
        private readonly Button _commitButton;
        private readonly Button _commitPushButton;
        private int _stagedCount;
        private string _template;

        public VisualElement Root { get; }

        public CommitBox(VisualElement host, GitServices services, StagingState state, bool showPush)
        {
            _services = services;
            _state = state;
            Root = GitUiAssets.CloneBlock(GitUiAssets.Load("CommitBox"));
            host.Add(Root);

            _subject = Root.Require<TextField>("subject");
            _description = Root.Require<TextField>("description");
            _amend = Root.Require<Toggle>("amend");
            _authorNotice = Root.Require<VisualElement>("author-notice");
            _commitButton = Root.Require<Button>("commit-button");
            _commitPushButton = Root.Require<Button>("commit-push-button");
            _commitPushButton.SetVisible(showPush);

            _subject.textEdition.placeholder = GitText.Get("staging.subjectPlaceholder");
            _description.textEdition.placeholder = GitText.Get("staging.descriptionPlaceholder");
            _subject.SetValueWithoutNotify(state.Subject);
            _description.SetValueWithoutNotify(state.Description);
            _subject.RegisterValueChangedCallback(evt =>
            {
                _state.Subject = evt.newValue;
                Render();
            });
            _description.RegisterValueChangedCallback(evt => _state.Description = evt.newValue);
            _amend.RegisterValueChangedCallback(evt => OnAmendChanged(evt.newValue));
            _commitButton.clicked += () => Commit(push: false);
            _commitPushButton.clicked += () => Commit(push: true);
            LoadTemplate();
        }

        /// <summary>
        /// 템플릿을 읽어 두고, 입력란이 비어 있으면 채운다.
        /// </summary>
        private async void LoadTemplate()
        {
            _template = await GitCommitTemplate.ReadAsync();
            if (_template != null && IsEmpty(_subject.value, _description.value) && _amend.value == false)
            {
                FillTemplate();
            }
        }

        private void FillTemplate()
        {
            (string subject, string description) = _template != null ? GitCommitTemplate.Split(_template) : (string.Empty, string.Empty);
            _subject.value = subject;
            _description.value = description;
        }

        /// <summary>
        /// 사용자가 아무것도 쓰지 않은 상태인가(비어 있거나 템플릿 그대로).
        /// </summary>
        private bool IsUntouched()
        {
            if (IsEmpty(_subject.value, _description.value))
            {
                return true;
            }
            if (_template == null)
            {
                return false;
            }
            (string subject, string description) = GitCommitTemplate.Split(_template);
            return _subject.value == subject && _description.value == description;
        }

        private static bool IsEmpty(string subject, string description)
        {
            return string.IsNullOrWhiteSpace(subject) && string.IsNullOrWhiteSpace(description);
        }

        /// <summary>
        /// stage 된 파일 수가 바뀌었을 때(버튼 글자·활성 상태).
        /// </summary>
        public void SetStagedCount(int count)
        {
            _stagedCount = count;
            Render();
        }

        public void Render()
        {
            IGitRepositoryState repository = _services.Repository;
            bool hasAuthor = string.IsNullOrEmpty(repository.UserName) == false && string.IsNullOrEmpty(repository.UserEmail) == false;
            _authorNotice.SetVisible(hasAuthor == false);

            bool isAmend = _amend.value;
            _commitButton.text = isAmend ? GitText.Get("staging.amendButton") : GitText.Format("staging.commitButton", _stagedCount);
            // 고쳐 쓰기는 stage 한 것이 없어도(메시지만 고칠 때) 할 수 있다. 새 커밋은 제목과 stage 한 파일이 있어야 한다.
            bool canCommit = _services.Runner.IsBusy == false && hasAuthor
                             && (isAmend || (_stagedCount > 0 && string.IsNullOrWhiteSpace(_subject.value) == false));
            _commitButton.SetEnabled(canCommit);
            _commitPushButton.SetEnabled(canCommit && repository.Branch != null && repository.Branch.Detached == false);
        }

        /// <summary>
        /// Amend 를 켜면 비어 있는(또는 템플릿 그대로인) 입력란에 마지막 커밋 메시지를 채운다(Fork·GitKraken 과 같다). 끌 때는 사용자가 쓴 글을 지우지 않는다.
        /// </summary>
        private async void OnAmendChanged(bool isAmend)
        {
            Render();
            if (isAmend == false || IsUntouched() == false)
            {
                return;
            }
            string message = await _services.Commands.LastCommitMessageAsync();
            (string subject, string description) = SplitMessage(message);
            _subject.value = subject;
            _description.value = description;
        }

        /// <summary>
        /// 커밋 메시지를 제목(첫 줄)과 설명(빈 줄 뒤)으로 나눈다. 예: "Fix: a\n\nbody" → ("Fix: a", "body").
        /// </summary>
        public static (string subject, string description) SplitMessage(string message)
        {
            string normalized = (message ?? string.Empty).Replace("\r\n", "\n").Trim();
            int newline = normalized.IndexOf('\n');
            if (newline < 0)
            {
                return (normalized, string.Empty);
            }
            return (normalized.Substring(0, newline).Trim(), normalized.Substring(newline + 1).Trim());
        }

        /// <summary>
        /// 제목과 설명을 git 커밋 메시지 형식(제목, 빈 줄, 본문)으로 합친다.
        /// </summary>
        public static string JoinMessage(string subject, string description)
        {
            string head = (subject ?? string.Empty).Trim();
            string body = (description ?? string.Empty).Trim();
            return body.Length == 0 ? head : $"{head}\n\n{body}";
        }

        private async void Commit(bool push)
        {
            string message = JoinMessage(_subject.value, _description.value);
            // 템플릿 안내문('#' 줄)은 git 편집기에서처럼 커밋 메시지에 남기지 않는다.
            if (_template != null)
            {
                message = GitCommitTemplate.StripComments(message);
            }
            bool isAmend = _amend.value;
            GitBranchInfo branch = _services.Repository.Branch;
            string title = GitText.Get(push ? "staging.commitAndPush" : "op.commit");

            GitResult result = await _services.Runner.RunAsync(title, async () =>
            {
                GitResult commit = await _services.Commands.CommitStagedAsync(message, isAmend);
                if (commit.Success == false || push == false)
                {
                    return commit;
                }
                return await _services.Commands.PushAsync(branch);
            }, refreshAssets: false);

            if (result.Success)
            {
                _amend.value = false;
                FillTemplate();
            }
        }
    }
}
