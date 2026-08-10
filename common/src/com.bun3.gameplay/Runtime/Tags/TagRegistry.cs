using System;
using System.Collections.Generic;
using System.Threading;

namespace Bun3.Gameplay.Tags
{
    /// <summary>
    /// 계층 태그 레지스트리 — 이름("A.B.C")을 핸들로 인터닝한다. 조상 태그는 자동 등록.
    /// 등록(쓰기)은 락, 핸들 기반 조회(읽기)는 락 프리 — 심 핫패스는 등록된 핸들만 다룬다.
    /// 미등록 이름은 <see cref="GetOrRegister"/>가 동적으로 등록한다(스펙 §7).
    /// </summary>
    public sealed class TagRegistry
    {
        private readonly struct Entry
        {
            public readonly string Name;
            public readonly int Parent;

            public Entry(string name, int parent)
            {
                Name = name;
                Parent = parent;
            }
        }

        private readonly object _gate = new object();
        private readonly Dictionary<string, int> _byName = new Dictionary<string, int>(StringComparer.Ordinal);
        private Entry[] _entries = new Entry[64];   // [0]은 None 자리로 비워둔다
        private int _count = 1;

        /// <summary>등록된 태그 수(None 제외).</summary>
        public int Count => Volatile.Read(ref _count) - 1;

        /// <summary>이름으로 태그를 얻는다. 미등록이면 조상 포함 등록한다. 스레드 안전.</summary>
        public GameplayTag GetOrRegister(string name)
        {
            Validate(name);
            lock (_gate)
            {
                return new GameplayTag(RegisterLocked(name));
            }
        }

        /// <summary>이름으로 등록된 태그를 찾는다. 등록하지 않는다.</summary>
        public bool TryGet(string name, out GameplayTag tag)
        {
            lock (_gate)
            {
                if (_byName.TryGetValue(name, out var handle))
                {
                    tag = new GameplayTag(handle);
                    return true;
                }
            }

            tag = GameplayTag.None;
            return false;
        }

        /// <summary>태그의 정식 이름. 등록 시 인터닝된 문자열이라 호출은 무할당이다.</summary>
        public string GetName(GameplayTag tag)
        {
            if (!tag.IsValid)
            {
                return string.Empty;
            }

            return Volatile.Read(ref _entries)[tag.Handle].Name;
        }

        /// <summary>부모 태그. 루트면 None.</summary>
        public GameplayTag GetParent(GameplayTag tag)
        {
            if (!tag.IsValid)
            {
                return GameplayTag.None;
            }

            return new GameplayTag(Volatile.Read(ref _entries)[tag.Handle].Parent);
        }

        /// <summary>ancestor가 tag 자신 또는 조상인지 — 계층 매칭의 원어. 무효 태그는 false.</summary>
        public bool IsAncestorOrSelf(GameplayTag ancestor, GameplayTag tag)
        {
            if (!ancestor.IsValid || !tag.IsValid)
            {
                return false;
            }

            var entries = Volatile.Read(ref _entries);
            var current = tag.Handle;
            while (current != 0)
            {
                if (current == ancestor.Handle)
                {
                    return true;
                }

                current = entries[current].Parent;
            }

            return false;
        }

        private int RegisterLocked(string name)
        {
            if (_byName.TryGetValue(name, out var existing))
            {
                return existing;
            }

            // 부모 먼저 (재귀 — 깊이는 태그 세그먼트 수)
            var parent = 0;
            var lastDot = name.LastIndexOf('.');
            if (lastDot > 0)
            {
                parent = RegisterLocked(name.Substring(0, lastDot));
            }

            var handle = _count;
            if (handle == _entries.Length)
            {
                var grown = new Entry[_entries.Length * 2];
                Array.Copy(_entries, grown, _entries.Length);
                grown[handle] = new Entry(name, parent);
                Volatile.Write(ref _entries, grown);   // 내용 기록 후 발행
            }
            else
            {
                _entries[handle] = new Entry(name, parent);
            }

            Volatile.Write(ref _count, handle + 1);
            _byName[name] = handle;
            return handle;
        }

        private static void Validate(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("태그 이름이 비어 있다.", nameof(name));
            }

            if (name[0] == '.' || name[name.Length - 1] == '.' || name.Contains(".."))
            {
                throw new ArgumentException($"잘못된 태그 이름: '{name}' (빈 세그먼트 금지)", nameof(name));
            }
        }
    }
}
