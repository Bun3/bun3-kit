# 게임플레이 관찰(Observation) API 조사 — GAS와 오픈소스 어빌리티 프레임워크

- 조사일: 2026-09-29
- 범위: 게임 코드(UI, VFX, 오디오, 네트워크, 전투 로그)가 시뮬레이션 결과를 **관찰**하는 방식. 활성 효과 열거, 적용/제거 알림과 출처, 게임플레이 이벤트, Attribute 변경, 쿨다운 잔여, 부여 어빌리티 열거, 코스메틱 채널(GameplayCue), push 대 pull
- 출처 정책: GAS는 Epic 공식 문서와 공식 C++ API 레퍼런스(`dev.epicgames.com`, 페이지 표기 UE 5.8)만 쓴다. 오픈소스는 GitHub 소스를 커밋 고정 링크로 인용한다. 커뮤니티 글(tranek GASDocumentation 등)은 탐색 포인터로만 썼고 본문 근거로 쓰지 않았다.
- 적용 대상: `Bun3.Gameplay`의 관찰 표면 — `EffectTarget.PendingEffectEvents`, `AttributeSet.PendingChanges`, `GameplayEvent`, `AbilityCatalog`, `AbilitySet`, `World.TryActivateAbility`
- 접근 불가/미확인:
  - Unreal Engine 소스(`github.com/EpicGames/UnrealEngine`)는 Epic 연동 계정이 필요하다. 이 조사 환경에서는 404로 열리지 않았다. 따라서 **구현 세부(예: `GetCooldownTimeRemaining`이 내부적으로 어떤 쿼리를 쓰는지)는 확인하지 못했다.** 공개 API 문서의 서명과 주석만 근거로 삼는다.
  - Lyra 소스도 같은 이유로 열지 못했다. `GameplayMessageSubsystem`, verb message, kill feed는 공식 문서에 설명이 없어 **확인 불가**로 둔다.
  - `FAbilityFailedDelegate`, `FGenericAbilityDelegate`, `SendGameplayEventToActor` 단독 API 페이지와 `NetMulticast_InvokeGameplayCueExecuted` 페이지는 목차만 렌더링됐다. 델리게이트 파라미터 목록과 복제 지정자(Unreliable 여부)는 개별 페이지로 확인하지 못했다.

## 1. 결론 요약

