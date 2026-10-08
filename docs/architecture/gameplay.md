# Bun3.Gameplay 구조 지도

- 기준: `Bun3.Gameplay` 0.14.0 · `Bun3.Gameplay.Authoring` 0.3.0
- 다이어그램만 위에서부터 순서대로 보면 패키지가 무엇을 하는지, 무엇으로 만들었는지, 무엇을 중요하게 지켰는지 잡히도록 구성했다.
- 결정의 근거와 경위는 스펙이 정본이다 — [프레임워크](../superpowers/specs/2026-08-10-gameplay-framework-design.md) ·
  [속성·효과](../superpowers/specs/2026-08-17-gameplay-slice2-attributes-effects-design.md) ·
  [어빌리티·유닛·월드](../superpowers/specs/2026-08-20-gameplay-slice3-ability-unit-world-design.md)

**색 범례**

```mermaid
%%{init: {"flowchart": {"wrappingWidth": 640}}}%%
flowchart LR
  F["프레임워크"] ~~~ G["게임이 만드는 것"]:::game ~~~ A["산출물 · 결과 코드"]:::art ~~~ K["성공 · 확인된 결과"]:::ok
  classDef game fill:#f6ead9,stroke:#a8691f,color:#2a1c08
  classDef art fill:#eceeef,stroke:#6a7479,color:#1c272b
  classDef ok fill:#dff0e6,stroke:#2f7a4f,color:#123a24
  classDef default fill:#e2edf3,stroke:#255f80,color:#14303f
```

## 1. 무엇을 하는 패키지인가

```mermaid
%%{init: {"flowchart": {"wrappingWidth": 640}}}%%
flowchart TB
  TITLE["Bun3.Gameplay<br/>버프 · 스킬 · 스탯을 데이터로 정의해 돌리는 게임플레이 규칙 엔진<br/>Unreal GAS 계열 · 서버와 Unity 클라이언트가 같은 코드를 쓴다"]:::title
  U["유닛 Unit<br/>캐릭터 · 몹 하나"]
  AT["속성 Attribute<br/>Hp · 공격력 · 쿨다운 감소율<br/>Base(영구값) + 수정자 → Current"]
  TG["태그 GameplayTag<br/>state.stunned · element.fire<br/>계층형 상태 표식"]
  AB["어빌리티 Ability<br/>스킬 = 조건 + 비용 + 쿨다운 + 효과 목록"]
  EF["효과 Effect<br/>데미지 · 버프 · 도트 · 쿨다운<br/>즉발 · 지속 · 무한"]
  EV["게임플레이 이벤트<br/>누가 누구에게 얼마나"]
  SE["시섬 · 게임 코드 확장점<br/>계산식 · 실행 계산 · 대상 선택"]:::game
  TITLE ~~~ U
  U -->|보유| AT
  U -->|보유| TG
  U -->|부여받음| AB
  AB -->|발동하면 적용| EF
  EF -->|수정자로 바꾼다| AT
  EF -->|붙이고 뗀다| TG
  EF -->|부여한다| AB
  TG -->|필요 · 차단 조건| AB
  TG -->|면역 · 일괄 제거| EF
  EF -->|발생| EV
  EV -->|반응 체인으로 다시 적용| EF
  SE -.->|데이터로 안 되는 공식만| EF
  classDef title fill:#255f80,stroke:#255f80,color:#ffffff,font-weight:bold
  classDef game fill:#f6ead9,stroke:#a8691f,color:#2a1c08
  classDef default fill:#e2edf3,stroke:#255f80,color:#14303f
```

## 2. 예: 가시 갑옷 — 코드 0줄로 만든 피해 반사

