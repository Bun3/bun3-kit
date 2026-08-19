#nullable enable
using Bun3.Gameplay.Tags;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests
{
    /// <summary>Verifies the tag catalog redirect contract.</summary>
    [TestFixture]
    public sealed class TagCatalogRedirectTests
    {
        /// <summary>Verifies an old path resolves to the redirect target regardless of case.</summary>
        [Test]
        public void Redirect_resolves_old_path_case_insensitively()
        {
            var catalog = TagCatalogTestData.Load(
                """
                {
                  "schemaVersion": 1,
                  "tags": [{"name":"State.Dead"}],
                  "redirects": [{"from":"State.Killed","to":"state.dead"}]
                }
                """);

            Assert.That(catalog.GetRequired("STATE.KILLED"), Is.EqualTo(catalog.GetRequired("State.Dead")));
        }

        /// <summary>Verifies a redirect can target an implicitly created parent.</summary>
        [Test]
        public void Redirect_can_target_an_implicit_parent()
        {
            var catalog = TagCatalogTestData.Load(
                """{"schemaVersion":1,"tags":[{"name":"A.B"}],"redirects":[{"from":"Old","to":"A"}]}""");

            Assert.That(catalog.GetRequired("Old"), Is.EqualTo(catalog.GetRequired("A")));
        }

        /// <summary>Verifies an inactive redirect target error reports the to token's location.</summary>
        [Test]
        public void Inactive_redirect_target_reports_the_to_token_location()
        {
            const string json =
                "{\n" +
                "  \"schemaVersion\": 1,\n" +
                "  \"tags\": [{\"name\":\"State.Active\"}],\n" +
                "  \"redirects\": [\n" +
                "    {\n" +
                "      \"from\": \"State.Old\",\n" +
                "      \"to\": \"State.Missing\"\n" +
                "    }\n" +
                "  ]\n" +
                "}";

            var error = Assert.Throws<TagCatalogException>(() => TagCatalogTestData.Load(json));

            Assert.That(error!.JsonPath, Is.EqualTo("redirects[0].to"));
            Assert.That(error.LineNumber, Is.EqualTo(7));
            Assert.That(error.LinePosition, Is.EqualTo(27));
        }

        /// <summary>Verifies a redirect source overlapping an active name is rejected.</summary>
        [Test]
        public void Redirect_source_cannot_overlap_an_active_name()
        {
            Assert.Throws<TagCatalogException>(() => TagCatalogTestData.Load(
                """{"schemaVersion":1,"tags":[{"name":"A"},{"name":"B"}],"redirects":[{"from":"A","to":"B"}]}"""));
        }

        /// <summary>Verifies invalid redirect graphs are rejected.</summary>
        [TestCase("""{"schemaVersion":1,"tags":[{"name":"A"}],"redirects":[{"from":"A","to":"A"}]}""")]
        [TestCase("""{"schemaVersion":1,"tags":[{"name":"A.B"}],"redirects":[{"from":"A","to":"A.B"}]}""")]
        [TestCase("""{"schemaVersion":1,"tags":[{"name":"A"}],"redirects":[{"from":"Old","to":"Missing"}]}""")]
        [TestCase("""{"schemaVersion":1,"tags":[{"name":"A"}],"redirects":[{"from":"Old","to":"A"},{"from":"old","to":"A"}]}""")]
        [TestCase("""{"schemaVersion":1,"tags":[{"name":"A"}],"redirects":[{"from":"Old1","to":"Old2"},{"from":"Old2","to":"A"}]}""")]
        public void Invalid_redirect_graph_is_rejected(string json)
        {
            Assert.Throws<TagCatalogException>(() => TagCatalogTestData.Load(json));
        }
    }
}
