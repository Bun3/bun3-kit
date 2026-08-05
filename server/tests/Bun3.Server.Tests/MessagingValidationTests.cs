using Bun3.Server.Messaging;
using Bun3.Server.Tests.GameProtocol;
using Bun3.Server.Tests.Helpers;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class MessagingValidationTests
{
    private static MessagingConfig<EchoSession> FullConfig()
    {
        var config = new MessagingConfig<EchoSession>();
        config.OnRequest<GetServerTimeRequest, GetServerTimeResponse>(
            (s, req) => new ValueTask<Reply<GetServerTimeResponse>>(new GetServerTimeResponse { UnixMs = 1 }));
        config.OnRequest<BuyItemRequest, BuyItemResponse>(
            (s, req) => new ValueTask<Reply<BuyItemResponse>>(new BuyItemResponse { RemainingGold = 1 }));
        return config;
    }

    [Test]
    public void Valid_config_passes()
    {
        var schema = MessagingSchema<Request, Response, Update>.Create();
        Assert.DoesNotThrow(() => schema.Validate(FullConfig()));
    }

    [Test]
    public void Missing_handler_fails_listing_the_case()
    {
        var schema = MessagingSchema<Request, Response, Update>.Create();
        var config = new MessagingConfig<EchoSession>();
        config.OnRequest<GetServerTimeRequest, GetServerTimeResponse>(
            (s, req) => new ValueTask<Reply<GetServerTimeResponse>>(new GetServerTimeResponse()));

        var ex = Assert.Throws<MessagingValidationException>(() => schema.Validate(config))!;
        Assert.That(ex.Message, Does.Contain("buy_item"));
    }

    [Test]
    public void Response_case_mismatch_reports_all_violations()
    {
        // MismatchResponse: buy_item 케이스 없음, get_server_time은 번호 12(요청은 10)
        var schema = MessagingSchema<MismatchRequest, MismatchResponse, Update>.Create();

        var ex = Assert.Throws<MessagingValidationException>(() => schema.Validate(FullConfig()))!;
        Assert.That(ex.Errors, Has.Some.Contains("get_server_time"));
        Assert.That(ex.Errors, Has.Some.Contains("buy_item"));
    }

    [Test]
    public void Wrong_response_type_fails()
    {
        var schema = MessagingSchema<Request, Response, Update>.Create();
        var config = new MessagingConfig<EchoSession>();
        config.OnRequest<GetServerTimeRequest, BuyItemResponse>(   // 잘못된 TRes
            (s, req) => new ValueTask<Reply<BuyItemResponse>>(new BuyItemResponse()));
        config.OnRequest<BuyItemRequest, BuyItemResponse>(
            (s, req) => new ValueTask<Reply<BuyItemResponse>>(new BuyItemResponse()));

        var ex = Assert.Throws<MessagingValidationException>(() => schema.Validate(config))!;
        Assert.That(ex.Errors, Has.Some.Contains("응답 타입 불일치"));
    }

    [Test]
    public void Duplicate_registration_throws_immediately()
    {
        var config = new MessagingConfig<EchoSession>();
        config.OnRequest<BuyItemRequest, BuyItemResponse>(
            (s, req) => new ValueTask<Reply<BuyItemResponse>>(new BuyItemResponse()));

        Assert.Throws<MessagingValidationException>(() =>
            config.OnRequest<BuyItemRequest, BuyItemResponse>(
                (s, req) => new ValueTask<Reply<BuyItemResponse>>(new BuyItemResponse())));
    }

    [Test]
    public void Root_without_request_id_fails_schema_creation()
    {
        // Update를 TRequest 자리에 — oneof body는 있지만 request_id가 없다
        var ex = Assert.Throws<MessagingValidationException>(() =>
            MessagingSchema<Update, Response, Update>.Create())!;
        Assert.That(ex.Message, Does.Contain("request_id"));
    }

    [Test]
    public void Duplicate_payload_types_in_request_root_fail_creation()
    {
        var ex = Assert.Throws<MessagingValidationException>(() =>
            MessagingSchema<DuplicatePayloadRequest, Response, Update>.Create())!;
        Assert.That(ex.Errors, Has.Some.Contains("first"));
        Assert.That(ex.Errors, Has.Some.Contains("second"));
    }

    [Test]
    public void Duplicate_payload_types_in_response_root_are_tolerated()
    {
        Assert.DoesNotThrow(() => MessagingSchema<Request, SharedResponse, Update>.Create());
    }

    [Test]
    public void Repeated_request_id_fails_schema_creation()
    {
        var ex = Assert.Throws<MessagingValidationException>(() =>
            MessagingSchema<RepeatedIdRequest, Response, Update>.Create())!;
        Assert.That(ex.Message, Does.Contain("request_id"));
    }
}
