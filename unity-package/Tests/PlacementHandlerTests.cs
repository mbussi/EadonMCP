using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityMcpBridge.Editor.Core;
using UnityMcpBridge.Editor.Tools;

namespace UnityMcpBridge.Editor.Tests
{
    /// <summary>
    /// EditMode tests for PlacementHandler.
    ///
    /// Handlers accept plain DTOs so tests call them directly without any
    /// HTTP mocking. MainThreadDispatcher.Tick() drains the queue synchronously
    /// since EditMode tests run on the main thread.
    /// </summary>
    [TestFixture]
    public class PlacementHandlerTests
    {
        private const string PrefabDir  = "Assets/UnityMcpBridge/Tests/Resources";
        private const string PrefabPath = "Assets/UnityMcpBridge/Tests/Resources/McpTestCube.prefab";

        // -----------------------------------------------------------------------
        // Setup / teardown
        // -----------------------------------------------------------------------

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
                if (go.name.StartsWith("McpTest") || go.name.StartsWith("TestContainer"))
                    Object.DestroyImmediate(go);
        }

        // -----------------------------------------------------------------------
        // FindByPath
        // -----------------------------------------------------------------------

        [Test]
        public void FindByPath_ReturnsNull_ForNonExistentPath()
        {
            Assert.IsNull(PlacementHandler.FindByPath("Ghost/Object"));
        }

