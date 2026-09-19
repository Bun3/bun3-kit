# SFX·음성 공통 공간 설정

> 상태: 설계 검토안. 구현 전 검토 대상.

## 목적

SFX와 음성의 거리 감쇠·근거리 모노 설정을 같은 방식으로 저작한다. 기획자는 공유 프로필을 선택하거나 해당 위치에 개별값을 입력하며, 기존 사운드의 청취 결과와 튜닝을 보존한다.

사용자가 승인한 방향은 공통 공간 프로필과 공유/개별 선택이다. SoundDef에 음성을 포함할지와 SoundManager의 음성 관리 범위는 이번 문서에서 구체화한다.

## 현재 구조

- JP SoundManager → Bun3SoundOutput → SoundSystem: SoundDef 기반 SFX의 클립·재생 풀·제한을 관리한다.
- JP GameVoiceSession → Dissonance: 음성 네트워크 연결과 수신 볼륨을 관리한다. PlayerVoiceEmitter는 그룹과 발화 보고를 연결한다.
- GameAcousticSession: 공간 SFX와 음성을 같은 PlanarAcousticWorld에 등록하고 맵·문·리스너 상태를 전달한다.
- SoundDef와 SoundSpatialProfile은 거리 감쇠/모노 설정을 제공하지만 음성 어댑터는 별도 IPlanarVoiceSettings/IPlanarVoiceSpatialSettings를 읽는다.
- audio 패키지의 ExternalAudioRegistry는 외부 AudioSource의 선택된 출력 제어만 소유한다. 음성 디코딩이나 재생을 소유하지 않으며 JP의 native 음성 경로를 SoundSystem 풀에 통합하는 장치는 아니다.
- SoundSystem의 VoiceTable, SfxVoices, ISoundVoiceOutput에서 voice는 재생 슬롯을 뜻한다. 네트워크 음성 채팅 플레이어와 구분한다.

## 검토한 선택

| 안 | 장점 | 비용 |
|---|---|---|
| SoundDef에 스트리밍 음성 모드 추가 | 하나의 정의 에셋으로 접근 | 클립·피치·반복·쿨다운과 스트리밍 디코더 수명을 분기해야 함. 기존 재생 API의 의미까지 확대 |
| 공통 공간 설정을 추출해 SoundDef와 음성이 사용 | 같은 프로필·같은 Inspector를 제공하면서 기존 재생 수명을 보존 | 기존 공간 필드의 이동·마이그레이션 필요 |
| 별도 VoiceDef에 기존 필드를 복제 | 음성 에셋 하나로 정리 | 공간 설정 해석과 Inspector 중복이 남음 |

**권장안은 공통 공간 설정 추출이다.** SoundDef는 재생할 클립을 가진 정의로 유지한다. 이번 범위에서는 새 VoiceDef나 중앙 음성 재생 매니저를 추가하지 않는다.

## 제안하는 저작 구조

공통 타입은 SDK 의존성이 없는 com.bun3.unity.audio에 둔다. 명칭은 아래를 기준으로 구현 계획에서 확정한다.

- `SoundAcousticSettings`: 거리 감쇠 사용, 거리 커브 참조, 프로필 없는 경우의 Min/Max 거리, 공통 모노 상속 여부, 모노 프로필 참조, 개별 Mono/FullSpatial 거리.
- `SoundAcousticProfile`: 위 설정을 보관하는 공유 ScriptableObject.
- `SoundAcousticSelection`: 공유 프로필 참조 + 보존되는 개별 설정. 프로필이 있으면 공유, 없으면 개별. 단일 해석 API와 공통 Inspector를 제공한다.
- SoundDef의 개별 Spatial 묶음과 공유 SoundSpatialProfile에 같은 선택 구조를 사용한다. 기존 상위 SpatialProfile 선택은 유지한다.
- 음성은 GameConfig의 `Voice Acoustics`에 같은 선택 구조를 배치한다. 양쪽이 같은 SoundAcousticProfile 에셋을 참조할 수 있다.
- 위치 모드(None/Positional/Follow), 기본 AudioSource 전용 차폐 옵션은 기존 SFX Spatial 묶음에 남는다. 음성용 공통 설정에 무시되는 클립·재생 제한·위치 모드 필드를 노출하지 않는다.

