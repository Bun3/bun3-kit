# Unity 팝업 스택 구현 플랜

스펙: `docs/superpowers/specs/2026-08-17-unity-popup-stack-design.md`

## 단계

1. **런타임 코드** — `unity/Packages/com.bun3.unity.ui/Runtime/Popups/`
   - `PopupKey.cs`, `PopupPhase.cs`, `PopupDuplicatePolicy.cs`, `PopupDelegates.cs`
   - `PopupBehaviour.cs` (가상 생명주기 + back 훅 + WaitUntilClosedAsync)
   - `PopupStack.cs` (정렬 삽입, 중복 정책, 대기열, back 라우팅, Clear)
   - `PopupBackKeyRouter.cs` (`#if ENABLE_INPUT_SYSTEM` / 레거시)
   - `Bun3.Unity.UI.asmdef`에 `UniTask`(+옵션 `Unity.InputSystem`) 참조 추가
2. **EditMode 테스트** — `Tests/Editor/Bun3.Unity.UI.Editor.Tests.asmdef` 신설,
   스펙의 테스트 전략 항목 전부. 수동 완료 UniTaskCompletionSource로 동기 검증.
3. **검증** — Unity 6000.3.14f1 batchmode `-runTests -testPlatform EditMode`.
   첫 실행은 Library 임포트로 오래 걸림 → 백그라운드 실행 후 결과 xml 확인.
   Unity가 생성한 .meta 파일 커밋 포함.
4. **커밋** — gitmoji + Co-Authored-By 트레일러. 머지/퍼블리시 없음.

## 리스크

- batchmode 임포트 실패(에디터 잠금 등) 시: .meta 수동 생성, 컴파일만이라도
  `-batchmode -quit` 로그의 `error CS` 검색으로 검증하고 코멘트로 보고.
