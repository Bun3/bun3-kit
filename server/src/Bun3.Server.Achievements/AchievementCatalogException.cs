using System;

namespace Bun3.Server.Achievements
{
    /// <summary>Catalog build or lookup failure — a startup-blocking data error.</summary>
    public class AchievementCatalogException : Exception
    {
        /// <summary>Creates the exception with a message.</summary>
        public AchievementCatalogException(string message) : base(message)
        {
        }

        /// <summary>Creates the exception with a message and an inner exception.</summary>
        public AchievementCatalogException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
