# Bun3.Server.Items v0.2 구현 플랜 (인스턴스 인벤토리)

- 스펙: `docs/superpowers/specs/2026-08-18-server-items-instances-design.md`
- 브랜치: `Bun3/server-items` (main 머지 금지)

## 1단계 — 카탈로그 확장
- [x] Register `unstackable` 파라미터 + `IsUnstackable(ItemId)` (bool[])
- [x] ItemError: NotStackable / UnknownInstance / DuplicateInstance / Locked
- [x] `ItemStackContainer<,>` 변경 경로에서 비스택형 거부(NotStackable)
- [x] 카탈로그·스택 컨테이너 테스트 갱신

## 2단계 — 인스턴스 모델
- [x] ItemInstance<TState> (InstanceId·Item·Quantity·Flags·TState·MarkChanged·추적 플래그)
- [x] ItemChange<TState> / ItemChangeKind

## 3단계 — 인벤토리
- [x] ItemInventory<TState>: 저장소(인스턴스 딕셔너리 + 스택 싱글턴 색인),
      GetQuantity·TryGetInstance·열거자
- [x] TryAdd / TryRemove / TryRemoveByInstance (판정 내부 흡수, 잠금 마스크)
- [x] TryApply (혼합 델타 전부-아니면-전무, 발급자 지연 호출)
- [x] TryLoadInstance / DrainChanges / HasChanges
- [x] 테스트 (스펙 §6 전 항목)

## 4단계 — 마무리
- [x] 빌드 경고 0 + 전체 테스트 통과
- [x] 커밋(📝 스펙+플랜 / ✨ 구현+테스트), 워크트리 코멘트 갱신
