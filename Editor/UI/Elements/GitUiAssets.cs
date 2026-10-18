using System;
using UnityEditor;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 패키지 안의 UXML 을 찾아 복제한다. 경로 규칙을 한곳에 두어, 폴더를 옮겨도 여기만 고치면 된다.
    /// </summary>
    public static class GitUiAssets
    {
        private const string LayoutFolder = "Packages/com.nkstudio.gitvcs/Editor/UI/Layouts/";

        public static VisualTreeAsset Load(string layoutName)
        {
            string path = LayoutFolder + layoutName + ".uxml";
            VisualTreeAsset asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
            if (asset == null)
            {
                throw new InvalidOperationException($"UXML not found: {path}");
            }
            return asset;
        }

        /// <summary>레이아웃 전체를 복제해 남는 공간을 채우는 컨테이너로 돌려준다(탭·창 루트용).</summary>
        public static TemplateContainer CloneLayout(string layoutName)
        {
            TemplateContainer container = Load(layoutName).Instantiate();
            Localize(container);
            // 탭·창 루트는 남는 공간만 받는다. basis 를 내용 크기(auto)로 두면 긴 목록이 부모를 밀어낸다.
            container.style.flexGrow = 1;
            container.style.flexShrink = 1;
            container.style.flexBasis = 0;
            return container;
        }

        /// <summary>
        /// 목록 줄처럼 같은 모양을 여러 번 만드는 템플릿용. TemplateContainer 껍데기를 벗기고 첫 요소(BEM 블록)를 돌려준다.
        /// </summary>
        public static VisualElement CloneBlock(VisualTreeAsset asset)
        {
            TemplateContainer container = asset.Instantiate();
            Localize(container);
            VisualElement block = container[0];
            block.RemoveFromHierarchy();
            return block;
        }

        /// <summary>
        /// UXML 의 <c>text="@키"</c>, <c>tooltip="@키"</c> 를 현재 언어로 바꾼다. 문구를 UXML 에 박지 않고 번역 표(<see cref="GitStrings"/>)에서 가져온다.
        /// 언어가 바뀌면 창이 UI 를 새로 만들므로, 원래 키를 따로 기억할 필요는 없다.
        /// </summary>
        public static void Localize(VisualElement root)
        {
            root.Query<VisualElement>().ForEach(element =>
            {
                if (string.IsNullOrEmpty(element.tooltip) == false)
                {
                    element.tooltip = GitText.Resolve(element.tooltip);
                }
                // Toggle 의 글자는 안쪽 Label 이 그리지만, Toggle.text 로 바꿔야 값이 어긋나지 않는다.
                if (element is Toggle toggle)
                {
                    toggle.text = GitText.Resolve(toggle.text);
                }
                else if (element is TextElement text)
                {
                    text.text = GitText.Resolve(text.text);
                }
            });
        }
    }

    public static class VisualElementExtensions
    {
        /// <summary>이름으로 찾되 없으면 바로 실패한다. UXML 과 코드의 이름이 어긋난 것을 조용히 넘기지 않는다.</summary>
        public static T Require<T>(this VisualElement root, string name) where T : VisualElement
        {
            T element = root.Q<T>(name);
            if (element == null)
            {
                throw new InvalidOperationException($"UXML has no element named '{name}' ({typeof(T).Name}).");
            }
            return element;
        }

        public static void SetVisible(this VisualElement element, bool visible)
        {
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
