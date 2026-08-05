using System;
using Google.Protobuf;

namespace Bun3.Server.Messaging
{
    /// <summary>요청 처리의 결과 — 성공(응답 메시지) 또는 실패(상태코드). 무할당 readonly struct.</summary>
    public readonly struct Reply<TRes> where TRes : class, IMessage<TRes>
    {
        /// <summary>0 = OK. 1~99 프레임워크 예약, 음수 게임 정의.</summary>
        public int Status { get; }

        /// <summary>불변식: Status == 0 ⟺ Value != null.</summary>
        public TRes? Value { get; }

        /// <summary>Status가 0(성공)인지 여부.</summary>
        public bool IsOk => Status == 0;

        private Reply(int status, TRes? value)
        {
            Status = status;
            Value = value;
        }

        /// <summary>성공 응답을 생성한다.</summary>
        public static Reply<TRes> Ok(TRes value) =>
            new Reply<TRes>(0, value ?? throw new ArgumentNullException(nameof(value)));

        /// <summary>실패 응답을 생성한다. status는 0이 될 수 없다.</summary>
        public static Reply<TRes> Fail(int status) =>
            status != 0
                ? new Reply<TRes>(status, null)
                : throw new ArgumentException("실패 상태코드는 0이 될 수 없다.", nameof(status));

        /// <summary>응답 메시지에서 성공 Reply로의 암시적 변환.</summary>
        public static implicit operator Reply<TRes>(TRes value) => Ok(value);

        /// <summary>ReplyFailure에서 실패 Reply로의 암시적 변환.</summary>
        public static implicit operator Reply<TRes>(ReplyFailure failure) => Fail(failure.Status);
    }

    /// <summary>제네릭 인자 없이 Reply.Fail(코드)를 쓰게 해주는 중간 값.</summary>
    public readonly struct ReplyFailure
    {
        /// <summary>실패 상태코드.</summary>
        public int Status { get; }

        /// <summary>지정한 상태코드로 실패 값을 생성한다.</summary>
        public ReplyFailure(int status)
        {
            Status = status;
        }
    }

    /// <summary>Reply&lt;TRes&gt;의 비제네릭 도우미.</summary>
    public static class Reply
    {
        /// <summary>지정한 상태코드로 ReplyFailure를 생성한다.</summary>
        public static ReplyFailure Fail(int status) => new ReplyFailure(status);
    }
}
