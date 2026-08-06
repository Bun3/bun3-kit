# Bun3.Server.Auth Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Players 신원 모델의 1층(자격증명 검증) 구현 — 검증기 계약 + GuestVerifier + Steam 검증기 2종(Web API / 네이티브 위임).

**Architecture:** `Bun3.Server.Auth`(계약 + Guest, 의존 0) + `Bun3.Server.Auth.Steam`(Web API 검증기 + BeginAuthSession 상관관계 검증기). 검증기는 게임 로그인 핸들러 안에서 조합되는 평범한 객체 — **Players·Rpc·Hosting 무변경**. 스펙: `docs/superpowers/specs/2026-08-07-server-auth-design.md`.

**Tech Stack:** netstandard2.1 + C#9, System.Text.Json 8.0.5(Steam 패키지만), NUnit 4(net10.0 테스트).

## Global Constraints

- 두 패키지 모두 `netstandard2.1` + `LangVersion 9.0` + `Nullable enable`, 블록 네임스페이스(파일 스코프 금지 — C#9).
- 의존성: `Bun3.Server.Auth`는 **의존 0**. `Bun3.Server.Auth.Steam`은 `Bun3.Server.Auth` 프로젝트 참조 + `System.Text.Json 8.0.5`만.
- net5+ 전용 API 금지: `Convert.FromHexString`, `ReadAsStringAsync(ct)`, `Task.WaitAsync` 등 사용 불가(테스트 코드는 net10.0이라 허용).
- 모든 public 멤버에 한국어 XML 문서 주석, `GenerateDocumentationFile true`, **빌드 경고 0**.
- 라이브러리 코드의 모든 await에 `ConfigureAwait(false)`.
- 검증 판정은 값(`AuthResult`), 인프라 실패만 예외. 호출자 `CancellationToken` 취소는 `OperationCanceledException`, `Timeout` 옵션 초과는 `AuthFailure.Timeout` 값.
- 옵션은 생성자에서 검증 후 **필드로 스냅샷**(라이브 읽기 금지 — PlayersOptions 교훈).
- 두 Steam 검증기 모두 `Provider == "steam"`, Subject는 SteamID64 문자열(`InvariantCulture`).
- 패키지 버전 둘 다 `0.1.0`. 커밋은 gitmoji + `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>` 트레일러.
- 테스트는 `server/tests/Bun3.Server.Tests`(NUnit 4, net10.0, 명시적 `using NUnit.Framework;`)에 추가.

---

### Task 1: `Bun3.Server.Auth` 계약 패키지 + GuestVerifier

**Files:**
- Create: `server/src/Bun3.Server.Auth/Bun3.Server.Auth.csproj`
- Create: `server/src/Bun3.Server.Auth/ProviderIdentity.cs`
- Create: `server/src/Bun3.Server.Auth/AuthFailure.cs`
- Create: `server/src/Bun3.Server.Auth/AuthResult.cs`
- Create: `server/src/Bun3.Server.Auth/IIdentityVerifier.cs`
- Create: `server/src/Bun3.Server.Auth/GuestVerifier.cs`
- Modify: `Bun3.sln` (dotnet sln add)
- Modify: `server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj` (프로젝트 참조 추가)
- Test: `server/tests/Bun3.Server.Tests/AuthTests.cs`

**Interfaces:**
- Consumes: 없음 (신규 최하층 패키지)
- Produces (Task 2·3·4가 사용):
  - `readonly struct ProviderIdentity { string Provider; string Subject; string ToAccountKey(); }` + `ProviderIdentity(string provider, string subject)`
  - `enum AuthFailure { None=0, InvalidCredential=1, Rejected=2, Banned=3, IdentityMismatch=4, Timeout=5 }`
  - `class AuthResult { bool Succeeded; ProviderIdentity Identity; AuthFailure Failure; string? Error; }` + `protected AuthResult(bool, ProviderIdentity, AuthFailure, string?)` + `static AuthResult Success(ProviderIdentity)` + `static AuthResult Fail(AuthFailure, string? error = null)`
  - `interface IIdentityVerifier { string Provider { get; } ValueTask<AuthResult> VerifyAsync(string credential, CancellationToken ct = default); }`
  - `sealed class GuestVerifier : IIdentityVerifier` (Provider="guest")

- [ ] **Step 1: 프로젝트 생성 + 솔루션/테스트 참조 연결**

`server/src/Bun3.Server.Auth/Bun3.Server.Auth.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <Nullable>enable</Nullable>
    <RootNamespace>Bun3.Server.Auth</RootNamespace>
    <PackageId>Bun3.Server.Auth</PackageId>
    <Version>0.1.0</Version>
    <Authors>Bun3</Authors>
    <RepositoryUrl>https://github.com/Bun3/bun3-kit</RepositoryUrl>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <Description>제공자 검증기 계약(IIdentityVerifier/AuthResult) + GuestVerifier — Players 신원 모델의 1층</Description>
  </PropertyGroup>

</Project>
```

Run (레포 루트에서):

```powershell
dotnet sln Bun3.sln add server/src/Bun3.Server.Auth/Bun3.Server.Auth.csproj
```

`server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj`의 ProjectReference ItemGroup에 추가:

```xml
    <ProjectReference Include="..\..\src\Bun3.Server.Auth\Bun3.Server.Auth.csproj" />
```

- [ ] **Step 2: 실패하는 테스트 작성**

`server/tests/Bun3.Server.Tests/AuthTests.cs`:

```csharp
using Bun3.Server.Auth;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class AuthTests
{
    [Test]
    public void ProviderIdentity_ToAccountKey_formats_provider_colon_subject()
    {
        var identity = new ProviderIdentity("steam", "76561198000000001");
        Assert.That(identity.ToAccountKey(), Is.EqualTo("steam:76561198000000001"));
    }

    [Test]
    public async Task GuestVerifier_accepts_and_trims_device_id()
    {
        var verifier = new GuestVerifier();
        Assert.That(verifier.Provider, Is.EqualTo("guest"));

        var result = await verifier.VerifyAsync("  device-abc  ");
        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Failure, Is.EqualTo(AuthFailure.None));
        Assert.That(result.Identity.Provider, Is.EqualTo("guest"));
        Assert.That(result.Identity.Subject, Is.EqualTo("device-abc"));
        Assert.That(result.Identity.ToAccountKey(), Is.EqualTo("guest:device-abc"));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("dev:ice")]            // 키 규약 오염 — ':' 금지
    public async Task GuestVerifier_rejects_invalid_credential(string credential)
    {
        var result = await new GuestVerifier().VerifyAsync(credential);
        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Failure, Is.EqualTo(AuthFailure.InvalidCredential));
    }

    [Test]
    public async Task GuestVerifier_rejects_over_128_chars()
    {
        var result = await new GuestVerifier().VerifyAsync(new string('a', 129));
        Assert.That(result.Failure, Is.EqualTo(AuthFailure.InvalidCredential));

        var ok = await new GuestVerifier().VerifyAsync(new string('a', 128));
        Assert.That(ok.Succeeded, Is.True);
    }
}
```

- [ ] **Step 3: 실패 확인**

Run: `dotnet test server/tests/Bun3.Server.Tests --filter "FullyQualifiedName~AuthTests"`
Expected: 컴파일 오류 (`ProviderIdentity` 미정의)

- [ ] **Step 4: 구현**

`server/src/Bun3.Server.Auth/ProviderIdentity.cs`:

```csharp
namespace Bun3.Server.Auth
{
    /// <summary>제공자 검증을 통과한 신원 — (제공자, 제공자 내 고유 id) 쌍.</summary>
    public readonly struct ProviderIdentity
    {
        /// <summary>제공자 이름(소문자 규약) — "guest", "steam" 등.</summary>
        public string Provider { get; }

        /// <summary>제공자 내 고유 id — SteamID64, device-id 등.</summary>
        public string Subject { get; }

        /// <summary>신원을 생성한다.</summary>
        public ProviderIdentity(string provider, string subject)
        {
            Provider = provider;
            Subject = subject;
        }

        /// <summary>Players 권장 규약("provider:subject")의 accountKey 문자열을 만든다.
        /// 계정 연동을 쓰는 게임은 이 값 대신 연동 테이블 조회 결과("acct:{id}")를 쓴다.</summary>
        public string ToAccountKey() => $"{Provider}:{Subject}";
    }
}
```

`server/src/Bun3.Server.Auth/AuthFailure.cs`:

```csharp
namespace Bun3.Server.Auth
{
    /// <summary>검증 실패 사유 — 제공자와 무관한 공통 어휘. 게임은 이 값을 자기 proto 에러코드로 매핑한다.</summary>
    public enum AuthFailure
    {
        /// <summary>실패 아님(성공).</summary>
        None = 0,

        /// <summary>자격증명 형식 불량 — 빈 device-id, hex 파싱 실패, 규약 위반.</summary>
        InvalidCredential = 1,

        /// <summary>제공자가 거절 — 위조/만료 티켓, 무효 토큰.</summary>
        Rejected = 2,

        /// <summary>제공자 밴 — VAC/퍼블리셔 밴(거절 옵션이 켜진 경우).</summary>
        Banned = 3,

        /// <summary>예약 — 주장한 신원과 검증 결과 불일치(현 구현 미발생, 미래 제공자용).</summary>
        IdentityMismatch = 4,

        /// <summary>검증 응답 시간 초과(네이티브 콜백 미도착 등).</summary>
        Timeout = 5,
    }
}
```

`server/src/Bun3.Server.Auth/AuthResult.cs`:

```csharp
namespace Bun3.Server.Auth
{
    /// <summary>검증 판정 — 예상 가능한 거절은 값으로, 인프라 실패만 예외로 표면화된다.</summary>
    public class AuthResult
    {
        /// <summary>검증 성공 여부.</summary>
        public bool Succeeded { get; }

        /// <summary>검증된 신원 — 성공 시에만 유효.</summary>
        public ProviderIdentity Identity { get; }

        /// <summary>실패 사유 — 실패 시에만 유효.</summary>
        public AuthFailure Failure { get; }

        /// <summary>로그용 설명 — 와이어에 싣지 말 것.</summary>
        public string? Error { get; }

        /// <summary>파생 결과 타입(제공자별 디테일)용 생성자.</summary>
        protected AuthResult(bool succeeded, ProviderIdentity identity, AuthFailure failure, string? error)
        {
            Succeeded = succeeded;
            Identity = identity;
            Failure = failure;
            Error = error;
        }

        /// <summary>성공 판정을 만든다.</summary>
        public static AuthResult Success(ProviderIdentity identity) =>
            new AuthResult(true, identity, AuthFailure.None, null);

        /// <summary>실패 판정을 만든다.</summary>
        public static AuthResult Fail(AuthFailure failure, string? error = null) =>
            new AuthResult(false, default, failure, error);
    }
}
```

`server/src/Bun3.Server.Auth/IIdentityVerifier.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;

namespace Bun3.Server.Auth
{
    /// <summary>제공자별 자격증명 검증기 — Players 신원 모델의 1층.
    /// 게임 로그인 핸들러가 호출하고, 성공 신원으로 accountKey를 만들어 SignInAsync에 넘긴다.</summary>
    public interface IIdentityVerifier
    {
        /// <summary>제공자 이름(소문자 규약) — 발급하는 ProviderIdentity.Provider와 일치한다.</summary>
        string Provider { get; }

        /// <summary>자격증명을 검증한다. 거절은 실패 값, 인프라 문제는 예외.
        /// credential 인코딩은 제공자 정의(각 검증기 문서 참고).</summary>
        ValueTask<AuthResult> VerifyAsync(string credential, CancellationToken ct = default);
    }
}
```

`server/src/Bun3.Server.Auth/GuestVerifier.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;

namespace Bun3.Server.Auth
{
    /// <summary>게스트 검증기 — 검증할 자격증명이 없는 대신 신뢰 경계 검증(형식)만 수행한다.
    /// credential = 클라이언트 device-id. 본질은 클라 주장 신뢰이며, 같은 계약 뒤에 두는
    /// 이유는 Steam 등으로 전환할 때 로그인 핸들러가 검증기 한 줄만 바뀌게 하기 위함.</summary>
    public sealed class GuestVerifier : IIdentityVerifier
    {
        /// <summary>device-id 최대 길이.</summary>
        public const int MaxSubjectLength = 128;

        /// <inheritdoc />
        public string Provider => "guest";

        /// <inheritdoc />
        public ValueTask<AuthResult> VerifyAsync(string credential, CancellationToken ct = default)
        {
            var subject = credential?.Trim() ?? string.Empty;
            if (subject.Length == 0 || subject.Length > MaxSubjectLength || subject.Contains(':'))
                return new ValueTask<AuthResult>(AuthResult.Fail(AuthFailure.InvalidCredential, "invalid device id"));

            return new ValueTask<AuthResult>(AuthResult.Success(new ProviderIdentity(Provider, subject)));
        }
    }
}
```

- [ ] **Step 5: 통과 확인**

Run: `dotnet test server/tests/Bun3.Server.Tests --filter "FullyQualifiedName~AuthTests"`
Expected: PASS (신규 6개 초록 — TestCase 3개 포함)

- [ ] **Step 6: 커밋**

```powershell
git add server/src/Bun3.Server.Auth server/tests/Bun3.Server.Tests/AuthTests.cs server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj Bun3.sln
git commit -m @'
✨ Add Bun3.Server.Auth contract package (ProviderIdentity, AuthResult, GuestVerifier)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 2: `Bun3.Server.Auth.Steam` — SteamAuthResult + SteamWebApiVerifier

**Files:**
- Create: `server/src/Bun3.Server.Auth.Steam/Bun3.Server.Auth.Steam.csproj`
- Create: `server/src/Bun3.Server.Auth.Steam/SteamAuthResult.cs`
- Create: `server/src/Bun3.Server.Auth.Steam/SteamWebApiOptions.cs`
- Create: `server/src/Bun3.Server.Auth.Steam/SteamWebApiVerifier.cs`
- Modify: `Bun3.sln` (dotnet sln add)
- Modify: `server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj` (프로젝트 참조 추가)
- Test: `server/tests/Bun3.Server.Tests/SteamWebApiVerifierTests.cs`

**Interfaces:**
- Consumes (Task 1): `AuthResult`(protected ctor + `Fail`), `AuthFailure`, `ProviderIdentity(string, string)`, `IIdentityVerifier`
- Produces (Task 3이 사용):
  - `sealed class SteamAuthResult : AuthResult { ulong SteamId; ulong OwnerSteamId; bool VacBanned; bool PublisherBanned; int ValveErrorCode; }`
  - `internal static SteamAuthResult Success(ulong steamId, ulong ownerSteamId, bool vacBanned, bool publisherBanned)`
  - `internal static SteamAuthResult Fail(AuthFailure failure, string? error, int valveErrorCode, ulong steamId = 0, ulong ownerSteamId = 0, bool vacBanned = false, bool publisherBanned = false)`
  - Task 3은 같은 어셈블리이므로 internal 팩토리를 그대로 쓴다.

- [ ] **Step 1: 프로젝트 생성 + 솔루션/테스트 참조 연결**

`server/src/Bun3.Server.Auth.Steam/Bun3.Server.Auth.Steam.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <Nullable>enable</Nullable>
    <RootNamespace>Bun3.Server.Auth.Steam</RootNamespace>
    <PackageId>Bun3.Server.Auth.Steam</PackageId>
    <Version>0.1.0</Version>
    <Authors>Bun3</Authors>
    <RepositoryUrl>https://github.com/Bun3/bun3-kit</RepositoryUrl>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <Description>Steam 검증기 — Web API(전용 서버) + BeginAuthSession 네이티브 위임(클라 호스트)</Description>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="System.Text.Json" Version="8.0.5" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Bun3.Server.Auth\Bun3.Server.Auth.csproj" />
  </ItemGroup>

</Project>
```

Run (레포 루트에서):

```powershell
dotnet sln Bun3.sln add server/src/Bun3.Server.Auth.Steam/Bun3.Server.Auth.Steam.csproj
```

`server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj`의 ProjectReference ItemGroup에 추가:

```xml
    <ProjectReference Include="..\..\src\Bun3.Server.Auth.Steam\Bun3.Server.Auth.Steam.csproj" />
```

- [ ] **Step 2: 실패하는 테스트 작성**

`server/tests/Bun3.Server.Tests/SteamWebApiVerifierTests.cs`:

```csharp
using System.Net;
using Bun3.Server.Auth;
using Bun3.Server.Auth.Steam;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class SteamWebApiVerifierTests
{
    private const string OkJson =
        """{"response":{"params":{"result":"OK","steamid":"76561198000000001","ownersteamid":"76561198000000002","vacbanned":false,"publisherbanned":false}}}""";
    private const string VacBannedJson =
        """{"response":{"params":{"result":"OK","steamid":"76561198000000001","ownersteamid":"76561198000000001","vacbanned":true,"publisherbanned":false}}}""";
    private const string PublisherBannedJson =
        """{"response":{"params":{"result":"OK","steamid":"76561198000000001","ownersteamid":"76561198000000001","vacbanned":false,"publisherbanned":true}}}""";
    private const string ErrorJson =
        """{"response":{"error":{"errorcode":101,"errordesc":"Invalid ticket"}}}""";

    private sealed class FakeHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Respond = _ => Json(OkJson);
        public HttpRequestMessage? LastRequest;

        public static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json),
        };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            return Task.FromResult(Respond(request));
        }
    }

    private static (SteamWebApiVerifier Verifier, FakeHandler Handler) Create(
        Action<SteamWebApiOptions>? configure = null)
    {
        var handler = new FakeHandler();
        var options = new SteamWebApiOptions { AppId = 480, WebApiKey = "test-key" };
        configure?.Invoke(options);
        return (new SteamWebApiVerifier(new HttpClient(handler), options), handler);
    }

    [Test]
    public async Task Ok_response_produces_steam_identity_with_flags()
    {
        var (verifier, _) = Create();
        Assert.That(verifier.Provider, Is.EqualTo("steam"));

        var result = await verifier.VerifyAsync("a1b2c3");
        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Identity.ToAccountKey(), Is.EqualTo("steam:76561198000000001"));

        var steam = (SteamAuthResult)result;
        Assert.That(steam.SteamId, Is.EqualTo(76561198000000001UL));
        Assert.That(steam.OwnerSteamId, Is.EqualTo(76561198000000002UL));   // 패밀리 공유
        Assert.That(steam.VacBanned, Is.False);
        Assert.That(steam.PublisherBanned, Is.False);
    }

    [Test]
    public async Task Request_carries_key_appid_ticket_and_identity_query()
    {
        var (verifier, handler) = Create(o => o.Identity = "login");
        await verifier.VerifyAsync("A1B2");

        var query = handler.LastRequest!.RequestUri!.Query;
        Assert.That(handler.LastRequest.RequestUri.AbsoluteUri,
            Does.StartWith("https://partner.steam-api.com/ISteamUserAuth/AuthenticateUserTicket/v1/"));
        Assert.That(query, Does.Contain("key=test-key"));
        Assert.That(query, Does.Contain("appid=480"));
        Assert.That(query, Does.Contain("ticket=A1B2"));
        Assert.That(query, Does.Contain("identity=login"));
    }

    [Test]
    public async Task Valve_error_maps_to_rejected_with_valve_code()
    {
        var (verifier, handler) = Create();
        handler.Respond = _ => FakeHandler.Json(ErrorJson);

        var result = (SteamAuthResult)await verifier.VerifyAsync("a1b2");
        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Failure, Is.EqualTo(AuthFailure.Rejected));
        Assert.That(result.ValveErrorCode, Is.EqualTo(101));
    }

    [Test]
    public async Task Vac_ban_rejected_by_default_with_flags_preserved()
    {
        var (verifier, handler) = Create();
        handler.Respond = _ => FakeHandler.Json(VacBannedJson);

        var result = (SteamAuthResult)await verifier.VerifyAsync("a1b2");
        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Failure, Is.EqualTo(AuthFailure.Banned));
        Assert.That(result.VacBanned, Is.True);
        Assert.That(result.SteamId, Is.EqualTo(76561198000000001UL));
    }

    [Test]
    public async Task Vac_ban_passes_when_reject_disabled()
    {
        var (verifier, handler) = Create(o => o.RejectVacBanned = false);
        handler.Respond = _ => FakeHandler.Json(VacBannedJson);

        var result = (SteamAuthResult)await verifier.VerifyAsync("a1b2");
        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.VacBanned, Is.True);    // 입장 판단용 플래그는 유지
    }

    [Test]
    public async Task Publisher_ban_rejected_by_default()
    {
        var (verifier, handler) = Create();
        handler.Respond = _ => FakeHandler.Json(PublisherBannedJson);

        var result = (SteamAuthResult)await verifier.VerifyAsync("a1b2");
        Assert.That(result.Failure, Is.EqualTo(AuthFailure.Banned));
        Assert.That(result.PublisherBanned, Is.True);
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("xyz!")]     // hex 아님
    public async Task Invalid_ticket_format_fails_without_http_call(string credential)
    {
        var (verifier, handler) = Create();
        var result = await verifier.VerifyAsync(credential);

        Assert.That(result.Failure, Is.EqualTo(AuthFailure.InvalidCredential));
        Assert.That(handler.LastRequest, Is.Null);   // HTTP 미호출
    }

    [Test]
    public void Http_failure_propagates_as_exception()
    {
        var (verifier, handler) = Create();
        handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);

        Assert.ThrowsAsync<HttpRequestException>(async () => await verifier.VerifyAsync("a1b2"));
    }

    [Test]
    public void Constructor_rejects_missing_appid_or_key()
    {
        var http = new HttpClient(new FakeHandler());
        Assert.Throws<ArgumentException>(() =>
            new SteamWebApiVerifier(http, new SteamWebApiOptions { AppId = 0, WebApiKey = "k" }));
        Assert.Throws<ArgumentException>(() =>
            new SteamWebApiVerifier(http, new SteamWebApiOptions { AppId = 480, WebApiKey = " " }));
    }
}
```

- [ ] **Step 3: 실패 확인**

Run: `dotnet test server/tests/Bun3.Server.Tests --filter "FullyQualifiedName~SteamWebApiVerifierTests"`
Expected: 컴파일 오류 (`SteamWebApiVerifier` 미정의)

- [ ] **Step 4: 구현**

`server/src/Bun3.Server.Auth.Steam/SteamAuthResult.cs`:

```csharp
using System.Globalization;

