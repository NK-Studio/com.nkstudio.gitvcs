using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

namespace GitVersionControl
{
    /// <summary>서브모듈 편집 서랍이 보이는 것: 이름·주소(.gitmodules)와 지금 체크아웃된 커밋.</summary>
    public sealed class GitSubmoduleDetail
    {
        /// <summary>.gitmodules 의 이름(<c>submodule.&lt;이름&gt;</c>). 경로와 다를 수 있다(경로를 옮긴 경우).</summary>
        public string Name;

        public string Url;

        /// <summary>지금 체크아웃된 커밋. 받지 않았으면 저장소에 기록된 커밋.</summary>
        public string Commit;

        /// <summary>커밋 시각과 메시지. 받지 않았으면 null.</summary>
        public DateTimeOffset? Date;

        public string Message;
    }

    /// <summary>
    /// 서브모듈 하나의 정보 읽기와 주소 바꾸기·지우기(GitKraken 의 Edit Submodule).
    /// </summary>
    public static class GitSubmodules
    {
        public static readonly string[] PathsCommand = { "config", "-f", ".gitmodules", "--get-regexp", @"^submodule\..*\.path$" };

        /// <summary>
        /// <c>git config -f .gitmodules --get-regexp ^submodule\..*\.path$</c> 출력("submodule.이름.path 경로" 줄들)에서 path 의 이름. 없으면 null.
        /// </summary>
        public static string NameForPath(string output, string path)
        {
            if (string.IsNullOrEmpty(output) || string.IsNullOrEmpty(path))
            {
                return null;
            }
            string wanted = path.Replace('\\', '/').TrimEnd('/');
            foreach (string rawLine in output.Split('\n'))
            {
                string line = rawLine.TrimEnd('\r');
                int space = line.IndexOf(' ');
                if (space < 0 || line.StartsWith("submodule.", StringComparison.Ordinal) == false)
                {
                    continue;
                }
                string key = line.Substring(0, space);
                string value = line.Substring(space + 1).Trim().Replace('\\', '/').TrimEnd('/');
                if (key.EndsWith(".path", StringComparison.Ordinal) && string.Equals(value, wanted, StringComparison.Ordinal))
                {
                    return key.Substring("submodule.".Length, key.Length - "submodule.".Length - ".path".Length);
                }
            }
            return null;
        }

        /// <summary><c>log -1 --format=%H%n%cI%n%B</c> 출력 → (해시, 시각, 메시지).</summary>
        public static (string hash, DateTimeOffset? date, string message) ParseHead(string output)
        {
            string[] lines = (output ?? string.Empty).Replace("\r", string.Empty).Split('\n');
            string hash = lines.Length > 0 ? lines[0].Trim() : string.Empty;
            DateTimeOffset? date = lines.Length > 1 && DateTimeOffset.TryParse(lines[1].Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset parsed) ? parsed : null;
            string message = lines.Length > 2 ? string.Join("\n", lines, 2, lines.Length - 2).Trim() : string.Empty;
            return (hash, date, message);
        }

        public static async Task<GitSubmoduleDetail> ReadAsync(GitSubmoduleRef submodule)
        {
            GitSubmoduleDetail detail = new() { Commit = submodule.Commit };
            GitResult paths = await GitRepository.RunAsync(PathsCommand);
            detail.Name = paths.Success ? NameForPath(paths.Output, submodule.Path) : null;
            if (detail.Name != null)
            {
                GitResult url = await GitRepository.RunAsync("config", "-f", ".gitmodules", "--get", "submodule." + detail.Name + ".url");
                detail.Url = url.Success ? url.Output.Trim() : null;
            }
            if (submodule.State != GitSubmoduleState.NotInitialized)
            {
                GitResult head = await GitCommand.RunAsync(FullPath(submodule.Path), new[] { "log", "-1", "--format=%H%n%cI%n%B" });
                if (head.Success)
                {
                    (string hash, DateTimeOffset? date, string message) = ParseHead(head.Output);
                    detail.Commit = string.IsNullOrEmpty(hash) ? detail.Commit : hash;
                    detail.Date = date;
                    detail.Message = message;
                }
            }
            return detail;
        }

        /// <summary>.gitmodules 의 주소를 바꾸고 서브모듈의 원격 설정도 맞춘다(<c>git submodule set-url</c>, git 2.25+).</summary>
        public static string[] SetUrl(string path, string url)
        {
            return new[] { "submodule", "set-url", "--", path, url };
        }

        /// <summary>
        /// 서브모듈을 저장소에서 뺀다: 작업 폴더에서 내리고(deinit), 인덱스·.gitmodules 에서 지우고(rm), .git/modules 의 사본을 지운다.
        /// 커밋해야 저장소 기록에 반영된다.
        /// </summary>
        public static async Task<GitResult> RemoveAsync(string path, string name)
        {
            GitResult deinit = await GitRepository.RunAsync("submodule", "deinit", "-f", "--", path);
            if (deinit.Success == false)
            {
                return deinit;
            }
            GitResult rm = await GitRepository.RunAsync("rm", "-f", "--", path);
            if (rm.Success == false)
            {
                return rm;
            }
            string modules = string.IsNullOrEmpty(GitRepository.GitDir) || string.IsNullOrEmpty(name) ? null : Path.Combine(GitRepository.GitDir, "modules", name);
            if (modules != null && Directory.Exists(modules))
            {
                try
                {
                    Directory.Delete(modules, recursive: true);
                }
                catch (Exception)
                {
                    // 남아도 저장소는 멀쩡하다(다시 같은 이름으로 추가할 때 git 이 재사용 여부를 묻는다).
                }
            }
            return rm;
        }

        public static string FullPath(string path)
        {
            return Path.Combine(GitRepository.RepoRoot ?? GitRepository.ProjectRoot, path).Replace('\\', '/');
        }
    }
}
