using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GitVersionControl
{
    public readonly struct GitResult
    {
        public readonly int ExitCode;
        public readonly string Output;
        public readonly string Error;

        public GitResult(int exitCode, string output, string error)
        {
            ExitCode = exitCode;
            Output = output;
            Error = error;
        }

        public bool Success => ExitCode == 0;

        /// <summary>
        /// 실패 시 사용자에게 보여 줄 한 덩어리 메시지. git 은 오류를 stderr 에, 일부 안내를 stdout 에 쓴다.
        /// </summary>
        public string Message => string.IsNullOrWhiteSpace(Error) ? Output.Trim() : Error.Trim();
    }

    /// <summary>
    /// 머신에 설치된 git CLI 를 실행한다.
    ///
    /// LibGit2Sharp 대신 CLI 를 쓰는 이유: 네이티브 바이너리 배포가 필요 없고, LFS·credential manager·hooks 가
    /// 사용자가 터미널에서 쓰는 것과 똑같이 동작한다.
    /// 모든 호출은 <see cref="RunAsync"/> 로 백그라운드 스레드에서 돌린다. 에디터 메인 스레드에서 프로세스를 기다리면
    /// 큰 저장소의 status 한 번에도 에디터가 멈춘다.
    /// </summary>
    public static class GitCommand
    {
        private const int DefaultTimeoutMs = 60_000;

        public static Task<GitResult> RunAsync(string workingDirectory, IReadOnlyList<string> args, int timeoutMs = DefaultTimeoutMs)
        {
            string gitPath = GitVcsSettings.instance.GitExecutable;
            return Task.Run(() => Run(gitPath, workingDirectory, args, timeoutMs));
        }

        /// <summary>
        /// 표준 입력을 넘기는 명령(<c>git credential fill</c> 등). <paramref name="environment"/> 는 이 실행에만 더할 환경 변수.
        /// </summary>
        public static Task<GitResult> RunWithInputAsync(string workingDirectory, IReadOnlyList<string> args, string standardInput, IReadOnlyDictionary<string, string> environment)
        {
            string gitPath = GitVcsSettings.instance.GitExecutable;
            return Task.Run(() => Run(gitPath, workingDirectory, args, DefaultTimeoutMs, standardInput, environment));
        }

        public static GitResult Run(string gitPath, string workingDirectory, IReadOnlyList<string> args, int timeoutMs = DefaultTimeoutMs,
            string standardInput = null, IReadOnlyDictionary<string, string> environment = null)
        {
            GitLocator.Location git = GitLocator.Resolve(gitPath);
            if (git.Found == false)
            {
                // 그냥 "git" 으로 실행하면 macOS 에서는 Xcode 껍데기(/usr/bin/git)가 설치 창을 띄울 수 있다. 실행하지 않고 해결 방법을 돌려준다.
                return new GitResult(-1, string.Empty, git.Hint);
            }
            ProcessStartInfo startInfo = new(git.Path)
            {
                Arguments = BuildArguments(args),
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            // 터미널이 없으니 사용자 이름·암호 프롬프트는 영원히 기다리게 된다. 실패로 돌려받고 메시지를 보여 준다.
            startInfo.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
            // 읽기 전용 명령(status 등)이 index.lock 을 잡아 사용자의 터미널 git 과 충돌하지 않게 한다.
            startInfo.EnvironmentVariables["GIT_OPTIONAL_LOCKS"] = "0";
            // Unity 경로의 [Ll]ibrary 같은 글자가 glob 으로 해석되지 않게 한다.
            startInfo.EnvironmentVariables["GIT_LITERAL_PATHSPECS"] = "1";
            // 에디터 PATH 에 git 이 없어서 시스템 PATH 로 찾은 경우에도 git-lfs·credential manager 를 찾게 한다.
            startInfo.EnvironmentVariables["PATH"] = GitLocator.SearchPath;
            if (environment != null)
            {
                foreach (KeyValuePair<string, string> pair in environment)
                {
                    startInfo.EnvironmentVariables[pair.Key] = pair.Value;
                }
            }

            try
            {
                using Process process = Process.Start(startInfo);
                if (process == null)
                {
                    return new GitResult(-1, string.Empty, GitText.Format("command.startFailed", gitPath));
                }
                if (standardInput != null)
                {
                    process.StandardInput.Write(standardInput);
                }
                process.StandardInput.Close();

                // stdout/stderr 를 동시에 비우지 않으면 한쪽 파이프 버퍼가 차서 git 이 멈춘다.
                // ReadToEndAsync 대신 전용 스레드에서 동기 ReadToEnd 를 쓴다: Mono 의 비동기 스트림 읽기는 continuation 을
                // Unity 메인 스레드 동기화 컨텍스트로 보낼 수 있어서, 메인 스레드가 이 작업을 기다리면 교착된다.
                Task<string> outputTask = Task.Factory.StartNew(() => process.StandardOutput.ReadToEnd(), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                Task<string> errorTask = Task.Factory.StartNew(() => process.StandardError.ReadToEnd(), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

                if (process.WaitForExit(timeoutMs) == false)
                {
                    try
                    {
                        process.Kill();
                    }
                    catch (InvalidOperationException)
                    {
                        // 그 사이에 끝났다.
                    }
                    return new GitResult(-1, string.Empty, GitText.Format("command.timeout", FirstArg(args), timeoutMs / 1000));
                }

                Task.WaitAll(outputTask, errorTask);
                return new GitResult(process.ExitCode, outputTask.Result, errorTask.Result);
            }
            catch (System.ComponentModel.Win32Exception e)
            {
                return new GitResult(-1, string.Empty, GitText.Format("command.notFound", git.Path, e.Message));
            }
        }

        /// <summary>
        /// stdout 을 글자로 바꾸지 않고 그대로 파일에 쓴다(<c>git show 리비전:경로</c> 로 그림·바이너리 원본을 꺼낼 때).
        /// <see cref="GitResult.Output"/> 은 비어 있다.
        /// </summary>
        public static Task<GitResult> RunToFileAsync(string workingDirectory, IReadOnlyList<string> args, string outputPath, int timeoutMs = DefaultTimeoutMs)
        {
            string gitPath = GitVcsSettings.instance.GitExecutable;
            return Task.Run(() =>
            {
                GitLocator.Location git = GitLocator.Resolve(gitPath);
                if (git.Found == false)
                {
                    return new GitResult(-1, string.Empty, git.Hint);
                }
                ProcessStartInfo startInfo = new(git.Path)
                {
                    Arguments = BuildArguments(args),
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardErrorEncoding = Encoding.UTF8,
                };
                startInfo.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
                startInfo.EnvironmentVariables["GIT_OPTIONAL_LOCKS"] = "0";
                startInfo.EnvironmentVariables["PATH"] = GitLocator.SearchPath;
                try
                {
                    using Process process = Process.Start(startInfo);
                    if (process == null)
                    {
                        return new GitResult(-1, string.Empty, GitText.Format("command.startFailed", gitPath));
                    }
                    process.StandardInput.Close();
                    // stdout 을 비우는 동안 stderr 파이프가 차서 멈추지 않도록 따로 읽는다(Run 과 같은 이유로 전용 스레드).
                    Task<string> errorTask = Task.Factory.StartNew(() => process.StandardError.ReadToEnd(), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                    using (System.IO.FileStream file = System.IO.File.Create(outputPath))
                    {
                        process.StandardOutput.BaseStream.CopyTo(file);
                    }
                    if (process.WaitForExit(timeoutMs) == false)
                    {
                        try
                        {
                            process.Kill();
                        }
                        catch (InvalidOperationException)
                        {
                            // 그 사이에 끝났다.
                        }
                        return new GitResult(-1, string.Empty, GitText.Format("command.timeout", FirstArg(args), timeoutMs / 1000));
                    }
                    errorTask.Wait();
                    return new GitResult(process.ExitCode, string.Empty, errorTask.Result);
                }
                catch (System.ComponentModel.Win32Exception e)
                {
                    return new GitResult(-1, string.Empty, GitText.Format("command.notFound", git.Path, e.Message));
                }
            });
        }

        /// <summary>
        /// 인자 목록을 Windows CommandLineToArgvW 규칙으로 이어 붙인다(.NET Standard 2.1 에는 ArgumentList 가 없다).
        /// macOS/Linux 의 Mono 도 같은 규칙으로 다시 쪼갠다.
        /// </summary>
        public static string BuildArguments(IReadOnlyList<string> args)
        {
            StringBuilder builder = new();
            for (int i = 0; i < args.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(' ');
                }
                AppendQuoted(builder, args[i]);
            }
            return builder.ToString();
        }

        private static void AppendQuoted(StringBuilder builder, string arg)
        {
            if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '\n', '"' }) < 0)
            {
                builder.Append(arg);
                return;
            }

            builder.Append('"');
            int backslashCount = 0;
            foreach (char c in arg)
            {
                if (c == '\\')
                {
                    backslashCount++;
                    continue;
                }
                if (c == '"')
                {
                    builder.Append('\\', backslashCount * 2 + 1);
                }
                else
                {
                    builder.Append('\\', backslashCount);
                }
                backslashCount = 0;
                builder.Append(c);
            }
            builder.Append('\\', backslashCount * 2);
            builder.Append('"');
        }

        private static string FirstArg(IReadOnlyList<string> args)
        {
            foreach (string arg in args)
            {
                if (arg.StartsWith("-") == false)
                {
                    return arg;
                }
            }
            return args.Count > 0 ? args[0] : string.Empty;
        }
    }
}
