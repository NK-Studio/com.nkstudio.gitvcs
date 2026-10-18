using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 그래프 위의 글자(Label)를 종류별로 재사용한다. 매 갱신마다 <see cref="Begin"/> → <see cref="Place"/>… → <see cref="End"/> 순서로 부르면
    /// 이번에 쓰인 만큼만 보이고 나머지는 숨는다. 화면에 보이는 커밋 수만큼만 Label 이 생긴다.
    /// </summary>
    public sealed class GraphLabelLayer
    {
        private const string LabelClass = "branch-graph__label";

        private readonly VisualElement _container;
        private readonly Dictionary<string, Pool> _pools = new();
        private readonly List<VisualElement> _images = new();
        private int _usedImages;

        private sealed class Pool
        {
            public readonly List<Label> Labels = new();
            public int Used;
        }

        public GraphLabelLayer(VisualElement container)
        {
            _container = container;
        }

        public void Begin()
        {
            foreach (Pool pool in _pools.Values)
            {
                pool.Used = 0;
            }
            _usedImages = 0;
        }

        /// <summary>둥근 그림 하나(아바타)를 꺼내 위치·그림을 정한다.</summary>
        public VisualElement PlaceCircleImage(string blockClass, Texture2D texture, Vector2 center, float radius)
        {
            if (_usedImages == _images.Count)
            {
                VisualElement created = new() { pickingMode = PickingMode.Ignore };
                created.AddToClassList(blockClass);
                _container.Add(created);
                _images.Add(created);
            }

            VisualElement image = _images[_usedImages++];
            image.style.display = DisplayStyle.Flex;
            image.style.backgroundImage = new StyleBackground(texture);
            image.style.left = center.x - radius;
            image.style.top = center.y - radius;
            image.style.width = radius * 2f;
            image.style.height = radius * 2f;
            StyleLength corner = new(radius);
            image.style.borderTopLeftRadius = corner;
            image.style.borderTopRightRadius = corner;
            image.style.borderBottomLeftRadius = corner;
            image.style.borderBottomRightRadius = corner;
            return image;
        }

        /// <summary>
        /// <paramref name="blockClass"/>(예: <c>branch-graph__subject</c>) Label 하나를 꺼내 위치·글자를 정한다.
        /// <paramref name="size"/> 의 성분이 0 이하면 그 방향은 글자 크기에 맡긴다.
        /// <paramref name="anchorX"/> 는 <paramref name="position"/> 이 Label 의 가로 어디인지(0 왼쪽, 0.5 가운데, 1 오른쪽) —
        /// 글자 폭을 몰라도 가운데·오른쪽 정렬할 수 있게 translate 퍼센트로 민다.
        /// </summary>
        public Label Place(string blockClass, string text, Vector2 position, Vector2 size, float fontSize, float anchorX = 0f, params string[] modifiers)
        {
            if (_pools.TryGetValue(blockClass, out Pool pool) == false)
            {
                pool = new Pool();
                _pools[blockClass] = pool;
            }
            if (pool.Used == pool.Labels.Count)
            {
                Label created = new();
                created.AddToClassList(LabelClass);
                created.AddToClassList(blockClass);
                created.pickingMode = PickingMode.Ignore;
                _container.Add(created);
                pool.Labels.Add(created);
            }

            Label label = pool.Labels[pool.Used++];
            label.text = text;
            label.style.display = DisplayStyle.Flex;
            label.style.left = position.x;
            label.style.top = position.y;
            label.style.width = size.x > 0 ? new StyleLength(size.x) : new StyleLength(StyleKeyword.Auto);
            label.style.height = size.y > 0 ? new StyleLength(size.y) : new StyleLength(StyleKeyword.Auto);
            label.style.fontSize = fontSize > 0 ? new StyleLength(fontSize) : new StyleLength(StyleKeyword.Null);
            label.style.translate = new Translate(Length.Percent(-anchorX * 100f), new Length(0f));

            // 수식어는 줄마다 다를 수 있으니(HEAD·태그 이름표 등) 매번 다시 단다.
            label.ClearClassList();
            label.AddToClassList(LabelClass);
            label.AddToClassList(blockClass);
            foreach (string modifier in modifiers)
            {
                label.AddToClassList(modifier);
            }
            return label;
        }

        public void End()
        {
            for (int i = _usedImages; i < _images.Count; i++)
            {
                _images[i].style.display = DisplayStyle.None;
            }
            foreach (Pool pool in _pools.Values)
            {
                for (int i = pool.Used; i < pool.Labels.Count; i++)
                {
                    pool.Labels[i].style.display = DisplayStyle.None;
                }
            }
        }
    }
}
