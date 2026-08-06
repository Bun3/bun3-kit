using System;

namespace Bun3.Server.Players
{
    /// <summary>RejectNew 정책에서 이미 접속 중인 계정으로 SignInAsync 시 던져진다.
    /// 게임 로그인 핸들러가 잡아 게임 상태코드로 변환하는 것을 권장.</summary>
    public sealed class DuplicateLoginException : Exception
    {
        /// <summary>중복 로그인이 시도된 계정 키.</summary>
        public string AccountKey { get; }

        /// <summary>주어진 계정 키로 예외를 생성한다.</summary>
        public DuplicateLoginException(string accountKey)
            : base($"계정 {accountKey}은(는) 이미 접속 중이다 (RejectNew 정책).")
        {
            AccountKey = accountKey;
        }
    }
}
