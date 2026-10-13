# 레거시의 저장 배치·공유 상태·다인 트랜잭션 비교

- 조사일: 2026-10-03.
- 질문: 서로 다른 플레이어의 같은 테이블 저장, 공유 행 갱신, 플레이어 간 이전, 저장 실패 후 복구를 각 게임이 어떻게 처리하는가.
- 방법: 로컬 소스의 호출자, SQL 실행, 예외 처리, 재접속 경로와 컴파일 목록을 확인했다. 외부 게임 파일 수정, 서버 실행, DB 연결, 성능 측정은 하지 않았다.
- 범위: 일반 플레이어 저장과 대표 활성 기능이다. 운영 DB 엔진 설정·격리 수준·배포 구조나 실제 장애 발생을 검증한 문서는 아니다.

## 조사 기준

| 저장소 | HEAD | 조사한 작업 트리 상태 |
|---|---|---|
| `E:/Projects/idlez-server` | `b39749fecdde973ea30875a7556990c6e09f888b` | 기존 `Server/Commons` 변경 및 `Server/.vscode/`, `commits_client.txt` 미추적 파일 유지 |
| `E:/Projects/growninja` | `cc392aef05e86e7481182d5b14cf165742249fcf` | 조사 시작·종료 시 clean |
| `E:/Projects/growdaemon` | `24fb76dc437d015f8b71b43826ab98d7a7a6bfb3` | 기존 `Client/Client.sln.DotSettings.user`, `Client/ProjectSettings/ProjectSettings.asset` 변경 유지 |

링크의 줄 번호는 조사 당시 작업 트리를 기준으로 한다. 이전 조사 이후 growninja의 HEAD가 달라졌으므로 이 문서의 판단은 위 리비전에 한정한다.

## 비교 요약

| 항목 | idlez | growninja | growdaemon |
|---|---|---|---|
| 일반 저장의 SQL 트랜잭션 | 플레이어의 아이템·업적·랭킹·로그 등을 같은 트랜잭션으로 저장 | 한 플레이어의 인벤토리만 같은 트랜잭션. 다른 모듈은 별도 | 조사한 일반 경로에 공동 트랜잭션 없이 행별 SQL |
| 여러 플레이어의 정기 저장 | 플레이어별 일반 30초 주기 | 전역 스레드가 일반 180초마다 순회 | 전역 스레드가 일반 60초마다 순회 |
| 일반 저장의 서버 공통 SQL 배치 큐 | 확인되지 않음 | 전체 순회는 있으나 플레이어 간 SQL 배치 아님 | 전체 순회는 있으나 플레이어 간 SQL 배치 아님 |
| 공유 행 갱신 대표 사례 | 랭킹 누적 SQL. 대부분 키는 플레이어별 | 클랜 EXP·클랜 변수의 SQL 증분 | 클랜 EXP의 SQL 증분, 일부 선물 기록은 읽고 절대값 저장 |
| 다인 업무 대표 사례 | 방어 랭크전 두 플레이어를 잠그고 공동 SQL 트랜잭션 | 클랜 카드 차감·요청 갱신·수신 처리가 각각 저장 | 클랜장 양도와 전용 아이템 이전이 각각 저장 |
| 일반 저장 실패 | 기본적으로 플레이어 종료. 종료 중 재저장 있음 | 정기 저장은 예외 로그. 강제 캐시 폐기 정책 아님 | 정기 저장은 예외 로그. 강제 캐시 폐기 정책 아님 |

전역 루프, 하나의 SQL 트랜잭션, 다중 행 SQL은 서로 다른 기능이다. 위 표의 일반 저장에서는 전체 플레이어 변경을 모아 공통 bulk SQL로 보내는 구조를 확인하지 못했다. 저장소 전체에 집합 SQL이 없다는 뜻은 아니다.

## idlez

### 일반 저장

