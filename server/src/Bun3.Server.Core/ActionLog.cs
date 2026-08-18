using System;

namespace Bun3.Server.Core
{
    /// <summary>로그 항목의 종류.</summary>
    public enum ActionLogEntryKind : byte
    {
        /// <summary>스코프 시작 — <see cref="ActionLogEntry.Text"/>가 행동 이름·문맥.</summary>
        ScopeStart,

        /// <summary>시스템이 남긴 자유 노트(업적 클리어·추첨 결과·천장 카운터 등).</summary>
        Note,

        /// <summary>구조화 항목 — <see cref="ActionLogEntry.Data"/>에 시스템 정의 페이로드
        /// (예: Items의 InventoryChange), <see cref="ActionLogEntry.Source"/>에 출처 라벨.</summary>
        Data,
    }

    /// <summary>CS 감사 원장의 한 항목 — Depth로 들여쓰면 "행동 트레이스"가 된다.</summary>
    public readonly struct ActionLogEntry
    {
        internal ActionLogEntry(ActionLogEntryKind kind, int depth, string? text, string? source, object? data)
        {
            Kind = kind;
            Depth = depth;
            Text = text;
            Source = source;
            Data = data;
        }

        /// <summary>항목 종류.</summary>
        public ActionLogEntryKind Kind { get; }

        /// <summary>중첩 깊이(루트 스코프 = 0).</summary>
        public int Depth { get; }

        /// <summary>스코프 이름 또는 노트 내용. Data 항목은 null.</summary>
        public string? Text { get; }

        /// <summary>구조화 항목의 출처 라벨(복수 인벤토리 구분 등). 미지정이면 null.</summary>
        public string? Source { get; }

        /// <summary>시스템 정의 페이로드 — 싱크가 타입 매칭으로 해석한다
        /// (예: <c>entry.Data is InventoryChange c</c>). Data 항목에만 유효.</summary>
        public object? Data { get; }
    }

    /// <summary>원장 싱크 — 루트 스코프가 닫힐 때(스코프 밖 항목은 즉시) 완성된 묶음을
    /// 받는다. span은 호출 동안만 유효 — 보관은 복사/직렬화로.</summary>
    /// <param name="entries">완성된 로그 항목들(발생 순서, Depth 들여쓰기).</param>
    public delegate void ActionLogHandler(ReadOnlySpan<ActionLogEntry> entries);

    /// <summary>로그 스코프 핸들 — using으로 닫는다. 여는 순서의 역순으로만 닫을 수 있다.</summary>
    public readonly struct ActionLogScope : IDisposable
    {
        private readonly ActionLog? _log;
        private readonly int _token;

        internal ActionLogScope(ActionLog? log, int token)
        {
            _log = log;
            _token = token;
        }

        /// <summary>스코프를 닫는다 — 루트가 닫히면 원장 싱크로 묶음이 전달된다.</summary>
        public void Dispose() => _log?.EndScope(_token);
    }

    /// <summary>
    /// 행동 로그(CS 감사 원장) — 세션/플레이어당 1개를 두는 범용 문맥. 핸들러가 루트
    /// 스코프를 열면 그 아래에서 일어나는 모든 일 — 각 모듈의 구조화 항목(인벤토리 변경
    /// 등 자동 첨부)과 자유 노트(업적 클리어 등) — 이 하나의 트리에 순서대로 남고,
    /// 하위 로직은 "뭘로 시작됐는지" 몰라도 된다. 스코프는 자유 중첩, 루트가 닫힐 때
    /// 완성 묶음이 싱크로 전달되며, 스코프 밖 항목은 즉시 전달돼 원장에 구멍이 없다.
    /// 저빈도 감사 경로라 문자열·박싱 할당을 허용한다. 단일 세션 액터 안에서만 쓴다(락 없음).
    /// </summary>
    public sealed class ActionLog
    {
        private readonly ActionLogHandler _sink;
        private ActionLogEntry[] _entries = Array.Empty<ActionLogEntry>();
        private int _count;
        private int _depth;

        /// <summary>원장 싱크로 로그를 만든다 — 게임은 싱크 어댑터 하나로
        /// (플레이어·시각·행동 트리·페이로드)를 영속화하면 라이브 CS 추적이 완성된다.</summary>
        public ActionLog(ActionLogHandler sink)
        {
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        }

        /// <summary>스코프를 연다 — 이름에 행동과 문맥을 담는다(예: "BuyItem product=1021 x3").</summary>
        public ActionLogScope BeginScope(string name)
        {
            AppendEntry(ActionLogEntryKind.ScopeStart, name, null, null);
            _depth++;
            return new ActionLogScope(this, _depth);
        }

        /// <summary>현재 스코프에 자유 노트를 남긴다 — 어떤 시스템이든.
        /// 스코프 밖이면 즉시 단건 묶음으로 전달된다.</summary>
        public void Log(string note)
        {
            AppendEntry(ActionLogEntryKind.Note, note, null, null);
            FlushIfRoot();
        }

        /// <summary>현재 스코프에 구조화 항목을 남긴다 — 모듈이 자기 타입을 그대로 싣고
        /// 싱크가 타입 매칭으로 해석한다. 스코프 밖이면 즉시 전달된다.</summary>
        /// <param name="data">시스템 정의 페이로드.</param>
        /// <param name="source">출처 라벨(복수 컨테이너 구분 등).</param>
        public void Append(object data, string? source = null)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            AppendEntry(ActionLogEntryKind.Data, null, source, data);
            FlushIfRoot();
        }

        internal void EndScope(int token)
        {
            if (token != _depth)
            {
                throw new InvalidOperationException("로그 스코프는 여는 순서의 역순으로 닫아야 합니다.");
            }

            _depth--;
            FlushIfRoot();
        }

        private void AppendEntry(ActionLogEntryKind kind, string? text, string? source, object? data)
        {
            if (_count == _entries.Length)
            {
                Array.Resize(ref _entries, _entries.Length == 0 ? 16 : _entries.Length * 2);
            }

            _entries[_count++] = new ActionLogEntry(kind, _depth, text, source, data);
        }

        private void FlushIfRoot()
        {
            if (_depth != 0 || _count == 0)
            {
                return;
            }

            var count = _count;
            _count = 0;
            _sink(new ReadOnlySpan<ActionLogEntry>(_entries, 0, count));
        }
    }
}
