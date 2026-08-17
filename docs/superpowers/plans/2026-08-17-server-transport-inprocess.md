# Bun3.Server.Transport.InProcess 구현 플랜

스펙: `docs/superpowers/specs/2026-08-17-server-transport-inprocess-design.md`

1. `server/src/Bun3.Server.Transport.InProcess` 프로젝트 생성
   (netstandard2.1, C#9, Abstractions만 참조, Version 0.1.0) + `dotnet sln add`.
2. `InProcessConnection` — 인박스/펌프/Close/SendAsync (스펙의 종료·백프레셔·복사 규칙).
3. `InProcessListener` / `InProcessConnector` / `InProcessTransport` — 페어 발급과
   OnConnected 순서 계약.
4. `InProcessTransportTests` — Tcp 계약 테스트 대응 + 인프로세스 고유 계약
   (복사 검증, 드레인, 백프레셔, Stop 후 Connect 실패).
5. 전체 빌드 경고 0 + 테스트 통과 확인 → 커밋.
