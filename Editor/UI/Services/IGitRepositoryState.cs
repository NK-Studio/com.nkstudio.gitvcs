using System;
using System.Collections.Generic;

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
        DateTime? LastFetchTime { get; }
        string UserName { get; }
        string UserEmail { get; }

        /// <summary>새 상태가 반영된 뒤 메인 스레드에서 온다.</summary>
        event Action Changed;

        void RequestRefresh(bool immediate = false);
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
        public DateTime? LastFetchTime => GitRepository.LastFetchTime;
        public string UserName => GitRepository.UserName;
        public string UserEmail => GitRepository.UserEmail;

        public event Action Changed
        {
            add => GitRepository.Changed += value;
            remove => GitRepository.Changed -= value;
        }

        public void RequestRefresh(bool immediate = false)
        {
            GitRepository.RequestRefresh(immediate);
        }
    }
}
