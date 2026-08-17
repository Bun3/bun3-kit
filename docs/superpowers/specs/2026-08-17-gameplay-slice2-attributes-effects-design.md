# 게임플레이 슬라이스 2 — Attribute · Effect 심 코어 설계

- 상태: 승인됨
- 작성일: 2026-08-17
- 적용 패키지: `Bun3.Gameplay` (0.12.0 → 0.13.0 예정)
- 기반 명세: [`2026-08-10-gameplay-framework-design.md`](2026-08-10-gameplay-framework-design.md) §8.1–8.2, §13 슬라이스 2
- 참고 분석: Unreal GAS(tranek 문서·UE 5.3 GameplayEffectComponents), idlez(스탯·버프·트리거), PoE 수정자 시스템, EGamePlay

## 1. 목적과 범위

Attribute/AttributeSet(수치 집계)과 Effect(수명·스택·체인을 가진 상태 변경 단위)를 구현한다.
"이속 +20% 8초"가 코드 0줄, "빙결 3중첩 → 동결"이 코드 0줄이 되는 것이 합격선이다.

**핵심 접근 — Effect는 순수 데이터, 로직은 유형별 좁은 시섬(seam).**
GameplayEffect에 자유 훅(OnApplied/OnEvent류)을 두지 않는다. 로직이 필요한 지점을
유형별로 해부해 각각 좁은 시그니처의 확장점으로 만들고(GAS의 data-only GE + MMC/ExecCalc
구도), 조건부·연쇄 동작은 태그·조건·체인 데이터 어휘로 커버한다.

비목표(명시적 이연): §12 이연 목록 참조. 특히 반응형 콘텐츠(이벤트에 반응하는 반사·흡혈류)는
슬라이스 3의 Ability 영역이다 — 이 슬라이스의 Effect는 이벤트를 수신하지 않는다.

## 2. 결정 요약

| 주제 | 결정 |
|---|---|
| Hp 등 소모 리소스 | GAS식 — Hp도 Attribute. Instant가 Base를 영구 변경 |
| Effect의 로직 | 자유 훅 없음. 시섬 3종(MagnitudeCalc/ExecutionCalc/TargetSelector)만 |
| 시섬 식별자 | GameplayTag (예약 루트 `calc.*`, `selector.*`) — raw string 금지 |
| 조건 표현 | `[피연산자, 비교, 피연산자]` 행 — Modifier와 평행한 데이터 어휘 |
| 체인 | 트리거 4종 + 만감. 값(페이로드)은 나르지 않음 |
| 적용 실행 | FIFO 큐 + 틱당 예산. 순환은 컴파일 경고 |
| 클램프 | Current 안전망 + Base 쓰기 상시 클램프 + 경계 이동 정책 2축 |
| 집계 순서 | canonical(인스턴스 id, 행 번호) — BigNum 비결합성 대응 |
| 인스턴스 확장 상태(Var 슬롯) | 보류 — Attribute 승격·스택으로 커버, 실수요 시 재검토 |
| 빌더 동사 | `Build()` (Freeze 아님) |
| 집합체 이름 | `EffectTarget` — AttributeSet + TagCountContainer + ActiveEffects |
| 카탈로그 접근 | 조립 루트 주입. static Instance 금지(§5 전역 상태 없음) |

## 3. 패치 3계층 (설계의 배경 원칙)

| 계층 | 내용 | 수단 |
|---|---|---|
| L0 파라미터 | 수치·수명·스택·태그·조건·체인 변경 | 스펙 데이터 |
| L1 재배선 | 어떤 calc/selector/스펙을 쓸지 교체 | 태그 참조 (redirect로 리네임도 생존) |
| L2 신규 로직 | 새 공식·새 선택기 | 시섬 구현 = 바이너리. 미래: 결정론 스크립트 백엔드가 같은 시섬 ABI에 등록 |

EffectSpec은 로직 필드가 없는 순수 데이터 형태로 강제한다. JSON/proto 로더는 v1에 만들지
않되(첫 소비자는 Steam — 바이너리 패치 무료) 형태가 데이터이므로 나중에 기계적으로 붙는다.

## 4. Operand — 3용도 공용 피연산자 어휘

```
Operand (readonly struct, kind 판별자)
├─ Constant(BigNum)
└─ Attribute(attributeId, 상수 계수 = 1)
```