`WorldPlayer`가 운영 30초/debug 5초 주기 또는 저장 조건에 따라 `SaveAsync()`를 실행한다. `SaveInternal()`은 같은 connection/transaction을 아이템·업적·랭킹·로그·추가 저장 작업·플레이어 모델에 전달한다. 근거: [저장 주기](/E:/Projects/idlez-server/Server/WorldServer/WorldPlayer/WorldPlayer.Logic.cs:15), [저장 호출과 구성](/E:/Projects/idlez-server/Server/WorldServer/WorldPlayer/WorldPlayer.cs:81), [BEGIN·COMMIT·ROLLBACK](/E:/Projects/idlez-server/Server/Server/Managers/DbManager.cs:100).

아이템과 업적은 각 모델의 `SaveAsync(db, transaction)`을 차례로 실행한다. 한 트랜잭션이라는 점과 별개로 행별 SQL이다. 근거: [아이템 저장](/E:/Projects/idlez-server/Server/WorldServer/Managers/CashItemManager/CashItemManager.cs:299), [업적 저장](/E:/Projects/idlez-server/Server/WorldServer/Managers/AchievementManager/AchievementManager.cs:110), [모델 UPDATE](/E:/Projects/idlez-server/Server/Server/Models/Model.cs:106).

### 다인 업무와 공유 행

`RunMultiPlayerTasks()`가 이미 존재한다. 참여자를 PlayerId로 정렬하고 로그인 세마포어와 온라인 플레이어 세마포어를 확보한다. 게임 작업을 실행한 다음 하나의 `WithTransactionAsync`에서 온라인 플레이어의 저장과 오프라인 대상의 `SaveFunctions`를 수행한다. 이는 지정된 다인 업무를 묶는 경로이며, 정기 저장을 모으는 전역 배치 큐는 아니다. 근거: [다인 작업 조정](/E:/Projects/idlez-server/Server/Server/Server.Players.cs:279).

실제 호출은 방어 랭크전 결과 처리다. 본인과 상대의 랭크 포인트를 변경하고 양쪽 랭킹·로그 저장을 준비한다. 온라인이면 메모리 모델, 오프라인이면 DB 모델을 사용한다. 근거: [방어 랭크전 호출](/E:/Projects/idlez-server/Server/WorldServer/WorldPlayer/WorldPlayer.Events.cs:94).

랭킹 누적은 `ON CONFLICT ... score = player_rankings.score + EXCLUDED.score`로 DB에서 계산한다. 다만 키가 `(player_id, ranking_id, date)`이므로 같은 랭킹 테이블을 이용하는 여러 플레이어와 같은 행을 함께 수정하는 상황을 구분해야 한다. 근거: [랭킹 갱신](/E:/Projects/idlez-server/Server/Server/Models/PlayerRankingModel.cs:67).

### 실패 복구의 범위

일반 `SaveAsync()`는 예외를 잡아 기본적으로 `DestroyInternal()`을 호출한다. 기반 구현은 플레이어 목록에서 제거하고 연결을 닫는다. 다음 로그인은 새 `WorldPlayer`를 만들고 아이템을 DB에서 읽는다. 근거: [저장 catch](/E:/Projects/idlez-server/Server/WorldServer/WorldPlayer/WorldPlayer.cs:90), [제거·연결 종료](/E:/Projects/idlez-server/Server/Server/Player/Player.Logic.cs:134), [새 플레이어 생성](/E:/Projects/idlez-server/Server/WorldServer/WorldServer.Players.cs:109), [아이템 로드](/E:/Projects/idlez-server/Server/WorldServer/Managers/CashItemManager/CashItemManager.cs:39).

다음 이유로 이를 완전한 실패 상태 폐기 정책으로 일반화하면 안 된다.

