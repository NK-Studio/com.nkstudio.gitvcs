using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace GitVersionControl
{
    /// <summary>
    /// 파일 diff 열기. 설정의 엔진(<see cref="GitVcsSettings.DiffEngine"/>)에 따라 내장 뷰어·git difftool·외부 프로그램 중 하나로 연다.
    /// </summary>
    public interface IGitDiffService
    {
        void Open(GitDiffTarget target);

        /// <summary>
        /// 내장 뷰어가 쓰는 diff 를 읽는다. 실패하면 error 에 이유가 온다.
        /// </summary>
        Task<(GitDiffDocument document, string error)> LoadAsync(GitDiffTarget target, GitDiffOptions options);

        /// <summary>
        /// 엔진과 상관없이 외부 프로그램(설정의 외부 엔진, 없으면 git difftool)으로 연다 — 내장 뷰어의 "외부 도구로 열기".
        /// </summary>
        void OpenExternally(GitDiffTarget target, DiffEngine engine);
    }

    public sealed class GitDiffService : IGitDiffService
    {
        /// <summary>
        /// difftool·외부 프로그램은 사용자가 창을 닫을 때까지 돌아온다. 몇 시간 띄워 둘 수도 있어 넉넉히 둔다.
        /// </summary>
        private const int DifftoolTimeoutMs = 8 * 60 * 60_000;

        /// <summary>
        /// 아무 내용도 없는 git 트리. 최초 커밋의 "부모"로 쓴다(git 이 모든 저장소에 대해 같은 해시로 알고 있다).
        /// </summary>
        private const string EmptyTreeHash = "4b825dc642cb6eb9a060e54bf8d69288fbee4904";

        private readonly IDialogService _dialogs;

        public GitDiffService(IDialogService dialogs)
        {
            _dialogs = dialogs;
        }

        public void Open(GitDiffTarget target)
        {
            DiffEngine engine = GitVcsSettings.instance.DiffEngine;
            if (engine == DiffEngine.BuiltIn)
            {
                GitDiffWindow.Open(target, this);
                return;
            }
            OpenExternally(target, engine);
        }

        public void OpenExternally(GitDiffTarget target, DiffEngine engine)
        {
            if (ExternalDiffTool.IsExternal(engine) == false)
            {
                _ = RunDifftoolAsync(target);
            }
            else
            {
                _ = OpenWithToolAsync(target, engine);
            }
        }

        public async Task<(GitDiffDocument document, string error)> LoadAsync(GitDiffTarget target, GitDiffOptions options)
        {
            GitResult result = await GitRepository.RunAsync(GitDiff.BuildArguments(target, options).ToArray());
            // --no-index 는 차이가 있으면 1 로 끝난다(오류가 아니다).
            bool isNoIndex = (target.Source == GitDiffSource.WorkingTree || target.Source == GitDiffSource.Unstaged) && target.State == GitFileState.Untracked;
            bool succeeded = result.Success || (isNoIndex && result.ExitCode == 1);
            if (succeeded == false)
            {
                return (null, result.Message);
            }
            return (GitDiff.Parse(result.Output), null);
        }

        private async Task RunDifftoolAsync(GitDiffTarget target)
        {
            List<string> args = new() { "difftool", "--no-prompt" };
            string tool = GitVcsSettings.instance.DifftoolName;
            if (string.IsNullOrEmpty(tool) == false)
            {
                args.Add("--tool=" + tool);
            }

            string tempDirectory = null;
            switch (target.Source)
            {
                case GitDiffSource.WorkingTree when target.State == GitFileState.Untracked:
                case GitDiffSource.Unstaged when target.State == GitFileState.Untracked:
                    tempDirectory = CreateTempDirectory();
                    string empty = Path.Combine(tempDirectory, "empty");
                    File.WriteAllBytes(empty, Array.Empty<byte>());
                    args.AddRange(new[] { "--no-index", "--", empty, target.RepoPath });
                    break;
                case GitDiffSource.WorkingTree:
                    args.AddRange(new[] { "-M", "HEAD" });
                    break;
                case GitDiffSource.Staged:
                    args.AddRange(new[] { "-M", "--cached" });
                    break;
                case GitDiffSource.Unstaged:
                    args.Add("-M");
                    break;
                case GitDiffSource.Commit:
                    args.AddRange(new[] { "-M", await ParentOrEmptyTreeAsync(target.Commit), target.Commit });
                    break;
                case GitDiffSource.MergeBaseRange:
                    args.AddRange(new[] { "-M", $"{target.From}...{target.To}" });
                    break;
            }
            if (args.Contains("--no-index") == false)
            {
                args.Add("--");
                if (target.OriginalRepoPath != null)
                {
                    args.Add(target.OriginalRepoPath);
                }
                args.Add(target.RepoPath);
            }

            GitResult result = await GitCommand.RunAsync(GitRepository.RepoRoot, args, DifftoolTimeoutMs);
            // --no-index 는 차이가 있으면 1 로 끝난다.
            if (result.Success == false && (tempDirectory == null || result.ExitCode != 1))
            {
                _dialogs.Alert(GitText.Get("diff.difftoolFailed"), string.IsNullOrWhiteSpace(result.Message) ? GitText.Get("diff.difftoolHint") : result.Message);
            }
        }

        private async Task OpenWithToolAsync(GitDiffTarget target, DiffEngine engine)
        {
            GitVcsSettings settings = GitVcsSettings.instance;
            string executable = settings.GetDiffToolPath(engine);
            if (string.IsNullOrEmpty(executable))
            {
                executable = engine == DiffEngine.Custom ? null : ExternalDiffTool.FindExecutable(engine);
            }
            if (string.IsNullOrEmpty(executable))
            {
                if (_dialogs.Confirm(GitText.Get("diff.toolNotFound"), GitText.Format("diff.toolNotFoundMessage", GitText.Get("diffEngine." + engine)), GitText.Get("common.openVcsSettings")))
                {
                    SettingsService.OpenProjectSettings("Project/Version Control");
                }
                return;
            }

            (string left, string right, string leftTitle, string rightTitle, string error) = await MaterializeAsync(target);
            if (error != null)
            {
                _dialogs.Alert(GitText.Get("diff.prepareFailed"), error);
                return;
            }

            string template = engine == DiffEngine.Custom ? settings.CustomDiffArguments : ExternalDiffTool.ArgumentTemplate(engine, Application.platform);
            string arguments = ExternalDiffTool.Expand(template, left, right, leftTitle, rightTitle);
            try
            {
                ProcessStartInfo startInfo = new(executable, arguments)
                {
                    UseShellExecute = false,
                    WorkingDirectory = GitRepository.RepoRoot,
                };
                Process.Start(startInfo)?.Dispose();
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception || e is InvalidOperationException)
            {
                _dialogs.Alert(GitText.Get("diff.toolStartFailed"), $"{executable}\n{e.Message}");
            }
        }

        /// <summary>
        /// 외부 프로그램에 넘길 두 파일을 만든다. 작업 트리 쪽은 실제 파일을 그대로 넘겨(도구에서 고치면 바로 반영된다),
        /// 리비전 쪽은 <c>git cat-file --filters</c> 로 꺼낸다(LFS·줄 끝 변환까지 거친 실제 내용). 없는 쪽(추가·삭제)은 빈 파일이다.
        /// 임시 파일은 프로젝트 Temp 아래에 두어 에디터를 끄면 Unity 가 정리한다.
        /// </summary>
        private async Task<(string left, string right, string leftTitle, string rightTitle, string error)> MaterializeAsync(GitDiffTarget target)
        {
            string directory = CreateTempDirectory();
            string fileName = Path.GetFileNameWithoutExtension(target.RepoPath);
            string extension = Path.GetExtension(target.RepoPath);
            bool hasLeft = target.State != GitFileState.Added && target.State != GitFileState.Untracked;
            bool hasRight = target.State != GitFileState.Deleted;

            string leftRevision;
            string rightRevision;
            switch (target.Source)
            {
                case GitDiffSource.WorkingTree:
                    leftRevision = "HEAD";
                    rightRevision = null;
                    break;
                case GitDiffSource.Staged:
                    // ":경로" 는 인덱스(stage 0)의 내용이다. 리비전 자리에 빈 문자열을 두면 ExtractAsync 가 ":경로" 를 만든다.
                    leftRevision = "HEAD";
                    rightRevision = string.Empty;
                    break;
                case GitDiffSource.Unstaged:
                    leftRevision = string.Empty;
                    hasLeft &= target.State != GitFileState.Untracked;
                    rightRevision = null;
                    break;
                case GitDiffSource.Commit:
                    leftRevision = await ParentOrEmptyTreeAsync(target.Commit);
                    hasLeft &= leftRevision != EmptyTreeHash;
                    rightRevision = target.Commit;
                    break;
                case GitDiffSource.MergeBaseRange:
                    GitResult mergeBase = await GitRepository.RunAsync("merge-base", target.From, target.To);
                    if (mergeBase.Success == false)
                    {
                        return (null, null, null, null, mergeBase.Message);
                    }
                    leftRevision = mergeBase.Output.Trim();
                    rightRevision = target.To;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(target), target.Source, null);
            }

            string leftLabel = hasLeft ? LabelOf(leftRevision) : GitText.Get("diff.none");
            string left = Path.Combine(directory, $"{fileName}.{leftLabel}{extension}");
            string error = await ExtractAsync(hasLeft ? leftRevision : null, target.LeftPath, left);
            if (error != null)
            {
                return (null, null, null, null, error);
            }

            string right;
            string rightLabel;
            if (rightRevision == null)
            {
                rightLabel = GitText.Get("diff.workingCopy");
                right = Path.GetFullPath(Path.Combine(GitRepository.RepoRoot, target.RepoPath));
                if (hasRight == false)
                {
                    right = Path.Combine(directory, $"{fileName}.deleted{extension}");
                    File.WriteAllBytes(right, Array.Empty<byte>());
                }
            }
            else
            {
                rightLabel = hasRight ? LabelOf(rightRevision) : GitText.Get("diff.none");
                right = Path.Combine(directory, $"{fileName}.{rightLabel}{extension}");
                error = await ExtractAsync(hasRight ? rightRevision : null, target.RepoPath, right);
                if (error != null)
                {
                    return (null, null, null, null, error);
                }
            }
            return (left, right, $"{target.LeftPath} ({leftLabel})", $"{target.RepoPath} ({rightLabel})", null);
        }

        /// <param name="revision">null 이면 빈 파일을 만든다.</param>
        private static async Task<string> ExtractAsync(string revision, string repoPath, string outputPath)
        {
            if (revision == null)
            {
                File.WriteAllBytes(outputPath, Array.Empty<byte>());
                return null;
            }
            GitResult result = await GitCommand.RunToFileAsync(GitRepository.RepoRoot, new[] { "cat-file", "--filters", $"{revision}:{repoPath}" }, outputPath);
            return result.Success ? null : result.Message;
        }

        /// <summary>
        /// 커밋의 첫 부모. 최초 커밋이면 빈 트리.
        /// </summary>
        private static async Task<string> ParentOrEmptyTreeAsync(string commit)
        {
            GitResult parent = await GitRepository.RunAsync("rev-parse", "--verify", "--quiet", commit + "^1");
            return parent.Success ? parent.Output.Trim() : EmptyTreeHash;
        }

        /// <summary>
        /// 창 제목·파일 이름에 쓸 판 이름. 인덱스(빈 리비전)는 "Staged".
        /// </summary>
        private static string LabelOf(string revision)
        {
            return revision.Length == 0 ? GitText.Get("diff.index") : ShortRevision(revision);
        }

        private static string ShortRevision(string revision)
        {
            return revision.Length > 10 && revision != "HEAD" ? revision.Substring(0, 8) : revision;
        }

        private static string CreateTempDirectory()
        {
            string directory = Path.GetFullPath(Path.Combine(GitRepository.ProjectRoot, "Temp", "GitVcsDiff", Guid.NewGuid().ToString("N").Substring(0, 8)));
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