namespace Bun3.Server.Auth.Steam
{
    /// <summary>Steam 검증 판정 — 공통 판정에 Steam 디테일(밴 플래그, 원시 코드)을 더한다.</summary>
    public sealed class SteamAuthResult : AuthResult
    {
        /// <summary>검증된 SteamID64 — 실패 시에도 응답에 있었다면 채워진다(로그용).</summary>
        public ulong SteamId { get; }

        /// <summary>소유자 SteamID64 — 패밀리 공유로 빌린 계정이면 SteamId와 다르다.
        /// 네이티브 경로는 소유자 정보가 없어 SteamId와 같은 값이다.</summary>
        public ulong OwnerSteamId { get; }

        /// <summary>VAC 밴 여부(Web API 응답 기준).</summary>
        public bool VacBanned { get; }

        /// <summary>퍼블리셔 밴 여부(Web API 응답 기준).</summary>
        public bool PublisherBanned { get; }

        /// <summary>Valve 원시 코드 — Web API errorcode 또는 EAuthSessionResponse/EBeginAuthSessionResult.
        /// 로그·운영용이며 판정에는 쓰지 않는다.</summary>
        public int ValveErrorCode { get; }

        private SteamAuthResult(
            bool succeeded, ProviderIdentity identity, AuthFailure failure, string? error,
            ulong steamId, ulong ownerSteamId, bool vacBanned, bool publisherBanned, int valveErrorCode)
            : base(succeeded, identity, failure, error)
        {
            SteamId = steamId;
            OwnerSteamId = ownerSteamId;
            VacBanned = vacBanned;
            PublisherBanned = publisherBanned;
            ValveErrorCode = valveErrorCode;
        }