- 파생 `DestroyInternal()`은 `SaveAsync(destroyIfFailed: false)`를 다시 호출한다. 실패한 메모리를 재저장하지 않고 버리는 계약과 다르다. [종료 중 재저장](/E:/Projects/idlez-server/Server/WorldServer/WorldPlayer/WorldPlayer.Logic.cs:132)
- `RunMultiPlayerTasks()`의 바깥 catch는 로그만 남긴다. 최종 공동 COMMIT 실패 시 참여 플레이어 전체를 무효화하는 코드는 없다. 개별 `SaveAsync`가 오류를 내부에서 잡는 점도 오류 전파 계약을 복잡하게 만든다. [다인 저장과 catch](/E:/Projects/idlez-server/Server/Server/Server.Players.cs:320)
- 방어 랭크전의 푸시 레코드는 `work()` 안에서 별도 `SaveAsync()`로 저장한다. 그 작업은 공동 SQL 트랜잭션 시작 전이므로 결과 전체가 같은 원자 범위라는 뜻은 아니다. [푸시 저장](/E:/Projects/idlez-server/Server/WorldServer/WorldPlayer/WorldPlayer.Events.cs:173)
- Dirty 해제와 큐 소비 중 일부가 최종 COMMIT보다 먼저 일어난다. 실패 후 같은 객체로 다시 저장하면 원래 저장분 전체를 재시도한다고 보장할 수 없다. [인벤토리 Dirty 해제](/E:/Projects/idlez-server/Server/WorldServer/Managers/CashItemManager/CashItemManager.cs:299), [모델 Dirty 해제](/E:/Projects/idlez-server/Server/Server/Models/PlayerItemModel.cs:281), [로그 큐 소비](/E:/Projects/idlez-server/Server/WorldServer/Managers/PlayerLogManager/PlayerLogManager.cs:47)

## growninja

### 일반 저장

전역 저장 스레드는 운영 180초/debug 5초마다 플레이어를 순회하며 인벤토리와 업적의 `Save()`를 각각 호출한다. `CashItemManager.Save()`만 연결 하나와 트랜잭션 하나를 열고 그 플레이어의 아이템을 행별로 저장한다. 근거: [전역 저장 스레드](/E:/Projects/growninja/Commons/Server/WorldManager.cs:126), [인벤토리 트랜잭션](/E:/Projects/growninja/Commons/Server/CashItemManager.cs:494), [행별 SQL](/E:/Projects/growninja/Commons/Server/Models/PlayerItemModel.cs:484).

`WorldPlayer.Save()`도 아바타·인벤토리·업적·플레이어 필드·펫 등을 별도로 저장한다. 플레이어 전체를 하나의 DB 트랜잭션으로 묶는 경로는 아니다. DB helper는 연결 수명만 관리한다. 근거: [전체 저장](/E:/Projects/growninja/Commons/Server/WorldPlayer.cs:295), [DB helper](/E:/Projects/growninja/Commons/Server/DBManager.cs:39).

### 공유 행과 다인 업무

클랜 기여 아이템 처리가 개인 기여도와 클랜 EXP 갱신으로 연결된다. 클랜 EXP는 `UPDATE clans SET exp = exp + @amount`를 사용한다. 레벨 갱신과 클랜장 보상은 별도 SQL이다. 따라서 EXP 증가분을 DB에서 누적하는 것과 기여 업무 전체의 원자성은 다르다. 근거: [활성 호출](/E:/Projects/growninja/Commons/Server/CashItemManager.AddItem.cs:501), [개인 기여·클랜 연결](/E:/Projects/growninja/Commons/Server/WorldPlayer.cs:1364), [클랜 EXP·레벨·보상](/E:/Projects/growninja/Commons/Server/Models/ClanModel.cs:609).

`ClanModel.AddEXP`의 `lock(this)`는 같은 객체 안의 잠금이다. `RefreshClan()`에서 DB 모델을 새로 조회하는 경로와 같은 객체를 회원에게 전파하는 경로가 함께 있으므로, 항상 하나인 클랜별 잠금이나 분산 잠금이라고 볼 수 없다. 근거: [클랜 로드](/E:/Projects/growninja/Commons/Server/WorldPlayer.cs:189), [DB 조회](/E:/Projects/growninja/Commons/Server/Models/ClanModel.cs:163), [회원에게 객체 전파](/E:/Projects/growninja/Commons/Server/Models/ClanModel.cs:688).

