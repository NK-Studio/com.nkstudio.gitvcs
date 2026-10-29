using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
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
        private GitOperationKind _seenOperation;

        /// <summary>마지막으로 채운 준비 메시지. 작업이 배너(이어가기·취소)로 끝났는데 그대로 남아 있으면 템플릿으로 되돌린다.</summary>
        private string _preparedMessage;

        public VisualElement Root { get; }

        /// <summary>
        /// 제목이 바뀌었을 때(입력·템플릿 채우기·Amend 로 채우기). GitKraken 레이아웃은 그래프 작업 중 줄에 같은 제목을 보인다.
        /// </summary>
        public event Action SubjectChanged;

        /// <summary>
        /// 제목 칸의 글 그대로(템플릿 안내 줄도 포함). 그래프 작업 중 줄 입력란이 같은 글을 보인다.
        /// </summary>
        public string Subject => _subject.value ?? string.Empty;

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
                SubjectChanged?.Invoke();
            });
            _description.RegisterValueChangedCallback(evt => _state.Description = evt.newValue);
            // 칸을 다 지우고 다른 곳을 누르면 그 칸에 템플릿을 다시 채운다(Fork 와 같다).
            _subject.RegisterCallback<FocusOutEvent>(_ => RefillIfEmpty(_subject, GitCommitTemplate.Split(_template).subject));
            _description.RegisterCallback<FocusOutEvent>(_ => RefillIfEmpty(_description, GitCommitTemplate.Split(_template).description));
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
            Render();
        }

        /// <summary>
        /// 다른 곳(그래프 작업 중 줄)에서 쓴 제목을 받는다.
        /// </summary>
        public void SetSubject(string subject)
        {
            _subject.value = subject ?? string.Empty;
        }

        private void RefillIfEmpty(TextField field, string templateText)
        {
            if (_template != null && string.IsNullOrWhiteSpace(field.value))
            {
                field.value = templateText;
            }
        }

        /// <summary>
        /// 실제로 커밋될 제목이 있는가. 템플릿을 쓰면 '#' 안내 줄은 빠지므로 안내 그대로인 제목은 없는 것으로 본다.
        /// </summary>
        private bool HasSubject()
        {
            return _template != null
                ? GitCommitTemplate.StripComments(_subject.value).Length > 0
                : string.IsNullOrWhiteSpace(_subject.value) == false;
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
            FillPreparedMessage(repository);
            bool hasAuthor = string.IsNullOrEmpty(repository.UserName) == false && string.IsNullOrEmpty(repository.UserEmail) == false;
            _authorNotice.SetVisible(hasAuthor == false);

            bool isAmend = _amend.value;
            _commitButton.text = isAmend ? GitText.Get("staging.amendButton") : GitText.Format("staging.commitButton", _stagedCount);
            // 고쳐 쓰기는 stage 한 것이 없어도(메시지만 고칠 때) 할 수 있다. 새 커밋은 제목과 stage 한 파일이 있어야 한다.
            bool canCommit = _services.Runner.IsBusy == false && hasAuthor
                             && (isAmend || (_stagedCount > 0 && HasSubject()));
            _commitButton.SetEnabled(canCommit);
            // 원격이 없으면 커밋만 되고 Push 는 실패한다 — 누르기 전에 막고 까닭을 보인다.
            _commitPushButton.SetEnabled(canCommit && repository.Branch != null && repository.Branch.Detached == false && repository.HasRemote);
            _commitPushButton.tooltip = repository.HasRemote ? string.Empty : GitText.Get("staging.commitPushNoRemote");
        }

        /// <summary>
        /// 멈춘 병합·cherry-pick·revert 는 이 입력란의 커밋으로 끝난다. 그 작업이 보이기 시작할 때 비어 있는(템플릿 그대로인) 입력란에
        /// git 이 준비한 메시지(MERGE_MSG)를 채운다(GitKraken·Fork 와 같다). 사용자가 쓴 글은 덮지 않는다.
        /// 작업이 커밋 없이 끝나면(배너의 이어가기·취소) 손대지 않은 그 메시지를 거둔다.
        /// </summary>
        private void FillPreparedMessage(IGitRepositoryState repository)
        {
            GitOperationKind kind = repository.InProgressOperation;
            if (kind == _seenOperation)
            {
                return;
            }
            _seenOperation = kind;
            string prepared = _preparedMessage;
            _preparedMessage = null;
            if (prepared != null && JoinMessage(_subject.value, _description.value) == prepared)
            {
                FillTemplate();
            }
            if (GitInProgress.RequiresFullCommit(kind) == false || _amend.value || IsUntouched() == false)
            {
                return;
            }
            string message = repository.PreparedCommitMessage;
            if (message != null)
            {
                (string subject, string description) = SplitMessage(message);
                _subject.value = subject;
                _description.value = description;
                _preparedMessage = JoinMessage(subject, description);
            }
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
            // .meta 없이 새 에셋을 커밋하면 다른 사람 쪽에서 GUID 가 새로 생긴다. 빠진 .meta 가 있으면 함께 올릴지 묻는다.
            List<string> missingMeta = GitStaging.MissingMetaPaths(_services.Repository.Entries);
            if (missingMeta.Count > 0)
            {
                string list = string.Join("\n", missingMeta.Take(10)) + (missingMeta.Count > 10 ? $"\n… +{missingMeta.Count - 10}" : string.Empty);
                int choice = await _services.Dialogs.ChooseAsync(GitText.Get("commit.missingMetaTitle"), GitText.Format("commit.missingMetaMessage", list),
                    GitText.Get("commit.missingMetaInclude"), GitText.Get("commit.missingMetaSkip"), altDanger: true);
                if (choice == 1)
                {
                    return;
                }
                if (choice == 0)
                {
                    // Unity 가 아직 임포트하지 않아 .meta 가 없을 수 있다. 임포트해 만들게 한 뒤 있는 것만 올린다.
                    AssetDatabase.Refresh();
                    string root = GitRepository.RepoRoot;
                    string[] present = missingMeta.Where(path => File.Exists(Path.Combine(root, path))).ToArray();
                    if (present.Length > 0)
                    {
                        await _services.Runner.RunAsync(GitText.Get("op.stage"), () => GitRepository.RunAsync(new[] { "add", "--" }.Concat(present).ToArray()),
                            refreshAssets: false, workingTreeOnly: true);
                    }
                }
            }
            // 커밋과 Push 를 따로 돌린다: 커밋이 되면 Push 결과와 상관없이 입력란을 바로 비운다
            // (Push 가 실패해도 "커밋 실패" 로 보이지 않고, 같은 메시지로 다시 누를 일이 없게).
            GitResult result = await _services.Runner.RunAsync(GitText.Get("op.commit"), () => _services.Commands.CommitStagedAsync(message, isAmend), refreshAssets: false);
            if (result.Success == false)
            {
                return;
            }
            _amend.value = false;
            FillTemplate();
            if (push)
            {
                await new SyncFlows(_services).PushAsync(_services.Repository.Branch);
            }
        }
    }
}
