# Unity Audio 음성 우선순위와 카탈로그 음악 설계

- 상태: 구현 완료 (0.6.0)
- 적용 패키지: `com.bun3.unity.audio` (0.5.0 → 0.6.0)

## 목적

- 풀이 가득 찼을 때 중요한 소리가 오래됐다는 이유로 끊기지 않게 한다.
- 엔진의 가상 음성(Max Real Voices 초과분)이 덜 중요한 소리부터 가상화되도록 우선순위를 넘긴다.
- 게임이 효과음과 배경음을 하나의 `SoundCatalog` 문자열 키로 관리하고, 배경음에도 `VolumeGroup` 게인(음악 볼륨 설정)이 실시간 적용되게 한다.

## 결정

### 우선순위

- `SoundDef.Priority`와 `SoundConcurrencyProfile.Priority`에 둔다. 범위와 의미는 Unity `AudioSource.priority`와 같다(0이 가장 중요, 255가 가장 낮음, 기본 128). 동시성 묶음에 속하므로 프로필이 연결되면 프로필 값이 묶음 전체를 대신한다.
- 재생 시점 값을 `VoiceSlot.Priority`에 기록한다. 정의 수정은 다음 재생부터 반영한다는 기존 계약을 유지한다.
- 전체 풀이 가득 차면 가장 덜 중요한 음성을 교체하고, 같으면 가장 오래된 음성을 교체한다. 활성 음성이 모두 요청보다 중요하면 요청을 거절하며 쿨다운도 시작하지 않는다.
- 우선순위가 모두 같으면 기존 동작(전체 최고령 교체)과 같다.
- `MaxInstances` 초과 시 같은 정의의 최고령을 교체하는 규칙은 바꾸지 않는다. 같은 정의끼리는 우선순위가 같다.
- 가상화는 직접 구현하지 않는다. 우선순위를 `AudioSource.priority`에 넣고, 게임이 풀을 엔진 Real Voices보다 크게 잡으면 엔진이 우선순위·가청도 순으로 가상화한다.

### 카탈로그 음악

- 별도 음악 카탈로그나 `MusicDef` 카탈로그 항목을 추가하지 않는다. `PlayMusic(SoundDef, fade)` 오버로드가 기존 2채널 음악 경로(크로스페이드, 일시정지)를 그대로 쓴다.
- `SoundDef` 트랙은 `Clips` 중 하나를 뽑아 항상 반복한다. 사용 값은 굴린 `Volume`, `MixerGroup`(없으면 음악 그룹), `VolumeGroup`이다. Pitch·Loop·동시성·공간·오클루전은 음악에 적용하지 않는다.
- 음량은 매 틱 `Volume × GroupGain(VolumeGroup) × Fade`로 계산한다. 그래서 게임의 음악 볼륨 설정이 재생 중인 트랙에 바로 적용된다.
- `MusicDef` 트랙의 음량 계산(그룹 게인 없음)은 기존 그대로 둔다. 기존 게임의 동작을 바꾸지 않기 위해서다.
- 음악 소스는 `AudioSource.priority` 0으로 두어 효과음이 많아도 엔진이 가상화하지 않게 한다.
- 채널을 다시 쓸 때 출력 그룹을 매번 지정해, `SoundDef`의 믹서 그룹이 다음 `MusicDef` 트랙에 남지 않게 한다.

## 미포함

- `PlayMusicAsync(SoundDef)`: 필요해지면 `MusicDef` 비동기 경로와 같은 방식으로 추가한다.
- `MusicDef`의 `VolumeGroup`: 기존 사용처의 음량 변화를 피하려고 넣지 않았다.

## 검증

- `VoiceTableStealTests`: 덜 중요한 음성 우선 교체, 모두 더 중요할 때 거절, 거절 시 쿨다운 미시작.
- `SoundProfileTests`: 프로필 우선 적용과 0..255 고정.
- `SoundSystemMusicTests`: `SoundDef` 음악이 음성 풀을 쓰지 않음, 반복 재생, 실시간 그룹 게인, 같은 채널의 출력 그룹 복원, 음성·음악 소스 우선순위.
