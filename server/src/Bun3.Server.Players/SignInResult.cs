namespace Bun3.Server.Players
{
    /// <summary>SignInAsync 결과.</summary>
    public readonly struct SignInResult<TPlayer> where TPlayer : Player
    {
        /// <summary>바인딩된 Player (신규 로드 또는 기존 재바인딩).</summary>
        public TPlayer Player { get; }

        /// <summary>true면 기존 Player 재사용(유예 재바인딩 또는 중복 로그인 이전).</summary>
        public bool IsReconnect { get; }

        /// <summary>바인딩된 Player와 재접속 여부로 결과를 생성한다.</summary>
        public SignInResult(TPlayer player, bool isReconnect)
        {
            Player = player;
            IsReconnect = isReconnect;
        }
    }
}
