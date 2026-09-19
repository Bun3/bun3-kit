# 사운드 패키지 코드 읽기 안내

현재 대상: bun3의 오디오·음향·음성·사운드 이벤트 7개 Unity 패키지.

이 문서는 패키지 사용자가 계산과 리소스 수명을 구분해 따라갈 수 있도록 읽기 시작점을 안내한다. 게임 정책은 소비 프로젝트가 제공하고, 패키지는 재생·전달·경로 평가·연결 수명을 담당한다.

## 읽기 시작점

| 관심 기능 | 패키지 | 시작할 코드 | 읽는 순서 |
|---|---|---|---|
| SFX 재생 | audio | SoundSystem.PlayCore | 슬롯 확보 → 소스 설정 → 출력 선택 → 기존 발화 완료 알림 |
| 재생 종료/페이드 | audio | VoiceTable.Tick | 한 슬롯 진행 → 종료 판정 → 해제 → 완료 목록 기록 |
| 음악 전환 | audio | SoundSystem.PlayMusic / TickMusic | 채널 선택 → 전환 적용 → 두 채널 갱신 후 알림 |
| 외부 소스 제어 | audio | ExternalAudioRegistry.Register | 설정 검증 → 빈 슬롯 탐색 → 원래 값 보관과 제어권 획득 |
| AI용 소리 전달 | sound-events | SoundEventWorld.Tick | 이벤트/청취자 유효성 → 도달 정책 → 수신 알림 |
| 발화 이벤트 수명 | sound-events | ValidatedSoundActivitySource / SoundActivityLease | 보고 검증 → 시작/갱신/종료 → 짧은 발화 보존 |
| 그리드 연결성 | acoustics | AcousticGridQuery | 공간 범위 → 연결 성분/프로브 → 경로 조회 |
| 네이티브 경로 평가 | audio.steamaudio | SteamAudioPathSimulationScope.Simulate | 입력 준비 → 직접음 → 경로 평가 → 결과 복사 |
| SFX PCM 출력 | audio.steamaudio | SteamAudioSoundOutput.Process | 읽기 소유권 → 프레임 렌더링/피치 보간 → 출력 합성 |
| 음성 PCM 출력 | audio.dissonance.steamaudio | DissonancePathPlayback.ReadInterleaved | 읽기 소유권 → 디코딩/프레임 렌더링 → 채널 복사 → 종료/소유권 반환 |
| 음성 세대 전환 | audio.dissonance.steamaudio | DissonanceSteamAudioPlayback.Update | 현재 출력 확인/배출 → 다음 세션 시작 |
| 음성 룸/발화 | audio.dissonance | DissonanceRoomScope / DissonanceVoiceActivityScope | 송수신 채널 수명 / 발화 상태 스냅샷 |
| NGO 음성 신원 | audio.dissonance.netcode | DissonanceNfgoPlayer | 소유권 확인 → 신원 검증 → Dissonance 추적 등록 |

## 작성 기준

- 계산·판정은 입력과 결과가 드러나는 함수로 작성한다. 예: 채널 선택, 음량 계산, 축 투영/겹침, 보고 전송 시점.
- Unity 호출·슬롯 변경·자원 해제는 실행 순서가 드러나는 메서드로 묶는다. 계산 함수 안에 숨기지 않는다.
- 조건식에 의미 있는 이름을 붙이고, 상태 변경을 삼항식이나 여러 명령이 섞인 한 줄에 압축하지 않는다.
- 함수를 나눌 때는 호출부가 한 단계 더 높은 의미를 표현해야 한다. 단순 전달만 하는 함수나 새 추상 계층을 늘리지 않는다.
- 관련된 작은 함수는 같은 파일에 둔다. 파일 길이 자체보다 동작을 이해하기 위해 이동해야 하는 범위를 줄인다.
- 핫패스에는 캡처 람다, LINQ 열거, 새 배열/컬렉션을 추가하지 않는다. 순수 함수도 명시적 루프로 작성할 수 있다.
- 직렬화 필드와 공개 API 이름은 호환성 경계다. 내부 변수 이름 개선과 구분한다.

## 수명·동시성에서 유지해야 할 것

완료 콜백은 동기적으로 새 재생을 시작할 수 있다. 따라서 완료된 소스를 먼저 모두 중단하고, 상태 변경을 마친 뒤 콜백을 호출한다. 음악 두 채널도 같은 원칙을 따른다.

오디오 스레드의 읽기와 메인 스레드의 종료 요청은 겹칠 수 있다. retired는 즉시 메모리 해제를 뜻하지 않으며, 읽기 소유권이 반환된 뒤에 reclaim한다. 세대 번호는 이전 콜백이 새 재생의 상태에 접근하는 것을 막는다. 메서드 추출 때문에 원자 연산이나 잠금 경계를 옮겨서는 안 된다.

그리드와 네이티브 시뮬레이션은 다른 책임을 가진다. 가독성 개선 과정에서 두 거리 판정이나 경로를 서로 대체하지 않는다.

## 이번 점검 범위

Runtime/Editor C# 98개 파일을 검토하고 32개 구현 파일을 수정했다. 짧은 데이터 타입·설정·인터페이스, 기존 ABI 선언과 이미 응집된 리소스 래퍼 등 66개는 유지했다. SDK 원본은 수정하지 않았다. 자세한 변경 범위와 검증 기록은 `docs/superpowers/plans/2026-09-19-sound-package-readability.md`에 남긴다.
