using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 읽는 동안 내용 자리에 그리는 스켈레톤(회색 막대). 진짜 배치와 같은 자리·크기로 두어, 다 읽었을 때 화면이 덜 흔들린다.
    /// 막대들은 <see cref="Pulse"/> 한 그릇 안에서, 밝은 띠가 위에서 아래로 내려가며 지나는 막대를 차례로 밝힌다(물결).
    /// </summary>
    public static class GitSkeleton
    {
        public const string Block = "git-skeleton";

        /// <summary>띠가 그릇 위에서 아래까지 한 번 내려가는 시간(초). 다 내려가면 다시 위에서 시작한다.</summary>
        private const float SweepSeconds = 1.4f;

        /// <summary>띠의 반쪽 높이: 그릇 높이의 이 비율(그래도 이 px 보다 얇지 않게). 띠 가운데에서 멀어질수록 막대가 어두워진다.</summary>
        private const float BandRatio = 0.22f;
        private const float MinBandHalf = 48f;

        /// <summary>띠가 지나지 않는 막대의 밝기와 띠 한가운데 막대의 밝기(opacity).</summary>
        private const float RestOpacity = 0.35f;
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
                    float near = Mathf.Clamp01(1f - Mathf.Abs(center - bandY) / half);
                    // 띠 가장자리는 부드럽게(smoothstep), 가운데는 둥글게 밝힌다.
                    near = near * near * (3f - 2f * near);
                    bar.style.opacity = Mathf.Lerp(RestOpacity, PeakOpacity, near);
                }
            }).Every(FrameMs);
        }
    }
}