```mermaid
sequenceDiagram
  participant A as 공격자
  participant P as EffectPipeline
  participant D as 수비자 (thorns 보유)
  Note over A,D: 데이터 정의 3개뿐 — bolt(즉발 피해) · thorns(무한, 반응 체인) · retaliate(즉발 반사)
  A->>P: bolt 적용<br/>Modifiers: Hp Add −40 · RaiseEvents: event.damage.taken
  P->>D: Hp Base 100 → 60
  P->>P: 이벤트 발생<br/>Tag event.damage.taken · Source 공격자 · Target 수비자<br/>Magnitude −40 (클램프 후 실제 변화량)
  P->>P: thorns의 Chains 매칭<br/>Trigger OnEvent · EventTag event.damage.taken · Listen AsTarget
  P->>A: retaliate 적용 — 대상 selector.event.source<br/>Modifiers: Hp Add EventMagnitude × 0.3
  Note over A: Hp Base 100 → 88
  Note over A,D: 같은 틱 안에서 끝난다 · 서로 반사해도 틱당 적용 예산이 고리를 끊는다
```

## 3. 중요하게 지킨 것

```mermaid
%%{init: {"flowchart": {"wrappingWidth": 640}}}%%
flowchart LR
  H1["지킨 원칙"]:::head ~~~ H2["어떻게"]:::head ~~~ H3["확인 · 결과"]:::head
  P1["데이터가 정본<br/>콘텐츠는 코드 없이"] --> M1["효과 · 어빌리티 · 아키타입 = JSON 데이터<br/>로드 시 컴파일 · 검증, 수식은 한 번만 파싱<br/>새 공식이 필요할 때만 시섬 코드"] --> R1["피해 반사 · 쿨다운 20% 감소 반지<br/>코드 0줄 (테스트로 고정)"]:::ok
  P2["결정론<br/>같은 입력이면 같은 결과"] --> M2["시간 = 틱 카운터뿐 · 난수 = 시드 주입 IRng<br/>처리 · 집계 순서 고정"] --> R2["골든 해시 테스트<br/>스냅샷 복원 · 효과 제거 후 비트 동일"]:::ok
  P3["틱 경계 규율<br/>틱 밖에서는 상태가 안 바뀐다"] --> M3["틱 밖 = 명령 큐 적재 · 조회만<br/>결과 = 관찰 버퍼 4종"] --> R3["명령은 쌓인 순서대로 처리<br/>틱 중 외부 변경 경합 없음"]:::ok
  P4["핫패스 무할당"] --> M4["풀 · 버퍼 재사용<br/>클로저 · LINQ · 문자열 생성 금지"] --> R4["부하 벤치 측정 구간<br/>GC 할당 0"]:::ok
  P5["캐시에 맞는 유닛"] --> M5["아키타입별 속성 레이아웃 · 시작 어빌리티 공유<br/>수정자가 붙은 슬롯만 합산 기록<br/>기한이 된 유닛만 시간 진행"] --> R5["몹 2.8 → 1.5KB · 영웅 7.8 → 5.1KB<br/>유닛 · 틱당 100 → 70ns (월드 50개)"]:::ok
  P6["프레임워크는 도메인을 모른다"] --> M6["게임 지식은 시섬 3종 · IRng · 태그로만 들어온다"] --> R6["다음 게임도<br/>콘텐츠와 시섬만 새로 짠다"]:::ok
  classDef head fill:#255f80,stroke:#255f80,color:#ffffff,font-weight:bold
  classDef ok fill:#dff0e6,stroke:#2f7a4f,color:#123a24
  classDef default fill:#e2edf3,stroke:#255f80,color:#14303f
```

수치는 i7-13700K에서 현실 인구 벤치(영웅 + 몹 웨이브)를 번갈아 3회 돌린 최솟값이다.

## 4. 패키지와 기술 스택