사용처: ① Modifier 크기 ② Condition 양변 ③ 클램프 경계. Modifier 크기 자리에서만
네 번째 형태(MagnitudeCalc 태그 참조)가 허용된다.

`속성 × 속성` 같은 다항은 데이터로 표현하지 않는다 — MagnitudeCalc의 몫. kind 판별자
덕에 미래의 결정론 Expression 노드(idlez 트리거의 BigNum 교정판)가 스키마 파괴 없이
추가될 수 있으나 v1은 구현하지 않는다.

레벨 스케일: Modifier 크기는 `base + perLevel × (level-1)` 선형 스케일 필드를 지원한다
(Operand 두 개로 구성).

## 5. Attribute 집계

### 5.1 등록 — AttributeRegistry

AttributeId는 게임 proto enum(ushort). 게임이 기동 시 등록하고 `Build()`로 확정한다:

```csharp
builder.Register(Attr.Hp,
    min: Operand.Constant(0),
    max: Operand.Attribute(Attr.MaxHp),
    onMaxIncrease: MaxIncreasePolicy.Follow,     // 기본 Stay
    onMaxDecrease: MaxDecreasePolicy.Follow);    // 기본 Follow
var registry = builder.Build();
```

- 등록 순서는 무의미 — 수집·전방 참조 허용 후 Build에서 일괄 검증한다.
- Build 검증: 참조 해석(미등록 속성 참조 = 예외), 클램프 순환 = 예외, 이동 정책이
  기본값이 아닌데 해당 경계가 속성 참조가 아님 = 예외.
- Build 산출: 평가 위상 순서(동순위는 AttributeId 오름차순 타이브레이크), 속성별
  클램프 후손 목록(즉시 전파용), AttributeId 순 canonical 슬롯 배치.
- 정책은 속성 정체성에 속한다 — 스펙별로 다르게 줄 수 없다. 정의는 코드 등록이며
  패치 데이터가 아니다(클램프 구조는 게임 코드와 강결합, 밸런스로 흔들리는 건 수정자다).
- 아키타입 선언(유닛 종류별 사용 속성 집합)은 별개 계층 — EffectTarget 생성 시 밀집
  슬롯 배치의 근거.

### 5.2 집계 공식과 슬롯

```
Current = Clamp( Override ?? (Base + ΣAdd) × (1 + ΣMulPct) )
슬롯 = (Base, ΣAdd, ΣMulPct, Override, Current 캐시, dirty)   — 전부 BigNum
```

- **Add**: 가산. **Multiply**: 합산식(퍼센트 합산 후 1회 곱 — idlez·GAS 관례).
  **Override**: 최우선, 복수면 EffectInstance id 최대(가장 나중 적용)가 승리.
- **canonical 순서**: BigNum은 절사 때문에 덧셈조차 비결합이므로 수정자 순회는 항상
  (EffectInstance id, 스펙 내 Modifier 행 번호) 순. 인스턴스 id는 단조 증가 발급이라
  권위·미러·재시뮬에서 동일하다.

### 5.3 두 갱신 경로

| 사건 | 처리 |
|---|---|
| Instant가 Base 변경 | Σ 캐시 불변 → 공식 재적용 O(1) + 자기 클램프 + **즉시 전파** — 같은 드레인 내 후속 로직이 신선한 값을 본다 |
| 수정자 세트 변경(적용/제거/스택/토글) | dirty 마킹 → 페이즈에서 canonical 풀 리빌드 |

**즉시 전파**: 어떤 경로로든 Current가 바뀌는 순간, 클램프로 이 속성을 참조하는 후손의
재클램프까지 같은 시점에 위상 순서로 전파한다(사전 계산된 후손 목록, 후손당 O(1)).
불변식: **관찰 가능한 모든 시점에 클램프 불변식(예: Hp ≤ MaxHp)이 성립한다.**
(드레인 중 MaxHp가 변했는데 Hp 재클램프가 페이즈 대기면 후속 조건이 Hp > MaxHp를
관찰하는 창이 생긴다 — 이를 봉인하는 규칙이다.)

### 5.4 클램프 — 항상 규칙 2개 + 이동 정책 2축

수정자는 Base를 만질 수 없다. Base가 변하는 사건은 ① Instant 쓰기 ② 경계 이동
③ Follow 동반 상승, 셋뿐이다.

