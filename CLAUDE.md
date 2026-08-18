# bun3-kit

게임 서버/클라 공용 모듈 모노레포. `Bun3.*` NuGet 패키지(GitHub Packages, `nuget.pkg.github.com/Bun3`)와
얇은 템플릿(bun3-server-template)으로 구성되며, 각 게임 프로젝트는 이 패키지들을 조립해서 만든다.

## 최상위 설계 방침 — 프레임워크가 최대한 지원한다

**이 레포의 제1 원칙: 재사용 가능한 것은 전부 프레임워크(패키지) 단에서 지원하고,
게임 레포에는 그 게임의 도메인 코드만 남긴다.**

- 소유자는 솔로 개발자이고 이 패키지들을 여러 게임에 반복 사용한다. "게임이 직접
  짜게 둘까, 프레임워크가 지원할까?"의 기본 답은 **프레임워크가 지원한다**이다.
  (예: Player 틱/주기 저장의 동시성 처리를 게임 책임으로 미루는 설계는 기각 대상)
- 판단 기준: 두 번째 게임에서도 똑같이 짜게 될 코드라면 프레임워크 몫이다.
  게임마다 달라지는 것(스키마, 밸런스, DB 매핑, 콘텐츠 로직)만 게임 몫이다.
- 템플릿/게임 레포에 공용 로직이 쌓이기 시작하면 즉시 이 레포의 패키지로 내린다.
- 단, 프레임워크는 도메인을 모른다 — 게임 지식이 필요한 지점은 훅/델리게이트로
  연다(로더, 저장, 검증기 델리게이트 시섬 등 기존 패턴 참고).

## 작업 관례

- 레포 최상위 분기는 `common/` `server/` `unity/` 셋뿐이다 — 신규 패키지는 반드시
  이 세 영역 하위(`<영역>/src/<패키지>`, `<영역>/tests/<테스트>`)에 둔다.
  공용(클라+서버) 패키지는 common, 서버 전용은 server, Unity 전용은 unity.

- 설계·구현 흐름: superpowers 브레인스토밍 → 스펙(`docs/superpowers/specs/`) →
  플랜(`docs/superpowers/plans/`) → SDD 실행. 스펙이 결정의 원본이다.
- 패키지 코드: netstandard2.1 + C#9(블록 네임스페이스), 모든 public 멤버 한국어
  XML 문서, 빌드 경고 0, 라이브러리 await에는 `ConfigureAwait(false)`.
- 코드 구성 — 폴더도 partial도 **역할 묶음** 한 원리다:
  - 폴더 = 여러 타입이 이루는 역할 묶음(예: `Popups/Core|Stack|Services|Manager`).
    타입 종류(enum/interface 모음)로 묶지 않는다 — enum·인터페이스·작은 struct는
    주 소비자 타입 옆에 둔다(예: `PopupDuplicatePolicy`는 `Stack/`).
    같은 역할 파일 3개부터 폴더로 승격, 1~2개짜리 폴더는 만들지 않는다.
  - partial = 타입 하나 안의 역할 축. 역할이 여러 개인 클래스는 `타입.역할.cs`로
    나누고(예: `PopupStack.Push.cs`, `Util.Component.cs`), 각 파일 머리에 담당 역할
    주석 한 줄, 본체 XML 문서에 partial 구성을 적는다.
  - 제네릭 변형 파일명은 `타입{TParam}.cs`(예: `Popup{TResult}.cs`).
- 네이밍: `using`으로 감싸는 수명 타입은 `~Scope` 접미
  (예: `CancellationScope`, `ButtonInteractableScope`, `PopupCloseScope`).
- 범용 헬퍼는 패키지 로컬 클래스가 아니라 아래 계층의 Util로 내린다 — Unity 범용은
  `Bun3.Unity.Core.Utils.Util` partial 확장 메서드(`Util.<주제>.cs`, 예: `SafeDestroy`),
  클라+서버 범용은 `Bun3.Common`.
- **런타임 문자열 할당 최소화**: 프레임워크 코어는 핫패스에서 문자열을 만들지 않는다 —
  포맷은 `TryFormat(Span<char>)` 패턴, 식별자는 기동 시 1회 인터닝, 로그는 저빈도 경로만.
  Unity 계층은 ZString + TMP `SetText` 적극 사용(목표: Unity에서 `.text` 문자열 할당 제로).
  틱/패킷 핫패스 무할당 규율(클로저·LINQ·스냅샷 복사 금지)은 전 패키지 공통.
- 퍼블리시: 내용이 바뀌면 반드시 버전을 올린다 — **같은 버전 재퍼블리시 금지**.
- 커밋: gitmoji + `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>` 트레일러.
  서브에이전트 커밋은 `git commit -m "<제목>" -m "<트레일러>"` 이중 플래그로
  (here-string을 bash로 돌리면 메시지가 깨진다).