        internal static SteamAuthResult Success(ulong steamId, ulong ownerSteamId, bool vacBanned, bool publisherBanned) =>
            new SteamAuthResult(
                true,
                new ProviderIdentity("steam", steamId.ToString(CultureInfo.InvariantCulture)),
                AuthFailure.None, null,
                steamId, ownerSteamId, vacBanned, publisherBanned, 0);

        internal static SteamAuthResult Fail(
            AuthFailure failure, string? error, int valveErrorCode,
            ulong steamId = 0, ulong ownerSteamId = 0, bool vacBanned = false, bool publisherBanned = false) =>
            new SteamAuthResult(false, default, failure, error, steamId, ownerSteamId, vacBanned, publisherBanned, valveErrorCode);
    }
}
```

`server/src/Bun3.Server.Auth.Steam/SteamWebApiOptions.cs`:

```csharp
namespace Bun3.Server.Auth.Steam
{
    /// <summary>SteamWebApiVerifier 옵션 — 생성자에서 검증·스냅샷되며 이후 변경은 무시된다.</summary>
    public sealed class SteamWebApiOptions
    {
        /// <summary>Steam AppId. 0이면 생성자에서 거부.</summary>
        public uint AppId { get; set; }

        /// <summary>Publisher Web API Key — 서버 비밀. 환경변수/설정으로만 주입하고 커밋 금지.</summary>
        public string WebApiKey { get; set; } = "";

