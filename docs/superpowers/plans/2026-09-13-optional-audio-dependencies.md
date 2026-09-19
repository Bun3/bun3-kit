# 선택적 음성·공간 음향 의존성 복구

사용자 보고: SDK 없는 bun3 Unity 프로젝트에서 Dissonance와 Steam Audio 어댑터 및 테스트가 컴파일 실패한다. 사용자 지시에 따라 개발 프로젝트에는 Dissonance 9.0.9, 공식 NGO 연동, Steam Audio 4.8.1을 먼저 설치하고 의존성 리뷰를 수행한다.

## 확인된 상태

- 개발 프로젝트 `unity/Assets/Dissonance`, `Assets/Plugins/Dissonance`, `Assets/Plugins/SteamAudio`에 게임 프로젝트에서 Unity가 임포트한 SDK와 메타데이터를 보존해 설치했다. 공식 NGO 어셈블리 경계도 포함한다. Unity 6000.3.14f1의 Safe Mode가 해제되고 Pipeline 연결이 복구됐다.
- 별도 SDK 없는 프로젝트의 `sdk-absent-red2.log`에서 사용자와 같은 CS0400/CS0246 오류를 재현했다. 첫 시도는 테스트 호스트의 SerializeReference 패키지 루트 의존성이 빠져 UPM 해석 실패였으므로 증거로 사용하지 않는다.
- Dissonance에는 컴파일 활성화 조건이 없고, 결합 패키지는 Steam Audio 조건만 확인한다. SDK가 남기는 `STEAMAUDIO_ENABLED`는 삭제 시 자동 제거되지 않는다.

## 수정 계약

1. Dissonance, 공식 NGO 어댑터, Steam Audio에 별도 `BUN3_DISSONANCE`, `BUN3_DISSONANCE_NFGO`, `BUN3_STEAMAUDIO` 활성화 조건을 둔다. 결합 어댑터는 양쪽 조건을 모두 요구한다. 모든 Runtime/Editor/Tests에 동일하게 적용한다.
2. SDK와 직접 연결되지 않는 Editor 설정 도구가 실제 SDK 어셈블리 자산과 필수 타입을 검사한 뒤 활성화 심볼을 맞춘다. 설치되지 않은 SDK를 활성화하지 않는다. 개발 프로젝트와 게임·검증 호스트에 설정을 적용한다.
3. 기본 상태는 비활성이다. SDK 제거 전 설정 해제도 제공한다. Asset Store SDK를 UPM 패키지처럼 versionDefines로 감지했다고 주장하지 않는다. 에디터 밖에서 SDK를 지운 뒤 예전 활성화 심볼을 남긴 콜드 스타트의 자동 복구까지 보장하지 않는다.
4. 현재 검증한 코어 버전과 어댑터 의존성·최소 Unity 버전을 정리한다. 설치된 SDK 원본은 패키지 안에 번들하지 않는다.
5. SDK 없음(기존 Steam 심볼 잔존), Dissonance만, Steam Audio만, 전체 SDK 조합의 실제 컴파일과 어셈블리 활성화 상태를 검증한다. 설치 상태의 관련 회귀 테스트도 실행한다.

## 리뷰 판단

- Standards: NGO 어댑터의 최소 Unity 6000.0은 필수 Dissonance 패키지의 6000.3과 맞지 않는다.
- Spec: SDK 미설치/부분 설치에서의 컴파일 분리가 빠졌다.
- 오디오 의존성 0.1.1에 대해서는 리뷰 의견이 갈렸다. 변경 이력상 외부 출력 API를 0.1.1에 기록했으나 현재 검증한 통합 코어는 0.2.0이다. 검증한 버전을 명시하는 방향으로 정리한다.
- 기존 spatializer 설정 경고는 이번 수정에서 확대하지 않는다. 새 네이티브 스테레오 출력에는 Unity spatializer 플러그인 선택이 필수가 아니므로 무조건 경고하면 잘못된 진단이 된다.

## 완료 및 검증 (2026-09-13)

- SDK 설치 후 `SoundSdkSetup.SyncInstalledAdapters`로 bun3 개발 프로젝트, 게임, 전체 SDK 검증 호스트를 활성화한 뒤 13개 asmdef의 조건을 적용했다. `DisableAdapters`는 Bun3 심볼만 제거한다. 설정은 `AssetDatabase.SaveAssets`로 저장하며 기존 심볼을 보존한다.
- 버전: audio 0.3.0, Dissonance·NGO·결합 어댑터 0.2.0, Steam Audio 0.4.0. 의존 버전과 NGO 최소 Unity 6000.3을 정렬했다.
- 독립 리뷰에서 추가 차단 사항 없음. 테스트 실행기는 필수 활성화 심볼과 양쪽 SDK 테스트의 실제 발견을 요구한다. PowerShell 구문 검사 및 심볼 네 종류 각각 누락 시 사전 실패 검증도 통과했다.

증거 위치: `E:/Temp/bun3-optional-audio-validation-20260913/Results`.

| 검증 | 결과 |
|---|---|
| SDK 없음, 이전 `STEAMAUDIO_ENABLED` 잔존 / Unity 6000.3.14f1 | `absent-green.json` PASS, 종료 0 |
| Steam Audio만 설치 / Unity 6000.3.14f1 | `steam-only.json` PASS, 종료 0 |
| Dissonance만 설치, 공식 NGO·Steam Audio 없음 / Unity 6000.3.14f1 | `dissonance-only.json` PASS, 종료 0 |
| 전체 SDK·공식 NGO / Unity 6000.5.5f1 | `full-sdk.json` PASS, 종료 0 |
| 활성화 해제·재동기화, 네 대상의 기존/SDK 소유 심볼 보존 | `symbol-lifecycle.log` PASS, 종료 0 |
| 게임 어셈블리 재컴파일 | `game-gated-compile.log` 종료 0 |
| 별도 호스트 기초 SDK 회귀 | `Regression/20260913-163356-6696268c75b74c2a8abcd21ca392b3b1`: EditMode 42/42, PlayMode 82/82 |
| 별도 호스트 결합 네이티브 출력 | `combined-play.xml` 20/20 |
| 실제 bun3 에디터 | 재컴파일 오류 없음, SDK·네 어댑터·테스트 로드 확인, `bun3-dissonance-edit.json` 24/24 |

bun3 에디터의 추가 PlayMode 실행은 기존 GameplayTag 카탈로그(`ProjectSettings/GameplayTags.json`) 누락에 의한 `B3TAG3004/B3TAG3101` 진입 차단으로 취소했다. SDK 오류로 집계하지 않으며 통과했다고 주장하지 않는다. TestRunner의 실제 작업도 취소해 임시 씬을 정리하고 이전 빈 씬 상태로 복원했다. PlayMode 검증은 위의 별도 호스트 결과를 사용한다. SDK 원본과 패키지 변경을 커밋·푸시하지 않았다.
