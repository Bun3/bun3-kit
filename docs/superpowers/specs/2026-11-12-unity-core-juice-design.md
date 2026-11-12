# 피드백 모션 Juice — 펀치·흔들림·섬광

> 상태: 구현 (2026-11-12)
> 패키지: `com.bun3.unity.core` (`Bun3.Unity.Core.Motion`), 0.5.2 → 0.6.0
> 첫 사용처: 쥬라기 낙원 행동 결과 연출 보강. 게임 쪽에 먼저 만든 `UiJuice`를 옮겨 왔다.

## 1. 의도

버튼을 눌렀을 때 튀기, 거절됐을 때 흔들기, 맞았을 때 번쩍이기처럼 짧은 반응 모션을 한 줄로 부른다. 게임마다 이 모션을 다시 짜게 되므로 프레임워크가 지원한다. 대상이 끝나면 원래 값으로 정확히 돌아오고, 연타해도 크기·위치가 누적되지 않는 것이 핵심 계약이다.

## 2. 사용자 결정 (2026-11-12 쥬라기 낙원 대화)

1. Feel(MMFeedbacks)을 쓰지 않고 정적 헬퍼 하나로 간다. 게임의 트윈이 이미 직접 짠 보간 코드로 통일돼 있어서다.
2. bun3-kit으로 뺀다. — "bun3-kit으로 뺄만한 건 없어?" → `UiJuice`를 kit으로 옮기는 안에 "ㄱㄱ".
3. UI 전용이 아니라 월드 Transform·SpriteRenderer에도 쓰므로 `com.bun3.unity.ui`가 아니라 `com.bun3.unity.core`에 둔다.

## 3. API

```csharp
namespace Bun3.Unity.Core.Motion
{
    public static class Juice
    {
        public static void Punch(Transform target, float scale = 1.15f, float seconds = 0.12f);
        public static void Shake(Transform target, float distance = 6f, float seconds = 0.18f);
        public static void Flash(Graphic target, Color color, float seconds = 0.15f);
        public static void Flash(SpriteRenderer target, Color color, float seconds = 0.15f);
    }
}
```

## 4. 규칙

- 시작 값을 기억했다가 끝나는 프레임에 정확히 되돌린다. 같은 대상·같은 종류를 다시 걸면 기억한 값에서 새로 시작한다.
- 펀치는 `sin(πt)` 한 번, 흔들림은 좌우 세 번 왕복하며 선형으로 줄어든다. 흔들림 거리는 로컬 단위다(UI는 픽셀, 월드는 월드 단위).
- unscaled 시간으로 흐르고 `PlayerLoopSystemHelper`로 `PreLateUpdate.ScriptRunBehaviourLateUpdate` 뒤에 돈다. 씬 오브젝트가 없다.
- 플레이 모드에서만 동작한다. 편집 모드 호출은 무시한다.
- 파괴된 대상은 조용히 뺀다.
- 다른 스크립트가 매 프레임 쓰는 속성에는 걸지 않는다. 두 쓰기가 서로 덮는다(문서에 명시, 코드로 막지 않는다).
- 도메인 리로드 없이 플레이 모드에 들어가도 남은 항목이 없게 `SubsystemRegistration`에서 비운다. 틱 시스템은 헬퍼가 종료 때 걷어 내고 다음 첫 호출에 다시 넣는다.

## 5. 범위 밖

- 카메라 흔들림. Cinemachine 의존이 필요해 kit에 넣지 않았다. 게임의 `JpShake`로 둔다.
- 곡선·이징 선택, 회전·색 외 속성. 쓰는 곳이 생기면 그때 더한다.
