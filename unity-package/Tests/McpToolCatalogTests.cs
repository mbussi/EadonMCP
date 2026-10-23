using System.Text.RegularExpressions;
using NUnit.Framework;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.TestTools;
using UnityMcpBridge.Editor.Core;

namespace UnityMcpBridge.Editor.Tests
{
    /// <summary>
    /// EditMode tests for the tool catalog: the built-in tools loaded from
    /// Catalog/builtin-tools.json, describe_tool, the registry's refusals, and the
    /// argument spellings Unity accepts from the in-editor agent.
    /// </summary>
    [TestFixture]
    public class McpToolCatalogTests
    {
        [Test]
        public void Catalog_LoadsBuiltInTools()
        {
            Assert.IsTrue(McpToolCatalog.TryGet("place_prefab", out var tool));
            Assert.IsTrue(tool.IsBuiltIn);
            Assert.AreEqual("POST", tool.Method);
            Assert.IsFalse(string.IsNullOrEmpty(tool.Path));
            Assert.IsFalse(string.IsNullOrEmpty(tool.Summary));
            Assert.IsNotNull(tool.InputSchema["properties"]);
        }

        [Test]
        public void Catalog_IncludesDescribeTool()
        {
            Assert.IsTrue(McpToolCatalog.IsBuiltIn(McpToolCatalog.DescribeToolName));
            Assert.IsFalse(McpToolCatalog.NeedsConfirmation(McpToolCatalog.DescribeToolName, confirmAllChanges: true));
        }

        [Test]
        public void Describe_ReturnsTheFullDescription()
        {
            var response = McpToolCatalog.Describe("dress_area");
            Assert.IsTrue(response.Success);

            var json = JObject.FromObject(response.Data);
            Assert.AreEqual("dress_area", (string)json["name"]);
            Assert.Greater(((string)json["description"]).Length, ((string)json["summary"]).Length);
        }

        [Test]
        public void Describe_UnknownTool_ReturnsNotFound()
        {
            var response = McpToolCatalog.Describe("no_such_tool");
            Assert.IsFalse(response.Success);
            Assert.AreEqual("NOT_FOUND", response.Error.Code);
        }

        [Test]
        public void GetCatalog_SummaryDetail_OmitsDescriptions()
        {
            var json = JObject.FromObject(McpToolCatalog.GetCatalog("builtin", "summary").Data);
            Assert.Greater((int)json["count"], 50);
            foreach (var tool in (JArray)json["tools"])
                Assert.IsNull(tool["description"], (string)tool["name"]);
        }

        [Test]
        public void NeedsConfirmation_FollowsTheToolKind()
        {
            Assert.IsTrue(McpToolCatalog.NeedsConfirmation("unknown_tool", confirmAllChanges: false));
            Assert.IsTrue(McpToolCatalog.NeedsConfirmation("delete_object", confirmAllChanges: false));
            Assert.IsFalse(McpToolCatalog.NeedsConfirmation("place_prefab", confirmAllChanges: false));
            Assert.IsTrue(McpToolCatalog.NeedsConfirmation("place_prefab", confirmAllChanges: true));
            Assert.IsFalse(McpToolCatalog.NeedsConfirmation("get_scene_hierarchy", confirmAllChanges: true));
        }

        [Test]
        public void Register_BuiltInName_IsRefused()
        {
            LogAssert.Expect(LogType.Error, new Regex("built-in tool"));
            McpToolRegistry.Register(new McpToolDefinition
            {
                Name    = "place_prefab",
                Owner   = "Catalog test",
                Handler = _ => System.Threading.Tasks.Task.FromResult(McpResponse.Ok(null)),
            });
            Assert.IsFalse(McpToolRegistry.TryGet("place_prefab", out _));
        }

        [Test]
        public void CallAsync_BuiltInTool_PointsToTheDirectRoute()
        {
            var response = McpToolRegistry.CallAsync("place_prefab", new JObject()).GetAwaiter().GetResult();
            Assert.AreEqual("NOT_AN_EXTENSION_TOOL", response.Error.Code);
        }

        [Test]
        public void CallAsync_UnknownTool_ReturnsNotFound()
        {
            var response = McpToolRegistry.CallAsync("no_such_tool", null).GetAwaiter().GetResult();
            Assert.AreEqual("NOT_FOUND", response.Error.Code);
        }

        [Test]
        public void ComponentPropertyValue_AcceptsBothSpellings()
        {
            var snake = JsonConvert.DeserializeObject<ComponentPropertyValue>(@"{""property_path"":""m_Mass"",""value"":5}");
            var camel = JsonConvert.DeserializeObject<ComponentPropertyValue>(@"{""propertyPath"":""m_Mass"",""value"":5}");
            Assert.AreEqual("m_Mass", snake.propertyPath);
            Assert.AreEqual("m_Mass", camel.propertyPath);
        }
    }
}
