# Unreal GameplayTag/GAS 태그 저장·소유 구조 조사

- 조사일: 2026-08-12
- 범위: Unreal Engine GameplayTags와 Gameplay Ability System(GAS)의 태그 정의, 컨테이너 의미, 런타임 소유 구조
- 출처 정책: Epic Games 공식 문서와 공식 C++ API 레퍼런스만 사용
- 적용 대상: `Bun3.Gameplay`의 `TagCatalog`, `GameplayTag`, `TagSet` 후속 설계

## 1. 결론 요약

1. **Unreal의 사람이 편집하는 태그 정의는 영구 숫자 ID나 `parentId`가 아니라 `A.B.C` 전체 경로를 적는 목록이다.** 현재 엔진의 설정 객체도 `Tag`와 `DevComment`로 이루어진 행 목록을 저장한다. 따라서 Bun3의 JSON도 경로 문자열을 정본으로 삼는 편이 Unreal의 작성 경험과 더 가깝다. [Using Gameplay Tags](https://dev.epicgames.com/documentation/en-us/unreal-engine/using-gameplay-tags-in-unreal-engine), [UGameplayTagsList](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Runtime/GameplayTags/UGameplayTagsList), [FGameplayTagTableRow](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Runtime/GameplayTags/FGameplayTagTableRow)
2. **영구 저장 표현과 런타임 표현은 분리할 수 있다.** Unreal도 네트워크에서는 태그 이름 대신 `uint16` 계열 Net Index를 사용할 수 있고, 이때 서버와 클라이언트의 태그 사전이 같아야 한다. Bun3도 JSON 경로를 기동 시 한 번 읽고 결정적인 `ushort` 핸들로 변환하는 방식이 자연스럽다. [Gameplay Tags 프로젝트 설정](https://dev.epicgames.com/documentation/en-us/unreal-engine/project-section-of-the-unreal-engine-project-settings), [GameplayTags API의 `FGameplayTagNetIndex`](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Runtime/GameplayTags)
3. **일반 `FGameplayTagContainer`와 GAS의 런타임 소유 태그 저장소는 역할이 다르다.** 전자는 명시적으로 넣은 태그의 집합과 조회용 부모 태그를 다루고, 후자인 `FGameplayTagCountContainer`는 같은 태그가 여러 원인으로 적용되는 횟수와 모든 부모의 누적 횟수를 추적한다. [FGameplayTagContainer](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Runtime/GameplayTags/FGameplayTagContainer), [FGameplayTagCountContainer](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/FGameplayTagCountContainer)
4. **GAS에서 상태 태그의 중심 소유자는 보통 `UAbilitySystemComponent`(ASC)다.** Loose Tag, Gameplay Effect가 부여한 태그, 실행 중 Ability가 부여한 태그를 하나의 카운트 관점으로 조회한다. Ability와 Effect 자체에도 분류·요구조건용 태그 컨테이너가 있지만, 이것은 Actor의 현재 상태 저장소와 별개다. [UAbilitySystemComponent](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/UAbilitySystemComponent), [`GetGameplayTagCount`](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/UAbilitySystemComponent/GetGameplayTagCount)
5. **Item이 반드시 독립적인 ASC나 카운트 컨테이너를 가져야 한다는 GAS 규칙은 없다.** 공식 Lyra 샘플에서는 인벤토리/장비가 별도 게임 시스템이고, 장비가 플레이어의 ASC에 Ability Set을 부여한다. 아이템 인스턴스의 탄약 같은 상태에는 Lyra 전용 태그 스택 컨테이너를 쓴다. 이는 유용한 사례이지 엔진 강제 구조는 아니다. [Lyra Abilities](https://dev.epicgames.com/documentation/en-us/unreal-engine/abilities-in-lyra-in-unreal-engine), [Lyra Inventory and Equipment](https://dev.epicgames.com/documentation/en-us/unreal-engine/lyra-inventory-and-equipment-in-unreal-engine)

## 2. Unreal의 태그 정의와 저장 형식

### 2.1 태그의 정체성은 점 구분 전체 경로다

`FGameplayTag`는 `x.y` 형태로 태그 관리자에 등록되는 계층 이름이다. `Event.Movement.Dash` 같은 이름에서 `.`이 계층을 나누며, 하위 태그의 존재는 상위 경로의 존재를 암시한다. [FGameplayTag](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Runtime/GameplayTags/FGameplayTag), [Gameplay Tags 개요](https://dev.epicgames.com/documentation/en-us/unreal-engine/using-gameplay-tags-in-unreal-engine)

Unreal은 태그를 다음 세 경로로 정의할 수 있다.

- Project Settings에서 추가
- `.ini` 설정이나 Data Table에서 로드
- C++ Native Gameplay Tag로 정의

현재 공식 문서는 `.ini`의 `Config/DefaultGameplayTags.ini`와 `Config/Tags` 디렉터리를 읽고, Data Table은 CSV 또는 JSON으로 가져올 수 있다고 설명한다. [Using Gameplay Tags - Defining Gameplay Tags](https://dev.epicgames.com/documentation/en-us/unreal-engine/using-gameplay-tags-in-unreal-engine)

### 2.2 실제 설정 파일도 경로와 주석의 목록이다

Epic의 공식 4.27 문서에 공개된 `.ini` 예시는 다음 형태다. 이 예시는 형식을 식별하기 위한 최소 재작성이다.

```ini
[/Script/GameplayTags.GameplayTagsList]
GameplayTagList=(Tag="Vehicle.Air.Helicopter",DevComment="...")
GameplayTagList=(Tag="Movement.Flying",DevComment="")
```

즉, 작성 파일에는 별도의 숫자 ID와 부모 ID가 없고 전체 경로가 들어간다. [Gameplay Tags 4.27 - Editing `.ini` Files Directly](https://dev.epicgames.com/documentation/en-us/unreal-engine/gameplay-tags?application_version=4.27)

이 형식이 오래된 문서에만 남은 우연한 예가 아닌지는 현재 API로 교차 확인할 수 있다.

- `UGameplayTagsList`는 현재도 “ini list”를 위한 클래스이며, `GameplayTagList`의 타입은 `TArray<FGameplayTagTableRow>`다. [UGameplayTagsList](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Runtime/GameplayTags/UGameplayTagsList)
- `FGameplayTagTableRow`의 저장 필드는 `Tag: FName`과 `DevComment: FString`이다. 부모 ID 필드는 없다. [FGameplayTagTableRow](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Runtime/GameplayTags/FGameplayTagTableRow)
- 태그 트리 노드는 명시적으로 데이터에 적힌 태그인지 암시적으로 생긴 부모인지 구분하며, 소스가 없는 노드는 암시적으로 추가된 태그라고 설명되어 있다. [FGameplayTagNode](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Runtime/GameplayTags/FGameplayTagNode)

따라서 “Unreal은 `A.B.C`를 쭉 적는다”는 이해가 맞다. 에디터의 트리는 저장 구조가 아니라 전체 경로 목록에서 복원한 보기라고 이해하는 편이 정확하다.

### 2.3 Bun3 JSON에 적용할 수 있는 형태

다음은 **Epic의 파일 형식을 그대로 복제한 것이 아니라**, 그 의미를 JSON에 옮긴 Bun3 권장안이다.

```json
{
  "schemaVersion": 1,
  "tags": [
    {
      "name": "Ability.Movement.Jump",
      "comment": "기본 점프 능력"
    },
    {
      "name": "State.Dead.Ghost"
    }
  ],
  "redirects": [
    {
      "from": "State.Killed",
      "to": "State.Dead"
    }
  ]
}
```

판단:

- `id`와 `parentId`는 작성 JSON에 넣지 않아도 된다.
- 부모는 마지막 `.` 이전 경로로 계산하고, 명시되지 않은 부모도 로드 과정에서 만든다.
- 주석이 전혀 필요 없다면 문자열 배열만으로 더 단순화할 수 있다. 다만 Unreal UI처럼 툴팁·설명을 제공하려면 `{ name, comment }` 행이 낫다.
- 이름 변경 호환성이 필요하면 영구 ID보다 문자열 redirect가 Unreal의 방식에 가깝다. Unreal 설정에도 `GameplayTagRedirects`가 별도로 존재한다. [UGameplayTagsList](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Runtime/GameplayTags/UGameplayTagsList)

### 2.4 JSON을 기동 시 한 번 읽는 방식

사용자가 선택한 “JSON 하나를 기동 시 읽어 불변 카탈로그를 만든다”는 방식은 수천~수만 개 규모에서도 핫패스 성능과 분리된다. JSON 파싱과 문자열 검증은 시작 시 한 번만 수행하고, 게임 실행 중에는 숫자 핸들과 배열만 사용하면 되기 때문이다. 이는 Unreal의 구현을 그대로 옮긴다는 뜻이 아니라, **작성 데이터와 런타임 표현을 분리한다는 설계 추론**이다.

권장 로드 절차:

1. JSON을 한 번 파싱한다.
2. 빈 세그먼트, 금지 문자, 중복 경로, 잘못된 redirect를 검증한다.
3. 모든 암시적 부모 경로를 생성한다.
4. 전체 경로를 ASCII 소문자로 canonicalize한 뒤 ordinal 기준으로 정렬한다.
5. `None = 0`, 나머지에 결정적인 `ushort` Runtime Index를 부여한다.
6. `ParentIndex`, 조상 목록 또는 부모 누적 갱신용 범위를 배열로 만든다.
7. 정규화된 경로와 redirect로 카탈로그 fingerprint를 계산한다.
8. 서버와 클라이언트가 시뮬레이션 전에 fingerprint가 같은지 확인한다.
9. 이후 카탈로그를 동결하고 미등록 태그 요청은 실패시킨다.

Unreal도 Fast Replication에서 이름 대신 인덱스를 보내며, 이 기능을 쓰려면 서버와 클라이언트의 태그 목록이 동일해야 한다. 또한 Unreal의 `FGameplayTagNetIndex`는 `uint16`이다. 이 사실은 Bun3의 `ushort` 런타임 핸들 방향을 지지하지만, 위 정렬·fingerprint 절차 자체는 Bun3의 설계 선택이다. [Gameplay Tags 프로젝트 설정 - Advanced Replication](https://dev.epicgames.com/documentation/en-us/unreal-engine/project-section-of-the-unreal-engine-project-settings), [GameplayTags API](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Runtime/GameplayTags)

주의할 점:

- 제한 수에는 JSON에 명시한 태그뿐 아니라 자동 생성한 모든 부모 노드도 포함해야 한다.
- 런타임 인덱스는 태그 추가·삭제에 따라 달라질 수 있다. DB, 장기 저장 파일, 서로 다른 버전의 리플레이에는 인덱스만 영구 저장하지 말고 경로 문자열 또는 카탈로그 버전과 함께 저장해야 한다.
- 결정론에는 JSON의 공백이나 원래 배열 순서가 아니라 정규화 후의 의미가 중요하다. fingerprint도 원본 바이트보다 정규화된 경로 집합을 기준으로 만드는 편이 안전하다는 것이 이 조사의 설계 추론이다.

## 3. `FGameplayTagContainer`의 정확한 역할

### 3.1 명시 태그 집합과 암시 부모 캐시

공식 API는 `FGameplayTagContainer`를 “명시적으로 추가된 태그와, 하위 태그를 넣어 암시적으로 포함된 태그”를 보유하는 컬렉션으로 설명한다. `GetGameplayTagArray`와 `Num`은 명시 태그만 대상으로 하고, `FillParentTags`는 명시 태그에서 부모 목록을 채운다. [FGameplayTagContainer API](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Runtime/GameplayTags/FGameplayTagContainer)

예를 들어 명시 목록에 `State.Dead.Ghost`만 있을 때:

- `HasTag(State.Dead.Ghost)` → `true`
- `HasTag(State.Dead)` → `true`
- `HasTag(State)` → `true`
- `HasTagExact(State.Dead)` → `false`
- 반대로 `State`만 보유했을 때 `HasTag(State.Dead)` → `false`

이 방향성은 공식 API의 `HasTag`, `HasAny`, `HasAll` 예제에 명시되어 있다. [FGameplayTagContainer 계층/정확 조회](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Runtime/GameplayTags/FGameplayTagContainer)

### 3.2 기본 동작은 멀티셋이 아니라 집합에 가깝다

일반 `AddTag`는 태그를 추가하고, `AppendTags`는 문서상 집합의 합집합으로 설명된다. `AddTagFast`만 명시적으로 유일성 검사를 건너뛴다. 따라서 정상 API 사용에서 같은 태그를 여러 번 넣은 횟수를 의미 있는 상태로 간주하지 않는다. [FGameplayTagContainer의 `AddTagFast`와 `AppendTags`](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Runtime/GameplayTags/FGameplayTagContainer)

`AddLeafTag`는 자식이 이미 있으면 부모를 추가하지 않고, 더 구체적인 자식을 추가하면 직접 보유하던 부모를 제거할 수 있다. 이는 컨테이너를 “가장 구체적인 명시 태그들의 집합”으로 관리할 때 쓰는 별도 편의 동작이다. [FGameplayTagContainer::AddLeafTag](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Runtime/GameplayTags/FGameplayTagContainer)

### 3.3 Container가 적합한 곳

Unreal은 객체에 `FGameplayTagContainer` 프로퍼티를 추가해 여러 태그를 적용하고, `HasTag`/`HasAny`/`HasAll` 또는 `FGameplayTagQuery`로 평가하도록 안내한다. `IGameplayTagAssetInterface`를 구현하면 서로 다른 객체 타입에서 태그 조회를 표준화하고, 필요하면 여러 내부 컨테이너를 합쳐 반환할 수도 있다. [Using Gameplay Tags - Applying/Evaluating Tags](https://dev.epicgames.com/documentation/en-us/unreal-engine/using-gameplay-tags-in-unreal-engine), [IGameplayTagAssetInterface](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Runtime/GameplayTags/IGameplayTagAssetInterface)

이 성격에 맞는 Bun3 사용처는 다음과 같다.

- `AbilityDef` 자체를 분류하는 태그
- `EffectSpec` 자체를 분류하는 태그
- `ItemDef`의 고정 분류 태그
- Required/Blocked/Cancel/Query 같은 조건식의 피연산자
- 현재 상태가 아니라 정적 정의나 필터를 표현하는 작은 태그 집합

## 4. `FGameplayTagCountContainer`와의 차이

`FGameplayTagCountContainer`는 GameplayAbilities 플러그인에 속하며, 동일 태그가 몇 번 적용되었는지와 부모 태그의 누적 횟수를 동시에 추적한다. 공식 API에는 다음 저장 개념이 드러난다. [FGameplayTagCountContainer API](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/FGameplayTagCountContainer)

- `ExplicitTags`: 직접 추가된 태그들의 `FGameplayTagContainer`
- `ExplicitTagCountMap`: 정확히 그 태그가 추가된 횟수
- `GameplayTagCountMap`: 부모까지 포함한 활성 횟수
- 태그 카운트 변화 이벤트/델리게이트

공식 예시는 `A.B`와 `A.C`를 한 번씩 추가했을 때 다음 차이를 설명한다.

- `GetExplicitTagCount("A") == 0`
- `GetTagCount("A") == 2`
- `GetExplicitTagCount("A.B") == 1`

즉, `FGameplayTagContainer`가 “무슨 태그가 있는가”를 나타내는 집합이라면, `FGameplayTagCountContainer`는 “여러 부여 원인이 합쳐진 현재 소유 상태”를 나타낸다. [`GetExplicitTagCount`와 `GetTagCount`](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/FGameplayTagCountContainer)

| 구분 | `FGameplayTagContainer` | `FGameplayTagCountContainer` |
|---|---|---|
| 핵심 의미 | 명시 태그의 집합 | 태그 적용 원인의 누적 카운트 |
| 중복 추가 | 보통 유일성 유지 | 횟수 증가가 의미 있음 |
| 부모 | 계층 조회용 암시 부모 | 모든 부모의 누적 카운트 추적 |
| 제거 | 명시 태그 제거 | delta 감소, 0 전환 관리 |
| 이벤트 | 주 역할 아님 | 카운트/존재 전환 이벤트 지원 |
| 대표 사용처 | 정의, 분류, 필터, 쿼리 | ASC가 소유한 현재 상태 |

현재 Bun3의 `TagSet`이 “같은 태그를 Effect 두 개가 주면 하나가 제거되어도 남아야 한다”는 의미를 갖는다면, 이름과 무관하게 Unreal의 `FGameplayTagContainer`보다 `FGameplayTagCountContainer`에 가깝다.

## 5. GAS에서 누가 태그를 소유하는가

### 5.1 중심 집계자: `UAbilitySystemComponent`

Epic의 GAS 개요는 ASC가 Actor의 Ability, 진행 중 Effect, Attribute, Gameplay Tag와 Event를 추적한다고 설명한다. ASC는 `IGameplayTagAssetInterface`를 구현하고, 공식 API 주석상 그 태그 연산은 Tag Count Container를 사용한다. [Understanding GAS - Tracking Ownership](https://dev.epicgames.com/documentation/en-us/unreal-engine/understanding-the-unreal-engine-gameplay-ability-system), [UAbilitySystemComponent API](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/UAbilitySystemComponent)

ASC의 `GetGameplayTagCount`는 다음 기여를 합산한 현재 카운트를 반환한다.

- 게임 코드가 직접 추가한 Loose Gameplay Tag
- Gameplay Effect가 부여한 태그
- 실행 중 Gameplay Ability가 부여한 태그

공식 API가 이 세 종류를 명시하며, Loose Tag는 Gameplay Effect에 의해 뒷받침되지 않는 게임 코드용 태그라고 설명한다. [`GetGameplayTagCount`](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/UAbilitySystemComponent/GetGameplayTagCount), [`AddLooseGameplayTag`](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/UAbilitySystemComponent/AddLooseGameplayTag)

따라서 GAS식으로 보면 Unit/Character의 “현재 상태 태그”는 여러 시스템에 흩어진 컨테이너를 매번 순회해 계산하는 값이라기보다, ASC의 카운트 저장소로 모이는 집계 상태다.

### 5.2 Gameplay Ability가 가진 태그

Gameplay Ability는 두 종류의 태그 관계를 가진다.

1. **Ability 자체의 분류**: Asset/Ability Tags와 `FGameplayAbilitySpec.DynamicAbilityTags`가 Ability를 찾고, 차단하고, 취소하는 분류 정보로 쓰인다. `FGameplayAbilitySpec`은 ASC에 호스팅되는 실행 가능한 Ability의 런타임 상태다. [UGameplayAbility::GetAssetTags](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/UGameplayAbility/GetAssetTags), [FGameplayAbilitySpec](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/FGameplayAbilitySpec)
2. **Owner/Source/Target와의 조건·기여**: Activation Required/Blocked Tags는 실행 주체의 상태를 검사하고, Activation Owned Tags는 Ability가 실행 중인 동안 owner에게 태그를 부여한다. Source/Target Required/Blocked Tags는 소스와 대상의 태그 조건을 검사한다. [Using Gameplay Abilities - Tags](https://dev.epicgames.com/documentation/en-us/unreal-engine/using-gameplay-abilities-in-unreal-engine)

중요한 구분은 Ability의 분류 태그 컨테이너가 그 자체로 Actor의 현재 상태 저장소는 아니라는 점이다. `ActivationOwnedTags`처럼 명시적으로 owner에게 부여하는 태그만 실행 기간 동안 ASC의 소유 카운트에 기여한다. [UGameplayAbility::ActivationOwnedTags](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/UGameplayAbility/ActivationOwnedTags?application_version=5.5)

### 5.3 Gameplay Effect가 가진 태그

Gameplay Effect에도 “Effect가 가진 태그”와 “대상에게 주는 태그”가 분리되어 있다.

- Asset Tags는 Effect Asset을 분류하지만 Actor에게 전달되지 않는다.
- Target/Granted Tags는 Effect가 적용된 Target Actor에게 부여된다.
- Target Tag Requirements는 대상의 현재 태그로 Effect 적용 또는 지속 여부를 판단한다.

현재 공식 Gameplay Effect 문서는 이 역할들을 `UAssetTagsGameplayEffectComponent`, `UTargetTagsGameplayEffectComponent`, `UTargetTagRequirementsGameplayEffectComponent`로 구분한다. [Gameplay Effects - Gameplay Effect Components](https://dev.epicgames.com/documentation/en-us/unreal-engine/gameplay-effects-for-the-gameplay-ability-system-in-unreal-engine), [`UGameplayEffect::GetGrantedTags`](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/UGameplayEffect/GetGrantedTags)

`FGameplayEffectSpec`은 생성 시 Source Tags를, 실행 시 Target Tags를 캡처하며, `FTagContainerAggregator`는 Actor에서 온 태그와 Effect Spec에서 온 태그를 합쳐 계산에 제공한다. 이 Source/Target 태그는 Effect가 영구 소유하는 새 상태라기보다 Effect 실행·계산 문맥의 캡처/집계 정보다. [FGameplayEffectSpec](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/FGameplayEffectSpec), [FTagContainerAggregator](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/FTagContainerAggregator)

### 5.4 Actor, Pawn, PlayerState

GAS를 사용하는 Actor는 직접 ASC를 소유하거나, Pawn/PlayerState 등 다른 Actor가 소유한 ASC에 접근할 수 있다. Epic은 Pawn 교체나 respawn 뒤에도 점수·긴 쿨다운 같은 상태를 유지하려면 PlayerState가 ASC를 소유하고 Pawn이 그것을 반환하는 구성을 예로 든다. [Ability System Component And Attributes - Basic Requirements/Advanced Usage](https://dev.epicgames.com/documentation/en-us/unreal-engine/gameplay-ability-system-component-and-gameplay-attributes-in-unreal-engine)

공식 Lyra 샘플은 이 선택을 실제로 사용한다.

- 모든 `ALyraPlayerState`가 ASC를 소유한다.
- 플레이어와 AI bot 모두 PlayerState ASC를 사용한다.
- Pawn이 바뀌거나 아직 Pawn이 없어도 플레이어 고유 GAS 상태를 유지한다.
- Pawn Data, Game Feature, Equipment가 부여하는 Ability Set도 PlayerState의 ASC에 들어간다.

이것은 “태그는 반드시 PlayerState에 둬야 한다”는 엔진 규칙이 아니라, 수명과 소유 경계에 맞춘 Lyra의 선택이다. [Abilities in Lyra - ALyraPlayerState/ULyraAbilitySet](https://dev.epicgames.com/documentation/en-us/unreal-engine/abilities-in-lyra-in-unreal-engine)

### 5.5 Item과 Equipment

Core GAS에는 모든 Item이 ASC를 가져야 한다는 규칙이 없다. `FGameplayAbilitySpec.SourceObject`는 Ability를 만든 Actor 또는 일반 UObject를 가리킬 수 있으므로, 장비/아이템을 Ability의 부여 출처로 연결할 수 있지만 Ability 자체는 여전히 ASC에 호스팅된다. [FGameplayAbilitySpec::SourceObject](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/FGameplayAbilitySpec)

Lyra의 구체적인 선택은 다음과 같다.

- Inventory와 Equipment는 GAS 외부의 게임별 시스템이다.
- 장비는 장착 중 플레이어에게 Ability Set을 부여할 수 있다.
- Inventory Item Instance는 탄약·용량 같은 상태를 Gameplay Tag Stack으로 보유한다.
- Equipment Instance도 별도 태그 스택을 가질 수 있다.

따라서 Item의 태그는 용도에 따라 갈린다.

- 아이템 종류·필터·슬롯 같은 **정적 분류**: 작은 일반 태그 컨테이너
- 탄약·충전·스택처럼 아이템 자체의 **가변 카운트 상태**: 아이템 전용 카운트/스택 저장소
- 장착으로 캐릭터에게 생기는 버프·능력·상태: 캐릭터/플레이어의 중심 태그 집계자에 기여

Lyra는 한 가지 공식 샘플일 뿐, 이 세 분리를 GAS 자체가 강제하지는 않는다. [Lyra Inventory and Equipment](https://dev.epicgames.com/documentation/en-us/unreal-engine/lyra-inventory-and-equipment-in-unreal-engine), [Abilities in Lyra - Equipment grants](https://dev.epicgames.com/documentation/en-us/unreal-engine/abilities-in-lyra-in-unreal-engine)

## 6. Bun3 설계에 적용하는 권장 소유 모델

아래는 공식 동작에서 도출한 **Bun3 설계 제안**이며 Unreal의 필수 구현을 그대로 복제한다는 뜻은 아니다.

| Bun3 개념 | 역할 | 예상 소유자 |
|---|---|---|
| `TagCatalog` | 전체 경로, 부모 관계, 런타임 `ushort` 인덱스의 불변 사전 | 프로세스/World가 공유 |
| `GameplayTag` | 카탈로그 안의 값 타입 핸들 | 어디서든 값으로 사용 |
| `TagContainer` 또는 유사 불변/소형 집합 | 정의·분류·쿼리 조건 | AbilityDef, EffectSpec, ItemDef, Query |
| `TagCountContainer` | 여러 원인이 기여하는 현재 상태와 계층 카운트 | Unit 또는 ASC에 해당하는 gameplay subject |
| 선택적 Item Tag Stack | 아이템 자체의 가변 수량성 태그 | 필요한 ItemInstance만 |

### 6.1 모든 GameplayObject가 카운트 저장소를 가질 필요는 없다

기존 설계의 “GameplayObject 전원이 `TagSet` 보유”는 단순하지만, GAS의 구조를 그대로 따른 결정은 아니다. 더 정확한 기준은 다음 질문이다.

> 이 객체가 여러 Effect/Ability/장비/게임 코드로부터 태그를 동적으로 부여받고, 각 원인이 사라질 때 독립적으로 감소해야 하는가?

- 그렇다면 `TagCountContainer`가 필요하다.
- 단지 정적 분류 태그를 갖거나 Required/Blocked 조건을 표현한다면 작은 `TagContainer`면 충분하다.
- Item이 자신만의 동적 상태를 가질 때만 ItemInstance용 카운트/스택 저장소를 추가하는 편이 깊은 모듈 경계에 맞다.

### 6.2 전체 태그 수와 컨테이너당 보유 수를 분리해야 한다

요구사항은 전체 카탈로그가 수천 개, 상한은 암시 부모를 포함해 65,535개 미만이며, 한 runtime subject가 직접 보유하는 태그 종류는 최대 약 64개다. 이 경우 전역 카탈로그와 개별 상태 저장소를 같은 밀도 구조로 만들 이유가 없다.

- 전역 `TagCatalog`: 전체 태그 수에 비례하는 조밀 배열이 적합하다.
- 정의용 `TagContainer`: 최대 64개의 정렬된 `ushort` 배열이나 소형 버퍼가 후보가 된다.
- 상태용 `TagCountContainer`: 직접 활성 태그와 그 조상만 저장하는 희소 카운트 구조가 후보가 된다.

65,535개 전체에 대한 비트셋은 컨테이너 하나당 약 8 KiB다. subject가 많으면 이 비용이 반복되므로 기본 표현으로 단정하기 어렵다. 반면 직접 태그가 64개 이하이고 깊이가 작다면 Add/Remove 시 조상 카운트를 갱신하고 조회 시 누적 카운트를 한 번 찾는 방식이 읽기 중심 사용에 맞는다. 이는 공식 Epic 성능 보장이 아니라 요구 규모에 대한 설계 추론이며, 최종 선택은 benchmark로 확인해야 한다.

### 6.3 권장 의미와 연산

GAS의 두 컨테이너 의미를 Bun3에 옮기면 API를 다음처럼 분리할 수 있다.

**정의/집합 컨테이너**

- `Add`는 유일성을 보장한다.
- `Remove`는 명시 태그를 제거한다.
- `Has`는 자식 보유가 부모 질의를 만족한다.
- `HasExact`는 명시 태그만 검사한다.
- 카운트 의미는 없다.

**상태/카운트 컨테이너**

- `Add(tag, count)`는 exact count와 모든 조상 aggregate count를 늘린다.
- `Remove(tag, count)`는 같은 범위를 줄인다.
- `ExactCount`와 hierarchical `Count`를 분리한다.
- `HasExact`와 hierarchical `Has`를 분리한다.
- Unreal은 존재·카운트 변화 이벤트도 제공하지만 Bun3 v1 설계에서는 결정론과 재진입 계약을
  별도로 확정할 때까지 컨테이너 이벤트를 제공하지 않는다.

`FGameplayTagCountContainer`의 공식 API도 exact count와 hierarchical count, 태그 delta 갱신, 태그 이벤트를 별도 개념으로 제공한다. [FGameplayTagCountContainer](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/FGameplayTagCountContainer), [`UpdateTagCount`](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/FGameplayTagCountContainer/UpdateTagCount)

## 7. 확인된 사실과 남은 불확실성

### 공식 자료로 확인된 사실

- Unreal의 작성 데이터는 점 구분 전체 경로와 선택적 주석의 목록이며, 영구 숫자 ID/부모 ID를 요구하지 않는다.
- 하위 경로가 있으면 상위 태그 경로는 암시적으로 존재할 수 있다.
- 일반 Container의 기본 의미는 유일한 명시 태그 집합이며 계층/정확 조회를 구분한다.
- GAS Count Container는 exact와 parent aggregate count를 모두 관리한다.
- ASC는 Loose/Effect/Ability 기여 태그를 카운트 관점으로 조회한다.
- Ability/Effect의 분류 태그와 Actor에게 실제 부여되는 상태 태그는 구분된다.
- ASC는 Actor, Pawn, PlayerState 등 수명에 적합한 곳에 둘 수 있다.
- Item마다 ASC를 두는 것은 GAS의 필수 구조가 아니다.
- Unreal Fast Replication은 인덱스를 사용하며 동일한 서버/클라이언트 태그 사전을 전제한다.

### 공식 자료만으로 확정할 수 없는 부분

- Epic API 문서는 `FGameplayTagContainer`와 `FGameplayTagCountContainer`의 공개 의미는 보여 주지만, Bun3이 기대하는 수만~수십만 회/프레임 조건의 benchmark나 최악 지연시간을 제공하지 않는다.
- Unreal 내부 컨테이너와 캐시의 정확한 성능 특성은 엔진 버전에 따라 달라질 수 있으며 Bun3의 .NET 자료구조 성능을 직접 예측하지 못한다.
- `FGameplayTagNetIndex = uint16`이라는 사실만으로 Bun3이 65,535개 모든 값을 쓸 수 있다고 단정할 수 없다. `None`과 sentinel 정책, 암시 부모 포함 제한을 Bun3에서 명시해야 한다.
- Lyra의 PlayerState ASC와 Item Tag Stack 구조는 공식 샘플의 검증된 사례지만 모든 게임에 대한 Epic의 강제 규약은 아니다.
- JSON 파서 라이브러리, 초기화 API, 카탈로그 파일 탐색/배포 위치는 Unreal 조사로 결정할 수 없는 Bun3 구현 선택이다.

## 8. 이 조사에서 바로 내릴 수 있는 결정

1. 작성 정본은 ID 없는 JSON 경로 목록으로 한다.
2. JSON은 기동 시 한 번 로드하고 이후 `TagCatalog`를 동결한다.
3. 전체 경로를 결정적으로 정렬해 `ushort` Runtime Index를 만든다.
4. 서버·클라이언트 카탈로그 fingerprint가 다르면 세션/락스텝 시작을 거부한다.
5. 영구 저장과 버전 경계를 넘는 데이터는 태그 경로를 정본으로 둔다.
6. 정적 분류 집합과 동적 카운트 상태를 서로 다른 타입 또는 명확히 다른 모듈 역할로 분리한다.
7. Unit/플레이어처럼 Effect와 Ability의 기여를 받는 gameplay subject가 중심 `TagCountContainer`를 가진다.
8. AbilityDef, EffectSpec, ItemDef는 기본적으로 작은 분류 컨테이너만 가진다.
9. ItemInstance의 카운트 저장소는 실제 아이템 상태 요구가 생긴 경우에만 선택적으로 둔다.
10. 최종 자료구조는 전체 태그 수, subject 수, 직접 태그 수, 트리 깊이를 분리한 benchmark로 확정한다.
