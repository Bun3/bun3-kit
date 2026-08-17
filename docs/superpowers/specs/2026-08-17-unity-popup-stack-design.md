# Unity 팝업/모달 스택 프레임워크 설계

날짜: 2026-08-17
상태: 확정 (병렬 워크트리 태스크 — 합리적 기본값으로 진행, 쟁점은 워크트리 코멘트로 공유)

## 목표

도메인 무관 팝업/모달 스택을 `com.bun3.unity.ui` 패키지에 추가한다.
게임 몫(프리팹 로딩, 애니메이션, 콘텐츠)은 델리게이트/가상 메서드로 열어 둔다.

## 배치 결정

- **신규 패키지가 아니라 `com.bun3.unity.ui`에 추가** (`Runtime/Popups/`).
  이 레포의 Unity 패키지는 core/ui/window 세 개의 굵은 단위로 유지돼 왔고,
  ui 패키지 설명이 "DEV kits for Unity UI"다. 팝업은 그 정의에 정확히 들어간다.
- 네임스페이스: `Bun3.Unity.UI.Popups`.
- 비동기: UniTask (core가 이미 의존, CancellationScope 관례와 일치).
  `Bun3.Unity.UI.asmdef`에 `UniTask` 참조 추가.
- 테스트: `Tests/Editor` EditMode (window/gameplay 관례). 전이 완료를
  `UniTaskCompletionSource`로 수동 제어해 플레이어 루프 펌핑 없이 동기 검증.

## 공개 API

### PopupKey (readonly struct)

`int` 래퍼, `IEquatable<PopupKey>`, `int`에서 암시적 변환. 게임은 enum을 캐스팅해 쓴다.
문자열 키 금지(무할당 규율 + 오타 방지).

### PopupBehaviour (abstract MonoBehaviour)

게임 팝업 프리팹의 베이스. 스택이 소유 관계를 주입한다.

- `PopupKey Key`, `int Layer`, `PopupPhase Phase` — 스택이 설정, 게임은 읽기만.
- `protected virtual UniTask PlayOpenAsync(CancellationToken)` — 열림 연출 대기 지점. 기본 즉시 완료.
- `protected virtual UniTask PlayCloseAsync(CancellationToken)` — 닫힘 연출 대기 지점. 기본 즉시 완료.
- `protected virtual bool OnBackRequested()` — back 키가 이 팝업에 라우팅됐을 때.
  `true`(기본) = 닫기 진행, `false` = 닫기 거부(키는 소비됨).
- `public void Close()` — 자신을 소유 스택에서 닫는 편의 메서드.
- `public UniTask WaitUntilClosedAsync(CancellationToken)` — 닫힘까지 대기
  (확인 다이얼로그/보상 연출 체인용).

`PopupPhase`: `None → Opening → Open → Closing → None`.

### 델리게이트 (게임 몫을 여는 지점)

```csharp
public delegate UniTask<PopupBehaviour> PopupFactory(PopupKey key, CancellationToken cancellationToken);
public delegate void PopupReleaser(PopupBehaviour popup);
```

- 팩토리: 프리팹 로딩+인스턴스화 방식(Resources/Addressables/풀)은 전부 게임 몫.
- 릴리저 기본값: `Destroy` (에디터 비플레이 시 `DestroyImmediate`). 풀 반납은 게임이 교체.

### PopupStack (sealed class, MonoBehaviour 아님)

씬 구조를 강제하지 않는다. 게임이 소유·보관한다.

- `PopupStack(PopupFactory factory, PopupReleaser releaser = null)`
- `void Push(PopupKey key, int layer = 0, PopupDuplicatePolicy duplicate = Ignore)`
  / `UniTask PushAsync(...)` — 팩토리 로딩 → 스택 삽입 → `PlayOpenAsync` 대기.
- `void Enqueue(PopupKey key, int layer = 0)` — 순차 표시 대기열(보상 연출 등).
  **스택이 비면** 머리부터 하나씩 표시, 닫히면 다음. (기본값 결정 — 아래 참고)
- `bool HandleBack()` — 최상단 팝업에 라우팅. 소비 여부 반환(스택 비면 false → 게임이 종료 다이얼로그 등 처리).
- `void Close(PopupBehaviour)`, `void Pop()`(최상단), `void Clear()`(연출 생략 즉시 전부 해제).
- `int Count`, `PopupBehaviour Top`, `bool IsOpen(PopupKey)`.
- `event Action<PopupBehaviour> Opened, Closed` — 게임이 z-order/딤/사운드 등 표현 계층 연결.

### PopupDuplicatePolicy

같은 키가 이미 열려 있거나 로딩 중일 때의 `Push` 처리:

- `Ignore` (기본) — 무시.
- `Queue` — 순차 대기열 끝에 추가 (= `Enqueue`와 동일 경로).
- `Replace` — 기존 인스턴스 닫고 새로 연다.

### PopupBackKeyRouter (MonoBehaviour, 선택 컴포넌트)

