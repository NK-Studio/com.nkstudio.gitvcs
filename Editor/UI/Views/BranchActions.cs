using System.Collections.Generic;
using System.Threading.Tasks;

namespace GitVersionControl
{
    /// <summary>
    /// 브랜치 전환·생성·삭제의 "묻고 → 저장하고 → 실행하고 → 실패를 설명하는" 흐름. 뷰는 버튼을 이 클래스에 연결만 한다.
    ///
    /// 전환은 작업 트리 파일을 바꾸므로 먼저 씬·에셋을 저장하고, 끝나면 AssetDatabase 를 새로고침한다.
    /// 커밋하지 않은 변경은 git 이 충돌하지 않는 것만 들고 가고, 충돌하면 전환 자체를 거부한다(데이터 손실 없음).
    /// </summary>
    public sealed class BranchActions
    {
        private readonly GitServices _services;

        public BranchActions(GitServices services)
        {
            _services = services;
        }

        /// <summary>origin/feature/x → feature/x</summary>
        public static string LocalNameOf(GitBranchRef remote)
        {
            int slash = remote.Name.IndexOf('/');
            return slash < 0 ? remote.Name : remote.Name.Substring(slash + 1);
        }

        /// <summary>원격 브랜치를 골랐는데 같은 이름의 로컬 브랜치가 이미 있으면, 새로 만들지 않고 그 로컬 브랜치로 간다.</summary>
        public static GitBranchRef ResolveSwitchTarget(GitBranchRef branch, ICollection<string> localNames)
        {
            if (branch.IsRemote && localNames.Contains(LocalNameOf(branch)))
            {
                return new GitBranchRef { Name = LocalNameOf(branch), IsRemote = false };
            }
            return branch;
        }

        public async Task SwitchAsync(GitBranchRef branch, ICollection<string> localNames)
        {
            GitBranchRef target = ResolveSwitchTarget(branch, localNames);
            if (PrepareWorkingTree(GitText.Format("branches.actionSwitch", target.Name)) == false)
            {
                return;
            }
            await _services.Runner.RunAsync(GitText.Format("op.switch", target.Name), () => _services.Commands.SwitchAsync(target), refreshAssets: true);
        }

        /// <returns>만들었으면 true.</returns>
        public async Task<bool> CreateAsync(string name, bool switchTo)
        {
            GitResult valid = await _services.Commands.ValidateBranchNameAsync(name);
            if (valid.Success == false)
            {
                _services.Dialogs.Alert(GitText.Get("branches.createTitle"), GitText.Format("branches.invalidName", name));
                return false;
            }
            if (switchTo && PrepareWorkingTree(GitText.Format("branches.actionCreateSwitch", name)) == false)
            {
                return false;
            }
            GitResult result = await _services.Runner.RunAsync(GitText.Format("op.createBranch", name), () => _services.Commands.CreateBranchAsync(name, switchTo), refreshAssets: false);
            return result.Success;
        }

        public async Task DeleteAsync(GitBranchRef branch)
        {
            if (_services.Dialogs.Confirm(GitText.Get("branches.deleteTitle"), GitText.Format("branches.deleteConfirm", branch.Name), GitText.Get("common.delete")) == false)
            {
                return;
            }

            GitResult result = await _services.Runner.RunAsync(GitText.Format("op.deleteBranch", branch.Name), () => _services.Commands.DeleteBranchAsync(branch.Name, force: false),
                refreshAssets: false, showErrorDialog: false);
            if (result.Success)
            {
                return;
            }
            // -d 는 다른 브랜치에 병합되지 않은 커밋이 있으면 거부한다. 그 커밋을 버려도 되는지 한 번 더 묻는다.
            if (result.Message.Contains("not fully merged"))
            {
                if (_services.Dialogs.Confirm(GitText.Get("branches.deleteTitle"), GitText.Format("branches.notMerged", branch.Name), GitText.Get("branches.forceDelete")))
                {
                    await _services.Runner.RunAsync(GitText.Format("op.forceDeleteBranch", branch.Name), () => _services.Commands.DeleteBranchAsync(branch.Name, force: true), refreshAssets: false);
                }
                return;
            }
            _services.Dialogs.Alert(GitText.Get("branches.deleteFailed"), result.Message);
        }

        /// <summary>작업 트리가 바뀌기 전에 열린 씬·에셋을 저장하고, 커밋하지 않은 변경이 있으면 알린다. 취소하면 false.</summary>
        private bool PrepareWorkingTree(string action)
        {
            if (_services.Dialogs.SaveOpenWork() == false)
            {
                return false;
            }
            int pending = _services.Repository.Entries.Count;
            if (pending == 0)
            {
                return true;
            }
            return _services.Dialogs.Confirm(GitText.Get("branches.switchTitle"),
                GitText.Format("branches.pendingWarning", pending, action), GitText.Get("common.continue"));
        }
    }
}
