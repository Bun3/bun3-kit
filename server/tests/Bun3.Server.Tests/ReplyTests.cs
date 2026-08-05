using Bun3.Server.Messaging;
using Bun3.Server.Tests.GameProtocol;
using Google.Protobuf.Reflection;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class ReplyTests
{
    [Test]
    public void Ok_holds_value_with_status_zero()
    {
        var res = new BuyItemResponse { RemainingGold = 900 };
        var reply = Reply<BuyItemResponse>.Ok(res);
        Assert.That(reply.IsOk, Is.True);
        Assert.That(reply.Status, Is.EqualTo(0));
        Assert.That(reply.Value, Is.SameAs(res));
    }

    [Test]
    public void Implicit_conversion_from_value_is_Ok()
    {
        Reply<BuyItemResponse> reply = new BuyItemResponse { RemainingGold = 1 };
        Assert.That(reply.IsOk, Is.True);
    }

    [Test]
    public void ReplyFailure_converts_to_failed_reply()
    {
        Reply<BuyItemResponse> reply = Reply.Fail(-1001);
        Assert.That(reply.IsOk, Is.False);
        Assert.That(reply.Status, Is.EqualTo(-1001));
        Assert.That(reply.Value, Is.Null);
    }

    [Test]
    public void Ok_with_null_throws()
    {
        Assert.Throws<System.ArgumentNullException>(() => Reply<BuyItemResponse>.Ok(null!));
    }

    [Test]
    public void Fail_with_zero_throws()
    {
        Assert.Throws<System.ArgumentException>(() => Reply<BuyItemResponse>.Fail(0));
    }

    [Test]
    public void Generated_game_protocol_matches_root_conventions()
    {
        // Grpc.Tools 파이프라인 스모크: 루트 3형의 oneof "body"와 규약 필드가 생성됐는지
        Assert.That(Request.Descriptor.Oneofs, Has.Some.Matches<OneofDescriptor>(o => o.Name == "body"));
        Assert.That(Request.Descriptor.FindFieldByName("request_id"), Is.Not.Null);
        Assert.That(Response.Descriptor.FindFieldByName("status"), Is.Not.Null);
        Assert.That(Update.Descriptor.Oneofs, Has.Some.Matches<OneofDescriptor>(o => o.Name == "body"));
    }
}