1. **GAS의 관찰 표면은 "상태 조회(pull) + 멀티캐스트 델리게이트(push)"의 이중 구조다.** 버프 바처럼 계속 그려야 하는 것은 쿼리로 읽는다(`GetActiveEffects(Query)`, `GetActiveEffectsTimeRemainingAndDuration`, `GetActiveGameplayEffect(Handle)`). 상태가 바뀌는 순간은 델리게이트로 받는다(`OnActiveGameplayEffectAddedDelegateToSelf`, `OnAnyGameplayEffectRemovedDelegate`, 핸들별 stack/time 변경 델리게이트). [UAbilitySystemComponent](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/UAbilitySystemComponent), [FActiveGameplayEffectEvents](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/FActiveGameplayEffectEvents)
2. **GAS의 알림 페이로드에는 거의 항상 출처(instigator)가 실린다.** 효과 문맥은 `GetInstigator`/`GetEffectCauser`/`GetOriginalInstigator`를 제공한다. 제거 정보는 `EffectContext`, `StackCount`, `bPrematureRemoval`을 싣는다. 이벤트 데이터는 `Instigator`/`Target`/`EventMagnitude`를, 큐 파라미터는 `Instigator`/`EffectCauser`/`RawMagnitude`를 싣는다. Bun3의 `EffectLifecycleEvent`에는 이 출처가 빠져 있다. [FGameplayEffectContext](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/FGameplayEffectContext), [FGameplayEffectRemovalInfo](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/FGameplayEffectRemovalInfo), [FGameplayEventData](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/FGameplayEventData), [FGameplayCueParameters](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/FGameplayCueParameters)
3. **GAS 코어에는 "전투 로그" 전용 채널이 없다.** 공식 Lyra 문서에 적힌 데미지 숫자 경로는 GameplayCue다. `GCN_Character_DamageTaken`이 클라이언트에서 cue의 raw magnitude와 hit result 위치를 받아 로컬 instigator에게만 표시한다. 즉 "누가 누구를 얼마나"는 코스메틱 채널로 흐른다. [Abilities in Lyra](https://dev.epicgames.com/documentation/en-us/unreal-engine/abilities-in-lyra-in-unreal-engine)
4. **쿨다운은 별도 타이머가 아니라 "쿨다운 태그를 주는 활성 효과의 남은 시간"이다.** GAS는 `GetCooldownTimeRemainingAndDuration`(남은 시간, 원래 길이)과 `GetCooldownTags`를 공개한다. 조사한 오픈소스 셋도 모두 같은 `(TimeRemaining, TotalDuration)` 형태를 쓴다. Bun3도 쿨다운을 효과로 구현했으므로 조회만 추가하면 된다. [UGameplayAbility](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/UGameplayAbility), [sjai013 `CheckCooldown`](https://github.com/sjai013/unity-gameplay-ability-system/blob/725f1abd149e22c02fb51fefe7899aabb3f963d7/Packages/com.sjai013.abilitysystem/Runtime/ability-system/Authoring/AbstractAbilitySpec.cs#L90-L133)
5. **GameplayCue는 코스메틱 전용이고 비신뢰 복제되며, 게임 로직은 여기에 의존하면 안 된다.** 이벤트는 `OnActive`/`WhileActive`/`Executed`/`Removed` 넷이다. `WhileActive`는 적용 순간을 보지 못한 관찰자(join in progress)를 위한 것이다. 따라서 코스메틱 계층에는 전이 이벤트와 "지금 활성인 것"의 재구성 경로가 둘 다 필요하다. [Understanding GAS](https://dev.epicgames.com/documentation/en-us/unreal-engine/understanding-the-unreal-engine-gameplay-ability-system), [EGameplayCueEvent](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/EGameplayCueEvent__Type)
6. **오픈소스는 스펙트럼 전체에 퍼져 있다.**
   - sjai013: 순수 pull. public 리스트를 두고 매 LateUpdate에 이전/현재 값을 diff한다.
   - EX-GAS: payload 없는 dirty 이벤트를 받은 뒤 리스트를 pull한다.
   - CycloneGames: GAS식 push다. 대신 무할당 콜백 리스트, `readonly struct` + `in` 페이로드, post-commit 디스패치로 비용을 막는다.
   - Flexi: 이벤트를 큐에 쌓았다가 페이즈 경계에서 드레인하고, 게임이 구현한 resolver 인터페이스 하나로 전달한다.

   (§9 참고)
7. **결정론·무할당 시뮬레이션에는 Bun3의 현행 pull 드레인 버퍼가 맞다.** push가 치르는 비용(재진입, 구독 순서 의존, 델리게이트 할당, 콜백 예외의 시뮬 오염)을 CycloneGames는 tombstone·post-commit·예외 격리·스레드 assert라는 별도 기계로 막는다. Bun3는 이 기계를 들이는 대신 **드레인 버퍼를 보강하고(출처 필드, GameplayEvent 버퍼 신설) 값 복사 조회 API를 추가하는 방향**을 권한다(§11).

## 2. GAS — 활성 효과 열거, 남은 시간, 스택

ASC는 쿼리 기반 조회를 제공한다. [UAbilitySystemComponent](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/UAbilitySystemComponent)

| API | 공식 설명 |
|---|---|
| `TArray<FActiveGameplayEffectHandle> GetActiveEffects(const FGameplayEffectQuery&)` | "Returns list of active effects, for a query" |
| `TArray<float> GetActiveEffectsTimeRemaining(Query)` | "Gets time remaining for all effects that match query" |
| `TArray<TPair<float,float>> GetActiveEffectsTimeRemainingAndDuration(Query)` | 남은 시간과 전체 길이를 함께 반환 |
| `const FActiveGameplayEffect* GetActiveGameplayEffect(Handle)` | "Returns const pointer to the actual active gameplay effect structure" |
| `GetActiveEffectsWithAllTags(Tags)` | 태그를 모두 가진 효과의 핸들 |
| `const FActiveGameplayEffectsContainer& GetActiveGameplayEffects()` | "Returns all active gameplay effects on this ASC" |

- **식별은 핸들 값으로 한다.** `FActiveGameplayEffect.Handle`은 "Globally unique ID"다. 시작 시각(`StartServerWorldTime`)과 `GetTimeRemaining`/`GetDuration`/`GetEndTime`을 갖는다. 핸들별 이벤트 묶음 `EventSet`도 있다. [FActiveGameplayEffect](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/FActiveGameplayEffect)
- **Blueprint 편의 함수도 핸들을 받는다.** `GetActiveGameplayEffectRemainingDuration`, `GetActiveGameplayEffectStackCount`, `GetActiveGameplayEffectExpectedEndTime`이 있다. 마지막 함수는 "someone could remove or change it before that happens"라고 경고한다. [UAbilitySystemBlueprintLibrary](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/UAbilitySystemBlueprintLibrary)
- **쿼리는 AND 조합 필터다.** `FGameplayEffectQuery`의 필드는 `OwningTagQuery`(효과가 부여하는 태그), `EffectTagQuery`(효과 자체 태그), `SourceTagQuery`, `ModifyingAttribute`, `EffectSource`, `EffectDefinition`, `IgnoreHandles`이고, 네이티브 `CustomMatchDelegate`도 둔다. [FGameplayEffectQuery](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/FGameplayEffectQuery)
- **컨테이너는 range-for 순회를 권장한다.** `FActiveGameplayEffectsContainer`는 `FFastArraySerializer` 기반이다. 문서는 `for (const FActiveGameplayEffect& Effect : this)` 같은 iterator 순회를 권장한다. `GetActiveEffectCount`는 쿼리에 맞는 수를 "including stack count"로 센다. [FActiveGameplayEffectsContainer](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/FActiveGameplayEffectsContainer)

관찰: 조회 결과가 `TArray`(할당)거나 내부 구조체의 const 포인터(수명 계약 필요)다. Bun3에는 둘 다 그대로 옮기기 어렵다. 할당 금지 규칙에 걸리고, `EffectInstance`가 풀링된다.

## 3. GAS — 효과 적용/제거 알림과 출처

| 델리게이트 | 공식 설명 | 비고 |
|---|---|---|
| `OnGameplayEffectAppliedDelegateToSelf` | "Called on server whenever a GE is applied to self." | 서버 전용, instant 포함 |
| `OnGameplayEffectAppliedDelegateToTarget` | "Called on server whenever a GE is applied to someone else." | **가해자 쪽** 알림 |
| `OnActiveGameplayEffectAddedDelegateToSelf` | "Called on both client and server whenever a duration based GE is added" | UI용 |
| `OnPeriodicGameplayEffectExecuteDelegateOnSelf/OnTarget` | 주기 실행 시 | 서버 |
| `OnAnyGameplayEffectRemovedDelegate()` | "Called when any gameplay effects are removed" | |
| `OnGameplayEffectRemoved_InfoDelegate(Handle)` / `OnGameplayEffectStackChangeDelegate(Handle)` / `OnGameplayEffectTimeChangeDelegate(Handle)` | 핸들별 구독 | 공식 설명 없음 |
| `OnImmunityBlockGameplayEffectDelegate` | "Immunity notification support" | 면역 차단 알림 |

출처: [UAbilitySystemComponent](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/UAbilitySystemComponent). 핸들별 묶음은 `FActiveGameplayEffectEvents`의 `OnEffectRemoved`/`OnInhibitionChanged`/`OnStackChanged`/`OnTimeChanged`다. [FActiveGameplayEffectEvents](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/FActiveGameplayEffectEvents)

**출처 정보**

- `FGameplayEffectContext`: "stores an instigator and related data"이며 게임이 서브클래싱할 수 있다. 제공 멤버는 다음과 같다. [FGameplayEffectContext](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/FGameplayEffectContext)
  - `GetInstigator`: "immediate instigator"
  - `GetEffectCauser`: "physical actor tied to the application"
  - `GetOriginalInstigator`: "original instigator that started the whole chain"
  - `GetSourceObject`, `GetAbility`, `GetHitResult`
- `FGameplayEffectRemovalInfo`: `ActiveEffect`, `StackCount`("before it was removed"), `bPrematureRemoval`, `bPredictionRejected`, `EffectContext`를 담는다. [FGameplayEffectRemovalInfo](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/FGameplayEffectRemovalInfo)

Bun3와의 대응:

- `EffectLifecycleKind.Expired`/`RemovedPrematurely`는 `bPrematureRemoval`과 같은 구분이다.
- `StackChanged`는 `OnStackChanged`에 해당한다.
- 빠진 것은 두 가지다. 하나는 **출처**(`EffectInstance.Source`는 이미 있지만 이벤트에 실리지 않는다)다. 다른 하나는 **면역 차단 알림**이다. `EffectPipeline.ImmuneDropCount`는 internal 카운터일 뿐이다.
- instant 효과는 인스턴스가 없어 lifecycle 이벤트가 나지 않는다(`EffectPipeline.cs`의 `CreateInstance`만 `Applied`를 낸다). GAS의 `...AppliedDelegateToSelf`와 달리 Bun3에서 instant 적용은 `RaiseEvents`로 선언된 `GameplayEvent`와 `AttributeChange`로만 드러난다.

## 4. GAS — Gameplay Event("누가 누구를 얼마나")

- **이벤트 데이터 구조.** `FGameplayEventData`는 "Metadata for a tag-based Gameplay Event"다. 필드는 `EventTag`, `Instigator`, `Target`, `EventMagnitude`(float), `InstigatorTags`, `TargetTags`, `ContextHandle`, `TargetData`, `OptionalObject`/`OptionalObject2`다. [FGameplayEventData](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/FGameplayEventData)
- **처리 진입점.** `virtual int32 HandleGameplayEvent(FGameplayTag EventTag, const FGameplayEventData* Payload)`는 이벤트를 실행하고 트리거된 어빌리티 활성화 수를 반환한다. [HandleGameplayEvent](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/UAbilitySystemComponent/HandleGameplayEvent)
- **외부 주입.** `SendGameplayEventToActor(Actor, EventTag, Payload)`는 "trigger an ability on the actor in question with useful payload data"다. [UAbilitySystemBlueprintLibrary](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/UAbilitySystemBlueprintLibrary)
- **게임 코드 구독.** `TMap<FGameplayTag, FGameplayEventMulticastDelegate> GenericGameplayEventCallbacks`는 "these only activate if the exact tag is used"다. 즉 태그별 멀티캐스트 맵이고 **정확 일치만** 받는다. [UAbilitySystemComponent](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/UAbilitySystemComponent)
- **어빌리티 내부 대기.** `UAbilityTask_WaitGameplayEvent`는 `OnlyTriggerOnce`, `OnlyMatchExact`(계층/정확 선택) 플래그를 가진다. [UAbilityTask_WaitGameplayEvent](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/UAbilityTask_WaitGameplayEvent)
- **공식 문서가 말하는 이벤트의 용도는 어빌리티 활성화 추상화다.** "fires all abilities with a matching Ability Trigger". 전투 로그용 채널이라는 설명은 없다. [Understanding GAS](https://dev.epicgames.com/documentation/en-us/unreal-engine/understanding-the-unreal-engine-gameplay-ability-system)

**Lyra(공식 문서 범위)**

- **데미지 숫자.** 데미지 GE → execution이 Health를 깎는다 → `GameplayCue.Character.DamageTaken` → `GCN_Character_DamageTaken`이 "only displayed if the effect instigator is a local player"로 표시한다. 크기는 cue의 raw magnitude, 위치는 hit result에서 온다. 숫자 표시는 로컬 컨트롤러의 `ULyraNumberPopComponent`가 풀링해 Niagara로 그린다.
- **사망 알림.** `ULyraHealthSet`이 체력 소진 시 `FLyraAttributeEvent` 델리게이트를 쏘고 `ULyraHealthComponent`가 구독한다.
- **쿨다운 UI.** `GA_Grenade`는 남은 쿨다운을 "Gameplay Message Subsystem"으로 브로드캐스트한다.

출처: [Abilities in Lyra](https://dev.epicgames.com/documentation/en-us/unreal-engine/abilities-in-lyra-in-unreal-engine)

- **확인 불가:** Lyra의 verb message 구조체, kill feed(elimination) 흐름, `GameplayMessageSubsystem` API. 공식 문서에 설명이 없고 소스는 접근하지 못했다.

## 5. GAS — Attribute 변경 알림

- **구독 API.** `FOnGameplayAttributeChange& GetGameplayAttributeValueChangeDelegate(FGameplayAttribute)`는 "Register for when an attribute value changes"다. attribute별 멀티캐스트다. [UAbilitySystemComponent](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/UAbilitySystemComponent)
- **페이로드.** `FOnAttributeChangeData`는 "Temporary parameter struct"이고 `Attribute`, `OldValue`, `NewValue`, `GEModData`를 담는다. `GEModData`는 `const FGameplayEffectModCallbackData*`로, 원인이 된 `EffectSpec`, `EvaluatedData`, `Target`을 가리킨다. 이 포인터는 GE 실행으로 바뀐 경우에만 의미가 있다. 이는 이름과 타입에서 나온 추론이고, 공식 주석은 없다. [FOnAttributeChangeData](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/FOnAttributeChangeData), [FGameplayEffectModCallbackData](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/FGameplayEffectModCallbackData)
- **AttributeSet 훅.** `PreGameplayEffectExecute`/`PostGameplayEffectExecute`로 수정을 거부·변경하거나 반응할 수 있다. [Gameplay Effects](https://dev.epicgames.com/documentation/en-us/unreal-engine/gameplay-effects-for-the-gameplay-ability-system-in-unreal-engine)
- **UI 용도.** Attribute는 일반 UProperty처럼 복제되며 "safe for local UI visualizations such as enemy health bars"라고 문서에 적혀 있다. [Understanding GAS](https://dev.epicgames.com/documentation/en-us/unreal-engine/understanding-the-unreal-engine-gameplay-ability-system)

Bun3의 `AttributeChange(AttributeId, OldCurrent, NewCurrent)` 드레인 버퍼는 `OldValue/NewValue`와 같은 모양이다. 원인(`GEModData`)만 없다.

## 6. GAS — 쿨다운 잔여 시간

`UGameplayAbility`는 다음을 공개한다. [UGameplayAbility](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/UGameplayAbility)

- `float GetCooldownTimeRemaining()`: "time in seconds remaining on the currently active cooldown."
- `GetCooldownTimeRemainingAndDuration(Handle, ActorInfo, float& TimeRemaining, float& CooldownDuration)`: 남은 시간과 "the original duration"을 **out 파라미터**로 반환한다.
- `const FGameplayTagContainer* GetCooldownTags()`: "all tags that can put this ability into cooldown."
- `UGameplayEffect* GetCooldownGameplayEffect()`: "the gameplay effect used to determine cooldown."
- `CheckCooldown(..., OptionalRelevantTags)`: 부울 판정이며 실패 태그를 채운다.

`GetCooldownTags`가 "can put this ability into cooldown"이라고 말한다. 이는 쿨다운이 **특정 GE 하나가 아니라 태그 집합으로 판정됨**을 뜻한다. 다른 GE가 같은 쿨다운 태그를 줘도 쿨다운이다. 다만 `GetCooldownTimeRemaining`의 내부 계산이 이 태그로 활성 효과를 조회하는지는 엔진 소스를 열지 못해 **확인하지 못했다.** 참고로 오픈소스 셋은 모두 "쿨다운 GE의 부여 태그로 활성 효과를 스캔해 최댓값"을 구현한다(§9).

## 7. GAS — 부여 어빌리티 열거와 활성화 콜백

- **열거.** `TArray<FGameplayAbilitySpec>& GetActivatableAbilities()`("the list of all activatable abilities")와 `FindAbilitySpecFromHandle(Handle, ConsiderPending)`가 있다. [UAbilitySystemComponent](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/UAbilitySystemComponent)
- **스펙 내용.** `FGameplayAbilitySpec`은 `Handle`("for outside sources to refer to this spec by"), `Ability`(CDO), `Level`, `InputID`, `SourceObject`, `ActiveCount`, `GameplayEffectHandle`("Handle to GE that granted us")과 `IsActive()`를 갖는다. [FGameplayAbilitySpec](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/FGameplayAbilitySpec)
- **부여/회수 알림은 델리게이트가 아니다.** `virtual void OnGiveAbility(FGameplayAbilitySpec&)`는 "called from GiveAbility or from OnRep"인 서브클래스 훅이다. [OnGiveAbility](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/UAbilitySystemComponent/OnGiveAbility). `OnRemoveAbility` 개별 페이지는 렌더링되지 않았다.
- **생명주기 콜백(ASC 멤버).**
  - `AbilityActivatedCallbacks`: "anytime an ability is activated (started)"
  - `AbilityCommittedCallbacks`: "(cost/cooldown applied)"
  - `AbilityEndedCallbacks`
  - `OnAbilityEnded`: "with extra information", 즉 `FAbilityEndedData`의 `AbilitySpecHandle`, `bWasCancelled` 등
  - `AbilityFailedCallbacks`: "Called with a failure reason when an ability fails to activate"

  출처: [UAbilitySystemComponent](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/UAbilitySystemComponent), [FAbilityEndedData](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/FAbilityEndedData)
- **실패 사유는 enum이 아니라 태그다.** `UAbilitySystemGlobals`가 설정 가능한 태그를 둔다.
  - `ActivateFailCooldownTag`: "on cooldown"
  - `ActivateFailCostTag`
  - `ActivateFailTagsBlockedTag`
  - `ActivateFailTagsMissingTag`
  - `ActivateFailIsDeadTag`
  - `ActivateFailNetworkingTag`

  출처: [UAbilitySystemGlobals](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/UAbilitySystemGlobals)

GAS는 활성화가 비동기이고 네트워크 예측을 거치므로 결과가 콜백으로 나올 수밖에 없다. Bun3의 `TryActivateAbility`는 동기이고 `AbilityActivationResult` enum이 같은 사유 집합을 반환한다(Cooldown은 `BlockedByTag`로 보고). 따라서 호출자 관점에서는 이미 동등하다.

## 8. GAS — GameplayCue: 코스메틱 채널

- **용도는 코스메틱 전용이다.** "Gameplay Cues should be used for cosmetic feedback only. For gameplay-relevant feedback that needs to be replicated to all clients, you should rely on Ability Tasks". [Understanding GAS](https://dev.epicgames.com/documentation/en-us/unreal-engine/understanding-the-unreal-engine-gameplay-ability-system)
- **복제는 비신뢰다.** "Gameplay Cues do not use reliable replication, so there is a chance that some clients do not receive the Cue". **이것이 로직과 분리하는 이유다.** 도달이 보장되지 않는 채널에 게임 상태를 걸 수 없다. [Understanding GAS](https://dev.epicgames.com/documentation/en-us/unreal-engine/understanding-the-unreal-engine-gameplay-ability-system)
- **식별자는 태그다.** 태그는 `GameplayCue.` 접두를 쓰고, GE가 태그를 매칭해 cue를 자동 적용한다. 공식 문서는 cue를 "a network-efficient way to manage cosmetic effects"라고 부른다. [Gameplay Effects](https://dev.epicgames.com/documentation/en-us/unreal-engine/gameplay-effects-for-the-gameplay-ability-system-in-unreal-engine)
- **이벤트 종류(`EGameplayCueEvent`).** [EGameplayCueEvent](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/EGameplayCueEvent__Type)
  - `OnActive`: 지속 cue가 처음 활성화될 때. "only be called if the client witnessed the activation"
  - `WhileActive`: "first seen as active, even if it wasn't actually just applied (Join in progress, etc)"
  - `Executed`: "instant effects or periodic ticks"
  - `Removed`
- **핸들러 두 종류.**
  - `UGameplayCueNotify_Static`: "non instantiated UObject … useful for one-off 'burst' effects"
  - `AGameplayCueNotify_Actor`: 인스턴스형이다. `bUniqueInstancePerInstigator`/`bUniqueInstancePerSourceObject`로 인스턴스 분리를 정하고, `Recycle`/`ReuseAfterRecycle`로 풀링한다. `IsOverride`로 부모 태그 cue 호출을 막는다.

  출처: [UGameplayCueNotify_Static](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/UGameplayCueNotify_Static), [AGameplayCueNotify_Actor](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/AGameplayCueNotify_Actor)
- **파라미터.** `FGameplayCueParameters`는 `Instigator`, `EffectCauser`("can be a weapon or projectile"), `SourceObject`, `RawMagnitude`, `NormalizedMagnitude`, `Location`/`Normal`(양자화 벡터), `GameplayEffectLevel`/`AbilityLevel`, `AggregatedSource/TargetTags`, `EffectContext`, `bGameplayEffectActive`를 담는다. [FGameplayCueParameters](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/FGameplayCueParameters)
- **배치 처리.** `UGameplayCueManager`는 대기 cue를 `FlushPendingCues`("Send out any pending cues")로 내보낸다. `OnFlushPendingCues`는 "useful for custom batching"이고, `ShouldSuppressGameplayCues`도 있다. 즉 엔진 안에서도 cue는 **즉시 호출이 아니라 모았다가 내보내는 경로**를 갖는다. [UGameplayCueManager](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/UGameplayCueManager)
- **복제 모드와 관찰 가능 범위.** `EGameplayEffectReplicationMode::Mixed`는 "minimal gameplay effect info to simulated proxies but full info to owners"를 보낸다. 따라서 **타 플레이어의 활성 효과 목록은 클라이언트에서 관찰 불가일 수 있고**, 그 경우 태그와 cue가 관찰 수단이 된다. [EGameplayEffectReplicationMode](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/EGameplayEffectReplicationMode)

## 9. 오픈소스 프레임워크

선정 기준은 읽을 수 있는 소스와 GitHub 스타 수(2026-09-29 기준)다. 스타 수는 실사용 규모를 보장하지 않는다.

### 9.1 sjai013/unity-gameplay-ability-system (C#, ★1083, archived) — 순수 pull

- **효과·어빌리티 조회.** `AbilitySystemCharacter`가 `List<GameplayEffectContainer> AppliedGameplayEffects`와 `List<AbstractAbilitySpec> GrantedAbilities`를 **public 필드**로 노출한다. 알림은 없고, 만료 제거는 `RemoveAll` 람다로 조용히 일어난다. [AbilitySystemCharacter.cs L16-17, L189-192](https://github.com/sjai013/unity-gameplay-ability-system/blob/725f1abd149e22c02fb51fefe7899aabb3f963d7/Packages/com.sjai013.abilitysystem/Runtime/ability-system/Components/AbilitySystemCharacter.cs#L16-L17)
- **Attribute 변경.** "이벤트"는 `AttributeSystemComponent.LateUpdate`가 이전 값 리스트와 현재 값 리스트를 `AbstractAttributeEventHandler.PreAttributeChange(prev, ref current)`에 넘기는 **diff 방식**이다. [AttributeSystemComponent.cs L179-194](https://github.com/sjai013/unity-gameplay-ability-system/blob/725f1abd149e22c02fb51fefe7899aabb3f963d7/Packages/com.sjai013.abilitysystem/Runtime/attribute-system/Components/AttributeSystemComponent.cs#L179-L194)
- **데미지 숫자와 HP 바.** 샘플의 데미지 숫자는 이 diff로 `currentValue - prevValue`만 계산해 표시한다. 출처(가해자)는 없다. HP 바는 매 `LateUpdate` 폴링이다. [ShowDamageNumbersEventHandler.cs](https://github.com/sjai013/unity-gameplay-ability-system/blob/725f1abd149e22c02fb51fefe7899aabb3f963d7/Assets/My%20Gameplay%20Ability%20System/Attributes/ShowDamageNumbersEventHandler.cs#L16-L32), [UIAttributeUpdater.cs](https://github.com/sjai013/unity-gameplay-ability-system/blob/725f1abd149e22c02fb51fefe7899aabb3f963d7/Assets/My%20Gameplay%20Ability%20System/Attributes/UIAttributeUpdater.cs#L23-L35)
- **쿨다운.** `CheckCooldown()`은 `AbilityCooldownTime { TimeRemaining, TotalDuration }`을 반환한다. 쿨다운 GE의 `GrantedTags`를 가진 활성 효과를 스캔해 가장 긴 잔여를 고르고, Infinite면 `float.MaxValue`를 쓴다. 효과 스펙이 `DurationRemaining`/`TotalDuration`/`Source`/`Target`을 들고 있어 가능하다. [AbstractAbilitySpec.cs L6-10, L90-133](https://github.com/sjai013/unity-gameplay-ability-system/blob/725f1abd149e22c02fb51fefe7899aabb3f963d7/Packages/com.sjai013.abilitysystem/Runtime/ability-system/Authoring/AbstractAbilitySpec.cs#L90-L133), [GameplayEffectSpec.cs L18-24](https://github.com/sjai013/unity-gameplay-ability-system/blob/725f1abd149e22c02fb51fefe7899aabb3f963d7/Packages/com.sjai013.abilitysystem/Runtime/ability-system/GameplayEffectSpec.cs#L18-L24)
- **교훈.** pull만으로 버프 바·쿨다운·HP 바는 충분하다. 반면 **전이 이벤트가 없으면 "누가 때렸나"와 "막 만료됨"을 복원할 수 없다.** diff로는 크기만 알 수 있다.

### 9.2 No78Vino/gameplay-ability-system-for-unity "EX-GAS" (C#, ★847) — dirty 신호 + pull

- **효과 목록.** `GameplayEffectContainer`는 payload 없는 `event Action OnGameplayEffectContainerIsDirty`와 `List<GameplayEffectSpec> GameplayEffects()`를 짝으로 둔다. 추가·제거·스택 변경·활성 상태 갱신 때마다 dirty만 쏘고, 구독자는 목록을 다시 읽는다. [GameplayEffectContainer.cs L17-22, L113-114, L138-145](https://github.com/No78Vino/gameplay-ability-system-for-unity/blob/801735292820a6de9dd1abbac0e2139025840a61/Assets/GAS/Runtime/Effects/GameplayEffectContainer.cs#L17-L22)
- **세부 알림.** 스펙별 `event Action<int,int> onStackCountChanged`(old, new)가 있고, 스펙은 `Source`, `StackCount`, `DurationRemaining()`을 공개한다. [GameplayEffectSpec.cs](https://github.com/No78Vino/gameplay-ability-system-for-unity/blob/801735292820a6de9dd1abbac0e2139025840a61/Assets/GAS/Runtime/Effects/GameplayEffectSpec.cs#L21)
- **Attribute 변경.** `RegisterPostCurrentValueChange(Action<AttributeBase, float, float>)` 같은 attribute별 콜백이 있다. [AttributeBase.cs L13-16](https://github.com/No78Vino/gameplay-ability-system-for-unity/blob/801735292820a6de9dd1abbac0e2139025840a61/Assets/GAS/Runtime/Attribute/AttributeBase.cs#L13-L16)
- **쿨다운.** `CheckCooldownFromTags`가 `CooldownTimer { TimeRemaining, Duration }`을 반환한다(Infinite는 `-1`). 알고리즘은 sjai013과 같다. [GameplayEffectContainer.cs L167-198](https://github.com/No78Vino/gameplay-ability-system-for-unity/blob/801735292820a6de9dd1abbac0e2139025840a61/Assets/GAS/Runtime/Effects/GameplayEffectContainer.cs#L167-L198)
- **활성화 결과.** 활성화 결과는 enum `AbilityActivateResult { Success, FailHasActivated, FailTagRequirement, FailCost, FailCooldown, FailOtherReason }`이고, `AbilitySpec.RegisterActivateResult(Action<AbilityActivateResult>)`로도 받는다. [AbilityActivateResult.cs](https://github.com/No78Vino/gameplay-ability-system-for-unity/blob/801735292820a6de9dd1abbac0e2139025840a61/Assets/GAS/Runtime/Ability/AbilityActivateResult.cs), [AbilitySpec.cs L50-60](https://github.com/No78Vino/gameplay-ability-system-for-unity/blob/801735292820a6de9dd1abbac0e2139025840a61/Assets/GAS/Runtime/Ability/AbilitySpec.cs#L50-L60)
- **Cue.** Cue는 스펙 내부에서 동기 호출된다. 지속 cue 스펙은 `OnAdd/OnRemove/OnGameplayEffectActivate/OnGameplayEffectDeactivate/OnTick`을 받는다. 파라미터 구조체는 `object[] customArguments`를 갖고, 나머지(Magnitude, EffectContext 등)는 주석으로 남긴 미구현 상태다. [GameplayCueDurational.cs](https://github.com/No78Vino/gameplay-ability-system-for-unity/blob/801735292820a6de9dd1abbac0e2139025840a61/Assets/GAS/Runtime/Cue/Base/GameplayCueDurational.cs), [GameplayCueParameters.cs](https://github.com/No78Vino/gameplay-ability-system-for-unity/blob/801735292820a6de9dd1abbac0e2139025840a61/Assets/GAS/Runtime/Cue/Base/GameplayCueParameters.cs)
- **교훈.** "변했다"는 신호만 주고 내용은 조회로 읽게 하면 이벤트 페이로드 설계를 피할 수 있다. 결국 UI 갱신 트리거(dirty)와 표시 데이터(조회)가 분리된다.

### 9.3 MaiKuraki/UnityStarter — CycloneGames.GameplayAbilities (C#, ★796) — 무할당 push

- **ASC 이벤트.** ASC가 GAS 대응 이벤트를 C# `event`로 노출한다. [AbilitySystemComponent.cs L984-1079](https://github.com/MaiKuraki/UnityStarter/blob/5138a2b7c4b4481051cd03a20e6ad3783c807d1b/UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.GameplayAbilities/Runtime/AbilitySystemComponent.cs#L984-L1079)
  - 효과: `OnGameplayEffectAppliedToSelf`, `OnGameplayEffectRemovedFromSelf`
  - cue: `OnGameplayCueCommitted`
  - 어빌리티: `OnAbilityActivated`, `OnAbilityEndedEvent`, `OnAbilityCommitted`
  - 태그별 이벤트 맵(`Dictionary<GameplayTag, GASCallbackList<...>>`)
- **콜백 저장소.** 모든 구독은 `GASCallbackList<T>`에 들어간다. 주석은 "allocation-free steady-state dispatch"라고 적는다. 디스패치 중 추가는 다음 디스패치부터 보이고, 제거는 tombstone으로 표시했다가 최외곽 디스패치가 끝나면 압축한다. 구독과 해지는 `GetInvocationList()`를 호출하므로 **할당은 구독 시점에 몰린다.** [GASCallbackList.cs](https://github.com/MaiKuraki/UnityStarter/blob/5138a2b7c4b4481051cd03a20e6ad3783c807d1b/UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.GameplayAbilities/Runtime/GASCallbackList.cs)
- **Cue 관찰.** `GameplayCueCommitted`는 `readonly struct`이고 `in` 파라미터 델리게이트로 전달된다. 주석은 "Immutable, post-commit … contains no presentation objects and is valid only for the synchronous callback"이다. 필드는 `Cue`, `Event`, `Source`, `Target`, `EffectDefinition`, `EffectLevel`, `EffectDurationRaw`, `PredictionKey`, `ActiveEffectReconciliationId`, `StateVersion`이다. 디스패치는 **상태 커밋 이후**에 일어나며 관찰자마다 try/catch로 격리한다("an exception cannot affect the committed effect"). [GameplayCueCommitted.cs](https://github.com/MaiKuraki/UnityStarter/blob/5138a2b7c4b4481051cd03a20e6ad3783c807d1b/UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.GameplayAbilities/Runtime/GameplayCueCommitted.cs), [NotifyGameplayCuesCommitted](https://github.com/MaiKuraki/UnityStarter/blob/5138a2b7c4b4481051cd03a20e6ad3783c807d1b/UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.GameplayAbilities/Runtime/AbilitySystemComponent.cs#L5653-L5740)
- **조회.**
  - `ActiveEffects`와 `GetActivatableAbilities()`는 `GASReadOnlyListView<T>`를 반환한다. 구조체 enumerator와 런타임 스레드 assert를 포함한다. [AbilitySystemComponent.cs L281-316](https://github.com/MaiKuraki/UnityStarter/blob/5138a2b7c4b4481051cd03a20e6ad3783c807d1b/UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.GameplayAbilities/Runtime/AbilitySystemComponent.cs#L281-L316), [GASReadOnlyCollectionViews.cs](https://github.com/MaiKuraki/UnityStarter/blob/5138a2b7c4b4481051cd03a20e6ad3783c807d1b/UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.GameplayAbilities/Runtime/GASReadOnlyCollectionViews.cs)
  - `ActiveGameplayEffect`는 풀링되는 lease 객체다("Released instances are invalidated and discarded"). 남은 시간은 고정소수 `TimeRemainingRaw`로도 노출한다. [ActiveGameplayEffect.cs](https://github.com/MaiKuraki/UnityStarter/blob/5138a2b7c4b4481051cd03a20e6ad3783c807d1b/UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.GameplayAbilities/Runtime/ActiveGameplayEffect.cs#L9-L28)
- **쿨다운.** `GetCooldownTimeRemaining(ability)`와 `GetCooldownInfo(ability, out timeRemaining, out totalDuration)`가 있다. 부여 태그 인덱스 → 효과 목록 맵(`grantedTagIndexToEffects`)으로 스캔 범위를 줄인다. [AbilitySystemComponent.Cooldown.cs](https://github.com/MaiKuraki/UnityStarter/blob/5138a2b7c4b4481051cd03a20e6ad3783c807d1b/UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.GameplayAbilities/Runtime/AbilitySystemComponent.Cooldown.cs)
- **교훈.** push를 무할당·결정적으로 유지하려면 재진입 규칙, 예외 격리, 스레드 소유 검사, post-commit 시점 보장이 **모두** 필요하다. 이것이 push의 실제 비용이다.

### 9.4 PhysaliaStudio/Flexi (C#, ★245) — 큐 드레인 + 게임 구현 resolver

- **이벤트 흐름.** `FlexiCore.EnqueueEvent(IEventContext)`는 이벤트를 큐에 쌓는다. 러너는 `TriggerCachedEvents`에서 **페이즈 경계에 한꺼번에 드레인**하며 `IFlexiEventResolver.ResolveEvent`를 부른다. 게임은 multicast 구독 대신 resolver 인터페이스 하나(`OnEventReceived`, `ResolveEvent`)와 `IFlexiRunnerResolver.OnFlowFinished`를 구현한다. [FlexiCore.cs L6-26, L284-303](https://github.com/PhysaliaStudio/Flexi/blob/51da089e62d7b9bea03ddd7fa087078ecef2254d/Packages/studio.physalia.flexi/Runtime/FlexiCore.cs#L284-L303), [AbilityEventQueue.cs](https://github.com/PhysaliaStudio/Flexi/blob/51da089e62d7b9bea03ddd7fa087078ecef2254d/Packages/studio.physalia.flexi/Runtime/Internal/AbilityEventQueue.cs)
- **할당 비용.** 이벤트는 마커 인터페이스 `IEventContext`를 구현하는 게임 정의 **클래스**여서 이벤트마다 할당된다. 큐의 `Dequeue`도 `RemoveAt(0)`이다. [Contexts.cs](https://github.com/PhysaliaStudio/Flexi/blob/51da089e62d7b9bea03ddd7fa087078ecef2254d/Packages/studio.physalia.flexi/Runtime/Runner/Contexts.cs)
- **교훈.** "지연 큐 + 페이즈 경계 드레인 + 도메인 지식은 게임 구현 인터페이스로"라는 형태는 Bun3의 기존 방침(확장점, 페이즈 경계, 새 큐 금지)과 같은 계열이다. 페이로드는 값 타입이어야 한다.

### 9.5 비교

| 관점 | GAS | sjai013 | EX-GAS | CycloneGames | Flexi | Bun3 현행 |
|---|---|---|---|---|---|---|
| 활성 효과 조회 | 쿼리 → 핸들 배열/const 포인터 | public List | List 반환 | 읽기 전용 뷰 + lease 객체 | — | `ActiveEffectCount`만 |
| 적용/제거 알림 | multicast, 컨텍스트 포함 | 없음 | dirty 신호 | multicast, 효과 객체 | resolver | **드레인 버퍼**, 출처 없음 |
| 전투 이벤트 | 태그별 multicast(정확 일치) | 없음 | 없음 | 태그별 콜백 | 큐 + resolver | 내부 체인 전용 |
| Attribute 변경 | attribute별 multicast + 원인 | 프레임 diff | attribute별 콜백 | 태스크/콜백 | — | **드레인 버퍼** |
| 쿨다운 | (남은, 전체) out | (남은, 전체) struct | (남은, 전체) struct | (남은, 전체) out | — | 없음 |
| 코스메틱 | GameplayCue(비신뢰 복제) | — | 동기 cue 호출 | post-commit cue 이벤트 | — | 없음 |

## 10. Push와 Pull — 결정론·무할당 시뮬레이션 관점

| 기준 | Push(멀티캐스트 델리게이트) | Pull(드레인 버퍼 + 조회) |
|---|---|---|
| 재진입 | 콜백이 시뮬을 건드리면 순회 중 변경·무한 연쇄가 생긴다. 막으려면 tombstone과 post-commit 규칙이 필요하다(CycloneGames) | 읽기 시점이 틱 밖이라 구조적으로 재진입이 없다 |
| 결정론 | 구독 순서가 곧 부수효과 순서다. 서버와 클라이언트의 구독자 구성이 다르면 콜백이 시뮬을 건드리는 순간 갈라진다 | 버퍼 순서는 시뮬이 정한 발행 순서로 고정된다(Id 오름차순 등) |
| 할당 | C# 델리게이트 결합/해지는 할당한다. 람다는 클로저를 만든다. steady-state 무할당은 별도 리스트 구현이 필요하다 | 값 타입 배열의 amortized 성장만 있다(기존 `Array.Resize` 패턴) |
| 예외 | 관찰자 예외가 시뮬 중간에 전파된다. 격리하려면 try/catch가 필요하다 | 시뮬 밖에서 터지므로 시뮬 상태가 오염되지 않는다 |
| 지연 | 즉시 | 최대 1틱. 틱 단위 시뮬에서는 문제되지 않는다 |
| 누락/늦은 관찰자 | 구독 전 이벤트는 사라진다(GAS가 `WhileActive`를 따로 둔 이유) | 조회 API로 현재 상태를 재구성할 수 있다 |
| 소비자 다수 | 자연스럽다 | **누가 비우는가**가 문제다. 소비자가 여럿이면 Clear 소유권 계약이 필요하다 |
| 필터링 | 태그별 구독 맵 | 소비자가 드레인하며 필터링한다(태그 계층 검사는 `TagCatalog`) |

결론: 시뮬이 결정적이고 틱 기반이며 서버와 Unity가 같은 코드를 돌리는 Bun3에서는 pull이 기본값이다. push가 주는 편의(태그별 구독, 즉시성)는 **게임 쪽 또는 Unity 계층의 어댑터가 드레인 결과 위에 얹으면 된다.** 프레임워크 코어가 델리게이트를 들고 있을 이유가 없다. GAS조차 cue는 `FlushPendingCues`로 모아서 내보낸다(§8).

## 11. Bun3 적용 제안

아래는 공식 동작과 소스에서 도출한 **Bun3 설계 제안**이며, 스펙 확정 전 초안이다.

| # | 주제 | GAS 대응 | 판정 | Bun3 형태 |
|---|---|---|---|---|
| 1 | 활성 효과 열거 | `GetActiveEffects` / `GetActiveGameplayEffect` | **변형** | 인덱스 접근 + 값 복사 `ActiveEffectInfo` |
| 2 | 적용/제거 알림 | Applied/Removed 델리게이트 + `FGameplayEffectRemovalInfo` | **변형** | 기존 드레인 버퍼에 `Source`, `Level` 추가 |
| 3 | 전투 이벤트 | `FGameplayEventData` + `GenericGameplayEventCallbacks` | **변형** | `EffectPipeline.PendingGameplayEvents` 드레인 버퍼 |
| 4 | Attribute 변경 | `FOnAttributeChangeData` | **그대로(현행 유지)** | `PendingChanges` 유지, 원인 필드 미추가 |
| 5 | 쿨다운 잔여 | `GetCooldownTimeRemainingAndDuration` | **그대로(형태)** | `World.TryGetCooldown(unit, ability, out remaining, out total)` |
| 6 | 부여 어빌리티 열거 | `GetActivatableAbilities` / `FGameplayAbilitySpec` | **변형** | `AbilitySet` 인덱스 접근 + `Version` 카운터 |
| 7 | 활성화 결과/실패 사유 | `AbilityFailedCallbacks` + 실패 태그 | **미채택** | 동기 `AbilityActivationResult` 유지 |
| 8 | GameplayCue | cue 매니저 + Notify | **원칙만 채택** | 코어에 cue 없음. 네 이벤트를 드레인 + 조회로 재구성 |

### 11.1 활성 효과 열거 (변형)

`EffectInstance`는 static 풀에 반환되는 객체다(`EffectInstance.Return`). 이를 public 컬렉션으로 노출하면 반환 후 재사용된 인스턴스를 관찰자가 들고 있을 수 있다. 이는 CycloneGames가 lease 무효화로 막는 문제와 같다. GAS의 const 포인터 방식도 같은 수명 계약을 요구한다. 그래서 **값 복사 구조체**를 인덱스로 준다.

```csharp
// EffectTarget
public int ActiveEffectCount { get; }                          // existing
public ActiveEffectInfo GetActiveEffect(int index);            // ascending instance Id order
public bool TryGetActiveEffect(ulong instanceId, out ActiveEffectInfo info);

public readonly struct ActiveEffectInfo
{
    public ulong InstanceId { get; }
    public int EffectId { get; }
    public TargetId Source { get; }
    public int Level { get; }
    public int Stack { get; }
    public int RemainingTicks { get; }   // Duration only; -1 otherwise
    public int DurationTicks { get; }    // see open question 1
    public bool Enabled { get; }
}
```

- `for (i < ActiveEffectCount) GetActiveEffect(i)` 루프는 할당이 없고 순서가 결정적이다(이미 Id 오름차순이다).
- `FGameplayEffectQuery`는 들이지 않는다. 태그·효과 id 필터는 소비자 루프의 `if` 한 줄이면 된다. 쿼리 객체는 할당과 델리게이트(`CustomMatchDelegate`)를 끌고 온다.
- 이 조회는 선택이 아니라 필수다. `RestoreSnapshot`은 "a restore is unobservable"이라 이벤트를 내지 않는다. 그래서 스냅샷 복원, 늦은 합류, 씬 재진입 뒤 버프 바와 지속 VFX를 다시 세우는 유일한 경로가 이 조회다. 이것이 GAS `WhileActive`의 역할이다.

### 11.2 적용/제거 알림 (변형)

`EffectLifecycleEvent`에 `TargetId Source`와 `int Level`을 추가한다. 둘 다 값 필드라 무할당이 유지된다. `Expired`/`RemovedPrematurely`는 GAS의 `bPrematureRemoval`에 이미 대응한다.

- 면역 차단(`OnImmunityBlockGameplayEffectDelegate` 대응)은 인스턴스가 없어 이 버퍼에 넣기 어렵다. 필요성부터 확인한다(질문 3).
- instant 효과 적용은 lifecycle에 넣지 않는다. "맞았다"는 §11.3의 GameplayEvent가 담당한다. 이는 GAS에서 instant가 cue `Executed`로 흐르는 것과 같은 분리다.

### 11.3 전투 이벤트 드레인 버퍼 (변형)

`GameplayEvent(Tag, Source, Target, Magnitude, NominalMagnitude)`는 이미 GAS `FGameplayEventData`의 핵심 필드(`EventTag`, `Instigator`, `Target`, `EventMagnitude`)와 같은 모양이다. 공개 경로만 없다. `RaiseGameplayEvent`에서 체인 디스패치와 함께 버퍼에 한 줄 적재한다.

```csharp
// EffectPipeline
public ReadOnlySpan<GameplayEvent> PendingGameplayEvents { get; }
public void ClearGameplayEvents();
```

- **버퍼는 파이프라인 단위 하나로 둔다.** 이벤트는 source와 target 두 당사자를 가지므로 target별 버퍼는 가해자 관점 조회(GAS `...ToTarget`)를 어렵게 한다. 발행 순서가 곧 결정적 순서다.
- **태그별 구독 맵(`GenericGameplayEventCallbacks`)은 들이지 않는다.** 소비자는 드레인하며 `TagCatalog.IsAncestorOrSelf`로 계층 필터링한다. GAS 맵은 정확 일치만 받는데, 계층 필터가 더 유용하다.
- **이 버퍼가 전투 로그, 데미지 숫자, 킬 피드, 네트워크 브로드캐스트의 단일 원천이다.** GAS/Lyra는 이 역할을 cue와 메시지 서브시스템에 나눠 맡겼다. Bun3는 시뮬 산출물 하나로 모으고 표현 분배는 게임에 맡긴다.
- 발행은 저작 데이터 `RaiseEvents`로 선언된 attribute에서만 일어난다. 모든 피해를 로그에 남기려면 저작 규칙이 필요하다(질문 4).
- `SendGameplayEventToActor`처럼 게임 코드가 이벤트를 **주입**하는 경로는 이번 범위(관찰) 밖이다.

### 11.4 Attribute 변경 (현행 유지)

`AttributeSet.PendingChanges`(`AttributeId`, `OldCurrent`, `NewCurrent`)는 GAS `OldValue/NewValue`와 같다. `GEModData` 같은 원인 필드는 넣지 않는다. Current는 여러 modifier 집계와 clamp 전파로 재계산되므로 단일 원인이 없다. 원인이 필요한 소비자(데미지 숫자)는 §11.3 GameplayEvent를 쓴다. HP 바는 마지막 `NewCurrent`만 쓰면 된다.

### 11.5 쿨다운 잔여 (형태 그대로)

GAS와 오픈소스 셋 모두 (남은, 전체) 쌍을 쓴다. 단위만 초 대신 틱(`int`)으로 한다. 결정적이며, 초 변환은 표현 계층이 한다.

```csharp
// World
public bool TryGetCooldown(TargetId unitId, int abilityId, out int remainingTicks, out int durationTicks);
```

- 구현: `CompiledAbility.CooldownEffectId`와 같은 `EffectId`를 가진 활성 인스턴스를 찾아 최대 `RemainingTicks`를 고른다. 없으면 false, 쿨다운 효과가 없는 어빌리티도 false다. 루프 한 번이고 할당이 없다.
- **주의(판정 불일치).** 실제 게이트는 `BlockedTags` 검사다. 다른 효과가 같은 쿨다운 태그를 주면(공유 쿨다운, 침묵) 발동은 막히는데, 이 조회는 0을 보고한다. GAS의 `GetCooldownTags`와 오픈소스 셋은 **태그 기준**으로 스캔한다. 어느 쪽을 정본으로 삼을지는 질문 2로 넘긴다.
- `AbilityCatalog`의 internal 정보(이름, 쿨다운 효과 id, 비용) 중 UI에 필요한 것은 `GetName(int)` 같은 읽기 전용 접근자로 연다. 이름은 Build 때 만든 문자열이므로 반환해도 할당이 없다.

### 11.6 부여 어빌리티 열거 (변형)

```csharp
// AbilitySet
public int GrantCount { get; }                  // existing
public AbilityGrantInfo GetGrant(int index);    // (AbilityId, Source, Level), canonical order
public int Version { get; }                     // incremented on Grant/Revoke/RestoreSnapshot
```

- 스킬 바는 `Version`이 바뀐 틱에만 다시 그린다. 이는 EX-GAS의 dirty 이벤트를 pull로 옮긴 형태다. 부여/회수 이벤트 버퍼는 만들지 않는다. GAS도 `OnGiveAbility`를 델리게이트가 아닌 가상 훅으로 두며, UI 용도로는 "바뀌었나 + 지금 목록"이면 충분하다.
- 같은 어빌리티가 여러 origin에서 부여되면 grant 행이 여럿이다. "고유 어빌리티 목록"이 필요하면 소비자가 인접 중복을 건너뛴다. canonical order가 ability id 오름차순이라 인접해 있다.

### 11.7 활성화 결과 (미채택)

GAS의 Activated/Committed/Failed 콜백은 비동기·예측 활성화 때문에 존재한다. Bun3의 `TryActivateAbility`는 동기이고 enum이 실패 사유(쿨다운 포함 `BlockedByTag`)를 돌려준다. 입력 계층은 이미 결과를 안다. 제3자 관찰자(관전 UI, 리플레이 로그)가 생기면 그때 `PendingAbilityActivations` 드레인 버퍼를 검토한다(YAGNI).

### 11.8 코스메틱 채널 (원칙만 채택)

프레임워크는 표현을 모른다. `Bun3.Gameplay`에 cue 매니저, 태그→프리팹 매핑, 풀링을 넣지 않는다. GAS의 네 이벤트는 위 API로 전부 재구성된다.

| GAS cue 이벤트 | Bun3 재구성 |
|---|---|
| `OnActive` | `EffectLifecycleEvent.Applied` |
| `WhileActive` | 스냅샷 복원·합류 후 `GetActiveEffect` 순회 |
| `Executed` | `PendingGameplayEvents`(instant/주기 실행 결과) |
| `Removed` | `Expired` / `RemovedPrematurely` |

태그→VFX/SFX 매핑과 풀링(GAS의 `IsOverride`, 인스턴스 분리 정책 포함)은 Unity 계층 패키지의 몫이다. 두 번째 게임에서도 같은 코드가 된다면 그때 unity 영역 패키지로 내린다.

### 11.9 드레인 버퍼의 소유권 계약

현행은 각 버퍼를 소비자가 직접 `Clear*()`한다. 소비자가 UI, VFX, 네트워크, 로그로 늘어나면 **먼저 비운 쪽이 다른 소비자의 이벤트를 지운다.** 반대로 아무도 비우지 않으면 서버에서 버퍼가 무한히 자란다.

- 권장: 게임 쪽 관찰 디스패처 하나가 모든 버퍼를 읽어 팬아웃하고 한 번에 비운다. 프레임워크는 편의 메서드 `World.ClearPendingEvents()`(모든 유닛의 lifecycle·attribute 버퍼와 파이프라인 GameplayEvent 버퍼)를 제공한다.
- **틱 시작 시 자동 비움은 권하지 않는다.** `TryActivateAbility`는 틱 사이에 호출되어 비용 지불(`AttributeChange`)과 쿨다운 효과의 즉시 적용(`Applied`)을 버퍼에 남긴다. 다음 `Tick()` 진입에서 자동으로 비우면 소비자가 읽기 전에 사라진다.

### 11.10 주인에게 확인할 질문

1. **전체 지속시간(`DurationTicks`)을 인스턴스에 저장할 것인가?** Duration은 레벨·attribute 스케일(`ComputeScaledDuration`)과 DR 배율로 적용 시점마다 달라져 카탈로그에서 복원할 수 없다. 버프 바의 "남은/전체" 게이지와 GAS `GetActiveEffectsTimeRemainingAndDuration`을 따르려면 인스턴스 필드와 `EffectTargetSnapshot` 행이 늘어난다.
2. **쿨다운 조회의 정본은 무엇인가?** 쿨다운 효과 id 기준(단순, 현재 게이트와 불일치 가능)과 태그 기준(GAS/오픈소스와 같고 게이트와 일치, `AbilityDef`에 "쿨다운 태그" 개념 추가 필요) 중 고른다.
3. 면역·DR로 무효화된 적용을 관찰할 필요가 있는가? ("면역!" 팝업) 필요하면 인스턴스 id 없는 `Blocked` 종류를 추가한다.
4. 전투 로그의 완전성이 필요한가? 필요하면 모든 피해 효과가 `RaiseEvents`를 선언하도록 Build 검증이나 저작 규칙을 둔다.
5. §11.9 소유권 계약(게임 디스패처가 한 번에 비움)에 동의하는가? 이 경우 `ClearEffectEvents`/`ClearChanges`의 개별 공개는 유지할 것인가?
6. `EffectInstance`의 static 풀은 CLAUDE.md의 "static mutable state 금지"와 이미 충돌한다(ponytail 주석으로 표시됨). 관찰 API를 값 복사로 설계하면 풀을 World 소유로 옮길 때 공개 API가 바뀌지 않는다. 풀 이전을 이번 슬라이스에 묶을 것인가?
