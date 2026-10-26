using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 작업 막대의 저장소 이름을 누르면 그 아래에 펼치는 저장소 고르기. 프로젝트 저장소와 그 서브모듈을 보이고,
    /// 누르거나 Enter 면 Git 창이 그 저장소를 연다(<see cref="GitRepository.OpenRepository"/>). 받지 않은 서브모듈은 고를 수 없다.
    /// <see cref="GitBranchPicker"/> 와 같은 막·목록 상자에 그린다. 목록 밖을 누르거나 Esc 면 닫는다. ↑·↓ 로 고르는 줄을 옮긴다.
    /// </summary>
    public sealed class GitRepositoryPicker
    {
        private const string HighlightModifier = "branch-picker__row--highlight";
        private const float MinWidth = 260f;

        private sealed class Item
        {
            public string Name;
            public string Detail;
            public string Root;
            public bool IsMain;
            public bool IsOpen;
            public bool Enabled = true;
            public VisualElement Row;
        }

        private readonly Action<bool> _openChanged;
        private readonly VisualElement _overlay;
        private readonly VisualElement _panel;
        private readonly List<Item> _items = new();
        private int _highlight;
        private bool _closed;

        /// <summary>
        /// <paramref name="anchor"/> 아래에 펼친다. 띄울 Git 창이 없거나 프로젝트 저장소를 아직 모르면 false.
        /// </summary>
        /// <param name="openChanged">펼칠 때 true, 닫을 때 false(버튼의 ▾ 를 뒤집는 데 쓴다).</param>
        public static bool Show(GitServices services, VisualElement anchor, Action<bool> openChanged = null)
        {
            VisualElement host = services.PopupHost;
            if (host == null || host.panel == null || string.IsNullOrEmpty(GitRepository.MainRepoRoot))
            {
                return false;
            }
            _ = new GitRepositoryPicker(host, anchor, openChanged);
            return true;
        }

        private GitRepositoryPicker(VisualElement host, VisualElement anchor, Action<bool> openChanged)
        {
            _openChanged = openChanged;

            _overlay = new VisualElement();
            _overlay.AddToClassList("branch-picker-overlay");
            _overlay.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.target == _overlay)
                {
                    Close();
                    evt.StopPropagation();
                }
            });

            _panel = new VisualElement { focusable = true };
            _panel.AddToClassList("branch-picker");
            _panel.AddToClassList("repo-picker");
            _overlay.Add(_panel);
            _panel.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            _overlay.RegisterCallback<DetachFromPanelEvent>(_ => Finish());

            // 프로젝트 저장소는 바로 보이고, 서브모듈은 읽는 대로 아래에 붙인다.
            string mainRoot = GitRepository.MainRepoRoot.TrimEnd('/');
            AddItem(new Item
            {
                Name = Path.GetFileName(mainRoot),
                Detail = GitText.Get("repoPicker.project"),
                Root = null,
                IsMain = true,
                IsOpen = GitRepository.OpenedRoot == null,
            });
            Label loading = new(GitText.Get("common.loading"));
            loading.AddToClassList("branch-picker__message");
            _panel.Add(loading);

            host.Add(_overlay);
            Place(host, anchor);
            _openChanged?.Invoke(true);
            _panel.Focus();
            _panel.schedule.Execute(() =>
            {
                if (_closed == false)
                {
                    _panel.Focus();
                }
            });
            LoadSubmodules(mainRoot, loading);
        }

        private async void LoadSubmodules(string mainRoot, Label message)
        {
            // submodule status 대신 인덱스의 gitlink 만 읽는다(GitRefs.GitlinksCommand 참고). 받았는지는 .git 이 있는지로 본다.
            GitResult result = await GitCommand.RunAsync(mainRoot, GitRefs.GitlinksCommand);
            if (_closed)
            {
                return;
            }
            if (result.Success == false)
            {
                message.text = result.Message;
                return;
            }
            List<string> submodulePaths = GitRefs.ParseGitlinkPaths(result.Output);
            if (submodulePaths.Count == 0)
            {
                message.text = GitText.Get("actionbar.noSubmodules");
                return;
            }
            message.RemoveFromHierarchy();
            VisualElement divider = new();
            divider.AddToClassList("repo-picker__divider");
            _panel.Add(divider);
            string opened = GitRepository.OpenedRoot;
            foreach (string submodulePath in submodulePaths)
            {
                string root = $"{mainRoot}/{submodulePath}";
                string dotGit = $"{root}/.git";
                bool initialized = Directory.Exists(dotGit) || File.Exists(dotGit);
                AddItem(new Item
                {
                    Name = Path.GetFileName(submodulePath.TrimEnd('/')),
                    Detail = initialized ? submodulePath : submodulePath + GitText.Get("actionbar.submoduleNotInitialized"),
                    Root = root,
                    IsOpen = string.Equals(opened, root, StringComparison.OrdinalIgnoreCase),
                    Enabled = initialized,
                });
            }
        }

        private void AddItem(Item item)
        {
            int index = _items.Count;
            VisualElement row = new();
            row.AddToClassList("branch-picker__row");
            row.AddToClassList("repo-picker__row");
            row.EnableInClassList("branch-picker__row--current", item.IsOpen);
            row.EnableInClassList("repo-picker__row--disabled", item.Enabled == false);

            ActionGlyph glyph = new(item.IsMain ? ActionGlyphKind.Folder : ActionGlyphKind.Submodule);
            glyph.AddToClassList("branch-picker__glyph");
            row.Add(glyph);
            Label name = new(item.Name) { pickingMode = PickingMode.Ignore };
            name.AddToClassList("branch-picker__name");
            name.AddToClassList("ellipsis");
            row.Add(name);
            Label detail = new(item.Detail) { pickingMode = PickingMode.Ignore };
            detail.AddToClassList("repo-picker__detail");
            detail.AddToClassList("ellipsis");
            row.Add(detail);

            if (item.Enabled)
            {
                row.RegisterCallback<PointerEnterEvent>(_ => SetHighlight(index));
                row.RegisterCallback<ClickEvent>(_ => Pick(item));
            }
            item.Row = row;
            _items.Add(item);
            _panel.Add(row);
            if (item.IsOpen || index == 0)
            {
                SetHighlight(index);
            }
        }

        /// <summary>버튼이 든 칸 왼쪽 아래에 붙인다. 창 오른쪽을 넘으면 안으로 당긴다.</summary>
        private void Place(VisualElement host, VisualElement anchor)
        {
            Rect bound = anchor.worldBound;
            Vector2 topLeft = host.WorldToLocal(new Vector2(bound.xMin, bound.yMax));
            float width = Mathf.Max(MinWidth, host.WorldToLocal(new Vector2(bound.xMax, 0f)).x - topLeft.x);
            float hostWidth = host.contentRect.width;
            float left = float.IsNaN(hostWidth) ? topLeft.x : Mathf.Clamp(topLeft.x, 4f, Mathf.Max(4f, hostWidth - width - 4f));
            _panel.style.left = left;
            _panel.style.top = topLeft.y + 2f;
            _panel.style.minWidth = width;
        }

        private void SetHighlight(int index)
        {
            if (index < 0 || index >= _items.Count || _items[index].Enabled == false)
            {
                return;
            }
            _highlight = index;
            for (int i = 0; i < _items.Count; i++)
            {
                _items[i].Row.EnableInClassList(HighlightModifier, i == _highlight);
            }
        }

        /// <summary>↑·↓ 로 고를 수 있는 다음 줄로(받지 않은 서브모듈은 건너뛴다).</summary>
        private void MoveHighlight(int step)
        {
            for (int i = _highlight + step; i >= 0 && i < _items.Count; i += step)
            {
                if (_items[i].Enabled)
                {
                    SetHighlight(i);
                    return;
                }
            }
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            switch (evt.keyCode)
            {
                case KeyCode.DownArrow:
                    MoveHighlight(1);
                    break;
                case KeyCode.UpArrow:
                    MoveHighlight(-1);
                    break;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    if (_highlight < _items.Count)
                    {
                        Pick(_items[_highlight]);
                    }
                    break;
                case KeyCode.Escape:
                    Close();
                    break;
                default:
                    return;
            }
            evt.StopPropagation();
        }

        private void Pick(Item item)
        {
            Close();
            if (item.IsOpen == false)
            {
                GitRepository.OpenRepository(item.Root);
            }
        }

        private void Close()
        {
            _overlay.RemoveFromHierarchy();
            Finish();
        }

        private void Finish()
        {
            if (_closed)
            {
                return;
            }
            _closed = true;
            _openChanged?.Invoke(false);
        }
    }
}
