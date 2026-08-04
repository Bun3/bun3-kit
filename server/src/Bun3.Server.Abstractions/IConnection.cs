using System;
using System.Threading;
using System.Threading.Tasks;

namespace Bun3.Server.Abstractions
{
    /// <summary>
    /// 연결된 원격 상대 하나. 전송(TCP/Steam/인프로세스)에 무관한 프레임 단위 송신 계약.
    /// </summary>
    public interface IConnection
    {
        /// <summary>
        /// 프로세스 내 유일 연결 식별자(단조 증가). 로그 상관·레지스트리 키 용도.
        /// 계정/플레이어 ID가 아니며 재접속 시 새 값이 부여된다.
        /// </summary>
        long Id { get; }

        /// <summary>전송별 원격 주소 표현. TCP는 "IP:포트", Steam은 SteamID 문자열.</summary>
        string? RemoteAddress { get; }

        bool IsOpen { get; }

        /// <summary>
        /// 프레임 하나를 송신한다. 닫힌 연결에 대한 호출은 no-op이다(예외를 던지지 않는다).
        /// </summary>
        ValueTask SendAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default);

        /// <summary>연결을 닫는다. 멱등. 이후 전송 구현이 OnClosed를 정확히 1회 통지한다.</summary>
        void Close();
    }
}