        /// <summary>GetAuthTicketForWebApi("identity")로 발급한 티켓이면 같은 identity 문자열 — 쿼리에 동봉된다.</summary>
        public string? Identity { get; set; }

        /// <summary>VAC 밴을 위조/만료와 동일하게 실패 처리(기본). 끄면 성공 + 플래그.</summary>
        public bool RejectVacBanned { get; set; } = true;

        /// <summary>퍼블리셔 밴을 실패 처리(기본). 끄면 성공 + 플래그.</summary>
        public bool RejectPublisherBanned { get; set; } = true;
    }
}
```

`server/src/Bun3.Server.Auth.Steam/SteamWebApiVerifier.cs`:

```csharp
using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Bun3.Server.Auth.Steam
{
    /// <summary>전용 서버용 Steam 검증기 — Valve Web API(ISteamUserAuth/AuthenticateUserTicket)에
    /// HTTPS 1회로 티켓을 검증한다. credential = 클라 티켓의 hex 문자열
    /// (GetAuthSessionTicket/GetAuthTicketForWebApi 산출물).</summary>
    public sealed class SteamWebApiVerifier : IIdentityVerifier
    {
        private const string Endpoint = "https://partner.steam-api.com/ISteamUserAuth/AuthenticateUserTicket/v1/";

        private readonly HttpClient _http;
        private readonly uint _appId;
        private readonly string _webApiKey;
        private readonly string? _identity;
        private readonly bool _rejectVacBanned;
        private readonly bool _rejectPublisherBanned;

        /// <inheritdoc />
        public string Provider => "steam";

        /// <summary>검증기를 생성한다. AppId 0 또는 빈 WebApiKey는 즉시 거부(부팅 시 즉사).</summary>
        public SteamWebApiVerifier(HttpClient http, SteamWebApiOptions options)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            if (options is null) throw new ArgumentNullException(nameof(options));
            if (options.AppId == 0)
                throw new ArgumentException("AppId is required.", nameof(options));
            if (string.IsNullOrWhiteSpace(options.WebApiKey))
                throw new ArgumentException("WebApiKey is required.", nameof(options));

            _appId = options.AppId;
            _webApiKey = options.WebApiKey;
            _identity = options.Identity;
            _rejectVacBanned = options.RejectVacBanned;
            _rejectPublisherBanned = options.RejectPublisherBanned;
        }

        /// <inheritdoc />
        public async ValueTask<AuthResult> VerifyAsync(string credential, CancellationToken ct = default)
        {
            var ticket = credential?.Trim() ?? string.Empty;
            if (ticket.Length == 0 || !IsHex(ticket))
                return AuthResult.Fail(AuthFailure.InvalidCredential, "ticket must be a hex string");

            var url = Endpoint +
                      "?key=" + Uri.EscapeDataString(_webApiKey) +
                      "&appid=" + _appId.ToString(CultureInfo.InvariantCulture) +
                      "&ticket=" + ticket;
            if (_identity != null)
                url += "&identity=" + Uri.EscapeDataString(_identity);

            using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement.GetProperty("response");

            if (root.TryGetProperty("error", out var error))
            {
                var code = error.TryGetProperty("errorcode", out var ec) ? ec.GetInt32() : 0;
                var desc = error.TryGetProperty("errordesc", out var ed) ? ed.GetString() : null;
                return SteamAuthResult.Fail(AuthFailure.Rejected, desc ?? "rejected by Valve", code);
            }

            var p = root.GetProperty("params");
            var result = p.GetProperty("result").GetString();
            if (result != "OK")
                return SteamAuthResult.Fail(AuthFailure.Rejected, "result=" + result, 0);

            var steamId = ulong.Parse(p.GetProperty("steamid").GetString()!, CultureInfo.InvariantCulture);
            var ownerSteamId = p.TryGetProperty("ownersteamid", out var os) && os.GetString() is { } ownerRaw
                ? ulong.Parse(ownerRaw, CultureInfo.InvariantCulture)
                : steamId;
            var vacBanned = p.TryGetProperty("vacbanned", out var vb) && vb.GetBoolean();
            var publisherBanned = p.TryGetProperty("publisherbanned", out var pb) && pb.GetBoolean();

            if (vacBanned && _rejectVacBanned)
                return SteamAuthResult.Fail(AuthFailure.Banned, "VAC banned", 0, steamId, ownerSteamId, vacBanned, publisherBanned);
            if (publisherBanned && _rejectPublisherBanned)
                return SteamAuthResult.Fail(AuthFailure.Banned, "publisher banned", 0, steamId, ownerSteamId, vacBanned, publisherBanned);

            return SteamAuthResult.Success(steamId, ownerSteamId, vacBanned, publisherBanned);
        }

        private static bool IsHex(string value)
        {
            foreach (var c in value)
            {
                var isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!isHex) return false;
            }
            return true;
        }
    }
}
```

- [ ] **Step 5: 통과 확인**

Run: `dotnet test server/tests/Bun3.Server.Tests --filter "FullyQualifiedName~SteamWebApiVerifierTests"`
Expected: PASS (신규 11개 초록 — TestCase 3개 포함)

- [ ] **Step 6: 커밋**

```powershell
git add server/src/Bun3.Server.Auth.Steam server/tests/Bun3.Server.Tests/SteamWebApiVerifierTests.cs server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj Bun3.sln
git commit -m @'
✨ Add SteamWebApiVerifier (dedicated-server Steam ticket verification)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 3: `SteamSessionVerifier` — 클라 호스트 네이티브 위임

