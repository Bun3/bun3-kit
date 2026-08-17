# Bun3.Server.Items 구현 플랜

- 스펙: `docs/superpowers/specs/2026-08-17-server-items-design.md`
- 브랜치: `Bun3/server-items` (main 머지 금지 — 통합은 사용자)

## 1단계 — 프로젝트 뼈대
- [x] `server/src/Bun3.Server.Items/Bun3.Server.Items.csproj` (netstandard2.1, C#9,
      Nullable, XML 문서, Version 0.1.0, ProjectReference → Bun3.Gameplay,
      InternalsVisibleTo Bun3.Server.Tests)
- [x] `dotnet sln add`로 Bun3.sln 등록, 테스트 csproj에 ProjectReference 추가

## 2단계 — 카탈로그
- [x] ItemId / ItemError / ItemCatalogException
- [x] ItemCatalog (비제네릭 코어: 인터닝 배열 + 문자열 역색인 + maxStack)
- [x] ItemCatalog<TDefinition> + ItemCatalogBuilder<TDefinition> (Register/AddValidator/Build)
- [x] 카탈로그 테스트

## 3단계 — 수량 시섬
- [x] IQuantityOps<TQuantity> / LongQuantityOps / BigNumQuantityOps
- [x] BigNum 손실 의미론 XML 문서 명시

## 4단계 — 컨테이너
- [x] ItemDelta / ItemStack / ItemStackContainer<TQuantity,TOps>
      (GetQuantity·TryAdd·TryRemove·TryApply·TryMoveTo·TryLoad·Clear·struct 열거자·onChanged)
- [x] sealed 특수화: ItemStackContainer(long) / BigNumItemStackContainer
- [x] 컨테이너·트랜잭션·이동·BigNum 차등·무할당·onChanged 테스트

## 5단계 — 마무리
- [x] `dotnet build`(경고 0) + `dotnet test` 전체 통과
- [x] 커밋: 📝 스펙+플랜 / ✨ 구현 / ✅ 테스트 (또는 구현+테스트 통합)
- [x] 워크트리 코멘트 갱신
