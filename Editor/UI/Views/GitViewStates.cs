using System;
using System.Collections.Generic;
using UnityEngine;

namespace GitVersionControl
{
    /// <summary>
    /// 도메인 리로드·창 다시 열기에도 남아야 하는 탭 상태. 창이 직렬화 필드로 들고 각 탭에 자기 몫을 넘긴다.
    /// git 에서 읽는 목록은 여기 두지 않는다(다시 읽으면 된다).
    /// </summary>
    [Serializable]
    public sealed class GitWindowState
    {
        public int SelectedTab;
        public PendingChangesState Pending = new();
        public ChangesetsState Changesets = new();
        public RefSidebarState Refs = new();
        public WorkspaceState Workspace = new();
        public BranchExplorerState Explorer = new();
        public StagingState Staging = new();
    }

    /// <summary>
    /// Fork·GitKraken 레이아웃의 커밋 입력란. 두 레이아웃이 같은 글을 이어 쓴다.
    /// </summary>
    [Serializable]
    public sealed class StagingState
    {
        public string Subject = string.Empty;
        public string Description = string.Empty;
    }

    [Serializable]
    public sealed class PendingChangesState
    {
        /// <summary>새 항목은 기본 체크이므로 체크 해제한 경로만 기억한다.</summary>
        public List<string> ExcludedPaths = new();
        public string CommitMessage = string.Empty;
    }

    [Serializable]
    public sealed class ChangesetsState
    {
        public bool AllBranches;
    }

    /// <summary>
    /// 사이드바 참조 트리. 펼침은 "기본과 다르게 바꾼 키" 만 기억한다(새 폴더·구역은 기본 상태로 나온다).
    /// </summary>
    [Serializable]
    public sealed class RefSidebarState
    {
        public List<string> Toggled = new();
        public string Filter = string.Empty;
    }

    /// <summary>
    /// 워크스페이스 탭(Gluon 식). 펼친 폴더는 저장소 기준 경로.
    /// </summary>
    [Serializable]
    public sealed class WorkspaceState
    {
        public List<string> Expanded = new();
        public bool ShowIgnored = true;
        public bool ChangedOnly;
        public string Filter = string.Empty;
    }

    [Serializable]
    public sealed class BranchExplorerState
    {
        public int RangeIndex = 1;
        public float Zoom = 1f;
        public Vector2 Pan;
        public string Search = string.Empty;
    }
}