클랜 카드 전달은 발신 횟수 저장 → 발신 아이템 차감 저장 → 요청 상태 저장 → 수신자 badge 저장을 각각 수행한다. 이후 별도 수령 요청에서 아이템 지급 또는 환불 메일을 처리하고 요청을 삭제한다. 두 경로에 공동 SQL 트랜잭션은 없다. 근거: [카드 전달](/E:/Projects/growninja/Commons/Server/WorldPlayer.Handler.cs:6692), [카드 수령](/E:/Projects/growninja/Commons/Server/WorldPlayer.Handler.cs:6586), [요청 모델 저장](/E:/Projects/growninja/Commons/Server/Models/ClanItemRequestModel.cs:67).

코드상 추론: 중간 SQL 실패 시 앞선 저장이 남을 수 있다. 발신자의 `clanLock`은 플레이어별이고, 요청 상태에 대한 조건부 UPDATE도 없어 서로 다른 발신자의 같은 요청 처리까지 직렬화하는 구조는 아니다. 실제 경쟁 실행은 재현하지 않았다. [플레이어별 잠금](/E:/Projects/growninja/Commons/Server/WorldPlayer.cs:143)

### 실패와 재접속

정기 저장 예외는 전체 플레이어 순회 바깥에서 로그로 처리한다. 인벤토리 예외가 나면 그 회차의 뒤쪽 플레이어까지 건너뛸 수 있다. 업적 저장은 내부 catch에서 경고만 남기고 `changed=false`를 설정한다. 아이템 `SaveIfDirty`도 SQL 전에 `dirty=false`로 바꾼다. 따라서 자동 재시도나 실패한 플레이어의 강제 폐기를 보장하지 않는다. 근거: [순회 catch](/E:/Projects/growninja/Commons/Server/WorldManager.cs:133), [아이템 Dirty 해제](/E:/Projects/growninja/Commons/Server/Models/PlayerItemModel.cs:492), [업적 catch](/E:/Projects/growninja/Commons/Server/AchievementManager.cs:303).

소켓 단절은 세션 참조를 정리하지만 플레이어 캐시는 바로 제거하지 않는다. 플레이어의 120초 ping timeout 등이 `Destroy()`를 호출한다. `Destroy`는 destroyed 표시 → DB server ID 갱신 → 저장 → 연결 종료·목록 제거 순서라, 저장 예외가 전파되면 마지막 제거에 도달하지 못할 수 있다. 근거: [단절 처리](/E:/Projects/growninja/Commons/Server/WorldSession.cs:52), [timeout](/E:/Projects/growninja/Commons/Server/WorldPlayer.cs:1439), [Destroy 순서](/E:/Projects/growninja/Commons/Server/WorldPlayer.cs:441).

로그인 시 캐시가 있으면 기존 Player를 갱신·재연결하고, 없을 때 새 Player와 인벤토리·업적을 로드한다. 단절만으로 DB 전체 재로딩이 강제되는 계약은 아니다. [로그인 캐시 분기](/E:/Projects/growninja/Commons/Server/WorldManager.cs:352)

## growdaemon

### 일반 저장

전역 저장 스레드는 운영 60초/debug 5초마다 플레이어별 인벤토리·업적을 순회 저장한다. 인벤토리는 아이템마다 `SaveIfDirty()`를 호출하며 각 모델이 별도 `WithSession`에서 SQL을 실행한다. 조사한 경로에는 플레이어 전체 또는 다인 저장을 묶는 명시적 DB 트랜잭션이 없다. 근거: [정기 저장](/E:/Projects/growdaemon/Commons/Server/WorldManager.cs:107), [인벤토리 순회](/E:/Projects/growdaemon/Commons/Server/CashItemManager.cs:199), [행별 저장](/E:/Projects/growdaemon/Commons/Server/Models/PlayerItemModel.cs:302), [DB helper](/E:/Projects/growdaemon/Commons/Server/DBManager.cs:39).

`WorldPlayer.Save()`도 아바타·아이템·업적·경험치·재화·펫 등을 따로 저장한다. 운영 DB의 autocommit 설정까지 조사한 것은 아니며, 애플리케이션의 공동 트랜잭션이 없다는 판단이다. [전체 저장](/E:/Projects/growdaemon/Commons/Server/WorldPlayer.cs:273)

