# 게임플레이 슬라이스 3 — Ability · Unit · World 설계

- 상태: 승인됨
- 작성일: 2026-08-20
- 적용 패키지: `Bun3.Gameplay` (0.13.0 → 0.14.0 예정)
- 기반 명세: [`2026-08-10-gameplay-framework-design.md`](2026-08-10-gameplay-framework-design.md) §8.3·§8.5, §13 슬라이스 3
- 직전 슬라이스: [`2026-08-17-gameplay-slice2-attributes-effects-design.md`](2026-08-17-gameplay-slice2-attributes-effects-design.md) — 본 스펙은 그 §12 이연 목록에서 출발한다
- 참고 분석: Unreal GAS(Lyra 5.3 실코드), idlez(GameSkill·GameBuff·ResourceTrigger + 출하 데이터), growninja(ReactBuff + 출하 데이터), growdaemon(ResourceBuff·ResourceUnit.Trigger)

## 1. 목적과 범위

슬라이스 2가 남긴 세 구멍을 메우고 능동 발동 계층을 세운다.

1. **이벤트 → 반응형 콘텐츠**(반사·흡혈·처치 반응) — 슬라이스 2 §12 이연
2. **TargetSelector 호출 배선** — 인터페이스는 출하됐으나 후보를 열거할 수단이 없었다
3. **EffectTarget을 품는 Unit**과 그 레지스트리인 World
4. **Ability** — 게이트(태그·조건·코스트·쿨다운)와 부여 소스 4종

합격선: "피격 시 공격자에게 받은 피해의 30%를 되돌린다"가 코드 0줄, "쿨다운 20% 감소 반지"가
코드 0줄.

함께 처리하는 백로그 3종: 수식 파싱 1회화, 출하 컴파일 아티팩트, 셀렉터 절단 카운터.

**비목표(명시적 이연)**: §14 참조. 특히 시전 인스턴스(타임라인 액터)는 위치·공간 수치에
의존하므로 이동 스펙 이후다.

## 2. 결정 요약

| 주제 | 결정 |
|---|---|
| Unit ↔ EffectTarget | **합성** — Unit이 EffectTarget을 보유. EffectTarget은 sealed 유지·무수정 |
| 반응형 수신자 | **Effect**(체인의 `OnEvent` 트리거). Ability가 아니다 |
| 이벤트 큐 | **없음** — 발행 즉시 체인 매칭 → 기존 적용 큐로. 새 기계 0개 |
| 이벤트 페이로드 | `(Tag, Source, Target, Magnitude, NominalMagnitude)` — 체인 값 전달 규칙을 `OnEvent`에 한해 개정 |
| magnitude | **실적용**(클램프 후) 기본. Operand kind 1종. 명목값은 이벤트에 동승하되 Operand 없음 |
| magnitude 소스 | **속성 명시** — `RaiseEvents`가 태그와 속성 id를 짝으로 선언 |
| Ability의 로직 | **없음**(순수 데이터). `IAbilityBehavior` 시섬 불채택 — 시섬은 기존 3종 유지 |
| 쿨다운 | Effect로 표현(GrantedTags ↔ BlockedTags). 감소는 `DurationScale`이 속성 참조 |
| 코스트 | **즉시 차감**(Effect 우회 안 함) — 게이트 검사와 차감의 원자성 |
| 발동 API | ~~동기 반환(사유 enum)~~ → **§16 개정**: World 명령 큐에 적재, 틱 안에서 처리, 결과는 `PendingActivations` 버퍼 + 부수효과 없는 `CanActivateAbility` 조회 |
| 스폰/디스폰 | 큐 적재 후 페이즈 경계 반영 — 순회 중 컬렉션 변경 금지 |
| 셀렉터 후보 | `resolver.TargetIds` 전체 스팬(오름차순) + 태그·속성 접근자 |
| 시전 인스턴스 | 불채택 — 즉발 발동만 |

## 3. 조사 근거 (결정의 배경)

세 레거시와 GAS를 실코드·출하 데이터까지 확인한 결과가 §2의 두 핵심 결정을 정했다.

### 3.1 반응 주체는 언제나 Buff였다

| | 반응 주체 | 표현 방식 | 페이로드 | 성숙도 |
|---|---|---|---|---|
| growdaemon | `UnitBuff` | 하드코딩 필드 ~20종 + 행동 태그 | C# 시그니처로만 | 원시 |
| growninja | Buff | `ReactBuff` = 버프 조합/교체만, 이벤트 반응 없음 | 없음 | 미도달 |
| idlez | `GameBuff` | 일반 트리거(표현식 트리) | `Damage`/`ValidDamage`/`IsCritical`/`Return` | 성숙 |

셋 다 반응이 Buff(= 우리 Effect)에 붙는다. 별도 Ability 개념을 거치지 않는다.

진화 방향도 명확하다 — **하드코딩 필드 → 일반 트리거 기계**. growdaemon이
`killAddBuffs`·`mainAttackHitAddBuffs`·`guardianHPPercent`·`infectionAddBuffs`·`auraBuffID`·
`disposableImmortalReplaceBuffID`·`unattackedLevelUpDuration` 같은 필드를 하나씩 늘려 겪은
비용을 idlez가 일반 트리거로 해결했다. 본 슬라이스는 한 걸음 더 간다 — 표현식 트리 없이
데이터 어휘(체인 + 조건 + 셀렉터)로.

growdaemon은 여기에 더해 `UnitBuff.HandleAttacked`가 `Tag.DESTROY_ON_ATTACKED`·
`Tag.RESET_LEVEL_ON_ATTACKED`·`Tag.RESTART_TIMER_ON_ATTACKED_BY_BOSS`로 분기한다.
프레임워크 설계 §8.2가 명시적으로 기각한 동작-태그 혼합이며, 본 스펙도 채택하지 않는다.

### 3.2 반응형은 희소하고, 값 비례 반사는 더 희소하다

idlez 출하 데이터(`Triggers.json`, 트리거 134개) 타입 분포:

| 트리거 | 개수 | 본 레포 대응 |
|---|---|---|
| `OnUpdate` | 86 (64%) | 이미 있음 — `PeriodTicks` 주기 실행 |
| `OnDestroy` | 31 (23%) | 이미 있음 — 만료/조기제거 체인 |
| `OnAttacked` | 4 | 본 슬라이스 |
| `OnAttackedPost` | 3 | 본 슬라이스 |
| `OnKill`·`OnDead`·`OnBuffApply`·`OnAttack`·`OnOwnerKill` | 각 1~2 | 본 슬라이스 |

반응형 훅은 16건(12%)이고 87%는 이미 보유한 기계다. 그 16건의 실체:

```
Buff_HealOnHpLossLuckScaling_Attacked            피격 시 Luck 확률로 회복 버프 부여
Buff_IncreaseBuffLevelOnMonsterHit_Attacked      피격 시 버프 레벨 증가
Buff_IncreaseBuffStackOnMonsterHit_Attacked      피격 시 버프 스택 증가
Buff_UseSkillToAttackerWhenDamaged_AttackedPost  피격 시 공격자에게 스킬     ← 유일한 반사
Buff_UseSkillWhenDamaged_AttackedPost            피격 시 스킬
```

출하 콘텐츠의 반사는 1건이고 값 비례가 아니다. 그럼에도 `EventMagnitude`를 본 슬라이스에
넣는 것은 사용자 결정이다 — "받은 피해의 30%"가 장르 표준 어휘이고, 나중에 넣으면
`OnEvent` 스키마를 다시 여는 비용이 든다.

growninja 쪽 대조군: `Buffs.xml`의 `ReactBuff` 60건 전부가 `RemoveBuffID`(37)·`AddBuff`(17)·
`MinStack`(22)이고 Type은 `REPLACE` 20 / `FUSION` 3이다. 파서에 선언된
`addDamages`·`selfAddBuffs`·`addShield`는 출하 데이터에서 사용 0건 — 능력을 미리 뚫어두면
안 쓰인다는 증거로 §14의 이연 판단에 반영했다.

### 3.3 피해 수정 훅은 불필요하다

idlez의 `Return` 기계(훅이 damage를 고쳐 돌려주는 경로)는 만들어졌으나 출하 데이터가 거의
쓰지 않는다 — 확인한 `OnAttacked` 트리거는 `Return = Damage`로 통과시킨다. 실제 피해 감소는
전부 스탯이다: `ApplyDefense`(Defense/MagicResist), `ApplyDamageTakenEfficiency`,
`ApplyArmor`(방어 계수 테이블). growninja `addShield`는 출하 사용 0건이고 idlez는 SP로 대신한다.
전부 Attribute + Multiply 수정자로 커버되므로 **피해 수정 훅을 도입하지 않는다.**

대가를 명시한다: 본 레포는 피해가 "Instant Effect의 Hp Add"라 idlez 같은 고정 피해
파이프라인(치명타 → 방어구 → 방어력 → 피해효율 순서)이 없다. 그 순서는 게임이
`MagnitudeCalc` 또는 Operand 조합으로 스스로 정한다.

### 3.4 GAS는 발행 주체가 게임 코드다

Lyra 5.3 `ULyraHealthComponent::HandleOutOfHealth`가 `FGameplayEventData`를 채워
`ASC->HandleGameplayEvent(...)`를 호출한다. 페이로드는 `EventTag`·`Instigator`·`Target`·
`OptionalObject`·`InstigatorTags`·`TargetTags`·`EventMagnitude`. 수신은
`FAbilityTriggerData { TriggerTag, TriggerSource }`이고 `EGameplayAbilityTriggerSource`는
`GameplayEvent`/`OwnedTagAdded`/`OwnedTagPresent` 3종이다.

GAS의 GameplayEffect에는 "이 GE가 이런 이벤트를 발행한다"는 필드가 없다 — 속성 델리게이트를
게임이 구독해 이벤트로 변환한다. 이 지점에서 갈라진다: 그대로 두면 모든 게임이 같은 변환
계층을 다시 짜게 되고, 이는 레포 제1 원칙 위반이다. 본 스펙은 발행을 **데이터 선언**으로
내린다(`EffectSpec.RaiseEvents`).

