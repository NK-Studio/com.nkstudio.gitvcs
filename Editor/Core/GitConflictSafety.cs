using System;
using System.Collections.Generic;
using System.Linq;

namespace GitVersionControl
{
    /// <summary>충돌 중에 Git 창을 잃을 수 있는 위험(<see cref="GitConflictSafety.Check"/>).</summary>
    public enum GitConflictRisk
    {
        None,

        /// <summary>
        /// 충돌을 푸는 중인데 스크립트 컴파일 오류가 있다. 지금은 이미 불러온 패키지 코드로 Git 창이 돌지만,
        /// 이대로 Unity 를 끄면 다음에 Safe Mode 로 열려 패키지 에디터 코드가 실행되지 않는다(Git 창을 쓸 수 없다).
        /// </summary>
        CompileErrors,

        /// <summary>
        /// Packages/manifest.json(또는 packages-lock.json)이 충돌했다. 충돌 표시로 JSON 이 깨져 Package Manager 가 패키지 목록을 읽지 못하고,
        /// Unity 가 패키지를 다시 읽으면 이 패키지도 빠질 수 있다. 다른 충돌보다 먼저 풀어야 한다.
        /// </summary>
        PackageManifest,
    }

    /// <summary>
    /// 충돌을 푸는 동안 Git 창 자체를 잃게 되는 상황을 알아본다. Unity 종료 확인(GitQuitGuard)과 충돌 목록 정렬이 쓴다.
    /// </summary>
    public static class GitConflictSafety
    {
        private static readonly string[] ManifestFiles = { "Packages/manifest.json", "Packages/packages-lock.json" };

        /// <summary>저장소 경로가 Unity 프로젝트의 패키지 목록 파일인지(프로젝트가 저장소 하위 폴더여도 끝부분으로 본다).</summary>
        public static bool IsPackageManifest(string repoPath)
        {
            if (string.IsNullOrEmpty(repoPath))
            {
                return false;
            }
            string path = repoPath.Replace('\\', '/');
            return ManifestFiles.Any(file => path == file || path.EndsWith("/" + file, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 지금 가장 급한 위험. 패키지 목록 충돌이 컴파일 오류보다 먼저다 — 패키지가 빠지면 Unity 를 켜 둔 채로도 Git 창을 잃는다.
        /// 컴파일 오류는 충돌이 있거나 작업(병합 등)이 멈춰 있을 때만 위험으로 본다. 그 밖의 컴파일 오류는 이 패키지가 다룰 일이 아니다.
        /// </summary>
        public static GitConflictRisk Check(IEnumerable<GitStatusEntry> entries, bool operationInProgress, bool compileFailed)
        {
            List<GitStatusEntry> conflicts = (entries ?? Enumerable.Empty<GitStatusEntry>()).Where(e => e.State == GitFileState.Conflicted).ToList();
            if (conflicts.Any(e => IsPackageManifest(e.RepoPath)))
            {
                return GitConflictRisk.PackageManifest;
            }
            if (compileFailed && (conflicts.Count > 0 || operationInProgress))
            {
                return GitConflictRisk.CompileErrors;
            }
            return GitConflictRisk.None;
        }

        /// <summary>충돌 목록 순서: 패키지 목록 파일을 맨 위로, 나머지는 그대로.</summary>
        public static List<GitStatusEntry> ManifestFirst(IEnumerable<GitStatusEntry> conflicts)
        {
            return conflicts.OrderBy(e => IsPackageManifest(e.RepoPath) ? 0 : 1).ToList();
        }
    }
}