### 공유 행과 다인 업무

클랜 기여도는 개인 필드의 SQL 증분과 `ClanModel.AddEXP()`로 이어진다. 클랜 EXP도 `exp = exp + @amount`이며 이후 레벨과 랭킹 저장은 별도다. 객체 잠금은 있지만 클랜 모델 재조회 경로도 있으므로 분산된 모든 변경을 하나의 잠금이 보호한다고 보장할 수 없다. 근거: [활성 기여 호출](/E:/Projects/growdaemon/Commons/Server/CashItemManager.cs:2810), [개인·클랜·랭킹 연결](/E:/Projects/growdaemon/Commons/Server/WorldPlayer.cs:1056), [클랜 EXP](/E:/Projects/growdaemon/Commons/Server/Models/ClanModel.cs:565), [클랜 재조회](/E:/Projects/growdaemon/Commons/Server/WorldPlayer.cs:174).

`GiveClan`은 클랜장·부클랜장 변경과 전용 아이템 이전을 처리하는 활성 다인 사례다. 기존 장의 아이템 수량을 0으로 저장한 뒤 새 장에게 지급·저장한다. 온라인 대상의 인벤토리 잠금은 개별적으로 확보하며, 전체를 같은 SQL 트랜잭션으로 묶지 않는다. 코드상 추론으로, 중간 저장 실패 시 차감만 남을 수 있다. 근거: [클랜장 양도](/E:/Projects/growdaemon/Commons/Server/WorldPlayer.Handler.cs:6452), [이전 장 차감](/E:/Projects/growdaemon/Commons/Server/WorldPlayer.Handler.cs:6524), [새 장 지급](/E:/Projects/growdaemon/Commons/Server/WorldPlayer.Handler.cs:6540).

하트·편지 선물 `SendItem`도 수신 기록 → 발신 기록 → 우편 INSERT를 각각 저장한다. 오프라인 수신자의 기록은 DB에서 읽고 C#에서 횟수를 올린 뒤 절대값을 저장한다. 해당 경로에는 여러 발신자에 공통인 수신자 잠금·버전 검사·조건부 갱신이 없다. 코드상 추론으로 동시 요청의 증가분 누락 또는 부분 저장이 가능하며, 실제 발생은 검증하지 않았다. 근거: [선물 처리](/E:/Projects/growdaemon/Commons/Server/WorldPlayer.Handler.cs:5345), [오프라인 수신 기록](/E:/Projects/growdaemon/Commons/Server/WorldPlayer.Handler.cs:5477), [발신 기록·메일](/E:/Projects/growdaemon/Commons/Server/WorldPlayer.Handler.cs:5554).

### 실패와 재접속

정기 저장은 예외 로그 후 계속 실행된다. 아이템은 SQL 전에 `dirty=false`가 되고, 업적은 실패 catch에서 `changed=false`를 지운다. 저장 실패가 Player 폐기나 다음 회차의 동일 저장분 재시도로 연결되는 정책은 아니다. 근거: [정기 저장 catch](/E:/Projects/growdaemon/Commons/Server/WorldManager.cs:114), [아이템 Dirty 해제](/E:/Projects/growdaemon/Commons/Server/Models/PlayerItemModel.cs:310), [업적 catch](/E:/Projects/growdaemon/Commons/Server/AchievementManager.cs:259).

소켓 종료는 플레이어 캐시 제거와 다르다. `Destroy()`는 destroyed 표시 → DB 갱신·저장 → 연결 종료·캐시 제거 순서다. 따라서 저장 예외로 제거에 도달하지 못할 가능성이 있다. 재접속 시 기존 Player가 있으면 재사용하며 `RefreshModel()`도 먼저 Save하고 플레이어 모델만 다시 읽는다. 아이템·업적까지 새로 로드하는 것은 새 Player를 만드는 경로다. 근거: [세션 종료](/E:/Projects/growdaemon/Commons/Server/WorldSession.cs:53), [Destroy](/E:/Projects/growdaemon/Commons/Server/WorldPlayer.cs:375), [로그인 재사용](/E:/Projects/growdaemon/Commons/Server/WorldManager.cs:219), [RefreshModel](/E:/Projects/growdaemon/Commons/Server/WorldPlayer.cs:1199), [새 Player](/E:/Projects/growdaemon/Commons/Server/WorldManager.cs:247).