**Files:**
- Create: `server/src/Bun3.Server.Auth.Steam/SteamSessionOptions.cs`
- Create: `server/src/Bun3.Server.Auth.Steam/SteamSessionVerifier.cs`
- Test: `server/tests/Bun3.Server.Tests/SteamSessionVerifierTests.cs`

**Interfaces:**
- Consumes (Task 1·2): `AuthResult.Fail`, `AuthFailure`, `IIdentityVerifier`, `SteamAuthResult.Success/Fail`(internal, 같은 어셈블리)
- Produces (게임 글루가 사용):
  - `SteamSessionVerifier(SteamSessionOptions options)` — `VerifyAsync("steamId64:ticketHex", ct)`
  - `void HandleValidateResult(ulong steamId, int authSessionResponse)` — Steamworks 콜백에서 호출
  - `void EndSession(ulong steamId)` — 플레이어 퇴장 시 게임이 호출
  - `event Action<ulong, int>? SessionInvalidated` — 접속 승인 후 무효화(밴/티켓 취소) 통지

- [ ] **Step 1: 실패하는 테스트 작성**

`server/tests/Bun3.Server.Tests/SteamSessionVerifierTests.cs`:

```csharp
using Bun3.Server.Auth;
using Bun3.Server.Auth.Steam;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class SteamSessionVerifierTests
{
    private const ulong SteamId = 76561198000000001UL;
    private const string Credential = "76561198000000001:a1b2c3d4";

    private sealed class Harness
    {
        public readonly List<(byte[] Ticket, ulong SteamId)> BeginCalls = new();
        public readonly List<ulong> EndCalls = new();
        public int BeginResult;
        public SteamSessionVerifier Verifier;

        public Harness(TimeSpan? timeout = null)
        {
            Verifier = new SteamSessionVerifier(new SteamSessionOptions
            {
                BeginSession = (ticket, steamId) => { BeginCalls.Add((ticket, steamId)); return BeginResult; },
                EndSession = steamId => EndCalls.Add(steamId),
                Timeout = timeout ?? TimeSpan.FromSeconds(5),
            });
        }
    }

    [Test]
    public async Task Ok_callback_completes_verification()
    {
        var h = new Harness();
        Assert.That(h.Verifier.Provider, Is.EqualTo("steam"));

        var verify = h.Verifier.VerifyAsync(Credential).AsTask();
        h.Verifier.HandleValidateResult(SteamId, 0);   // k_EAuthSessionResponseOK

        var result = (SteamAuthResult)await verify;
        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Identity.ToAccountKey(), Is.EqualTo("steam:76561198000000001"));
        Assert.That(result.OwnerSteamId, Is.EqualTo(SteamId));   // 네이티브 경로: 소유자 정보 없음
        Assert.That(h.BeginCalls, Has.Count.EqualTo(1));
        Assert.That(h.BeginCalls[0].SteamId, Is.EqualTo(SteamId));
        Assert.That(h.BeginCalls[0].Ticket, Is.EqualTo(new byte[] { 0xa1, 0xb2, 0xc3, 0xd4 }));
        Assert.That(h.EndCalls, Is.Empty);   // 성공 시 세션 유지 — 정리는 게임 몫
    }

    [TestCase("not-a-number:a1b2")]
    [TestCase("76561198000000001")]        // 구분자 없음
    [TestCase("76561198000000001:")]       // 티켓 없음
    [TestCase("76561198000000001:a1b")]    // 홀수 hex
    [TestCase("76561198000000001:zz")]     // hex 아님
    [TestCase("0:a1b2")]                   // steamId 0
    public async Task Malformed_credential_fails_without_begin_call(string credential)
    {
        var h = new Harness();
        var result = await h.Verifier.VerifyAsync(credential);

        Assert.That(result.Failure, Is.EqualTo(AuthFailure.InvalidCredential));
        Assert.That(h.BeginCalls, Is.Empty);
    }

    [Test]
    public async Task Immediate_begin_failure_maps_to_rejected()
    {
        var h = new Harness { BeginResult = 1 };   // k_EBeginAuthSessionResultInvalidTicket
        var result = (SteamAuthResult)await h.Verifier.VerifyAsync(Credential);

        Assert.That(result.Failure, Is.EqualTo(AuthFailure.Rejected));
        Assert.That(result.ValveErrorCode, Is.EqualTo(1));
        Assert.That(h.EndCalls, Is.EqualTo(new[] { SteamId }));   // 실패 정리 규약
    }

    [Test]
    public async Task Rejecting_callback_maps_to_rejected_and_ends_session()
    {
        var h = new Harness();
        var verify = h.Verifier.VerifyAsync(Credential).AsTask();
        h.Verifier.HandleValidateResult(SteamId, 6);   // k_EAuthSessionResponseAuthTicketCanceled

        var result = (SteamAuthResult)await verify;
        Assert.That(result.Failure, Is.EqualTo(AuthFailure.Rejected));
        Assert.That(result.ValveErrorCode, Is.EqualTo(6));
        Assert.That(h.EndCalls, Is.EqualTo(new[] { SteamId }));
    }

    [TestCase(3)]   // k_EAuthSessionResponseVACBanned
    [TestCase(9)]   // k_EAuthSessionResponsePublisherIssuedBan
    public async Task Ban_callback_maps_to_banned(int response)
    {
        var h = new Harness();
        var verify = h.Verifier.VerifyAsync(Credential).AsTask();
        h.Verifier.HandleValidateResult(SteamId, response);

        var result = (SteamAuthResult)await verify;
        Assert.That(result.Failure, Is.EqualTo(AuthFailure.Banned));
        Assert.That(result.ValveErrorCode, Is.EqualTo(response));
    }

    [Test]
    public async Task Callback_timeout_fails_with_timeout_and_ends_session()
    {
        var h = new Harness(timeout: TimeSpan.FromMilliseconds(50));
        var result = await h.Verifier.VerifyAsync(Credential);

        Assert.That(result.Failure, Is.EqualTo(AuthFailure.Timeout));
        Assert.That(h.EndCalls, Is.EqualTo(new[] { SteamId }));
    }

    [Test]
    public async Task Concurrent_verify_for_same_steamid_fails_fast()
    {
        var h = new Harness();
        var first = h.Verifier.VerifyAsync(Credential).AsTask();

        var second = await h.Verifier.VerifyAsync(Credential);
        Assert.That(second.Failure, Is.EqualTo(AuthFailure.Rejected));
        Assert.That(h.BeginCalls, Has.Count.EqualTo(1));   // 두 번째는 Begin 미호출

        h.Verifier.HandleValidateResult(SteamId, 0);
        Assert.That((await first).Succeeded, Is.True);
    }

    [Test]
    public async Task Late_invalidation_raises_event()
    {
        var h = new Harness();
        var invalidated = new List<(ulong SteamId, int Code)>();
        h.Verifier.SessionInvalidated += (steamId, code) => invalidated.Add((steamId, code));

        var verify = h.Verifier.VerifyAsync(Credential).AsTask();
        h.Verifier.HandleValidateResult(SteamId, 0);
        await verify;

        h.Verifier.HandleValidateResult(SteamId, 6);   // 접속 승인 후 티켓 취소
        Assert.That(invalidated, Is.EqualTo(new[] { (SteamId, 6) }));
    }

    [Test]
    public void Ok_callback_without_pending_is_ignored()
    {
        var h = new Harness();
        var invalidated = 0;
        h.Verifier.SessionInvalidated += (_, _) => invalidated++;

        h.Verifier.HandleValidateResult(SteamId, 0);   // pending 없음 + OK → 무시
        Assert.That(invalidated, Is.Zero);
    }

    [Test]
    public void External_cancellation_throws_and_ends_session()
    {
        var h = new Harness();
        using var cts = new CancellationTokenSource();
        var verify = h.Verifier.VerifyAsync(Credential, cts.Token).AsTask();
        cts.Cancel();

        Assert.ThrowsAsync<TaskCanceledException>(async () => await verify);
        Assert.That(h.EndCalls, Is.EqualTo(new[] { SteamId }));
    }

    [Test]
    public void EndSession_forwards_to_delegate()
    {
        var h = new Harness();
        h.Verifier.EndSession(SteamId);
        Assert.That(h.EndCalls, Is.EqualTo(new[] { SteamId }));
    }

    [Test]
    public void Constructor_rejects_missing_delegates()
    {
        Assert.Throws<ArgumentException>(() => new SteamSessionVerifier(new SteamSessionOptions
        {
            BeginSession = null,
            EndSession = _ => { },
        }));
        Assert.Throws<ArgumentException>(() => new SteamSessionVerifier(new SteamSessionOptions
        {
            BeginSession = (_, _) => 0,
            EndSession = null,
        }));
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test server/tests/Bun3.Server.Tests --filter "FullyQualifiedName~SteamSessionVerifierTests"`
Expected: 컴파일 오류 (`SteamSessionVerifier` 미정의)

