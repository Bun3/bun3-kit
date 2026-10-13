using System;

namespace Bun3.Server.Players
{
    internal readonly struct PlayerExecutionScope : IDisposable
    {
        private readonly PlayerExecutionTable? _table;
        private readonly PlayerExecutionTable.Gate? _gate;
        private readonly long _token;
        private readonly PlayerExecutionScope[]? _scopes;

        internal PlayerExecutionScope(PlayerExecutionTable table, PlayerExecutionTable.Gate gate, long token)
        {
            _table = table;
            _gate = gate;
            _token = token;
            _scopes = null;
        }

        internal PlayerExecutionScope(PlayerExecutionScope[] scopes)
        {
            _table = null;
            _gate = null;
            _token = 0;
            _scopes = scopes;
        }

        public void Dispose()
        {
            if (_scopes != null)
            {
                for (var i = _scopes.Length - 1; i >= 0; i--) _scopes[i].Dispose();
            }
            else if (_gate != null) _table!.Release(_gate, _token);
        }
    }
}