**항상 규칙(플래그 없음):**
1. Current 클램프는 안전망 — 공식 마지막 단계에서 항상 적용, Base 불변.
   (이속 −200% 슬로우 → Current 0, 해제 시 복원 — 정책과 무관하게 성립)
2. Base 쓰기는 항상 클램프 통과 — 과다 힐이 Base를 MaxHp 위로 못 올리고, 치명
   데미지가 0 아래로 못 내린다.

**경계 이동 정책(속성 정의별):**

| | Stay | Follow |
|---|---|---|
| MaxIncrease (경계 +Δ) | Base 그대로 (기본) | Base += Δ (잃은 량 보존) |
| MaxDecrease (경계 하락) | Base 보존, Current만 눌림 — 경계 복귀 시 복원 | Base를 경계로 잘라 기록 — 초과분 영구 소실 (기본, idlez 의미론) |

- Decrease를 Δ 감산으로 하지 않는 이유: 최대치 디버프가 사망을 유발하는 규칙이 된다.
- Follow+Follow 조합(리소스 표준)의 알려진 성질: 임시 최대치 버프 한 바퀴가 순 회복을
  만든다(버프 사이클링 힐) — 장르 관례로 수용.
- 비율 보존(`Scale`, GAS 샘플 `AdjustAttributeForMaxChange` 관례)은 제3 옵션 후보로
  기록만 — 첫 수요 시 추가(데이터 호환 무파괴).
- GAS는 이 전부를 게임의 AttributeSet C++ 손코드(PreAttributeChange /
  PostGameplayEffectExecute)로 남긴다 — 본 설계는 이를 선언적 정책으로 어휘화한 것.

### 5.5 변경 이벤트

`(attributeId, oldCurrent, newCurrent)`를 EffectTarget의 풀드 이벤트 버퍼에 적재.
C# event 구독이 아니라 틱이 드레인하는 버퍼다. old == new면 미방출. 재클램프·전파로
인한 변경도 그 시점에 적재된다.

## 6. EffectSpec · EffectInstance · 스택

```
EffectSpec (순수 데이터, EffectCatalog 소유 id)
├─ DurationType: Instant | Duration(틱) | Infinite     Period(틱, 선택)
├─ Stack: { maxStack, 재적용: Refresh | AddStack(n)(+지속갱신 여부, 주기리셋 여부),
│           만료: ClearAll | RemoveOneAndRefresh, 만감: Deny | ApplyEffect(+스택리셋) }
├─ Modifiers:   [attributeId, Op, 크기(Operand ①②③ | MagnitudeCalc 태그 ④)] ×n
│               (scaleWithStack 기본 true — 크기 × 스택 수)
├─ Executions:  [ { calc: ExecutionCalc 태그, inputs: Operand[] } ] ×n — Instant/주기 전용
├─ ApplicationConditions / OngoingConditions:  [Operand, 비교op, Operand] ×n
├─ GrantedTags(TagContainer) · 면역 태그 쿼리
└─ 체인: ChainEdge ×n
```

- **Instant는 인스턴스를 만들지 않는다** — 드레인 시점에 실행되고 끝(핸들 없음).
- **Modifiers vs Executions**: Modifiers는 선언적(Duration이면 집계 참여, Instant면
  Base 단순 가감), Executions는 명령적(다속성 공식 — 데미지 파이프라인). GAS의 Meta
  Attribute 관용구는 불필요 — calc가 `Input(n)`으로 스펙 크기 평가값을 직접 받는다.
- **병합은 대상 기준**(AggregateByTarget): 같은 스펙 재적용 = 기존 인스턴스에 스택 규칙.
  소스별 독립 스택은 스펙 분리로 표현.
- **Periodic의 각 틱 = Instant 실행**(Base 영구 변경) — 독이 해제돼도 깎인 Hp는 불변.
  첫 발화는 적용 즉시가 아니라 첫 Period 경과 후(8초/1초 독 = 8회).

```
EffectInstance (풀드)
└─ id(World 단조 발급) · 스펙 id · 남은틱 · 주기 카운트다운 · stack · level ·
   sourceTargetId · enabled(Ongoing 토글 상태)
```

인스턴스별 게임 확장 상태(Var 슬롯)는 두지 않는다. 유닛 스코프 상태는 Attribute로
승격(보호막 = Shield 속성, GAS 관용구), 횟수는 스택으로. 둘로 안 되는 실수요가 나오면
재검토한다.

## 7. 조건과 체인

