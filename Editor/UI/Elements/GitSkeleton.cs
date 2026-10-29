using System.Collections.Generic;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 읽는 동안 내용 자리에 그리는 스켈레톤(회색 막대). 진짜 배치와 같은 자리·크기로 두어, 다 읽었을 때 화면이 덜 흔들린다.
    /// 막대들은 <see cref="Pulse"/> 한 그릇 안에서 위에서 아래로 물결치듯 옅어졌다 짙어진다(USS transition + 위치에 따른 지연).
    /// </summary>
    public static class GitSkeleton
    {
        public const string Block = "git-skeleton";
        private const string DimModifier = Block + "-group--dim";

        /// <summary>맨 위 막대와 맨 아래 막대가 바뀌기 시작하는 시차(물결이 위에서 아래로 내려가는 시간).</summary>
        private const float WaveSpreadMs = 500f;

        /// <summary>한 번 옅어지고(또는 짙어지고) 다음으로 넘어가는 주기. USS transition(0.6s)과 물결 시차를 다 지난 뒤.</summary>
        private const int PulseMs = 1200;

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
        /// group 안의 막대들을 위에서 아래로 물결치게 한다: 그릇의 옅게·짙게를 번갈아 바꾸고, 막대마다 그릇 안 세로 위치만큼 전환을 늦춘다.
        /// 보이지 않거나 패널에서 떨어지면 쉰다(스케줄은 패널에 붙어 있을 때만 돈다).
        /// </summary>
        public static void Pulse(VisualElement group)
        {
            group.AddToClassList(Block + "-group");
            group.RegisterCallback<GeometryChangedEvent>(_ => SetWaveDelays(group));
            group.schedule.Execute(() =>
            {
                if (group.resolvedStyle.display == DisplayStyle.None)
                {
                    return;
                }
                group.ToggleInClassList(DimModifier);
            }).Every(PulseMs);
        }

        private static void SetWaveDelays(VisualElement group)
        {
            float top = group.worldBound.yMin;
            float height = group.worldBound.height;
            if (height <= 0)
            {
                return;
            }
            group.Query(className: Block).ForEach(bar =>
            {
                float t = (bar.worldBound.yMin - top) / height;
                float delay = t < 0 ? 0 : t > 1 ? WaveSpreadMs : t * WaveSpreadMs;
                bar.style.transitionDelay = new List<TimeValue> { new(delay, TimeUnit.Millisecond) };
            });
        }
    }
}
