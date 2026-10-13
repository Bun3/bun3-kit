using System.Collections.Generic;

namespace Bun3.Server.Players
{
    internal sealed class PlayerSaveData
    {
        internal PlayerSaveData(string key, long? expectedVersion, int capturedDirtyVersion, object changes, IReadOnlyList<PlayerAuditRecord> auditRecords)
        { Key = key; ExpectedVersion = expectedVersion; CapturedDirtyVersion = capturedDirtyVersion; Changes = changes; AuditRecords = auditRecords; }
        internal string Key { get; }
        internal long? ExpectedVersion { get; }
        internal int CapturedDirtyVersion { get; }
        internal object Changes { get; }
        internal IReadOnlyList<PlayerAuditRecord> AuditRecords { get; }
    }
}