- [ ] **Step 3: 구현**

`server/src/Bun3.Server.Auth.Steam/SteamSessionOptions.cs`:

```csharp
using System;

namespace Bun3.Server.Auth.Steam
{
    /// <summary>SteamSessionVerifier 옵션 — 네이티브 호출 2개를 게임이 델리게이트로 꽂는다
    /// (프레임워크는 Steamworks C# 바인딩에 의존하지 않는다). 생성자에서 검증·스냅샷된다.</summary>
    public sealed class SteamSessionOptions
    {
        /// <summary>SteamUser.BeginAuthSession 래핑 — (티켓 바이트, 주장 SteamID64)를 받아
        /// 즉시 결과 코드(EBeginAuthSessionResult, 0=OK)를 돌려준다. 필수.</summary>
        public Func<byte[], ulong, int>? BeginSession { get; set; }

        /// <summary>SteamUser.EndAuthSession 래핑. 필수.</summary>
        public Action<ulong>? EndSession { get; set; }

        /// <summary>ValidateAuthTicketResponse 콜백 대기 한도 — 초과 시 AuthFailure.Timeout 값으로 실패.</summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
    }
}
```

`server/src/Bun3.Server.Auth.Steam/SteamSessionVerifier.cs`:

```csharp
using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Bun3.Server.Auth.Steam
{
    /// <summary>클라 호스트(리슨 서버)용 Steam 검증기 — BeginAuthSession 네이티브 흐름의
    /// 상관관계(correlation)만 소유하고, 네이티브 호출은 게임이 델리게이트로 꽂는다.
    /// credential = "steamId64:ticketHex" (클라가 자기 SteamID를 주장 + 티켓).
    ///
    /// 게임 글루 계약: Steamworks의 ValidateAuthTicketResponse 콜백에서
    /// <see cref="HandleValidateResult"/>를 호출하고, 플레이어 퇴장 시
    /// <see cref="EndSession"/>을 호출한다. 실패·타임아웃 정리는 검증기가 스스로 한다.</summary>
    public sealed class SteamSessionVerifier : IIdentityVerifier
    {
        private readonly Func<byte[], ulong, int> _beginSession;
        private readonly Action<ulong> _endSession;
        private readonly TimeSpan _timeout;
        private readonly ConcurrentDictionary<ulong, TaskCompletionSource<AuthResult>> _pending = new();

        /// <inheritdoc />
        public string Provider => "steam";

        /// <summary>접속 승인 "이후" 도착한 무효화 통지(게임 중 밴, 티켓 취소) —
        /// (SteamID64, EAuthSessionResponse). 게임이 구독해서 해당 플레이어를 킥한다.</summary>
        public event Action<ulong, int>? SessionInvalidated;

        /// <summary>검증기를 생성한다. 델리게이트 누락은 즉시 거부(부팅 시 즉사).</summary>
        public SteamSessionVerifier(SteamSessionOptions options)
        {
            if (options is null) throw new ArgumentNullException(nameof(options));
            _beginSession = options.BeginSession
                ?? throw new ArgumentException("BeginSession delegate is required.", nameof(options));
            _endSession = options.EndSession
                ?? throw new ArgumentException("EndSession delegate is required.", nameof(options));
            _timeout = options.Timeout;
        }

        /// <inheritdoc />
        public async ValueTask<AuthResult> VerifyAsync(string credential, CancellationToken ct = default)
        {
            if (!TryParseCredential(credential, out var steamId, out var ticket))
                return AuthResult.Fail(AuthFailure.InvalidCredential, "credential must be \"steamId64:ticketHex\"");

            var tcs = new TaskCompletionSource<AuthResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_pending.TryAdd(steamId, tcs))
                return AuthResult.Fail(AuthFailure.Rejected, "verification already pending for this steamId");

            int beginResult;
            try
            {
                beginResult = _beginSession(ticket, steamId);
            }
            catch
            {
                _pending.TryRemove(steamId, out _);
                throw;
            }

            if (beginResult != 0)
            {
                _pending.TryRemove(steamId, out _);
                _endSession(steamId);
                return SteamAuthResult.Fail(AuthFailure.Rejected, "BeginAuthSession failed", beginResult);
            }

            var delay = Task.Delay(_timeout, ct);
            var completed = await Task.WhenAny(tcs.Task, delay).ConfigureAwait(false);
            if (completed == tcs.Task)
                return await tcs.Task.ConfigureAwait(false);

            // 타임아웃 또는 외부 취소 — 콜백과의 경합은 pending 제거로 판정
            if (!_pending.TryRemove(steamId, out _))
                return await tcs.Task.ConfigureAwait(false);   // 콜백이 경합에서 이김

            _endSession(steamId);
            if (ct.IsCancellationRequested)
                await delay.ConfigureAwait(false);   // OperationCanceledException 전파 (인프라 취소)
            return SteamAuthResult.Fail(AuthFailure.Timeout, "auth callback not received", 0);
        }

        /// <summary>게임 글루가 Steamworks의 ValidateAuthTicketResponse 콜백에서 호출한다.
        /// 검증 대기 중이면 판정을 완성하고, 아니면(접속 승인 후 무효화) SessionInvalidated를 발화한다.
        /// 어느 스레드에서 호출해도 안전하며, Steam 콜백 스레드를 붙잡지 않는다.</summary>
        public void HandleValidateResult(ulong steamId, int authSessionResponse)
        {
            if (_pending.TryRemove(steamId, out var tcs))
            {
                AuthResult result;
                if (authSessionResponse == 0)
                {
                    result = SteamAuthResult.Success(steamId, steamId, false, false);
                }
                else
                {
                    _endSession(steamId);   // 실패 정리 규약
                    result = SteamAuthResult.Fail(MapFailure(authSessionResponse), "auth session rejected", authSessionResponse);
                }
                tcs.TrySetResult(result);
                return;
            }

            if (authSessionResponse != 0)
                SessionInvalidated?.Invoke(steamId, authSessionResponse);
        }

        /// <summary>인증 세션을 닫는다 — 성공 검증 후 플레이어 퇴장 시(OnRetiredAsync 등) 게임이 호출한다.</summary>
        public void EndSession(ulong steamId) => _endSession(steamId);

        private static AuthFailure MapFailure(int authSessionResponse) =>
            authSessionResponse == 3 || authSessionResponse == 9   // VACBanned / PublisherIssuedBan
                ? AuthFailure.Banned
                : AuthFailure.Rejected;

        private static bool TryParseCredential(string? credential, out ulong steamId, out byte[] ticket)
        {
            steamId = 0;
            ticket = Array.Empty<byte>();
            if (string.IsNullOrEmpty(credential)) return false;

            var separator = credential!.IndexOf(':');
            if (separator <= 0 || separator == credential.Length - 1) return false;

            if (!ulong.TryParse(credential.Substring(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out steamId)
                || steamId == 0)
                return false;

            var hex = credential.Substring(separator + 1);
            if (hex.Length % 2 != 0) return false;

            var bytes = new byte[hex.Length / 2];
            for (var i = 0; i < bytes.Length; i++)
            {
                var hi = HexValue(hex[i * 2]);
                var lo = HexValue(hex[i * 2 + 1]);
                if (hi < 0 || lo < 0) return false;
                bytes[i] = (byte)((hi << 4) | lo);
            }

            ticket = bytes;
            return true;
        }

        private static int HexValue(char c) =>
            c >= '0' && c <= '9' ? c - '0' :
            c >= 'a' && c <= 'f' ? c - 'a' + 10 :
            c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;
    }
}
```