### 7.1 조건 3자리 (통일 의미론)

| 자리 | 평가 시점 | 미충족 시 |
|---|---|---|
| ApplicationConditions | 드레인 시 1회 | 적용 무산 |
| OngoingConditions | 페이즈 ④, 틱당 1회 | enabled=false 토글(수정자·GrantedTags 꺼짐, 제거 아님) |
| ChainEdge.conditions | 엣지 발화 시 1회 | 엣지 불발 |

조건에 시섬 참조는 불허(데이터 조건의 정적 분석 가능성 유지). "저체력 태그" 같은 임계값
물화는 프리미티브가 아니라 조합으로 창발한다:
`Infinite + OngoingConditions[[Hp, <, MaxHp×0.3]] + GrantedTags[state.lowhealth]`.

### 7.2 체인 엣지

```
ChainEdge = { trigger:  OnApplication | OnCompleteNormal | OnCompletePrematurely | OnStackOverflow
              effect:   EffectSpec 참조 (컴파일 후 id)
              target:   TargetSelector 태그 + BigNum 상수 파라미터[]   (생략 = 원 대상)
              conditions: Condition[] (선택)
              level:    Inherit | Fixed(n) }
```

- source는 원 인스턴스의 source 승계(귀속 유지). 체인은 값을 나르지 않는다 — 크기는
  적용 시점의 속성참조/MagnitudeCalc가 당시 상태를 읽는다.
- 큐에 실린 엣지는 값 튜플(대상 id, 스펙 id, source, level). 드레인 시 대상 소멸이면
  조용히 드랍(카운터만).
- Normal/Prematurely 구분이 "디스펠당하면 페널티" 콘텐츠의 근거다.

### 7.3 표현력 경계 (문서화하는 한계)

체인+태그+스택+주기 = 타이머·카운터 달린 상태기계. 다음은 구조적으로 데이터 밖이다:
이벤트 페이로드 소비(→시섬·슬라이스 3), 임의 수식(→MagnitudeCalc), 공간·조건 대상
선택(→TargetSelector), 교차 개체 조율(→슬라이스 3).

## 8. 시섬 (확장점) 3종

### 8.1 식별 — GameplayTag

시섬 식별자는 raw string이 아니라 태그다(GAS GameplayCue가 태그 식별자인 것과 동일
패턴). 예약 루트 `calc.magnitude.*` / `calc.execution.*` / `selector.*`.

- 구현 클래스에 `[NativeGameplayTag("calc.execution.damage.physical")]` 선언 — 기존
  Native 추출 파이프라인에 실리고, provenance(Native Source)가 분류 태그와의 정체성
  구분을 담당한다. 예약 루트는 검증·Picker 필터 용도.
- 이득: 문법·중복 검증(저작 시점), **redirect로 리네임 생존**(패치 데이터 수명),
  Unity Picker 재사용, 카탈로그 fingerprint에 시섬 계약이 포함되어 서버
  ExpectedFingerprint 검증이 데이터-코드 계약 검증을 겸함.
- 용량: 시섬 수는 콘텐츠가 아니라 코드 규모에 비례(스펙이 늘지 시섬이 늘지 않는다).
  카탈로그 컴파일이 Source·루트별 태그 카운트를 진단 출력한다.

### 8.2 계약

```csharp
public interface IMagnitudeCalc  { BigNum Calculate(in MagnitudeContext ctx); }   // 읽기 전용
public interface IExecutionCalc  { void Execute(ref ExecutionContext ctx); }      // 다속성 읽기·쓰기
public interface ITargetSelector { int Select(in SelectorContext ctx, Span<TargetId> results); }
```

- 인터페이스이므로 게임 측 구현 구조(상속·템플릿 메서드·컴포지션)는 자유 — UE에서
  ExecCalc 계층을 세우던 방식 그대로 가능하다. 등록은 기동 1회, 인스턴스 재사용.
- `MagnitudeContext`(readonly ref struct): Source/Target 속성 Current 읽기, 태그 읽기 뷰,
  Level, StackCount, WorldTick.
- `ExecutionContext`(ref struct): 위 + `Input(n)`(Executions.inputs 평가값),
  `WriteTarget(id, value)`(항상 규칙·즉시 전파·이벤트 강제 통과 — 시섬이 불변식을 우회할
  수 없다), `ApplyToTarget(specId)`(직접 아님 — 적용 큐 적재), `Rng`(World 시드 IRng).
