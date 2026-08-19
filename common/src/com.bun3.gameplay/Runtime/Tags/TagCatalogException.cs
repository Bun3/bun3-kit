#nullable enable
using System;

namespace Bun3.Gameplay.Tags
{
    /// <summary>
    /// Thrown when tag catalog JSON is malformed or semantically invalid.
    /// </summary>
    public sealed class TagCatalogException : FormatException
    {
        /// <summary>JSON path where the error occurred.</summary>
        public string JsonPath { get; }

        /// <summary>1-based line number where the error occurred.</summary>
        public int LineNumber { get; }

        /// <summary>1-based position within the line where the error occurred.</summary>
        public int LinePosition { get; }

        /// <summary>Creates the exception with JSON location information.</summary>
        public TagCatalogException(string message, string jsonPath, int lineNumber, int linePosition)
            : base(message)
        {
            JsonPath = jsonPath;
            LineNumber = lineNumber;
            LinePosition = linePosition;
        }
    }
}
