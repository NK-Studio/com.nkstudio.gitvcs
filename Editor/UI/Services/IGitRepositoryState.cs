using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GitVersionControl
{
    /// <summary>
    /// 뷰가 읽는 저장소 상태. 뷰는 정적 <see cref="GitRepository"/> 대신 이 인터페이스에 의존한다(DIP) —
    /// 테스트에서는 가짜 상태를 넣을 수 있고, 캐시 구현이 바뀌어도 뷰는 그대로다.
    /// </summary>
    public interface IGitRepositoryState
    {
        bool IsRunning { get; }
        bool IsRepository { get; }
        bool IsRefreshing { get; }
        string LastError { get; }
        GitBranchInfo Branch { get; }
        IReadOnlyList<GitStatusEntry> Entries { get; }
        string HeadCommit { get; }
        string UpstreamCommit { get; }

        /// <summary>등록된 원격이 하나라도 있는지.</summary>
        bool HasRemote { get; }

        /// <summary>모든 참조의 지문. 브랜치·태그·stash 가 움직이면 바뀐다.</summary>
        string RefsFingerprint { get; }
        DateTime? LastFetchTime { get; }
        string UserName { get; }
        string UserEmail { get; }

        /// <summary>충돌 등으로 멈춘 채 진행 중인 작업(<see cref="GitRepository.InProgressOperation"/>).</summary>
        GitOperationKind InProgressOperation { get; }

        /// <summary>멈춘 병합·cherry-pick·revert 에 git 이 준비해 둔 커밋 메시지(<see cref="GitInProgress.ReadPreparedMessage"/>). 없으면 null.</summary>
        string PreparedCommitMessage { get; }

        /// <summary>새 상태가 반영된 뒤 메인 스레드에서 온다.</summary>
        event Action Changed;

        /// <summary>마지막 새로고침이 작업 트리만 읽었는지(<see cref="GitRepository.LastRefreshWasWorkingTreeOnly"/>).</summary>
        bool LastRefreshWasWorkingTreeOnly { get; }

        /// <param name="workingTreeOnly">HEAD·브랜치가 바뀌지 않는 작업 뒤면 true — git status 하나만 돌린다.</param>
        void RequestRefresh(bool immediate = false, bool workingTreeOnly = false);

        /// <summary>지금까지 요청된 새로고침이 반영되면 끝난다(<see cref="GitRepository.WaitForRefreshAsync"/>).</summary>
        Task WaitForRefreshAsync();
    }

    /// <summary><see cref="GitRepository"/> 를 <see cref="IGitRepositoryState"/> 로 감싼 어댑터.</summary>
    public sealed class GitRepositoryState : IGitRepositoryState
    {
        public bool IsRunning => GitRepository.IsRunning;
        public bool IsRepository => GitRepository.IsRepository;
        public bool IsRefreshing => GitRepository.IsRefreshing;
        public string LastError => GitRepository.LastError;
        public GitBranchInfo Branch => GitRepository.Branch;
        public IReadOnlyList<GitStatusEntry> Entries => GitRepository.Entries;
        public string HeadCommit => GitRepository.HeadCommit;
        public string UpstreamCommit => GitRepository.UpstreamCommit;
        public bool HasRemote => GitRepository.HasRemote;
        public string RefsFingerprint => GitRepository.RefsFingerprint;
        public DateTime? LastFetchTime => GitRepository.LastFetchTime;
        public string UserName => GitRepository.UserName;
        public string UserEmail => GitRepository.UserEmail;
        public GitOperationKind InProgressOperation => GitRepository.InProgressOperation;
        public string PreparedCommitMessage => GitInProgress.ReadPreparedMessage(GitRepository.GitDir);
        public bool LastRefreshWasWorkingTreeOnly => GitRepository.LastRefreshWasWorkingTreeOnly;

        public event Action Changed
        {
            add => GitRepository.Changed += value;
            remove => GitRepository.Changed -= value;
        }

        public void RequestRefresh(bool immediate = false, bool workingTreeOnly = false)
        {
            GitRepository.RequestRefresh(immediate, workingTreeOnly);
        }

        public Task WaitForRefreshAsync()
        {
            return GitRepository.WaitForRefreshAsync();
        }
    }
}
