# Bun3.Server.Achievements 구현 플랜

- 스펙: `docs/superpowers/specs/2026-08-17-server-achievements-design.md` (결정의 원본)
- 원칙: netstandard2.1 + C#9 블록 네임스페이스, 전 public 멤버 한국어 XML 문서,
  경고 0, 핫패스 무할당, 의존성 0.

## 태스크

1. **프로젝트 뼈대** — `server/src/Bun3.Server.Achievements/Bun3.Server.Achievements.csproj`
   (Players csproj 형식, Version 0.1.0, ProjectReference 없음) + `dotnet sln add`.
2. **AchievementDefinition** — 열린 클래스: `Id`(string), `Target`(long), `Repeatable`(bool).
   생성자에서 인자 검증은 하지 않음(카탈로그가 기동 시 일괄 검증 — 단일 검증 지점).
3. **AchievementState** — public 필드 struct: Progress/CompletedCount/ClaimedCount/LastCompletedAtUtcTicks.
4. **AchievementCatalog\<TDef\>** — 생성자 검증(빈/중복 id ordinal, Target≤0, 상한 65,536,
   null 항목) + `Action<TDef>?` 게임 validator → `TDef[]` + `Dictionary<string,int>` 동결.
   `Count`/`GetDefinition(int)`/`GetIndex(string)`/`TryGetIndex`.
5. **AchievementTracker\<TDef\>** — 스펙 §4 산식 그대로. 생성자
   `(catalog, Action? onDirty = null, Func<long>? utcNowTicks = null)`.
   `Add`/`Set`/`TryClaim`/`GetClaimableCount`/`GetState(ref readonly)`/`Restore`/`OnCompleted`.
6. **테스트** — `server/tests/Bun3.Server.Tests/AchievementTests.cs` (NUnit),
   스펙 §7 표 전 행. 테스트 csproj에 ProjectReference 추가.
7. **검증** — `dotnet build`(경고 0) + `dotnet test --filter Achievement`.
8. **커밋** — ✨ gitmoji + Co-Authored-By 트레일러. 퍼블리시/버전 태깅 없음.

## v2 (2026-08-18 — 사용자 문답으로 확정된 방향 반영)

1. `AchievementStatus` enum + `Availability` 저장 필드(부분집합) + `GetStatus` 파생.
2. `Add`→`Increase`, `Set`→`SetProgress` 개명. 진행은 Active에서만.
3. 반복 업적을 "누적 + 수령 시 차감"(idlez/growninja 동일 모델)으로 — 10/10 UI 성립.
4. 태그 인터닝(`Tags`/`GetTagIndex`/`GetIndicesByTag`) + `IncreaseByTag`(+필터 오버로드),
   호출 시점 스냅샷 의미론(체인 티어 배치 미이월).
5. 가용성 전이 `Unlock`/`Activate`/`Lock`, Def `InitialAvailability`.
6. Reset은 카운터만(가용성 유지). 진행도 타입 long 확정(스펙 §8 근거).
7. 테스트 37건, 버전 0.2.0.
