# Gameplay Tags

게임과 프레임워크가 공유하는 계층형 Gameplay Tag의 작성 출처, 병합 의미, 배포 계약을 정의한다.

## Language

**Gameplay Tag**:
`.`으로 계층을 표현하는 소문자 canonical 이름이다. Source가 달라도 canonical 이름이 같으면 같은 태그다.
_Avoid_: Source Tag, Runtime Tag ID

**Tag Source**:
태그 선언, Source별 comment, Redirect의 작성 출처이자 소유 단위다. Source는 태그 identity의 일부가 아니다.
_Avoid_: Tag Namespace, Catalog

**Explicit Tag**:
Tag Source에 직접 작성된 태그다.
_Avoid_: Root Tag, Authored Node

**Implicit Tag**:
자식 태그의 계층 때문에 파생되었지만 Tag Source에 직접 작성되지 않은 유효 태그다. Explicit Tag와 마찬가지로 선택하고 런타임에서 사용할 수 있다.
_Avoid_: Folder, Group Node

**Game Source**:
게임 프로젝트가 직접 소유하고 편집하는 기본 Tag Source다.
_Avoid_: Default Catalog, User Tags

**GameplayTag Project Settings**:
Unity Editor가 소유하는 Catalog ID 설정이며 Tag Source가 아니다.
_Avoid_: Tag Source, Catalog

**Runtime Catalog**:
모든 Tag Source를 canonical 이름으로 병합한 단일 불변 태그 집합이다. Source별 comment와 소유 정보는 포함하지 않는다.
_Avoid_: Tag Source, Editor Catalog

**Published Catalog**:
특정 Catalog Version으로 게시되어 Unity 클라이언트와 서버가 함께 사용하는 Runtime Catalog다.
_Avoid_: Unity Catalog, Server Catalog

**Local Development Catalog**:
로컬 클라이언트와 서버 개발에서 공유하는 미게시 Runtime Catalog다.
_Avoid_: Test Catalog, Temporary Tags

**Catalog Version**:
Published Catalog를 선택하는 명시적인 배포 버전이다. 태그를 편집할 때마다 자동으로 증가하지 않는다.
_Avoid_: Schema Version, Package Version

**Semantic Fingerprint**:
태그 이름, 계층, 인덱스와 Redirect의 의미가 같은지를 식별하는 결정론적 값이다.
_Avoid_: File Checksum, Catalog Version

**Redirect**:
더 이상 사용하지 않는 canonical 태그 이름을 현재 태그로 해석하기 위한 호환성 mapping이다.
_Avoid_: Alias, Display Name
