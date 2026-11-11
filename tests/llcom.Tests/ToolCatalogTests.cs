using System;
using System.Linq;
using llcom.Services.Tooling;
using Xunit;

namespace llcom.Tests
{
    public class ToolCatalogTests
    {
        [Fact]
        public void Ids_AreUniqueAndNonEmpty()
        {
            var ids = ToolCatalog.Tools.Select(tool => tool.Id).ToList();

            Assert.All(ids, id => Assert.False(string.IsNullOrWhiteSpace(id)));
            Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }

        [Fact]
        public void EveryTool_HasTitleAndPageUri()
        {
            Assert.NotEmpty(ToolCatalog.Tools);

            foreach (var tool in ToolCatalog.Tools)
            {
                Assert.False(string.IsNullOrWhiteSpace(tool.FallbackTitle));
                Assert.False(string.IsNullOrWhiteSpace(tool.PageUri));
                Assert.StartsWith("Pages/", tool.PageUri);
                Assert.EndsWith(".xaml", tool.PageUri);
                if (tool.TitleKey != null)
                    Assert.False(string.IsNullOrWhiteSpace(tool.TitleKey));
            }
        }

        [Fact]
        public void Catalog_ContainsExistingTools()
        {
            Assert.NotNull(ToolCatalog.Find("dlt645"));
            Assert.NotNull(ToolCatalog.Find("tcp-test"));
            Assert.NotNull(ToolCatalog.Find("serial-monitor"));
            Assert.Null(ToolCatalog.Find("not-a-tool"));
        }
    }
}
