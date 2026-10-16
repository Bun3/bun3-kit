using System.Collections.Generic;

namespace Bun3.Unity.Rooms
{
    /// <summary>Options for <see cref="IRoomBackend.ListPublicAsync"/>.</summary>
    public struct RoomListOptions
    {
        /// <summary>Most rooms one list returns.</summary>
        public const int Limit = 50;

        /// <summary>Compared ordinally with each room's <see cref="RoomCreateOptions.Version"/>.</summary>
        public string Version;

        /// <summary>Rooms to return; values outside 1..<see cref="Limit"/> mean <see cref="Limit"/>.</summary>
        public int MaxResults;

        /// <summary>Room data keys to read with each room; null reads none.</summary>
        public IReadOnlyList<string> DataKeys;

        /// <summary><see cref="MaxResults"/> clamped to 1..<see cref="Limit"/>.</summary>
        public readonly int Count => MaxResults < 1 || MaxResults > Limit ? Limit : MaxResults;
    }

    /// <summary>A public room from <see cref="IRoomBackend.ListPublicAsync"/>. Join it with <see cref="IRoomBackend.JoinByIdAsync"/>.</summary>
    public sealed class RoomListing
    {
        private readonly IReadOnlyDictionary<string, string> data;

        /// <summary>Creates a listing. <paramref name="data"/> holds the requested keys and may be null.</summary>
        public RoomListing(string id, string name, int memberCount, int maxMembers, IReadOnlyDictionary<string, string> data)
        {
            Id = id;
            Name = name;
            MemberCount = memberCount;
            MaxMembers = maxMembers;
            this.data = data;
        }

        /// <summary>Backend room id, accepted by <see cref="IRoomBackend.JoinByIdAsync"/>.</summary>
        public string Id { get; }

        /// <summary>Room name; null when the backend does not know it.</summary>
        public string Name { get; }

        /// <summary>Members when the list was made.</summary>
        public int MemberCount { get; }

        /// <summary>Capacity including the owner; 0 when unknown.</summary>
        public int MaxMembers { get; }

        /// <summary>Requested room data for <paramref name="key"/>; null when absent, empty or not requested.</summary>
        public string GetData(string key) =>
            key != null && data != null && data.TryGetValue(key, out string value) && !string.IsNullOrEmpty(value) ? value : null;
    }
}
