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
        /// 이미지 diff 용으로 양쪽 판의 이미지를 텍스처로 읽는다. 받은 쪽이 <see cref="GitImagePair.Dispose"/> 로 텍스처를 지운다.
        /// LFS 원본이 없는 쪽은 텍스처 대신 <see cref="GitImageSide.LfsPointerSize"/> 가 찬다.
        /// </summary>
        /// <param name="fetchLfs">true 면 저장소가 받지 않게 막아 둔 LFS 원본(<c>lfs.fetchexclude</c>)도 이 파일의 두 판만 내려받는다.</param>
        Task<(GitImagePair pair, string error)> LoadImagesAsync(GitDiffTarget target, bool fetchLfs = false);

        /// <summary>
        /// 바이너리 파일 카드(Fork 처럼 "created · 242 KB")에 쓸 양쪽 판의 크기(바이트). 없는 쪽(추가·삭제)은 null.
        /// </summary>
        Task<(long? left, long? right, string error)> LoadSizesAsync(GitDiffTarget target);

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

        private const int DefaultExtractTimeoutMs = 60_000;

        /// <summary>
        /// LFS 원본 내려받기. 수백 MB 짜리 PSB 도 있어 넉넉히 둔다.
        /// </summary>
        private const int LfsFetchTimeoutMs = 10 * 60_000;

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

        public async Task<(GitImagePair pair, string error)> LoadImagesAsync(GitDiffTarget target, bool fetchLfs = false)
        {
            string directory = CreateTempDirectory();
            try
            {
                (string left, string right, string leftLabel, string rightLabel, string error) = await MaterializeAsync(target, directory, fetchLfs);
                if (error != null)
                {
                    return (null, error);
                }

                // 저장소가 받지 않게 막아 둔 경로는 원본을 이미 받아 뒀어도 주소 파일로 나온다. 받아 둔 원본이 있으면 그걸 쓴다.
                left = UseLocalLfsObject(left, directory, "left");
                right = UseLocalLfsObject(right, directory, "right");

                GitImagePair pair = new()
                {
                    Left = new GitImageSide { Label = leftLabel, Format = Path.GetExtension(target.LeftPath ?? target.RepoPath) },
                    Right = new GitImageSide { Label = rightLabel, Format = Path.GetExtension(target.RepoPath) },
                };
                string loadError = LoadSide(pair.Left, left) ?? LoadSide(pair.Right, right);
                if (loadError != null)
                {
                    pair.Dispose();
                    return (null, loadError);
                }
                return (pair, null);
            }
            finally
            {
                // 작업 사본 쪽은 실제 파일(이 폴더 밖)이라 지워지지 않는다.
                TryDeleteDirectory(directory);
            }
        }

        public async Task<(long? left, long? right, string error)> LoadSizesAsync(GitDiffTarget target)
        {
            // 이미지와 같은 길(MaterializeAsync)로 양쪽 판을 꺼내 크기만 잰다 — 어느 출처(작업 사본·Staged·커밋·범위)든 리비전 계산이 같다.
            string directory = CreateTempDirectory();
            try
            {
                (string left, string right, _, _, string error) = await MaterializeAsync(target, directory);
                if (error != null)
                {
                    return (null, null, error);
                }
                (bool hasLeft, bool hasRight) = SidesOf(target);
                return (hasLeft ? LengthOf(left) : null, hasRight ? LengthOf(right) : null, null);
            }
            finally
            {
                TryDeleteDirectory(directory);
            }
        }

        /// <summary>
        /// 양쪽 판이 있는지(추가·Untracked 면 왼쪽 없음, 삭제면 오른쪽 없음). <see cref="MaterializeAsync"/> 와 같은 규칙.
        /// </summary>
        private static (bool hasLeft, bool hasRight) SidesOf(GitDiffTarget target)
        {
            bool hasLeft = target.State != GitFileState.Added && target.State != GitFileState.Untracked;
            bool hasRight = target.State != GitFileState.Deleted;
            return (hasLeft, hasRight);
        }

        private static long? LengthOf(string path)
        {
            return string.IsNullOrEmpty(path) == false && File.Exists(path) ? new FileInfo(path).Length : null;
        }

        /// <summary>
        /// <paramref name="path"/> 가 LFS 주소 파일이고 원본이 .git/lfs 에 있으면 그 원본을 임시 폴더에 같은 확장자로 복사해 그 경로를 준다.
        /// 아니면 <paramref name="path"/> 그대로. 확장자를 지키는 이유: 형식(PSD 등)을 확장자로 판별한다.
        /// </summary>
        private static string UseLocalLfsObject(string path, string directory, string tag)
        {
            if (GitLfs.ReadPointer(path) is not (string oid, long size))
            {
                return path;
            }
            string local = GitLfs.LocalObjectPath(GitRepository.GitDir, oid, size);
            if (local == null)
            {
                return path;
            }
            string copy = Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(path)}.{tag}.lfs{Path.GetExtension(path)}");
            try
            {
                File.Copy(local, copy, true);
                return copy;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return path;
            }
        }

        private static string LoadSide(GitImageSide side, string path)
        {
            // LFS 원본을 받지 않은 파일은 주소 파일만 있다. 이미지로 읽으려다 "읽지 못함" 이 되지 않게 따로 표시한다.
            side.LfsPointerSize = GitLfs.ReadPointerSize(path);
            if (side.LfsPointerSize != null)
            {
                return null;
            }
            (Texture2D texture, int? layerCount, string error) = GitImageLoader.Load(path);
            side.Texture = texture;
            side.LayerCount = texture != null ? layerCount : null;
            side.ByteSize = texture != null ? new FileInfo(path).Length : 0;
            return error;
        }

        private static void TryDeleteDirectory(string directory)
        {
            try
            {
                Directory.Delete(directory, true);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // 다른 프로그램이 잡고 있으면 남겨 둔다. 프로젝트 Temp 아래라 에디터를 끄면 Unity 가 정리한다.
            }
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
                case GitDiffSource.Range:
                    args.AddRange(target.To == null ? new[] { "-M", target.From } : new[] { "-M", target.From, target.To });
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
                await _dialogs.AlertAsync(GitText.Get("diff.difftoolFailed"), string.IsNullOrWhiteSpace(result.Message) ? GitText.Get("diff.difftoolHint") : result.Message);
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
                if (await _dialogs.ConfirmAsync(GitText.Get("diff.toolNotFound"), GitText.Format("diff.toolNotFoundMessage", GitText.Get("diffEngine." + engine)), GitText.Get("common.openVcsSettings")))
                {
                    GitPreferences.Open();
                }
                return;
            }

            (string left, string right, string leftLabel, string rightLabel, string error) = await MaterializeAsync(target, CreateTempDirectory());
            if (error != null)
            {
                await _dialogs.AlertAsync(GitText.Get("diff.prepareFailed"), error);
                return;
            }
            string leftTitle = $"{target.LeftPath} ({leftLabel})";
            string rightTitle = $"{target.RepoPath} ({rightLabel})";

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
                await _dialogs.AlertAsync(GitText.Get("diff.toolStartFailed"), $"{executable}\n{e.Message}");
            }
        }

        /// <summary>
        /// 외부 프로그램에 넘길 두 파일을 만든다. 작업 트리 쪽은 실제 파일을 그대로 넘겨(도구에서 고치면 바로 반영된다),
        /// 리비전 쪽은 <c>git cat-file --filters</c> 로 꺼낸다(LFS·줄 끝 변환까지 거친 실제 내용). 없는 쪽(추가·삭제)은 빈 파일이다.
        /// 임시 파일은 <paramref name="directory"/>(프로젝트 Temp 아래)에 두어 에디터를 끄면 Unity 가 정리한다.
        /// </summary>
        /// <returns>두 파일 경로와 각 판 이름(커밋 앞 8자리·Staged·작업 사본·없음).</returns>
        /// <param name="fetchLfs">true 면 LFS 원본을 막아 둔 저장소 설정을 이번만 비워 두 판의 원본을 받는다. 작업 사본이 주소 파일이면 그 원본도 임시 파일로 받는다(작업 사본은 건드리지 않는다).</param>
        private async Task<(string left, string right, string leftLabel, string rightLabel, string error)> MaterializeAsync(GitDiffTarget target, string directory, bool fetchLfs = false)
        {
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
                case GitDiffSource.Range:
                    leftRevision = target.From;
                    rightRevision = target.To;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(target), target.Source, null);
            }

            string leftLabel = hasLeft ? LabelOf(leftRevision) : GitText.Get("diff.none");
            string left = Path.Combine(directory, $"{fileName}.{leftLabel}{extension}");
            string error = await ExtractAsync(hasLeft ? leftRevision : null, target.LeftPath, left, fetchLfs);
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
                else if (fetchLfs && GitLfs.ReadPointerSize(right) != null)
                {
                    string smudged = Path.Combine(directory, $"{fileName}.{rightLabel}{extension}");
                    error = await SmudgeAsync(right, target.RepoPath, smudged);
                    if (error != null)
                    {
                        return (null, null, null, null, error);
                    }
                    right = smudged;
                }
            }
            else
            {
                rightLabel = hasRight ? LabelOf(rightRevision) : GitText.Get("diff.none");
                right = Path.Combine(directory, $"{fileName}.{rightLabel}{extension}");
                error = await ExtractAsync(hasRight ? rightRevision : null, target.RepoPath, right, fetchLfs);
                if (error != null)
                {
                    return (null, null, null, null, error);
                }
            }
            return (left, right, leftLabel, rightLabel, null);
        }

        /// <param name="revision">null 이면 빈 파일을 만든다.</param>
        /// <param name="fetchLfs">true 면 받지 않게 막아 둔 LFS 원본도 이 판 하나만 내려받는다.</param>
        private static async Task<string> ExtractAsync(string revision, string repoPath, string outputPath, bool fetchLfs = false)
        {
            if (revision == null)
            {
                File.WriteAllBytes(outputPath, Array.Empty<byte>());
                return null;
            }
            List<string> args = new();
            if (fetchLfs)
            {
                args.AddRange(GitLfs.FetchAllArguments);
            }
            args.AddRange(new[] { "cat-file", "--filters", $"{revision}:{repoPath}" });
            GitResult result = await GitCommand.RunToFileAsync(GitRepository.RepoRoot, args, outputPath, fetchLfs ? LfsFetchTimeoutMs : DefaultExtractTimeoutMs);
            return result.Success ? null : result.Message;
        }

        /// <summary>
        /// 작업 사본에 남은 LFS 주소 파일을 원본으로 바꿔 <paramref name="outputPath"/> 에 쓴다(<c>git lfs smudge</c>). 작업 사본 파일은 그대로 둔다.
        /// </summary>
        private static async Task<string> SmudgeAsync(string pointerPath, string repoPath, string outputPath)
        {
            List<string> args = new(GitLfs.FetchAllArguments) { "lfs", "smudge", "--", repoPath };
            GitResult result = await GitCommand.RunToFileAsync(GitRepository.RepoRoot, args, outputPath, LfsFetchTimeoutMs, inputPath: pointerPath);
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