### 3.5 쿨다운이 아니라 확률이 실전 관례다

idlez의 반응 훅은 Buff에 달려 쿨다운 개념이 없고, 발동 제어를 확률로 한다
(`Buff_HealOnHpLossLuckScaling_Attacked`가 `Luck` 유닛 변수로 확률을 스케일). 본 레포는 이미
`ChanceToApply`(슬라이스 2.1 G2)를 갖고 있고 `MagnitudeDef`를 받으므로 속성 참조 확률까지
표현된다. 따라서 "반응형에 게이트가 필요하니 Ability여야 한다"는 논거는 성립하지 않는다.

### 3.6 idlez GameSkill은 Ability가 아니다

`GameSkill`은 proto 필드가 `position`·`direction`·`velocity`·`acceleration`·`timelineSpeed`·
`tick`·`hitCountByUnitIds`인 **시전 중인 개체**(투사체/장판/타임라인 액터)다.
`GameBoard.skills_`에 유닛과 별개의 id 공간(`nextSkillId_`)으로 등록되고, 타임라인이
`Hit`·`UnitKnockback`·`UnitCharge`·`PlayFx`·`AddSkill`·`SelfAddBuff`·`Destroy`를 시간축에서
실행한다.

셋으로 갈린다: `ResourceSkill`(정의) / `GameUnit.cooldowns_`(유닛이 소유한 쿨다운) /
`GameSkill`(시전 개체). 본 슬라이스의 `AbilityDef` + `AbilitySet`은 앞의 둘에 대응하고,
`GameSkill`에 해당하는 것은 만들지 않는다(§14).

`GameSkill`에는 AttributeSet도 버프 목록도 없다 — **시전 개체는 이펙트 타깃이 아니다.**
이 사실이 §4의 합성 결정을 뒷받침한다.

### 3.7 틱 모델의 선례

```
GameBoard.Update():   board.RunLogic → unit.RunLogic → skill.RunLogic → buff.RunLogic
                    → unit.Update → FlushPosition → 공간 인덱스 재구축
                    → dropItem.Update → skill.Update → buff.Update
GameBoard.PostUpdate(): postActions → Add큐 반영 → Destroy 정리 → buff Add/Remove/Finalize
```

`RunLogic`(트리거·타임라인)과 `Update`(상태 전진)를 분리하고 **추가·제거를 전부 큐로 미룬다.**
본 레포 `EffectPipeline`의 6페이즈와 같은 철학이며, §5의 스폰/디스폰 큐가 이 선례를 따른다.

## 4. Unit — EffectTarget 합성

```
Unit
├─ TargetId Id
├─ EffectTarget Effects        AttributeSet + TagCountContainer + ActiveEffects + DR 이력
├─ AbilitySet Abilities
└─ ulong OwnerId               0 = 무소유(NPC·몬스터)

전달 프로퍼티(상태 중복 없음): Attributes => Effects.Attributes, Tags => Effects.Tags
```

소유자를 `ulong`로 두는 이유: 심은 세션·플레이어 개념을 모른다(프레임워크 설계 §4).
`Bun3.Server.Players`의 식별자 타입을 끌어오면 `Bun3.Gameplay`가 서버 패키지에 의존하게 된다.
`TargetId`가 id 중립인 것과 같은 규율이며, 게임·서버 글루가 자기 식별자를 이 자리에 매핑한다.

**상속을 채택하지 않은 이유** (직렬화가 결정적):

1. **proto에는 상속이 없다.** 와이어는 어차피 합성(`UnitSnapshot ⊇ EffectTargetSnapshot`)이므로
   클래스 계층이 직렬화에서 벌어주는 것이 없다. 합성은 객체 그래프와 메시지 트리가 1:1이다.
2. **판별자 세금.** TargetId 하나로 여러 종류를 가리키면 복원 시 종류 기록 + 팩토리 분기가
   필요하다(`oneof` + kind). 합성이면 `UnitSnapshot`/`ItemSnapshot`이 스키마 레벨에서 이미
   다르고 각자 `effect_target`을 optional로 품는다.
3. **반쪽 스냅샷 불가능.** `EffectTarget.CreateSnapshot()`은 이미 출하됐고
   `EffectTargetSnapshot`은 전 멤버 internal인 opaque 토큰이다. sealed를 풀면
   "어빌리티가 빠진 스냅샷을 조용히 반환하는 호출 경로"가 생긴다. 합성은 그것을 표현할 수
   없다 — 슬라이스 2가 수명·스택을 enum으로 고정한 "잘못된 상태 표현 불가"와 같은 논리다.
4. **dirty 추적(슬라이스 4)이 경계를 그대로 쓴다.** `Effects`·`Abilities`·`Inventory`가 각자
   dirty를 들고 복제 수집기가 하위 집합체를 순회한다.
5. **아이템 확장의 선택성.** 슬라이스 2 §12가 "아이템·스킬의 EffectTarget 보유 여부"를
   슬라이스 4 안건으로 열어뒀다. 합성이면 `ItemInstance.Effects`가 대부분 null이고 필요한
   아이템에만 붙는다. 상속이면 스택형 잡템까지 속성 슬롯과 태그 카운터를 진다.
6. **미러 클라의 부분 상태.** 시야 필터에 걸린 유닛은 `Effects = null`인 껍데기가 자연스럽다.

상속의 이점 둘은 작다 — 객체 헤더 하나(~24B, 스폰 경로이지 틱 핫패스가 아님)와 역조회인데,
역조회 경로는 실제로 없다(`ExecutionContext`·`SelectorContext`가 이미 `TargetId`를 들고 있고
이벤트 라우팅도 TargetId로 World에 묻는다). 필요해지면 `object? Owner` 역포인터 한 줄이다.

### 4.1 UnitSnapshot

```
UnitSnapshot { EffectTargetSnapshot Effects; AbilitySetSnapshot Abilities; ulong OwnerId; }
```

`EffectTarget.CreateSnapshot`/`RestoreSnapshot`은 수정하지 않는다. World 수준 스냅샷은
유닛 스냅샷 배열 + 파이프라인 틱 카운터 + 다음 발급 인스턴스 id + Rng 상태를 함께 싣는다
(슬라이스 2가 "캘러가 별도 복원"으로 남긴 항목들).

## 5. World

> **§16 개정**: 발동은 `TryActivateAbility` 동기 반환이 아니라 World 명령 큐(`Enqueue*`)로 바뀌었고, 틱 순서에 ⓪-1 버퍼 비움·⓪-2 명령 처리가 들어갔다. 이 절의 World API·틱 서술은 §16이 대체한다.

```
World : IEffectTargetResolver
├─ Unit 레지스트리 (TargetId 오름차순 — resolver 계약)
├─ EffectPipeline·IRng 소유 (조립 루트 주입, static Instance 금지)
├─ TryResolve(id, out EffectTarget?) → unit.Effects
├─ TargetIds → 오름차순 스팬
├─ Spawn(unit) / Despawn(id)       큐 적재 → 페이즈 경계 반영
├─ TryActivateAbility(unitId, abilityId) → AbilityActivationResult   (동기)
├─ CurrentTick => pipeline.CurrentTick      틱 카운터는 파이프라인이 단독 소유
└─ Tick():  ⓪ 스폰/디스폰 큐 반영 → EffectPipeline.Tick()
```

World는 별도 틱 카운터를 두지 않는다. 두 카운터가 생기면 스냅샷·복원에서 어긋날 수 있고,
`EffectPipeline.CurrentTick`은 이미 슬라이스 2에서 복원 replay를 위해 internal setter까지
갖췄다.

- **범용 Command 타입 계층을 만들지 않는다.** 소비자가 발동·스폰·디스폰 셋이고 발동은 사유를
  동기 반환해야 한다. `PostAsync` 액터 래퍼는 `Bun3.Server.Gameplay`(슬라이스 5) 몫이며 심은
  동기 API만 낸다 — 프레임워크 설계 §5의 액터 모델은 서버 글루 계층에서 성립한다.
- 스폰/디스폰만 지연하는 이유는 §3.7 — 틱 순회 중 레지스트리 변경 금지. 발동이 거는 Effect는
  기존 적용 큐로 들어가므로 별도 지연이 필요 없다.
- 디스폰된 유닛을 대상으로 남은 적용 요청은 기존 `UnresolvedTargetDropCount` 경로로 조용히
  드랍된다(슬라이스 2 계약 유지).

## 6. 이벤트와 OnEvent 체인

### 6.1 새 큐를 만들지 않는다

이벤트는 `ApplyInstant`·`FirePeriodic` 안에서 발생하며, 그 자리에서 `OnEvent` 체인을 매칭해
`EnqueueApply`만 한다. 재진입이 없고 순환은 **기존 적용 큐의 틱당 예산**이 그대로 유계화한다.
슬라이스 2 §9.2의 "큐+예산이 순환 체인을 틱당 1회 도는 느린 루프로 강등시킨다"가 이벤트에도
그대로 적용된다.

### 6.2 발행 — EffectSpec.RaiseEvents

```
RaiseEvents: [{ Tag: GameplayTag, AttributeId: ushort }]
```

Instant 적용과 주기 실행에서, 지정 속성의 Base 변화를 magnitude로 삼아 발행한다.
속성을 명시하는 이유: 한 Effect에 modifier가 여럿이면 합계가 무의미하다(Hp −100, Mp +50).

```
GameplayEvent = (Tag, Source: TargetId, Target: TargetId,
                 Magnitude: BigNum, NominalMagnitude: BigNum)
```

- `Magnitude` = **클램프 후 실제 Base 변화량**. `AttributeSet.AddBase → SetBase → ClampToBounds`
  경로 때문에 명목과 다르다(HP 300인데 500 피해 → 300). before/after 차분으로 얻는다.