## 검색 범위와 부정 결론의 제한

- idlez: `Server/Server`, `WorldServer` 중심으로 `EdgeServer`, `ApiServer`의 저장 호출까지 검색했다. 생성 코드·`Commons`·빌드 산출물·마이그레이션은 일반 저장 경로 검색에서 제외했다. `RunMultiPlayerTasks`, `WithTransactionAsync`, 배치 API와 공유 상태 갱신을 추적했다.
- growninja/growdaemon: `Commons`, `RemoteServer`, `WebServer`에서 트랜잭션 생성·commit/rollback·행 잠금·bulk API를 검색하고, 실제 패킷 처리와 프로젝트 컴파일 목록을 확인했다.
- 두 프로젝트의 구 `Server/PlayerUnit*` 거래 코드는 실제 공유 프로젝트의 컴파일 목록과 다르다. 거래소 코드는 `#if REMOTE_SERVER && false`로 비활성이다. growdaemon의 카드 전달 코드 일부도 주석이다. 따라서 활성 사례로 growninja의 클랜 카드와 growdaemon의 양도·선물을 사용했다. 근거: [growninja 컴파일 목록](/E:/Projects/growninja/Commons/Commons.projitems:178), [growdaemon 컴파일 목록](/E:/Projects/growdaemon/Commons/Commons.projitems:156), [비활성 거래소](/E:/Projects/growdaemon/Commons/Server/PlayerUnit.Handler.Exchange.cs:14).
- 저장소에 집합 SQL은 있다. idlez 푸시는 `id = ANY(@ids)`로 일괄 갱신하며, growdaemon의 랭킹 집계에는 `INSERT ... SELECT`가 있다. growninja의 랭킹 bulk upsert는 마이그레이션용이며 찾은 기동 호출은 주석이다. 일반 플레이어 저장의 공통 배치와 구분해야 한다. 근거: [idlez 푸시](/E:/Projects/idlez-server/Server/Server/Models/PlayerPushModel.cs:48), [growdaemon 랭킹 집계](/E:/Projects/growdaemon/Commons/Server/Models/PlayerRankingDailyModel.cs:331), [growninja bulk](/E:/Projects/growninja/Commons/Server/Models/PlayerRankingDailyModel.cs:80), [주석 처리된 호출](/E:/Projects/growninja/Commons/Server/WorldServer.cs:214).

## Bun3 설계에 주는 근거

1. idlez의 다인 업무 조정은 이미 존재하는 요구의 근거다. 저장 단위를 플레이어 하나로 고정할 이유는 없다.
2. 세 게임의 일반 저장에서 확인한 전역 순회·행별 저장과, 여러 플레이어 변경을 SQL 배치로 합치는 공통 저장기는 다르다. 후자는 Bun3에 새로 설계할 기능이며 성능 개선 폭은 측정해야 한다.
3. 공유 행의 SQL 증분은 일부 경쟁을 처리하지만, 여러 행·여러 플레이어의 업무 전체를 원자적으로 만들지는 않는다.
4. 선택한 저장 실패 정책은 프레임워크가 추가 요청·저장을 막고, 실패한 원자 저장 묶음의 영향받는 메모리 상태를 폐기한 후 새 DB 로드를 강제하는 계약으로 구현해야 한다. 이 보장이 레거시 세 프로젝트에 공통으로 완성되어 있다고 가정하면 안 된다.
5. 함께 확정해야 하는 업무 묶음과 성능을 위해 합치는 실행 배치를 구분해야 한다. 하나의 실제 DB 트랜잭션에 여러 묶음을 합치면, 실패 시 폐기 대상도 그 참여자들로 넓어진다.