- `SelectorContext`: World 읽기 뷰, source, BigNum 파라미터, Rng. 출력은 caller 버퍼.

**ExecutionCalc가 GAS의 ExecCalc + PostGameplayEffectExecute를 겸한다** — 계산과 분배
(보호막→체력)를 한 함수에서. 대상별 다형성은 가상 메서드가 아니라 대상의 속성·태그로
표현한다(보스 피해 상한 = MaxDamagePerHit 속성). PreAttributeChange/PostGEE형 범용 훅은
제공하지 않는다 — 클램프는 선언(§5.4), 리액션은 이벤트 버퍼가 그 역할이다.

### 8.3 결정론 규칙

| 규칙 | 강제 수단 |
|---|---|
| 난수는 ctx.Rng만 | 구조 — 컨텍스트가 유일 입구, ref struct라 캡처·탈출 불가 |
| 상태 변경은 WriteTarget/ApplyToTarget만 | 구조 — Magnitude/Selector엔 쓰기 API 없음 |
| 재진입 금지 | 구조 — ApplyToTarget이 큐 적재 |
| 시간은 WorldTick만 | 문서 + 리뷰 (아날라이저 후보 백로그) |
| float/double 금지, BigNum·정수만 | 문서 (시그니처가 유도) |

## 9. 실행 기계 — EffectTarget과 틱 파이프라인

### 9.1 EffectTarget

`EffectTarget` = AttributeSet + TagCountContainer + ActiveEffects + 이벤트 버퍼의 집합체
(GAS AbilitySystemComponent의 자리). 슬라이스 3의 Unit이 이를 품는다.

- 식별자는 중립 `TargetId` — UnitId를 기계에 박지 않는다. 아이템·스킬 인스턴스가
  EffectTarget을 품을 수 있는지(WoW 무기 오일 사례)는 **슬라이스 4 공식 안건**으로
  이연하되, 중립 id와 독립 집합체 덕에 어느 답이든 기계 수정이 없다.
- 파이프라인은 `IEffectTargetResolver`(TargetId → EffectTarget) 추상 위에 동작 —
  슬라이스 2 테스트는 딕셔너리 스텁, 슬라이스 3 World가 실구현.
- GrantedTags는 인스턴스 활성 시 TagCountContainer에 카운트로 실리고 비활성/제거 시
  회수된다 — 슬라이스 1 인프라 소비.

### 9.2 틱 파이프라인

```
① 적용   큐 드레인(FIFO, 틱당 예산 N) — 면역 → 적용조건 → Instant 실행 또는 병합/신규
          OnApplication 체인은 같은 큐 적재(예산 내 같은 틱), 초과분 다음 틱 이월+경고
② 시간   인스턴스 id 순: ttl 감소, Period 발화(Instant 실행), 만료 제거(id 순),
          만료 체인 큐 적재 → 다음 틱 ①에서 처리 (1틱 지연 수용 — 20~30tps에서 33~50ms)
③ 재계산 1차   dirty 속성 위상 순 재집계·클램프, 이벤트 적재
④ 조건   Ongoing 일괄 평가(틱당 1회 — 경계값 진동이 틱당 1회로 유계) → enabled 토글
⑤ 재계산 2차   토글분만 — 이로 인한 조건 변화는 다음 틱 관찰. 재계산 최대 2패스로 유계
⑥ 방출   이벤트 버퍼 확정 (복제 큐·게임 소비)
```

큐+예산이 순환 체인을 행이 아니라 "틱당 1회 도는 느린 루프"로 강등시킨다 — 그래서
컴파일 타임 순환 검출은 오류가 아니라 경고다(§10).

### 9.3 제거 API

`RemoveByTags(태그 쿼리)` / `RemoveById(인스턴스 id)` → OnCompletePrematurely 경로.
Infinite는 디스펠·Ongoing 미충족 제거·소유 소스 소멸(슬라이스 3~4)로만 죽는다.

## 10. EffectCatalog — Build 체인의 마지막 고리

빌더 수집 → `Build(tagCatalog, seamRegistry, attributeRegistry)`:

- **전수 해석**: 스펙 참조·시섬 태그·속성 id·태그 문자열 → 전부 id/인스턴스 직결로
  컴파일. 미해석 = 기동 예외. **런타임에 문자열·딕셔너리 조회 없음.**
