# 게임플레이 프레임워크 (Bun3.Gameplay) 설계

- 날짜: 2026-08-10
- 상태: 승인 대기
- 영감: 언리얼 GameplayAbilitySystem(GAS)의 실전 교훈 + idlez Commons(Game/Types)의 검증된 패턴
- 첫 소비자: 어몽어스류 소셜 디덕션 게임 (Steam, 클라 호스트 권위, 실시간)
- 두 번째 소비자(예정): 방치형 게임 (idlez 계열 — 초대형 수치, 아이템/옵션 경제)

## 1. 목적

Item·Buff(Effect)·Skill(Ability)·Unit과 그 수치·태그·복제를 **프레임워크 단에서 범용으로**
제공하고, 게임에는 도메인 동작(어빌리티 구현, 스탯 정의, 밸런스 데이터)만 남긴다.
"두 번째 게임에서도 똑같이 짜게 될 코드는 프레임워크 몫"(레포 제1 원칙)의 게임플레이 판.

idlez에서 승격하는 것: 태그를 데이터에 붙여 정의를 서술하는 사용 방식, dataId→테이블 참조,
TTL/스택 수명 규칙, 트리거(이벤트 훅) 개념.
idlez에서 버리는 것: RPG 도메인 상수(Team/ArmorType/저항/보스군)의 공용층 침투,
proto 생성 클래스=심 상태 결합, 스탯 하드코딩(UnitStat 프로퍼티 ~20개 + 특수 딕셔너리 6종).

## 2. 결정 요약

| 축 | 결정 |
|---|---|
| 접근 | 하이브리드(GAS 실전형): Effect는 선언 데이터 + 선택 훅, Ability는 코드 |
| 심 위치 | 공용 netstandard2.1 라이브러리 — 호스트(권위)와 원격 클라(미러)가 같은 코드 |
| 수치 | BigNum(long 가수+int 지수, 결정론적) — Attribute/경제 전반. FixedFloat은 이동 스펙에서 |
| 태그 | 언리얼식 계층 문자열, 런타임 인터닝 핸들, 카운트 보유, 미등록은 동적 등록 |
| 복제 | 프레임워크 내장 — `gameplay.proto` + 게임 Update oneof에 필드 1개, 시야 필터 훅 |
| 이동/위치 동기화 | 범위 밖 — 별도 스펙(비신뢰 채널 + 보간/예측, Transport.Steam 포함) |
| 락스텝/결정론 | 코어는 "결정론 준비"만(틱 전진·시드 RNG·스냅샷). 락스텝 동기화 모듈은 후속 스펙 |
| DB 경계 | 프레임워크는 proto 직렬화 모델 + dirty 추적까지. 저장소 매핑은 게임(기존 Player 훅) |
| Effect 시맨틱 표현 | 프레임워크가 해석·분기하는 것(수명/스택)은 enum·프로퍼티, 해석하지 않는 서술(분류/면역/해제)은 태그 |
| BigNum 오버플로 | 지수 오버플로·0 나눗셈은 예외(fail-fast). 가수 정규화는 정상 동작 |
| 문자열 | 런타임 문자열 할당 최소화 — 코어는 TryFormat(Span) 패턴, ZString은 Unity 어댑터 계층 |

## 3. 범위 / 비목표

**범위**: BigNum, GameplayTag/TagSet, Attribute/AttributeSet, Effect, Ability, Item/Inventory,
Unit, World, 복제(gameplay.proto + 서버 글루), 코드 룰(CLAUDE.md).

**비목표(후속 스펙)**:
- 이동/위치 동기화(비신뢰 채널, 보간/예측, Transport.Steam) — 디덕션 게임 전 필수, 바로 뒤이어
- 락스텝 동기화 모듈(입력 복제·desync 해시) — 은닉 정보 게임엔 부적합, RTS/리플레이형 수요 시
- 태그 계층의 데이터 정의 테이블(부모 관계 데이터화) — 코드/데이터 등록으로 충분해질 때까지
- 저장소 계약 패키지(IItemStore류) — 두 번째 게임이 같은 매핑을 반복하면 그때
- 채널링/지속 어빌리티 1급 지원 — v1은 즉발 발동 + Effect 조합으로 구현

## 4. 패키지 구조

```
Bun3.Gameplay            (netstandard2.1, C#9 — Unity 로드 가능)
  BigNum, GameplayTag/TagRegistry/TagSet, Attribute/AttributeSet,
  EffectSpec/EffectInstance, AbilityDef/AbilitySet, ItemDef/ItemInstance/Inventory,
  Unit, World, IRng, gameplay.proto (복제 메시지)
  의존: Google.Protobuf, Bun3.Common(풀드 컬렉션)

Bun3.Server.Gameplay     (netstandard2.1)
  AddGameplayWorld DI 확장: World 생성 + TickLoop 잡 등록 + 커맨드 큐 +
  Rpc Update 브로드캐스트 배선 + PlayerSession↔뷰어 매핑 + 시야 필터 훅
  의존: Bun3.Gameplay, Bun3.Server.Players
```

