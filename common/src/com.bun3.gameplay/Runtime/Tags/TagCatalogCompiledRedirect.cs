#nullable enable

namespace Bun3.Gameplay.Tags
{
    internal readonly struct CompiledRedirect
    {
        internal CompiledRedirect(string from, string to)
        {
            From = from;
            To = to;
        }

        internal string From { get; }
        internal string To { get; }
    }
}
