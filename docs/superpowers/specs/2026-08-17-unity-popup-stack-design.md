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
  `Bun3.Unity.UI.asmdef`에 `UniTask` 참조 추가 (+back 키 라우터의 `#if ENABLE_INPUT_SYSTEM`
  컴파일용 `Unity.InputSystem` 참조 — 패키지 미설치 시 미해결 참조는 무시됨).
- 테스트: `Tests/Editor` EditMode (window/gameplay 관례). 전이 완료를
  `UniTaskCompletionSource`로 수동 제어해 플레이어 루프 펌핑 없이 동기 검증.

## 공개 API

### PopupKey (readonly struct)

`int` 래퍼, `IEquatable<PopupKey>`, `int`에서 암시적 변환. 게임은 enum을 캐스팅해 쓴다.
문자열 키 금지(무할당 규율 + 오타 방지).

### Popup (abstract MonoBehaviour)

게임 팝업 프리팹의 베이스. 스택이 소유 관계를 주입한다.

- `PopupKey Key`, `int Layer`, `PopupPhase Phase` — 스택이 설정, 게임은 읽기만.
- `protected virtual UniTask PlayOpenAsync(CancellationToken)` — 열림 연출 대기 지점. 기본 즉시 완료.
- `protected virtual UniTask PlayCloseAsync(CancellationToken)` — 닫힘 연출 대기 지점. 기본 즉시 완료.
- `protected virtual bool OnBackRequested()` — back 키가 이 팝업에 라우팅됐을 때.
  `true`(기본) = 닫기 진행, `false` = 닫기 거부(키는 소비됨).
- `public void Close()` — 자신을 소유 스택에서 닫는 편의 메서드.
- `public UniTask WaitUntilClosedAsync()` — 닫힘까지 대기(확인 다이얼로그/보상 연출 체인용).
  무인자 — 취소가 필요하면 게임 쪽에서 `WhenAny`로 감싼다. (구현 시 시그니처 확정)

`PopupPhase`: `None → Opening → Open → Closing → None`.

### 델리게이트 (게임 몫을 여는 지점)

