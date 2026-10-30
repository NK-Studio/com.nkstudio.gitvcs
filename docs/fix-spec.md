# Git VCS 수정 스펙

- 기준 커밋: `e09f997` (0.1.0) · Unity 6000.7.0b5 · macOS · 2026-10-29
- 근거: Unity 에디터에서 직접 조작한 실사용 테스트, GitKraken 12.6.1 의 Pull Requests 패널 조사
- 경로는 `Editor/` 기준, 줄 번호는 기준 커밋 시점

## 공통 원칙

1. 묻기·알리기·입력은 모두 Git 창(또는 도구 창) 안의 UI Toolkit 오버레이로 띄운다. OS 대화상자와 별도 모달 창은 쓰지 않는다.
2. 클릭하면 0.1초 안에 결과(예측 상태, 진행 표시, 비활성화)가 보인다.
3. 낙관적 예측은 작업이 어떻게 끝나든 반드시 거둔다.
4. `.meta` 와 폴더는 한 묶음으로 다루고, git 이 파일을 바꾼 뒤에는 에디터 메모리가 디스크를 덮어쓰지 않게 한다.
5. 오류는 원인에 맞는 다음 행동을 준다. "새로고침 후 다시 시도" 는 분류되지 않은 오류에만 쓴다.
6. 툴바 버튼은 문구 길이와 무관하게 제자리에 있다.

## POP · 팝업 UI 통일

현재 `IDialogService`(`UI/Services/IDialogService.cs`)가 `EditorUtility.DisplayDialog` 로 구현되어 있고, UI Element 카드(`GitConfirmDialog`, `GitErrorDialog`)는 일부 경로만 쓴다.

| ID | 요구 |
|---|---|
| POP-01 | `IDialogService` 를 `ConfirmAsync` / `ChooseAsync` / `AlertAsync` / `PromptAsync` 비동기 API 로 바꾸고 기본 구현은 `GitConfirmDialog` 오버레이. 위험 동작은 `danger` 옵션으로 경고색 + 기본 포커스 취소 |
| POP-02 | `Dialogs.Confirm/Alert/Choose` 직접 호출 47곳을 모두 비동기 API 로 전환 (아래 목록) |
| POP-03 | `GitTextPromptWindow`(`ShowModalUtility`, 9곳)를 오버레이 입력 카드로 교체. 입력 중 검증, 실패해도 닫지 않고 값 유지 |
| POP-04 | `GitToolWindowBase` 계열 도구 창도 자신의 루트를 팝업 호스트로 쓴다 |
| POP-05 | 호스트가 없으면 OS 대화상자로 떨어지지 않는다. Git 창을 열어 그 위에 띄우고, 열 수 없으면 취소로 처리하고 경고 로그 |
| POP-06 | Project Settings > Git 의 git init 확인·실패(`UI/GitSettingsGUI.cs:426, 436`)를 설정 화면 안 인라인 확인으로 |
| POP-07 | `UI/Window/GitWindow.cs:279` 직접 `DisplayDialog` 제거, 터미널 열기 실패 알림 하나로 통합 |
| POP-08 | 우클릭 메뉴는 항목을 고르면 즉시 닫고, 팝업이 열리면 메뉴·드롭다운·툴팁을 모두 닫는다 |
| POP-09 | 카드 종류: 확인 / 선택 / 알림 / 입력 / 오류. 막 클릭으로 닫지 않음, Esc 취소, 한 번에 하나(나머지는 대기열) |
| POP-10 | 병합 충돌은 빨간 오류 대신 안내 카드 + [충돌 보기] |
| POP-11 | 오류 카드에 해결 버튼: Push 거절 → Pull, 리모트 없음 → 리모트 추가, Pop 실패 → 로컬 변경 처리 후 Pop |
| POP-12 | 카드를 닫을 때 포커스를 돌려줘 `FocusController has unprocessed focus events` 경고 제거 |
| POP-13 | 소스에 `DisplayDialog`, `DisplayDialogComplex`, `ShowModalUtility` 가 있으면 실패하는 EditMode 테스트 |

### OS 대화상자 호출부 (관찰 = 테스트 중 OS 창으로 뜨는 것을 확인)

| 위치 | 종류 | 상황 | 관찰 |
|---|---|---|---|
| `UI/Views/BranchActions.cs:54` | Alert | 브랜치 이름 오류 | ✔ |
| `UI/Views/BranchActions.cs:67, 81, 87` | Confirm/Alert | 브랜치 삭제, 강제 삭제, 삭제 실패 | ✔ |
| `UI/Views/BranchActions.cs:101` | Alert | 이름 바꾸기 오류 | |
| `UI/Views/BranchActions.cs:113` | Confirm | 브랜치 푸시 | ✔ |
| `UI/Views/BranchActions.cs:130, 157` | Confirm/Alert | 원격 브랜치 삭제, 업스트림 없음 | |
| `UI/Views/CommitActions.cs:55, 90, 116, 143` | Alert | 리비전·브랜치·태그 이름 오류 | |
| `UI/Views/CommitActions.cs:187, 205, 257, 287, 293, 306` | Confirm/Alert | 리베이스, 실패, 다시 쓰기, fixup, drop, 빈 메시지 | |
| `UI/Views/CommitActions.cs:350` | Confirm | 병합·리셋·체리픽·되돌리기 | ✔ |
| `UI/Views/RefActions.cs:69, 78, 87, 109` | Confirm | 태그 푸시·삭제, stash 삭제 | ✔ |
| `UI/Views/RefActions.cs:132, 154, 160` | Alert/Confirm | stash 브랜치 이름, 터미널 실패, 워크트리 삭제 | ✔ |
| `UI/Views/PendingChangesView.cs:300` | Confirm | 변경 버리기 | |
| `UI/Views/GraphWorkspaceView.cs:345` | Confirm | 모두 버리기 | |
| `UI/Elements/FileActionMenu.cs:89, 203, 232, 256, 262` | Confirm/Alert | 마커째 해결, 패치 실패, 파일 삭제, 새 파일 이름 | ✔ |
| `UI/Elements/CommitFileMenu.cs:106` | Confirm | 파일을 리비전으로 되돌리기 | |
| `UI/Elements/StagingLists.cs:505` | Choose | 내장 저장소 처리 | |
| `UI/Elements/OperationBanner.cs:72, 82` | Confirm | Skip / Abort | |
| `UI/Elements/GitDiffPanel.cs:1964` | Confirm | Discard Hunk | ✔ |
| `UI/Window/GitToolbar.cs:244` | Alert | 터미널 실패 | |
| `UI/Window/GitAccountDialog.cs:41` | Alert | 계정 연결 실패 | |
| `UI/Window/GitConfirmDialog.cs:58–105`, `GitErrorDialog.cs:41` | 대체 경로 | 호스트 없을 때 | |
| `UI/Services/IGitOperationRunner.cs:102, 135, 146` | Alert/Confirm | 실패, index.lock | |
| `UI/Services/GitDiffService.cs:279–322` | Alert/Confirm | 외부 diff 도구 | |
| `UI/Window/GitWindow.cs:279`, `UI/GitSettingsGUI.cs:426, 436` | DisplayDialog 직접 | 터미널, git init | |
| `UI/Window/GitTextPromptWindow.cs:39` | 별도 모달 창 | 이름·메시지 입력 9곳 | ✔ |

범위 밖(유지): 파일·폴더 선택 창(`EditorUtility.Open*/Save*Panel`), 메인 툴바 `GenericMenu`.

## P0 · 데이터 손상·막힘

| ID | 현상 | 원인 | 요구 |
|---|---|---|---|
| P0-01 | 충돌 파일을 Stage File 한 뒤 다시 고쳐도(`MM`) Unstaged 에 안 나옴 | `UI/Elements/StagingLists.cs:250` `RunPredicted` 의 `_prediction` 이 남음 | 예측은 작업이 끝나면(성공·실패·예외) 반드시 제거, 시간 제한, 실제 상태 우선 |
| P0-02 | 갈라진 브랜치에서 Pull 실패, 선택지 없음 | `Core/GitOperations.cs:177` `pull --ff-only` | ff 불가 시 병합/리베이스 선택 카드. 기본값은 `pull.rebase` 설정 |
| P0-03 | 충돌 마커가 남은 파일을 hover Stage File · Stage Hunk 로 경고 없이 스테이징 | 경고가 Mark as Resolved 에만 있음 | 모든 스테이징 경로에서 마커 검사 후 위험 확인 |
| P0-04 | 나란히 보기에서 수정 줄의 오른쪽 + 만 누르면 추가 줄만 스테이징 → 원래 줄과 새 줄이 모두 index 에 | `UI/Elements/GitDiffPanel.cs` `BindSide` | 수정 쌍(삭제+추가)을 한 단위로 스테이징·버리기 |
| P0-05 | Stash 뒤 Unity 가 메모리 상태로 URP 에셋을 다시 써서 Pop 실패 | git 작업 전후 에셋 저장·재임포트 없음 | 작업 전 `SaveAssets`, 후 바뀐 에셋 강제 재임포트. Pop 실패 시 해결 버튼 |
| P0-06 | 커밋 성공 뒤에도 메시지가 입력칸에 남음(브랜치 전환·병합 중에도) | | 커밋 성공 즉시 비우기, 병합 중에는 `MERGE_MSG` |

## P1 · 기능 버그

| ID | 현상 | 요구 |
|---|---|---|
| P1-01 | 병합 커밋이 "Merge into Current Branch" 로 표시 — `Core/GitStrings.cs:506`, `:749` 키 `commit.merge` 중복 | 목록용 키 분리, 중복 키 검사 테스트 |
| P1-02 | 리모트 이름 `origin` 고정 (`Core/GitOperations.cs:184`, 브랜치 Push 메뉴) | 리모트가 여럿이면 선택, 없으면 리모트 추가 안내 |
| P1-03 | 리모트 없을 때 Commit & Push 가 "failed" — 실제로는 커밋 성공 | 리모트 없으면 버튼 비활성, 결과를 커밋/푸시로 나눠 표시 |
| P1-04 | 행 툴팁이 아래 행 클릭을 가로챔 | 툴팁 `pickingMode = Ignore`, 클릭 시 숨김 |
| P1-05 | 낡은 툴팁·상태 문구(Continue 툴팁, "2 outgoing", "Track … failed") | 툴팁은 열 때마다 다시 계산, 상태 문구는 다음 작업 시작 또는 10초 뒤 지움 |
| P1-06 | Stash 직후 그래프에 `refs/stash` 없음 | stash·pop·drop 은 전체 새로고침 |
| P1-07 | Esc 한 번에 서랍 전체가 닫힘 | 가장 위 하나만 닫음 |
| P1-08 | 워크트리 생성 중 "This folder isn't empty." 거짓 오류 | 작업 중 검증 중지 |
| P1-09 | `Unknown pseudo class "last-child"` 경고 | 지원하지 않는 의사 클래스 제거 |
| P1-10 | PR 목록이 Fetch 로 갱신 안 됨 | Fetch 뒤 PR 목록 다시 읽기 |
| P1-11 | Add Remote > GitHub 저장소 목록 캐시 | 드롭다운 열 때 다시 읽기 + 새로고침 버튼 |
| P1-12 | 같은 경로가 Staged·Unstaged 양쪽인 경우 표시 | 회귀 테스트 |

## P2 · 반응 속도

| ID | 측정 | 요구 |
|---|---|---|
| P2-01 | 툴바 버튼이 상태 문구·브랜치 이름에 따라 30px 넘게 이동, 실제 오클릭 | 상태 문구 영역 폭 고정 |
| P2-02 | Stage File 약 1초 뒤 반영 | 낙관적 갱신 (P0-01 선행) |
| P2-03 | 커밋 후 그래프 2~2.5초 | 진행 표시 |
| P2-04 | Discard Hunk 2초, 언어 전환 1~2초 무표시 | 진행 표시 |
| P2-05 | 새로고침 중 Stage All 무시 | 대기열에 넣어 실행 |
| P2-06 | macOS 작업 트리 변경이 포커스 복귀 때만 반영, diff 휠 1칸=1줄 | Refresh 즉시 실행, 휠 줄 수 OS 설정, Cmd+Home/End |

## P3 · UX·Unity 워크플로