- [ ] **Step 4: 통과 확인**

Run: `dotnet test server/tests/Bun3.Server.Tests --filter "FullyQualifiedName~SteamSessionVerifierTests"`
Expected: PASS (신규 18개 초록 — TestCase 8개 포함)

- [ ] **Step 5: 커밋**

```powershell
git add server/src/Bun3.Server.Auth.Steam server/tests/Bun3.Server.Tests/SteamSessionVerifierTests.cs
git commit -m @'
✨ Add SteamSessionVerifier (client-host BeginAuthSession correlator)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 4: Players E2E를 GuestVerifier 경유로 전환 + 전체 검증

**Files:**
- Modify: `server/tests/Bun3.Server.Tests/PlayersE2ETests.cs:35-39`

**Interfaces:**
- Consumes (Task 1): `GuestVerifier.VerifyAsync(string)`, `AuthResult.Succeeded/Identity`, `ProviderIdentity.ToAccountKey()`
- Produces: 없음 (검증 태스크)

- [ ] **Step 1: 로그인 핸들러를 검증기 경유로 교체**

`server/tests/Bun3.Server.Tests/PlayersE2ETests.cs` — 파일 상단 using에 `using Bun3.Server.Auth;` 추가 후, 기존:

```csharp
        var config = new PlayersConfig<E2ESession>();
        config.OnRequestUnauthenticated<LoginRequest, LoginResponse>(async (s, req) =>
        {
            var result = await s.SignInAsync($"guest:{req.DeviceId}");
            return new LoginResponse { Gold = result.Player.Gold, IsReconnect = result.IsReconnect };
        });