```csharp
public delegate UniTask<Popup> PopupFactory(PopupKey key, CancellationToken cancellationToken);
public delegate void PopupReleaser(Popup popup);
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
- `void Close(Popup)`, `void Pop()`(최상단), `void Clear()`(연출 생략 즉시 전부 해제).
- `int Count`, `Popup Top`, `bool IsOpen(PopupKey)`.
- `event Action<Popup> Opened, Closed` — 게임이 z-order/딤/사운드 등 표현 계층 연결.

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
  스택은 `List<Popup>` 재사용. 이벤트는 `Action<Popup>` 직접 호출.
  `WaitUntilClosedAsync`는 요청 시에만 `UniTaskCompletionSource` 생성.

## 기본값으로 결정하고 넘어간 것 (통합 시 재검토 가능)

1. 대기열은 **스택이 완전히 빌 때** 드레인 — "다른 팝업 위에 겹치지 않게 순차 표시"가
   보상 연출의 통상 요구. 레이어별 드레인이 필요해지면 확장.
2. `Replace`가 로딩 중 인스턴스와 겹치면 열린 인스턴스만 닫는다(동시 로딩 허용).
3. 버전 범프/퍼블리시는 통합 시 사용자 처리 (브리프 지시).

## 1차 리뷰 반영 (2026-08-17, idlez-client/growninja UIPopup 비교 후)

레거시 구조와 비교 리뷰 후 사용자 결정으로 반영:

1. **`PushAsync`가 인스턴스를 반환** (`UniTask<Popup>`). null = 중복 정책으로
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

## 2차 리뷰 반영 (2026-08-18, 잔여 갭 + idlez ContentsOpenNotice/획득 아이템 큐 확인 후)

사용자 선택으로 전부 반영:

1. **`PopupDuplicatePolicy.Focus`** — 레거시 `GetOrShowPopup` 대응. 기존 인스턴스를
   같은 레이어 최상단으로 이동 + 인자 push면 `IPopupArg` 재주입 + `Focused` 이벤트.
   로딩 중 인스턴스만 있으면 no-op(null 반환).
2. **`PopupStack.Popups`** — 읽기 전용 라이브 뷰(아래→위). 타입 검색/조건부 일괄 닫기
   같은 게임 유틸의 기반.
3. **`PopupQueue`(채널 큐)** — idlez `ZModeManagerLobby.DequeueAcquiredItems`(획득 아이템
   3단 큐: 승급 > 특별 > 일반) 패턴의 도메인 무관 버전. 드레인 게이트가 스택 전체가 아니라
   **"이 큐가 연 팝업이 닫혔는가"** — 다른 팝업(우편함) 위에도 뜬다. `priority` 파라미터
   (내림차순, 동순위 FIFO)로 레거시의 다중 큐를 하나로 대체. `PopupStack.Enqueue`
   (화면 비면 순차)와 용도 구분해 공존.
4. **`PopupPool`** — growninja `Scene_Lobby.popupPool`(프리로드+타입별 Queue 재사용)의
   도메인 무관 버전. `RentAsync`/`Return`이 Factory/Releaser 시그니처와 일치해 스택에
   그대로 꽂힘. 풀 대상은 `PreloadAsync`/`MarkPooled` 옵트인, 미등록 키는 반납 시 파괴.
   재초기화는 매 오픈 `IPopupArg` 재전달로 자연 해결(레거시 OnEnable Refresh 우회 불필요).
5. **`PopupSiblingArranger`** — 열림/닫힘/Focus마다 부모별 sibling index를 스택 순서로
   정렬하고 각 팝업에 `OnStackOrderChanged(index, isTopmost)` 통지(가상 훅 신설).
   "최상단만 딤"은 게임이 훅에서 처리. 팝업 전용 부모 전제.

## 3차 리뷰 반영 (2026-08-18, 사용성 라운드)

1. **네이밍**: `PopupBehaviour` → **`Popup`** (사용자 선택). 파생 시점 가독성
   (`class ShopPopup : Popup`) 우선, 충돌 시 네임스페이스로 구분.
2. **순서 통지를 스택으로 이관** — `OnStackOrderChanged(index, isTopmost)`를 arranger가
   아니라 스택이 구조 변화(열림/닫힘/Focus)마다 직접 호출. arranger는 sibling index
   정렬만 남음(팝업 전용 부모 전제).
3. **딤 기본 동작 내장** — `Popup`에 `[SerializeField] GameObject _backgroundDim`
   (레거시 backgroundDimTransform 대응, null = 딤 없음). 스택이 순서 통지 때
   **딤 보유 팝업 중 최상단**의 딤만 켠다 — 딤 없는 팝업이 맨 위여도 아래 보유자의
   딤 유지(idlez와 동일 규칙, 사용자 확인).
4. **`PopupManager` + `PopupManagerBuilder`** — 풀→스택→back 라우터→정렬 배선과
   Dispose 순서를 한 곳에. DI 컨테이너화는 하지 않음 — 조각들은 생성자 주입 POCO라
   게임 쪽 DI에 직접 등록 가능(사용자 제안의 Builder 채택, DI는 게임 몫으로 결정).
5. **`PopupManager.Instance` 전역 슬롯(선택)** — 레거시 `GameManager.Get().ShowPopup`
   스타일 전역 접근 요구 반영. 게임 부트스트랩이 Build 결과를 대입, Dispose 시 자동
   해제, 도메인 리로드 off 대응 SubsystemRegistration 리셋(ButtonInteractableScope
   관례). Build가 자동 대입하지 않는 이유: 다중 매니저(씬별/테스트) 허용.

## 4차 — 최종 코드리뷰(2축) 반영 (2026-08-18)

Standards/Spec 병렬 리뷰에서 확인된 실버그 수정:

1. 열림 연출 중 `Clear()`되면 `PushAsync`가 파괴된 인스턴스 대신 **null 반환**.
2. 팩토리/`OnPopupArg` 예외 시에도 대기열 드레인 지속(스택 대기열은 finally 드레인,
   `PopupQueue`는 항목별 catch+LogException 후 다음 항목).
3. 닫기 잠금 **세대 토큰** 도입 — Detach마다 세대 증가로, Clear 후 살아남은 이전 세션
   가드의 늦은 Dispose가 새 세션(풀 재사용) 잠금을 훼손하지 못함. Detach가 잠금 중이면
   `OnCloseBlockedChanged(false)` 통지로 스피너/레이캐스트 표현 누수 방지.
4. `PopupQueue.Current` 스테일 수정(닫힌 뒤 다음 로딩 동안 null).
5. Focus 대상이 로딩 중뿐이라 인자가 유실되면 경고 로그로 표면화.
6. Destroy/DestroyImmediate 분기 3중복 → 내부 `EditorSafeDestroy` 헬퍼로 추출.
   public 멤버 XML 문서 누락분 보강. 버전 범프는 기존 결정대로 통합 시 사용자 처리.

## 5차 — 사용성 리뷰 반영 (2026-08-18)

1. **`Popup<TResult>` 결과 채널** — 레거시 `Callback(int result)` 대응. 팝업이 닫기 전
   `SetResult(value)`, 호출자는 `WaitForResultAsync(defaultResult)` 또는
   `stack.PushForResultAsync<TResult>(key)`(인자 버전은 `<TArg, TResult>` 명시)로 수신.
   SetResult 없이 닫히면(back/취소) defaultResult — 취소 처리 코드 불필요.
   풀 재사용 세션마다 리셋(`private protected OnAttached` 훅 신설).
2. **`PopupManager` 파사드 위임** — `Instance.Stack.Push` 이중 홉이 불편하다는 피드백.
   자주 쓰는 동사(Push 계열/PushForResult/Enqueue/Pop/Close/HandleBack/Clear/IsOpen/
   Top/Count)를 매니저에 위임. 전체 API(이벤트, Popups 뷰)는 여전히 Stack.
3. 닫기 차단 이원화 유지 확인(질의 응답): `OnBackRequested` = back 입력 정책(영구 성격),
   `BlockClose` = 일시적 상태 잠금(모든 닫기 경로 예약). 잠금 중엔 정책 훅 미호출(잠금 우선).
4. 다중 인자는 별도 기능 없이 타입 묶음으로: 인자 struct(권장)/튜플/`string[]` 전부
   `TArg` 하나로 전달 — 레거시 `InitializeUsingToken(string[])`의 타입 안전 대체.
5. **`PopupKey<TResult>` 타입 키** — "키가 int면 결과 타입을 어떻게 검사하나"는 질의 반영.
   게임 키 선언부 한 곳에 키↔결과 타입 계약을 고정해 `PushForResultAsync` 호출부를
   전부 추론+컴파일 검사로 전환(호출부 N곳 검사 → 선언부 1곳). 선언부↔프리팹 불일치는
   기존 런타임 LogError가 계속 담당. 원시 키로의 암시적 변환으로 결과 없는 API에도
   같은 키를 그대로 사용.

## 6차 — 키 기조 전환: 타입 = 키 (2026-08-18, 사용자 지시)

레거시(idlez/growninja)의 실제 기조 — `ShowPopup<PopupType>()`이 기본, 같은 클래스로
다른 프리팹을 쓸 때만 문자열 이름 — 를 채택. int `PopupKey`와 5차의
`PopupKey<TResult>` 타입 키를 **대체**한다.

- **`PopupKey` = (Name: string, PopupType: Type 메타)**. 동등성은 Name(ordinal)만 —
  타입 경로와 데이터 경로(서버/테이블 문자열, 암시적 변환)가 같은 키로 중복 판정된다.
  비교 무할당, 타입 키 이름은 제네릭 정적 캐시로 기동 시 1회 인터닝(관례 준수).
  클래스 이름이 곧 식별자이므로 동명 팝업 클래스 금지(레거시와 동일 제약).
- **타입 제네릭 API**: `Push<TPopup>()`/`PushAsync<TPopup>()`(타입된 인스턴스 반환, 캐스팅
  불필요)/`PushWithArg<TPopup,TArg>`/`IsOpen<TPopup>()` + `PopupQueue.Enqueue<TPopup>`,
  `PopupPool.PreloadAsync<TPopup>`, 매니저 파사드 동일 미러. 전부
  `popupName` 선택 인자로 변형 프리팹 지원.
- **결과 검증은 제약으로**: `PushForResultAsync<TPopup, TResult>() where TPopup :
  Popup<TResult>` — 키 선언부조차 없이 팝업↔결과 타입이 컴파일 검증된다.
  문자열 키 오버로드(데이터 경로)는 런타임 LogError 검증 유지.
- 팩토리는 `key.Name`을 그대로 로드 주소로 사용 — 게임 쪽 키→주소 매핑 자체가 사라짐.
- 초기 스펙의 "문자열 키 금지" 결정은 이 기조로 대체됨(타입 경로가 오타를 원천 차단하고,
  문자열은 변형/데이터 경로 한정).

## 7차 — 구조 정리 (2026-08-18)

1. `EditorSafeDestroy` → **core 패키지 `Util.SafeDestroy(this Object)` 확장 메서드**로 이동
   (`com.bun3.unity.core/Runtime/Utils/Util.Object.cs`, 기존 Util partial 관례).
   플레이/에디터 Destroy 분기가 팝업 전용이 아니라 범용이라는 판단.
2. **폴더 구조화**: `Runtime/Popups/` → `Core/`(팝업·키·정책·인터페이스) /
   `Stack/`(PopupStack partial들) / `Services/`(큐·풀·정렬·back 라우터) /
   `Manager/`(매니저·빌더).
3. **partial 분할**: `PopupStack` → 코어(상태·수명·정렬)/Push(열기·중복 정책)/
   Close(닫기·back)/Result(결과)/Queue(순차 대기열). `Popup` → 코어(생명주기·훅)/
   CloseGuard(닫기 잠금). `PopupManager` → 코어(조립·수명·전역 슬롯)/Facade(위임),
   Builder는 별도 파일.

## 8차 — 분류 방법론·네이밍 (2026-08-19, 사용자 리뷰)

1. **분류 방법론 확정(CLAUDE.md 반영)**: 폴더도 partial도 "역할 묶음" 한 원리 —
   폴더는 여러 타입의 역할 묶음, partial은 한 타입 안의 역할 축. 타입 종류별
   폴더(enum 모음/Config)는 채택하지 않음: 계약 enum은 주 소비자 옆
   (`PopupDuplicatePolicy` → `Stack/`으로 이동, `PopupPhase`는 Popup 옆 유지).
   같은 역할 3파일부터 폴더 승격.
2. **`PopupCloseGuard` → `PopupCloseScope`** — 레포 어휘 정합(using 수명 타입 =
   `~Scope`: CancellationScope, ButtonInteractableScope). 내부 멤버·partial 파일명
   (`Popup.CloseScope.cs`)·테스트 동반 개명.
3. **`PopupResult.cs` → `Popup{TResult}.cs`** — 파일명이 "결과 값 컨텍스트"로
   오독되던 것을 클래스명 그대로로. 제네릭 변형 파일명 `타입{TParam}.cs` 관례 확정.

## 9차 — 방법론 재정렬: .NET 기조 채택 (2026-08-19, 1차 사료 리서치 후 사용자 선택)

8차의 자작 "역할 묶음" 폴더 규칙을 폐기하고, 리서치
(`docs/research/2026-08-19-folder-organization-methodologies.md`)로 확인한 확립된
방법론 중 **.NET 기조**를 채택:

1. **폴더=네임스페이스**(IDE0130 기본 on·Unity 공식 "폴더는 네임스페이스대로") —
   `Popups/Core|Stack|Services|Manager` 하위 폴더는 단일 네임스페이스와 불일치라
   **평탄화**(전 파일 `Runtime/Popups/` 직하). 단일 네임스페이스 폴더는 평평하게
   유지(BCL `System/` 실태).
2. **동명 제네릭은 같은 파일**(BCL 실측: CoreLib 1,843경로에 백틱/중괄호 파일 0건,
   `Tuple.cs`·`Action.cs`가 형제 타입 동거) — `Popup{TResult}.cs` 파일 폐기,
   `Popup<TResult>`는 `Popup.cs`에 합류. 8차의 `타입{TParam}.cs` 규칙 철회
   (SA1649 문서 기본값이긴 하나 BCL 실태와 다른 niche 관례).
3. partial `타입.역할.cs`와 타입 폴더 회피(Angular 공식 등 feature 계열 공통 권고)는
   1차 사료로 뒷받침 확인 — 유지.

## 10차 — 갭 완충 (2026-08-19, 레거시 전수 비교 + 라이브러리 4종 조사 후 사용자 전체 채택)

레거시(idlez/growninja) 잔여 갭과 라이브러리(UnityScreenNavigator 등) 교차 검증에서
확인된 항목 전부 반영:

1. **입력 보호(Popup.Interaction)** — 전환 중 raycast 차단(기본 on) + 열림 후 유예
   (`postOpenInteractionDelay`, 레거시 ignoreInteractDuration), 딤 클릭 닫기
   (`closeOnDimClick`, 레거시 HideIfClickedOutside의 폴링 없는 대체 — 닫기 스코프 존중),
   열림 시 EventSystem 선택 해제(기본 on), `OnBecameTopmost`/`OnCovered` 훅
   (USN 가려짐/드러남 생명주기 대응).
2. **내장 연출(Popup.Animation)** — 레거시 animated/faded/animDuration 대응 직렬화
   플래그(스케일 팝 0.7→1, 페이드, 이징 커브). 기본 PlayOpen/CloseAsync가 실행,
   오버라이드하면 대체. DOTween 무의존(unscaled UniTask 루프).
3. **일괄 조작·빈 상태 시그널** — `CloseAll(except)`/`CloseAll(predicate)`(정상 닫기
   경로, 레거시 HideAllPopups 대응 — Clear는 연출 생략이라 별개), `IsEmpty`/`Emptied`/
   `WaitUntilEmptyAsync`(레거시 UISequence 예약 플래그 대체).
4. **MessageBoxPopup** — "제목+본문+버튼N → 인덱스 await" 프리셋 + `ShowMessageBoxAsync`
   /`ConfirmAsync` 확장. 취소(-1)는 defaultResult로 수렴.
5. **ToastQueue<TData>** (`Runtime/Toasts`, 신규 네임스페이스) — 순차 표시·대기 상한·
   중복 억제(comparer 옵트인)·force 끼어들기·뷰 1회 생성 재사용. 팝업 스택과 무관
   (back/딤/정렬 비참여). 레거시 Toast 정적 큐 대응.
6. **LoadingOverlay** (`Runtime/Loading`, 신규 네임스페이스) — ref-count(`LoadingScope`,
   ~Scope 관례) + 지연 표시(플래시 방지, 레거시 0.2s) + `During(task)` 래핑 + 진행률.
   레거시 Popup_Loading 대응.

불채택(조사 근거): DI 통합(델리게이트로 이미 개방), Sheet/씬 히스토리(팝업 밖),
Timeline 전환 에셋(훅으로 충분), UI Toolkit 백엔드(uGUI 기조).

## 11차 — MessageBoxPopup 철회, configure 채널로 교체 (2026-08-19, 사용자 리뷰)

10차 4항의 `MessageBoxPopup`(제목+본문+버튼N 고정 스키마)은 레거시 `Popup_Alert`의
범용성 — 어떤 콘텐츠든 세터 추가로 확장하는 열린 빌더 — 에 못 미친다는 지적 수용.
**닫힌 스키마 대신 열린 빌더**로 교체:

- **configure 델리게이트 채널** — `Push/PushAsync/PushForResultAsync<TPopup[, TResult]>
  (Action<TPopup> configure)`. 게임 팝업의 fluent 세터 체인(레거시 그대로)을
  **비동기 로딩 완료 후·열림 연출 전**에 실행 — 레거시 빌더 DX를 동기 생성 강제 없이 유지.
  `Focus`면 기존 인스턴스에 재적용(레거시 GetOrShow().Set체인), `Queue`면 표시 시점까지
  캐리어로 보관. 저빈도 다이얼로그 경로라 클로저/캐리어 할당 허용.
- 위젯 기본값 리셋은 게임 팝업의 `OnAttached()`(레거시 Initialize() 대응, 풀 재사용 안전).
- `MessageBoxPopup`/`MessageBoxRequest`/확장 삭제 — configure + `Popup<TResult>` 조합이
  상위 호환(레거시 `WaitResultAsync()` 수동 TCS 패턴의 표준화).

## 테스트 전략 (EditMode)

수동 완료 `UniTaskCompletionSource`를 반환하는 테스트 팝업으로 전이를 제어:

- push/pop 순서, layer 정렬, Top 계산
- 중복 정책 3종
- back 라우팅: 소비/거부/전이 중 무시/빈 스택 false
- 대기열: 순차 드레인, 스택 사용 중 대기
- Opening 중 Close 요청 → 열림 완료 후 닫힘
- Clear의 즉시 해제, 릴리저 호출 검증
