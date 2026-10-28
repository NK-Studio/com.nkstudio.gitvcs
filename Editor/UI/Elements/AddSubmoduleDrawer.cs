using System.IO;
using System.Linq;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 서브모듈 추가 서랍(GitKraken 의 Add Submodule). 사이드바 서브모듈 머리 줄의 + 로 연다.
    /// 원격 주소와 저장소 안 경로를 받아 <c>git submodule add</c> 한다. 경로는 주소의 마지막 이름으로 채운다(고치면 그대로 둔다).
    /// </summary>
    public sealed class AddSubmoduleDrawer : SideDrawer
    {
        private readonly TextField _url;
        private readonly TextField _path;
        private readonly Label _problem;
        private readonly Button _add;
        private bool _pathEdited;

        public AddSubmoduleDrawer(VisualElement host, GitServices services)
            : base(host, services, ActionGlyphKind.Submodule, GitText.Get("submodule.addTitle"))
        {
            VisualElement page = Section(Body);
            _url = Field(page, GitText.Get("submodule.url"), null);
            _url.RegisterValueChangedCallback(evt =>
            {
                if (_pathEdited == false)
                {
                    _path.SetValueWithoutNotify(GitSubmoduleForm.PathFromUrl(evt.newValue));
                }
                Validate();
            });
            _path = Field(page, GitText.Get("submodule.path"), GitText.Get("submodule.pathPlaceholder"));
            _path.RegisterValueChangedCallback(_ =>
            {
                _pathEdited = true;
                Validate();
            });
            _problem = Problem(page);
            _add = PrimaryButton(page, GitText.Get("submodule.add"), Add);
        }

        protected override void Reset()
        {
            _url.SetValueWithoutNotify(string.Empty);
            _path.SetValueWithoutNotify(string.Empty);
            _pathEdited = false;
        }

        protected override void FocusFirstField()
        {
            _url.Focus();
        }

        private static bool IsTaken(string relativePath)
        {
            string full = Path.Combine(GitRepository.RepoRoot ?? GitRepository.ProjectRoot, relativePath);
            return File.Exists(full) || (Directory.Exists(full) && Directory.EnumerateFileSystemEntries(full).Any());
        }

        protected override void Validate()
        {
            GitSubmoduleForm.PathProblem problem = GitSubmoduleForm.CheckPath(_path.value, IsTaken);
            Show(_problem, problem is GitSubmoduleForm.PathProblem.None or GitSubmoduleForm.PathProblem.Empty ? null : GitText.Get("submodule.problem" + problem));
            _add.SetEnabled(Services.Runner.IsBusy == false && string.IsNullOrWhiteSpace(_url.value) == false && problem == GitSubmoduleForm.PathProblem.None);
        }

        private async void Add()
        {
            if (Services.Runner.IsBusy)
            {
                return;
            }
            string url = _url.value.Trim();
            string path = _path.value.Trim().Replace('\\', '/');
            // 받은 파일이 Assets·Packages 아래면 Unity 가 바로 가져오도록 새로고침한다.
            GitResult result = await Services.Runner.RunAsync(GitText.Format("op.addSubmodule", path), () => Services.Commands.AddSubmoduleAsync(url, path), refreshAssets: true);
            if (result.Success)
            {
                Close();
            }
        }
    }
}
