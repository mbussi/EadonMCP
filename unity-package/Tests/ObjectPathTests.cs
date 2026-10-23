using NUnit.Framework;
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

        [Test]
        public void OtherLoadedScene_IsSearched_AndPrefixed()
        {
            // Unity cannot add a scene while an untitled scene is unsaved, which is how the
            // Test Runner usually runs EditMode tests. The cross-scene search is then
            // checked by hand (manage_scene create, then a path into the new scene).
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (string.IsNullOrEmpty(SceneManager.GetSceneAt(i).path))
                    Assert.Ignore("Needs every open scene saved: Unity cannot add a scene next to an untitled one.");

            var active = SceneManager.GetActiveScene();
            var scene  = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(active);
            try
            {
                var go = new GameObject("PathTestElsewhere");
                SceneManager.MoveGameObjectToScene(go, scene);

                Assert.AreSame(go, PlacementHandler.FindByPath("PathTestElsewhere"));
                var path = PlacementHandler.GetPath(go);
                var expected = string.IsNullOrEmpty(scene.name)
                    ? "PathTestElsewhere"                      // an unsaved scene has no name to prefix
                    : scene.name + ":PathTestElsewhere";
                Assert.AreEqual(expected, path);
                Assert.AreSame(go, PlacementHandler.FindByPath(path));
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, removeScene: true);
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
