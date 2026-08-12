#nullable enable
using Bun3.Gameplay.Tags;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests
{
    /// <summary>태그 카탈로그 redirect 계약을 검증합니다.</summary>
    [TestFixture]
    public sealed class TagCatalogRedirectTests
    {
        /// <summary>대소문자와 관계없이 이전 경로가 redirect 대상 태그를 복원하는지 검증합니다.</summary>
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

        /// <summary>redirect가 암시적으로 생성된 부모를 대상으로 할 수 있는지 검증합니다.</summary>
        [Test]
        public void Redirect_can_target_an_implicit_parent()
        {
            var catalog = TagCatalogTestData.Load(
                """{"schemaVersion":1,"tags":[{"name":"A.B"}],"redirects":[{"from":"Old","to":"A"}]}""");

            Assert.That(catalog.GetRequired("Old"), Is.EqualTo(catalog.GetRequired("A")));
        }

        /// <summary>활성 태그가 아닌 redirect target 오류가 to token의 위치를 보고하는지 검증합니다.</summary>
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
            Assert.That(error.Message, Is.EqualTo("redirect target은 활성 태그여야 합니다."));
        }

        /// <summary>활성 이름과 겹치는 redirect source가 거부되는지 검증합니다.</summary>
        [Test]
        public void Redirect_source_cannot_overlap_an_active_name()
        {
            Assert.Throws<TagCatalogException>(() => TagCatalogTestData.Load(
                """{"schemaVersion":1,"tags":[{"name":"A"},{"name":"B"}],"redirects":[{"from":"A","to":"B"}]}"""));
        }

        /// <summary>유효하지 않은 redirect graph가 거부되는지 검증합니다.</summary>
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