- **정합성 거부**(문서화 대신 컴파일 오류): Instant에 Ongoing 조건·GrantedTags·Period,
  maxStack 없는 만감 정책, Period 없는 Duration/Infinite 스펙에 Executions(실행 시점이
  없다 — Instant이거나 Period가 있어야 한다) 등.
- **체인 순환 = 경고**: OnApplication만으로 닫히는 순환은 높은 심각도, Duration/Period
  경유(감쇠 루프 가능성)는 낮은 심각도.

기동 Build 순서(뒤가 앞을 참조): **TagCatalog → SeamRegistry → AttributeRegistry →
EffectCatalog → World/EffectTarget.** 전부 조립 루트가 주입한다 — static Instance는
두지 않는다(프레임워크 스펙 §5 "전역 상태 없음", Unity 에디터의 다중 카탈로그 현실,
테스트 격리). 게임 측에서 기동 시 static GameplayTag 핸들을 1회 채우는 관례는 허용
(문서화만).

## 11. 예측·롤백 대비 불변식 (이번 슬라이스가 보증하는 것)

FPredictionKey류 기계는 만들지 않는다(복제 계층 몫, 모델 미정). 대신 어느 예측 모델이
와도 요구하는 성질을 계약으로 보증한다:

1. Instant(Base)/수정자(집계) 분리의 엄격성 — 수정자는 Base에 도달 불가.
2. **무흔적 제거**: apply→remove 후 Current 비트 동일(전체 재집계 방식의 귀결).
3. 상태 스냅샷→복원 비트 동일(EffectTarget 전체 — v1은 인메모리 복사로 충분,
   proto 직렬화는 슬라이스 4).
4. 이벤트에 EffectInstance id 포함 — 미래 상관(correlation) 지점.

## 12. 이연 목록

| 항목 | 어디로 |
|---|---|
| 반응형 콘텐츠(이벤트→발동·반사·흡혈), 이벤트 라우팅 | 슬라이스 3 Ability |
| TargetSelector 호출 배선(인터페이스는 본 슬라이스 출하) | 슬라이스 3 World |
| 아이템·스킬의 EffectTarget 보유 여부 | 슬라이스 4 안건 |
| 스펙·인스턴스 proto 직렬화, dirty 추적 | 슬라이스 4 |
| EffectSpec JSON 로더(패치 데이터 출하) | 모바일 소비자 발생 시 |
| Expression 노드(결정론 수식 트리), 스크립트 백엔드 | 로드맵 — Operand kind·시섬 ABI가 자리 |
| MaxChange `Scale`(비율 보존) 정책 | 첫 수요 시 |
| 시간 소스 아날라이저(WorldTick 강제) | 백로그 |

## 13. 테스트 전략

**결정론 오라클 3종** (플랜 단계에 스켈레톤 포함 — 슬라이스 1 교훈):

1. **순서 셔플 불변**: 같은 수정자 집합을 무작위 순서로 적용/제거 후 결과 비트 동일 —
   canonical 집계의 직접 검증(BigNum 비결합성 때문에 자명하지 않다).
2. **무흔적 왕복**: 시드 랜덤 스펙으로 apply→remove N회 후 초기와 비트 동일.
3. **시나리오 상태 해시**: 고정 시드 시나리오(적용·틱·디스펠·체인) 수백 틱 후 상태 해시
   골든 고정. 같은 파일을 .NET 테스트와 Unity Player(IL2CPP/Mono)에 링크해 백엔드 간
   비트 동일 검증(Fixed64 conformance의 파일 공유 패턴).

**기계별**: 이동 정책 매트릭스(버프 사이클링 힐 포함), 즉시 전파(클램프 불변식 창 회귀),
스택 규칙·만감 체인, Normal/Prematurely 구분, 조건 토글 틱당 1회, 큐 예산·이월,
Build 예외 전 경로(오류 하나당 테스트 하나).

**무할당 스모크**: 정착 상태 틱 반복에서 `GC.GetAllocatedBytesForCurrentThread` == 0.
적용·만료 순간은 풀 왕복만 허용.

## 14. 버전

기능 추가(파괴 없음): `Bun3.Gameplay` 0.12.0 → 0.13.0, UPM 동반. 시섬 태그 예약 루트가
게임 카탈로그에 처음 등장하므로 카탈로그 스키마 영향은 없음(일반 태그일 뿐).
