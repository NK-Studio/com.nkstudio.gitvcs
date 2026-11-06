using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace GitVersionControl
{
    /// <summary>
    /// git 이 <c>--progress</c> 로 stderr 에 쓰는 진행 한 줄("Receiving objects:  45% (1234/2700), 1.20 MiB | 2.00 MiB/s").
    /// </summary>
    public readonly struct GitProgressLine
    {
        /// <summary>원격 서버가 알린 단계("remote: Counting objects: …").</summary>
        public readonly bool Remote;

        /// <summary>단계 이름(영어 그대로): "Receiving objects", "Resolving deltas", "Writing objects" 등.</summary>
        public readonly string Phase;

        /// <summary>0~100. 모르면 -1.</summary>
        public readonly int Percent;

        /// <summary>지금까지 수. 모르면 -1.</summary>
        public readonly long Current;

        /// <summary>전체 수. 모르면 -1.</summary>
        public readonly long Total;

        /// <summary>쉼표 뒤 덧붙임(받은 양·속도). 없으면 빈 글.</summary>
        public readonly string Detail;

        public readonly bool Done;

        public GitProgressLine(bool remote, string phase, int percent, long current, long total, string detail, bool done)
        {
            Remote = remote;
            Phase = phase;
            Percent = percent;
            Current = current;
            Total = total;
            Detail = detail ?? string.Empty;
            Done = done;
        }

        public bool HasPercent => Percent >= 0;
    }

    /// <summary>
    /// 오래 걸리는 원격 작업(Pull·Push·Fetch)의 진행. git 을 <c>--progress</c> 로 부르고(<see cref="WithProgress"/>),
    /// stderr 를 흘려 읽으며 진행 줄은 여기 남기고 나머지만 오류 글로 돌려준다(<see cref="Drain"/>).
    /// 화면(진행 카드)은 <see cref="TryGetLatest"/> 를 짧은 간격으로 읽는다 — git 은 작업 스레드에서 돌아 이벤트를 메인 스레드로 넘기기보다 읽어 가는 편이 안전하다.
    /// </summary>
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public static class GitProgress
    {
        private sealed class Snapshot
        {
            public GitProgressLine Line;
            public long Sequence;
        }

        private static Snapshot s_latest;
        private static long s_sequence;

        /// <summary>
        /// 마지막 진행 줄과 그 번호(새 줄이 올 때마다 커진다). 아직 없으면 false. 어느 스레드에서든 읽을 수 있다.
        /// </summary>
        public static bool TryGetLatest(out GitProgressLine line, out long sequence)
        {
            Snapshot snapshot = Volatile.Read(ref s_latest);
            if (snapshot == null)
            {
                line = default;
                sequence = 0;
                return false;
            }
            line = snapshot.Line;
            sequence = snapshot.Sequence;
            return true;
        }

        /// <summary>지금까지의 진행 번호. 작업을 시작할 때 읽어 두면 그 뒤에 온 줄만 골라 볼 수 있다.</summary>
        public static long Sequence => Interlocked.Read(ref s_sequence);

        private static void Publish(GitProgressLine line)
        {
            long sequence = Interlocked.Increment(ref s_sequence);
            Volatile.Write(ref s_latest, new Snapshot { Line = line, Sequence = sequence });
        }

        private static readonly HashSet<string> NetworkCommands = new() { "pull", "push", "fetch", "clone" };

        /// <summary>
        /// 원격 명령(pull·push·fetch·clone)이면 하위 명령 바로 뒤에 <c>--progress</c> 를 넣는다. 조용히 하라는 명령(<c>--quiet</c>·<c>-q</c>, 자동 fetch)은 그대로.
        /// 앞의 <c>-c</c> 같은 전역 옵션은 건너뛴다. 순수 함수라 테스트로 확인한다.
        /// </summary>
        public static IReadOnlyList<string> WithProgress(IReadOnlyList<string> args)
        {
            int command = -1;
            for (int i = 0; i < args.Count; i++)
            {
                if (args[i] == "-c" || args[i] == "-C")
                {
                    i++;
                    continue;
                }
                if (args[i].StartsWith("-", StringComparison.Ordinal))
                {
                    continue;
                }
                command = i;
                break;
            }
            if (command < 0 || NetworkCommands.Contains(args[command]) == false)
            {
                return args;
            }
            for (int i = command + 1; i < args.Count; i++)
            {
                string arg = args[i];
                if (arg == "--progress" || arg == "--no-progress" || arg == "--quiet" || arg == "-q")
                {
                    return args;
                }
                if (arg == "--")
                {
                    break;
                }
            }
            List<string> result = new(args.Count + 1);
            for (int i = 0; i < args.Count; i++)
            {
                result.Add(args[i]);
                if (i == command)
                {
                    result.Add("--progress");
                }
            }
            return result;
        }

        /// <summary>
        /// 진행 줄인지 보고 나눈다. 단계 이름이 objects·deltas·files·content 로 끝나는 "단계: 숫자…" 꼴만 진행으로 친다.
        /// 순수 함수라 테스트로 확인한다.
        /// </summary>
        public static bool TryParse(string text, out GitProgressLine line)
        {
            line = default;
            string body = (text ?? string.Empty).Trim();
            bool remote = false;
            if (body.StartsWith("remote:", StringComparison.Ordinal))
            {
                remote = true;
                body = body.Substring("remote:".Length).TrimStart();
            }
            int colon = body.IndexOf(':');
            if (colon <= 0)
            {
                return false;
            }
            string phase = body.Substring(0, colon).Trim();
            if (phase.Length == 0 || char.IsUpper(phase[0]) == false
                || (phase.EndsWith("objects", StringComparison.Ordinal) == false && phase.EndsWith("deltas", StringComparison.Ordinal) == false
                    && phase.EndsWith("files", StringComparison.Ordinal) == false && phase.EndsWith("content", StringComparison.Ordinal) == false))
            {
                return false;
            }
            string rest = body.Substring(colon + 1).Trim();
            if (rest.Length == 0 || char.IsDigit(rest[0]) == false)
            {
                return false;
            }
            bool done = rest.EndsWith("done.", StringComparison.Ordinal);
            string head = rest;
            string detail = string.Empty;
            int comma = rest.IndexOf(", ", StringComparison.Ordinal);
            if (comma >= 0)
            {
                head = rest.Substring(0, comma).Trim();
                detail = rest.Substring(comma + 2).Trim();
                if (detail.EndsWith("done.", StringComparison.Ordinal))
                {
                    detail = detail.Substring(0, detail.Length - "done.".Length).Trim().TrimEnd(',').Trim();
                }
            }
            else
            {
                head = head.TrimEnd('.');
            }
            int percent = -1;
            long current = -1;
            long total = -1;
            int percentAt = head.IndexOf('%');
            if (percentAt >= 0)
            {
                if (int.TryParse(head.Substring(0, percentAt).Trim(), out int p))
                {
                    percent = Math.Max(0, Math.Min(100, p));
                }
                int open = head.IndexOf('(', percentAt);
                int close = open >= 0 ? head.IndexOf(')', open) : -1;
                if (open >= 0 && close > open)
                {
                    string[] parts = head.Substring(open + 1, close - open - 1).Split('/');
                    if (parts.Length == 2 && long.TryParse(parts[0].Trim(), out long c) && long.TryParse(parts[1].Trim(), out long t))
                    {
                        current = c;
                        total = t;
                    }
                }
            }
            else if (long.TryParse(head.Trim().TrimEnd(','), out long count))
            {
                current = count;
            }
            line = new GitProgressLine(remote, phase, percent, current, total, detail, done);
            return true;
        }

        /// <summary>진행이 너무 잦으면(초당 수십 번) 화면이 따라가지 못한다. 단계가 바뀌거나 끝났거나 이만큼 지났을 때만 남긴다.</summary>
        private static readonly long PublishIntervalTicks = Stopwatch.Frequency / 10;

        /// <summary>
        /// stderr 를 끝까지 흘려 읽는다. 진행 줄(<c>\r</c> 로 덮어쓰는 줄 포함)은 <see cref="TryGetLatest"/> 로 남기고, 나머지 줄만 모아 돌려준다 —
        /// 오류 해설(<see cref="GitErrorAdvice"/>)·성공 안내에 진행 줄이 섞이지 않게.
        /// </summary>
        public static string Drain(TextReader reader)
        {
            StringBuilder kept = new();
            StringBuilder line = new();
            char[] buffer = new char[4096];
            long lastAt = 0;
            string lastPhase = null;

            void Flush(bool newline)
            {
                if (line.Length == 0)
                {
                    if (newline)
                    {
                        kept.Append('\n');
                    }
                    return;
                }
                string text = line.ToString();
                line.Clear();
                if (TryParse(text, out GitProgressLine progress))
                {
                    long now = Stopwatch.GetTimestamp();
                    if (progress.Done || progress.Phase != lastPhase || now - lastAt >= PublishIntervalTicks)
                    {
                        Publish(progress);
                        lastAt = now;
                        lastPhase = progress.Phase;
                    }
                    return;
                }
                kept.Append(text).Append('\n');
            }

            int read;
            while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                for (int i = 0; i < read; i++)
                {
                    char c = buffer[i];
                    if (c == '\r')
                    {
                        Flush(newline: false);
                    }
                    else if (c == '\n')
                    {
                        Flush(newline: true);
                    }
                    else
                    {
                        line.Append(c);
                    }
                }
            }
            Flush(newline: false);
            return kept.ToString();
        }

        /// <summary>
        /// 진행 한 줄을 사람이 읽는 글로: "받기 45% (1,234/2,700) · 12.40 MiB | 3.10 MiB/s". 서버 단계는 앞에 "서버 ".
        /// </summary>
        public static string Describe(GitProgressLine line)
        {
            string phase = PhaseKey(line.Phase) is string key ? GitText.Get(key) : line.Phase;
            StringBuilder text = new();
            if (line.Remote)
            {
                text.Append(GitText.Get("progress.remote")).Append(' ');
            }
            text.Append(phase);
            if (line.HasPercent)
            {
                text.Append(' ').Append(line.Percent).Append('%');
            }
            if (line.Current >= 0 && line.Total >= 0)
            {
                text.Append(" (").Append(line.Current.ToString("N0")).Append('/').Append(line.Total.ToString("N0")).Append(')');
            }
            else if (line.Current >= 0)
            {
                text.Append(' ').Append(line.Current.ToString("N0"));
            }
            if (string.IsNullOrEmpty(line.Detail) == false)
            {
                text.Append(" · ").Append(line.Detail);
            }
            return text.ToString();
        }

        /// <summary>git 단계 이름의 번역 키. 모르는 단계면 null(영어 그대로 보인다).</summary>
        public static string PhaseKey(string phase)
        {
            return phase switch
            {
                "Enumerating objects" => "progress.enumerating",
                "Counting objects" => "progress.counting",
                "Compressing objects" => "progress.compressing",
                "Writing objects" => "progress.writing",
                "Receiving objects" => "progress.receiving",
                "Resolving deltas" => "progress.resolving",
                "Unpacking objects" => "progress.unpacking",
                "Updating files" => "progress.updating",
                "Checking out files" => "progress.checkingOut",
                "Filtering content" => "progress.lfsDownloading",
                "Downloading LFS objects" => "progress.lfsDownloading",
                "Uploading LFS objects" => "progress.lfsUploading",
                _ => null,
            };
        }
    }
}
