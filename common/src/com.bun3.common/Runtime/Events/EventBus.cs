using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;

namespace Bun3.Common.Events
{
    /// <summary>
    /// Receives an event published on an <see cref="EventBus"/>. The event is passed by readonly reference.
    /// </summary>
    /// <typeparam name="T">Struct event type.</typeparam>
    /// <param name="evt">The published event.</param>
    public delegate void EventBusHandler<T>(in T evt) where T : struct;

    /// <summary>
    /// Marks an event type that describes current state. The bus retains its latest value
    /// and replays it to every new subscriber immediately.
    /// </summary>
    public interface IStateEvent { }

    /// <summary>
    /// Synchronous publish/subscribe for struct events. Publishing does not allocate once a
    /// channel exists. Instances define scope: inject the bus that matches the lifetime of the
    /// subscribers. Not thread safe; use from a single thread.
    /// </summary>
    public sealed class EventBus : IDisposable
    {
        /// <summary>Maximum nested publishes of one event type before publishing throws.</summary>
        public const int MaxPublishDepth = 8;

        private readonly Dictionary<Type, IChannel> _channels = new Dictionary<Type, IChannel>();
        private readonly Action<Exception> _onHandlerException;
        private bool _disposed;

        /// <summary>Creates a bus.</summary>
        /// <param name="onHandlerException">
        /// Receives subscriber exceptions while the remaining subscribers keep running. When null,
        /// all subscribers run and the first exception is rethrown afterwards.
        /// </param>
        public EventBus(Action<Exception> onHandlerException = null)
        {
            _onHandlerException = onHandlerException;
        }

        /// <summary>
        /// Delivers <paramref name="evt"/> to current subscribers in subscription order. State events
        /// are retained even without subscribers.
        /// </summary>
        /// <exception cref="ObjectDisposedException">The bus is disposed.</exception>
        /// <exception cref="InvalidOperationException">Nested publishing of <typeparamref name="T"/> exceeds <see cref="MaxPublishDepth"/>.</exception>
        public void Publish<T>(in T evt) where T : struct
        {
            ThrowIfDisposed();
            Channel<T> channel;
            if (_channels.TryGetValue(typeof(T), out var existing)) channel = (Channel<T>)existing;
            else if (Channel<T>.Retains) channel = Create<T>();
            else return;
            channel.Publish(in evt);
        }

        /// <summary>
        /// Adds <paramref name="handler"/>. Subscribing the same handler twice is ignored. For
        /// <see cref="IStateEvent"/> types the latest value, if any, is delivered immediately.
        /// A handler added during a publish receives events from the next publish.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="handler"/> is null.</exception>
        /// <exception cref="ObjectDisposedException">The bus is disposed.</exception>
        public void Subscribe<T>(EventBusHandler<T> handler) where T : struct
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            ThrowIfDisposed();
            var channel = _channels.TryGetValue(typeof(T), out var existing) ? (Channel<T>)existing : Create<T>();
            channel.Subscribe(handler);
        }

        /// <summary>
        /// Removes <paramref name="handler"/>. A handler removed during a publish is not called
        /// again in that publish. Unknown handlers and a disposed bus are ignored.
        /// </summary>
        public void Unsubscribe<T>(EventBusHandler<T> handler) where T : struct
        {
            if (handler == null || _disposed) return;
            if (_channels.TryGetValue(typeof(T), out var existing)) ((Channel<T>)existing).Unsubscribe(handler);
        }

        /// <summary>Reads the retained value of an <see cref="IStateEvent"/> type.</summary>
        /// <returns>False for non-state events, before the first publish, and after disposal.</returns>
        public bool TryGetLast<T>(out T evt) where T : struct
        {
            if (!_disposed && _channels.TryGetValue(typeof(T), out var existing))
                return ((Channel<T>)existing).TryGetLast(out evt);
            evt = default;
            return false;
        }

        /// <summary>Removes every subscriber and retained value. Further publishing or subscribing throws.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var channel in _channels.Values) channel.Clear();
            _channels.Clear();
        }

        private Channel<T> Create<T>() where T : struct
        {
            var channel = new Channel<T>(_onHandlerException);
            _channels.Add(typeof(T), channel);
            return channel;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(EventBus));
        }

        private interface IChannel
        {
            void Clear();
        }

        private sealed class PublishDepthExceededException : InvalidOperationException
        {
            public PublishDepthExceededException(Type eventType)
                : base("Nested publishing of " + eventType.Name + " exceeded " + MaxPublishDepth + ".") { }
        }

        private sealed class Channel<T> : IChannel where T : struct
        {
            public static readonly bool Retains = typeof(IStateEvent).IsAssignableFrom(typeof(T));

            private readonly Action<Exception> _onHandlerException;
            private EventBusHandler<T>[] _handlers = Array.Empty<EventBusHandler<T>>();
            private int _count;
            private int _depth;
            private bool _hasRemoved;
            private T _last;
            private bool _hasLast;

            public Channel(Action<Exception> onHandlerException)
            {
                _onHandlerException = onHandlerException;
            }

            public void Publish(in T evt)
            {
                if (Retains)
                {
                    _last = evt;
                    _hasLast = true;
                }
                if (_depth >= MaxPublishDepth) throw new PublishDepthExceededException(typeof(T));

                ExceptionDispatchInfo first = null;
                _depth++;
                try
                {
                    // Indices stay stable during dispatch: removals only null slots and appends land past the snapshot.
                    var count = _count;
                    for (var i = 0; i < count; i++)
                    {
                        var handler = _handlers[i];
                        if (handler != null) Invoke(handler, in evt, ref first);
                    }
                }
                finally
                {
                    _depth--;
                    if (_depth == 0 && _hasRemoved) Compact();
                }
                first?.Throw();
            }

            public void Subscribe(EventBusHandler<T> handler)
            {
                for (var i = 0; i < _count; i++)
                    if (handler.Equals(_handlers[i])) return;
                if (_count == _handlers.Length) Array.Resize(ref _handlers, _count == 0 ? 4 : _count * 2);
                _handlers[_count++] = handler;

                if (!_hasLast) return;
                var last = _last;
                ExceptionDispatchInfo first = null;
                Invoke(handler, in last, ref first);
                first?.Throw();
            }

            public void Unsubscribe(EventBusHandler<T> handler)
            {
                for (var i = 0; i < _count; i++)
                {
                    if (!handler.Equals(_handlers[i])) continue;
                    _handlers[i] = null;
                    _hasRemoved = true;
                    if (_depth == 0) Compact();
                    return;
                }
            }

            public bool TryGetLast(out T evt)
            {
                evt = _last;
                return _hasLast;
            }

            public void Clear()
            {
                Array.Clear(_handlers, 0, _count);
                _count = 0;
                _hasRemoved = false;
                _last = default;
                _hasLast = false;
            }

            private void Invoke(EventBusHandler<T> handler, in T evt, ref ExceptionDispatchInfo first)
            {
                try
                {
                    handler(in evt);
                }
                catch (PublishDepthExceededException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    if (_onHandlerException != null) _onHandlerException(exception);
                    else if (first == null) first = ExceptionDispatchInfo.Capture(exception);
                }
            }

            private void Compact()
            {
                var write = 0;
                for (var read = 0; read < _count; read++)
                {
                    var handler = _handlers[read];
                    if (handler == null) continue;
                    _handlers[write++] = handler;
                }
                Array.Clear(_handlers, write, _count - write);
                _count = write;
                _hasRemoved = false;
            }
        }
    }
}