공통값 선택 순서는 명시적이다. SFX는 상위 SpatialProfile 또는 로컬 Spatial을 먼저 고르고 그 안의 AcousticSelection을 해석한다. 음성은 Voice Acoustics를 해석한다. 모노 공통 상속이 켜져 있으면 월드의 공통 SpatialBlendProfile/개별 거리로 해석한다. 상속이 꺼져 있으면 선택된 모노 프로필, 없으면 개별 거리를 쓴다.

공유 선택으로 전환해도 개별값을 덮어쓰지 않는다. 프로필을 비울 때 자동 복사하지 않는다. 공유 프로필은 값만 보관해 순환 참조/프로필 체인을 만들지 않는다.

## 런타임 경계

- SFX와 음성 어댑터는 같은 공통 설정 해석 결과를 받아 경로 감쇠와 스테레오 폭을 계산한다. JP에 별도 계산식을 만들지 않는다.
- Dissonance는 계속 수신·디코딩·재생 시작/종료를 소유한다. 음성 출력의 풀/세대/reader barrier와 native tail 처리에는 손대지 않는다.
- JP SoundManager는 SFX 진입점으로 유지한다. 음성 출력의 공통 공간 계산은 기존 GameAcousticSession 등록 경로를 유지한다.
- 기존 Master × Voice 볼륨을 한 번만 적용한다. 이번 변경은 통합 볼륨 매니저나 사용자별 볼륨 UI를 추가하지 않는다.
- 거리 커브는 native 경로 거리, 모노 전환은 native 최단 평가 경로 거리를 사용한다. 직선/그리드 거리로 바꾸지 않는다.
- 공통 문 차폐 배율·프로브 커버리지 차단·마이크 발화 AI 이벤트·대화 그룹은 유지한다.
- 설정 해석은 main thread에서 수행하고 값/참조 조회 시 프레임별 할당을 만들지 않는다. audio callback에 ScriptableObject 접근이나 커브 복사를 추가하지 않는다.
- 음성의 옛 인터페이스는 호환 경로로 유지하고 새 공통 설정 경로를 추가한다. 기존 사용자가 당장 재작성하지 않아도 이전 결과가 유지되어야 한다.

## 마이그레이션

1. 직렬화 버전과 이전 필드 보존을 통해 미변환 에셋을 읽는다. 런타임에서 에셋을 생성·저장하지 않는다.
2. SoundDef 로컬값과 SoundSpatialProfile 공유값을 각각 이전한다. 비활성 로컬값도 보존하며 모든 SoundDef를 같은 값으로 덮어쓰지 않는다.
3. 기존 감쇠 커브와 모노 프로필의 GUID 참조를 유지한다. 공유 관계가 달랐던 설정을 임의로 병합하지 않는다.
4. JP GameConfig 음성 필드를 Voice Acoustics로 이전한다. 음성의 프로필 없는 기본 1/15m, 기존 SFX 개별 Min/Max, 모노 상속을 그대로 보존한다.
5. 현재 WorldSpatial의 감쇠 비활성도 보존한다. 구조 변경이 감쇠 활성화 같은 밸런스 수정으로 이어지지 않는다.
6. M1 생성 코드와 생성 버전을 함께 갱신하며 반복 셋업에서 변환과 임의 키·공유 프로필 선택이 보존되어야 한다. .meta는 Unity로 생성한다.
7. SoloVoice는 새 설정의 실효값을 수신 측 임시 설정으로 전달한다. 수신 원본을 변경하지 않고 종료 시 복원한다. SFX별 설정 동기화는 추가하지 않는다.
8. JP 한글 운영 가이드와 관련 bun3 영문 README를 새 UI 기준으로 갱신한다.

## 완료 기준

- 같은 공통 프로필을 SFX와 음성에 지정하면 감쇠/모노 설정 해석과 native 계산 입력이 같다.
- 공유/개별 전환, 모노 상속/전용 프로필/직접값, 프로필 없는 거리 fallback을 EditMode에서 검증한다.
- 기존 직렬화 fixture의 변환 전후 값·참조·비활성 개별값 보존 및 반복 변환의 멱등성을 검증한다.
- 실행 중 공통 프로필 수정이 기존 SFX/음성 출력에 반영되며 native 출력·음성 수명 회귀 테스트가 통과한다.
- SoloVoice 설정 전송·임시 수신·원본 복원 테스트가 통과한다.
- 새 설정 해석의 warm managed allocation이 0B이며 SDK 미설치 시 공통 패키지는 독립적으로 컴파일된다.
- 실제 마이크 청취 테스트는 사용자가 추후 수행한다. 자동 테스트로 하드웨어 음질 검증을 대체했다고 보고하지 않는다.
