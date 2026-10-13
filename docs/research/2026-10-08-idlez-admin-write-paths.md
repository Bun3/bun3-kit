# idlez Admin과 게임 내 변경의 저장 경로

- 조사일: 2026-10-08.
- 질문: Admin이 DB를 직접 수정하는지, 게임 서버에 명령을 보내는지, WorldServer가 꺼져 있어도 처리할 수 있는지.
- 원본: `E:/Projects/idlez-server`, HEAD `b39749fecdde973ea30875a7556990c6e09f888b`.
- 방법: 로컬 API controller에서 모델 저장·DB 연결까지, 별도로 게임 내 GM 패킷에서 플레이어 메모리·정기 저장까지 추적했다. 서버 실행·DB 접속·외부 저장소 수정은 하지 않았다. 기존 `Server/Commons` 및 미추적 파일 상태는 유지했다. 조사 작업자 실행은 thread limit으로 거부되어 직접 조사했다.
- 범위: 이 checkout의 Admin API와 대표 게임 내 명령. 별도 Admin 프런트엔드, 실제 배포/사용 여부, 운영 DB trigger나 배포 의존성은 확인하지 않았다.

## 확인한 두 경로

| 진입점 | 실제 처리 | WorldServer 의존성 |
|---|---|---|
| `PUT /admin/players/{id}` | request의 `PlayerModel.SaveAsync()`로 DB 직접 저장 | 해당 호출 경로에는 없음 |
| `PUT /admin/playertelegrams/{id}` | `PlayerTelegramModel.SaveAsync()`로 DB 직접 저장 | 해당 호출 경로에는 없음 |
| 게임 내 `SendCommandRequest`, 예: `/item` | 현재 WorldPlayer의 CashItemManager로 도메인 상태 변경 | 해당 플레이어를 처리하는 WorldServer 필요 |

## 웹 Admin API

[PlayersController.UpdatePlayer](</E:/Projects/idlez-server/Server/ApiServer/Controllers/AdminApi/PlayersController.cs:36>)는 ID 일치를 확인한 뒤 `await player.SaveAsync()`를 호출한다. POST 생성도 같은 저장 메서드를 사용한다. [PlayerTelegramsController](</E:/Projects/idlez-server/Server/ApiServer/Controllers/AdminApi/PlayerTelegramsController.cs:39>)도 동일한 직접 저장 형태다.

[Model.SaveAsync](</E:/Projects/idlez-server/Server/Server/Models/Model.cs:96>)는 새 모델이면 INSERT, 기존 모델이면 `DbManager.WithSessionAsync(db => db.UpdateAsync((T)this))`를 실행한다. [DbManager.WithSessionAsync](</E:/Projects/idlez-server/Server/Server/Managers/DbManager.cs:70>)가 직접 PostgreSQL 연결을 연다. controller의 저장 경로에 WorldServer 명령, 해당 플레이어의 세마포어 획득, 캐시 무효화/재로딩 통지는 없다.

[ApiServer.Program](</E:/Projects/idlez-server/Server/ApiServer/Program.cs:16>)은 DB를 초기화하고 HTTP controller를 등록한다([MapControllers](</E:/Projects/idlez-server/Server/ApiServer/Program.cs:71>)). 따라서 **소스 구조상 해당 수정 API는 ApiServer와 DB가 동작하면 처리 가능하고 WorldServer의 명령 처리를 기다리지 않는다**. 실제 배포에서 프로세스들을 함께 기동/종료하는지는 별개이며 조사하지 않았다.

Admin API가 완성된 운영 명령 시스템이라는 뜻은 아니다. [AdminController.Post](</E:/Projects/idlez-server/Server/ApiServer/Controllers/AdminApi/AdminController.cs:56>)는 전달받은 Action의 메시지만 반환하며, [플레이어 DELETE](</E:/Projects/idlez-server/Server/ApiServer/Controllers/AdminApi/PlayersController.cs:45>)는 `not implemented` 상태다. 이 checkout에서 확인한 수정 API는 Player/Telegram 모델 CRUD이며, 이를 운영툴 전체의 모든 기능으로 일반화하지 않는다.

## 게임 내 변경과 저장

[WorldPlayer.SendCommand](</E:/Projects/idlez-server/Server/WorldServer/WorldPlayer/Handler/Debug/WorldPlayer.SendCommand.cs:16>)는 debug 또는 관리자 여부를 확인하고, `/item`에서 [CashItemManager.AddItem](</E:/Projects/idlez-server/Server/WorldServer/WorldPlayer/Handler/Debug/WorldPlayer.SendCommand.cs:79>)을 호출한다. HTTP Admin controller를 통과하는 경로가 아니다.

플레이어 [Update](</E:/Projects/idlez-server/Server/Server/Player/Player.Logic.cs:43>)는 플레이어 세마포어를 확보하고 패킷·tick 처리를 실행한다. [정기/조건부 저장](</E:/Projects/idlez-server/Server/WorldServer/WorldPlayer/WorldPlayer.Logic.cs:15>)은 일반 30초/debug 5초 또는 즉시 저장 조건에 따라 동작한다. [SaveInternal](</E:/Projects/idlez-server/Server/WorldServer/WorldPlayer/WorldPlayer.cs:101>)은 같은 DB transaction으로 도메인 모듈들을 저장하고 `Model.Dirty`이면 보관 중인 PlayerModel을 저장한다.

## version과 캐시 일관성

[PlayerModel](</E:/Projects/idlez-server/Server/Server/Models/PlayerModel.cs:12>)과 위 일반 저장 경로에는 Bun3의 플레이어별 영속 version 비교 계약이 없다. [기반 Model의 updated_at](</E:/Projects/idlez-server/Server/Server/Models/Model.cs:12>)은 ReadOnly 모델 필드이며 이 저장 경로에서 기대값 조건으로 비교하지 않는다.

코드상 추론: 접속 중 PlayerModel을 Admin API에서 직접 바꾸면 게임 서버의 기존 객체는 그 변경을 즉시 반영하지 않는다. 이후 `Model.Dirty`로 게임 서버가 기존 객체를 저장하면, 일반 UPDATE 대상 필드에서 Admin 변경이 덮일 가능성이 있다. 특정 필드의 재계산/개별 SQL 경로는 추가로 작용할 수 있으므로 모든 Admin 변경이 다음 저장에서 반드시 사라진다고 단정하지 않는다. 실제 경쟁 실행이나 운영 사고는 재현하지 않았다.

앞선 대화의 `version=12 → 13` 사례는 Bun3 설계 설명용 예시이며 idlez의 현재 구현을 나타낸 것이 아니다. 확인한 idlez Admin API는 DB 직접 수정 경로이고, 동시 접속 캐시와의 버전 충돌 검출까지 갖춘 구조로 확인되지는 않았다.
