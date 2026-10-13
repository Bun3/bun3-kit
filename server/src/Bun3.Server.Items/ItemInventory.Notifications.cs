// ItemInventory partial — notification batch ownership and independent delivery channels.
using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;

namespace Bun3.Server.Items
{
    public sealed partial class ItemInventory<TState>
    {
        private readonly struct Notification
        {
            internal Notification(InventoryChange[] changes, int count, bool dirty)
            {
                Changes = changes;
                Count = count;
                Dirty = dirty;
            }

            internal InventoryChange[]? Changes { get; }
            internal int Count { get; }
            internal bool Dirty { get; }
        }

        private readonly List<InventoryChange[]> _notificationBuffers = new List<InventoryChange[]>();
        private Notification _committedNotification;

        private Notification FreezeNotification(bool dirty)
        {
            var notification = new Notification(_applied, _appliedCount, dirty);
            _appliedCount = 0;
            if (_applied.Length != 0)
            {
                var last = _notificationBuffers.Count - 1;
                if (last < 0) _applied = Array.Empty<InventoryChange>();
                else
                {
                    _applied = _notificationBuffers[last];
                    _notificationBuffers.RemoveAt(last);
                }
            }

            return notification;
        }

        private void DeliverNotification(Notification notification)
        {
            List<Exception>? errors = null;
            DeliverNotification(notification, ref errors);
            ThrowNotificationErrors(errors);
        }

        private void DeliverNotification(Notification notification, ref List<Exception>? errors)
        {
            try
            {
                if (_log != null)
                {
                    for (var i = 0; i < notification.Count; i++)
                    {
                        try { _log.Append(notification.Changes![i], _logLabel); }
                        catch (Exception error) { (errors ??= new List<Exception>()).Add(error); }
                    }
                }

                if (notification.Dirty)
                {
                    try { _onDirty?.Invoke(); }
                    catch (Exception error) { (errors ??= new List<Exception>()).Add(error); }
                }

                if (notification.Count > 0)
                {
                    try { _onApplied?.Invoke(new ReadOnlySpan<InventoryChange>(notification.Changes, 0, notification.Count)); }
                    catch (Exception error) { (errors ??= new List<Exception>()).Add(error); }
                }
            }
            finally
            {
                if (notification.Changes != null && notification.Changes.Length != 0)
                {
                    Array.Clear(notification.Changes, 0, notification.Count);
                    _notificationBuffers.Add(notification.Changes);
                }
            }
        }

        private void FinishOperationNotifications()
        {
            if (_txEnlisted) _txDirtyPending = true;
            else DeliverNotification(FreezeNotification(true));
        }

        private static void ThrowNotificationErrors(List<Exception>? errors)
        {
            if (errors == null) return;
            if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
            throw new AggregateException(errors);
        }
    }
}
