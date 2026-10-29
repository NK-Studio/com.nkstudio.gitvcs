using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 작업 막대의 브랜치 이름을 누르면 그 아래에 펼치는 브랜치 고르기(GitKraken 과 같다). 맨 위 검색창에 쓰는 대로 걸러지고,
    /// 누르거나 Enter 면 그 브랜치로 전환한다(<see cref="BranchActions.SwitchAsync"/> — 원격은 같은 이름 로컬이 있으면 그쪽, 없으면 추적 브랜치를 만든다).
    /// 따로 창을 띄우지 않고 Git 창 위에 덮어 띄운다(<see cref="GitServices.PopupHost"/>). 목록 밖을 누르거나 Esc 면 닫는다.
    /// 순서: 현재 브랜치 → 로컬(최근 커밋 순) → 원격. ↑·↓ 로 고르는 줄을 옮긴다.
    /// </summary>
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public sealed class GitBranchPicker
    {
        private const float RowHeight = 28f;
        private const float MinWidth = 320f;
        private const string HighlightModifier = "branch-picker__row--highlight";
        private const string CurrentModifier = "branch-picker__row--current";

        private readonly GitServices _services;
        private readonly Action<bool> _openChanged;
        private readonly VisualElement _overlay;
        private readonly VisualElement _panel;
        private readonly VisualElement _field;
        private readonly TextField _search;
        private readonly ListView _list;
        private readonly Label _message;
        private List<GitBranchRef> _all = new();
        private readonly List<GitBranchRef> _shown = new();
        private int _highlight;
        private bool _closed;

        /// <summary>
        /// <paramref name="anchor"/> 아래에 펼친다. 띄울 Git 창이 없으면 false(부른 쪽이 다른 길로 연다).
        /// </summary>
        /// <param name="openChanged">펼칠 때 true, 닫을 때 false(버튼의 ▾ 를 뒤집는 데 쓴다).</param>
        public static bool Show(GitServices services, VisualElement anchor, Action<bool> openChanged = null)
        {
            VisualElement host = services.PopupHost;
            if (host == null || host.panel == null)
            {
                return false;
            }
            _ = new GitBranchPicker(services, host, anchor, openChanged);
            return true;
        }

        private GitBranchPicker(GitServices services, VisualElement host, VisualElement anchor, Action<bool> openChanged)
        {
            _services = services;
            _openChanged = openChanged;

            // 창 전체를 덮는 투명한 막. 목록 밖을 누르면 닫는다(작업 막대의 브랜치 버튼을 다시 눌러도 닫힌다).
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

            _panel = new VisualElement();
            _panel.AddToClassList("branch-picker");
            GitShadow.Add(_panel, GitShadowSize.Large);
            _overlay.Add(_panel);

            // 검색창: 안쪽 왼쪽에 돋보기, 비어 있으면 "Search". 포커스면 바깥 상자를 강조색으로.
            _field = new VisualElement();
            _field.AddToClassList("branch-picker__field");
            ActionGlyph glyph = new(ActionGlyphKind.Search);
            glyph.AddToClassList("branch-picker__search-glyph");
            _field.Add(glyph);
            _search = new TextField();
            _search.AddToClassList("branch-picker__search");
            _search.textEdition.placeholder = GitText.Get("branchPicker.search");
            _search.textEdition.hidePlaceholderOnFocus = false;
            _search.RegisterValueChangedCallback(_ => Filter());
            _search.RegisterCallback<FocusInEvent>(_ => _field.AddToClassList("branch-picker__field--focused"));
            _search.RegisterCallback<FocusOutEvent>(_ => _field.RemoveFromClassList("branch-picker__field--focused"));
            _field.RegisterCallback<PointerDownEvent>(_ => _search.Focus());
            _field.Add(_search);
            _panel.Add(_field);

            _list = new ListView
            {
                fixedItemHeight = RowHeight,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                selectionType = SelectionType.None,
                makeItem = MakeRow,
                bindItem = BindRow,
                itemsSource = _shown,
            };
            _list.AddToClassList("branch-picker__list");
            // 다 읽기 전에는 목록 자리를 비워 두고 "읽는 중…" 만 보인다(Filter 가 걸린 줄이 있을 때 연다).
            _list.SetVisible(false);
            _panel.Add(_list);

            _message = new Label(GitText.Get("branchPicker.empty"));
            _message.AddToClassList("branch-picker__message");
            _message.SetVisible(false);
            _panel.Add(_message);

            // ↑·↓ 로 고르는 줄을 옮기고, Enter 는 전환, Esc 는 닫기. 검색창에 포커스가 있어도 받고 밑의 창으로 넘기지 않는다.
            _panel.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            _overlay.RegisterCallback<DetachFromPanelEvent>(_ => Finish());

            // 누르자마자 다 그린 목록이 보이게 미리 읽어 둔 목록(Prefetch)으로 먼저 채운다. 아직 없으면 지금 브랜치 한 줄부터 보인다.
            _all = CachedOrCurrent(services);
            Filter();

            host.Add(_overlay);
            Place(host, anchor);
            _openChanged?.Invoke(true);
            // 바로 쓸 수 있게 지금 포커스한다(패널에 붙기 전이면 안 되므로 다음 프레임에 한 번 더).
            _search.Focus();
            _search.schedule.Execute(() =>
            {
                if (_closed == false)
                {
                    _search.Focus();
                }
            });
            Refresh();
        }

        // ── 미리 읽어 두기 ──
        // git for-each-ref 는 브랜치가 많으면 수십~수백 ms 걸린다. 펼칠 때 읽으면 빈 상자가 먼저 보였다가 채워지므로,
        // 창이 저장소를 새로 읽을 때마다 뒤에서 읽어 두고(GitWindow → Prefetch) 펼칠 때는 그 목록을 바로 쓴다.

        private static List<GitBranchRef> s_cached;
        private static string s_cachedRoot;
        private static Task<(List<GitBranchRef> branches, string error)> s_loading;

        /// <summary>
        /// 브랜치 목록을 뒤에서 읽어 둔다. 이미 읽는 중이면 그것을 기다린다.
        /// </summary>
        public static Task<(List<GitBranchRef> branches, string error)> Prefetch()
        {
            if (s_loading == null || s_loading.IsCompleted)
            {
                s_loading = LoadSortedAsync();
            }
            return s_loading;
        }

        private static async Task<(List<GitBranchRef> branches, string error)> LoadSortedAsync()
        {
            string root = GitRepository.RepoRoot;
            (List<GitBranchRef> branches, string error) = await GitHistory.BranchesAsync();
            // 현재 브랜치 → 로컬 → 원격. 같은 무리 안에서는 읽은 순서(최근 커밋 순)를 지킨다.
            List<GitBranchRef> sorted = (branches ?? new List<GitBranchRef>())
                .OrderByDescending(b => b.IsCurrent)
                .ThenBy(b => b.IsRemote)
                .ToList();
            if (string.IsNullOrEmpty(error) || sorted.Count > 0)
            {
                s_cached = sorted;
                s_cachedRoot = root;
            }
            return (sorted, error);
        }

        /// <summary>
        /// 같은 저장소에서 읽어 둔 목록. 없으면 저장소 상태가 아는 지금 브랜치 한 줄.
        /// </summary>
        private static List<GitBranchRef> CachedOrCurrent(GitServices services)
        {
            if (s_cached != null && s_cachedRoot == GitRepository.RepoRoot)
            {
                return s_cached;
            }
            GitBranchInfo branch = services.Repository.Branch;
            return branch == null || branch.Detached
                ? new List<GitBranchRef>()
                : new List<GitBranchRef> { new() { Name = branch.Name, IsCurrent = true } };
        }

        /// <summary>
        /// 펼친 뒤 최신 목록을 다시 읽어, 달라졌을 때만 바꾼다(같으면 그대로 두어 깜빡이지 않는다). 쓰던 검색어·고른 줄은 지킨다.
        /// </summary>
        private async void Refresh()
        {
            (List<GitBranchRef> branches, string error) = await Prefetch();
            if (_closed)
            {
                return;
            }
            if (branches.Count == 0 && string.IsNullOrEmpty(error) == false)
            {
                if (_all.Count == 0)
                {
                    _message.text = error;
                    _message.SetVisible(true);
                }
                return;
            }
            if (SameBranches(_all, branches))
            {
                return;
            }
            string highlighted = _highlight < _shown.Count ? _shown[_highlight].Name : null;
            _all = branches;
            Filter(highlighted);
        }

        private static bool SameBranches(List<GitBranchRef> a, List<GitBranchRef> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i].Name != b[i].Name || a[i].IsCurrent != b[i].IsCurrent || a[i].IsRemote != b[i].IsRemote)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 버튼이 든 칸(“branch” 글자와 이름) 왼쪽 아래에 붙인다. 창 오른쪽을 넘으면 안으로 당긴다.
        /// </summary>
        private void Place(VisualElement host, VisualElement anchor)
        {
            Rect bound = anchor.worldBound;
            Vector2 topLeft = host.WorldToLocal(new Vector2(bound.xMin, bound.yMax));
            float width = Mathf.Max(MinWidth, host.WorldToLocal(new Vector2(bound.xMax, 0f)).x - topLeft.x);
            float hostWidth = host.contentRect.width;
            float left = float.IsNaN(hostWidth) ? topLeft.x : Mathf.Clamp(topLeft.x, 4f, Mathf.Max(4f, hostWidth - width - 4f));
            _panel.style.left = left;
            _panel.style.top = topLeft.y + 2f;
            _panel.style.width = width;
        }

        /// <summary>
        /// 검색어를 빈칸으로 나눠 모두 들어 있는 이름만 남긴다(대소문자 무시).
        /// 고르는 줄은 첫 줄로 돌린다. <paramref name="keepHighlight"/> 가 남아 있으면 그 브랜치를 계속 고른다(뒤에서 목록만 새로 읽었을 때).
        /// </summary>
        private void Filter(string keepHighlight = null)
        {
            string[] terms = (_search.value ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            _shown.Clear();
            foreach (GitBranchRef branch in _all)
            {
                string name = GitDisplay.Nfc(branch.Name);
                if (terms.All(term => name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    _shown.Add(branch);
                }
            }
            int kept = keepHighlight == null ? -1 : _shown.FindIndex(b => b.Name == keepHighlight);
            _highlight = Mathf.Max(kept, 0);
            _list.RefreshItems();
            if (_shown.Count > 0)
            {
                _list.ScrollToItem(_highlight);
            }
            // 목록 높이는 줄 수만큼(최대 높이는 USS). 걸린 것이 없으면 안내 글만 보인다.
            _list.style.height = Mathf.Min(_shown.Count, 12) * RowHeight;
            _list.SetVisible(_shown.Count > 0);
            _message.text = GitText.Get("branchPicker.empty");
            _message.SetVisible(_shown.Count == 0);
        }

        private VisualElement MakeRow()
        {
            VisualElement row = new();
            row.AddToClassList("branch-picker__row");
            ActionGlyph branch = new(ActionGlyphKind.Branch);
            branch.AddToClassList("branch-picker__glyph");
            branch.AddToClassList("branch-picker__glyph--local");
            row.Add(branch);
            ActionGlyph cloud = new(ActionGlyphKind.Cloud);
            cloud.AddToClassList("branch-picker__glyph");
            cloud.AddToClassList("branch-picker__glyph--remote");
            row.Add(cloud);
            Label name = new();
            name.AddToClassList("branch-picker__name");
            name.AddToClassList("ellipsis");
            name.pickingMode = PickingMode.Ignore;
            row.Add(name);
            row.RegisterCallback<PointerEnterEvent>(_ =>
            {
                if (row.userData is int index)
                {
                    SetHighlight(index, scroll: false);
                }
            });
            row.RegisterCallback<ClickEvent>(_ =>
            {
                if (row.userData is int index && index < _shown.Count)
                {
                    Pick(_shown[index]);
                }
            });
            return row;
        }

        private void BindRow(VisualElement row, int index)
        {
            GitBranchRef branch = _shown[index];
            row.userData = index;
            row.EnableInClassList("branch-picker__row--remote", branch.IsRemote);
            row.EnableInClassList(CurrentModifier, branch.IsCurrent);
            row.EnableInClassList(HighlightModifier, index == _highlight);
            row.Q<Label>(className: "branch-picker__name").text = GitDisplay.Nfc(branch.Name);
        }

        private void SetHighlight(int index, bool scroll)
        {
            if (_shown.Count == 0)
            {
                return;
            }
            _highlight = Mathf.Clamp(index, 0, _shown.Count - 1);
            _list.Query<VisualElement>(className: "branch-picker__row").ForEach(row => row.EnableInClassList(HighlightModifier, row.userData is int i && i == _highlight));
            if (scroll)
            {
                _list.ScrollToItem(_highlight);
            }
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            switch (evt.keyCode)
            {
                case KeyCode.DownArrow:
                    SetHighlight(_highlight + 1, scroll: true);
                    break;
                case KeyCode.UpArrow:
                    SetHighlight(_highlight - 1, scroll: true);
                    break;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    if (_highlight < _shown.Count)
                    {
                        Pick(_shown[_highlight]);
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

        /// <summary>
        /// 고른 브랜치로 전환한다. 이미 현재 브랜치거나 다른 git 작업 중이면 닫기만 한다.
        /// </summary>
        private void Pick(GitBranchRef branch)
        {
            Close();
            if (branch.IsCurrent || _services.Runner.IsBusy)
            {
                return;
            }
            HashSet<string> localNames = new(_all.Where(b => b.IsRemote == false).Select(b => b.Name));
            _ = new BranchActions(_services).SwitchAsync(branch, localNames);
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
