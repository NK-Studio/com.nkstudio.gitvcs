using System;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 오래 걸리는 작업(Pull·Push·Fetch 등)의 진행 카드. Git 창 오른쪽 아래에 뜬다.
    /// 짧게 끝나는 작업은 깜빡이지 않게 0.9초가 지나야 보인다. git 이 알리는 진행(<see cref="GitProgress"/>, 받기 45% 등)이 오면
    /// 단계·막대·숫자를 채우고, 진행을 모르는 동안은 "서버 응답을 기다리는 중…" 과 흐르는 막대. 지난 시간은 매초 올라간다.
    /// 끝나면 "~ 완료" 를 잠깐 보이고 사라진다. 실패는 바로 닫는다(오류 알림이 따로 뜬다).
    /// </summary>
    public sealed class GitOperationCard
    {
        private const string Block = "op-card";
        private const long ShowAfterMs = 900;
        private const long DoneLingerMs = 1400;
        private const long PollMs = 50;

        private readonly VisualElement _root;
        private readonly VisualElement _badge;
        private ActionGlyph _glyph;
        private readonly ActionGlyph _check;
        private readonly Spinner _spinner;
        private readonly Label _title;
        private readonly Label _time;
        private readonly Label _phase;
        private readonly VisualElement _bar;
        private readonly VisualElement _fill;

        private readonly Stopwatch _clock = new();
        private IVisualElementScheduledItem _tick;
        private string _operation;
        private bool _running;
        private bool _shown;
        private long _sequenceAtStart;
        private long _seenSequence;
        private long _doneAt = -1;
        private float _flow;

        public GitOperationCard(VisualElement host, IGitOperationRunner runner)
        {
            _root = new VisualElement { pickingMode = PickingMode.Ignore };
            _root.AddToClassList(Block);
            GitShadow.Add(_root, GitShadowSize.Small);

            _badge = new VisualElement { pickingMode = PickingMode.Ignore };
            _badge.AddToClassList(Block + "__badge");
            _glyph = new ActionGlyph(ActionGlyphKind.Refresh);
            _glyph.AddToClassList(Block + "__glyph");
            _check = new ActionGlyph(ActionGlyphKind.Check);
            _check.AddToClassList(Block + "__glyph");
            _check.SetVisible(false);
            _spinner = new Spinner();
            _spinner.AddToClassList(Block + "__spinner");
            _badge.Add(_glyph);
            _badge.Add(_check);
            _badge.Add(_spinner);
            _root.Add(_badge);

            VisualElement body = new() { pickingMode = PickingMode.Ignore };
            body.AddToClassList(Block + "__body");
            VisualElement head = new() { pickingMode = PickingMode.Ignore };
            head.AddToClassList(Block + "__head");
            _title = new Label { pickingMode = PickingMode.Ignore };
            _title.AddToClassList(Block + "__title");
            _time = new Label { pickingMode = PickingMode.Ignore };
            _time.AddToClassList(Block + "__time");
            head.Add(_title);
            head.Add(_time);
            _phase = new Label { pickingMode = PickingMode.Ignore };
            _phase.AddToClassList(Block + "__phase");
            _bar = new VisualElement { pickingMode = PickingMode.Ignore };
            _bar.AddToClassList(Block + "__bar");
            _fill = new VisualElement { pickingMode = PickingMode.Ignore };
            _fill.AddToClassList(Block + "__fill");
            _bar.Add(_fill);
            body.Add(head);
            body.Add(_phase);
            body.Add(_bar);
            _root.Add(body);

            _root.SetVisible(false);
            host.Add(_root);

            runner.OperationStarted += Start;
            runner.OperationFinished += Finish;
            // 창이 닫히면(다시 만들면) 듣기를 푼다.
            _root.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                runner.OperationStarted -= Start;
                runner.OperationFinished -= Finish;
                _tick?.Pause();
            });
        }

        private void Start(string title)
        {
            _operation = title;
            _running = true;
            _shown = false;
            _doneAt = -1;
            _sequenceAtStart = GitProgress.Sequence;
            _seenSequence = _sequenceAtStart;
            _clock.Restart();
            _root.RemoveFromClassList(Block + "--done");
            _glyph.SetVisible(true);
            _check.SetVisible(false);
            _spinner.SetVisible(true);
            _title.text = GitText.Format("runner.running", title);
            _phase.text = GitText.Get("opcard.waiting");
            SetPercent(-1);
            _root.SetVisible(false);
            // 진행 줄은 git 작업 스레드에서 오므로 짧은 간격으로 읽어 온다(GitProgress.TryGetLatest).
            _tick ??= _root.schedule.Execute(Tick).Every(PollMs);
            _tick.Resume();
        }

        private void Finish(string title, bool success)
        {
            if (_running == false || title != _operation)
            {
                return;
            }
            _running = false;
            if (_shown == false || success == false)
            {
                Hide();
                return;
            }
            // 완료: 막대를 끝까지 채우고 체크를 보인 뒤 잠깐 머문다.
            _root.AddToClassList(Block + "--done");
            _glyph.SetVisible(false);
            _check.SetVisible(true);
            _spinner.SetVisible(false);
            _title.text = GitText.Format("opcard.done", title);
            _phase.text = GitText.Format("opcard.doneDetail", Elapsed(_clock.ElapsedMilliseconds));
            _time.text = string.Empty;
            SetPercent(100);
            _doneAt = _clock.ElapsedMilliseconds;
        }

        private void Hide()
        {
            _root.SetVisible(false);
            _shown = false;
            _tick?.Pause();
        }

        private void Tick()
        {
            long now = _clock.ElapsedMilliseconds;
            if (_doneAt >= 0)
            {
                if (now - _doneAt >= DoneLingerMs)
                {
                    Hide();
                }
                return;
            }
            if (_running == false)
            {
                return;
            }
            if (_shown == false)
            {
                if (now < ShowAfterMs)
                {
                    return;
                }
                _shown = true;
                // 작업 아이콘(Pull·Push·Fetch)으로 바꿔 끼운다. ActionGlyph 는 모양을 만들 때 정한다.
                ActionGlyph glyph = new(GlyphOf(_operation));
                glyph.AddToClassList(Block + "__glyph");
                _badge.Insert(_badge.IndexOf(_glyph), glyph);
                _badge.Remove(_glyph);
                _glyph = glyph;
                _root.SetVisible(true);
                _root.BringToFront();
            }
            _time.text = Elapsed(now);
            if (GitProgress.TryGetLatest(out GitProgressLine line, out long sequence) && sequence > _sequenceAtStart && sequence != _seenSequence)
            {
                _seenSequence = sequence;
                _phase.text = GitProgress.Describe(line);
                SetPercent(line.HasPercent ? line.Percent : -1);
            }
            _spinner.Advance(PollMs);
            if (_bar.ClassListContains(Block + "__bar--known") == false)
            {
                // 진행률을 모르면 막대 한 토막이 왼쪽에서 오른쪽으로 흐른다.
                _flow = (_flow + PollMs / 1200f) % 1f;
                _fill.style.left = Length.Percent(-30f + _flow * 130f);
            }
        }

        private void SetPercent(int percent)
        {
            bool known = percent >= 0;
            _bar.EnableInClassList(Block + "__bar--known", known);
            _fill.style.left = known ? Length.Percent(0) : StyleKeyword.Null;
            _fill.style.width = known ? Length.Percent(percent) : StyleKeyword.Null;
        }

        /// <summary>작업 이름으로 아이콘을 고른다(Pull·Push·Fetch, 나머지는 새로고침 모양).</summary>
        private static ActionGlyphKind GlyphOf(string title)
        {
            if (title == GitText.Get("op.pull"))
            {
                return ActionGlyphKind.Pull;
            }
            if (title == GitText.Get("op.push"))
            {
                return ActionGlyphKind.Push;
            }
            if (title == GitText.Get("op.fetch") || title.IndexOf("Fetch", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return ActionGlyphKind.Fetch;
            }
            return ActionGlyphKind.Refresh;
        }

        /// <summary>"4초", "1분 12초".</summary>
        public static string Elapsed(long ms)
        {
            long seconds = Math.Max(0, ms / 1000);
            return seconds < 60
                ? GitText.Format("opcard.seconds", seconds)
                : GitText.Format("opcard.minutes", seconds / 60, seconds % 60);
        }

        /// <summary>배지 둘레를 도는 고리(4분의 1 원호). 글자색(강조색)으로 그린다.</summary>
        private sealed class Spinner : VisualElement
        {
            private float _angle;

            public Spinner()
            {
                pickingMode = PickingMode.Ignore;
                generateVisualContent += Draw;
                RegisterCallback<CustomStyleResolvedEvent>(_ => MarkDirtyRepaint());
            }

            public void Advance(long ms)
            {
                _angle = (_angle + ms * 0.4f) % 360f;
                MarkDirtyRepaint();
            }

            private void Draw(MeshGenerationContext context)
            {
                Rect rect = contentRect;
                float radius = Mathf.Min(rect.width, rect.height) * 0.5f - 1.5f;
                if (radius <= 0f)
                {
                    return;
                }
                Painter2D painter = context.painter2D;
                painter.strokeColor = resolvedStyle.color;
                painter.lineWidth = 2f;
                painter.lineCap = LineCap.Round;
                painter.BeginPath();
                painter.Arc(rect.center, radius, _angle, _angle + 90f);
                painter.Stroke();
            }
        }
    }
}
