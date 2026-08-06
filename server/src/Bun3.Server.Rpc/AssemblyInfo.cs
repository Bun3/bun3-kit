using System.Runtime.CompilerServices;

// OnGateRequest는 protected internal — 임의 NuGet 소비자가 아닌, 프레임워크가 지정한
// 신뢰 어셈블리(테스트, Players 등 공식 확장 모듈)만 재정의할 수 있도록 제한한다.
[assembly: InternalsVisibleTo("Bun3.Server.Tests")]