| ID | 현상 | 요구 |
|---|---|---|
| P3-01 | Stage Folder 가 폴더 자신의 `.meta` 를 빠뜨림 → 브랜치 전환 뒤 빈 폴더 재생성 경고 | 폴더 단위 동작에 `폴더.meta` 포함 |
| P3-02 | `.meta` 없는 새 에셋 커밋 경고 없음 | 커밋 전 짝 검사 경고 |
| P3-03 | 커밋 상세는 `.meta` 를 묶지 않음 | 변경 패널과 같은 묶음 |
| P3-04 | 충돌 시 패널 자동 전환 없음, 충돌 구역 없음, 진행 중 Pull/Push 활성 | 자동 전환, 충돌 구역, 진행 중 비활성 |
| P3-05 | 브랜치 이름 칸 placeholder 잘림, 실시간 검증 없음, 오류 시 입력값 버림 | POP-03 |
| P3-06 | Discard Hunk 후 diff 화면이 닫힘 | 남은 변경이 있으면 유지 |
| P3-07 | "되돌릴 수 없음" 문구 vs 툴바 Undo | 문구 정리 |
| P3-08 | 리모트 메뉴에 Fetch 만, 빈 리모트 안 보임 | 이름 변경·URL 수정·삭제, 빈 리모트 표시 |
| P3-09 | PR 제목 기본값이 병합 커밋, 생성 후 브라우저가 포커스 가져감, PR 클릭 무반응 | 병합 커밋 건너뛰기, 브라우저 열기 끔, 클릭 시 그래프 선택 |
| P3-10 | 첫 Push 후 PR 제안 없음, 브랜치 삭제 시 원격 삭제 선택 없음 | 결과 카드에 [PR 만들기], 체크박스 |
| P3-11 | 언어 전환 후 영어 혼재 | git 용어 외 번역 |
| P3-12 | 토글·비활성 버튼 상태 구분, 작은 + 버튼, 레인 색 변화, untracked diff 헤더 | 각각 수정 |

## PR · GitKraken 과 같은 Pull Requests 패널

