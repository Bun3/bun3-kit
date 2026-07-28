# bun3-kit

Bun3의 개인 프레임워크 모노레포.

| 폴더 | 내용 |
|---|---|
| `common/` | 전 플랫폼 공용 .NET 라이브러리 (`com.bun3.common` = UPM+NuGet 이중 포장) |
| `server/` | 서버 재사용 모듈 라이브러리 (`Bun3.Server.*`, NuGet 배포) |
| `unity/` | Unity 패키지 개발 프로젝트 (`com.bun3.unity.*`) |

- 설계 문서: `docs/superpowers/specs/2026-07-26-monorepo-structure-design.md`
- 솔루션: 루트 `Bun3.sln`(common+server). `unity/unity.sln`은 Unity 자동 생성물.
- 외부 소비: 서버/닷넷은 NuGet, Unity는 `?path=` UPM git URL. 서버 앱 시작점은
  별도 `bun3-server-template` 레포(추후).
