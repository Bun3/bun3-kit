using System.Collections.Generic;

namespace Bun3.Unity.Rooms
{
    /// <summary>Host election shared by backends and sessions.</summary>
    public static class RoomOwnerElection
    {
        /// <summary>
        /// The member who joined first, ignoring <paramref name="excludedId"/>. Ties break by ordinal id.
        /// Returns null when no candidate remains. Does not allocate.
        /// </summary>
        public static string Oldest(IReadOnlyList<RoomMember> members, string excludedId)
        {
            string oldest = null;
            long joinedAt = 0;
            for (int i = 0; i < members.Count; i++)
            {
                var member = members[i];
                if (member.Id == excludedId) continue;
                if (oldest == null || member.JoinedAt < joinedAt ||
                    member.JoinedAt == joinedAt && string.CompareOrdinal(member.Id, oldest) < 0)
                {
                    oldest = member.Id;
                    joinedAt = member.JoinedAt;
                }
            }
            return oldest;
        }
    }
}