GitKraken 12.6.1 에서 `www` 에 PR 3개(#2 담당자 지정, #3, #4 draft)를 만들고 조사한 결과.

### 구성

- 구역 머리: `PULL REQUESTS` + 전체 개수
- 머리 아래: `Search pull requests` 검색칸 + 필터 추가 버튼(깔때기+)
- 필터 그룹(기본 4개): My Pull Requests, Assigned To Me, Awaiting My Review, All Pull Requests. 각 그룹 아래 리모트(GitHub 아이콘 + `origin`)로 한 번 더 묶고 그 아래 PR
- PR 줄: 상태 아이콘 + `#번호 제목`, draft 는 제목에 표시. 호버하면 카드(제목 #번호, "작성자 wants to merge head → base")

### 그룹(필터) ⋮ 메뉴

```
Edit this filter
Remove this filter
─────────────
Move this filter up      (첫 그룹 제외)
Move this filter down    (마지막 그룹 제외)
```

- 필터 추가/편집 서랍: `Filter name`, `Filter Pull Requests`(GitHub 검색 문법 쿼리), `Create Filter`/`Edit Filter`, 아래에 문법 도움말(author, assignee, involves, review-requested, reviewed-by, base, head, draft, label, review, status, no, created, updated)
- 기본 쿼리: My = `author:@me`, Assigned = `assignee:@me`, Awaiting = `review-requested:@me`, All = (조건 없음)

### PR ⋮ 메뉴

```
View pull request #N on GitHub.com
Copy link for pull request #N
─────────────
View changes in #N
Go to <head> in graph
─────────────
Checkout <remote>/<head>
Create Worktree from Pull Request
```

(GitKraken 의 "Review on GitKraken.dev" 는 대응 서비스가 없어 제외)

### PR 클릭

PR 상세 화면: `#번호`, 제목, 상태 배지(Open/Draft), "작성자 wants to merge head into base", 설명, 충돌 여부, `Merge Pull Request` + 방식 드롭다운(Create a merge commit / Squash and merge / Rebase and merge), 오른쪽에 바뀐 파일 수·Reviewers·Assignees·Labels·Branch.

## 검증

- 자동: POP-13, P1-01 중복 키, P0-01 예측 해제, P0-04 패치, P0-02 인자, P0-03 마커 검사
- 수동: 테스트 시나리오 재실행 — OS 창 0개, 클릭 0.1초 내 반응, 콘솔 경고 0개

## 진행 상태

검증(1차): `dotnet build`(패키지·테스트 csproj) 오류 0, Unity 재컴파일 오류 0, EditMode 테스트 804개 중 795 통과·8 건너뜀·1 실패
(`GitCommitTemplateTests` 의 `C:/` Windows 경로 케이스 — 이번에 건드리지 않은 코드, macOS 에서만 실패). 새 회귀 테스트 41개(`FixSpecRegressionTests`) 모두 통과.
화면 확인은 화면이 잠긴 상태라 마우스 대신 에디터 명령(창 캡처·코드 실행)으로 했다.

| ID | 상태 | 비고 |
|---|---|---|
| POP-01~05, 07, 09, 12, 13 | 완료 | `OverlayDialogService`·`GitPopupHosts`·`GitPromptDialog`·`GitPickDialog`. 소스의 `DisplayDialog`/`ShowModalUtility` 0건(테스트로 고정) |
| POP-06 | 완료 | 설정 화면 git init 은 화면 안 확인 영역 |
| POP-08 | 완료 | OS 모달이 사라져 메뉴 잔상 원인 제거 |
| POP-10 | 완료 | 충돌은 안내 카드 + [충돌 보기] |
| POP-11 | 완료 | Push 거절→Pull, 리모트 없음→리모트 추가, Pop 막힘→버리고 Pop. git 메시지를 영어(`LC_MESSAGES=C`)로 받아 오류 분류가 동작하게 함 |
| P0-01 | 완료 | 예측 3초 수명 + 자기 예측만 해제 |
| P0-02 | 완료 | `pull.rebase` 설정 우선, ff 불가 시 병합/리베이스 선택 |
| P0-03 | 완료 | 목록·hunk·줄·파일 전체 스테이징 모두 마커 확인 |
| P0-04 | 완료 | 나란히 보기에서 같은 행의 짝 줄을 함께 스테이징 |
| P0-05 | 완료(완화) | 작업 전 `SaveAssets`, 후 동기 재임포트, Pop 막힘 해결 버튼. URP 가 옛 버전 에셋을 스스로 업그레이드해 다시 쓰는 것은 막을 수 없다 |
| P0-06 | 완료 | 커밋과 Push 분리, 커밋 성공 즉시 입력칸 비움 |
| P1-01~04, 06~11 | 완료 | |
| P1-05 | 완료 | 상태 문구는 10초 뒤 정리. 툴팁은 Unity 기본 툴팁 대신 우리 말풍선으로 바꿔(`HoverCallout.ReplaceTooltips`, 아래 "자체 툴팁") 뜰 때마다 글을 새로 읽는다 — 처음엔 "Unity 가 다시 읽지 않는다" 를 한계로 적었으나, 기본 툴팁을 계속 쓴다는 전제 안에서만 본 것이었다 |
| P1-12 | 완료 | 기존 `StagingLayoutTests` 가 검증, 원인은 P0-01 |
| P2-01, 02, 04, 05 | 완료 | |
| P2-03 | 완료(3차) | 커밋 클릭→그래프 반영 약 0.9초(아래 3차 개발 참고) |
| P2-06 | 완료(3차) | macOS FSEvents 감시(`MacDirectoryWatcher`) 추가 — 바깥에서 고친 파일이 Refresh 없이 약 0.6초 안에 보임 |
| P3-01~05, 07~12 | 완료 | |
| P3-06 | 의도된 동작 | 그 파일에 남은 Unstaged 변경이 없을 때만 닫힘 |
| PR 패널 | 완료 | 검색 칸·필터 추가, 필터(묶음) ⋮: 고치기/지우기/위로/아래로, 원격 줄, PR ⋮: GitHub 보기·링크 복사·변경 보기·그래프로 가기·체크아웃·워크트리. 필터 조건은 로컬 평가 + GitHub 검색 API |
| PR 상세 화면 | 완료(3차·4차) | PR 을 누르면 상세 서랍(`PullRequestDetailDrawer`). 대화·리뷰 작성·Draft 전환·닫기/다시 열기는 4차 |

## 2차 실사용 테스트 (2026-10-29)

마우스·키보드로 실제 조작. 다른 환경(Windows, Git 창 닫힘, 보조 창)은 제외.
검증: Unity 재컴파일 오류 0, EditMode 테스트 810개 중 801 통과·8 건너뜀·1 실패(1차와 같은 `C:/` 경로 케이스). 새 회귀 테스트 6개 추가.

### 통과

| 영역 | 확인한 것 |
|---|---|
| 스테이징 | Stage File 누르자마자 Staged 로 이동, 스테이징 뒤 또 고친 파일(MM)은 양쪽 목록에, 나란히 보기에서 한 줄 Stage 시 `-row 3`/`+row 3 CHANGED` 짝이 함께 들어감, 충돌 표식 경고(Stage File·Stage All) |
| 커밋 상자 | 커밋 직후 입력칸 비움, 새 에셋을 .meta 없이 커밋하면 경고(폴더 .meta 포함) → Include .meta Files 로 함께 커밋 |
| Push | 원격 둘이면 고르기 카드, 첫 Push 뒤 PR 만들기 제안 → 서랍이 브랜치·제목 채워 열림, 거절 → [Pull] 제안 |
| Pull | 갈라짐 → Merge/Rebase 카드, Merge 성공 |
| 충돌 | 안내 카드, 진행 배너(Continue/Abort/Skip), 진행 중 Pull·Push 꺼짐, 오른쪽 클릭으로 한쪽 고르기 → Continue, Abort |
| Stash·Pop | 막힘 → 버리고 Pop, 끝까지 복원 |
| PR 패널 | 필터 ⋮ 고치기·지우기(확인 카드)·위로·아래로, GitHub 검색 조건(`review:none` 3개, `review:approved` 0개), PR 메뉴 변경 보기·워크트리 만들기·체크아웃(작업 변경이 있으면 까닭 안내), 검색 칸 포커스·`#2` 거르기 |
| 원격 메뉴 | Prune, 이름 바꾸기(중복 이름 검사), URL 고치기, 삭제(확인 카드) |
| 툴바 | 배지·꺼짐 상태가 바뀌어도 버튼 자리 그대로 |

### 새로 찾아 고친 것

| ID | 증상 | 원인 → 수정 |
|---|---|---|
| R2-01 | 변경 묶음(Hunk) 보기 → 통합(Inline) 보기로 바꾸면 diff 가 빈 화면 | 가변 → 고정 높이로 바꾼 틀에 ListView 내용 높이가 0 으로 남음 → 바꾼 다음 틀에 한 번 더 채움(`GitDiffPanel.ApplyLayout`) |
| R2-02 | 충돌 카드 [충돌 보기] 가 오른쪽 패널만 바꾸고 파일을 열지 않음 | 첫 충돌 파일을 골라 diff 를 엶(`StagingLists.SelectFirstConflict`, `GraphWorkspaceView.ShowConflicts`) |
| R2-03 | 병합이 끝나도 커밋 제목 칸에 "Merge branch …" 가 남음 | 채운 준비 메시지를 메모리에만 들고 있어 병합 중 스크립트를 다시 읽으면 잊음 → `StagingState.PreparedMessage` 로 저장 |
| R2-04 | Rebase 로 Pull 하면 작업 중 변경이 있을 때 "cannot pull with rebase" 로 실패 | `pull --rebase --autostash`(GitKraken 과 같음) |
| R2-05 | Rebase 충돌 카드에 "0 file(s)", 파일 목록 없음 | git 출력에 "Merge conflict in" 이 없으면 `git diff --name-only --diff-filter=U` 로 다시 물음 |
| R2-06 | Stash 뒤에도 새 파일(untracked)이 작업 폴더에 그대로 남음 | `GIT_LITERAL_PATHSPECS=1` 이면 `stash -u` 가 안의 매직 경로(`:/`)를 글자로 읽어 지우지 않음 → stash push 만 끄고 경로는 `:(literal)` 로 감쌈(`GitCommand.IsStashPush`·`LiteralStashPaths`) |
| R2-07 | Pop 할 새 파일이 이미 있으면 "already exists" 로 멈추며 추적 파일만 반쯤 적용, 카드의 버리고 Pop 도 실패 | Pop 전에 stash 의 새 파일과 겹치는 파일을 찾아, 내용이 같으면 조용히 치우고 다르면 카드로 물음. 추적 파일로 막혀 다시 할 때도 먼저 치움(`SyncFlows.UntrackedCollisionsAsync`) |
| R2-08 | 직접 만든 PR 필터를 이름 그대로 두고 다시 고치면 이름이 사라짐 | 저장(JsonUtility)을 거치면 null 번역 키가 "" 가 되는데 `!= null` 로 검사 → `GitPullRequestFilters.KeepsDefaultName` |
| R2-09 | PR 줄 끝 ⋮ 가 안 보임(오른쪽 클릭만 됨) | TreeView 줄 내용이 줄어들지 않아(flex-shrink 0) 깊이 들여쓴 긴 제목이 사이드바 밖으로 밀어냄 → 사이드바 트리에서 줄어들게 하고 제목은 말줄임 |

### 남은 관찰(고치지 않음)

- .meta 누락 경고는 새로 추가한 에셋만 본다. 이미 추적 중인데 .meta 가 커밋된 적 없는 파일은 대상이 아니다(설계).
- Pull 버튼 툴팁의 받을 커밋 수는 Fetch 전에는 0 으로 보인다.
- 원격 URL 고치기는 형식을 검사하지 않는다(GitKraken 도 같음).
- 바깥에서 고친 파일은 Refresh 를 눌러야 보인다(P2-06 의 FSEvents 미구현과 같은 건). 이미 떠 있는 Unity 툴팁 글이 남는 것은 P1-05(뒤에 "자체 툴팁" 으로 해결).
- `pull --rebase` 는 병합 커밋을 풀어 로컬 커밋을 다시 적용하므로, 이미 병합으로 해결한 충돌이 다시 나올 수 있다(git 동작).

## 3차 개발 (2026-10-29)

작은 것부터 큰 순서로 진행. Windows 확인은 macOS 개발을 마친 뒤 마지막에 한다.
검증: Unity 재컴파일 오류 0, EditMode 테스트 835개 중 827 통과·8 건너뜀·실패 0(1·2차의 `C:/` 경로 실패도 고침). 새 회귀 테스트 추가.

| 항목 | 내용 |
|---|---|
| Windows 경로 테스트 | `GitCommitTemplate.ResolvePath` 가 macOS 에서 `C:/…` 를 상대 경로로 보던 것 — 드라이브 문자 경로도 절대 경로로 본다 |
| 원격 URL 검사 | URL 고치기 카드·리모트 추가 서랍이 `https://…`·`git@host:owner/repo`·있는 폴더 경로만 받는다(`GitRemoteForm.IsPlausibleUrl`). "/" 같은 글은 막고 까닭을 보인다 |
| Pull 툴팁 | 받을 커밋 수 아래에 "마지막 Fetch: n분 전 / 아직 Fetch 하지 않음"(`GitToolbar.FetchAgeText`) |
| .meta 누락 경고 | 새 에셋뿐 아니라, 이미 추적 중인데 .meta 가 저장소에 없는 에셋을 고쳐 커밋할 때도 경고 |
| PR 필터 추가 아이콘 | 검색 칸 옆 돋보기를 Material "filter_plus" 아이콘(`ActionGlyphKind.FilterAdd`)으로 바꿈 |
| 커밋 후 그래프 반영(P2-03) | 측정: 커밋 클릭→새 HEAD 약 0.8초→그래프 1.0~1.3초. 원인: Unity(Mono)에서 git 프로세스 띄우기 1회 30~60ms, 입력 없는 에디터 틱 간격 약 0.1초(메인 스레드로 돌아올 때마다), 작업 목록 다시 그리기(200ms+)가 끝나야 그래프 git 이 시작. 수정: 새로고침 git 8회→4회(config 한 번·HEAD/upstream 은 for-each-ref 에서), 즉시 새로고침은 다음 틱을 기다리지 않음, 그래프 읽기·커밋 git 사슬을 백그라운드 한 번으로 묶음, 그래프 git 을 목록 그리기보다 먼저 시작·뷰를 사이드바보다 먼저 알림, git 이 도는 동안·직후 0.3초만 Git 창이 스스로 다시 그려 틱을 깨움. 결과 약 0.9초 |
| 바깥 파일 변경 감지(P2-06) | macOS FSEvents(`MacDirectoryWatcher`, CoreServices P/Invoke·전용 디스패치 큐). 도메인 리로드 전에 스트림을 멈추고 진행 중 콜백을 기다린다. 실패하면 예전 폴링으로 돌아간다. 유휴 중 새로고침이 반복되지 않음 확인 |
| PR 상세 화면 | PR 을 누르면 열리는 서랍: 번호·제목·상태 배지(Open/Draft/Merged/Closed), "누가 어디로 커밋 n개를 합치려 함", 파일·줄·댓글 수, 병합 가능 여부(충돌·보호 규칙·뒤처짐·검사 실패·계산 중), 병합 방식(merge/squash/rebase) + Merge(확인 카드, 본 때의 head SHA 를 함께 보내 그 사이 새 커밋이 오면 거절), GitHub 보기·바뀐 것 보기·체크아웃, 설명, 리뷰어·담당자·라벨·브랜치, 바뀐 파일 목록. 병합하면 PR 목록을 다시 읽고 fetch |

### 남은 것

- Windows 에서 전체 확인(1·2차의 다른 환경 항목 포함).
- PR 상세: 리뷰어·담당자·라벨 고치기(4차에서 대화·리뷰·Draft·닫기는 끝남).
- 커밋 후 그래프 반영의 나머지 시간은 사이드바가 같은 순간 띄우는 git 10여 개와의 프로세스 경합이다.
- 이미 떠 있는 Unity 기본 툴팁 글이 남는 것(P1-05)은 Unity 쪽 한계로 적었으나, 뒤에 "자체 툴팁" 으로 해결.

## 4차 개발 — PR 대화·리뷰·상태 바꾸기 (2026-10-29)

검증: Unity 재컴파일 오류 0, EditMode 테스트 837개 중 829 통과·8 건너뜀·실패 0. 시험 저장소 PR #2 로 실제 확인 뒤 되돌림(코멘트 지움, open·draft=false).

| 항목 | 내용 | 확인 |
|---|---|---|
| 대화 보기 | 일반 코멘트(`issues/{n}/comments`)·리뷰(`pulls/{n}/reviews`)·코드 줄 코멘트(`pulls/{n}/comments`)를 시간순으로 합쳐 보인다. 승인은 초록, 변경 요청은 빨강, 줄 코멘트는 "파일:줄 에 코멘트". 아직 안 낸(PENDING) 리뷰와 본문 없는 코멘트 리뷰는 뺀다 | 코멘트가 대화에 나옴 |
| 코멘트 쓰기 | 쓰고 [Comment] → 대화·댓글 수를 다시 읽고 칸을 비운다. 실패하면 글을 남기고 오류 카드 | 확인 |
| 리뷰 쓰기 | [Approve](글 없이 됨)·[Request changes](글 필요). 자기 PR 승인·변경 요청은 GitHub 가 거절 → 오류 카드 | 실제 제출은 권한 확인에서 막혀 하지 않음(코드·테스트로만 확인) |
| Draft ↔ Ready | REST 에 없어 GraphQL(`convertPullRequestToDraft`·`markPullRequestReadyForReview`, PR 의 `node_id`). GraphQL 은 실패해도 200 이라 `errors` 를 읽는다 | Draft → Ready 왕복 확인 |
| 닫기·다시 열기 | `PATCH pulls/{n}` state. 닫기는 확인 카드(위험 버튼). 병합된 PR 에는 버튼 없음 | 닫기(카드) → 다시 열기 확인 |
| 사이드바 반영 | 상태가 바뀌면 PR 목록을 다시 읽는다(Draft 표시·묶음) | — |

## 5차 — 남은 시험·다듬기·스크롤바 (2026-10-29)

검증: Unity 재컴파일 오류 0, EditMode 테스트 844개 중 836 통과·8 건너뜀·실패 0. 화면은 마우스 대신 에디터 명령·창 캡처로 확인(사용자가 다른 앱 사용 중).

| 항목 | 결과 |
|---|---|
| Git 창이 닫힌 상태의 팝업 | 통과 — Git 창을 모두 닫고 띄울 곳 없는 확인 카드를 요청하면 Git 창이 열리고 그 위에 카드, 답(OK)이 돌아옴. 두 카드를 한꺼번에 요청하면 하나씩 차례로 뜨고 각자 답이 돌아옴 |
| 보조 창 위 팝업 | 통과 — Git 창 없이 비교 창만 열려 있으면 비교 창 위에 카드(Git 창을 열지 않음). 비교 창을 닫은 뒤에는 낡은 호스트를 쓰지 않고 Git 창을 열어 띄움 |
| 승인·변경 요청 실제 제출 | 통과 — 다른 계정(hykim-nk, 쓰기 권한 협업자)으로 패키지에 로그인해 NK-Studio 의 PR #2 에 Approve·Request changes 를 냄. GitHub 에 APPROVED·CHANGES_REQUESTED 로 기록되고 대화에 초록·빨강으로 보임. 보호 규칙이 없는 저장소라 변경 요청 뒤에도 병합 가능(clean)으로 보이는 것은 GitHub 와 같음 |
| 사이드바 다시 읽기 미루기 | 저장소가 바뀌면 보이는 뷰(그래프)의 git 이 끝난 뒤(최대 1초) 사이드바·stash 개수·브랜치 목록 미리 읽기를 시작. 같은 조건(Unity 가 뒤에 있음) 비교: 커밋→그래프 평균 1.34초 → 1.21초(약 10%, 흔들림 큼) |
| 영어 단수·복수 | `GitText.Format` 이 영어일 때 "1 commits"·"1 files" 같은 곳을 단수로(정해진 명사만, "#1"·"v1" 은 그대로) |
| 스크롤바 | Unity 기본(화살표·각진 막대) 대신 화살표 없는 6px 둥근 반투명 막대, 마우스 올림·끌기에서 진해짐. 밝음·어두움 테마 토큰(--git-scroll-thumb…) |
| 새로 찾아 고친 것 | 스크립트가 Unity 가 뒤에 있을 때 다시 컴파일되면(외부 IDE 저장 등) Git 연동 시작이 `delayCall` 에 걸려 앞으로 돌아올 때까지 "Starting Git integration." 에 머묾 → 업데이트 틱에서도 한 번 시작 |

## 6차 — UI 다듬기 (2026-10-29)

사용자가 화면을 보며 짚은 것을 고쳤다. 화면 확인은 대부분 실제 창 백그라운드 캡처, 어두운 스킨은 에디터 스킨을 잠시 바꿔 확인한 뒤 되돌렸다.

| 항목 | 내용 | 확인 |
|---|---|---|
| Preferences > Git | 화면 여백, 라벨 칸 220px(체크박스가 라벨에 붙지 않게), "일반" 묶음 제목, GitHub 계정을 카드(둥근 아바타·세로 가운데 이름·오른쪽 로그아웃)로 | 로그인한 화면 확인. 로그인 전·로그인 중(코드 입력) 화면은 로그아웃해야 볼 수 있어 미확인 |
| 패키지 아이콘 | Unity 내장 VersionControl 아이콘은 밝은 스킨에도 밝은 회색만 있어 밝은 툴바·탭에서 안 보였다 → 패키지 PNG 두 벌(밝은 스킨 #555 / 어두운 스킨 d_ #C4C4C4). 툴바용 `GitToolbar`(그림 약 63%)와 창 탭용 `GitWindow`(약 51%)를 따로 둔다(`GitIcons.Toolbar`·`Window`) | 밝은·어두운 스킨, 에디터를 켠 채 스킨 전환 시 툴바·탭 모두 바로 따라 바뀜 |
| 사이드바 아이콘 굵기 | 선 아이콘(1.6 칸)과 Material 윤곽 채움 아이콘(1.25 칸)이 섞여 굵기가 달랐다 → 나무는 윤곽 덧긋기로 1.6, 구름·필터+ 는 선으로 다시 그림(곡선 윤곽을 덧그으면 이음매에 가시) | 확인 |
| 사이드바 줄·검색 칸 | 줄 높이 24 → 28. PR 검색 칸은 바로 아래 자식 줄 화살표 위치부터(TreeView 는 들여쓰기를 화살표 칸 여백으로 넣어, 화살표 칸을 숨긴 만큼 19px 를 직접 띄움), 높이 24·글자 12 | 확인 |
| 검색창 돋보기 | Unity 기본 PNG(키우면 흐릿) 대신 벡터 아이콘(`ActionGlyph.ReplaceSearchIcon`: 사이드바·Workspace·브랜치 탐색기) | 확인 |
| 스크롤바 | 화살표 없는 6px 둥근 반투명 막대(5차) | 밝은·어두운 테마 모두 확인 |
| 서랍 전환 | 다른 서랍이 열린 채 새 서랍을 열면 두 서랍이 0.4초 겹쳐 미끄러지며(앞쪽 서랍은 밑에 깔려) 버벅였다 → 이전 서랍은 바로 닫고 새 서랍은 움직임 없이 바로, 열리는 서랍은 맨 앞. 처음 열기·닫기는 그대로 미끄러짐 | 상태로 확인(전환 직후 새 서랍만 보임). 손으로 빠르게 누르는 체감은 사용자 확인 필요 |
| Create Pull Request 서랍 | 고르기 칸 글자 줄어듦·말줄임, ⌄ 칸 28px, 가운데 화살표를 저장소 칸 높이로, 체크박스 간격 | 확인 |
| 서랍 보조 버튼 | Unity 기본 회색 대신 흰 바탕·얇은 테두리(취소·Browse·View on GitHub…) | Create PR·Create Worktree·PR 상세에서 확인 |
| PR 서랍 열 때 깜빡임 | 칸을 비웠다 다시 채우며 "Select…"·병합 커밋 제목이 잠깐 보였다 → 지난 참조로 같은 프레임에 채움, 처음엔 다 읽을 때까지 숨김, 병합 아닌 제목은 기억해 바로 씀 | 프레임별 값 기록으로 확인(두 번째부터 첫 프레임에 값이 다 있음, 처음엔 제목만 한 프레임 늦음) |

## 7차 — Windows 확인·설정 정리 (2026-10-29)

Windows 11 · Unity 6000.7.0b5 · Git for Windows 2.53.0. 새 Unity 프로젝트(gitTest)에 git URL 로 설치한 뒤 패키지를 Packages/ 에 내려받아(embedded) 고치며 확인.
원격은 로컬 bare 저장소(`origin`, Windows 경로)와 시험 저장소(`github` = `NK-Studio/gitvcs-pr-test-www`, Push 하지 않음) 둘.
검증: 설치 직후 EditMode 844/844 통과(건너뜀 0), 아래 수정 뒤 852/852(새 테스트 8개). Unity 재컴파일 오류 0. 마우스·키보드로 실제 조작.

### 통과

| 영역 | 확인한 것 |
|---|---|
| 설정 | Project Settings 의 git init 화면 안 확인 → 저장소 생성, Git 창 바로 반영 |
| 원격 | 원격 없을 때 Push → 리모트 추가 카드, "/" 거부, `E:\…\gitTest-remote.git` 받음, 빈 원격 표시, GitHub 탭 저장소 목록 |
| 커밋 | 커밋 직후 입력칸 비움, .meta 없는 새 에셋 경고 → ".meta 도 함께 커밋" 이 Unity 임포트를 돌려 새로 생긴 .meta 까지 커밋 |
| 바깥 변경 | 탐색기·터미널에서 만든 파일이 Refresh 없이 약 1초 안에 보임(`WindowsDirectoryWatcher`) |
| 스테이징 | Stage 즉시 이동, 스테이징 뒤 또 고친 파일(MM) 양쪽 표시, 나란히 보기 오른쪽 + 로 수정 짝 함께 스테이징(CRLF 경고 있는 저장소에서도) |
| 충돌 | 진행 배너·Pull/Push 꺼짐·충돌 구역·병합 메시지 채움, 마커 경고(그대로 Stage), 우클릭 ours 로 해결 → 이어가기, 중단 → 시작 전 상태 |
| Stash·Pop | 새 파일까지 stash, Pop 때 같은 내용 새 파일은 조용히 치우고 다른 내용만 카드 → 버리고 Pop 으로 끝까지 복원 |
| 브랜치 | 이름 칸 실시간 검사(공백·`..`), 커밋 안 한 변경 안내 후 전환, 새 브랜치 Push 때 원격 둘이면 고르기 카드 |
| PR | 묶음·원격 줄·호버 카드·⋮ 메뉴, 그래프로 가기, 상세 서랍, Draft ↔ Ready, 닫기(확인 카드) → 다시 열기(GitHub 에서 상태 확인 후 원래대로) |
| 콘솔 | 실행 중 경고·오류 없음(FocusController 경고 없음) |

### 새로 찾아 고친 것

| ID | 증상 | 원인 → 수정 |
|---|---|---|
| W-01 | 개인 설정(언어·테마·git 경로·Diff 등)이 Project Settings 에 있음 | Project Settings 는 저장소에 매인 것(GitHub 계정·자동 Fetch·rebase 멈추기·.meta 묶기·저장소)만, 나머지는 Preferences > Git 으로. 값은 EditorPrefs 에 두어 모든 프로젝트 공통, 처음 열 때 UserSettings 값을 옮김. git 이 백그라운드에서 읽으므로 필드를 캐시로 씀(`GitVcsSettings.OnEnable`) |
| W-02 | Windows 에서 갈라진 Pull 이 묻지 않고 바로 병합 | Git for Windows 설치 프로그램이 시스템 gitconfig 에 `pull.rebase=false` 를 늘 써 둠 → 시스템 범위 값은 무시(`ParseScopedPullRebase`, `--show-scope`) |
| W-03 | Fetch 전 Pull 이 충돌하면 빨간 "Pull 실패"(fetch 줄만 보임) | fetch 줄은 stderr, CONFLICT 는 stdout 인데 `GitResult.Message` 가 stderr 만 씀 → 실패면 둘 다 |
| W-04 | Fork·Unity VCS 배치에서 [충돌 보기] 무반응 | GitKraken 뷰에만 연결 → `GitView.ShowConflicts` 로 세 배치 모두 |
| W-05 | 한국어 중단 확인 카드의 두 버튼이 모두 "취소" | `inprogress.abort` → "중단", "{0} 을(를)" 띄어쓰기 |
| W-06 | Fork 배치 Shift+Stage(전부) 무반응 | Button 의 Clickable 이 수정 키 없는 클릭만 받음 → Shift 활성기 추가(모든 OS) |
| W-07 | 로컬 폴더 원격에 첫 Push 해도 PR 만들기 제안 → 빈 서랍 | 올린 원격이 GitHub 일 때만 제안(`PushedToGitHubAsync`) |
| W-08 | 자동 Fetch·간격, rebase 충돌 시 멈추기, .meta 묶어 보기·짝 옮기기가 Project Settings 에 남음 | 이 값들도 원래 UserSettings(저장소에 안 올라감)라 팀 공유가 아니었다 → Preferences > Git·EditorPrefs 로. Project Settings 에는 이 저장소의 GitHub 계정과 저장소 상태(git init)만 |
| W-09 | Project Settings > Git 이 남을 까닭이 없음(남은 것은 계정·저장소 정보·git init 뿐, 정보는 Git 창에 이미 있음). Diff 도구·LFS 계정 버튼은 설정이 옮겨 간 뒤에도 Project Settings 를 열었다 | Project Settings > Git 을 없앰. git init 은 Git 창의 "git 저장소가 아닙니다" 안내 줄의 버튼(확인 카드), 이 저장소의 GitHub 계정은 Git 창 ⋮ 메뉴, 설정은 모두 Preferences > Git(도구 막대 메뉴도 한 줄로). git 을 못 찾는 등 오류면 안내 줄 버튼이 Preferences 를 연다. Project Settings 를 가리키던 안내 글 5개 고침 |
| W-10 | git init 뒤 Library·Temp 등 수천 개가 변경 목록에 잡힘 | .gitignore 가 없으면 Unity 용(github/gitignore 의 Unity.gitignore 바탕)을 함께 만듦. 있던 .gitignore 는 그대로(`GitInit`). 실제로 저장소를 떼어 놓고 Git 창에서 git init → Library·Temp·csproj·UserSettings 가 목록에 없음을 확인한 뒤 원래 저장소로 되돌림 |

### 추가 기능 — Project 창 우클릭 Git 메뉴

Project 창에서 에셋·폴더를 우클릭하면 **Git** 묶음. Git 창 변경 목록과 같은 작업을 같은 길(같은 확인·같은 실행기)로 한다. 메뉴를 열 때 도는 검사는 읽어 둔 상태만 보고 git 을 부르지 않는다.
메뉴 글은 Unity 메뉴처럼 영어(MenuItem 은 컴파일 때 정해져 언어 설정을 따를 수 없다), 확인 카드는 언어 설정을 따른다.

| 항목 | 켜지는 때 | 하는 일 |
|---|---|---|
| Stage | 고른 것에 Unstaged 변경 | 충돌 표시 확인, 별도 git 저장소 폴더는 뺌, 열린 씬 저장 뒤 Stage |
| Unstage | Staged 변경 | Unstage |
| Discard Changes… | 변경이 있고 충돌 파일이 없음 | Git 창 위 확인 카드(파일 목록) 뒤 마지막 커밋 상태로 |
| Diff | 바뀐 파일 하나 | HEAD ↔ 지금 파일을 Preferences 의 Diff 엔진으로. 내장 뷰어면 이미지는 이미지 diff |
| Show in Git Window | 변경이 있음 | Git 창 변경 탭에서 그 파일을 골라 diff |
| File History · Blame | 커밋된 파일 하나 | 히스토리 창·Blame 창 |
| Add to .gitignore | 고른 것이 모두 새 파일 | 파일(또는 폴더 /…/)과 그 .meta 줄을 더함. 이미 추적 중이면 무시돼도 계속 추적되므로 끔 |

고른 에셋의 .meta, 폴더 아래 전부와 폴더의 .meta 를 한 묶음으로 다룬다(`GitProjectSelection`). 저장소 밖(PackageCache)은 대상이 아니다.
확인(손): 수정 파일 Stage → Unstage, 새 폴더 Stage(폴더 .meta·안의 파일·.meta 3개) → Discard(카드에 3개만, Notes 는 남음), Show in Git Window(로컬 변경 탭에서 diff), 바꾼 PNG 의 Diff(이미지 diff, 바뀐 픽셀 100%), 항목별 켜짐·꺼짐(추적 파일의 .gitignore·폴더의 History·Blame 꺼짐).

### 보조 창 크기·배율

| ID | 증상 | 원인 → 수정 |
|---|---|---|
| W-11 | Project 창에서 연 이미지 diff 가 아주 작게 보임 | diff 창이 Unity 기본(최소 크기 근처)으로 떴다 → 새로 뜰 때 1200×800 을 메인 창의 85% 안으로 줄여 가운데에(`GitWindowPlacement`). 히스토리·blame·비교 창도 같다. 옮기거나 키운 뒤에는 그대로 |
| W-12 | 창마다 글자 크기가 다름(diff 창 140%, Git 창 130%) | 히스토리·blame·비교 창은 Git 창 배율을 쓰는데 diff 창만 따로(`DiffWindowScale`) → diff 창도 Git 창 배율. 따로 두던 설정은 지움 |

### diff 창 디자인 점검(모서리·여백)

규칙을 `GitDiff.uss` 맨 위에 적고 그대로 맞췄다.
- 모서리: 조작 요소(22px)·카드 `--git-radius`(6px), 작은 꼬리표(16~18px) `--git-radius-small`(4px, 두 테마에 새로), 원은 높이의 반, 창 끝까지 닿는 막대·목록은 0.
- padding = 그릇 안쪽 여백: 가로 막대 좌우 8px(모든 막대의 첫 요소가 한 세로선), 버튼 글자 좌우 10px.
- margin = 앞 형제와의 간격만: 같은 묶음 안 6px, 묶음 사이 12px. 줄 맨 앞 요소는 0.
- 높이: 막대 32px, 정보 줄 24px, 조작 요소 22px, 코드 줄 18px. 고른 상태는 한 가지 색.

| 찾은 것 | 고침 |
|---|---|
| 이미지 막대가 Unity 기본 Toolbar(ToolbarToggle·ToolbarButton)라 높이·간격·고른 색이 코드 막대와 다름 | 코드 막대와 같은 부품으로: [나란히·스와이프·겹쳐 보기·차이 강조] 한 묶음, [화면에 맞춤·100%] 한 묶음, "속성 보기"는 테두리 버튼(켜지면 고른 색) |
| "바뀐 픽셀 …" 줄이 창 왼쪽 끝에 붙음(`git-diff__info` 규칙이 없었다) | 막대와 같은 좌우 8px·높이 24px 정보 줄 |
| 제목 줄 첫 글자(2px+6px)와 아래 막대 첫 버튼(8px)이 어긋남 | 제목 줄 padding 8px, 제목 margin 왼쪽 0 |
| 가운데 묶음 간격 8px·10px, 버튼 글자 9px·10px 섞임 | 요소 사이 6px·묶음 사이 12px, 버튼 글자 10px |
| 판 이름 꼬리표·줄 + 버튼 3px, 바이너리 카드 6px 숫자 직접 | `--git-radius-small`·`--git-radius` 토큰 |
| LFS 안내 버튼이 Unity 기본 버튼 | 막대 버튼과 같은 한 벌 |
| 바이너리 카드 간격 10px | 12px(간격 단계 4·6·8·12·16·24 안으로) |

### 사이드바 다듬기(GitKraken 비교)

| 찾은 것 | 고침 |
|---|---|
| PR 필터 추가 아이콘이 작고(16px 그림 안 실제 10px 남짓) "+" 가 깔때기 옆에 떨어져 무게가 왼쪽 위로 쏠림 | GitKraken 처럼 깔때기 다리 끝에 원 안의 + 를 겹친 모양, 그림 전체를 20 칸 가운데에, 24px 버튼 안 20px. 칸과의 간격 6px, 올렸을 때 바탕 모서리 4px |
| 0 인 구역·묶음(스테시·서브모듈·내 리뷰 대기)만 펼침 화살표가 빠져 들쭉날쭉 | 담는 줄은 비어 있어도 화살표(펼친 모양 ⌄)를 그림. TreeView 가 인라인으로 숨기는 것을 줄 그릴 때 덮어씀, 누를 것은 없음 |
| 브랜치·PR 줄에 마우스를 올리면 ⋮ 가 자리를 만들며 나타나 개수가 왼쪽으로 밀렸다 돌아옴 | 메뉴가 있는 줄은 ⋮ 자리를 늘 두고 보이기만 바꿈(고른 줄에도 보임) |
| 오른쪽 끝이 셋(필터 버튼 18px·구역 개수 14px·⋮ 4px) | 모두 줄 padding 14px 선. ⋮ 칸 12px 라 [개수 ⋮] 가 필터 버튼(24px) 폭 안 — 개수는 왼쪽 절반, 점은 오른쪽 절반 아래 |
| 구역 개수가 원 안의 작은 흰 글자라 잘 안 읽힘 | 바탕 없이 13px 강조색(`--git-accent-fg`) 숫자, 굵기 Medium(Bold 는 너무 무거웠다). 묶음 개수도 Medium |
| 필터 추가 아이콘이 흐린 회색, ⋮ 점이 가늘다 | 아이콘 기본색 `--git-text-secondary`(한 단계 밝게), ⋮ 점 반지름 1.55 |
| 필터 아이콘의 + 가 꽉 찬 글자 모양 | 꽉 찬 원에 + 를 뚫은 모양(`DrawPlusBadge`, OddEven 채우기). 원은 GitKraken 처럼 깔때기 오른쪽 비탈에 바짝 붙이고 원과 겹치는 오른쪽 다리는 없앰. 그림 18px |
| 구역 머리 줄(로컬·리모트…)이 선택돼 파랗게 칠해짐 | 머리 줄은 고르는 줄이 아님: 누르면 고른 줄을 그대로 두고 접고 펼침. 키보드 ↑↓ 로 닿으면 그 방향의 다음 줄로 건너뜀(PR 검색 줄도) |
| 구역 머리 줄의 + (원격 추가·PR 만들기 등)를 누르면 서랍과 함께 그 구역도 접히고 펼쳐짐 | + 의 클릭도 줄로 올라와 머리 줄 접고 펴기가 함께 돌았다 → 줄 안의 버튼에서 온 클릭은 접고 펴기에서 뺌 |
| 구역 사이 경계가 없음 | 구역 머리 줄 위에 1px `--git-border` 구분선(맨 위 구역은 빼고) |
| 다크 테마에서 구역 이름이 너무 흐림 | 구역 이름 `--git-text`(브랜치 이름과 같은 색, 굵기로 구분), 아이콘 `--git-text-secondary` |
| 빈 구역을 누른 뒤 다른 줄을 누르면 다른 빈 구역의 화살표가 > 로 뒤집힘 | 빈 줄을 누르면 TreeView 안의 펼침 상태가 접힘이 되고, 다음에 줄을 다시 그릴 때 화살표가 > 로 돌아갔다 → 빈 담는 줄은 펼침 상태로 고정(`KeepsOpen`), 화살표 안쪽까지 클릭을 막음 |
| 지금 체크아웃한 브랜치가 어디인지 잘 안 보임 | 현재 브랜치 줄에 한 단계 밝은 회색 바탕(`--git-current-row`) + 왼쪽 3px 강조 막대. 막대만큼 왼쪽 여백을 줄여 글자 자리는 그대로, 고르면 바탕은 고른 색 |
| PR 검색 칸이 일반 입력칸이라 커서가 칸 위아래에 닿음 | 위 필터와 같은 검색창(`ToolbarSearchField`, 지우기 x·포커스 테두리), 돋보기는 뺌 |
| 검색창 지우기(x)가 오른쪽 끝에 붙음(브랜치 탐색기 1px, 필터·PR 5px) | 모든 검색창에서 x 와 돋보기를 테두리에서 8px 로 |
| 다크 테마의 현재 브랜치 알약(커밋 상세 참조) 초록이 너무 밝음 | `--git-graph-pill-head` #5fb865 → #2f6b3a(옆 파랑 알약과 비슷한 깊이) |

### 한글 입력(IME)

모든 입력란(사이드바 필터·PR 검색, 브랜치 탐색기 검색 두 모양, 워크스페이스 필터, 브랜치 고르기 검색, 세 배치의 커밋 제목·설명, GitKraken WIP 제목, 그래프 새 브랜치 칸, 브랜치·태그 대화 상자, 원격·워크트리·서브모듈·PR 만들기·PR 필터 서랍, PR 코멘트)에 한글 IME 로 직접 쳐서 확인했다. Preferences > Git(IMGUI)는 설정이 저장되므로 치지 않았다.

| ID | 증상 | 원인 → 수정 |
|---|---|---|
| W-13 | PR 검색 칸에 "내" 를 치면 "ㄴ내" | 글자마다 `TreeView.Rebuild` 가 줄 요소를 새로 만들어 쓰는 중인 검색 칸이 다른 요소로 바뀌어 조합이 끊김 → IME 조합 중(`GUIUtility.compositionString`, internal 이라 리플렉션)에는 다시 그리기를 미루고 글자가 확정되면 한 번 그림. 결과는 한 음절 확정마다 바뀐다 |
| W-14 | 메뉴로 연 태그·이름 바꾸기 대화 상자에 바로 치면 첫 글자가 영문("sㅐ리") | Unity 는 실제 OS 입력(마우스·키)을 처리할 때에야 창에 IME 를 붙임 → 클릭 없이 코드로 포커스를 받은 입력란은 첫 키를 놓침. 테마를 입히는 창 루트마다 입력란 FocusIn 에서 포커스 창에 기본 IME 를 붙임(Windows, `ImmAssociateContextEx`, `GitIme`). 메뉴 창이 아직 OS 포커스를 쥔 경우를 위해 150ms 뒤 한 번 더 |

### 작은 고침

| 찾은 것 | 고침 |
|---|---|
| 상단 도구 막대가 한국어에서도 Fetch·Pull·Push·Branch·Stash·Pop | 페치·풀·푸시·브랜치·스태시·팝 |
| Draft PR 병합 안내가 "GitHub 에서 Ready for review 로 바꾸면"(바로 옆에 버튼이 있음) | "Ready for review 를 누르면 병합할 수 있습니다" |
| "{0} 을(를)"·"{0} 이(가)"·"{0} 은(는)" 띄어쓰기 15곳 | 조사를 붙여 씀(W-05 와 같게) |
| Unity 6.7 분석기 경고(UAL0010·UAL0013) 18개 | 정적 필드가 있는 7개 형식(`GitCommand`·`GitIntegration`·`GitPullRequestFilters`·`GitShadow`·`HoverCallout`·`RefSidebar`·`GitProjectSelectionTests`)에 다른 파일처럼 `NoAutoStaticsCleanup`(UNITY_6000_7_OR_NEWER) |
| EditMode 테스트 뒤 열린 Git 창이 임시 저장소(GitVcsRebase… 등)에 머묾 | 테스트 범위(`UseRepositoryForTests`)가 끝날 때 새로고침을 요청하고 `RepositorySwitched` 를 알려 실제 저장소를 다시 읽게 함 |

### PR 상세 — 읽는 중 화면

| 찾은 것 | 고침 |
|---|---|
| PR 을 누르면 "읽는 중…" 글과 함께 빈 배지·빈 칸·꺼진 버튼이 먼저 보였다가 채워짐 | 상세를 받기 전에는 내용 대신 같은 배치의 스켈레톤(배지·제목·요약 두 줄, 병합 안내·방식 셋·병합 버튼, 작업 버튼 줄, 설명, 대화 카드 둘). 대화가 늦게 오면 대화 자리에도 카드 스켈레톤(`GitSkeleton`) |
| — | 스켈레톤은 위에서 아래로 물결치듯 옅어졌다 짙어짐: 그릇의 옅게·짙게를 1.2초마다 바꾸고, 막대마다 그릇 안 세로 위치만큼 전환을 늦춤(최대 0.5초) |
| 금방 읽히면 스켈레톤이 깜박 스치기만 함 | 연출로 적어도 0.9초는 스켈레톤을 보임(내용은 미리 채워 숨겨 둠). 그 사이 다른 PR 을 누르면 지난 것은 버림 |
| 스켈레톤이 걷히며 내용이 툭 나타남 | 내용은 투명·6px 아래에서 시작해 0.35초 동안 제자리로(ease-out-cubic) |

### 서브모듈 편집 서랍(GitKraken 의 Edit Submodule)

사이드바 서브모듈 줄을 두 번 누르거나 우클릭 → 편집… 으로 연다(전에는 두 번 누르면 그 저장소를 바로 열었다 — 이제 서랍의 "이 서브모듈 열기").

| 항목 | 하는 일 |
|---|---|
| 원격 URL·이름/경로 | .gitmodules 의 주소(이름은 경로와 다를 수 있어 `--get-regexp` 로 찾음), 경로는 읽기 전용 |
| 이 서브모듈 고치기 | 주소가 바뀌었을 때만 켜짐. `git submodule set-url` (원격 설정도 함께 맞춤) |
| 상태 줄 | 저장소에 기록된 커밋과 맞음(초록)·다른 커밋(노랑)·받지 않음(노랑)·충돌(빨강) |
| 현재 리비전 | 지금 체크아웃된 커밋의 시각·해시·메시지(서브모듈 안에서 `log -1`). 읽는 동안 스켈레톤 |
| 이 서브모듈 열기 · 지우기 | 열기는 Git 창에서 그 저장소로. 지우기는 확인 카드 뒤 `submodule deinit -f` → `rm -f` → `.git/modules/<이름>` 삭제(커밋해야 기록에 반영) |

사이드바: 저장소 기록과 맞는 서브모듈 줄은 아이콘 왼쪽(펼침 화살표 자리)에 초록 체크.
확인(손): unitytable 을 서브모듈로 추가 → Packages/ 로 옮김(`git mv`, .gitmodules 경로도 바뀜) → 두 번 눌러 서랍(주소·상태 초록·리비전 2026-10-11·해시·여러 줄 메시지), 주소 그대로면 고치기 버튼 꺼짐. 주소 바꾸기·지우기는 실제로 하지 않았다.

### 아이콘 굵기 규칙(하네스)

- 모든 선 아이콘(`ActionGlyph`)은 굵기 하나 `ActionGlyph.StrokeUnits`(20 칸 기준 1.6)만 쓴다. 외부 SVG(Material Symbols 등)를 옮길 때도 윤곽을 채우지 말고 같은 굵기의 선으로 다시 그리거나, 채운 윤곽이면 `FillOutline` 으로 테두리를 이 굵기에 맞춘다.
- `ActionGlyphRulesTests` 가 지킨다: `lineWidth` 를 숫자로 따로 정하는 줄이 없을 것, 모든 아이콘 종류에 그리는 코드가 있을 것.
- 이 규칙으로 고친 것: 체크(Material 윤곽 채움 1.25 칸 → 선 1.6 칸), 검색 돋보기(1.75 칸 → 1.6 칸).

| 찾은 것 | 고침 |
|---|---|
| 서브모듈 초록 체크가 작고 가늘다 | 체크를 기준 굵기 선으로, 크기 16px |
| 서브모듈 편집 서랍의 열기·지우기 버튼에 아이콘이 없음 | 폴더·휴지통(새 `Trash`, Material "delete" 비례를 선으로 옮김) 아이콘 + 글자. 우리 버튼 규칙대로 평소엔 테두리, 마우스를 올리면 강조색·위험색으로 채우고 글자·아이콘 흰색 |

### 버튼 규칙 점검 — 평소엔 테두리, 마우스를 올리면 채움

색이 있는 버튼은 평소엔 그 색 테두리(위험·성공은 글자도 그 색), 마우스를 올리면 그 색으로 채우고 글자·아이콘은 흰색, 누르면 한 단계 진하게. 채움 색 규칙 46곳을 선택자별로 훑어 버튼이 아닌 것(배지·표시선·토글)을 빼고 고쳤다.

| 버튼 | 전 | 후 |
|---|---|---|
| 커밋(세 배치 공통 `pending-changes__commit-button`) | 평소에도 파랑 채움 | 파랑 테두리, 올리면 채움 |
| 대화 상자 주 버튼(만들기·확인… `branch-dialog__create`) | 평소에도 파랑 채움 | 파랑 테두리, 올리면 채움 |
| 대화 상자 위험 버튼(삭제·버리기…) | 평소에도 빨강 채움, 올리면 흐려짐 | 빨강 테두리·글자, 올리면 채움 |
| GitHub 연결 화면 주 버튼 | 평소에도 파랑 채움, 올려도 그대로 | 파랑 테두리, 올리면 채움 |
| PR 상세 닫기·변경 요청(빨강)·승인(초록) | 글자색만, 올리면 회색 | 그 색 테두리·글자, 올리면 그 색으로 채움 |

평평한 채움: 마우스를 올렸을 때 바탕이 반투명(`--git-*-strong`, 알파 0.6)이라 원색 테두리와 색이 달라 보였다(테두리만 밝은 초록·빨강). → 채움 토큰을 테두리와 같은 불투명 색으로(다크 성공 #5fb865·위험 #db5c5c, 라이트 #208a3c·#db3b4b), 누름은 한 단계 진한 불투명 색. 올림·누름 규칙 35곳에서 테두리도 바탕과 같은 색을 따른다 — 테두리와 바탕이 한 색인 평평한 버튼.

평소 상태(GitKraken 과 같게): 바탕을 투명하게 비우지 않고 그 색을 옅게 깐다(`--git-accent-soft`·`--git-success-soft`·`--git-danger-soft`, 다크 알파 0.24~0.28·라이트 0.12) + 원색 테두리 + 밝은 글자(`--git-text`, 빨강·초록 버튼도 글자는 밝게). 색 버튼 17곳(서랍 주 버튼, 커밋, 대화 상자 주·위험, GitHub 연결, Stage/Unstage 버튼·줄 단추, 모두 버리기, diff 스테이지·저장, 사이드바 +, 서브모듈 열기·지우기, PR 닫기·승인·변경 요청).

그대로 둔 것: 서랍 주 버튼(이미 규칙대로), 병합 방식 셋(하나를 고르는 선택 버튼 — 고른 것은 선택 색), diff 줄의 작은 +/− Stage 단추(16px, 줄에 올렸을 때만 보이는 표시라 채운 모양이 그 자체).

### 커밋 그래프 이름표 — 로컬·원격 합치기(GitKraken)

| 찾은 것 | 고침 |
|---|---|
| 로컬 브랜치와 그 원격(github/…, origin/…)이 같은 커밋인데 이름표를 따로 세어 "feature/win-test2 💻 +3" | 같은 이름의 원격은 로컬 이름표에 합쳐 노트북 뒤 구름을 함께(`CommitRef.HasRemote`), "+n" 은 남은 참조만 → "feature/win-test2 💻☁ +1". 펼친 목록도 합친 모양 |
| 원격 구분이 접두사(origin/·upstream/)뿐이라 "github/…" 원격을 로컬로 봤다 | 브랜치 목록의 실제 원격 브랜치 이름으로 가름(`GitGraphNode.RemoteRefLabels`) |

| 원격 이름표가 "github/feature/pr-menu-a ☁" 처럼 원격 이름까지 보임 | 원격은 앞의 원격 이름을 빼고 보임(`CommitRef.DisplayName`, "feature/pr-menu-a ☁") — 구름이 원격임을 알려 준다. 전환 등 동작은 전체 이름 그대로 |

| 이름표에 마우스를 올려 펼친 목록 오른쪽이 텅 빔 | 최소 폭을 "+n" 상자까지 덮게 잡았고, 그마저 창 배율이 곱해진 worldBound 로 재 1.4배 넓었다 → 폭은 가장 긴 이름에 맞추고(줄 이름표보다 좁아지지만 않게, _body 좌표로 잼), 열린 동안 줄의 "+n" 은 숨기고, 숨긴 자리만큼 선이 끊겨 보이지 않게 연결선은 목록 오른쪽 끝에서 바로 잇는다 |

| 원격 이름표가 바탕 없이 가지 색 테두리만, 태그는 옅은 바탕에 진한 테두리 | 모두 테두리와 바탕이 한 색인 평평한 채움(GitKraken): 현재 0.7·로컬 0.35·원격 0.2 알파의 가지 색, 테두리는 투명. 원격 글자도 밝게. 태그는 옅은 금색 채움만 |
| 그래프 이름표에 마우스를 올려도 변화가 없음(펼친 목록에만 있었다) | 마우스를 올리면 그 이름표의 가지 색을 밝기 1.3 배·진하기 +0.2 로(`SetRefHover`, 줄 이름표와 펼친 목록 모두). 흰 막은 색이 회색빛으로 바래 쓰지 않는다. 태그는 금색 채움을 진하게. 지금 있는 브랜치(체크 표시)는 이미 진해 올려도 그대로 |

| 브랜치/태그 칸을 끝까지 좁혀도 "✓ ☁"·"💻 ☁" 두 아이콘이 남음(합친 이름표의 구름이 좁은 칸 규칙을 덮었다) | 가장 좁을 때는 GitKraken 처럼 이름표마다 아이콘 하나(현재 ✓·로컬 💻·원격 ☁·태그). 펼친 목록에서는 구름도 다시 보인다 |

| 칸을 줄이는 동안 이름표 크기가 튀고 아이콘이 몇 px 움직이며 스냅(이름을 한 번에 끄는 단계·맞춤 폭 계산) | GitKraken 녹화와 같게: 이름만 글자 단위로 말줄임되며 깎이고 체크·아이콘·"+n" 은 크기·간격 그대로(`KeepIconsWidth` 가 이름표 최소 폭을 아이콘 폭으로 잰다), 이름이 다 깎이면 칸 경계가 오른쪽부터 "+n" → 구름 → 노트북 순으로 자른다. 이름을 끄는 단계(--no-name)와 맞춤 계산은 없앰 |
| 가장 좁을 때 이름표가 가로로 긴 상자에 아이콘이 한쪽으로 쏠리고, 최소 폭에 닿는 순간 아이콘이 가운데로 옮겨지며 맞춰짐 | 맞추는 단계를 없앰: 이름표 왼쪽 여백(현재 4·나머지 5)을 넓을 때부터 첫 아이콘이 28 상자 가운데에 오는 값으로, 이름 뒤 간격은 아이콘 여백 대신 이름과 함께 0 까지 깎이는 틈(`commit-graph-ref__gap`)으로, 이름표 최소 폭은 28 로. 맨 왼쪽 칸은 오른쪽 투명 테두리 13 이라 자르는 경계가 칸 끝 - 13 으로 함께 줄고, 이름표 둥근 끝이 그 경계를 따라 줄어든다. 최소 폭 55(13 + 28 + 13 + 반올림 1)에서 그대로 머리 아이콘과 같은 칸 가운데. "+n" 은 자리를 남긴 채 감춰 이름표가 다시 늘지 않는다. 폭 120→55 를 재 보면 이름표 왼쪽·첫 아이콘·오른쪽 끝이 모두 끊김 없이 움직이고 최소 폭에서 바뀌는 값이 없다 |
| 칸 순서를 그래프 · 브랜치 · 메시지로 두면 가장 좁을 때 이름표가 칸 오른쪽에서 각지게 잘리고 가운데가 아님(최소 폭 32·왼쪽 여백 8 이 이 방식에 맞지 않았다) | 맨 왼쪽이 아니어도 같은 방식: 칸 양옆 6(오른쪽은 투명 테두리라 자르는 경계가 함께 줄어든다), 최소 폭 41(6 + 28 + 6 + 1), 브랜치/태그 머리는 이름표보다 7 안쪽(13)이라 최소 폭에서 머리 아이콘과 이름표 가운데가 맞는다. 가장 좁은 모드(--refs-centered)도 칸 순서와 상관없이 붙인다 |
| 브랜치 칸이 맨 오른쪽이면 이름표가 칸 끝에서 잘리고 "." 만 남은 이름이 보이며, 머리 아이콘으로 바뀌는 때가 실제 줄 칸과 어긋남 | 맨 오른쪽 칸 폭을 머리 줄이 아니라 줄 안 폭(세로 스크롤바·줄 오른쪽 여백 4 를 뺀 `RowsWidth`)으로 잼, 브랜치 칸이 맨 오른쪽이어도 최소 폭 41. 이름이 12 밑으로 깎이면 글자를 서서히 흐려 2 에서 사라지게 해 잘린 말줄임표(.)가 남지 않는다 |
| 창·패널이 좁아지면 맨 오른쪽 칸이 최소 폭을 지키느라 줄 밖으로 밀려 스크롤바 밑에서 잘림(앞 칸은 저장된 폭 그대로였다) | 줄 폭이 모자라면 앞 칸을 메시지 → 브랜치/태그 → 그래프 순으로 각자 최소 폭까지 줄여 맨 오른쪽 칸 자리를 지킨다(`Squeeze`, 저장된 폭은 그대로라 넓히면 돌아온다). 창 크기가 바뀌어 줄일 양이 달라지면 칸 폭을 다시 넣고, 칸 경계 끌기는 지금 보이는 폭에서 시작한다. 오른쪽 패널·사이드바를 넓히고 그래프 칸을 넓혀 가며 여섯 칸 순서 모두 화면에서 확인 |
| 그래프 칸을 가지 묶음보다 좁히면 오른쪽 가지의 원이 칸 경계·막대에 반쯤 잘리고, 두 번째 열이 안 보이는 순간 한 줄로 접히며 원들이 한꺼번에 옮겨 감 | 열 간격을 폭에 따라 이어서 좁혀(`VerticalGraphLayout.SpacingFor`) 맨 오른쪽 가지까지 늘 칸 안에 두고, 최소 폭(여백 × 2 + 막대)에서 간격 0 → 그대로 한 줄. 선은 원과 함께 찌그러지지 않고 원래 간격 자리에 둔 채, 원이 옮겨 간 만큼 그 선만 흐려진다(`EdgeAlpha`: 선이 닿는 가장 오른쪽 열의 원이 원래 간격의 절반만큼 옮겨 가면 0) — 오른쪽 가지의 선부터 하나씩 빠지고, 첫 열끼리 잇는 선은 둘째 열 원이 다가오는 마지막 구간에서 흐려진다. 폭 110·96·86·66·52 에서 화면 확인 |
| 칸·패널 경계를 끄는 동안 커서가 이름표 위를 지나면 펼치는 참조 목록이 열렸다 닫혀 깜박임(GitKraken 도 같다) | 창 어디서든 버튼을 누르고 있으면(패널 루트에서 누름·뗌을 받음 — 칸 폭이 바뀔 때 오는 들어옴 이벤트에는 버튼 정보가 없다) 목록을 열지 않고, 끄는 중에 들어온 이름표 칸은 그 칸을 벗어나기 전까지 열지 않는다 — 놓는 순간 튀어나오지 않고, 나갔다 다시 들어오면 연다. 브랜치 탐색기에서 경계를 끌어 이름표 위에서 놓고, 그 안에서 움직이고, 나갔다 들어와 확인 |
| GitHub 연결 화면 카드가 작음 | 카드 폭 420 → 640, 글자·버튼 키움, 코드는 글자마다 칸 상자(복사하면 왼쪽부터 반짝임), 카드·안쪽 줄이 차례로 올라오며 나타남, 코드 복사·취소는 카드용 보조 버튼. 한때 오로라 배경·흐르는 가지 선·유리 카드(filter·backdrop-filter blur, 매 프레임 다시 그리기)를 넣었으나 보기에 어수선하고 사양을 많이 먹어 걷어 냄 — 바탕은 단색 |
| 연결 화면에서 로그인하거나 "연결하지 않고 계속" 을 누르면 "다시 그리는 중" 덮개 뒤 본체가 한 번에 바뀜 | 전환 연출(`GitWindow.LeaveConnect`): 카드가 살짝 작아지며 200ms 에 사라지고, 덮개 없이 본체를 만든 뒤 본체가 6px 아래에서 260ms 동안 올라오며 나타난다. 본체를 만든 직후 에디터가 몇백 ms 멈춰 그사이 전환이 다 지나 한 번에 튀어나오던 것은, 틀 간격이 다시 짧아질 때까지 기다렸다 시작해 고침(기록: 0 → 0.34 → … → 1.00 이 틀마다 이어짐). opacity·scale·translate 만 써서 가볍다 |
| 사이드바 브랜치 트리가 처음 읽히는 동안(약 1초) 비어 있다가 툭 나타남 | 처음 읽는 동안 트리 자리에 같은 모양의 스켈레톤(구역 머리 줄: 화살표·아이콘·이름·오른쪽 개수, 하위 줄: 단계마다 21px 들여 쓴 아이콘·이름)을 두고 위에서 아래로 물결(GitSkeleton.Pulse), 다 읽으면 트리가 220ms 동안 서서히 나타남(`RefSidebar.BuildSkeleton`·`ShowTree`). 그 뒤 다시 읽을 때는 트리를 그대로 둔다. 아이콘 위치는 실제 트리와 1px 안팎(구역 32/32.7, 1단계 52/51.2, 2단계 72.7/74). 기록: 스켈레톤 약 1.1초 → 트리 0 → 1.00 이 틀마다 이어짐 |
| 스켈레톤 물결이 위에서 아래로 내려가지 않고 다 같이 깜박이는 느낌(전체 높이에 지연 0.5초만 두고 0.6초 전환이 겹쳤다) | 밝은 띠가 그릇 위쪽 밖에서 들어와 2.3초에 걸쳐 아래쪽 밖으로 내려가며(3초는 오히려 늘어져 살짝 당김)(띠 폭은 그릇 높이의 45%, 코사인 곡선으로 완만하게), 막대마다 띠 가운데와의 거리로 밝기(0.4~1)를 틀마다 정한다(`GitSkeleton.Pulse`). 처음 1.4초·좁은 띠는 너무 빨라 파도 느낌이 나지 않았다. 기록: 줄 0 → 5 → 10 → 15 → 19 가 약 400ms 간격으로 차례로 가장 밝아짐. 막대 색이 너무 옅어 띠가 안 보여 --git-skeleton 을 진하게(다크 0.08 → 0.16, 라이트 0.08 → 0.13). PR 상세·서브모듈 서랍 스켈레톤도 같은 방식 |
| 창을 열면 사이드바만 스켈레톤이고 다른 칸은 먼저 채워져 한쪽만 로딩하는 느낌 | 창을 처음 만들 때 사이드바와 처음 보이는 탭의 칸(로컬 변경 목록·그래프·브랜치 탐색기·커밋 기록·체인지 세트·워크스페이스 목록)을 함께 스켈레톤으로 덮고(`GitSkeleton.Cover`, 파일·커밋 모양), 사이드바와 그 탭이 처음 읽기를 마치면 한꺼번에 걷는다(`GitWindow.CoverWhileLoading`, 10초 넘으면 걷음). 그 뒤 아직 읽지 않은 탭을 열면 그 칸만 덮는다. 기록: 사이드바 스켈레톤과 그래프 덮개가 같은 틀에 걷히고 덮개가 220ms 동안 투명해짐 |
| 워크스페이스처럼 빨리 읽히는 화면에서 스켈레톤이 한 번 번쩍이고 사라져 잔상처럼 보임 | 스켈레톤은 투명하게 두었다가 250ms 뒤에도 아직 읽는 중일 때만 서서히 나타나고(`GitSkeleton.AppearLater`), 한 번 나타났으면 적어도 450ms 는 보인 뒤에 걷는다(`RemainingShowMs`). 칸 덮개·사이드바 스켈레톤 모두. 기록: 워크스페이스 탭을 처음 열면 196ms 에 다 읽혀 덮개가 한 번도 보이지 않음, 창을 다시 만들면 약 270ms 에 함께 나타나 730ms 에 함께 걷힘 |
| 컴파일 뒤 창이 새로 만들어질 때 사이드바가 늦게 뜨는데 스켈레톤 없이 빈 칸만 1초쯤 보임 | git 연동이 준비되기 전에 사이드바가 빈 참조로 한 번 읽기를 마쳐 "다 읽음" 으로 보고 스켈레톤을 걷었다 → 읽은 때의 저장소 상태가 지금과 같을 때만 다 읽은 것으로 봄(`RefSidebar.HasLoaded`). 컴파일 직후 에디터가 몇 초 멈추므로 강제로 걷는 시간 10 → 20초. 기록(리로드 뒤): 스켈레톤 7.7초 → 내용 24줄이 준비된 8.8초에 트리·그래프 덮개가 함께 바뀜, 빈 트리 구간 없음 |
| 칸 덮개가 늦게 나타나는 250ms 동안 아래의 이전 파일 목록이 먼저 보였다가 스켈레톤으로 덮이는 잔상 | 덮개 바탕은 처음부터 불투명하게 깔고 막대 묶음만 늦게 나타난다(`GitSkeleton.Cover`). 빨리 읽히면 막대 없이 바탕만 잠깐 있다가 바로 내용. 기록: 덮개 1.0·막대 0 으로 시작해 막대만 약 450ms 에 나타남 |
| 로그인 없이 "계속" 을 누르면 저장소가 아닌 프로젝트는 본체의 "저장소가 아님" 안내로 가고, 나중에 로그인하고 싶어도 연결 화면으로 돌아갈 길이 없음 | 연결 화면을 마칠 때(로그인 없이 계속·로그인 성공) 프로젝트가 아직 저장소가 아니면 같은 카드가 "Git 저장소 만들기" 단계로 바뀐다(만들 폴더·.gitignore 안내·만들기 버튼·"나중에 만들기", `GitHubConnectPanel.ShowInitStep`, 기존 `GitInit.CreateRepositoryAsync`). 만들면 전환 연출과 함께 본체로. 이미 저장소면 바로 본체. ⋮ 메뉴에 "GitHub 로그인…"(로그인하지 않았을 때만)을 두어 연결 화면으로 돌아간다. 확인: 저장소 만들기 단계 모양, "나중에 만들기" → 본체, ⋮ "GitHub 로그인…" → 연결 화면 → "계속" → 저장소가 있어 바로 본체. 실제 git init 은 이 프로젝트가 이미 저장소라 돌려 보지 못함 |

커밋 상세의 참조 목록은 합치지 않고 모두 보인다(`CommitRefs.Of(node, mergeRemotes: false)`). 테스트: 합치기·다른 브랜치의 원격은 따로.

### 자체 툴팁 — Unity 기본 툴팁을 우리 말풍선으로(2026-10-30)

**문제**

- 툴팁이 두 가지로 섞여 있었다: Unity 기본 툴팁(요소의 `tooltip` 속성, C# 68곳·UXML 19곳)과 우리 말풍선(`HoverCallout`, 사이드바 + 버튼·⋮ 버튼·줄 전체 이름).
- 한 요소에 둘이 걸리면 두 번 떴다.
- 기본 툴팁은 Git 창의 테마·UI 배율을 따르지 않는다.
- 이미 떠 있는 기본 툴팁은 Unity 가 글을 다시 읽지 않아, Push 뒤 낡은 글이 잠깐 남았다(P1-05).

**처음 판단이 틀렸던 점**

- 2·3차에서 P1-05 를 "Unity 쪽 한계"로 적고 종료했다. 기본 툴팁을 계속 쓴다는 전제 안에서만 고칠 길을 찾은 판단이었고, 툴팁 자체를 우리 것으로 바꾸는 길은 검토하지 않았다.

**Unity 가 툴팁을 띄우는 방식(확인함)**

- 마우스가 머물면 `DefaultWindowBackend.GetTooltip` 이 마우스 아래 요소를 찾아(`Pick`) 글이 빈 `TooltipEvent` 를 보낸다.
- 요소는 `tooltip` 을 넣을 때 등록된 처리기(`VisualElement.SetTooltip`)로 이벤트에 글을 채운다.
- 이벤트를 다 보낸 뒤 글이 비어 있으면 기본 툴팁을 띄우지 않는다.
- 6000.0.84f1 과 6000.7.0b5 의 두 DLL(`UnityEditor.UIElementsModule`·`UnityEngine.UIElementsModule`)을 Mono.Cecil 로 열어 비교했다. 6.0 쪽에 디버그용 빈 명령이 더 있을 뿐 하는 일은 같다. 패키지 최소 버전(6000.0)에서도 같은 방식이 통한다(6.0 에디터로 띄워 보지는 않음).
- 실제 확인(6.7): 창 루트에서 이벤트를 막으면 저장소 이름 툴팁("E:/0.Project/1.unity/gitTest")이 뜨지 않고, 막지 않으면 뜬다.

**고친 것** — `HoverCallout.ReplaceTooltips(root, host)`

- 창 루트에서 `TooltipEvent` 를 먼저 받아(TrickleDown) 전달을 멈춘다 → 기본 툴팁이 뜨지 않는다.
- 이벤트 대상(없으면 그 부모)의 `tooltip` 글을 우리 말풍선으로 띄운다. 루트에서 받는 시점에는 이벤트의 글이 아직 비어 있으므로 요소의 속성에서 직접 읽는다.
- 위치는 대상 아래 가운데. 창 아래로 넘치면 대상 위로 뒤집고(꼭지도 아래로), 좌우로 넘치면 안쪽으로 민다.
- 뜨는 시점은 Unity 의 기본 대기 시간(약 0.5초)을 그대로 따른다.
- 대상에서 벗어나거나, 누르거나, 휠을 굴리거나, 창 밖으로 나가면 닫힌다.
- 뜰 때마다 글을 새로 읽는다 → 낡은 글이 남지 않는다(P1-05 해결).
- 기존 `tooltip` 속성 87곳은 손대지 않고 글의 출처로 그대로 쓴다.
- 즉시 뜨는 안내 말풍선(`HoverCallout.Attach`)이 달린 요소(와 그 안)는 그 말풍선에 맡기고 기본 툴팁만 막는다 → 두 번 뜨지 않는다.
- 툴팁 글은 서식(rich text)으로 읽지 않는다(경로·명령의 `<`). 긴 설명은 360px 에서 줄 바꿈, 왼쪽 맞춤.
- 띄울 덮개가 없으면(연결 화면 등) 기본 툴팁을 그대로 둔다.
- 붙인 곳: Git 창(`GitWindow`, 덮개 = 창 본체)·보조 창(`GitToolWindowBase`, 덮개 = 내용 루트). 창 밖 IMGUI 영역(Unity 메뉴 등)은 Unity 툴팁 그대로.

**확인한 것**

- 저장소 버튼·새로고침 버튼: 기본 툴팁 없이 우리 말풍선만 뜨고, 커서가 있는 동안 유지된다. 새로고침 버튼은 창 오른쪽 끝이라 말풍선이 안쪽으로 밀린다.
- 시험 중 말풍선이 저절로 닫힌 것처럼 보인 것은 커서가 다른 모니터로 움직여 "벗어남" 이 들어온 것이었다(기록으로 확인).
- 위로 뒤집기: 창 맨 아래 "마지막 커밋 고치기 (Amend)" 체크박스 — 말풍선이 위에 뜨고 꼭지가 아래에서 체크박스를 가리킨다. 긴 설명은 줄 바꿈.
- 보조 창(Blame): 처음엔 말풍선이 줄 아래가 아니라 창 맨 아래에 창 폭 전체로 깔려 보이지 않았다 — 말풍선 스타일(`.hover-callout`)이 `GitWindow.uss` 에만 있고 보조 창은 그 시트를 읽지 않았다. 스타일을 두 창이 함께 읽는 `GitCommon.uss` 로 옮겨 고침. 고친 뒤 Blame 줄에 커서를 올리면 줄 아래에 커밋 정보(해시·작성자·날짜 / 메시지) 말풍선이 뜬다. 보조 창은 포커스를 받아야 Unity 가 툴팁 이벤트를 보낸다(기본 툴팁과 같음).
- 스타일을 옮긴 뒤 Git 창(Amend·새로고침 버튼)도 그대로인 것을 다시 확인.

### 남은 관찰(고치지 않음)

- 없음(Push 뒤 낡은 툴팁 글(P1-05)은 위 "자체 툴팁" 으로 해결).

## Unity 6.0 확인 (2026-10-30)

새 Unity 6000.0.84f1 프로젝트(`E:/0.Project/1.unity/git-test-6-0`, 2D URP 템플릿, 아직 저장소가 아님)에 패키지를 로컬 경로(`file:`)로 넣어 확인했다.

**찾아 고친 것**

| 찾은 것 | 고침 |
|---|---|
| 컴파일 실패: `ITextSelection.GetCursorPositionFromStringIndex` 가 6.0 에 없다(diff 코드 글 끌어 고르기에서 마우스 위치 → 글자 순번) | 실행 중에 그 메서드가 있으면 쓰고(6.x 뒤 버전), 없으면 앞쪽 글자를 같은 스타일로 재서(`TextElement.MeasureTextSize`, 서식 태그는 빼고 `<noparse>`) 한 줄로 셈한다(`GitDiffPanel.CursorPosition`). 버전 번호를 추측하지 않으려고 리플렉션으로 찾는다 |
| USS 경고: `column-gap` 을 6.0 이 모른다(연결 화면 코드 칸 사이) | 칸마다 바깥 여백으로 |
| 사이드바가 스켈레톤에서 멈춤(창을 다시 만든 뒤) | 트리로 바꾸는 예약을 숨겨 둔(display: none) 트리에 걸었는데 6.0 은 숨긴 요소의 예약을 실행하지 않는다 → 보이는 사이드바 루트에 건다 |
| (6.7) 새 정적 필드의 UAL0010·UAL0013 경고(`GitSkeleton.ShownAt`, `GitDiffPanel.s_cursorPosition`) | 다른 파일과 같이 `NoAutoStaticsCleanup`(6.7 이상에서만) |

**확인한 것(6.0)**

- 컴파일: 패키지·테스트 어셈블리 모두 오류 없음, 콘솔 오류·경고 없음.
- 테스트: 872개 모두 통과(기본 803 + git 시나리오 69, 약 132초). 처음 한 번은 실행 두 개가 겹쳐 49개가 실패했는데, 시험용 임시 저장소를 둘이 동시에 쓴 탓이었다(다시 한 번만 돌려 모두 통과).
- 연결 화면 → "이번에는 연결하지 않고 계속" → 저장소가 아니라 "Git 저장소 만들기" 단계 → 만들기: 실제 `git init` 과 Unity .gitignore 가 생기고(Library·Temp·Logs 빠짐) 전환 연출과 함께 본체로, 브랜치 `master`.
- 자체 툴팁(새로고침 버튼), 브랜치 탐색기(이름표·아바타), diff 열기와 글 끌어 고르기(한글 줄 포함).
- 6.7 은 배치 모드로 다시 컴파일해 오류·UAL 경고가 없는 것을 확인했다.

**미확인이던 것 확인(6.0, 화면 조작)**

- 보조 창(Blame) 툴팁: 커밋이 있는 줄에 커서를 올리면 줄 아래에 커밋 정보 말풍선(해시·작성자·날짜 / 메시지).
- Preferences > Git 로그인 전 화면(설명 + "GitHub 로 로그인")과 로그인 중 화면(안내·코드·"코드 복사"·"다음: GitHub 페이지 열기"·"취소"). 취소하면 로그인 전으로 돌아간다. 실제 로그인 성공은 브라우저에서 사용자 계정으로 승인해야 해 하지 않았다.
- Preferences > Git(IMGUI) 입력란: 글을 넣는 칸은 git 실행 파일 경로·difftool 이름·외부 diff 도구 경로·인자, 숫자 칸은 자동 페치 간격·diff 앞뒤 줄 수. 경로 칸에 한글("git/한글폴더")이 깨지지 않고 들어간다(원래 값 `git` 으로 되돌림). 한/영 키는 화면 조작 도구로 보낼 수 없어 IME 조합 과정 자체는 보지 못했다(Unity 기본 IMGUI 입력란이라 조합은 Unity 가 처리).
- Hunk 버리기: Hunk 보기의 "변경 묶음 버리기" → Git 창 위 확인 팝업 → 버리기 → 파일이 커밋된 내용으로 돌아간다.
  - 고침: `core.autocrlf=true` 저장소에서 변경을 모두 버리면 diff 없는 "수정됨" 줄이 변경 목록에 남았다.
    - 원인: git 이 버린 파일을 CRLF 로 다시 쓰는데, 인덱스에는 LF 일 때의 크기가 기록되어 있었다. git 은 기록된 크기가 실제와 다르면 내용을 비교하지 않고 "수정됨" 으로 본다(`git update-index --refresh` 로도 안 걷힌다). 그래서 `git diff` 는 비어 있는데 `git status` 는 ` M` 이었다.
    - 처리: `GitOperations.DiscardPatchFromWorkTreeAsync` 가 패치를 되돌린 뒤 그 파일에 `git diff --quiet -- 경로` 를 돌린다. 차이가 없으면 `git add` 로 인덱스의 파일 기록을 새로 맞춘다. 내용이 같을 때만 하므로 스테이징되는 것은 없다. 호출부(GitDiffPanel)가 파일 경로를 넘긴다.
    - `git diff` 는 `--pathspec-from-file` 을 받지 않아(종료 코드 129) 경로를 `--` 뒤에 직접 넘긴다. `GIT_LITERAL_PATHSPECS=1` 이라 `:(literal)` 접두사를 붙이면 안 된다. 접두사가 이름의 일부로 읽혀 아무 파일과도 맞지 않고 0 이 나와, 일부만 버린 파일까지 스테이징됐다(확인 중 잡음).
    - 6.0 확인(인덱스에 LF 크기를 기록한 뒤 CRLF 로 바꾸고 한 줄 수정): 수정 전에는 버린 뒤 ` M` 이 남았고 diff 는 비어 있었다. 수정 뒤에는 상태가 깨끗하고 스테이징된 것도 없었다. 두 줄을 바꾸고 한 Hunk 만 버리면 남은 변경은 ` M` 그대로 스테이징되지 않는다.
- 워크트리 만들기: + → 서랍(참조·새 브랜치·작업 폴더). `master` 를 고르면 작업 폴더가 자동으로 채워지고, 이미 체크아웃된 브랜치라 새 브랜치 이름을 적으라는 검사 문구가 뜬다. `wt-test` 를 넣으면 폴더가 따라 바뀌고 버튼이 켜진다 → 만들기 → `git worktree list` 에 `git-test-6-0-wt-test [wt-test]`, 사이드바에도 반영.
- 언어 전환: 한국어 ↔ English 전환이 곧바로 반영된다(창을 다시 만들고 스켈레톤 규칙을 따른다).

**확인하지 못한 것**

- GitHub 로그인 성공 경로(사용자 계정 승인이 필요).
- 서랍을 손으로 빠르게 바꿀 때의 체감: 시험 중 Unity 창이 화면에서 사라져(다른 앱이 앞에 옴) 멈춤.
- PR 대화·리뷰 쓰기: GitHub 에 글이 남아 하지 않음.

## 브랜치 탐색기 빈 목록 (2026-10-31)

- 증상: 6.7 에서 스크립트를 고쳐 컴파일하면(도메인 리로드) 브랜치 탐색기 목록이 가끔 "List is empty" 로 비어 있고, 새로고침해야 채워졌다.
- 원인: 리로드 직후에는 저장소 정보(HEAD)를 아직 몰라, 탭이 HEAD 없이 첫 읽기를 시작한다(커밋을 읽지 않아 빈 그래프). 이 읽기 도중에 저장소 정보가 오면 `OnRepositoryChanged` → `ReloadIfNeeded` 가 "읽는 중" 이라 건너뛴다. 읽기가 끝난 뒤에는 키가 바뀌었는지 다시 보지 않아 빈 목록에 머물렀다. 저장소 정보가 읽기 전·후에 오면 생기지 않아 가끔만 보였다.
- 고침: 읽기가 끝날 때 `ReloadIfNeeded()` 를 한 번 더 부른다. 그사이 키가 바뀌었으면 다시 읽는다. GitKraken 그래프(`GraphWorkspaceView`)는 이미 이렇게 하고 있었고, 빠져 있던 `CommitHistoryView`(커밋 목록 모양)·`BranchExplorerView`(그래프 모양)·`ChangesetsView` 에 같은 줄을 넣었다.
- 그대로 둔 것: `HasLoaded` 에 키 비교를 넣으면 낡은 탭으로 바꿀 때마다 스켈레톤이 덮여 동작이 바뀌어 넣지 않았다. 그래서 리로드 직후 빈 목록이 다시 읽는 동안(수백 ms) 잠깐 보일 수 있다.
- 6.7 확인:
  - 재현: 컴파일 직후 탭 상태가 HEAD 없는 키·읽는 중이었다(이번엔 저장소 정보가 늦게 와 저절로 채워짐 — 경쟁이라 매번 나지는 않는다).
  - 고친 뒤: 읽기를 시작한 직후 키(기간)를 바꿔 "읽는 도중 바뀐" 상황을 만들었다. 읽기가 끝나자 새 키로 스스로 다시 읽었다(`_loadedKey` 끝이 바뀐 기간). 고치기 전 코드라면 처음 키에 멈춘다.

## 시작 화면 순서 — git init 먼저 (2026-10-31)

- 바꾼 까닭: 저장소가 아닌 프로젝트에서 Git 창을 열면 GitHub 로그인 화면이 먼저 떴다. 저장소가 없으면 어떤 기능도 못 쓰지만 로그인은 없어도 커밋·브랜치·diff·히스토리를 다 쓴다. 로그인은 PC 마다 한 번, 저장소는 프로젝트마다 한 번이라 두 번째 프로젝트부터는 로그인 화면이 필요 없다.
- 새 순서:
  - 저장소가 아니면 "Git 저장소 만들기" 카드를 먼저 띄운다(`GitWindow.NeedsInitCard`).
  - 만든 뒤 로그인하지 않았으면 같은 카드가 "저장소를 만들었습니다" 로 바뀌고 GitHub 연결을 선택으로 권한다(`GitHubConnectPanel.ShowLoginStep`, 건너뛰기 버튼은 "나중에 연결"). 로그인했으면 바로 본체로.
  - "나중에 만들기" 는 지금은 Git 창만 보겠다는 뜻이라 이번 세션의 로그인 화면도 건너뛴다. 본체의 "저장소가 아님" 안내가 저장소 만들기를 맡는다.
  - 저장소가 이미 있으면 예전처럼 로그인하지 않았을 때만 연결 화면을 띄운다.
  - 카드를 지나온 것은 세션 키(`GitVersionControl.SkipInitCard`)로 기억한다. 만든 직후 저장소 상태가 아직 갱신되지 않아도 카드가 다시 뜨지 않게 한다.
- 6.7 확인: git 은 건드리지 않고 `GitRepository.IsRepository` 만 잠깐 false 로 바꿔 창을 다시 만들었다.
  - 첫 카드는 "Git 저장소 만들기"(만들기 줄만 보이고 로그인·건너뛰기는 숨김).
  - 만든 것으로 처리하면 "저장소를 만들었습니다" + 로그인 버튼.
  - "나중에 만들기" 는 두 세션 키가 켜지고 본체로 간다.
  - 확인 중 로그인 단계로 바뀐 뒤에도 만들기 줄이 남고 "나중에 연결" 이 숨어 있던 것을 찾아 고쳤다(`Render` 가 로그인 단계에서 둘을 되돌림).
- 마지막 표시 고침은 에디터가 꺼져 화면에서 다시 보지 못했다. 6.0·6.7 컴파일러로 직접 컴파일해 둘 다 오류·경고 없음. 테스트는 돌리지 못했다.

## 원격이 없을 때 새 GitHub 저장소 만들기 (2026-10-31)

- 문제: 원격 없이 Push 하면 "원격을 추가하세요" → 원격 추가 서랍으로 안내했는데, 서랍은 주소 입력과 **이미 있는** GitHub 저장소 고르기만 됐다. 아직 GitHub 에 없는 프로젝트는 github.com 에서 빈 저장소를 먼저 만들고 돌아와야 했다.
- 고침: 원격 추가 서랍에 "새 저장소" 탭을 더했다(GitHub Desktop 의 Publish repository).
  - 칸: 소유자(내 계정이 맨 앞, 그 뒤 `GET /user/orgs` 의 조직), 저장소 이름(프로젝트 폴더 이름에서 쓸 수 없는 글자를 '-' 로 바꿔 채움), 비공개(기본 켬), 원격 이름(원격이 없으면 origin), "만든 뒤 '브랜치' 올리기"(기본 켬, 커밋 없는 브랜치·detached 면 감춤).
  - 이름 검사(`GitRemoteForm.CheckRepositoryName`): 영문·숫자·`. - _`, 100자, `.`·`..` 금지, 이미 아는 같은 소유자의 같은 이름. GitHub 는 다른 글자를 '-' 로 바꿔 만들어 버려 원격 주소가 적은 이름과 달라지므로 바꾸지 않고 막는다.
  - 만들기: `POST /user/repos`(내 계정) 또는 `POST /orgs/{owner}/repos`(조직)로 빈 저장소 → `git remote add` + fetch → 고르면 `git push -u`. 한 작업으로 묶어 상태 줄·오류 창을 함께 쓴다. 만든 뒤 원격 추가가 실패해 다시 누르면 또 만들지 않고 방금 만든 저장소를 쓴다.
  - 원격이 없어 Push 를 못 할 때의 "원격 추가" 는 로그인했으면 이 탭으로 연다(`AddRemoteDrawer.OpenForPublish`). 사이드바 + 는 예전처럼 주소 탭.
  - 로그인 범위는 이미 `repo` 라 새 권한이 필요 없다.
- 확인: 6.0·6.7 컴파일러로 패키지 소스와 테스트를 직접 컴파일해 오류·경고 없음. 이름 검사·이름 제안·요청 본문·응답·조직 목록 읽기 테스트를 더했다(`GitRefsTests`).
- 확인하지 못한 것: 에디터에서 서랍 화면과 실제 저장소 만들기. 6.7 에디터가 꺼져 있고, 6.0 프로젝트는 git URL 패키지(1.0.0)를 써서 이번 코드를 불러오지 않는다. 실제 만들기는 GitHub 계정에 저장소가 생기므로 사용자 확인 뒤에 한다.

## 남은 일과 시험 흔적 (2026-10-29)

- **Windows 전체 확인**: 7차에서 했다. 확인하지 않은 것: 대화·코멘트·리뷰 쓰기(GitHub 에 흔적이 남아 하지 않음), Hunk 버리기, 워크트리, 언어 전환 체감 속도.
- **시험 흔적은 Windows 확인을 위해 일부러 남겨 둔다**(사용자 결정). Windows 에서도 같은 원격·PR·브랜치로 Push·Pull·충돌·PR 화면(대화·리뷰·Draft·닫기)을 다시 해 보려는 것이다. Windows 확인이 끝나면 지운다.
  - www 저장소의 `origin` 원격 → 시험 저장소 `NK-Studio/gitvcs-pr-test-www`(비공개)
  - 그 저장소의 PR #2~4(#2 에는 hykim-nk 의 승인·변경 요청 기록이 남아 있다 — GitHub 리뷰는 지울 수 없다), 브랜치 `feature/pr-menu-a`·`feature/pr-menu-b`·`feature/pr-menu-draft`·`test/round2`
  - hykim-nk 협업자 권한은 시험 뒤 뺐다(다시 시험하려면 다시 초대).
- **하지 않기로 한 것**: PR 상세에서 리뷰어·담당자·라벨 고치기(추가 기능), URP 에셋 자동 업그레이드(P0-05)는 Unity 쪽 한계로 종료. Unity 기본 툴팁 글이 남는 것(P1-05)은 한때 같은 이유로 종료했다가 "자체 툴팁" 으로 해결. 커밋 후 그래프 반영 속도(약 0.9~1.2초)는 요청이 있을 때만 더 다룬다.
- **미확인**: Preferences 의 로그인 전·로그인 중 화면, 서랍을 손으로 빠르게 바꾸는 체감, Preferences > Git(IMGUI) 입력란의 한글 입력.