```mermaid
%%{init: {"flowchart": {"wrappingWidth": 640}}}%%
flowchart TB
  subgraph CORE["런타임 안의 핵심 기술"]
    direction LR
    K1["BigNum<br/>long 가수 + int 지수<br/>방치형 초대형 수치까지"]
    K2["수식 컴파일<br/>로드 시 한 번 파싱<br/>틱에서는 평가만"]
    K3["태그 = ushort 인덱스<br/>조상까지 세는<br/>카운트 컨테이너"]
    K4["바이너리 카탈로그<br/>지문 · 서명 확인<br/>JSON 없이 기동"]
  end
  subgraph PKG["패키지 — 소스 폴더 com.bun3.gameplay 하나"]
    direction LR
    RT["Bun3.Gameplay 런타임<br/>netstandard2.1 · C# 9<br/>외부 의존 0 · UnityEngine 무의존"]
    CA["Bun3.Gameplay.Authoring<br/>JSON 저작 로더<br/>Newtonsoft.Json 13"]
    UN["Editor · Unity 어댑터<br/>태그 에디터 · GameplayTagRef<br/>Unity 2022.3"]
    TL["태그 빌드 도구<br/>Roslyn 분석기 · MSBuild 태스크<br/>dotnet 도구 bun3-tags"]
  end
  subgraph DIST["배포 — 같은 소스를 두 경로로"]
    direction LR
    NG["NuGet · GitHub Packages<br/>Bun3.Gameplay · Authoring · Tags.Cli"]:::art
    UPM["UPM · git URL ?path=<br/>Runtime · Catalog · Editor · Unity 어셈블리"]:::art
  end
  subgraph USE["쓰는 곳"]
    direction LR
    SV[".NET 서버 · 도구<br/>예: Bun3.Server.GameplayTags"]:::game
    UC["Unity 클라이언트"]:::game
  end
  TE["검증 — NUnit 4 · net10.0<br/>결정론 골든 해시 · 스냅샷 왕복 비트 동일<br/>Explicit 부하 벤치 · 틱 시간 · 할당 측정"]:::ok
  K1 & K2 & K3 & K4 -.-> RT
  RT --> NG
  CA --> NG
  TL --> NG
  RT --> UPM
  CA --> UPM
  UN --> UPM
  NG --> SV
  UPM --> UC
  UC ~~~ TE
  classDef game fill:#f6ead9,stroke:#a8691f,color:#2a1c08
  classDef art fill:#eceeef,stroke:#6a7479,color:#1c272b
  classDef ok fill:#dff0e6,stroke:#2f7a4f,color:#123a24
  classDef default fill:#e2edf3,stroke:#255f80,color:#14303f
```

## 5. 콘텐츠 파이프라인 — 저작부터 실행까지

```mermaid
%%{init: {"flowchart": {"wrappingWidth": 640}}}%%
flowchart TB
  subgraph AUTH["저작 · 빌드 타임"]
    direction LR
    TS["태그 소스<br/>JSON 문서 · C# 네이티브 태그"]:::game
    AN["TagSourceAnalyzer<br/>컴파일 시 선언 검증"]
    TK["TagSource.Tasks<br/>MSBuild로 네이티브 태그 추출"]
    CLI["Tags.Cli<br/>compile · extract-native · inspect"]
    ED["Unity Editor 태그 도구<br/>카탈로그 창 · 피커 · 검증 · 빌드 처리"]
    CJ["콘텐츠 정의<br/>효과 · 어빌리티 · 아키타입 JSON"]:::game
    AJ["Bun3.Gameplay.Authoring<br/>JSON 로더"]
  end
  subgraph ART["산출물"]
    direction LR
    TB[("태그 카탈로그 바이너리")]:::art
    GB[("게임플레이 DB 바이너리<br/>GameplayCatalogBinary")]:::art
  end
  subgraph RT["Bun3.Gameplay 런타임"]
    direction LR
    TC["TagCatalog<br/>불변 · ushort 인덱스"]
    DB["GameplayDatabase<br/>태그 · 속성 · 시섬 · 효과 · 어빌리티 · 아키타입<br/>불변 · 월드 간 공유"]
    W["World<br/>유닛 · 명령 큐 · EffectPipeline"]
  end
  subgraph HOST["실행 환경"]
    direction LR
    U["Unity 클라이언트<br/>Bun3.Gameplay.Unity · GameplayTagRef"]
    S[".NET 서버<br/>Server.GameplayTags가 시작 시 로드"]
  end
  TS --> AN
  TS --> TK
  TK --> CLI
  TS --> ED
  CLI --> TB
  ED --> TB
  CJ --> AJ
  AJ -->|"개발 중: 바로 Build"| DB
  AJ -.->|"출시 전: Write로 굽기"| GB
  TB --> TC
  TC --> DB
  GB -->|"출시: Load · 지문 · 서명 확인"| DB
  DB --> W
  W --> U
  W --> S
  classDef game fill:#f6ead9,stroke:#a8691f,color:#2a1c08
  classDef art fill:#eceeef,stroke:#6a7479,color:#1c272b
  classDef default fill:#e2edf3,stroke:#255f80,color:#14303f
```