`Update`에서 ESC/Android back(둘 다 escape 키로 들어옴) 감지 → `Stack.HandleBack()`.
Input System(`ENABLE_INPUT_SYSTEM`)과 레거시(`ENABLE_LEGACY_INPUT_MANAGER`) 둘 다
`#if`로 지원. `Stack` 프로퍼티는 게임이 주입.

## 동작 규칙

- **정렬**: 스택은 (layer 오름차순, 삽입 순서) 정렬 리스트. Top = 리스트 끝.
  높은 layer가 항상 위. 같은 layer 안에서는 나중 push가 위.
- **back 라우팅**: Top이 `Open`이 아니면(전이 중) 키를 소비하되 아무것도 하지 않는다
  (연출 중 입력 무시). Top의 `OnBackRequested()`가 false면 소비만.
- **닫기 중복**: `Closing` 중 재-Close는 무시. `Opening` 중 Close 요청은 열림 완료 후 이어서 닫는다.
- **대기열 드레인**: 팝업이 닫힐 때(그리고 Enqueue 시점에) 스택이 비어 있으면 머리를 Push.
- **수명**: `Clear()`/`Dispose()`는 내부 CTS를 취소해 로딩/연출을 중단하고 전부 해제.
- **무할당**: push/pop/back 경로에 클로저·LINQ·문자열 할당 없음. 대기열은 `Queue<struct>`,
  스택은 `List<PopupBehaviour>` 재사용. 이벤트는 `Action<PopupBehaviour>` 직접 호출.
  `WaitUntilClosedAsync`는 요청 시에만 `UniTaskCompletionSource` 생성.

## 기본값으로 결정하고 넘어간 것 (통합 시 재검토 가능)

1. 대기열은 **스택이 완전히 빌 때** 드레인 — "다른 팝업 위에 겹치지 않게 순차 표시"가
   보상 연출의 통상 요구. 레이어별 드레인이 필요해지면 확장.
2. `Replace`가 로딩 중 인스턴스와 겹치면 열린 인스턴스만 닫는다(동시 로딩 허용).
3. 버전 범프/퍼블리시는 통합 시 사용자 처리 (브리프 지시).

## 1차 리뷰 반영 (2026-08-17, idlez-client/growninja UIPopup 비교 후)

레거시 구조와 비교 리뷰 후 사용자 결정으로 반영:

1. **`PushAsync`가 인스턴스를 반환** (`UniTask<PopupBehaviour>`). null = 중복 정책으로
   무시/큐잉, 팩토리 실패, Clear 취소.
2. **초기 데이터 채널** — 레거시의 근본 문제(Show 시점에 데이터를 넘기고 초기화하려면
   팝업을 유니티 스레드에서 동기 생성해야 함 → Addressables `WaitForCompletion` 강제)를
   해결. 팝업이 `IPopupArg<TArg>`를 구현하면 `PushWithArg(Async)`/`EnqueueWithArg`로
   전달된 데이터가 **비동기 로딩 완료 직후, 스택 삽입·열림 연출 전**에
   `OnPopupArg(arg)`로 도착한다. 직접 push 경로는 제네릭이라 무할당(박싱 없음),
   대기열 경유만 저빈도라 보관 객체 할당 허용.
   - `Push<TArg>(key, arg)` 오버로드로 만들지 않은 이유: `Push(key, x)`의 x가
     기존 layer 인자와 겹쳐 int 데이터가 조용히 layer로 해석되는 사고 방지.
3. **back 기본값은 "닫힘" 유지** — 레거시의 `cancelable=false` 기본이 오히려 불편했다는
   사용자 피드백.
4. **닫기 잠금(Close Guard)** — 레거시 `blockHide`(bool)의 고도화. ref-count 잠금:
   - `popup.BlockClose()` → `using` 스코프(`PopupCloseGuard`), 중첩 가능.
   - `popup.BlockCloseWhile(task)` / `BlockCloseWhile<T>(task)` — 팝업 초기 로딩,
     서버 데이터 패치, 서버 요청-응답 대기를 한 줄로 감싼다. 예외에도 해제 보장.
   - 잠금 중 `Close`/back은 **거부가 아니라 예약**(`CloseRequested`) — 마지막 잠금이
     풀릴 때 자동으로 닫힌다("응답 오면 닫히다"가 공짜). back 키는 소비만.
   - `OnCloseBlockedChanged(bool)` 가상 훅으로 게임이 raycast 차단/스피너 연결.
   - `Clear()`는 잠금 무시(씬 전환 강제 정리 우선). 풀 재사용 대비 Attach 시 카운트 리셋.

리뷰에서 확인된 나머지 갭(미반영, 필요 시 확장): 재사용+전면 이동(GetOrShow/Focus 정책),
sibling index/딤 자동 관리 헬퍼.

## 테스트 전략 (EditMode)

수동 완료 `UniTaskCompletionSource`를 반환하는 테스트 팝업으로 전이를 제어:

- push/pop 순서, layer 정렬, Top 계산
- 중복 정책 3종
- back 라우팅: 소비/거부/전이 중 무시/빈 스택 false
- 대기열: 순차 드레인, 스택 사용 중 대기
- Opening 중 Close 요청 → 열림 완료 후 닫힘
- Clear의 즉시 해제, 릴리저 호출 검증
