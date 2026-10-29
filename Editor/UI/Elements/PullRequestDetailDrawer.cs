using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// GitKraken 의 Pull Request 화면: 번호·제목·상태, "누가 어디로 합치려 한다", 병합 가능 여부와 Merge(방식 고르기), 설명,
    /// 대화(코멘트·리뷰·코드 줄 코멘트)와 작성(코멘트·승인·변경 요청), Draft ↔ Ready·닫기/다시 열기, 리뷰어·담당자·라벨·브랜치, 바뀐 파일.
    /// 사이드바에서 PR 을 누르면 열린다.
    /// 병합은 GitHub 에 바로 반영되므로 늘 확인 카드를 거치고, 본 때의 head 커밋을 함께 보내 그 사이 새 커밋이 올라왔으면 GitHub 가 거절하게 한다.
    /// </summary>
    public sealed class PullRequestDetailDrawer : SideDrawer
    {
        private const string Pr = "pr-detail";

        /// <summary>GitHub 가 병합 가능 여부를 계산하는 동안 다시 읽는 횟수와 간격.</summary>
        private const int CheckingRetries = 4;
        private const int CheckingDelayMs = 1500;

        private readonly VisualElement _signIn;
        private readonly VisualElement _content;
        private readonly Label _loading;
        private readonly Label _error;
        private readonly Label _title;
        private readonly Label _badge;
        private readonly Label _summary;
        private readonly Label _readiness;
        private readonly List<Button> _methodButtons = new();
        private readonly Button _merge;
        private readonly Button _viewChanges;
        private readonly Button _checkout;
        private readonly Button _draftToggle;
        private readonly Button _openToggle;
        private readonly Label _timelineCaption;
        private readonly VisualElement _timeline;
        private readonly TextField _reply;
        private readonly Label _replyProblem;
        private readonly Button _comment;
        private readonly Button _approve;
        private readonly Button _requestChanges;
        private readonly Label _stats;
        private readonly Label _body;
        private readonly VisualElement _facts;
        private readonly Label _filesCaption;
        private readonly VisualElement _files;

        private GitHubRemoteRepo _repository;
        private GitPullRequestInfo _info;
        private Action _openChanges;
        private Action _openCheckout;
        private GitPullRequestDetail _detail;
        private GitPullRequestMergeMethod _method = GitPullRequestMergeMethod.Merge;
        private bool _merging;

        /// <summary>GitHub 에 무언가 보내는 중(코멘트·리뷰·상태 바꾸기). 그동안 같은 버튼을 다시 누르지 못하게 한다.</summary>
        private bool _posting;

        /// <summary>병합했을 때. 창이 사이드바 PR 목록을 다시 읽고 원격을 fetch 한다.</summary>
        public event Action Merged;

        /// <summary>닫기·다시 열기·Draft 전환처럼 사이드바 목록에 보이는 상태가 바뀌었을 때.</summary>
        public event Action StateChanged;

        public PullRequestDetailDrawer(VisualElement host, GitServices services)
            : base(host, services, ActionGlyphKind.PullRequest, GitText.Get("prDetail.drawerTitle"))
        {
            Root.AddToClassList(Pr);
            _signIn = SignInPrompt(Body, GitText.Get("prDetail.signIn"));
            _loading = Note(Body);
            _error = Problem(Body);

            _content = new VisualElement();
            Body.Add(_content);

            VisualElement head = Section(_content);
            VisualElement titleRow = new();
            titleRow.AddToClassList(Pr + "__title-row");
            _badge = new Label();
            _badge.AddToClassList(Pr + "__badge");
            titleRow.Add(_badge);
            _title = new Label();
            _title.AddToClassList(Pr + "__title");
            _title.selection.isSelectable = true;
            titleRow.Add(_title);
            head.Add(titleRow);
            _summary = new Label();
            _summary.AddToClassList(Block + "__note");
            head.Add(_summary);
            _stats = new Label();
            _stats.AddToClassList(Block + "__note");
            head.Add(_stats);

            VisualElement merge = Section(_content);
            Caption(merge, GitText.Get("prDetail.mergeCaption"));
            _readiness = new Label();
            _readiness.AddToClassList(Pr + "__readiness");
            merge.Add(_readiness);
            VisualElement methods = new();
            methods.AddToClassList(Pr + "__methods");
            foreach (GitPullRequestMergeMethod method in (GitPullRequestMergeMethod[])Enum.GetValues(typeof(GitPullRequestMergeMethod)))
            {
                GitPullRequestMergeMethod captured = method;
                Button option = new(() => SelectMethod(captured)) { text = GitText.Get("prDetail.method" + method), userData = method };
                option.AddToClassList(Pr + "__method");
                methods.Add(option);
                _methodButtons.Add(option);
            }
            merge.Add(methods);
            _merge = PrimaryButton(merge, GitText.Get("prDetail.merge"), () => _ = MergeAsync());

            VisualElement actions = new();
            actions.AddToClassList(Block + "__buttons");
            actions.AddToClassList(Pr + "__actions");
            SecondaryButton(actions, GitText.Get("prDetail.viewOnGitHub"), () => UnityEngine.Application.OpenURL(_detail?.Url ?? _info?.Url));
            _viewChanges = SecondaryButton(actions, GitText.Get("prDetail.viewChanges"), () => _openChanges?.Invoke());
            _checkout = SecondaryButton(actions, GitText.Get("prDetail.checkout"), () => _openCheckout?.Invoke());
            _draftToggle = SecondaryButton(actions, string.Empty, () => _ = ToggleDraftAsync());
            _openToggle = SecondaryButton(actions, string.Empty, () => _ = ToggleOpenAsync());
            _content.Add(actions);

            VisualElement description = Section(_content);
            Caption(description, GitText.Get("prDetail.description"));
            _body = new Label();
            _body.AddToClassList(Pr + "__body");
            _body.selection.isSelectable = true;
            description.Add(_body);

            VisualElement conversation = Section(_content);
            _timelineCaption = Caption(conversation, string.Empty);
            _timeline = new VisualElement();
            _timeline.AddToClassList(Pr + "__timeline");
            conversation.Add(_timeline);
            _reply = Field(conversation, GitText.Get("prDetail.replyCaption"), GitText.Get("prDetail.replyPlaceholder"), multiline: true);
            _reply.AddToClassList(Pr + "__reply");
            _reply.RegisterValueChangedCallback(_ => Validate());
            _replyProblem = Problem(conversation);
            VisualElement replyButtons = new();
            replyButtons.AddToClassList(Block + "__buttons");
            replyButtons.AddToClassList(Pr + "__reply-buttons");
            _comment = SecondaryButton(replyButtons, GitText.Get("prDetail.comment"), () => _ = PostAsync(null));
            _approve = SecondaryButton(replyButtons, GitText.Get("prDetail.approve"), () => _ = PostAsync(GitPullRequestReviewEvent.Approve));
            _approve.AddToClassList(Pr + "__approve");
            _requestChanges = SecondaryButton(replyButtons, GitText.Get("prDetail.requestChanges"), () => _ = PostAsync(GitPullRequestReviewEvent.RequestChanges));
            _requestChanges.AddToClassList(Pr + "__request-changes");
            conversation.Add(replyButtons);

            _facts = Section(_content);

            VisualElement files = Section(_content);
            _filesCaption = Caption(files, string.Empty);
            _files = new VisualElement();
            _files.AddToClassList(Pr + "__files");
            files.Add(_files);
        }

        /// <param name="openChanges">로컬에서 바뀐 것 보기(보내는 브랜치를 fetch 하지 않았으면 null).</param>
        /// <param name="checkout">보내는 브랜치 체크아웃(없으면 null).</param>
        public void OpenFor(GitPullRequestInfo info, GitHubRemoteRepo repository, Action openChanges, Action checkout)
        {
            _info = info;
            _repository = repository;
            _openChanges = openChanges;
            _openCheckout = checkout;
            if (IsOpen)
            {
                // 다른 PR 을 눌렀으면 열린 서랍의 내용만 바꾼다.
                Reset();
                Validate();
                LoadAfterOpen(CurrentOpenCount());
                return;
            }
            Open();
        }

        private int _shownOpenCount;

        private int CurrentOpenCount() => _shownOpenCount;

        protected override void Reset()
        {
            _detail = null;
            _merging = false;
            SetTitle(_info == null ? GitText.Get("prDetail.drawerTitle") : GitText.Format("prDetail.drawerTitleNumber", _info.Number));
            _title.text = _info?.Title ?? string.Empty;
            _badge.text = string.Empty;
            _summary.text = string.Empty;
            _stats.text = string.Empty;
            _readiness.text = string.Empty;
            _body.text = string.Empty;
            _facts.Clear();
            _files.Clear();
            _filesCaption.text = string.Empty;
            _timeline.Clear();
            _timelineCaption.text = GitText.Get("prDetail.conversation");
            _reply.SetValueWithoutNotify(string.Empty);
            Show(_replyProblem, null);
            _posting = false;
            Show(_error, null);
            Show(_loading, GitText.Get("common.loading"));
            SelectMethod(_method);
        }

        protected override void LoadAfterOpen(int openCount)
        {
            _shownOpenCount = openCount;
            _ = LoadAsync(openCount, _info, _repository);
        }

        private async Task LoadAsync(int openCount, GitPullRequestInfo info, GitHubRemoteRepo repository)
        {
            bool signedIn = GitHubAccount.IsSignedIn;
            _signIn.SetVisible(signedIn == false);
            _content.SetVisible(signedIn);
            if (signedIn == false || info == null || repository == null)
            {
                Show(_loading, null);
                return;
            }
            Task<(List<GitPullRequestFile> files, string error)> filesTask = GitHubPullRequests.FilesAsync(repository, info.Number);
            Task<(List<GitPullRequestTimelineItem> items, string error)> timelineTask = GitHubPullRequests.TimelineAsync(repository, info.Number);
            (GitPullRequestDetail detail, string error) = await GitHubPullRequests.GetAsync(repository, info.Number);
            // 병합 가능 여부는 GitHub 가 PR 을 처음 열 때 뒤에서 계산한다. 계산 중이면 잠시 뒤 다시 읽는다.
            for (int retry = 0; retry < CheckingRetries && detail != null && detail.Mergeable == null && detail.IsOpen && IsStillShowing(openCount, info); retry++)
            {
                ShowDetail(detail);
                await Task.Delay(CheckingDelayMs);
                (detail, error) = await GitHubPullRequests.GetAsync(repository, info.Number);
            }
            if (IsStillShowing(openCount, info) == false)
            {
                return;
            }
            Show(_loading, null);
            if (detail == null)
            {
                Show(_error, error ?? GitText.Get("prDetail.loadFailed"));
                return;
            }
            ShowDetail(detail);
            (List<GitPullRequestTimelineItem> items, string timelineError) = await timelineTask;
            if (IsStillShowing(openCount, info))
            {
                ShowTimeline(items, timelineError);
            }
            (List<GitPullRequestFile> files, string filesError) = await filesTask;
            if (IsStillShowing(openCount, info))
            {
                ShowFiles(files, detail.ChangedFiles, filesError);
            }
        }

        /// <summary>코멘트·리뷰를 보낸 뒤 대화와 상태(리뷰 결정으로 병합 가능 여부가 바뀔 수 있다)만 다시 읽는다.</summary>
        private async Task ReloadAfterPostAsync()
        {
            GitPullRequestInfo info = _info;
            GitHubRemoteRepo repository = _repository;
            int openCount = _shownOpenCount;
            Task<(List<GitPullRequestTimelineItem> items, string error)> timelineTask = GitHubPullRequests.TimelineAsync(repository, info.Number);
            (GitPullRequestDetail detail, _) = await GitHubPullRequests.GetAsync(repository, info.Number);
            (List<GitPullRequestTimelineItem> items, string error) = await timelineTask;
            if (IsStillShowing(openCount, info) == false)
            {
                return;
            }
            if (detail != null)
            {
                ShowDetail(detail);
            }
            ShowTimeline(items, error);
        }

        private void ShowTimeline(List<GitPullRequestTimelineItem> items, string error)
        {
            _timeline.Clear();
            _timelineCaption.text = GitText.Format("prDetail.conversationCount", items.Count);
            if (items.Count == 0 && string.IsNullOrEmpty(error))
            {
                Label empty = new(GitText.Get("prDetail.noConversation"));
                empty.AddToClassList(Block + "__note");
                _timeline.Add(empty);
            }
            foreach (GitPullRequestTimelineItem item in items)
            {
                _timeline.Add(TimelineRow(item));
            }
            if (string.IsNullOrEmpty(error) == false)
            {
                Show(Problem(_timeline), error);
            }
        }

        private static VisualElement TimelineRow(GitPullRequestTimelineItem item)
        {
            VisualElement row = new();
            row.AddToClassList(Pr + "__event");
            row.AddToClassList(Pr + "__event--" + item.Kind.ToString().ToLowerInvariant());

            VisualElement head = new();
            head.AddToClassList(Pr + "__event-head");
            Label author = new(item.Author ?? "?");
            author.AddToClassList(Pr + "__event-author");
            head.Add(author);
            Label action = new(ActionText(item));
            action.AddToClassList(Pr + "__event-action");
            if (item.Kind == GitPullRequestTimelineKind.Review)
            {
                action.AddToClassList(Pr + "__event-action--" + item.ReviewState.ToLowerInvariant().Replace('_', '-'));
            }
            head.Add(action);
            Label time = new(item.Time == DateTime.MinValue ? string.Empty : item.Time.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
            time.AddToClassList(Pr + "__event-time");
            head.Add(time);
            row.Add(head);

            if (string.IsNullOrWhiteSpace(item.Body) == false)
            {
                Label body = new(item.Body);
                body.AddToClassList(Pr + "__event-body");
                body.selection.isSelectable = true;
                row.Add(body);
            }
            return row;
        }

        private static string ActionText(GitPullRequestTimelineItem item)
        {
            switch (item.Kind)
            {
                case GitPullRequestTimelineKind.Review:
                    return GitText.Get(item.ReviewState switch
                    {
                        "APPROVED" => "prDetail.eventApproved",
                        "CHANGES_REQUESTED" => "prDetail.eventChangesRequested",
                        "DISMISSED" => "prDetail.eventDismissed",
                        _ => "prDetail.eventReviewed",
                    });
                case GitPullRequestTimelineKind.ReviewComment:
                    return GitText.Format("prDetail.eventLineComment", item.Line > 0 ? $"{item.Path}:{item.Line}" : item.Path);
                default:
                    return GitText.Get("prDetail.eventCommented");
            }
        }

        /// <param name="decision">null 이면 일반 코멘트, 아니면 그 결정의 리뷰.</param>
        private async Task PostAsync(GitPullRequestReviewEvent? decision)
        {
            GitPullRequestDetail detail = _detail;
            GitHubRemoteRepo repository = _repository;
            string body = (_reply.value ?? string.Empty).Trim();
            if (detail == null || repository == null || _posting)
            {
                return;
            }
            bool needsBody = decision == null || GitPullRequestDetails.ReviewNeedsBody(decision.Value);
            if (needsBody && body.Length == 0)
            {
                Show(_replyProblem, GitText.Get(decision == null ? "prDetail.commentEmpty" : "prDetail.reviewEmpty"));
                return;
            }
            _posting = true;
            Validate();
            string error = decision == null
                ? await GitHubPullRequests.CommentAsync(repository, detail.Number, body)
                : await GitHubPullRequests.ReviewAsync(repository, detail.Number, decision.Value, body);
            _posting = false;
            if (ReferenceEquals(detail, _detail) == false)
            {
                return;
            }
            if (error != null)
            {
                Validate();
                await Services.Dialogs.AlertAsync(GitText.Get(decision == null ? "prDetail.commentFailed" : "prDetail.reviewFailed"), error, ActionGlyphKind.PullRequest);
                return;
            }
            // 보냈으면 쓴 글을 비운다(실패하면 남겨 다시 보낼 수 있게).
            _reply.SetValueWithoutNotify(string.Empty);
            Show(_replyProblem, null);
            Validate();
            await ReloadAfterPostAsync();
        }

        private async Task ToggleDraftAsync()
        {
            GitPullRequestDetail detail = _detail;
            if (detail == null || detail.IsOpen == false || _posting)
            {
                return;
            }
            bool toDraft = detail.IsDraft == false;
            await ChangeStateAsync(detail, () => GitHubPullRequests.SetDraftAsync(detail.NodeId, toDraft), toDraft ? "prDetail.toDraftFailed" : "prDetail.readyFailed");
        }

        private async Task ToggleOpenAsync()
        {
            GitPullRequestDetail detail = _detail;
            GitHubRemoteRepo repository = _repository;
            if (detail == null || repository == null || detail.IsMerged || _posting)
            {
                return;
            }
            bool reopen = detail.IsOpen == false;
            if (reopen == false && await Services.Dialogs.ConfirmAsync(GitText.Format("prDetail.closeTitle", detail.Number),
                    GitText.Format("prDetail.closeMessage", detail.Number, detail.Title), GitText.Get("prDetail.close"), danger: true, icon: ActionGlyphKind.PullRequest) == false)
            {
                return;
            }
            await ChangeStateAsync(detail, () => GitHubPullRequests.SetOpenAsync(repository, detail.Number, reopen), reopen ? "prDetail.reopenFailed" : "prDetail.closeFailed");
        }

        private async Task ChangeStateAsync(GitPullRequestDetail detail, Func<Task<string>> change, string failedKey)
        {
            _posting = true;
            Validate();
            string error = await change();
            _posting = false;
            if (ReferenceEquals(detail, _detail) == false)
            {
                return;
            }
            if (error != null)
            {
                Validate();
                await Services.Dialogs.AlertAsync(GitText.Format(failedKey, detail.Number), error, ActionGlyphKind.PullRequest);
                return;
            }
            StateChanged?.Invoke();
            await ReloadAfterPostAsync();
        }

        private bool IsStillShowing(int openCount, GitPullRequestInfo info)
        {
            return IsCurrent(openCount) && ReferenceEquals(info, _info);
        }

        private void ShowDetail(GitPullRequestDetail detail)
        {
            _detail = detail;
            _title.text = detail.Title;
            GitPullRequestReadiness readiness = GitPullRequestDetails.Readiness(detail);
            string state = detail.IsMerged ? "Merged" : detail.IsOpen == false ? "Closed" : detail.IsDraft ? "Draft" : "Open";
            _badge.text = GitText.Get("prDetail.state" + state);
            foreach (string modifier in new[] { "Merged", "Closed", "Draft", "Open" })
            {
                _badge.EnableInClassList(Pr + "__badge--" + modifier.ToLowerInvariant(), modifier == state);
            }
            _summary.text = GitText.Format("prDetail.summary", detail.Author ?? "?", detail.Commits, detail.BaseRef ?? "?", HeadLabel(detail));
            _stats.text = GitText.Format("prDetail.stats", detail.ChangedFiles, detail.Additions, detail.Deletions, detail.Comments);
            _readiness.text = GitText.Get("prDetail.readiness" + readiness);
            _readiness.EnableInClassList(Pr + "__readiness--ok", GitPullRequestDetails.CanMerge(readiness));
            _readiness.EnableInClassList(Pr + "__readiness--bad", readiness is GitPullRequestReadiness.Conflicts or GitPullRequestReadiness.Blocked or GitPullRequestReadiness.Behind);
            _body.text = string.IsNullOrWhiteSpace(detail.Body) ? GitText.Get("prDetail.noDescription") : detail.Body;
            _body.EnableInClassList(Pr + "__body--empty", string.IsNullOrWhiteSpace(detail.Body));

            _facts.Clear();
            Fact(GitText.Get("prDetail.reviewers"), detail.Reviewers);
            Fact(GitText.Get("prDetail.assignees"), detail.Assignees);
            Fact(GitText.Get("prDetail.labels"), detail.Labels);
            Fact(GitText.Get("prDetail.branch"), new[] { HeadLabel(detail) + " → " + detail.BaseRef });
            Validate();
        }

        private static string HeadLabel(GitPullRequestDetail detail)
        {
            // 포크에서 온 PR 은 GitHub 처럼 "owner:branch" 로 보인다.
            string owner = detail.HeadRepository?.Split('/').FirstOrDefault();
            string baseOwner = detail.Url?.Split('/').Skip(3).FirstOrDefault();
            return owner != null && baseOwner != null && string.Equals(owner, baseOwner, StringComparison.OrdinalIgnoreCase) == false
                ? owner + ":" + detail.HeadRef
                : detail.HeadRef ?? "?";
        }

        private void Fact(string caption, IReadOnlyCollection<string> values)
        {
            VisualElement row = new();
            row.AddToClassList(Pr + "__fact");
            Label name = new(caption);
            name.AddToClassList(Pr + "__fact-name");
            row.Add(name);
            Label value = new(values.Count == 0 ? GitText.Get("prDetail.none") : string.Join(", ", values));
            value.AddToClassList(Pr + "__fact-value");
            value.EnableInClassList(Pr + "__fact-value--empty", values.Count == 0);
            value.selection.isSelectable = true;
            row.Add(value);
            _facts.Add(row);
        }

        private void ShowFiles(List<GitPullRequestFile> files, int total, string error)
        {
            _files.Clear();
            _filesCaption.text = GitText.Format("prDetail.files", Math.Max(total, files.Count));
            foreach (GitPullRequestFile file in files)
            {
                VisualElement row = new();
                row.AddToClassList(Pr + "__file");
                Label status = new(StatusLetter(file.Status));
                status.AddToClassList(Pr + "__file-status");
                status.AddToClassList(Pr + "__file-status--" + (string.IsNullOrEmpty(file.Status) ? "modified" : file.Status));
                row.Add(status);
                Label path = new(file.PreviousPath == null ? file.Path : file.PreviousPath + " → " + file.Path);
                path.AddToClassList(Pr + "__file-path");
                path.AddToClassList("ellipsis");
                path.tooltip = file.Path;
                row.Add(path);
                Label counts = new($"+{file.Additions} −{file.Deletions}");
                counts.AddToClassList(Pr + "__file-counts");
                row.Add(counts);
                _files.Add(row);
            }
            if (string.IsNullOrEmpty(error) == false)
            {
                Label problem = Problem(_files);
                Show(problem, error);
            }
        }

        private static string StatusLetter(string status)
        {
            return status switch
            {
                "added" => "A",
                "removed" => "D",
                "renamed" => "R",
                "copied" => "C",
                _ => "M",
            };
        }

        private void SelectMethod(GitPullRequestMergeMethod method)
        {
            _method = method;
            foreach (Button button in _methodButtons)
            {
                button.EnableInClassList(Pr + "__method--selected", (GitPullRequestMergeMethod)button.userData == method);
            }
        }

        protected override void FocusFirstField()
        {
            _merge.Focus();
        }

        protected override void Validate()
        {
            GitPullRequestReadiness readiness = _detail == null ? GitPullRequestReadiness.Checking : GitPullRequestDetails.Readiness(_detail);
            bool open = _detail != null && _detail.IsOpen;
            _merge.SetEnabled(_merging == false && GitPullRequestDetails.CanMerge(readiness));
            _merge.text = GitText.Get(_merging ? "prDetail.merging" : "prDetail.merge");
            foreach (Button button in _methodButtons)
            {
                button.SetEnabled(open && _merging == false);
            }
            _viewChanges.SetEnabled(_openChanges != null);
            _checkout.SetEnabled(_openCheckout != null && Services.Runner.IsBusy == false);

            bool merged = _detail != null && _detail.IsMerged;
            bool idle = _detail != null && _posting == false && _merging == false;
            _draftToggle.text = GitText.Get(_detail != null && _detail.IsDraft ? "prDetail.readyForReview" : "prDetail.toDraft");
            _draftToggle.SetVisible(open);
            _draftToggle.SetEnabled(idle && string.IsNullOrEmpty(_detail?.NodeId) == false);
            _openToggle.text = GitText.Get(open ? "prDetail.close" : "prDetail.reopen");
            _openToggle.SetVisible(_detail != null && merged == false);
            _openToggle.SetEnabled(idle);
            _openToggle.EnableInClassList(Pr + "__danger", open);
            bool hasText = string.IsNullOrWhiteSpace(_reply.value) == false;
            _comment.SetEnabled(idle && hasText);
            // 승인·변경 요청은 열린 PR 에만 낸다. 승인은 글 없이 된다.
            _approve.SetEnabled(idle && open);
            _requestChanges.SetEnabled(idle && open && hasText);
        }

        private async Task MergeAsync()
        {
            GitPullRequestDetail detail = _detail;
            GitHubRemoteRepo repository = _repository;
            if (detail == null || repository == null || _merging)
            {
                return;
            }
            string method = GitText.Get("prDetail.method" + _method);
            bool confirmed = await Services.Dialogs.ConfirmAsync(GitText.Format("prDetail.confirmTitle", detail.Number),
                GitText.Format("prDetail.confirmMessage", detail.Number, detail.Title, HeadLabel(detail), detail.BaseRef, method, repository.FullName),
                GitText.Get("prDetail.merge"), icon: ActionGlyphKind.PullRequest);
            if (confirmed == false || ReferenceEquals(detail, _detail) == false)
            {
                return;
            }
            _merging = true;
            Validate();
            string error = await GitHubPullRequests.MergeAsync(repository, detail.Number, _method, detail.HeadSha);
            _merging = false;
            if (error != null)
            {
                Validate();
                await Services.Dialogs.AlertAsync(GitText.Format("prDetail.mergeFailed", detail.Number), error, ActionGlyphKind.PullRequest);
                return;
            }
            Merged?.Invoke();
            StateChanged?.Invoke();
            // 병합된 상태(Merged 배지·버튼 끔)로 다시 읽는다.
            Reset();
            Validate();
            LoadAfterOpen(_shownOpenCount);
        }
    }
}
