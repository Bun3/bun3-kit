using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace Bun3.Unity.Rooms.Ngo.Tests
{
    // In-memory room shared by local members. Losing the owner hands ownership to the newest member,
    // like a platform picking an arbitrary owner, so sessions must converge on the elected host.
    internal sealed class FakeRoomHub
    {
        public readonly List<RoomMember> Members = new List<RoomMember>();
        public readonly Dictionary<string, string> Data = new Dictionary<string, string>();
        public readonly List<FakeRoom> Views = new List<FakeRoom>();
        public string OwnerId;
        public bool IgnoreTransfers;
        private long clock;

        public FakeRoom Join(string memberId)
        {
            Members.Add(new RoomMember(memberId, ++clock));
            OwnerId ??= memberId;
            var view = new FakeRoom(this, memberId);
            Views.Add(view);
            RaiseMembersChanged();
            return view;
        }

        public void Remove(string memberId)
        {
            Members.RemoveAll(member => member.Id == memberId);
            Views.RemoveAll(view => view.LocalMemberId == memberId);
            bool ownerLeft = OwnerId == memberId;
            if (ownerLeft) OwnerId = Members.Count > 0 ? Members[Members.Count - 1].Id : null;
            RaiseMembersChanged();
            if (ownerLeft) RaiseOwnerChanged();
        }

        public void SetOwner(string memberId)
        {
            if (IgnoreTransfers) return;
            OwnerId = memberId;
            RaiseOwnerChanged();
        }

        private void RaiseMembersChanged()
        {
            foreach (var view in Views.ToArray()) view.RaiseMembersChanged();
        }

        private void RaiseOwnerChanged()
        {
            foreach (var view in Views.ToArray()) view.RaiseOwnerChanged();
        }
    }

    internal sealed class FakeRoom : IRoom
    {
        private readonly FakeRoomHub hub;

        public FakeRoom(FakeRoomHub hub, string memberId)
        {
            this.hub = hub;
            LocalMemberId = memberId;
        }

        public string Id => "fake";
        public RoomCode Code => RoomCode.FromValue(1);
        public string Name => "Fake";
        public IReadOnlyList<RoomMember> Members => hub.Members;
        public string LocalMemberId { get; }
        public string OwnerId => hub.OwnerId;
        public bool IsOwner => hub.OwnerId == LocalMemberId;
        public int MaxMembers => 0;
        public bool IsJoinable => true;
        public bool IsPublic => false;
        public event Action MembersChanged;
        public event Action OwnerChanged;
        public event Action DataChanged { add { } remove { } }
        public event Action Closed;

        public string GetData(string key) => hub.Data.TryGetValue(key, out string value) ? value : null;

        public UniTask SetDataAsync(string key, string value)
        {
            RequireOwner();
            hub.Data[key] = value;
            return UniTask.CompletedTask;
        }

        public UniTask SetJoinableAsync(bool joinable) => UniTask.CompletedTask;
        public UniTask SetPublicAsync(bool isPublic) => UniTask.CompletedTask;

        public UniTask TransferOwnershipAsync(string memberId)
        {
            RequireOwner();
            hub.SetOwner(memberId);
            return UniTask.CompletedTask;
        }

        public UniTask LeaveAsync()
        {
            hub.Remove(LocalMemberId);
            return UniTask.CompletedTask;
        }

        public void RaiseMembersChanged() => MembersChanged?.Invoke();
        public void RaiseOwnerChanged() => OwnerChanged?.Invoke();
        public void RaiseClosed() => Closed?.Invoke();

        private void RequireOwner()
        {
            if (!IsOwner) throw new InvalidOperationException("Not the owner.");
        }
    }
}
