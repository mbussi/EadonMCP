using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityMcpBridge.Editor.Core;
using UnityMcpBridge.Editor.Tools;

namespace UnityMcpBridge.Editor.Tests
{
    /// <summary>
    /// EditMode tests for object paths (PlacementHandler.FindByPath / GetPath): indexes
    /// on shared sibling names, ambiguity errors, other loaded scenes and the scene
    /// prefix; and for the project file path check.
    /// </summary>
    [TestFixture]
    public class ObjectPathTests
    {
        private GameObject _root;

        [SetUp]
        public void SetUp() => _root = new GameObject("PathTestRoot");

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
        }

        private GameObject Child(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent != null ? parent : _root.transform, false);
            return go;
        }

        [Test]
        public void UniqueNames_KeepPlainPaths()
        {
            var child = Child("Lamp");
            Assert.AreEqual("PathTestRoot/Lamp", PlacementHandler.GetPath(child));
            Assert.AreSame(child, PlacementHandler.FindByPath("PathTestRoot/Lamp"));
            Assert.AreSame(child, PlacementHandler.FindByPath("/PathTestRoot/Lamp"));
        }

        [Test]
        public void SharedSiblingNames_GetIndexes_AndResolve()
        {
            var first  = Child("Barrel");
            var second = Child("Barrel");

            Assert.AreEqual("PathTestRoot/Barrel[1]", PlacementHandler.GetPath(first));
            Assert.AreEqual("PathTestRoot/Barrel[2]", PlacementHandler.GetPath(second));
            Assert.AreSame(second, PlacementHandler.FindByPath("PathTestRoot/Barrel[2]"));
            Assert.IsNull(PlacementHandler.FindByPath("PathTestRoot/Barrel[3]"));
        }

        [Test]
        public void SharedName_WithoutIndex_IsAmbiguous()
        {
            Child("Barrel");
            Child("Barrel");

            var ex = Assert.Throws<AmbiguousPathException>(() => PlacementHandler.FindByPath("PathTestRoot/Barrel"));
            CollectionAssert.AreEqual(new[] { "PathTestRoot/Barrel[1]", "PathTestRoot/Barrel[2]" }, ex.Candidates);
        }

        [Test]
        public void ExactNameWithBrackets_WinsOverIndex()
        {
            var literal = Child("Item[1]");
            Child("Item");
            Assert.AreSame(literal, PlacementHandler.FindByPath("PathTestRoot/Item[1]"));
        }

        [Test]
        public void IndexedPathsReturnedByGetPath_RoundTrip()
        {
            var group = Child("Group");
            Child("Group");
            var leaf = Child("Leaf", group.transform);
            Assert.AreSame(leaf, PlacementHandler.FindByPath(PlacementHandler.GetPath(leaf)));
        }

        // -----------------------------------------------------------------------
        // Other loaded scenes
        // -----------------------------------------------------------------------

        private const string TempFolder = "Assets/McpPathTestTemp";

        /// <summary>
        /// The Test Runner runs EditMode tests in an untitled scene, and Unity cannot add a
        /// scene next to an untitled one. So these tests save their own scenes in a
        /// temporary folder, which they remove afterwards. The Test Runner reopens the
        /// user's scenes when the run ends.
        /// </summary>
        private static Scene OpenSavedMainScene()
        {
            if (!AssetDatabase.IsValidFolder(TempFolder))
                AssetDatabase.CreateFolder("Assets", "McpPathTestTemp");
            var main = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Assert.IsTrue(EditorSceneManager.SaveScene(main, TempFolder + "/PathTestMain.unity"));
            return main;
        }

        private static void CloseTempScenes()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.DeleteAsset(TempFolder);
        }

        [Test]
        public void OtherLoadedScene_IsSearched_AndPrefixed()
        {
            var main = OpenSavedMainScene();
            try
            {
                var other = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                Assert.IsTrue(EditorSceneManager.SaveScene(other, TempFolder + "/PathTestOther.unity"));
                SceneManager.SetActiveScene(main);

                var elsewhere = new GameObject("PathTestElsewhere");
                SceneManager.MoveGameObjectToScene(elsewhere, other);

                Assert.AreSame(elsewhere, PlacementHandler.FindByPath("PathTestElsewhere"));
                Assert.AreEqual("PathTestOther:PathTestElsewhere", PlacementHandler.GetPath(elsewhere));
                Assert.AreSame(elsewhere, PlacementHandler.FindByPath("PathTestOther:PathTestElsewhere"));

                // The active scene is searched first; the prefix still reaches the other scene.
                var local = new GameObject("PathTestElsewhere");
                Assert.AreEqual("PathTestElsewhere", PlacementHandler.GetPath(local));
                Assert.AreSame(local, PlacementHandler.FindByPath("PathTestElsewhere"));
                Assert.AreSame(elsewhere, PlacementHandler.FindByPath("PathTestOther:PathTestElsewhere"));
            }
            finally
            {
                CloseTempScenes();
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CreateScene_KeepsTheActiveScene_UnlessSetActive(bool setActive)
        {
            var main = OpenSavedMainScene();
            try
            {
                var task = SceneManagementHandler.ManageSceneAsync(new ManageSceneRequest
                {
                    Operation = "create",
                    ScenePath = TempFolder + "/PathTestCreated.unity",
                    SetActive = setActive,
                });
                MainThreadDispatcher.Tick();

                Assert.IsTrue(task.Result.Success, task.Result.Error?.Message);
                Assert.AreEqual(2, SceneManager.sceneCount);
                var expected = setActive ? "PathTestCreated" : main.name;
                Assert.AreEqual(expected, SceneManager.GetActiveScene().name);
            }
            finally
            {
                CloseTempScenes();
            }
        }

        // -----------------------------------------------------------------------
        // Project file paths
        // -----------------------------------------------------------------------

        [TestCase("C:/Windows")]
        [TestCase("D:\\Projects\\Other")]
        [TestCase("//server/share")]
        [TestCase("../ProjectSettings")]
        [TestCase("Scripts/../../Library")]
        public void FilePaths_OutsideAssets_AreRefused(string input)
        {
            Assert.IsFalse(ProjectFileHandler.TryResolvePath(input, out _, out _, out var error, allowAssetsRoot: true));
            Assert.IsNotEmpty(error);
        }

        [TestCase("Assets/Scripts/Foo.cs", "Scripts/Foo.cs")]
        [TestCase("Scripts/Foo.cs",        "Scripts/Foo.cs")]
        [TestCase("/Assets/Scripts",       "Scripts")]
        public void FilePaths_InsideAssets_Resolve(string input, string relative)
        {
            Assert.IsTrue(ProjectFileHandler.TryResolvePath(input, out var rel, out var full, out _));
            Assert.AreEqual(relative, rel);
            StringAssert.StartsWith(System.IO.Path.GetFullPath(Application.dataPath), full);
        }

        [Test]
        public void AssetsFolderItself_OnlyWhenAllowed()
        {
            Assert.IsTrue(ProjectFileHandler.TryResolvePath("Assets", out _, out _, out _, allowAssetsRoot: true));
            Assert.IsFalse(ProjectFileHandler.TryResolvePath("Assets", out _, out _, out _));
        }
    }
}