- `NominalMagnitude` = 클램프 전 명목값. 이벤트에 동승하지만 **읽는 Operand는 두지 않는다** —
  세 레거시 출하 데이터에서 명목값 수요 사례를 찾지 못했다. 수요 발생 시 Operand kind 추가는
  비파괴다.
- `Source`는 그 적용의 시전자, `Target`은 적용 대상이다.

### 6.3 수신 — ChainTrigger.OnEvent

```
ChainTrigger.OnEvent                    (5번째 멤버)
ChainEdgeDef.EventTag: GameplayTag      OnEvent일 때만 유효
ChainEdgeDef.Listen: AsTarget | AsSource
```

이벤트가 발생하면 두 방향을 검사한다:

- `Target` 유닛의 활성 Effect 중 `Listen = AsTarget`인 `OnEvent` 체인 → 반사·피격 반응
- `Source` 유닛의 활성 Effect 중 `Listen = AsSource`인 `OnEvent` 체인 → 흡혈·가해 반응

매칭은 이벤트 태그가 체인 선언 태그의 후손-또는-자기(`IsAncestorOrSelf(선언, 이벤트)`)일 때
성립한다 — 디스펠·면역 매칭과 같은 방향이다.

`Enabled = false`인 인스턴스는 수신하지 않는다(수정자·GrantedTags가 꺼진 것과 일관).

순회 순서는 canonical — 활성 인스턴스 id 오름차순, 인스턴스 안에서는 체인 선언 순.
BigNum 비결합성 때문에 발화 순서가 결과에 남으므로 슬라이스 2의 집계 순서 규율을 그대로 쓴다.

### 6.4 내장 셀렉터

`EventSource` / `EventTarget` 두 종을 프레임워크가 제공한다. 셀렉터 태그를 생략하면
슬라이스 2 계약대로 원 대상을 그대로 쓴다.

### 6.5 Operand.EventMagnitude

```
Operand.EventMagnitude(coefficient)     실적용 magnitude × 계수
```

`OnEvent` 체인이 건 Effect의 modifier 크기·조건 양변·Execution 입력에서만 유효하다.
클램프 경계에서는 금지(경계는 상시 평가되므로 이벤트 문맥이 없다).

