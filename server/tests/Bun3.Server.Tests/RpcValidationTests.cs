using Bun3.Server.Rpc;
using Bun3.Server.Tests.GameProtocol;
using Bun3.Server.Tests.Helpers;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class RpcValidationTests
{
    private static RpcConfig<EchoSession> FullConfig()
    {
        var config = new RpcConfig<EchoSession>();
        config.OnRequest<GetServerTimeRequest, GetServerTimeResponse>(
            (s, req) => new ValueTask<Reply<GetServerTimeResponse>>(new GetServerTimeResponse { UnixMs = 1 }));
        config.OnRequest<BuyItemRequest, BuyItemResponse>(
            (s, req) => new ValueTask<Reply<BuyItemResponse>>(new BuyItemResponse { RemainingGold = 1 }));
        return config;
    }

    [Test]
    public void Valid_config_passes()
    {
        var schema = RpcSchema<Request, Response, Update>.Create();
        Assert.DoesNotThrow(() => schema.Validate(FullConfig()));
    }

    [Test]
    public void Missing_handler_fails_listing_the_case()
    {
        var schema = RpcSchema<Request, Response, Update>.Create();
        var config = new RpcConfig<EchoSession>();
        config.OnRequest<GetServerTimeRequest, GetServerTimeResponse>(
            (s, req) => new ValueTask<Reply<GetServerTimeResponse>>(new GetServerTimeResponse()));

        var ex = Assert.Throws<RpcValidationException>(() => schema.Validate(config))!;
        Assert.That(ex.Message, Does.Contain("buy_item"));
    }

    [Test]
    public void Response_case_mismatch_reports_all_violations()
    {
        // MismatchResponse: buy_item 케이스 없음, get_server_time은 번호 12(요청은 10)
        var schema = RpcSchema<MismatchRequest, MismatchResponse, Update>.Create();

        var ex = Assert.Throws<RpcValidationException>(() => schema.Validate(FullConfig()))!;
        Assert.That(ex.Errors, Has.Some.Contains("get_server_time"));
        Assert.That(ex.Errors, Has.Some.Contains("buy_item"));
    }

    [Test]
    public void Wrong_response_type_fails()
    {
        var schema = RpcSchema<Request, Response, Update>.Create();
        var config = new RpcConfig<EchoSession>();
        config.OnRequest<GetServerTimeRequest, BuyItemResponse>(   // 잘못된 TRes
            (s, req) => new ValueTask<Reply<BuyItemResponse>>(new BuyItemResponse()));
        config.OnRequest<BuyItemRequest, BuyItemResponse>(
            (s, req) => new ValueTask<Reply<BuyItemResponse>>(new BuyItemResponse()));

        var ex = Assert.Throws<RpcValidationException>(() => schema.Validate(config))!;
        Assert.That(ex.Errors, Has.Some.Contains("응답 타입 불일치"));
    }

    [Test]
    public void Duplicate_registration_throws_immediately()
    {
        var config = new RpcConfig<EchoSession>();
        config.OnRequest<BuyItemRequest, BuyItemResponse>(
            (s, req) => new ValueTask<Reply<BuyItemResponse>>(new BuyItemResponse()));

        Assert.Throws<RpcValidationException>(() =>
            config.OnRequest<BuyItemRequest, BuyItemResponse>(
                (s, req) => new ValueTask<Reply<BuyItemResponse>>(new BuyItemResponse())));
    }

    [Test]
    public void Root_without_request_id_fails_schema_creation()
    {
        // Update를 TRequest 자리에 — oneof body는 있지만 request_id가 없다
        var ex = Assert.Throws<RpcValidationException>(() =>
            RpcSchema<Update, Response, Update>.Create())!;
        Assert.That(ex.Message, Does.Contain("request_id"));
    }

    [Test]
    public void Duplicate_payload_types_in_request_root_fail_creation()
    {
        var ex = Assert.Throws<RpcValidationException>(() =>
            RpcSchema<DuplicatePayloadRequest, Response, Update>.Create())!;
        Assert.That(ex.Errors, Has.Some.Contains("first"));
        Assert.That(ex.Errors, Has.Some.Contains("second"));
    }

    [Test]
    public void Duplicate_payload_types_in_response_root_are_tolerated()
    {
        Assert.DoesNotThrow(() => RpcSchema<Request, SharedResponse, Update>.Create());
    }

    [Test]
    public void Repeated_request_id_fails_schema_creation()
    {
        var ex = Assert.Throws<RpcValidationException>(() =>
            RpcSchema<RepeatedIdRequest, Response, Update>.Create())!;
        Assert.That(ex.Message, Does.Contain("request_id"));
    }
}
