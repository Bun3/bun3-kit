using System.Collections.Generic;
using Sentry;

namespace Bun3.Unity.Telemetry.Sentry
{
    /// <summary>The part of the Sentry static API that <see cref="SentrySink"/> uses.</summary>
    public interface ISentryApi
    {
        /// <summary>Adds a breadcrumb at info level when <paramref name="info"/> is true, otherwise at debug level.</summary>
        void AddBreadcrumb(string message, string category, IDictionary<string, string> data, bool info);

        /// <summary>Sets a tag; a null <paramref name="value"/> removes it.</summary>
        void SetTag(string key, string value);

        /// <summary>Sets the user id.</summary>
        void SetUser(string id);

        /// <summary>Sets a context block; null <paramref name="values"/> removes it.</summary>
        void SetContext(string key, IDictionary<string, object> values);
    }

    /// <summary>Delegates to <c>Sentry.Unity.SentrySdk</c>. Call from the main thread; does nothing while the SDK is not initialized.</summary>
    public sealed class SentryApi : ISentryApi
    {
        /// <inheritdoc />
        public void AddBreadcrumb(string message, string category, IDictionary<string, string> data, bool info) =>
            global::Sentry.Unity.SentrySdk.AddBreadcrumb(message, category, null, data, info ? BreadcrumbLevel.Info : BreadcrumbLevel.Debug);

        /// <inheritdoc />
        public void SetTag(string key, string value) =>
            global::Sentry.Unity.SentrySdk.ConfigureScope(static (scope, t) =>
            {
                if (t.value == null) scope.UnsetTag(t.key);
                else scope.SetTag(t.key, t.value);
            }, (key, value));

        /// <inheritdoc />
        public void SetUser(string id) =>
            global::Sentry.Unity.SentrySdk.ConfigureScope(static (scope, v) => scope.User = new SentryUser { Id = v }, id);

        /// <inheritdoc />
        public void SetContext(string key, IDictionary<string, object> values) =>
            global::Sentry.Unity.SentrySdk.ConfigureScope(static (scope, t) =>
            {
                if (t.values == null) scope.Contexts.Remove(t.key);
                else scope.Contexts[t.key] = new Dictionary<string, object>(t.values);
            }, (key, values));
    }
}