**슬라이스 2 §7.2 개정**: "체인은 값을 나르지 않는다"는 `OnApplication`·`OnCompleteNormal`·
`OnCompletePrematurely`·`OnStackOverflow` 4종에만 적용된다. 그 근거("크기는 적용 시점의
속성참조가 당시 상태를 읽는다")가 이벤트 magnitude에는 성립하지 않는다 — 방금 일어난 일에
대한 일시적 사실이지 상태가 아니다. `OnEvent`는 명시적 예외다.

큐에 실리는 값 튜플이 (대상 id, 스펙 id, source, level)에서 (…, eventMagnitude)로 확장된다.

### 6.6 핫패스 비용

컴파일 시 스펙에 "`OnEvent` 체인 보유" 플래그를 세우고, `EffectTarget`이 활성 인스턴스 중
리스너 수를 카운트한다. 카운터가 0이면 이벤트 발행이 즉시 반환한다. 매칭 경로에 할당이 없다
(스팬·인덱스만 사용).

### 6.7 Build 검증

- `RaiseEvents.AttributeId`가 그 스펙의 modifier 대상에 없음 = 오류
- `RaiseEvents.AttributeId`가 미등록 속성 = 오류
- `EventMagnitude` Operand가 `OnEvent` 체인으로 도달 불가능한 스펙에 있음 = 오류
- `EventTag`가 `OnEvent`가 아닌 트리거에 선언됨 = 오류
- `OnEvent`인데 `EventTag`가 비어 있음 = 오류
- 클램프 경계에 `EventMagnitude` = 오류
- `OnEvent` 체인이 자기 스펙의 `RaiseEvents`로 닫히는 순환 = **경고**(기존 체인 순환 검출 확장)

### 6.8 알려진 한계

반응이 **자기 자신의 상태를 바꾸는 것**은 표현할 수 없다. growdaemon의
`DESTROY_ON_ATTACKED`·`RESET_LEVEL_ON_ATTACKED`·`RESTART_TIMER_ON_ATTACKED_BY_BOSS`가 그
부류다. 체인은 "다른 Effect를 적용"만 하기 때문이다.

우회: 반응이 거는 Effect가 `RemoveOnApplyTags`(G1)로 원 Effect의 AssetTag를 지정하면 자기
제거까지는 된다. 레벨 리셋·타이머 재시작은 "제거 후 재적용"(Level 1로 새 인스턴스) 근사다.
`ChainEdgeDef.SelfAction: None | RemoveSelf | Refresh`는 실수요 시 비파괴 추가로 남긴다.

### 6.9 반사 예시 (코드 0줄 확인)

```
피해 스펙 "FireBolt" (Instant)
  Modifiers:   [{ Hp, Add, SourceAttribute(Attack) × -1.2 }]
  RaiseEvents: [{ Tag: event.damage.taken, AttributeId: Hp }]

반사 버프 "Thorns" (Infinite)
  Chains: [{ Trigger: OnEvent, EventTag: event.damage.taken, Listen: AsTarget,
             EffectName: "ThornsRetaliate", Selector: EventSource, LevelRule: Inherit }]

"ThornsRetaliate" (Instant)
  Modifiers: [{ Hp, Add, EventMagnitude × 0.3 }]
```

**magnitude는 부호가 있는 Base 변화량이다.** 위 예에서 피해 이벤트의 magnitude는 −100(Hp가
줄었으므로)이고, 반사는 `× 0.3 = −30`을 공격자 Hp에 Add하므로 30 피해가 된다. 회복 이벤트라면
magnitude가 양수이므로 같은 계수가 회복을 전파한다 — 부호가 의미를 나르기 때문에 절댓값을
쓰지 않는다.

흡혈은 같은 이벤트에 `Listen: AsSource` + 셀렉터 생략(= 시전자 자신) +
`Hp, Add, EventMagnitude × -0.1` → `−100 × −0.1 = +10`, 10 회복이 된다.
확률 반사는 `ThornsRetaliate`에 `ChanceToApply`(G2)를 달면 된다.

## 7. Ability

### 7.1 AbilityDef — 순수 데이터

```
AbilityDef
├─ Name, MaxLevel
├─ AssetTags[]                              어빌리티 분류 (쿨다운 Effect가 겨냥하는 대상)
├─ RequiredTags[] / BlockedTags[]           소유자 태그 게이트
├─ ActivationConditions: ConditionDef[]     Operand 비교 (슬라이스 2 조건 어휘 재사용)
├─ Costs: [{ AttributeId, MagnitudeDef }]
├─ CooldownEffect: string?                  발동 성공 시 자신에게 적용할 Effect 이름
└─ Effects: [{ EffectName, SelectorTag?, SelectorParams[], LevelRule }]
```

`IAbilityBehavior` 시섬을 두지 않는다. 소비자가 0개이고, 임의 로직 자리는 이미
`IExecutionCalc`가 있다(어빌리티가 Effect를 적용하고 그 Effect가 Execution을 갖는 경로).
시섬은 기존 3종(`IMagnitudeCalc`·`IExecutionCalc`·`ITargetSelector`)을 유지한다.
필요해지면 추가는 비파괴다.

### 7.2 발동 순서와 결과

> **§16 개정**: 진입점은 `World.EnqueueActivation`(틱 안에서 처리)과 `CanActivateAbility`(조회)로 바뀌었고, 게이트 4번에 `OnCooldown`이 추가됐다(§16.4). 아래 게이트·차감 의미는 그대로다.

```
TryActivateAbility(unitId, abilityId) → AbilityActivationResult
```

레벨은 인자가 아니라 **부여 시 기록된 값**이다(§7.4) — 같은 어빌리티를 여러 소스가 주면 최대
레벨을 쓴다. 호출자가 임의 레벨을 넘길 수 있으면 게이트를 통과한 뒤 크기만 부풀리는 경로가
생긴다.

1. 유닛 해석 → 실패 시 `UnknownUnit`
2. 카탈로그 해석 → 실패 시 `UnknownAbility`
3. 부여 여부 → 미부여면 `NotGranted`
4. `RequiredTags` 미충족 → `MissingTag`
5. `BlockedTags` 보유 → `BlockedByTag` (쿨다운은 이 경로로 잡힌다)
6. `ActivationConditions` 미충족 → `ConditionFailed`
7. `Costs` 전부 `Base >= 비용` 검사 → 미달이면 `CostInsufficient`. **`Current`가 아니라 `Base`를
   검사한다** — 자원은 `Base`에 산다(즉시 피해·회복은 `AddBase` 쓰기)는 점과, `Base` 검사라야
   차감이 클램프로 깎이지 않고 정확하다는 점 때문이다.
8. **코스트 즉시 차감** → `CooldownEffect` 적용 → `Effects` 각 항목을 셀렉터로 대상 선택 후
   `EnqueueApply` → `Success`

게이트 실패는 예외가 아니라 사유 반환이다(프레임워크 설계 §10). 7~8이 한 호출 안에서 끝나야
같은 틱 다중 발동에서 이중 소모 검사가 깨지지 않는다 — 이것이 **코스트를 Effect로 우회하지
않는 이유**다. 코스트 감소는 `MagnitudeDef`가 속성을 참조하면 되므로 데이터로 커버된다.

```
AbilityActivationResult:
  Success | UnknownUnit | UnknownAbility | NotGranted | MissingTag
| BlockedByTag | ConditionFailed | CostInsufficient
```

### 7.3 쿨다운 = Effect

프레임워크 설계 §8.3의 "쿨다운·코스트는 내부적으로 Effect로 표현(쿨다운 감소 아이템이 공짜)"를
다음으로 구현한다.

```
"Cooldown.Fireball" (Duration, DurationTicks: 200)
  GrantedTags:   [cooldown.fireball]
  AssetTags:     [cooldown]
  DurationScale: MagnitudeDef → Operand.Attribute(CooldownReduction)

AbilityDef "Fireball"
  BlockedTags:    [cooldown.fireball]
  CooldownEffect: "Cooldown.Fireball"
```

쿨다운 20% 감소 반지 = `CooldownReduction` 속성에 `Multiply −0.2`를 거는 Effect 하나.
`DurationScale`(G3)이 이미 `MagnitudeDef`이고 적용 시 1회 평가하며 Operand가 대상(= 시전자)
속성을 읽으므로 **추가 기계가 0개**다.

이 속성은 감소량이 아니라 **배율**이다 — `DurationScale`이 지속시간에 그대로 곱해지므로
게임이 유닛마다 Base를 1로 초기화해야 한다. 속성 Base 기본값은 0이라, 초기화를 빠뜨리면
쿨다운이 최소치 1틱으로 조용히 줄어든다. 합격선 테스트:
`AbilityActivationTests.A_twenty_percent_cooldown_reduction_ring_is_pure_data`.

쿨다운 초기화·단축은 기존 `RemoveByTags(cooldown.*)`로 커버된다.

### 7.4 AbilitySet과 부여 소스

```
AbilitySet (Unit 소유)
├─ Grant(abilityId, GrantSource, level)
├─ Revoke(GrantSource)              해당 소스가 준 것만 회수
├─ IsGranted(abilityId)
├─ GetLevel(abilityId)              부여 소스들의 레벨 중 최대값
└─ 소스별 참조 카운트 — 여러 소스가 같은 어빌리티를 주면 마지막 소스가 사라질 때 회수

GrantSource = Archetype | Effect(instanceId) | Item(itemId) | Code
```

부여 목록은 (abilityId, GrantSource) 오름차순으로 유지한다 — 최대 레벨 계산과 스냅샷
직렬화가 순서 의존이므로 결정론 근거가 필요하다.

프레임워크 설계 §8.3의 부여 소스 4종에 대응한다.

- **Archetype**: 유닛 생성 시 아키타입 선언에서 부여. 유닛 고유 반응(idlez
  `UNIT_ONATTACKED_MYPLAYER` 부류)도 "스폰 시 부여되는 Infinite Effect"로 표현되므로
  별도 어휘가 필요 없다.
- **Effect**: `EffectSpec.GrantedAbilities[]`를 `GrantedTags`와 평행하게 추가한다. 인스턴스
  제거 시 회수하고, `Enabled` 토글에도 함께 붙였다 뗀다(GrantedTags와 동일 규율).
- **Item**: 슬라이스 4에서 연결한다. 본 슬라이스는 `GrantSource.Item(itemId)` 케이스와
  `Revoke` 경로만 뚫어둔다.
- **Code**: 게임이 직접.

부여 시점에 `RequiredTags` 게이트를 걸지 않는다 — 게이트는 발동 시점 검사다(§7.2).
프레임워크 설계 §8.3의 "부여 조건은 grant 시 RequiredTags 게이트"는 발동 게이트와 중복이므로
채택하지 않는다.

### 7.5 AbilityCatalog

`EffectCatalog`와 같은 Build 체인에 붙는다:
`TagCatalog → SeamRegistry → AttributeRegistry → EffectCatalog → AbilityCatalog`.
어빌리티가 참조하는 Effect 이름은 Build에서 밀집 id로 해석되고, 미해석 참조는 예외다.
JSON 저작 스키마와 로더를 함께 출하한다(슬라이스 2 §10.1과 같은 strict 규율).

## 8. TargetSelector 배선

```
SelectorContext (기존 Source / Params / Rng 에 추가)
├─ ReadOnlySpan<TargetId> Candidates          = resolver.TargetIds (오름차순 — 결정론 근거)
├─ bool TryGetTags(TargetId, out TagCountContainer)
├─ bool TryGetAttribute(TargetId, ushort, out BigNum)
├─ TargetId EventSource / EventTarget         OnEvent 경로에서만 유효(그 외 기본값)
└─ TargetId Origin                            선택의 원 대상
```

"적 전체", "체력 최저 아군", "태그 보유자 N명" 등이 셀렉터 구현에서 표현된다. 공간 질의(거리
기반 오라·전염)는 이동·공간 스펙과 함께 온다 — 그때 `SelectorContext`에 공간 인덱스 접근자가
비파괴로 추가된다(idlez는 `GameBoard._unitRTree`가 그 자리다).

셀렉터에 World 참조를 통째로 주지 않는다. 심이 월드 전체를 만질 수 있게 되면 결정론·테스트
경계가 무너진다.

**절단 카운터**(백로그 ③): `FireChain`의 `stackalloc TargetId[32]`를 유지하되,
`Select`가 32를 초과해 반환하면 `SelectorTruncationCount`를 올린다. 기존
`UnresolvedTargetDropCount`·`ImmuneDropCount`·`ConditionDropCount`·`ChanceDropCount`·
`DrImmuneDropCount`와 나란히 노출한다. 어빌리티의 `Effects` 셀렉터 호출도 같은 경로를 쓴다.

## 9. 백로그 ① — 수식 파싱 1회화

현재 `EffectCatalogBuilder.CompileFormula`가 `BigNumFormula.TryEvaluate(formula, level, …)`를
`MaxLevel`회 호출해 매번 문자열을 재파싱한다.

```
BigNumFormula.TryCompile(string formula, out CompiledFormula compiled, out string? error)
CompiledFormula.Evaluate(BigNum x) → BigNum
```

`CompiledFormula`는 토큰/RPN 배열을 담는 불변 구조다. `CompileFormula`가 1회 컴파일 후 레벨
루프를 돈다. 결과값은 기존 경로와 **비트 동일**해야 한다(회귀 테스트로 고정).
`TryValidate`는 `TryCompile`의 얇은 래퍼가 된다.

현실 카탈로그 규모(2000 effect × 3 magnitude × 100 level ≈ 60만 회 ≈ 580ms) 기준으로 서버·클라
기동 모두에서 유의미하다.

## 10. 백로그 ② — 출하 컴파일 아티팩트

`TagCatalogBinary` 패턴을 그대로 복제한다.

```
EffectCatalogBinary / AbilityCatalogBinary
├─ Writer: 빌드 시 컴파일된 카탈로그를 바이너리로 기록 (AtomicFileWriter 재사용)
├─ Reader: 기동 시 로드 — JSON 파싱·수식 평가·검증을 건너뛴다
└─ fingerprint: TagCatalog fingerprint + AttributeRegistry 서명 + 스키마 버전
```

- **JSON 저작이 정본이라는 것은 불변이다.** 바이너리는 파생 산출물이며 빌드 파이프라인이 굽는다.
- fingerprint 불일치는 기동 시 예외(`TagCatalogCompatibilityException` 계열과 동형).
- 손상 파일 검출은 `TagCatalogBinaryCorruptionTests`의 패턴을 따른다.
- 이 아티팩트가 ①의 수식 평가 비용까지 흡수하므로 클라 기동에서는 양쪽 비용이 모두 0이 된다.
  ①은 바이너리를 굽는 빌드 측과 JSON 직접 로드를 쓰는 개발 워크플로에 남는다.

## 11. 틱 파이프라인 (개정판)

```
World.Tick():
  ⓪ 스폰/디스폰 큐 반영 (레지스트리 변경은 여기서만) + 적용 예산 재산정
  ⓪-1 관찰 버퍼 비움 · ⓪-2 명령 큐 처리 (§16.2)
  EffectPipeline.Tick():
    ① 적용   큐 드레인(FIFO, 틱당 예산 = 유닛당 예산 × 등록 유닛 수)
             — Instant 실행 시 RaiseEvents → OnEvent 체인 매칭 → 같은 큐 적재
    ② 시간   ttl 감소, Period 발화(→ RaiseEvents 동일), 만료 제거, 만료 체인 큐 적재
    ③ 재계산 1차                                          ┐ 유닛마다 ③→④→⑤를 한 번에
    ④ 조건   Ongoing 일괄 평가 → enabled 토글             │ (§19 — 유닛 간 독립이라
             (GrantedAbilities도 함께 토글)                │  페이즈별 일괄과 결과 동일)
    ⑤ 재계산 2차                                          ┘
    ⑥ 방출   이벤트 버퍼 확정
    CurrentTick++            (파이프라인 내부 — World는 별도 카운터를 두지 않는다)
```

`OnEvent`가 새 페이즈를 만들지 않는다는 점이 핵심이다 — 페이즈 ①·② 안에서 기존 큐에
합류하므로 예산·이월·순환 유계화가 전부 재사용된다.

**적용 예산은 유닛당이다**(`World` 생성자 `applyBudgetPerUnit`, 기본 32). 예산의 목적은 상호
반사 같은 끝없는 연쇄를 틱당 유한하게 끊는 것인데, 월드 전체 고정값(구 기본 64)은 유닛이
늘면 정상 부하만으로 큐가 무한히 밀렸다. 예산은 유닛별 칸막이가 아니라 월드 풀이며
(`32 × 유닛 수`, 유닛 0개면 1유닛분), ⓪에서 등록 유닛 수가 바뀔 때 다시 산정한다.
기본 32는 1:1(유닛 2개)에서 구 기본값 64와 같아지도록 골랐다. 16도 평균 부하로는 충분하지만
유닛이 적은 월드에서 체인이 많은 어빌리티 한 번이 예산을 넘겨 효과가 다음 틱으로 밀릴 수 있다.
단독 `EffectPipeline`은 생성자의 틱당 예산을 그대로 쓴다.

## 12. 테스트 전략

**결정론 오라클**(슬라이스 2의 3종에 시나리오 추가):
반사 왕복(A반사 ↔ B반사 상호 반사가 예산 안에서 수렴), 부여/회수 왕복, 쿨다운 사이클 후
상태 비트 동일.

**기계별**:
- 게이트 실패 사유 7종 각각 하나씩 + `Success` 경로
- 부여 소스 4종 × (부여 → 소스 소멸 → 회수), 중복 부여 참조 카운트, 최대 레벨 선택
- `Enabled` 토글 시 GrantedAbilities 동반 토글
- `OnEvent`: AsTarget/AsSource 방향, 태그 계층 매칭, `Enabled=false` 미수신,
  `EventMagnitude` 실적용값(클램프 걸린 케이스 포함), 순회 순서 canonical, 순환의 예산 유계
- 코스트 원자성: 같은 틱 다중 발동에서 이중 소모 없음
- 쿨다운 감소: `CooldownReduction` 속성이 `DurationScale`에 반영
- 셀렉터: 후보 스팬 오름차순, 절단 카운터 증가
- 스폰/디스폰이 페이즈 경계에서만 반영, 디스폰 대상 적용은 드랍 카운터
- Build 검증 오류 전 경로(§6.7) — 오류 하나당 테스트 하나

**직렬화 라운드트립**: 같은 스펙 집합을 ① 코드 빌더 ② JSON 로드 ③ 컴파일 아티팩트 세 경로로
Build했을 때 시나리오 해시가 일치.

**수식 컴파일 회귀**: 기존 `TryEvaluate` 경로와 새 `TryCompile → Evaluate`가 비트 동일.

**무할당 스모크**: 정착 상태 틱 반복 + 이벤트 발행 경로에서
`GC.GetAllocatedBytesForCurrentThread` == 0. 발동·스폰 순간은 풀 왕복만 허용.

**틱 경계·관찰 API(§16)**:
- 명령 큐: 틱 밖에서는 아무것도 반영되지 않음, 종류가 달라도 호출 순서 보존, 처리 중 적재된 명령은
  다음 틱, 대기 명령이 있으면 스냅샷 거부
- 경계 입력: 같은 프레임 디스폰 뒤 발동 → `UnknownUnit`, 미선언 코스트 속성 → `CostInsufficient`,
  미지 유닛·미선언 속성 대상 제거·Base 쓰기 → 무시(예외 없음)
- `CanActivateAbility` 무부작용, 같은 틱 두 번째 발동이 코스트·쿨다운에 막힘
- 쿨다운: BlockedTags 없이 `OnCooldown`, 공유 쿨다운 태그 게이트와 `TryGetCooldown`(30/30), 태그 없는
  쿨다운 이펙트 Build 오류, 쿨다운 20% 감소 반지 합격선(100 → 80틱)
- 관찰: `ActiveEffectInfo`·`DurationTicks`(스케일·연장·스냅샷 왕복), `PendingGameplayEvents`,
  수명 이벤트 `Source`·`Level`, `AbilitySet.GetGrant`·`Version`
- 버퍼 수명: 틱 뒤 버퍼 = 정확히 그 틱의 출력(스폰 전 초기화 제외, 명령 처리 이벤트 포함), 단독 파이프라인도
  다음 틱에 비움
- 무할당 스모크: 발동·적용·Base 쓰기·태그 제거 명령 + 관찰 읽기 반복이 정착 후 0바이트

## 13. 명명 컨벤션

Effect 도메인 명명 정리(`EffectSpec`→`EffectDef` 등)와 남은 세 건의 리네임을 거치며 굳은
규칙을 여기 확정한다. 각 계층은 데이터가 **어느 단계에 있는가**로 접미사·접두사가 정해지고,
타입이 속한 도메인 이름은 그대로 유지된다.

| 계층 | 규칙 | 실례 |
|---|---|---|
| authoring 루트 | `<Domain>Def` | `EffectDef`, `AbilityDef` |
| authoring 노드 | `<Role>Def` | `ModifierDef`, `CostDef`, `StackPolicyDef` |
| 파싱 중간 산출물 | `Parsed<Domain>` — 로더가 만들고, 소스 위치를 실어 `Build`로 넘긴 뒤 버린다 | `ParsedRedirect`(`TagCatalog.cs`) |
| 컴파일된 형태 | `Compiled<Domain>` — authoring 접미사를 뗀다 | `CompiledEffect`, `CompiledAttribute`, `CompiledRedirect` |
| 런타임 인스턴스 | `<Domain>Instance` | `EffectInstance` |
| 개체 소유 상태 집합 | `<Domain>Set` | `AttributeSet`, `AbilitySet` |
| 범용 재사용 컨테이너 | `<Domain>Container` | `TagContainer`, `TagCountContainer` |
| 스냅샷 | `<Aggregate>Snapshot` | `UnitSnapshot`, `AbilitySetSnapshot`, `EffectTargetSnapshot` |
| 컨테이너 출처 | authoring에서 컴파일 → `Catalog` / 게임 코드가 등록 → `Registry` | `TagCatalog`(JSON·소스 컴파일) vs `AttributeRegistry`(`Register()` 호출로 채움) |

**예외처럼 보이지만 아닌 두 규칙**:

- **`<Domain>Container`(태그 계열)는 `<Domain>Set`이 아니다** — `Set`은 특정 개체 하나가
  소유한 상태(그 개체가 사라지면 같이 사라진다)인 반면, `TagContainer`/`TagCountContainer`는
  소유 개체가 없는 범용 재사용 컬렉션이다. 태그는 인게임·아웃게임을 가리지 않고 프레임워크
  전역에서 쓰이므로 컨테이너다.
- **`Catalog` 대 `Registry`는 담긴 내용이 아니라 출처로 가른다** — 둘 다 "id → 컴파일된 데이터"
  룩업이라는 점은 같다. authoring 데이터(JSON·소스 문서)를 컴파일해서 채워지면 `Catalog`
  (`TagCatalog`), 게임 코드가 `Register()` 같은 API를 직접 호출해 채우면 `Registry`
  (`AttributeRegistry`)다.

**이번 리팩터가 남긴 미결 질문 둘**:

1. `Bun3.Server.Items`가 이미 `ItemCatalog`·`ItemInstance`를 쓰고 있다. 슬라이스 4에서
   게임플레이 아이템 계층을 붙이면 이름이 충돌한다 — 슬라이스 4가 뭔가 이름 짓기 전에
   먼저 정리해야 한다.
2. `Parsed<Domain>` 계층이 태그 파이프라인 밖에서도 일반화되는 패턴인지, 아니면 태그
   파이프라인(줄·컬럼 진단을 한 번에 모아 보고하기 위한 장치)에 국한된 특이 사례인지
   아직 모른다. `EffectDefJson`·`AbilityDefJson`은 이런 중간 구조 없이 `JToken`을 즉시
   검증해 던지므로, 현재로선 후보가 태그 파이프라인 하나뿐이다.

## 14. 이연 목록

| 항목 | 어디로 |
|---|---|
| 시전 인스턴스(타임라인 액터 — idlez `GameSkill` 상당) | 이동·공간 스펙 이후. World 틱에 페이즈 한 쌍 추가하는 비파괴 확장 |
| 채널링·지속 어빌리티 1급 지원 | 위와 동일 |
| `ChainEdgeDef.SelfAction`(반응이 자기 상태를 변경) | 실수요 시 — §6.8 |
| `Operand.EventNominalMagnitude` | 실수요 시 — 값은 이벤트에 이미 동승 |
| 아이템의 EffectTarget 보유 | 슬라이스 4 — 합성이라 optional로 붙는다 |
| Item 부여 소스 실배선 | 슬라이스 4 (케이스·Revoke 경로는 본 슬라이스에서 뚫림) |
| 공간 질의 셀렉터(거리 기반 오라·전염) | 이동·공간 스펙 |
| proto 직렬화·dirty 추적 | 슬라이스 4 |
| 복제(gameplay.proto)·미러 월드·시야 필터 | 슬라이스 5 |
| `World.PostAsync` 액터 래퍼 | `Bun3.Server.Gameplay`, 슬라이스 5 |
| 피해 파이프라인 1급화(치명타·방어구 순서 고정) | 채택 안 함 — §3.3. 게임이 MagnitudeCalc로 정한다 |
| `IAbilityBehavior` 시섬 | 실수요 시 — §7.1 |
| `Bun3.Server.Items`의 `ItemCatalog`/`ItemInstance`와 게임플레이 아이템 계층 이름 충돌 | §13 참조 — 슬라이스 4 착수 전 결정 |
| `Parsed<Domain>` 계층의 일반화 여부 | §13 참조 |
| 명령에 틱 번호 스탬프(과거·미래 틱 명령 거부 — idlez 입력 방식) | 슬라이스 5 복제 — 클라 예측·서버 검증에 필요 |
| 면역·DR로 무효화된 적용의 관찰 이벤트 | 실수요 시 — §16.6 |

## 15. 버전

기능 추가 + **공개 API 파괴 변경**: `Bun3.Gameplay` 0.13.0 → **0.14.0**, UPM 동반.
저작 어셈블리(`Bun3.Gameplay.Catalog`)를 싣는 `Bun3.Gameplay.Authoring`도 0.2.0 → **0.3.0**.
0.x 구간이므로 파괴 변경도 마이너 범프로 처리한다.

§13 명명 정리로 0.13.0에 공개됐던 이름이 바뀌었다.

| 0.13.0 | 0.14.0 |
|---|---|
| `EffectSpec` · `StackPolicy` · `LevelKey` | `EffectDef` · `StackPolicyDef` · `LevelKeyDef` |
| `AttributeDefinition` | `CompiledAttribute` |
| `EffectInstance.SpecId` · `EffectLifecycleEvent.SpecId` | `EffectId` |
| `EffectSpecJson` | `EffectDefJson` |
| Effect JSON 루트 키 `"specs"` | `"effects"` (`schemaVersion`은 1 유지) |

`[Obsolete]` 별칭과 `"specs"` 키 호환 수용은 두지 않는다. 확인해 보니 소비자가 없다 —
bun3-server-template은 `Bun3.Gameplay`를 참조하지 않고, 저장소 밖에 저작된 Effect JSON도 없다.
같은 이유로 JSON `schemaVersion`도 올리지 않는다.

슬라이스 2 §7.2의 체인 값 전달 규칙 개정은 문서 개정이며 기존 4종 트리거의 동작은 불변이다.
`calc.*`·`selector.*`에 이어 `event.*` 예약 루트가 추가된다(일반 태그이므로 카탈로그 스키마
영향 없음).

퍼블리시와 main 머지는 본 슬라이스 범위 밖이다 — 브랜치에서 완결하고 사용자 확인을 기다린다.

## 16. 틱 경계 규칙과 관찰 API (구현 리뷰 후 개정, 2026-09-29)

구현 리뷰(사용성 축)가 "게임 코드가 결과를 관찰할 수단이 없다"와 "틱 밖에서 상태가 바뀐다"를
지적했다. 조사 근거: [`docs/research/2026-09-29-gameplay-observation-api.md`](../../research/2026-09-29-gameplay-observation-api.md)
(GAS·오픈소스 4종), 그리고 idlez의 틱·입력 구조(입력은 틱 번호를 찍은 액션 큐에 적재하고
틱의 입력 단계에서 소비, 이벤트 버퍼는 틱 시작에 비움, 틱 밖 스탯 직접 쓰기는 보드 편입 전
초기값·스냅샷 역직렬화뿐이고 플레이 중 메타 스탯 교체도 큐 명령으로 처리).
이 절이 §2의 "발동 API: 동기 반환", §7.2의 `TryActivateAbility`, §11의 틱 순서를 대체한다.

### 16.1 규칙 — World 틱 밖에서는 큐 적재와 조회만

틱 밖에서 시뮬레이션 상태를 바꾸는 공개 경로를 없앤다. 게임이 하는 모든 변경 요청은 `World`의
**명령 큐 하나**(FIFO)에 쌓이고 다음 `Tick()` 안에서 호출 순서대로 처리된다. 종류가 달라도
큐가 하나라서 호출 순서가 보존된다(적용 → 발동 순으로 불렀으면 그 순서로 처리).

| 명령 | 처리 |
|---|---|
| `EnqueueApply(effectId, source, target, level)` | 파이프라인 적용 큐로 넘김 → 같은 틱 ①에서 드레인 |
| `EnqueueActivation(unitId, abilityId)` | §7.2 게이트·코스트·쿨다운 → 결과를 `PendingActivations`에 기록 |
| `EnqueueRemoveByTag(unitId, tag)` | `IsAncestorOrSelf(tag, asset)` 매칭 인스턴스 제거(`RemovedPrematurely`) |
| `EnqueueRemoveById(unitId, instanceId)` | 해당 인스턴스 제거 |
| `EnqueueSetBase(unitId, attributeId, value)` | 등록된 유닛의 Base 쓰기(메타 스탯 갱신 등) |
| `EnqueueGrant(unitId, abilityId, source, level)` · `EnqueueRevoke(unitId, source)` | 런타임 어빌리티 부여·회수(퀘스트 보상, 장착 아이템 등). 잘못된 id·레벨은 적재 시점에 거부 |

- **조회는 언제나 허용**된다: `CanActivateAbility`, `TryGetCooldown`, `GetActiveEffect`, 속성 읽기 등.
- **스폰 전 초기화는 예외가 아니다** — 아직 등록되지 않은 유닛의 `AttributeSet.SetBase`·
  `AbilitySet.Grant`·`Tags.Add` 직접 호출은 시뮬레이션 상태를 바꾸지 않는다. 등록된 유닛의 런타임 태그는
  이펙트(`GrantedTags`)로만 붙인다 — 느슨한 태그 명령은 두지 않는다(GAS도 런타임 상태 태그는 GE가 소유).
  풀힐·부활은 Instant 이펙트(`Add` + `MaxHp` 참조)로 `EnqueueApply`한다.
- 틱 안(시섬 등)에서 부른 `Enqueue*`는 다음 틱에 처리된다.
- 결정론: 상태 변경이 전부 `Tick()` 안의 고정 순서에서만 일어나므로, 같은 명령 열이면 같은
  결과다. 남는 조건은 **적재 순서의 결정성**뿐이다 — 여러 스레드가 한 World에 적재하는 호스트는
  적재 지점을 하나로 모아야 한다. 명령에 틱 번호를 찍는 방식(idlez)은 복제 단계(슬라이스 5)에서 다룬다.
- 단독 `EffectPipeline`(World 없이)은 기존 공개 API(`EnqueueApply`, `RemoveByTags`, `RemoveById`)를
  유지한다. World를 쓰는 게임은 `World.Enqueue*`만 쓰고 `World.Pipeline`은 읽기 전용으로 다룬다 — 그 변경
  멤버는 명령 큐와 호출 순서를 우회하고, 틱 사이에 낸 이벤트는 다음 틱 시작에 지워지며, 파이프라인 자체
  `Tick()`은 구조 변경·명령 처리를 건너뛴다(`World.Pipeline` XML 문서에 명시). 전투 이벤트는
  `World.PendingGameplayEvents`로 읽어 파이프라인에 손댈 이유를 없앤다.
- `CreateSnapshot`은 명령 큐가 비어 있어야 한다(구조 변경 큐와 같은 이유).
- **명령 처리 중 예외**: 던진 명령과 그 앞 명령은 큐에서 빠지고 예외는 `Tick()`으로 한 번 전파된다
  (그 틱의 파이프라인 페이즈는 돌지 않는다). 다음 틱은 남은 명령부터 정상 진행한다 — 처리한 명령을
  다시 실행하거나 같은 예외를 반복하지 않는다. 발동 commit은 코스트 차감 → 쿨다운 적용 → 효과 적재
  순이므로, 쿨다운 적용이 던지면(예: 쿨다운 `DurationScale`이 유닛이 선언하지 않은 속성을 참조) 코스트만
  빠진다. 이는 저작 오류이고 예외가 그 사실을 알린다.

### 16.2 World.Tick 순서

```
World.Tick():
  ⓪ 스폰/디스폰 반영 + 적용 예산 재산정 (§11)
  ⓪-1 관찰 버퍼 비움 (등록 유닛의 이펙트·속성 이벤트, GameplayEvent, PendingActivations)
  ⓪-2 명령 큐 처리 (FIFO)
  ①~⑥ EffectPipeline 페이즈 (§11)
```

틱 밖에서는 이벤트가 생기지 않으므로(16.1) ⓪-1에서 비워도 읽히기 전에 사라지는 이벤트가 없다.
게임의 약속은 **"매 `Tick()` 뒤에 한 번 읽는다"** 하나다. 단독 `EffectPipeline.Tick()`은 시작 시
자기 버퍼를 비운다. 기존 `Clear*` 메서드는 남긴다.
발동 타이밍은 이전과 같다 — 틱 사이에 요청한 발동의 효과는 다음 틱 ①에서 적용된다.

### 16.3 발동 결과와 조회

- `EnqueueActivation`은 반환값이 없다. 결과는 `World.PendingActivations`
  (`ReadOnlySpan<AbilityActivation>`: `UnitId`, `AbilityId`, `Result`)로 틱 뒤에 읽는다.
- `CanActivateAbility(unitId, abilityId) → AbilityActivationResult`는 §7.2 게이트 1~8을 부수효과
  없이 평가한다(버튼 활성 상태용). 코스트 차감·쿨다운 적용·효과 적재는 하지 않는다.
- 같은 틱의 두 번째 발동 요청이 쿨다운·코스트에 막히는 것은 이전과 같다(명령 처리 중 즉시 차감·즉시 쿨다운).

### 16.4 쿨다운 게이트 — GAS 방식

GAS는 쿨다운 태그를 차단 태그에 적게 하지 않고, 발동 검사에 별도 쿨다운 단계를 둔다
(`CheckCooldown`, `GetCooldownTags` = "이 어빌리티를 쿨다운에 넣을 수 있는 모든 태그").
같은 방식으로 §7.2 게이트에 단계를 추가한다(빌드 변환 없음, 런타임에 컴파일된 쿨다운 이펙트의
`GrantedTags`를 참조):

```
1 UnknownUnit → 2 UnknownAbility → 3 NotGranted → 4 OnCooldown(신규)
→ 5 MissingTag → 6 BlockedByTag → 7 ConditionFailed → 8 CostInsufficient
```

- `OnCooldown`: 유닛이 쿨다운 이펙트의 `GrantedTags` 중 하나라도 보유. 다른 이펙트가 같은 태그를
  줘도(공유 쿨다운) 막힌다. `BlockedTags`에 같은 태그를 적어도 무해하다(4에서 먼저 걸린다).
- `World.TryGetCooldown(unitId, abilityId, out remainingTicks, out durationTicks)`: 같은 태그를 주는
  활성 인스턴스 중 남은 틱이 최대인 것. 게이트와 같은 태그를 보므로 UI와 실제 동작이 일치한다.
  Infinite 인스턴스가 막고 있으면 `true`와 `-1/-1`(제거될 때까지 유지)을 돌려준다.

### 16.5 관찰 API

| 대상 | API | 비고 |
|---|---|---|
| 활성 이펙트 | `EffectTarget.GetActiveEffect(i)`, `TryGetActiveEffect(instanceId, out)` → `ActiveEffectInfo` | 값 복사(`InstanceId`, `EffectId`, `Source`, `Level`, `Stack`, `RemainingTicks`, `DurationTicks`, `Enabled`). 풀링 인스턴스를 노출하지 않는다. 스냅샷 복원·합류 뒤 버프 바를 다시 세우는 경로 |
| 전체 지속시간 | `EffectInstance.DurationTicks` | 생성·갱신 시점의 스케일·DR 반영값. 스냅샷 행에 포함 |
| 적용/제거 알림 | `EffectLifecycleEvent.Source`, `.Level` 추가 | 기존 드레인 버퍼 |
| 전투 이벤트 | `World.PendingGameplayEvents`(= `EffectPipeline.PendingGameplayEvents`) | 파이프라인 단위 버퍼 하나(발행 순서 = 결정적 순서). 데미지 숫자·전투 로그·네트워크의 단일 원천. 태그 필터는 소비자가 `IsAncestorOrSelf`로 |
| 쿨다운 | `World.TryGetCooldown` | 16.4 |
| 부여 어빌리티 | `AbilitySet.GetGrant(i)` → `AbilityGrantInfo(AbilityId, Source, Level)`, `AbilitySet.Version` | Grant·Revoke·복원마다 Version 증가. 스킬 바는 Version이 바뀐 틱에만 다시 그린다 |
| 어빌리티 이름 | `AbilityCatalog.GetName(id)` | |

속성 변경(`AttributeSet.PendingChanges`)은 현행 유지 — 원인 필드는 넣지 않는다(원인이 필요하면
GameplayEvent). 코어에 cue(VFX) 시스템은 두지 않는다 — 위 버퍼·조회로 GAS cue의 네 이벤트
(OnActive·WhileActive·Executed·Removed)가 전부 재구성되며, 태그→VFX 매핑은 Unity 계층 몫이다.

### 16.6 채택하지 않은 것

- 면역·DR로 무효화된 적용의 관찰 이벤트 — 실수요 시.
- 전투 로그 완전성의 Build 검증 — 저작 규칙으로 둔다: 로그에 남길 피해 이펙트는 `RaiseEvents`를 선언한다.
- 발동 성공/실패 콜백(GAS `AbilityActivatedCallbacks` 등) — `PendingActivations`로 충분.
- 틱 사이 이벤트 보존(버퍼별 워터마크) — 16.1로 틱 밖 이벤트 발생 자체가 없어져 불필요.

## 17. 발동 대상 지정 (2026-10-01)

구현 리뷰 사용성 축: 발동이 `(unit, ability)`뿐이라 "클릭한 적에게 파이어볼"을 데이터로 표현할 수
없었다. GAS는 어빌리티에 `TargetRequiredTags`/`TargetBlockedTags`를 두고 발동에 대상 정보를 싣는다.

### 17.1 결정

- **발동 인자**: `EnqueueActivation(unit, ability, target)`, `CanActivateAbility(unit, ability, target)`.
  결과 `AbilityActivation`에 `Target`이 실린다. `TargetId`에는 예약된 "없음" 값이 없으므로 대상 유무를
  sentinel로 판단하지 않는다 — 어빌리티의 `Target` 모드가 정한다. 대상 인자는 `TargetId?`(기본 null)이고,
  `Required`인데 null이면 `InvalidTarget`이다(최종 리뷰: `= default`였을 때 0번 유닛이 있으면 대상을 빠뜨린
  호출이 조용히 0번 유닛을 겨눴다). 결과의 `Target`도 `TargetId?`로 "대상 없음"과 "0번 대상"을 구분한다.
- **`AbilityDef.Target`**: `None`(기본, 대상 인자 무시) | `Required`(등록 유닛으로 해석돼야 함).
  빌드가 추론하지 않고 저작자가 명시한다. `None`인데 대상 기준점 항목이나 대상 검사가 있으면 Build 오류.
- **효과 항목 기준점** `AbilityEffectDef.Origin`: `Caster`(기본) | `Target`. 셀렉터가 없으면 기준점 유닛에
  적용하고, 있으면 셀렉터가 기준점에서 출발한다(`SelectorContext.Origin` = 기준점, `Source` = 시전자).
  같은 광역 셀렉터를 시전자 주변·대상 주변에 재사용한다.
- **대상 검사**(사용자 결정: 태그 + 속성 조건): `TargetRequiredTags`, `TargetBlockedTags`,
  `TargetConditions`. 대상 조건에서 `Operand.Attribute`는 대상, `Operand.SourceAttribute`는 시전자를 읽는다
  (처형: 대상 HP < 30%). 죽음·면역 같은 상태는 태그로 표현한다.

### 17.2 게이트 순서

```
1 UnknownUnit → 2 UnknownAbility → 3 NotGranted → 4 OnCooldown → 5 MissingTag → 6 BlockedByTag
→ 7 ConditionFailed → 8 InvalidTarget → 9 TargetMissingTag → 10 TargetBlockedByTag
→ 11 TargetConditionFailed → 12 CostInsufficient
```

대상 검사(8~11)는 `Target = Required`일 때만 돈다. 코스트가 마지막인 것은 그대로다.

### 17.3 저작 JSON

```jsonc
{ "name": "Fireball", "target": "Required",
  "targetBlockedTags": ["state.dead"],
  "targetConditions": [ { "left": { "attribute": "Hp" }, "op": "Less", "right": { "constant": "30" } } ],
  "effects": [
    { "effect": "FireBolt", "origin": "Target" },
    { "effect": "Splash",   "origin": "Target", "selector": "selector.near" },
    { "effect": "CastBuff" }
  ] }
```

컴파일 아티팩트(schema 1)도 위 필드를 싣는다 — 아직 퍼블리시 전이라 schema 번호는 올리지 않는다.

### 17.4 비채택

- 대상 유무를 효과 항목에서 추론하는 것 — 빌드 시점 암묵 동작을 늘리지 않는다(§16.4와 같은 원칙).
- 거리·시야 같은 공간 검사 — 이동·공간 스펙 이후.
- 셀렉터로 고른 대상이 0명일 때 발동 실패 — 광역기는 빗나가도 발동은 성공(코스트 소모)이 관례다.

## 18. 부트스트랩 묶음과 유닛 아키타입 (2026-10-01)

구현 리뷰 사용성 축: 태그 → 속성 → 이름표 → 시섬 → 이펙트 → 어빌리티 → 월드 → 유닛까지 약 11단계에
tags·seams·attributes를 네 번 넘겨야 했고, 예제 시나리오도 이 묶음을 따로 만들어 썼다. 두 번째 게임도
똑같이 짤 코드이므로 프레임워크 몫이다(CLAUDE.md). 참고: Lyra는 PawnData·AbilitySet 데이터 자산으로
"이런 유닛은 이런 어빌리티·속성을 갖는다"를 묶는다.

### 18.1 속성 이름 (사용자 결정: 레지스트리에 이름 포함)

- `AttributeRegistryBuilder.Register(attributeId, name, min, max, ...)` — 이름은 필수, 비어 있거나
  중복이면 Build 오류. `AttributeRegistry.GetName(id)`·`TryGetId(name, out id)`.
- JSON 로더(`EffectDefJson`·`AbilityDefJson`)는 별도 이름표 딕셔너리 대신 `AttributeRegistry`를 받는다.
- 이름은 `Signature`에 넣지 않는다 — 컴파일 아티팩트는 id만 쓰므로 이름 변경이 아티팩트를 무효화하지 않는다.
- 에러 메시지가 속성을 이름으로 보여줄 수 있게 된다(`'Hp' (1)` 표기).

### 18.2 유닛 아키타입 (사용자 결정: JSON 저작까지)

```
ArchetypeDef: Name, Attributes [(AttributeId, Base)], Abilities [(AbilityName, Level)]  → ArchetypeCatalogBuilder → ArchetypeCatalog
```

- 선언 속성 = `Attributes`의 속성들. 기본 Base는 **레지스트리 `EvaluationOrder` 순서로** 넣는다 —
  저작 순서와 무관하게 클램프 경계(MaxHp)가 값(Hp)보다 먼저 들어간다.
- 시작 어빌리티는 `GrantSource.Archetype()`으로 부여한다(§7.4의 Archetype 소스 배선).
- Build 검증: 이름 중복·빈 이름, 미등록 속성, 한 아키타입 안의 속성 중복, 미지 어빌리티, 레벨 1 미만·
  어빌리티 MaxLevel 초과.
- 쿨다운 배율 속성(§7.3)의 Base 1 초기화를 아키타입 기본값으로 저작할 수 있다.
- 시작 이펙트(패시브)는 두지 않는다 — 시작 어빌리티나 스폰 직후 `EnqueueApply`로 충분하다.
- JSON: `{ "schemaVersion": 1, "archetypes": [ { "name": "Warrior", "attributes": { "MaxHp": "100",
  "Hp": "100" }, "abilities": [ { "ability": "Slash", "level": 1 } ] } ] }` (속성은 이름, 값은 BigNum 문자열).

### 18.3 GameplayDatabase

```csharp
var db = GameplayDatabase.Build(tags, seams, attributes, effectDefs, abilityDefs, archetypeDefs);
// 또는 GameplayCatalogBinary.Load(stream, tags, seams, attributes) → GameplayDatabase
var world = new World(db, rng);
var unit = world.CreateUnit(new TargetId(1), db.Archetypes.GetRequiredId("Warrior"));
world.Spawn(unit);
```

- `GameplayDatabase`: `Tags`·`Seams`·`Attributes`·`Effects`·`Abilities`·`Archetypes`를 묶는 불변 객체.
  `Build`(정의에서 컴파일)와 이미 컴파일된 조각을 받는 생성자 두 경로.
- `World(GameplayDatabase, IRng, applyBudgetPerUnit)` 하나로 생성자를 통일한다.
- `World.CreateUnit(id, archetypeId, ownerId)`: 아키타입대로 유닛을 만들고 기본값·시작 어빌리티를 넣어
  돌려준다(아직 미등록 — 스폰 전 직접 초기화 규칙 §16.1 그대로). `Spawn`은 따로 부른다.
- 컴파일 아티팩트는 아키타입 섹션을 싣고 `GameplayDatabase`를 직접 읽고 쓴다(schema 1, 미퍼블리시).
- 저작 쪽 `GameplayDatabaseJson.Load(tags, seams, attributes, effectsJson, abilitiesJson, archetypesJson)`.

## 19. 틱 페이즈 순회 최적화 (2026-10-03)

### 19.1 측정 근거

전투형 부하 벤치(`WorldStressBenchmarkTests`: 유닛당 패시브 5·어빌리티 6, 활성 효과 평균 12)를
페이즈별로 계측한 결과(normal, 유닛 20,000, i7-13700K):
② 시간 9.7ms, ④ 조건 11.2ms, ① 적용 5.1ms, ⓪-2 명령 2.6ms, ③⑤ 재계산 1.5ms, ⓪-1 비움 0.3ms.
②④가 유닛마다 활성 인스턴스 전부(각각 별도 힙 객체)를 훑는 비용이 70%다 — ④는 Ongoing 조건을 가진
인스턴스가 유닛당 하나뿐인데도 12개를 모두 읽는다. 힙이 유닛당 약 6KB라 5,000명 근처에서 L3(30MB)를
넘고, 그 뒤로는 유닛당 비용이 2.5~3배가 된다(캐시 한계). 따라서 순회 횟수보다 "순회마다 건드리는
객체 수"를 줄이는 쪽이 효과가 크다.

### 19.2 결정

- **페이즈별 부분 목록**: `EffectTarget`이 활성 목록과 함께 두 부분 목록을 Id 오름차순으로 유지한다.
  - 시간 진행 대상: `DurationType == Duration` 또는 `PeriodTicks > 0`인 인스턴스 → ②가 이것만 훑는다.
  - Ongoing 대상: `OngoingConditions`가 있는 인스턴스 → ④가 이것만 훑는다.
  - 삽입·제거·스냅샷 복원은 활성 목록과 같은 지점에서 함께 갱신한다. 빠진 인스턴스는 원래 ②④에서
    아무 일도 하지 않았으므로(무한·비주기는 ②에서 무동작, 조건 없음은 ④에서 건너뜀) 순서·결과가 같다.
  - 할 일이 없는 유닛은 부분 목록이 비어 바로 넘어간다("할 일 없는 유닛 건너뛰기").
- **③④⑤를 유닛 단위 한 순회로**: 유닛마다 재계산 → Ongoing 평가 → 재계산을 연달아 하고 다음 유닛으로
  간다. 등가 근거 — 세 페이즈는 해당 유닛 상태만 읽고 쓴다.
  - 재계산은 적용 시점에 고정된 모디파이어 크기와 같은 유닛 인스턴스의 Enabled·Stack만 읽는다.
    클램프 경계는 자기 속성만 참조한다(SourceAttribute·EventMagnitude는 Build가 거부).
  - Ongoing 조건은 source 없이 대상 자신만 읽는다. 토글이 건드리는 태그·어빌리티 부여 훅·dirty도
    자기 유닛이다. 다른 유닛으로 가는 효과는 없다(큐 적재도 없다).
  - 따라서 유닛 X의 ③④⑤ 결과는 다른 유닛의 ③④⑤ 진행 여부와 무관하고, 페이즈별 일괄과 같다.
    관찰 버퍼 안의 순서(유닛별 속성 변경: ③ 다음 ⑤)도 그대로다.

### 19.3 기각한 대안

- **②까지 합치기(유닛당 ②→⑤ 한 번)**: ②의 주기 발화·체인 셀렉터는 다른 유닛의 Current를 읽는다
  (주기 모디파이어의 SourceAttribute 크기, 실행 계산의 `SourceAttr`, 셀렉터 후보 속성). 합치면 id가
  앞선 유닛은 이미 재계산된 값을, 뒤 유닛은 재계산 전 값을 보이게 되어 결과가 id 순서에 의존한다.
  현재 규칙(② 동안 모든 유닛의 Current는 ③ 이전 상태)을 유지한다.
- **⓪-1 관찰 버퍼 비우기 순회 제거(지연 비움)**: 틱 시작 비움 계약(§16.3)을 쓰기 시점 판정으로
  바꾸려면 `AttributeSet`이 월드 틱을 알아야 한다. 비용이 전체의 1% 미만이라 유지한다.

### 19.4 검증

- 결정론 골든 해시(`ScenarioGoldenHash`, `WorldScenarioGoldenHash`)와 스냅샷 복원 비트 동일 테스트가
  그대로 통과해야 한다 — 시나리오가 주기·Ongoing·무작위 `RemoveById`를 모두 거친다.
- 부하 벤치 전후 수치를 커밋에 남긴다.

### 19.5 후속 후보

- 만료 틱 기반 지속시간: 남은 틱을 매 틱 깎는 대신 만료 틱과 비교해 ②의 비주기 Duration 방문을 없앤다
  (`RemainingTicks`는 읽을 때 계산, 갱신·연장·스택 만료 경로 재설계 필요).
- 인스턴스 핫 데이터(남은 틱·주기 카운트다운·Enabled)를 유닛별 구조체 배열로 — 객체 추적 제거.
- BigNum 빠른 경로(같은 지수 덧셈 등): 덧셈+비교가 `long`의 약 80배.

## 20. 유닛 메모리 축소 (2026-10-05)

### 20.1 측정 근거

`WorldStressBenchmarkTests.Unit_memory_breakdown_by_path_and_type`(유닛 객체 그래프를 따라가며 공유 객체를
뺀 바이트 집계): 생성 직후 4,088B·객체 31개, 600틱 전투 후 4,988B·39개. 작은 월드(50유닛) 여럿을 번갈아
틱하면 전체 데이터가 L3를 넘는 순간 유닛당 비용이 약 300ns → 900ns(틱 사이 캐시 이탈)로 오른다
(`Many_small_worlds_report_tick_cost`). 월드마다 틱을 한 번에 진행해도 이 비용은 남으며, 크기는
"한 틱에 다시 읽어 오는 캐시 라인 수"를 정한다.

| 구분 | 항목 | 크기를 정하는 것 |
|---|---|---|
| 고정(약 0.7KB) | 유닛·타깃·속성 집합·어빌리티 집합·태그 컨테이너 객체, 목록 머리 | 기능 추가 |
| 콘텐츠 비례 | 속성 슬롯(속성당 약 100B) | 아키타입이 선언한 속성 수, 속성 id 체계(조회표 = 최대 id + 1) |
| 콘텐츠 비례 | 효과 인스턴스(효과당 약 90B) | 동시 활성 효과 수(장비·패시브·버프·쿨다운) |
| 콘텐츠 비례 | 수정자(32B + 수정 슬롯마다 List·배열) | 효과당 수정자 수 |
| 콘텐츠 비례 | 부여(어빌리티당 32B) | 보유 어빌리티 수 |
| 강도 비례 | 유닛별 관찰 버퍼(변경·생애 이벤트당 40B) | 한 틱 최대치, 줄지 않음 |
| 공유(유닛당 0) | 카탈로그·레지스트리·컴파일 공식 | — |

### 20.2 결정 ① 관찰 버퍼를 파이프라인 단위로 (사용자 결정: 유닛별 접근자 제거)

- `EffectPipeline.PendingAttributeChanges`·`PendingEffectEvents`를 두고 `World`가 그대로 노출한다
  (`PendingGameplayEvents`와 같은 모양). `AttributeChange`·`EffectLifecycleEvent`에 `Target`을 싣는다.
- 제거: `AttributeSet.PendingChanges/ClearChanges`, `EffectTarget.PendingEffectEvents/ClearEffectEvents`.
  유닛별로 걸러 보는 접근자는 두지 않는다 — 호출마다 월드 버퍼를 훑게 되고, 게임은 틱 뒤 한 번 훑으면 된다.
- 기록 규칙: `AttributeSet`은 연결된 파이프라인의 변경 로그에 기록한다. `World`는 스폰 반영 시 연결하고
  디스폰 시 끊는다. 단독 `EffectPipeline`은 자신이 값을 바꾸는 지점(`ExecuteOnBase`, ③④⑤ 정착)에서 대상을
  연결한다 — 파이프라인이 Current를 바꾸는 경로는 이 둘뿐이다. 어느 파이프라인에도 연결되지 않은 집합
  (스폰 전 초기화, 단독 `AttributeSet`)의 변경은 기록하지 않는다. 기존에도 스폰 전 초기화는 다음 틱 시작에
  비워져 관찰되지 않았다.
- 순서는 틱 안의 발생 순서(결정론). ⓪-1 비움은 카운트 초기화만 남아 유닛 순회가 사라진다.

### 20.3 결정 ② 슬롯·수정자 저장 재구성과 레이아웃 공유

- 슬롯은 `AttributeId`·`Base`·`Current`·`Dirty`·합산 기록 번호만 갖는다(96B → 40B).
- 합산 캐시(ΣAdd·ΣMulPct·Override)는 수정자가 붙은 적 있는 슬롯에만 별도 기록으로 둔다. 번호는 처음 붙을 때
  배정하고 유지한다. 합산 기록이 없는 슬롯의 Current는 `clamp(Base)` — 기존 식 `(Base + 0) × (1 + 0)`과
  BigNum 정규형이 같다.
- 수정자는 집합당 배열 하나에 (슬롯, 출처 Id, 행 번호) 순으로 둔다. 슬롯별 합산 순서가 기존 정렬 목록과
  같으므로(BigNum 덧셈은 결합 법칙이 성립하지 않는다) 결과가 비트 단위로 같다.
- 레이아웃(정렬된 id, id→슬롯 조회표, 평가 순서의 선언 슬롯 목록, 클램프 검증)은 선언 하나당 한 번 만든다.
  아키타입은 컴파일 때 만들어 그 아키타입의 모든 유닛이 공유한다. 재집계는 레지스트리 전체 평가 순서 대신
  선언 슬롯 목록만 훑는다.

### 20.4 결정 ③ 아키타입 시작 어빌리티 공유

`AbilitySet`이 아키타입의 고정 부여 배열과 `Version`을 공유하다가 첫 변경(부여·회수·복원)에서 자기 배열로
복사한다(copy-on-write). 관찰 결과(순서·레벨·Version)는 하나씩 부여한 것과 같다.

### 20.5 검증

- 결정론 골든 해시와 복원 비트 동일 테스트 불변(② ③은 동작 불변, ①은 관찰 API만 바뀜).
- 메모리 분해·작은 월드 다수·부하 벤치로 전후를 잰다. 예상: 벤치 유닛 약 5.0KB → 2.9KB, 객체 39 → 약 27.

