# Bun3.Server.Transport.InProcess 설계

날짜: 2026-08-17
상태: 확정

## 목적

소켓 없이 같은 프로세스 안에서 서버와 클라이언트를 잇는 루프백 전송.

- 클라호스트(어몽어스류): 호스트 플레이어가 자기 프로세스의 서버에 직접 붙는다.
- 테스트: 실TCP 없이 서버 E2E를 돌린다.

`Bun3.Server.Abstractions`의 `IConnection`/`IConnector`/`ITransportListener`/`IConnectionHandler`
계약을 구현하며, 관측 가능한 동작(순서·소유권·백프레셔·종료 의미론)을 `Transport.Tcp`와
동일하게 맞춘다.

## 공개 API

```csharp
public sealed class InProcessTransport
{
    public InProcessTransport(int maxQueuedPacketsPerConnection = 256, ILogger? logger = null);
    public ITransportListener Listener { get; }   // StartAsync(serverHandler)로 수락 개시
    public IConnector Connector { get; }          // ConnectAsync(clientHandler)마다 새 연결 페어
}
```

- 리스너는 Tcp와 같은 단일 사용: StartAsync 2회 호출은 예외, StopAsync 후 재시작 불가.
- StopAsync 이후(또는 StartAsync 이전) ConnectAsync는 `InvalidOperationException`.
  StopAsync는 기존 연결을 건드리지 않는다(계약: 기존 연결 종료는 상위 책임).
- 연결 Id는 프로세스 전역 static 단조 증가 카운터에서 발급(양 끝점이 각각 별도 Id).
- RemoteAddress는 두 끝점 모두 `"inproc"` (상수, 할당 없음).

## 내부 구조 — 끝점당 수신 인박스 + 펌프

레포 기존 패턴(Session.cs의 ConcurrentQueue + SemaphoreSlim + 소비 루프)을 그대로 쓴다.
새 의존성 없음.

- 끝점(`InProcessConnection`)마다: `ConcurrentQueue<byte[]>` 인박스, 아이템 세마포어,
  슬롯 세마포어(용량 = maxQueuedPacketsPerConnection), 수신 펌프 Task 1개.
- `SendAsync` = 상대 인박스에 인큐. 펌프가 순서대로 꺼내 `OnPacket` 호출 →
  Tcp의 단일 수신 루프와 동일하게 연결당 OnPacket이 직렬화된다. 송신자 스레드에서
  핸들러를 직접 호출하지 않으므로 재진입/ABBA 데드락이 없다.

### 소유권 — 송신당 복사 1회 (스펙 결정)

`IConnectionHandler.OnPacket`의 배열 소유권은 수신자에게 이전되는데, `SendAsync`의
`ReadOnlyMemory<byte>`는 송신자 소유(호출 후 재사용 가능)다. 따라서 루프백에서도
송신 시 `ToArray()` 복사 1회가 필요하다. Tcp도 수신 패킷당 새 배열 1개를 할당하므로
패킷당 할당 수는 동일하다. 핫패스에 클로저/LINQ 없음.

### 백프레셔 — 유한 인박스 + 송신자 대기

Tcp의 백프레셔(소켓 버퍼가 차면 송신 await가 블록)를 유한 인박스로 재현한다.
인박스가 가득 차면 `SendAsync`가 슬롯 세마포어에서 대기한다(ct로 취소 가능).
어느 한쪽이 닫히면 양쪽 인박스의 슬롯을 대량 방출해 대기 중인 송신자를 깨우고
(TCP의 로컬 Close가 블록된 write를 dispose로 깨우는 동작에 해당), 깨어난 송신자는
양 끝 닫힘 여부를 확인하고 no-op으로 반환한다(계약: 닫힌 연결 송신은 예외 없음).

### 종료 의미론 — Tcp와 동일

- `Close()` 멱등(Interlocked CAS). 로컬 끝점: 자기 펌프를 깨워 종료 →
  `OnClosed(null)` 정확히 1회(펌프 finally에서만 통지). 인박스에 남은 미전달
  패킷은 버린다(Tcp의 로컬 Close 후 미드레인과 동일).
- 상대 끝점: FIN에 해당하는 종료 센티널(참조 비교용 static 배열)을 상대 인박스
  꼬리에 인큐 → 상대 펌프는 먼저 큐잉된 패킷을 전부 전달한 뒤 `OnClosed(null)`
  (Tcp의 그레이스풀 드레인과 동일).
- 펌프 안에서 `OnPacket`이 던지면 그 연결을 error와 함께 닫는다(Tcp 수신 루프와 동일).

### OnConnected 순서 계약 (ConnectAsync 내부, 결정적)

1. 리스너 수락 확인(미시작/중지면 예외) → 끝점 페어 생성.
2. 서버 `OnConnected(serverConn)` → 서버 펌프 시작.
   - 던지면: Tcp 리스너와 동일 — 서버 OnClosed 없이 서버 끝점만 닫고 로그. 클라는
     정상 연결 후 즉시 `OnClosed(null)`을 받는다(원격 거부의 TCP 관측과 동일).
3. 클라 `OnConnected(clientConn)` → 클라 펌프 시작 → 반환.
   - 던지면: TcpConnector와 동일 — 클라 OnClosed 없이 페어를 닫고(서버는
     `OnClosed(null)` 수신) 예외를 호출자에게 전파.

펌프는 해당 끝점의 OnConnected 반환 후에만 시작하므로 "OnConnected 전 OnPacket/OnClosed
없음"이 구조적으로 보장된다.

## 범위 밖 (YAGNI)

- MaxPacketSize / MaxConnections: 같은 프로세스의 신뢰 코드 간 연결이라 보호 가치가
  없다. 필요해지면 옵션으로 추가.
- 프레이밍(PacketFormat): 바이트 스트림이 없으므로 불필요 — `Bun3.Common` 의존 없음.

## 테스트

`server/tests/Bun3.Server.Tests/InProcessTransportTests.cs` — Tcp 계약 테스트와 동일
시나리오를 기존 헬퍼(RecordingHandler)로 재사용. 추가로 인프로세스 고유 계약:
송신 버퍼 재사용 시 수신 패킷 불변(복사 검증), Close 후 큐잉분 드레인, 백프레셔
(OnPacket 블록 시 용량 초과 송신이 대기), Stop 후 Connect 실패.
