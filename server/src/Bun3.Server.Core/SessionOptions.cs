namespace Bun3.Server.Core
{
    public sealed class SessionOptions
    {
        /// <summary>세션 수신 큐 상한. 초과 시 연결을 종료해 메모리를 보호한다.</summary>
        public int MaxQueuedFrames { get; set; } = 256;
    }
}