```

를 다음으로 교체 (스펙 §5의 조립 형태 — 검증 → accountKey → SignIn):

```csharp
        var verifier = new GuestVerifier();
        var config = new PlayersConfig<E2ESession>();
        config.OnRequestUnauthenticated<LoginRequest, LoginResponse>(async (s, req) =>
        {
            var auth = await verifier.VerifyAsync(req.DeviceId);
            if (!auth.Succeeded)
                throw new InvalidOperationException(auth.Error);   // 이 E2E에 실패 경로 없음 — 방어만

            var result = await s.SignInAsync(auth.Identity.ToAccountKey());
            return new LoginResponse { Gold = result.Player.Gold, IsReconnect = result.IsReconnect };
        });
```

- [ ] **Step 2: E2E 통과 확인 (accountKey 산출이 동일한지 검증)**

Run: `dotnet test server/tests/Bun3.Server.Tests --filter "FullyQualifiedName~PlayersE2ETests"`
Expected: PASS — `guest:e2e` accountKey가 인라인 시절과 동일하므로 5단계 수직 슬라이스 그대로 초록.

- [ ] **Step 3: 전체 스위트 + 0 경고 빌드 확인**

Run: `dotnet build Bun3.sln --no-incremental` → Expected: 경고 0
Run: `dotnet test server/tests/Bun3.Server.Tests` → Expected: 전체 PASS (기존 102 + Task 1~3 신규 35 = 137)
Run: `dotnet test common/tests/Bun3.Common.Tests` → Expected: 28 PASS (무변경 확인)

- [ ] **Step 4: 커밋**

```powershell
git add server/tests/Bun3.Server.Tests/PlayersE2ETests.cs
git commit -m @'
♻️ Route Players E2E login through GuestVerifier

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```
