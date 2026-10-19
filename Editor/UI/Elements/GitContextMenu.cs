using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 우클릭 메뉴를 Unity 기본(OS 모양, 늘 밝은 회색) 대신 창 테마를 따르는 UI Toolkit 메뉴로 띄운다.
    ///
    /// 메뉴 항목은 지금처럼 각 목록이 <see cref="ContextualMenuManipulator"/>·<see cref="DropdownMenu"/> 로 채운다.
    /// 창 맨 위에서 <see cref="ContextualMenuPopulateEvent"/> 를 마지막으로 받아(다 채워진 뒤) 항목을 옮겨 오고,
    /// 기본 메뉴는 띄우지 않게 한다(항목을 비우면 Unity 는 메뉴를 띄우지 않는다). 그래서 커밋·파일·브랜치·참조 메뉴가 한 번에 바뀐다.
    /// 구분선·하위 메뉴("a/b")·꺼짐·체크를 지원하고, 창 밖으로 넘치면 안쪽으로 맞춘다. Esc·바깥 클릭으로 닫고 ↑↓·Enter·→·← 로 고른다.
    /// </summary>
    public sealed class GitContextMenu
    {
        private const string Block = "git-menu";

        /// <summary>메뉴 한 줄(또는 구분선·하위 메뉴).</summary>
        private sealed class Node
        {
            public string Label;
            public DropdownMenuAction Action;
            public bool IsSeparator;
            public readonly List<Node> Children = new();
            public bool IsSubmenu => Action == null && IsSeparator == false;
        }

        private readonly VisualElement _host;
        private readonly VisualElement _overlay;
        private readonly List<VisualElement> _panels = new();
        private readonly double _openedAt = Time.realtimeSinceStartupAsDouble;

        /// <summary>
        /// <paramref name="windowRoot"/> 안의 우클릭 메뉴를 테마 메뉴로 바꾼다. 메뉴는 <paramref name="host"/>(테마·UI 배율이 걸린 창 본체) 위에 뜬다.
        /// </summary>
        public static void Attach(VisualElement windowRoot, VisualElement host)
        {
            windowRoot.RegisterCallback<ContextualMenuPopulateEvent>(evt =>
            {
                List<DropdownMenuItem> items = evt.menu.MenuItems();
                if (items.Count == 0 || host.panel == null)
                {
                    return;
                }
                DropdownMenuEventInfo info = evt.triggerEvent != null ? new DropdownMenuEventInfo(evt.triggerEvent) : null;
                Node root = Build(items, info);
                if (root.Children.Count == 0)
                {
                    return;
                }
                // 기본 메뉴는 띄우지 않는다: 이 이벤트가 끝난 뒤 Unity 는 항목이 있을 때만 메뉴를 띄운다(PostDispatch 의 menu.Count > 0).
                items.Clear();
                Vector2 position = host.WorldToLocal(evt.mousePosition);
                _ = new GitContextMenu(host, root, position);
            });
        }

        /// <summary>
        /// "a/b/c" 이름을 하위 메뉴 나무로 묶는다. 숨김 항목은 뺀다. 구분선은 그 하위 메뉴 안에 둔다.
        /// 맨 앞·맨 뒤·겹친 구분선은 지운다.
        /// </summary>
        private static Node Build(List<DropdownMenuItem> items, DropdownMenuEventInfo info)
        {
            Node root = new();
            foreach (DropdownMenuItem item in items)
            {
                if (item is DropdownMenuSeparator separator)
                {
                    FolderOf(root, separator.subMenuPath, create: true).Children.Add(new Node { IsSeparator = true });
                    continue;
                }
                if (item is not DropdownMenuAction action)
                {
                    continue;
                }
                action.UpdateActionStatus(info);
                if ((action.status & DropdownMenuAction.Status.Hidden) != 0)
                {
                    continue;
                }
                string[] parts = action.name.Split('/');
                Node folder = FolderOf(root, string.Join("/", parts.Take(parts.Length - 1)), create: true);
                folder.Children.Add(new Node { Label = parts[^1], Action = action });
            }
            Tidy(root);
            return root;
        }

        private static Node FolderOf(Node root, string path, bool create)
        {
            Node folder = root;
            if (string.IsNullOrEmpty(path))
            {
                return folder;
            }
            foreach (string part in path.Split('/'))
            {
                Node next = folder.Children.FirstOrDefault(c => c.IsSubmenu && c.Label == part);
                if (next == null)
                {
                    next = new Node { Label = part };
                    folder.Children.Add(next);
                }
                folder = next;
            }
            return folder;
        }

        private static void Tidy(Node folder)
        {
            foreach (Node child in folder.Children.Where(c => c.IsSubmenu))
            {
                Tidy(child);
            }
            folder.Children.RemoveAll(c => c.IsSubmenu && c.Children.Count == 0);
            for (int i = folder.Children.Count - 1; i >= 0; i--)
            {
                bool separator = folder.Children[i].IsSeparator;
                bool edge = i == 0 || i == folder.Children.Count - 1;
                bool doubled = i > 0 && folder.Children[i - 1].IsSeparator;
                if (separator && (edge || doubled))
                {
                    folder.Children.RemoveAt(i);
                }
            }
        }

        private GitContextMenu(VisualElement host, Node root, Vector2 position)
        {
            _host = host;
            // 화면 전체를 덮는 막: 바깥을 누르면 닫는다(그 누름은 밑의 목록에 넘기지 않는다).
            _overlay = new VisualElement { focusable = true };
            _overlay.AddToClassList(Block + "__overlay");
            _overlay.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.target == _overlay)
                {
                    Close();
                    evt.StopPropagation();
                }
            });
            _overlay.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            // 줄은 포커스를 받지 않으므로, 메뉴 안을 눌러도 키보드(↑↓·Enter·Esc)가 계속 막으로 오게 포커스를 되돌린다.
            _overlay.RegisterCallback<PointerUpEvent>(_ => _overlay.schedule.Execute(() => _overlay.Focus()));
            // 우클릭한 요소가 아직 포인터를 잡고 있으면(입력란의 글자 고르기 등) Unity 는 잡은 요소의 커서만 쓰고
            // 다른 요소에는 hover 를 보내지 않는다 — 메뉴 줄 위에서도 I자 커서가 남고 줄이 밝아지지 않는다. 기본 메뉴처럼 놓게 한다.
            host.panel.ReleasePointer(PointerId.mousePointerId);
            if (MouseCaptureController.IsMouseCaptured())
            {
                MouseCaptureController.ReleaseMouse();
            }
            host.Add(_overlay);
            OpenPanel(root, position, level: 0, anchor: null);
            _overlay.schedule.Execute(() => _overlay.Focus());
        }

        // ── 패널(한 단계 메뉴) ─────────────────────────────

        private sealed class PanelState
        {
            public Node Folder;
            public List<VisualElement> Rows = new();
            public int Hovered = -1;
        }

        private void OpenPanel(Node folder, Vector2 position, int level, VisualElement anchor)
        {
            // 이 단계보다 깊은 하위 메뉴는 닫는다.
            ClosePanelsFrom(level);

            VisualElement panel = new();
            panel.AddToClassList(Block);
            PanelState state = new() { Folder = folder };
            panel.userData = state;
            foreach (Node node in folder.Children)
            {
                if (node.IsSeparator)
                {
                    VisualElement line = new();
                    line.AddToClassList(Block + "__separator");
                    panel.Add(line);
                    continue;
                }
                VisualElement row = Row(node, level, panel);
                state.Rows.Add(row);
                panel.Add(row);
            }
            _overlay.Add(panel);
            _panels.Add(panel);

            // 크기가 정해진 뒤 창 안으로 맞춘다. 하위 메뉴는 부모 줄 오른쪽(모자라면 왼쪽)에 붙인다.
            panel.style.visibility = Visibility.Hidden;
            panel.style.left = position.x;
            panel.style.top = position.y;
            panel.RegisterCallback<GeometryChangedEvent>(Fit);

            void Fit(GeometryChangedEvent _)
            {
                panel.UnregisterCallback<GeometryChangedEvent>(Fit);
                Rect area = _overlay.contentRect;
                float width = panel.resolvedStyle.width;
                float height = panel.resolvedStyle.height;
                float x = position.x;
                float y = position.y;
                if (anchor != null)
                {
                    Rect row = anchor.ChangeCoordinatesTo(_overlay, anchor.contentRect);
                    VisualElement parentPanel = anchor.parent;
                    Rect parent = parentPanel.ChangeCoordinatesTo(_overlay, parentPanel.contentRect);
                    x = parent.xMax - 2f;
                    y = row.y - 4f;
                    if (x + width > area.width)
                    {
                        x = parent.x - width + 2f;
                    }
                }
                x = Mathf.Clamp(x, 0f, Mathf.Max(0f, area.width - width));
                y = Mathf.Clamp(y, 0f, Mathf.Max(0f, area.height - height));
                panel.style.left = x;
                panel.style.top = y;
                panel.style.visibility = Visibility.Visible;
            }
        }

        private VisualElement Row(Node node, int level, VisualElement panel)
        {
            VisualElement row = new();
            row.AddToClassList(Block + "__item");
            bool disabled = node.Action != null && (node.Action.status & DropdownMenuAction.Status.Disabled) != 0;
            bool isChecked = node.Action != null && (node.Action.status & DropdownMenuAction.Status.Checked) != 0;
            row.EnableInClassList(Block + "__item--disabled", disabled);

            Label check = new(isChecked ? "✓" : string.Empty);
            check.AddToClassList(Block + "__check");
            row.Add(check);
            // 메뉴용으로 바꿔 둔 '∕'(하위 메뉴로 쪼개지지 않게)는 보일 때 다시 '/' 로 — 글꼴에 없을 수 있다.
            Label label = new(GitDisplay.Nfc(node.Label).Replace('∕', '/'));
            label.AddToClassList(Block + "__label");
            row.Add(label);
            Label arrow = new(node.IsSubmenu ? "›" : string.Empty);
            arrow.AddToClassList(Block + "__arrow");
            row.Add(arrow);

            row.RegisterCallback<PointerEnterEvent>(_ => Hover(panel, row, level));
            // 줄 밖(여백·구분선·메뉴 바깥)으로 나가면 밝힘을 끈다. 하위 메뉴 줄은 열린 하위 메뉴로 옮겨 가는 길이라 그대로 둔다.
            row.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                PanelState state = (PanelState)panel.userData;
                if (node.IsSubmenu == false && state.Hovered >= 0 && state.Rows[state.Hovered] == row)
                {
                    SetHovered(panel, -1);
                }
            });
            row.RegisterCallback<PointerUpEvent>(evt =>
            {
                // 오른쪽 버튼은 메뉴를 연 그 누름이 떼어질 때(누를 때 여는 플랫폼) 바로 항목을 고르지 않게 잠깐 무시한다.
                bool rightAfterOpen = evt.button == 1 && Time.realtimeSinceStartupAsDouble - _openedAt < 0.3;
                if ((evt.button == 0 || evt.button == 1) && rightAfterOpen == false)
                {
                    Activate(node, row, level);
                    evt.StopPropagation();
                }
            });
            row.userData = node;
            return row;
        }

        private void Hover(VisualElement panel, VisualElement row, int level)
        {
            PanelState state = (PanelState)panel.userData;
            int index = state.Rows.IndexOf(row);
            SetHovered(panel, index);
            Node node = (Node)row.userData;
            // 하위 메뉴는 올리기만 해도 연다. 다른 줄로 옮기면 그 단계 아래를 닫는다.
            if (node.IsSubmenu)
            {
                OpenPanel(node, Vector2.zero, level + 1, row);
            }
            else
            {
                ClosePanelsFrom(level + 1);
            }
        }

        private static void SetHovered(VisualElement panel, int index)
        {
            PanelState state = (PanelState)panel.userData;
            for (int i = 0; i < state.Rows.Count; i++)
            {
                state.Rows[i].EnableInClassList(Block + "__item--hover", i == index);
            }
            state.Hovered = index;
        }

        private void Activate(Node node, VisualElement row, int level)
        {
            if (node.IsSubmenu)
            {
                OpenPanel(node, Vector2.zero, level + 1, row);
                VisualElement child = _panels[^1];
                SetHovered(child, FirstEnabled((PanelState)child.userData, 0, 1));
                return;
            }
            if ((node.Action.status & DropdownMenuAction.Status.Disabled) != 0)
            {
                return;
            }
            // 메뉴를 먼저 닫고 실행한다(실행이 창·팝업을 띄워도 메뉴가 남지 않게).
            Close();
            node.Action.Execute();
        }

        // ── 키보드 ─────────────────────────────────────────

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (_panels.Count == 0)
            {
                return;
            }
            VisualElement panel = _panels[^1];
            PanelState state = (PanelState)panel.userData;
            int level = _panels.Count - 1;
            switch (evt.keyCode)
            {
                case KeyCode.Escape:
                    if (level > 0)
                    {
                        ClosePanelsFrom(level);
                    }
                    else
                    {
                        Close();
                    }
                    break;
                case KeyCode.DownArrow:
                    SetHovered(panel, FirstEnabled(state, state.Hovered + 1, 1));
                    break;
                case KeyCode.UpArrow:
                    SetHovered(panel, FirstEnabled(state, state.Hovered < 0 ? state.Rows.Count - 1 : state.Hovered - 1, -1));
                    break;
                case KeyCode.RightArrow:
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    if (state.Hovered >= 0 && state.Hovered < state.Rows.Count)
                    {
                        Node node = (Node)state.Rows[state.Hovered].userData;
                        if (evt.keyCode == KeyCode.RightArrow && node.IsSubmenu == false)
                        {
                            break;
                        }
                        Activate(node, state.Rows[state.Hovered], level);
                    }
                    break;
                case KeyCode.LeftArrow:
                    if (level > 0)
                    {
                        ClosePanelsFrom(level);
                    }
                    break;
                default:
                    return;
            }
            evt.StopPropagation();
        }

        /// <summary>
        /// <paramref name="start"/> 부터 <paramref name="step"/> 방향으로 처음 누를 수 있는 줄(끝에 닿으면 반대쪽에서 다시). 없으면 -1.
        /// </summary>
        private static int FirstEnabled(PanelState state, int start, int step)
        {
            int count = state.Rows.Count;
            for (int n = 0; n < count; n++)
            {
                int i = ((start + n * step) % count + count) % count;
                Node node = (Node)state.Rows[i].userData;
                if (node.IsSubmenu || (node.Action.status & DropdownMenuAction.Status.Disabled) == 0)
                {
                    return i;
                }
            }
            return -1;
        }

        private void ClosePanelsFrom(int level)
        {
            for (int i = _panels.Count - 1; i >= level; i--)
            {
                _panels[i].RemoveFromHierarchy();
                _panels.RemoveAt(i);
            }
        }

        private void Close()
        {
            _panels.Clear();
            _overlay.RemoveFromHierarchy();
        }
    }
}