`Bun3.Gameplay`는 순수 심 — 소켓·세션·DB를 모른다. 호스트는 입력으로 심을 전진시키고,
원격 클라는 수신한 복제 이벤트를 `MirrorWorld.Apply`로 적용해 같은 구조를 읽기 전용 유지.

## 5. 실행 모델

- **World는 단일 스레드 액터.** 세션 핸들러가 World를 직접 만지지 않는다 —
  `world.PostAsync(cmd)`로 월드 틱 컨텍스트에 커맨드를 넘기고 결과를 await(응답 필요 시).
  틱과 커맨드가 한 흐름에서 순차 실행되므로 심 내부는 락 제로.
- **틱**: TickLoop 잡으로 등록. `World.Tick(delta)`만이 시간을 전진시킨다(Effect ttl/period,
  커맨드 드레인, 이벤트 방출). 심 안에서 DateTime/실시간 접근 금지 — 틱 카운터만.
- **결정론 준비**: 난수는 시드 주입 `IRng`(idlez IRng 승격), 상태는 스냅샷 직렬화 가능
  (복제 proto 재활용). 게임 훅의 결정론 규율까지 강제하지는 않는다 — 락스텝 모듈이
  생기면 틱별 상태 해시로 desync 감지를 제공한다.
- 규모 전제: 월드 1개/호스트(클라 호스트 = 프로세스당 매치 1개). 다중 월드는 전용 서버
  시나리오에서 World 인스턴스 N개로 자연 확장(전역 상태 없음).

## 6. 수치 체계 — BigNum

`readonly struct BigNum { long Mantissa; int Exponent; }` — 정규화 규칙 고정(가수 자릿수 범위
불변식), 정수 연산만 사용해 플랫폼 무관 결정론.

- 범위: 사실상 무한(1e300+). 유효 18~19자리 — long(922경=9.2e18)까지 정수 정확,
  그 너머는 근사(방치형 표준 타협). double 빅넘버(유효 15~16자리)보다 정확.
- 연산: 사칙/비교/음수. **지수 오버플로와 0 나눗셈은 예외**(밸런스 공식 폭주 버그를
  숨기지 않는다 — fail-fast). 권위·미러가 같은 연산에서 같은 예외 → 결정론 유지.
- proto: `sint64 mantissa + int32 exponent`.
- **표시 포맷 내장**: idlez ToUnitString의 일반화 — 몇 자리마다(GroupDigits)·무엇으로
  (Units: 만/억/조/경… 또는 K/M/B/T)·최대 몇 번(MaxUnits) 유닛화할지, 소수 자릿수
  (MaxFractionDigits)와 고정 소수 여부(TrimFractionZeros), 정수부 3자리 구분자,
  상한 초과 표기(Scientific 지수 폴백 | TopUnit 상한 단위 유지+정수부 성장)를 전부
  설정으로 제어. `TryFormat(Span<char>, out int written, ...)` 무할당 패턴 —
  Unity에선 TMP `SetText(char[])`/ZString과 그대로 합성.

FixedFloat(Q47.16, idlez)은 공간/기하 전용으로 이동 동기화 스펙에서 Bun3.Common 승격을
다룬다. 범위 ±1.4e14·곱셈 중간 오버플로 특성상 경제 수치에는 부적합(스펙 논의 확정).

## 7. GameplayTag

- **정체성 = 점 구분 계층 문자열**: `State.Dead.Ghost`, `Ability.Movement.Vent`,
  `Effect.Debuff.Stun`, `Immune.Stun`, `Dispel.Magic`, `Item.Consumable` …
  idlez Tags.proto의 enum들은 이식 시 계층 이름으로 옮겨 앉는다(사용 방식 계승, 표현 교체).
- **런타임 인터닝**: 기동 시 등록(코드 상수 + 데이터 파일 양쪽) → `GameplayTag`는
  `readonly struct { int Handle; }`. 비교는 정수, 부모 체인은 등록 시 연결. 심 핫패스에
  문자열 비교/할당 없음. **미등록 태그는 동적 등록**(수신·로드 시 자동 추가).
- **계층 매칭**: `Has("State.Dead")`는 `State.Dead.Ghost` 보유 시에도 true(언리얼 기본
  시맨틱). Required/Blocked/Granted/면역/해제 쿼리 전부 계층 매칭.
- **TagSet = 카운트 맵**: 같은 태그를 부여하는 소스 2개가 공존(하나 꺼져도 유지) +
  `Count(tag)` 조회 제공(계층 매칭 시 하위 카운트 합산). 중첩 허용.
