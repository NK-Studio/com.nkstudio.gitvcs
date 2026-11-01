# Git Version Control for Unity

<img width="2807" height="1587" alt="bbb copy" src="https://github.com/user-attachments/assets/a918e7d7-f56d-4572-9ea8-c181dd8a8149" />

Unity 에디터 안에서 쓰는 Git 클라이언트입니다. GitKraken·Fork·Unity Version Control 과 같은 모양의 Git 창으로
커밋 그래프를 보고, 파일·줄 단위로 Stage 하고, 커밋·푸시·풀·병합 충돌 해결까지 에디터를 떠나지 않고 합니다.

Unity 기본 Version Control 모드(Visible Meta Files) 그대로, 컴퓨터에 설치된 `git` 으로 동작합니다.

## 요구 사항

- Unity 6000.0(6.0) 이상 — 6.0.84, 6.7.0b5 에서 확인
- [Git](https://git-scm.com/) 이 설치되어 `PATH` 에 있을 것
- (선택) Git LFS, GitHub 계정(Pull Request·저장소 만들기)

## 설치

Unity 의 **Window > Package Manager** 에서 **+ > Install package from git URL…** 을 누르고 다음 주소를 넣습니다.

```
https://github.com/NK-Studio/com.nkstudio.gitvcs.git
```

특정 버전을 쓰려면 주소 끝에 태그를 붙입니다(예: `…/com.nkstudio.gitvcs.git#1.0.0`).

`Packages/manifest.json` 에 직접 적어도 됩니다.

```json
{
  "dependencies": {
    "com.nkstudio.gitvcs": "https://github.com/NK-Studio/com.nkstudio.gitvcs.git"
  }
}
```

## 시작하기

1. **Window > Version Control > Git** 으로 Git 창을 엽니다.
2. 프로젝트가 아직 저장소가 아니면 창에서 바로 Git 저장소를 만듭니다(Unity 용 `.gitignore` 포함).
3. GitHub 에 연결하려면 창의 연결 화면이나 **Preferences > Git** 에서 로그인합니다(Device Flow). 로그인하지 않고도 쓸 수 있습니다.

## 주요 기능

### Git 창
- 왼쪽 사이드바(로컬·원격 브랜치, 태그, 스태시, 워크트리, 서브모듈, Pull Request), 가운데 커밋 그래프, 오른쪽 스테이징·커밋 패널
- 세 가지 배치: **GitKraken** · **Fork** · **Unity Version Control**(체크박스 방식)
- 작업 막대: 실행 취소·다시 실행, 페치·풀·푸시, 브랜치, 스태시·팝, 터미널
- 실시간 변경 감지와 자동 페치, JetBrains 다크·라이트 테마, UI 배율(Ctrl+휠)

### 커밋 그래프·브랜치 탐색기
- 작성자 아바타, 브랜치·태그 이름표, 칸 순서·폭 조절
- GitKraken 처럼 위부터 열을 주는 세로 그래프. 작업 중 변경이 있으면 지금 브랜치가 맨 왼쪽 열
- Unity VCS 식 가로 브랜치 탐색기: 가지 관, 화면 왼쪽에 붙어 따라오는 가지 이름표, 초록 병합 선, 태그 고리
- 커밋 우클릭: 체크아웃, 브랜치·태그 만들기, cherry-pick, revert, reset, rebase, 병합

### 스테이징·커밋
- 파일·폴더·줄·hunk 단위 Stage·Unstage·버리기, 에셋과 `.meta` 를 한 줄로 묶어 보기
- 커밋 템플릿, Amend, 커밋 후 푸시

### 병합 충돌 해결
- GitKraken 과 같은 병합 편집기: 위 A(현재)·B(들어오는 쪽), 아래 출력. 덩어리·줄 단위로 고르고, 출력을 직접 고칠 수도 있습니다
- 이미지 충돌은 A·B 를 나란히 보고 하나를 고릅니다
- 충돌 중에는 "충돌 파일 / 해결한 파일" 목록과 [커밋하고 병합]·[병합 중단]
- 컴파일 오류·패키지 목록 충돌이 남은 채 Unity 를 끄려 하면 한 번 묻습니다(Safe Mode 로 열려 Git 창을 쓸 수 없게 되는 것을 막음)

### diff·파일
- 코드 diff(Split·Inline·Hunk), 파일 히스토리, Blame, "이 파일 편집"
- 이미지 diff(스와이프·어니언 스킨·차이 표시), PSD·PSB 미리보기, Git LFS

### GitHub
- 로그인, 저장소마다 계정 고르기, 원격이 없을 때 새 저장소 만들어 올리기
- Pull Request 목록·검색·만들기·리뷰

## 설정

개인 설정은 **Preferences > Git** 에 있습니다 — 자동 페치, rebase, `.meta` 묶기, 테마, 배치, 메뉴 모양, GitHub 계정.

## 문서

- [매뉴얼](https://nk-studio.github.io/Packages/com.nkstudio.gitvcs@1.0/manual/index.html)
- [바뀐 점(CHANGELOG)](CHANGELOG.md)
- [서드 파티 고지](Third%20Party%20Notices.md)

## 라이선스

[MIT](LICENSE.md) © 2026 NK Studio
