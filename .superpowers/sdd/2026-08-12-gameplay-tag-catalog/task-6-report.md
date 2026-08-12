# Task 6 실행 보고서

## TDD

- RED: `LegacyTagApiRemovalTests`를 추가한 뒤 실행했다. `TagRegistry`가 exported type으로 남아 있어 예상대로 실패했다.
- GREEN: `TagRegistry`, `TagSet`, 두 테스트와 Unity meta를 삭제하고 `GameplayTag`의 `int` 생성자 및 `Handle` getter를 제거한 뒤 같은 reflection 테스트가 통과했다.

## 무할당 및 구조 계약

- `Tag_queries_do_not_allocate`: warmup 뒤 public `Has`/`Count` 100,000회 batch, checksum 400,000, 측정값 0 B.
- `Reserved_tag_mutations_do_not_allocate`: 예약된 8개의 깊이 16 leaf를 100 cycle add/remove, 최종 exact kind count 0/0, 측정값 0 B.
- 구조 행렬: N=5,000/50,000, M=8/32/64, D=1/4/8/16, exact/parent/miss의 72 query case를 각각 100,000회 실행했다. 모든 batch가 checksum 기대값과 0 B를 만족했고, public query가 공유하는 `HasCore`/`GetCountsCore` lower-bound 비교 수는 각각 <=7/<=11이었다.

## 검증 명령과 결과

```powershell
dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj --nologo --filter "FullyQualifiedName~LegacyTagApiRemovalTests"
# RED: TagRegistry exported type assertion 실패
# GREEN: 1/1 PASS

dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj --nologo --filter "FullyQualifiedName~AllocationSmokeTests|FullyQualifiedName~TagPerformanceContractTests"
# 7/7 PASS (12 s)

dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj --nologo
# 203/203 PASS (13 s)

dotnet build common/src/com.bun3.gameplay/Bun3.Gameplay.csproj --nologo -warnaserror
# warnings 0, errors 0

# exact legacy rg gate
rg -n "TagRegistry|TagSet|GetOrRegister|Handle" common/src/com.bun3.gameplay/Runtime/Tags
# exit 1: 0 matches, accepted by the gate

& 'common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1' -Mode Import
& 'common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1' -Mode EditMode
# both exit 0; EditMode 1/1 PASS
```

Unity EditMode가 `ProjectSettings.asset`의 `SENTIS_ANALYTICS_ENABLED` define만 제거했으며, 검증된 SENTIS drift여서 그 한 줄만 복원했다.

## 변경 및 self-review

- production: `GameplayTag` shim 제거, `TagRegistry`/`TagSet` 및 meta 삭제.
- tests: legacy reflection contract 추가, registry/set tests 삭제, allocation 및 72-case performance matrix 추가.
- staged scope: Task 6 Gameplay tag/test 경로와 이 보고서만 포함한다.
- self-review: public Korean XML의 기존 API는 유지했고, no-allocation 측정은 catalog/array/capacity/warmup 이후에만 수행했다. 알려진 우려 사항 없음.
