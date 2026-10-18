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
        public BranchesState Branches = new();
        public BranchExplorerState Explorer = new();
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

    [Serializable]
    public sealed class BranchesState
    {
        public string NewBranchName = string.Empty;
        public bool SwitchAfterCreate = true;
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
