using System;
using System.Collections.Generic;

namespace Bun3.Server.Rpc
{
    /// <summary>메시징 스키마/등록 검증 실패. Errors에 위반 전체 목록이 담긴다(fail-fast 기동 실패용).</summary>
    public sealed class RpcValidationException : Exception
    {
        /// <summary>검증 위반 전체 목록.</summary>
        public IReadOnlyList<string> Errors { get; }

        /// <summary>위반 목록으로 예외를 생성한다.</summary>
        public RpcValidationException(IReadOnlyList<string> errors)
            : base("메시징 구성 검증 실패:\n- " + string.Join("\n- ", errors))
        {
            Errors = errors;
        }
    }
}