- **직렬화는 문자열이 정본**: 데이터 정의·DB·와이어 모두 태그 이름. 버전 간 안정
  (정수 인덱스는 재정렬에 깨짐), 태그 이벤트는 저빈도라 와이어 비용 무시 가능.

## 8. 게임플레이 개체 합성

```
GameplayObject (베이스): TagSet 보유
  ├─ Unit:          + AttributeSet + ActiveEffects + AbilitySet + (선택) Inventory + OwnerPlayerId
  ├─ ItemInstance:  + RolledModifiers + AbilityGrants  (아이템 태그 — idlez 50000대의 자리)
  ├─ AbilityDef:    태그로 분류 (예: Rooted가 "Ability.Movement.*" 일괄 차단)
  └─ EffectSpec/Instance: 태그로 분류 (면역/해제 쿼리 대상)
```

- TagSet은 전원 보유. AttributeSet·능동 Effect 수용은 Unit만(살아있는 스탯이 필요한
  특수 아이템이 나오면 그때 확장). AbilitySet은 Unit이 보유하되 아이템·Effect는 "부여자".
- Skill = AbilityDef(데이터+코드 훅), Item = 부여자·수정자 운반체, Unit = 실행 주체.

### 8.1 Attribute / AttributeSet

- AttributeId는 게임 proto enum. 유닛 아키타입별 사용 Attribute 선언 등록.
- 값 = Base + 수정자 집계 → Current. 연산 3종: `Add`, `Multiply`(합산식 ×(1+Σ%)),
  `Override`(최우선). 수정자 변경 시 즉시 재계산(읽기 지배적), 변경 이벤트 → 복제 큐.
- 클램프: 정의에 min/max, max는 다른 Attribute 참조 가능(`Hp ≤ MaxHp`).

### 8.2 Effect

- `EffectSpec`(선언 데이터): `DurationType(Instant/Duration/Infinite)`, `Period`,
  스택 규칙(`Refresh` / `StackCount`+max), `Modifiers[]`(attributeId, op,
  `ScalableValue` = base + perLevel 선형 + 커스텀 델리게이트 훅), `GrantedTags`,
  `GrantedAbilities`, 선택 코드 훅(OnApplied/OnPeriodic/OnRemoved).
- **수명/스택은 enum·프로퍼티**(프레임워크가 분기하는 기계 — 잘못된 상태 표현 불가),
  **분류/면역/해제는 태그**(프레임워크는 매칭만, 의미는 게임 몫). idlez `Tag.NoExpiration`류의
  동작-태그 혼합은 채택하지 않는다.
- 스택 집계는 **대상 기준 병합**(같은 스펙이면 소스가 달라도 한 인스턴스에 스택 —
  GAS의 AggregateByTarget). 소스별 독립 스택이 필요한 콘텐츠는 스펙을 분리해서 표현한다.
- Instant = Base 영구 변경(즉발 데미지/회복). `EffectInstance`: 스펙 참조, 남은 ttl(틱),
  stack, level, source. 적용 차단: 대상의 `Immune.*` 태그와 스펙 태그의 계층 매칭.
- "이속 +20% 8초"는 코드 0줄(데이터만).

### 8.3 Ability

- `AbilityDef`: 발동 게이트는 데이터 — RequiredTags/BlockedTags, Cost(attribute+값),
  Cooldown. **쿨다운·코스트는 내부적으로 Effect로 표현**(쿨다운 감소 아이템이 공짜).
- `Activate(ctx)`만 게임 코드. v1은 즉발 발동. 게이트 실패는 예외가 아니라 사유 enum
  (`BlockedByTag/OnCooldown/CostInsufficient/...`) 반환.
- **부여(grant) 소스 모델**: ① 아키타입 기본셋 ② 아이템 획득/장착 ③ Effect 지속 중
  ④ 게임 코드 직접. 소스 소멸 시 부여 자동 회수(장착 해제 = 어빌리티 소실).
  부여 조건은 grant 시 RequiredTags 게이트.

### 8.4 Item / Inventory

- `ItemDef`(데이터): Tags, MaxStack, 장착 시 부여 EffectSpec들, 부여 AbilityDef들.
- `ItemInstance`: DefId, **Count=BigNum**(방치형 스케일), InstanceId, 인스턴스별 롤
  옵션 = `RolledModifiers[]`(idlez ItemOption의 일반화).
- Inventory: Add/Remove/Move/Equip 최소 + 정책 훅(CanAdd 등). 장착 = 유닛에 Effect
  부여(source=item) — 스탯 연결 자동.
- **DB 대응**: ItemInstance/Inventory는 proto 직렬화 정본 + dirty 추적. 저장은 기존
  Player 훅(OnSaveAsync)으로 — 블롭/로우 매핑은 게임 선택. 이것이 DB 경계의 답.

