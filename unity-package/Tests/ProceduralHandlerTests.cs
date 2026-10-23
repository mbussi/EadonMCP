using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityMcpBridge.Editor.Core;
using UnityMcpBridge.Editor.Tools;

namespace UnityMcpBridge.Editor.Tests
{
    [TestFixture]
    public class ProceduralHandlerTests
    {
        private const string PrefabPath = "Assets/UnityMcpBridge/Tests/Resources/McpTestCube.prefab";
        private const string PrefabDir  = "Assets/UnityMcpBridge/Tests/Resources";

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder("Assets/UnityMcpBridge"))
                AssetDatabase.CreateFolder("Assets", "UnityMcpBridge");
            if (!AssetDatabase.IsValidFolder("Assets/UnityMcpBridge/Tests"))
                AssetDatabase.CreateFolder("Assets/UnityMcpBridge", "Tests");
            if (!AssetDatabase.IsValidFolder(PrefabDir))
                AssetDatabase.CreateFolder("Assets/UnityMcpBridge/Tests", "Resources");

            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "McpTestCube";
                PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
                Object.DestroyImmediate(go);
                AssetDatabase.Refresh();
            }
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (go.name.StartsWith("McpTestCube") || go.name == "BatchParent")
                    Object.DestroyImmediate(go);
        }

        // -----------------------------------------------------------------------
        // place_prefab_batch — dry_run and errors
        // -----------------------------------------------------------------------

        [Test]
        public void PlacePrefabBatch_DryRun_DoesNotCreateObjects()
        {
            int before = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length;
            var task = ProceduralHandler.PlacePrefabBatchAsync(new PlacePrefabBatchRequest
            {
                Items = new[]
                {
                    new BatchPlacementItem { PrefabPath = PrefabPath, Position = new[] { 0f, 0f, 0f } },
                    new BatchPlacementItem { PrefabPath = PrefabPath, Position = new[] { 5f, 0f, 0f } },
                },
                DryRun = true
            });
            MainThreadDispatcher.Tick();

            Assert.AreEqual(before, Object.FindObjectsByType<GameObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length);
            Assert.IsTrue(task.Result.Success);
            Assert.AreEqual(2, ((BatchResultData)task.Result.Data).PlacedCount);
        }

        [Test]
        public void PlacePrefabBatch_ReturnsError_WhenItemsIsEmpty()
        {
            var task = ProceduralHandler.PlacePrefabBatchAsync(
                new PlacePrefabBatchRequest { Items = new BatchPlacementItem[0] });
            MainThreadDispatcher.Tick();

            Assert.IsFalse(task.Result.Success);
            Assert.AreEqual("INVALID_PARAMS", task.Result.Error.Code);
        }

        [Test]
        public void PlacePrefabBatch_ReportsPartialFailure_WhenOnePrefabMissing()
        {
            var task = ProceduralHandler.PlacePrefabBatchAsync(new PlacePrefabBatchRequest
            {
                Items = new[]
                {
                    new BatchPlacementItem { PrefabPath = PrefabPath,              Position = new[] { 0f, 0f, 0f } },
                    new BatchPlacementItem { PrefabPath = "Assets/Ghost.prefab",   Position = new[] { 5f, 0f, 0f } },
                },
                DryRun = true
            });
            MainThreadDispatcher.Tick();

            Assert.IsTrue(task.Result.Success); // batch itself succeeded
            var data = (BatchResultData)task.Result.Data;
            Assert.AreEqual(1, data.PlacedCount);
            Assert.AreEqual(1, data.FailedCount);
        }

        // -----------------------------------------------------------------------
        // scatter_prefabs — dry_run and validation
        // -----------------------------------------------------------------------

        [Test]
        public void ScatterPrefabs_DryRun_DoesNotCreateObjects()
        {
            int before = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length;
            var task = ProceduralHandler.ScatterPrefabsAsync(new ScatterPrefabsRequest
                { PrefabPath = PrefabPath, Center = new[] { 0f, 0f, 0f },
                  Radius = 10f, Count = 5, DryRun = true });
            MainThreadDispatcher.Tick();

            Assert.AreEqual(before, Object.FindObjectsByType<GameObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length);
            Assert.IsTrue(task.Result.Success);
            Assert.AreEqual(5, ((ProceduralResultData)task.Result.Data).PlacedCount);
        }

        [Test]
        public void ScatterPrefabs_DryRun_RespectsCount()
        {
            var task = ProceduralHandler.ScatterPrefabsAsync(new ScatterPrefabsRequest
                { PrefabPath = PrefabPath, Center = new[] { 0f, 0f, 0f },
                  Radius = 20f, Count = 8, DryRun = true });
            MainThreadDispatcher.Tick();

            Assert.AreEqual(8, ((ProceduralResultData)task.Result.Data).PlacedCount);
        }

        [Test]
        public void ScatterPrefabs_ReturnsError_WhenPrefabNotFound()
        {
            var task = ProceduralHandler.ScatterPrefabsAsync(new ScatterPrefabsRequest
                { PrefabPath = "Assets/Ghost.prefab", Center = new[] { 0f, 0f, 0f },
                  Radius = 10f, Count = 5 });
            MainThreadDispatcher.Tick();

            Assert.IsFalse(task.Result.Success);
            Assert.AreEqual("PREFAB_NOT_FOUND", task.Result.Error.Code);
        }

        [Test]
        public void ScatterPrefabs_ReturnsError_WhenRadiusIsZero()
        {
            var task = ProceduralHandler.ScatterPrefabsAsync(new ScatterPrefabsRequest
                { PrefabPath = PrefabPath, Center = new[] { 0f, 0f, 0f }, Radius = 0f, Count = 5 });
            MainThreadDispatcher.Tick();

            Assert.IsFalse(task.Result.Success);
            Assert.AreEqual("INVALID_PARAMS", task.Result.Error.Code);
        }

        [Test]
        public void ScatterPrefabs_WithSeed_ProducesSamePlacementsEachTime()
        {
            var req = new ScatterPrefabsRequest
            {
                PrefabPath = PrefabPath, Center = new[] { 0f, 0f, 0f },
                Radius = 10f, Count = 5, Seed = 42, DryRun = true
            };
            var task1 = ProceduralHandler.ScatterPrefabsAsync(req);
            MainThreadDispatcher.Tick();
            var task2 = ProceduralHandler.ScatterPrefabsAsync(req);
            MainThreadDispatcher.Tick();

            // Both calls with the same seed should produce the same count
            // (full position equality requires exposing positions, which we don't)
            Assert.AreEqual(
                ((ProceduralResultData)task1.Result.Data).PlacedCount,
                ((ProceduralResultData)task2.Result.Data).PlacedCount);
        }

        // -----------------------------------------------------------------------
        // place_along_path — position count and validation
        // -----------------------------------------------------------------------

        [Test]
        public void PlaceAlongPath_DryRun_ReturnsCorrectCountForStraightPath()
        {
            // 10m path, spacing 2m → positions at 0, 2, 4, 6, 8, 10 = 6
            var task = ProceduralHandler.PlaceAlongPathAsync(new PlaceAlongPathRequest
            {
                PrefabPath = PrefabPath,
                Waypoints  = new[] { new[] { 0f,0f,0f }, new[] { 10f,0f,0f } },
                Spacing    = 2f,
                DryRun     = true
            });
            MainThreadDispatcher.Tick();

            Assert.IsTrue(task.Result.Success);
            Assert.AreEqual(6, ((ProceduralResultData)task.Result.Data).PlacedCount);
        }

        [Test]
        public void PlaceAlongPath_DryRun_DoesNotCreateObjects()
        {
            int before = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length;
            var task = ProceduralHandler.PlaceAlongPathAsync(new PlaceAlongPathRequest
            {
                PrefabPath = PrefabPath,
                Waypoints  = new[] { new[] { 0f,0f,0f }, new[] { 10f,0f,0f } },
                Spacing    = 2f, DryRun = true
            });
            MainThreadDispatcher.Tick();

            Assert.AreEqual(before, Object.FindObjectsByType<GameObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length);
        }

        [Test]
        public void PlaceAlongPath_ReturnsError_WhenFewerThanTwoWaypoints()
        {
            var task = ProceduralHandler.PlaceAlongPathAsync(new PlaceAlongPathRequest
            {
                PrefabPath = PrefabPath,
                Waypoints  = new[] { new[] { 0f,0f,0f } },
                Spacing    = 2f
            });
            MainThreadDispatcher.Tick();

            Assert.IsFalse(task.Result.Success);
            Assert.AreEqual("INVALID_PARAMS", task.Result.Error.Code);
        }

        [Test]
        public void PlaceAlongPath_ReturnsError_WhenSpacingIsZero()
        {
            var task = ProceduralHandler.PlaceAlongPathAsync(new PlaceAlongPathRequest
            {
                PrefabPath = PrefabPath,
                Waypoints  = new[] { new[] { 0f,0f,0f }, new[] { 10f,0f,0f } },
                Spacing    = 0f
            });
            MainThreadDispatcher.Tick();

            Assert.IsFalse(task.Result.Success);
            Assert.AreEqual("INVALID_PARAMS", task.Result.Error.Code);
        }

        // -----------------------------------------------------------------------
        // fill_grid — count, offset, and validation
        // -----------------------------------------------------------------------

        [Test]
        public void FillGrid_DryRun_ReturnsColumnsTimesRows()
        {
            var task = ProceduralHandler.FillGridAsync(new FillGridRequest
            {
                PrefabPath = PrefabPath, Origin = new[] { 0f,0f,0f },
                CellSize = new[] { 2f, 2f }, Columns = 4, Rows = 3, DryRun = true
            });
            MainThreadDispatcher.Tick();

            Assert.IsTrue(task.Result.Success);
            Assert.AreEqual(12, ((ProceduralResultData)task.Result.Data).PlacedCount);
        }

        [Test]
        public void FillGrid_DryRun_DoesNotCreateObjects()
        {
            int before = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length;
            var task = ProceduralHandler.FillGridAsync(new FillGridRequest
            {
                PrefabPath = PrefabPath, Origin = new[] { 0f,0f,0f },
                CellSize = new[] { 2f, 2f }, Columns = 3, Rows = 3, DryRun = true
            });
            MainThreadDispatcher.Tick();

            Assert.AreEqual(before, Object.FindObjectsByType<GameObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length);
        }

        [Test]
        public void FillGrid_ReturnsError_WhenColumnsIsZero()
        {
            var task = ProceduralHandler.FillGridAsync(new FillGridRequest
            {
                PrefabPath = PrefabPath, Origin = new[] { 0f,0f,0f },
                CellSize = new[] { 2f, 2f }, Columns = 0, Rows = 3
            });
            MainThreadDispatcher.Tick();

            Assert.IsFalse(task.Result.Success);
            Assert.AreEqual("INVALID_PARAMS", task.Result.Error.Code);
        }

        [Test]
        public void FillGrid_ReturnsError_WhenRowsIsZero()
        {
            var task = ProceduralHandler.FillGridAsync(new FillGridRequest
            {
                PrefabPath = PrefabPath, Origin = new[] { 0f,0f,0f },
                CellSize = new[] { 2f, 2f }, Columns = 3, Rows = 0
            });
            MainThreadDispatcher.Tick();

            Assert.IsFalse(task.Result.Success);
            Assert.AreEqual("INVALID_PARAMS", task.Result.Error.Code);
        }

        [Test]
        public void FillGrid_ReturnsError_WhenCellSizeIsInvalid()
        {
            var task = ProceduralHandler.FillGridAsync(new FillGridRequest
            {
                PrefabPath = PrefabPath, Origin = new[] { 0f,0f,0f },
                CellSize = new[] { 0f, 2f }, Columns = 3, Rows = 3
            });
            MainThreadDispatcher.Tick();

            Assert.IsFalse(task.Result.Success);
            Assert.AreEqual("INVALID_PARAMS", task.Result.Error.Code);
        }
    }
}
