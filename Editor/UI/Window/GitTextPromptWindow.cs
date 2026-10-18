using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 글 한 줄을 묻는 작은 모달 창(Unity 에디터에는 입력 대화상자가 없다). 확인하면 입력한 글, 취소하면 null.
    /// </summary>
    public sealed class GitTextPromptWindow : EditorWindow
    {
        private string _message;
        private string _value;
        private bool _confirmed;

        public static string Ask(string title, string message, string initialValue)
        {
            GitTextPromptWindow window = CreateInstance<GitTextPromptWindow>();
            window.titleContent = new GUIContent(title);
            window._message = message;
            window._value = initialValue ?? string.Empty;
            Vector2 size = new(420, 120);
            Rect main = EditorGUIUtility.GetMainWindowPosition();
            window.position = new Rect(main.center - size * 0.5f, size);
            window.minSize = size;
            window.maxSize = size;
            window.ShowModalUtility();
            return window._confirmed ? window._value : null;
        }

        private void CreateGUI()
        {
            VisualElement root = rootVisualElement;
            root.style.paddingLeft = 10;
            root.style.paddingRight = 10;
            root.style.paddingTop = 10;
            root.style.paddingBottom = 10;

            Label message = new(_message);
            message.style.whiteSpace = WhiteSpace.Normal;
            message.style.marginBottom = 6;
            root.Add(message);

            TextField field = new() { value = _value };
            field.RegisterValueChangedCallback(evt => _value = evt.newValue);
            field.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    Confirm();
                }
                else if (evt.keyCode == KeyCode.Escape)
                {
                    Close();
                }
            }, TrickleDown.TrickleDown);
            root.Add(field);

            VisualElement buttons = new();
            buttons.style.flexDirection = FlexDirection.Row;
            buttons.style.justifyContent = Justify.FlexEnd;
            buttons.style.marginTop = 10;
            buttons.Add(new Button(Close) { text = GitText.Get("common.cancel") });
            buttons.Add(new Button(Confirm) { text = GitText.Get("common.ok") });
            root.Add(buttons);

            field.schedule.Execute(() => field.Focus());
        }

        private void Confirm()
        {
            _confirmed = true;
            Close();
        }
    }
}