        [Test]
        public void FindByPath_FindsRootObject()
        {
            var go = new GameObject("FindRoot");
            try   { Assert.AreEqual(go, PlacementHandler.FindByPath("FindRoot")); }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void FindByPath_FindsNestedChild()
        {
            var parent = new GameObject("P");
            var child  = new GameObject("C");
            child.transform.SetParent(parent.transform);
            try   { Assert.AreEqual(child, PlacementHandler.FindByPath("P/C")); }
            finally { Object.DestroyImmediate(parent); }
        }

        [Test]
        public void FindByPath_ToleratesLeadingSlash()
        {
            var go = new GameObject("SlashRoot");
            try   { Assert.IsNotNull(PlacementHandler.FindByPath("/SlashRoot")); }
            finally { Object.DestroyImmediate(go); }
        }

        // -----------------------------------------------------------------------
        // GetPath
        // -----------------------------------------------------------------------

        [Test]
        public void GetPath_ReturnsName_ForRootObject()
        {
            var go = new GameObject("Root");
            try   { Assert.AreEqual("Root", PlacementHandler.GetPath(go)); }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void GetPath_ReturnsFullPath_ForNestedObject()
        {
            var parent = new GameObject("A");
            var child  = new GameObject("B");
            child.transform.SetParent(parent.transform);
            try   { Assert.AreEqual("A/B", PlacementHandler.GetPath(child)); }
            finally { Object.DestroyImmediate(parent); }
        }

        // -----------------------------------------------------------------------
        // place_prefab — dry_run
        // -----------------------------------------------------------------------

        [Test]
        public void PlacePrefab_DryRun_DoesNotAddObjectToScene()
        {
            int before = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length;

            var task = PlacementHandler.PlacePrefabAsync(new PlacePrefabRequest
                { PrefabPath = PrefabPath, Position = new[] { 0f, 0f, 0f }, DryRun = true });

            MainThreadDispatcher.Tick();

            Assert.AreEqual(before, Object.FindObjectsByType<GameObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length);
            Assert.IsTrue(task.Result.Success);
            Assert.IsTrue(((PlacedObjectData)task.Result.Data).DryRun);
        }

        [Test]
        public void PlacePrefab_DryRun_ReturnsExpectedPathUnderParent()
        {
            var parent = new GameObject("ParentObj");
            try
            {
                var task = PlacementHandler.PlacePrefabAsync(new PlacePrefabRequest
                    { PrefabPath = PrefabPath, Position = new[] { 0f, 0f, 0f },
                      ParentPath = "ParentObj", DryRun = true });

                MainThreadDispatcher.Tick();

                StringAssert.StartsWith("ParentObj/", ((PlacedObjectData)task.Result.Data).InstancePath);
            }
            finally { Object.DestroyImmediate(parent); }
        }

        // -----------------------------------------------------------------------
        // place_prefab — errors
        // -----------------------------------------------------------------------

        [Test]
        public void PlacePrefab_ReturnsError_WhenPrefabPathIsInvalid()
        {
            var task = PlacementHandler.PlacePrefabAsync(new PlacePrefabRequest
                { PrefabPath = "Assets/Missing.prefab", Position = new[] { 0f, 0f, 0f } });

            MainThreadDispatcher.Tick();

            Assert.IsFalse(task.Result.Success);
            Assert.AreEqual("PREFAB_NOT_FOUND", task.Result.Error.Code);
        }

        [Test]
        public void PlacePrefab_ReturnsError_WhenParentPathIsInvalid()
        {
            var task = PlacementHandler.PlacePrefabAsync(new PlacePrefabRequest
                { PrefabPath = PrefabPath, Position = new[] { 0f, 0f, 0f },
                  ParentPath = "Ghost/Parent" });

            MainThreadDispatcher.Tick();

            Assert.IsFalse(task.Result.Success);
            Assert.AreEqual("PARENT_NOT_FOUND", task.Result.Error.Code);
        }

        // -----------------------------------------------------------------------
        // set_transform
        // -----------------------------------------------------------------------

        [Test]
        public void SetTransform_DryRun_DoesNotMoveObject()
        {
            var go = new GameObject("DryMoveObj");
            try
            {
                var task = PlacementHandler.SetTransformAsync(new SetTransformRequest
                    { ObjectPath = "DryMoveObj", Position = new[] { 99f, 99f, 99f }, DryRun = true });

                MainThreadDispatcher.Tick();

                Assert.AreEqual(Vector3.zero, go.transform.position);
                Assert.IsTrue(task.Result.Success);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void SetTransform_ReturnsError_WhenObjectNotFound()
        {
            var task = PlacementHandler.SetTransformAsync(new SetTransformRequest
                { ObjectPath = "Ghost", Position = new[] { 0f, 0f, 0f } });

            MainThreadDispatcher.Tick();

            Assert.IsFalse(task.Result.Success);
            Assert.AreEqual("OBJECT_NOT_FOUND", task.Result.Error.Code);
        }

        // -----------------------------------------------------------------------
        // delete_object
        // -----------------------------------------------------------------------

        [Test]
        public void DeleteObject_DryRun_DoesNotDeleteObject()
        {
            var go = new GameObject("DryDeleteObj");
            try
            {
                var task = PlacementHandler.DeleteObjectAsync(new DeleteObjectRequest
                    { ObjectPath = "DryDeleteObj", DryRun = true });

                MainThreadDispatcher.Tick();

                Assert.IsNotNull(GameObject.Find("DryDeleteObj"));
                Assert.IsTrue(task.Result.Success);
            }
            finally { if (go != null) Object.DestroyImmediate(go); }
        }

        [Test]
        public void DeleteObject_ReturnsError_WhenObjectNotFound()
        {
            var task = PlacementHandler.DeleteObjectAsync(new DeleteObjectRequest
                { ObjectPath = "GhostObj" });

            MainThreadDispatcher.Tick();

            Assert.IsFalse(task.Result.Success);
            Assert.AreEqual("OBJECT_NOT_FOUND", task.Result.Error.Code);
        }

        // -----------------------------------------------------------------------
        // create_empty
        // -----------------------------------------------------------------------

        [Test]
        public void CreateEmpty_DryRun_DoesNotAddObjectToScene()
        {
            int before = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length;

            var task = PlacementHandler.CreateEmptyAsync(new CreateEmptyRequest
                { Name = "TestContainer", DryRun = true });

            MainThreadDispatcher.Tick();

            Assert.AreEqual(before, Object.FindObjectsByType<GameObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length);
            Assert.IsTrue(task.Result.Success);
        }

        [Test]
        public void CreateEmpty_ReturnsError_WhenNameIsEmpty()
        {
            var task = PlacementHandler.CreateEmptyAsync(new CreateEmptyRequest { Name = "" });
            MainThreadDispatcher.Tick();

            Assert.IsFalse(task.Result.Success);
            Assert.AreEqual("INVALID_PARAMS", task.Result.Error.Code);
        }

        [Test]
        public void CreateEmpty_ReturnsError_WhenParentNotFound()
        {
            var task = PlacementHandler.CreateEmptyAsync(new CreateEmptyRequest
                { Name = "TestContainer", ParentPath = "GhostParent" });

            MainThreadDispatcher.Tick();

            Assert.IsFalse(task.Result.Success);
            Assert.AreEqual("PARENT_NOT_FOUND", task.Result.Error.Code);
        }
    }
}
