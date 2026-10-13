using System;

namespace Bun3.BadWords
{
    /// <summary>
    /// Lists a static <see cref="BadWordFilter"/> field in the editor's Bad Word Tester window,
    /// so text can be checked against the filter a consumer actually uses.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class BadWordTesterSourceAttribute : Attribute
    {
    }
}