## 6. 기동 순서 — 코드로 월드 하나 띄우기

```mermaid
%%{init: {"flowchart": {"wrappingWidth": 640}}}%%
flowchart TD
  T["TagCatalog 로드<br/>TagCatalogBinary.Load"] --> R
  R["속성 등록<br/>AttributeRegistryBuilder.Register(id, 이름, min, max)"]:::game --> S
  S["시섬 등록<br/>IMagnitudeCalc · IExecutionCalc · ITargetSelector"]:::game --> D
  D{"콘텐츠를<br/>어떻게 받나"}
  D -->|"개발 · JSON 또는 코드"| B["GameplayDatabase.Build<br/>효과 → 어빌리티 → 아키타입 순서로 컴파일 · 검증"]
  D -->|"출시 · 바이너리"| L["GameplayCatalogBinary.Load<br/>지문 · 서명 확인"]
  B --> DB[("GameplayDatabase<br/>불변 · 월드 간 공유")]:::art
  L --> DB
  DB --> W["new World(db, IRng, applyBudgetPerUnit = 32)"]
  W --> C["World.CreateUnit(id, archetype)<br/>속성 레이아웃 · 시작 어빌리티를 아키타입과 공유"]
  C --> P["World.Spawn(unit)<br/>다음 Tick 시작에 등록"]:::game
  classDef game fill:#f6ead9,stroke:#a8691f,color:#2a1c08
  classDef art fill:#eceeef,stroke:#6a7479,color:#1c272b
  classDef default fill:#e2edf3,stroke:#255f80,color:#14303f
```

## 7. 게임과의 경계 — 누가 무엇을 만드나

```mermaid
%%{init: {"flowchart": {"wrappingWidth": 640}}}%%
flowchart TB
  subgraph GAME["게임이 만드는 것"]
    direction LR
    C1["콘텐츠<br/>태그 · 속성 · 효과 · 어빌리티 · 아키타입"]:::game
    C2["시섬 구현<br/>크기 계산 · 실행 계산 · 대상 선택"]:::game
    C3["AI · 입력 처리"]:::game
    C4["게임 규칙<br/>사망 판정 · 리스폰 · 보상"]:::game
    C5["루프와 운영<br/>Tick 주기 · 네트워크 · 저장"]:::game
  end
  subgraph FW["Bun3.Gameplay"]
    direction LR
    F1["명령 큐<br/>Enqueue* · Spawn/Despawn"]
    F2["결정론 시뮬레이션<br/>효과 · 속성 · 태그 · 어빌리티"]
    F3["스냅샷 · 복원"]
    O1["관찰 버퍼<br/>발동 결과 · 게임플레이 이벤트<br/>효과 생애 이벤트 · 속성 변경"]
    O2["조회 API<br/>CanActivateAbility · TryGetCooldown<br/>GetActiveEffect · GetCurrent · Tags"]
  end
  subgraph VIEW["게임이 보여 주는 것"]
    direction LR
    V1["UI · 데미지 숫자 · 전투 로그"]:::game
    V2["VFX · 사운드"]:::game
    V3["네트워크 전송 · 리플레이"]:::game
  end
  C1 --> F2
  C2 --> F2
  C3 --> F1
  C4 --> F1
  F1 --> F2
  F2 --> O1
  F2 --> O2
  O2 --> C3
  O1 --> C4
  O1 --> V1
  O1 --> V2
  O1 --> V3
  C5 --> F3
  F3 --> F2
  classDef game fill:#f6ead9,stroke:#a8691f,color:#2a1c08
  classDef default fill:#e2edf3,stroke:#255f80,color:#14303f
```

