using System;
using System.Collections.Generic;
using System.Linq;
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

        /// <summary>사용자가 확인 팝업에서 취소해 git 을 부르지 않은 결과의 종료 코드.</summary>
        public const int CancelledExitCode = int.MinValue;

        /// <summary>사용자가 취소한 결과(실패로 알리지 않는다).</summary>
        public static GitResult Cancelled => new(CancelledExitCode, string.Empty, string.Empty);

        public bool Success => ExitCode == 0;

        public bool IsCancelled => ExitCode == CancelledExitCode;

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

        /// <summary>
        /// git 의 안내·오류 글을 영어로 받는다. 오류 해설(<see cref="GitErrorAdvice"/>)이 영어 문구로 원인을 알아보고,
        /// 사용자에게는 그 해설을 Git 창 언어로 보인다 — 운영체제 언어가 한국어여도 "알 수 없는 오류" 로 떨어지지 않게.
        /// 메시지만 바꾸고(LC_MESSAGES) 글자 인코딩(LC_CTYPE)은 그대로 둔다 — 한글 파일 이름이 깨지지 않게.
        /// </summary>
        private static void UseEnglishMessages(ProcessStartInfo startInfo)
        {
            string all = startInfo.EnvironmentVariables["LC_ALL"];
            if (string.IsNullOrEmpty(all) == false && string.IsNullOrEmpty(startInfo.EnvironmentVariables["LC_CTYPE"]))
            {
                startInfo.EnvironmentVariables["LC_CTYPE"] = all;
            }
            startInfo.EnvironmentVariables.Remove("LC_ALL");
            startInfo.EnvironmentVariables.Remove("LANGUAGE");
            startInfo.EnvironmentVariables["LC_MESSAGES"] = "C";
        }

        private static volatile string s_gitPath;
        private static int s_mainThreadId = -1;

        [UnityEditor.InitializeOnLoadMethod]
        private static void CaptureMainThread()
        {
            s_mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        /// <summary>
        /// 설정의 git 경로. 설정(ScriptableSingleton)은 메인 스레드에서만 읽고 값을 기억해 둔다 —
        /// 그래서 백그라운드 작업 안에서도 <see cref="RunAsync(string, IReadOnlyList{string}, int)"/> 를 이어 부를 수 있다
        /// (작업마다 메인 스레드로 돌아오면 입력 없는 에디터의 틱 간격(약 0.1초)만큼씩 늦어진다).
        /// </summary>
        private static string CurrentGitPath()
        {
            if (s_gitPath == null || Thread.CurrentThread.ManagedThreadId == s_mainThreadId)
            {
                s_gitPath = GitVcsSettings.instance.GitExecutable;
            }
            return s_gitPath;
        }

        public static Task<GitResult> RunAsync(string workingDirectory, IReadOnlyList<string> args, int timeoutMs = DefaultTimeoutMs)
        {
            string gitPath = CurrentGitPath();
            return Task.Run(() => Run(gitPath, workingDirectory, args, timeoutMs));
        }

        /// <param name="environment">더할 환경 변수(커밋을 다시 만들 때 원래 작성자·날짜를 지키는 <c>GIT_AUTHOR_*</c>).</param>
        public static Task<GitResult> RunAsync(string workingDirectory, IReadOnlyList<string> args, IReadOnlyDictionary<string, string> environment)
        {
            string gitPath = CurrentGitPath();
            return Task.Run(() => Run(gitPath, workingDirectory, args, DefaultTimeoutMs, environment));
        }

        /// <param name="input">표준 입력에 쓸 글(<c>git credential fill</c> 의 요청). null 이면 바로 닫는다.</param>
        /// <param name="applyTokenOverride">false 면 패키지 로그인 토큰(<see cref="GitAuth.TokenOverride"/>)을 붙이지 않는다 — 다른 계정의 자격 증명을 꺼내 볼 때.</param>
        public static GitResult Run(string gitPath, string workingDirectory, IReadOnlyList<string> args, int timeoutMs = DefaultTimeoutMs, IReadOnlyDictionary<string, string> environment = null, string input = null, bool applyTokenOverride = true)
        {
            GitLocator.Location git = GitLocator.Resolve(gitPath);
            if (git.Found == false)
            {
                // 그냥 "git" 으로 실행하면 macOS 에서는 Xcode 껍데기(/usr/bin/git)가 설치 창을 띄울 수 있다. 실행하지 않고 해결 방법을 돌려준다.
                return new GitResult(-1, string.Empty, git.Hint);
            }
            return RunProcess(git.Path, workingDirectory, args, timeoutMs, environment, input, applyTokenOverride);
        }

        /// <summary>
        /// git 이 아닌 도구(gh)를 git 과 같은 규칙(프롬프트 끔·PATH·시간 제한·stdout/stderr 동시 읽기)으로 실행한다. 패키지 로그인 토큰은 붙이지 않는다.
        /// </summary>
        public static GitResult RunTool(string executablePath, string workingDirectory, IReadOnlyList<string> args, int timeoutMs = DefaultTimeoutMs)
        {
            return RunProcess(executablePath, workingDirectory, args, timeoutMs, null, null, applyTokenOverride: false);
        }

        /// <summary><c>git stash push …</c> 인가(앞의 <c>-c</c> 같은 전역 옵션은 건너뛴다). 순수 함수라 테스트로 확인한다.</summary>
        public static bool IsStashPush(IReadOnlyList<string> args)
        {
            for (int i = 0; i + 1 < args.Count; i++)
            {
                if (args[i] == "stash")
                {
                    return args[i + 1] == "push";
                }
            }
            return false;
        }

        /// <summary>
        /// stash push 의 "--" 뒤 경로를 ":(literal)경로" 로 바꾼다(<c>GIT_LITERAL_PATHSPECS</c> 를 끈 채로도 [Ll]ibrary 같은 글자가 glob 이 되지 않게).
        /// </summary>
        public static IReadOnlyList<string> LiteralStashPaths(IReadOnlyList<string> args)
        {
            int separator = -1;
            for (int i = 0; i < args.Count; i++)
            {
                if (args[i] == "--")
                {
                    separator = i;
                    break;
                }
            }
            if (separator < 0)
            {
                return args;
            }
            return args.Select((arg, i) => i > separator && arg.StartsWith(":(", StringComparison.Ordinal) == false ? ":(literal)" + arg : arg).ToList();
        }

        private static int s_running;
        private static long s_hotUntil;

        /// <summary>얼마 동안 더 에디터 틱을 빠르게 둘지(git 이 끝난 직후, 결과를 받는 메인 스레드 이어 하기가 기다리지 않게).</summary>
        private static readonly long HotTailTicks = Stopwatch.Frequency * 3 / 10;

        /// <summary>
        /// git 이 돌고 있거나 막 끝났는가. Git 창이 그동안 스스로 다시 그려 에디터 틱을 빠르게 둔다 — 입력 없는 에디터는 틱이 약 0.1초 간격이라,
        /// 백그라운드 git 결과를 메인 스레드로 받을 때마다 그만큼 기다려 커밋·전환 뒤 화면이 늦게 바뀐다. 어느 스레드에서든 읽을 수 있다.
        /// </summary>
        /// <summary>지금 git 프로세스가 하나라도 돌고 있는가. 어느 스레드에서든 읽을 수 있다.</summary>
        public static bool IsRunning => Volatile.Read(ref s_running) > 0;

        public static bool IsBusyOrRecent => Volatile.Read(ref s_running) > 0 || Stopwatch.GetTimestamp() < Interlocked.Read(ref s_hotUntil);

        private static GitResult RunProcess(string executablePath, string workingDirectory, IReadOnlyList<string> args, int timeoutMs, IReadOnlyDictionary<string, string> environment, string input, bool applyTokenOverride)
        {
            Interlocked.Increment(ref s_running);
            try
            {
                return RunProcessCore(executablePath, workingDirectory, args, timeoutMs, environment, input, applyTokenOverride);
            }
            finally
            {
                Interlocked.Exchange(ref s_hotUntil, Stopwatch.GetTimestamp() + HotTailTicks);
                Interlocked.Decrement(ref s_running);
            }
        }

        private static GitResult RunProcessCore(string executablePath, string workingDirectory, IReadOnlyList<string> args, int timeoutMs, IReadOnlyDictionary<string, string> environment, string input, bool applyTokenOverride)
        {
            string gitPath = executablePath;
            // 이 저장소에 "패키지 로그인 쓰기" 를 골랐으면 그 토큰을 돌려주는 도우미를 앞에 붙인다. 토큰은 환경 변수로만 넘긴다(GitAuth).
            string tokenOverride = applyTokenOverride ? GitAuth.TokenOverride : null;
            bool stashPush = IsStashPush(args);
            if (stashPush)
            {
                args = LiteralStashPaths(args);
            }
            IReadOnlyList<string> finalArgs = string.IsNullOrEmpty(tokenOverride) ? args : GitAuth.TokenHelperArguments().Concat(args).ToList();
            ProcessStartInfo startInfo = new(executablePath)
            {
                Arguments = BuildArguments(finalArgs),
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
            // 단 stash push 는 끈다: --include-untracked 가 안에서 ":/" 같은 매직 경로로 새 파일을 지우는데, 글자 그대로 읽으면
            // 새 파일을 stash 에 넣기만 하고 작업 폴더에서 지우지 않는다. 그 대신 경로를 ":(literal)" 로 감싼다(LiteralStashPaths).
            startInfo.EnvironmentVariables["GIT_LITERAL_PATHSPECS"] = stashPush ? "0" : "1";
            // 에디터 PATH 에 git 이 없어서 시스템 PATH 로 찾은 경우에도 git-lfs·credential manager 를 찾게 한다.
            startInfo.EnvironmentVariables["PATH"] = GitLocator.SearchPath;
            UseEnglishMessages(startInfo);
            if (string.IsNullOrEmpty(tokenOverride) == false)
            {
                startInfo.EnvironmentVariables[GitAuth.TokenEnvironmentVariable] = tokenOverride;
            }
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
                if (input != null)
                {
                    process.StandardInput.Write(input);
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
                return new GitResult(-1, string.Empty, GitText.Format("command.notFound", executablePath, e.Message));
            }
        }

        /// <summary>
        /// stdout 을 글자로 바꾸지 않고 그대로 파일에 쓴다(<c>git show 리비전:경로</c> 로 그림·바이너리 원본을 꺼낼 때).
        /// <see cref="GitResult.Output"/> 은 비어 있다. LFS 원본을 내려받을 수도 있어 패키지 로그인 토큰을 <see cref="Run"/> 처럼 붙인다.
        /// </summary>
        /// <param name="inputPath">표준 입력으로 흘려 넣을 파일(<c>git lfs smudge</c> 에 주소 파일을 줄 때). null 이면 바로 닫는다.</param>
        public static Task<GitResult> RunToFileAsync(string workingDirectory, IReadOnlyList<string> args, string outputPath, int timeoutMs = DefaultTimeoutMs, string inputPath = null)
        {
            string gitPath = CurrentGitPath();
            string tokenOverride = GitAuth.TokenOverride;
            return Task.Run(() =>
            {
                GitLocator.Location git = GitLocator.Resolve(gitPath);
                if (git.Found == false)
                {
                    return new GitResult(-1, string.Empty, git.Hint);
                }
                IReadOnlyList<string> finalArgs = string.IsNullOrEmpty(tokenOverride) ? args : GitAuth.TokenHelperArguments().Concat(args).ToList();
                ProcessStartInfo startInfo = new(git.Path)
                {
                    Arguments = BuildArguments(finalArgs),
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
                UseEnglishMessages(startInfo);
                if (string.IsNullOrEmpty(tokenOverride) == false)
                {
                    startInfo.EnvironmentVariables[GitAuth.TokenEnvironmentVariable] = tokenOverride;
                }
                try
                {
                    using Process process = Process.Start(startInfo);
                    if (process == null)
                    {
                        return new GitResult(-1, string.Empty, GitText.Format("command.startFailed", gitPath));
                    }
                    if (inputPath != null)
                    {
                        using System.IO.FileStream input = System.IO.File.OpenRead(inputPath);
                        input.CopyTo(process.StandardInput.BaseStream);
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
