using System.Threading.Tasks;
using Google.Protobuf;

namespace Bun3.Server.Rpc
{
    /// <summary>Dispatches a parsed request in the context of its session.</summary>
    public delegate ValueTask<(int Status, IMessage? Response)> RpcRequestHandler(
        RpcSession session, IMessage request);
}