## 8. 한 틱의 흐름

```mermaid
sequenceDiagram
  participant G as 게임 코드
  participant W as World
  participant P as EffectPipeline
  Note over G,W: 틱 사이 — 큐 적재와 조회만
  G->>W: EnqueueActivation · EnqueueApply<br/>EnqueueRemoveByTag/ById · EnqueueSetBase<br/>EnqueueGrant/Revoke · Spawn/Despawn
  G->>W: Tick()
  W->>W: ⓪ 스폰 · 디스폰 반영<br/>적용 예산 = 32 × 유닛 수
  W->>P: ⓪-1 관찰 버퍼 비움
  W->>W: ⓪-2 명령을 쌓인 순서대로 처리<br/>발동 = 게이트 → 비용 → 쿨다운 → 효과 적재
  W->>P: RunPhases
  P->>P: ① 적용 큐 드레인<br/>면역 · 확률 · 조건 · DR → 즉발 / 병합 / 생성<br/>→ 체인 · 이벤트 반응
  P->>P: ② 기한이 된 유닛만 시간 진행<br/>밀린 틱 따라잡기 → 주기 발화 → 만료
  P->>P: ③④⑤ 유닛마다<br/>재집계 → Ongoing 토글 → 재집계
  W-->>G: 관찰 버퍼 4종<br/>PendingActivations · PendingGameplayEvents<br/>PendingEffectEvents · PendingAttributeChanges
  G->>W: 조회 — CanActivateAbility · TryGetCooldown<br/>GetActiveEffect · GetCurrent
```

## 9. 어빌리티 발동 게이트

```mermaid
%%{init: {"flowchart": {"wrappingWidth": 640}}}%%
flowchart TD
  A["EnqueueActivation(unit, ability, target?)"]:::game
  G1["1 · 유닛이 등록돼 있나<br/>아니면 UnknownUnit"]
  G2["2 · 어빌리티가 카탈로그에 있나<br/>아니면 UnknownAbility"]
  G3["3 · 부여돼 있나<br/>아니면 NotGranted"]
  G4["4 · 쿨다운 태그가 없나<br/>아니면 OnCooldown"]
  G5["5~7 · 시전자 태그와 조건<br/>아니면 MissingTag · BlockedByTag · ConditionFailed"]
  G6["8~11 · 대상 검사 (Target = Required일 때)<br/>아니면 InvalidTarget · TargetMissingTag<br/>TargetBlockedByTag · TargetConditionFailed"]
  G7["12 · 비용을 Base로 낼 수 있나<br/>아니면 CostInsufficient"]
  OK["Success<br/>비용 차감 → 쿨다운 효과 → 효과 적재"]:::ok
  NO["실패<br/>처음 걸린 사유 하나 · 상태는 그대로"]:::art
  PA["PendingActivations에 결과 기록"]:::art
  A --> G1 --> G2 --> G3 --> G4 --> G5 --> G6 --> G7 -->|모두 통과| OK
  G1 & G2 & G3 & G4 & G5 & G6 & G7 -.-> NO
  OK --> PA
  NO --> PA
  classDef game fill:#f6ead9,stroke:#a8691f,color:#2a1c08
  classDef art fill:#eceeef,stroke:#6a7479,color:#1c272b
  classDef ok fill:#dff0e6,stroke:#2f7a4f,color:#123a24
  classDef default fill:#e2edf3,stroke:#255f80,color:#14303f
```

## 10. 효과의 생애

