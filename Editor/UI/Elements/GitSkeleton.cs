using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>칸을 덮는 스켈레톤의 모양(<see cref="GitSkeleton.Cover"/>).</summary>
    public enum SkeletonShape
    {
        /// <summary>파일 목록·트리: 작은 아이콘 + 이름.</summary>
        Files,

        /// <summary>커밋 목록·그래프: 둥근 커밋 원 + 메시지 + 오른쪽 짧은 정보.</summary>
        Commits,
    }

    /// <summary>
    /// 읽는 동안 내용 자리에 그리는 스켈레톤(회색 막대). 진짜 배치와 같은 자리·크기로 두어, 다 읽었을 때 화면이 덜 흔들린다.
    /// 막대들은 <see cref="Pulse"/> 한 그릇 안에서, 밝은 띠가 위에서 아래로 내려가며 지나는 막대를 차례로 밝힌다(물결).
    /// </summary>
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public static class GitSkeleton
    {
        public const string Block = "git-skeleton";

        /// <summary>띠가 그릇 위에서 아래까지 한 번 내려가는 시간(초). 다 내려가면 다시 위에서 시작한다.</summary>
        private const float SweepSeconds = 2.3f;

        /// <summary>띠의 반쪽 높이: 그릇 높이의 이 비율(그래도 이 px 보다 얇지 않게). 띠 가운데에서 멀어질수록 막대가 어두워진다.</summary>
        private const float BandRatio = 0.45f;
        private const float MinBandHalf = 90f;

        /// <summary>띠가 지나지 않는 막대의 밝기와 띠 한가운데 막대의 밝기(opacity).</summary>
        private const float RestOpacity = 0.4f;
        private const float PeakOpacity = 1f;

        /// <summary>밝기를 다시 넣는 간격(ms).</summary>
        private const long FrameMs = 33;

        /// <summary>
        /// 막대 하나. width 가 0 이하이면 남은 폭을 채운다(flex-grow). 퍼센트는 widthPercent 로.
        /// </summary>
        public static VisualElement Bar(VisualElement parent, float width, float height, float widthPercent = 0, string modifier = null)
        {
            VisualElement bar = new() { pickingMode = PickingMode.Ignore };
            bar.AddToClassList(Block);
            if (modifier != null)
            {
                bar.AddToClassList(Block + "--" + modifier);
            }
            if (widthPercent > 0)
            {
                bar.style.width = Length.Percent(widthPercent);
            }
            else if (width > 0)
            {
                bar.style.width = width;
            }
            else
            {
                bar.style.flexGrow = 1;
            }
            bar.style.height = height;
            parent.Add(bar);
            return bar;
        }

        /// <summary>가로로 늘어놓는 줄.</summary>
        public static VisualElement Row(VisualElement parent)
        {
            VisualElement row = new() { pickingMode = PickingMode.Ignore };
            row.AddToClassList(Block + "-row");
            parent.Add(row);
            return row;
        }

        /// <summary>칸을 덮은 스켈레톤이 걷히는 시간(ms). 아래 내용이 서서히 드러난다.</summary>
        private const int UncoverMs = 50;

        /// <summary>
        /// 읽기가 이보다 빨리 끝나면 스켈레톤을 아예 보이지 않는다(ms). 빠른 화면에서 한 번 번쩍이고 사라지는 잔상을 막는다.
        /// </summary>
        public const long AppearDelayMs = 250;

        /// <summary>
        /// 한 번 보인 스켈레톤은 적어도 이만큼 보인 뒤에 걷는다(ms) — 나타나자마자 사라지는 깜박임을 막는다.
        /// 450 이면 다 읽은 뒤에도 0.5초 넘게 기다려(걷히는 시간 포함) 느리게 느껴졌다.
        /// </summary>
        private const double MinShownMs = 60;

        /// <summary>늦게 나타날 때 서서히 보이는 시간(ms).</summary>
        private const int AppearFadeMs = 150;

        /// <summary>스켈레톤이 화면에 나타난 시각(<see cref="AppearLater"/>). 아직 안 나타났으면 없다.</summary>
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, object> ShownAt = new();

        /// <summary>
        /// 스켈레톤을 투명하게 두었다가 <see cref="AppearDelayMs"/> 뒤에도 아직 붙어 있으면(그때까지 읽기가 안 끝났으면) 서서히 보인다.
        /// owner 를 주면 나타난 시각을 owner 에 기록한다(<see cref="Cover"/> 는 바탕은 바로 두고 막대 묶음만 늦게 보인다).
        /// </summary>
        public static void AppearLater(VisualElement skeleton, VisualElement owner = null)
        {
            VisualElement key = owner ?? skeleton;
            skeleton.style.opacity = 0f;
            skeleton.schedule.Execute(() =>
            {
                if (key.parent == null || skeleton.parent == null)
                {
                    return;
                }
                ShownAt.AddOrUpdate(key, Time.realtimeSinceStartupAsDouble);
                skeleton.style.transitionProperty = new List<StylePropertyName> { "opacity" };
                skeleton.style.transitionDuration = new List<TimeValue> { new(AppearFadeMs, TimeUnit.Millisecond) };
                skeleton.style.opacity = 1f;
            }).StartingIn(AppearDelayMs);
        }

        /// <summary>스켈레톤이 한 번이라도 화면에 나타났는지.</summary>
        public static bool WasShown(VisualElement skeleton)
        {
            return skeleton != null && ShownAt.TryGetValue(skeleton, out _);
        }

        /// <summary>
        /// 스켈레톤을 걷어도 되기까지 남은 시간(ms): 나타나지 않았으면 0, 나타났으면 <see cref="MinShownMs"/> 를 채울 때까지.
        /// </summary>
        public static long RemainingShowMs(VisualElement skeleton)
        {
            if (skeleton == null || ShownAt.TryGetValue(skeleton, out object at) == false)
            {
                return 0;
            }
            double shown = (Time.realtimeSinceStartupAsDouble - (double)at) * 1000.0;
            return (long)System.Math.Max(0.0, MinShownMs - shown);
        }

        /// <summary>
        /// host 칸 전체를 덮는 스켈레톤(그 칸 바탕색으로 내용을 가리고, 모양에 맞는 줄을 칸 높이보다 넉넉히 늘어놓아 물결을 흘린다).
        /// 내용을 다 읽으면 <see cref="Uncover"/> 로 걷는다. 줄 폭은 줄 번호로 정해 매번 같은 모양이다.
        /// </summary>
        public static VisualElement Cover(VisualElement host, SkeletonShape shape)
        {
            // 바탕은 처음부터 불투명하게 깔아 아래의 이전 내용이 비치지 않게 하고(잔상), 막대 묶음만 늦게 나타난다(빨리 읽히면 막대 없이 바로 내용).
            VisualElement cover = new() { pickingMode = PickingMode.Ignore };
            cover.AddToClassList(Block + "-cover");
            cover.AddToClassList(Block + "-cover--" + shape.ToString().ToLowerInvariant());
            VisualElement bars = new() { pickingMode = PickingMode.Ignore };
            bars.AddToClassList(Block + "-cover-bars");
            cover.Add(bars);
            int rows = shape == SkeletonShape.Commits ? 24 : 32;
            for (int i = 0; i < rows; i++)
            {
                VisualElement row = Row(bars);
                row.AddToClassList(Block + "-cover-row");
                // 40~85% 사이에서 줄마다 다른 폭(같은 길이가 반복되면 격자처럼 보인다).
                float width = 40f + (i * 37 % 46);
                if (shape == SkeletonShape.Commits)
                {
                    Bar(row, 20, 20, modifier: "dot").AddToClassList(Block + "-cover-lead");
                    Bar(row, 0, 11, widthPercent: width * 0.7f);
                    VisualElement fill = new() { pickingMode = PickingMode.Ignore };
                    fill.style.flexGrow = 1;
                    row.Add(fill);
                    Bar(row, 48 + i * 13 % 30, 10).AddToClassList(Block + "-cover-meta");
                }
                else
                {
                    Bar(row, 14, 14).AddToClassList(Block + "-cover-lead");
                    Bar(row, 0, 10, widthPercent: width * 0.8f);
                }
            }
            // ListView 처럼 contentContainer 가 없는 요소는 Add 가 예외를 낸다(체인지 세트 탭에서 났다) — 그런 칸이면 hierarchy 에 바로 붙인다.
            if (host.contentContainer == null)
            {
                host.hierarchy.Add(cover);
            }
            else
            {
                host.Add(cover);
            }
            Pulse(bars);
            AppearLater(bars, cover);
            return cover;
        }

        /// <summary>
        /// 덮은 스켈레톤을 걷는다. 아직 나타나지 않았으면(빨리 읽혔으면) 바로 떼어 내 내용이 그대로 보이고,
        /// 나타났으면 최소 표시 시간을 채운 뒤 서서히 투명하게 해 아래 내용을 드러낸다.
        /// </summary>
        public static void Uncover(VisualElement cover)
        {
            if (cover == null || cover.parent == null)
            {
                return;
            }
            if (WasShown(cover) == false)
            {
                cover.RemoveFromHierarchy();
                return;
            }
            cover.schedule.Execute(() => FadeOut(cover)).StartingIn(RemainingShowMs(cover));
        }

        private static void FadeOut(VisualElement cover)
        {
            cover.style.transitionProperty = new List<StylePropertyName> { "opacity" };
            cover.style.transitionDuration = new List<TimeValue> { new(UncoverMs, TimeUnit.Millisecond) };
            cover.style.opacity = 0f;
            cover.schedule.Execute(cover.RemoveFromHierarchy).StartingIn(UncoverMs + 40);
        }

        /// <summary>
        /// group 안의 막대들에 위에서 아래로 내려가는 밝은 띠를 흘린다. 막대마다 그릇 안 세로 위치를 재 두고(배치가 바뀔 때만),
        /// 틀마다 띠 가운데와의 거리로 밝기를 정한다 — 같은 줄의 막대는 함께, 아래 줄은 띠가 내려온 만큼 늦게 밝아진다.
        /// 띠는 그릇 위쪽 밖에서 들어와 아래쪽 밖으로 나가므로 맨 위·맨 아래 줄도 다른 줄과 같은 만큼 밝아진다.
        /// 보이지 않거나 패널에서 떨어지면 쉰다(스케줄은 패널에 붙어 있을 때만 돈다).
        /// </summary>
        public static void Pulse(VisualElement group)
        {
            group.AddToClassList(Block + "-group");
            List<(VisualElement Bar, float Center)> bars = new();
            float height = 0f;
            group.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                bars.Clear();
                Rect bound = group.worldBound;
                height = bound.height;
                group.Query(className: Block).ForEach(bar => bars.Add((bar, bar.worldBound.center.y - bound.yMin)));
            });
            float startedAt = Time.realtimeSinceStartup;
            group.schedule.Execute(() =>
            {
                if (height <= 0f || group.resolvedStyle.display == DisplayStyle.None)
                {
                    return;
                }
                float half = Mathf.Max(height * BandRatio, MinBandHalf);
                float phase = (Time.realtimeSinceStartup - startedAt) / SweepSeconds % 1f;
                float bandY = Mathf.Lerp(-half, height + half, phase);
                foreach ((VisualElement bar, float center) in bars)
                {
                    // 파도처럼 완만하게: 띠 가운데에서 가장자리까지 코사인 곡선으로 잦아든다.
                    float distance = Mathf.Clamp01(Mathf.Abs(center - bandY) / half);
                    float near = 0.5f + 0.5f * Mathf.Cos(distance * Mathf.PI);
                    bar.style.opacity = Mathf.Lerp(RestOpacity, PeakOpacity, near);
                }
            }).Every(FrameMs);
        }
    }
}
