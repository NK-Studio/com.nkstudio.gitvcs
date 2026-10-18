using System.Collections.Generic;

namespace GitVersionControl
{
    /// <summary>
    /// 작업 중(커밋하지 않은) 변경의 종류별 개수. 세로 그래프 맨 위 작업 중 줄에 "~수정 +추가 −삭제" 로 보인다(GitKraken 과 같다).
    /// 추가에는 아직 git 이 모르는 새 파일(Untracked)도 넣고, 이름 변경·충돌은 수정으로 센다.
    /// </summary>
    public readonly struct WipCounts
    {
        public readonly int Modified;
        public readonly int Added;
        public readonly int Deleted;

        public WipCounts(int modified, int added, int deleted)
        {
            Modified = modified;
            Added = added;
            Deleted = deleted;
        }

        public int Total => Modified + Added + Deleted;

        public static WipCounts From(IEnumerable<GitStatusEntry> entries)
        {
            int modified = 0;
            int added = 0;
            int deleted = 0;
            foreach (GitStatusEntry entry in entries)
            {
                switch (entry.State)
                {
                    case GitFileState.Added:
                    case GitFileState.Untracked:
                        added++;
                        break;
                    case GitFileState.Deleted:
                        deleted++;
                        break;
                    default:
                        modified++;
                        break;
                }
            }
            return new WipCounts(modified, added, deleted);
        }
    }
}