```mermaid
%%{init: {"flowchart": {"wrappingWidth": 640}}}%%
stateDiagram-v2
  [*] --> 적용요청: EnqueueApply<br/>어빌리티 · 체인
  적용요청 --> 탈락: 대상 없음 · 면역<br/>확률 · 조건 · DR
  탈락 --> [*]
  적용요청 --> 즉발: Instant
  즉발 --> [*]: Base 변경<br/>실행 계산 · 이벤트
  적용요청 --> 병합: 같은 효과가 이미 활성
  병합 --> 활성: Refresh · AddStack<br/>ExtendCapped
  적용요청 --> 활성: 새 인스턴스<br/>수정자 · 태그 · 어빌리티 부여
  state 활성 {
    [*] --> 켜짐
    켜짐 --> 꺼짐: Ongoing 조건 불충족
    꺼짐 --> 켜짐: 조건 다시 충족
  }
  활성 --> 활성: 주기 발화 (Base 변경)
  활성 --> 만료: 지속시간 끝
  만료 --> 활성: 스택 하나만 줄고 남음
  만료 --> [*]: Expired<br/>OnCompleteNormal 체인
  활성 --> 조기제거: RemoveByTag<br/>RemoveById · RemoveOnApply
  조기제거 --> [*]: RemovedPrematurely<br/>OnCompletePrematurely 체인
```

## 11. 시간은 필요할 때만 흐른다

```mermaid
%%{init: {"flowchart": {"wrappingWidth": 640}}}%%
flowchart TB
  S["매 틱 ② 시간 진행 차례"] --> Q{"이 유닛의<br/>다음 기한 틱 ≤ 지금?"}
  RS["기한을 지금으로 당기는 일<br/>시간 효과 추가 · 병합 · 스냅샷 복원 · 다른 월드로 이동"]:::art -.-> Q
  Q -->|"아니오 — 대부분의 유닛"| SKIP["건너뛴다<br/>유닛 메모리를 건드리지 않는다"]:::ok
  Q -->|예| CU["밀린 틱만큼 한 번에 따라잡기<br/>주기 발화 · 만료 처리"]
  RD["틱 사이에 누군가 값을 읽을 때<br/>조회 API · 스냅샷 · 상태 해시"]:::game -->|SyncTime| CU
  CU --> NX["다음 기한 틱 계산<br/>가장 가까운 주기 발화 또는 만료"]
  EX["예: 5틱 주기 · 30틱 지속 독<br/>매 틱 방식은 30번 방문<br/>기한 방식은 주기 발화 · 만료 때만 방문"]:::art
  RES["월드 1000개 기준<br/>유닛 · 틱당 232 → 178ns"]:::ok
  SKIP ~~~ EX
  NX ~~~ RES
  classDef game fill:#f6ead9,stroke:#a8691f,color:#2a1c08
  classDef art fill:#eceeef,stroke:#6a7479,color:#1c272b
  classDef ok fill:#dff0e6,stroke:#2f7a4f,color:#123a24
  classDef default fill:#e2edf3,stroke:#255f80,color:#14303f
```

## 12. 유닛 한 개의 구조

```mermaid
classDiagram
  direction TB
  class Unit {
    TargetId Id
    ulong OwnerId
    전투 중 크기 몹 약 1.5KB · 영웅 약 5.1KB
  }
  class EffectTarget {
    활성 효과 목록, Id 순
    시간 진행 대상 목록
    Ongoing 조건 효과 목록
    소유 파이프라인 · 다음 기한 틱
  }
  class AttributeSet {
    슬롯 Base·Current, 선언 속성마다 40B
    합산 기록, 수정자가 붙은 슬롯만
    수정자 배열 하나, 슬롯·출처·행 순
  }
  class AttributeLayout {
    id → 슬롯 조회표
    클램프 평가 순서
  }
  class TagCountContainer {
    보유 태그와 조상 태그 카운트
  }
  class AbilitySet {
    부여 목록, 어빌리티·출처·레벨
    Version
  }
  class ArchetypeCatalog {
    기본값 · 시작 어빌리티 템플릿
  }
  Unit *-- EffectTarget
  Unit *-- AbilitySet
  EffectTarget *-- AttributeSet
  EffectTarget *-- TagCountContainer
  AttributeSet --> AttributeLayout : 아키타입과 공유
  AbilitySet --> ArchetypeCatalog : 첫 변경 전까지 공유
  ArchetypeCatalog --> AttributeLayout : 아키타입당 하나
```