### 8.5 Unit / World

- Unit: §8 합성 참조. 생성은 게임 팩토리(아키타입 데이터 참조).
- World: Unit 레지스트리 + `Tick(delta)` + 프레임워크 이벤트 방출(스폰/디스폰,
  Attribute/태그 변경, Effect 적용·제거, 발동, 인벤토리 변경) + IRng/틱 카운터.
  틱 경로는 Bun3.Common 풀드 컬렉션으로 무할당 규율.

## 9. 복제

- `gameplay.proto`의 `GameplayUpdate` oneof: `WorldSnapshot` / `UnitSpawned·Despawned` /
  `AttributeChanged`(unitId, attributeId, current) / `TagChanged` / `EffectApplied·Removed` /
  `AbilityActivated` / `InventoryChanged`.
- **게임 통합은 필드 1개**: 게임 소유 Update oneof에
  `bun3.gameplay.GameplayUpdate gameplay = N;` 선언. 서버 글루가 자동으로 실어 보내고,
  클라는 `client.OnUpdate<GameplayUpdate>(mirror.Apply)`.
  (메모: oneof 통합 방식의 대안 — 별도 채널 바이트, 제네릭 엔벨로프 등 — 은 추후 연구
  여지로 남긴다. 현 방식이 순서 보장·기존 구독 API 재사용에서 우위라 v1 채택.)
- **시야 필터 훅**: `bool ShouldReplicate(viewerId, in GameplayEvent)` — 디덕션 게임의
  역할 은닉·시야 차단이 이 델리게이트 하나로. 기본은 전체 공개. 후참여/재접속은 뷰어
  필터 적용된 `WorldSnapshot`.
- base/수정자 내역은 권위 전용(와이어에는 current만) — 은닉 정보 최소화 원칙.
- UDP 도입은 이번 범위 아님: 상태 이벤트는 저빈도·순서 중요·신뢰 필수라 TCP(NoDelay)가
  적합. 고빈도 위치 스트림은 이동 스펙에서 비신뢰 채널로.

## 10. 에러 처리

- 게임 훅(Effect/Ability) 예외 격리: 로그 + 해당 인스턴스 제거/발동 취소, 월드 계속
  (TickLoop 잡 격리와 동일 철학).
- Ability 게이트 실패 = 정상 결과(사유 enum). BigNum 지수 오버플로·0 나눗셈 = 예외.
- 복제는 기존 신뢰 채널 위 — 세션 큐 상한/킥 경로가 이미 담당, 별도 기계 없음.

## 11. 코드 룰 (CLAUDE.md 명시)

- **런타임 문자열 할당 최소화.** 프레임워크 코어는 문자열을 만들지 않는다:
  포맷은 `TryFormat(Span<char>)` 패턴, 태그는 등록 시 1회 인터닝, 로그는 저빈도 경로만.
  Unity 계층은 ZString + TMP `SetText`를 적극 사용 — 최종 목표는 Unity에서 `.text`
  문자열 할당 제로.
- 틱/패킷 핫패스 무할당 규율은 서버 리뷰 웨이브에서 확립한 기준을 그대로 적용.

## 12. 테스트 전략

- 심이 순수하므로 전부 결정론 유닛 테스트: 수동 Tick으로 Effect 수명/스택/집계/게이트/
  부여 회수. BigNum 연산·정규화·포맷 라운드트립 + 비트 동일성. 태그 등록/계층 매칭/카운트.
- 복제: InMemoryDuplex로 권위→미러 왕복, 시야 필터 검증, 스냅샷 후참여.
- 틱 경로 무할당 스모크(GC.GetAllocatedBytesForCurrentThread).

## 13. 구현 슬라이스 (플랜 분할 순서)

1. BigNum + GameplayTag/TagRegistry/TagSet + TryFormat (기반, 의존 없음)
2. Attribute/AttributeSet + Effect (심 코어)
3. Ability(게이트·부여 소스) + Unit/World/커맨드 큐
4. Item/Inventory + proto 직렬화 + dirty 추적
5. 복제(gameplay.proto, 권위 수집/미러 적용/시야 필터) + Bun3.Server.Gameplay 글루
   + 미니 디덕션 시나리오 E2E
6. CLAUDE.md 코드 룰 명시 + 템플릿 반영

## 14. 미래 확장 (이 스펙의 문이 열어두는 것)

- 이동 동기화 모듈(비신뢰 채널 + FixedFloat 공간 수치 + Transport.Steam) — 다음 스펙
- 락스텝 동기화 모듈(입력 복제, 틱 상태 해시 desync 감지) — 결정론 준비 코어 위에
- 태그 계층 데이터 테이블, 저장소 계약 패키지, 예측(prediction) — 수요 발생 시
