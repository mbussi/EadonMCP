using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityMcpBridge.Editor.Core;
using UnityMcpBridge.Editor.Tools;

namespace UnityMcpBridge.Editor.Tests
{
    /// <summary>
    /// EditMode tests for LightingHandler.
    ///
    /// RenderSettings state is saved and restored around each test that
    /// touches it, so tests leave the scene in the same state they found it.
    /// </summary>
    [TestFixture]
    public class LightingHandlerTests
    {
        // Saved ambient state
        private AmbientMode _savedAmbientMode;
        private Color       _savedAmbientLight;
        private float       _savedAmbientIntensity;

        // Saved fog state
        private bool     _savedFog;
        private Color    _savedFogColor;
        private FogMode  _savedFogMode;
        private float    _savedFogDensity;
        private float    _savedFogStart;
        private float    _savedFogEnd;

        // Saved skybox
        private Material _savedSkybox;

        [SetUp]
        public void SetUp()
        {
            _savedAmbientMode      = RenderSettings.ambientMode;
            _savedAmbientLight     = RenderSettings.ambientLight;
            _savedAmbientIntensity = RenderSettings.ambientIntensity;
            _savedFog              = RenderSettings.fog;
            _savedFogColor         = RenderSettings.fogColor;
            _savedFogMode          = RenderSettings.fogMode;
            _savedFogDensity       = RenderSettings.fogDensity;
            _savedFogStart         = RenderSettings.fogStartDistance;
            _savedFogEnd           = RenderSettings.fogEndDistance;
            _savedSkybox           = RenderSettings.skybox;
        }

        [TearDown]
        public void TearDown()
        {
            RenderSettings.ambientMode        = _savedAmbientMode;
            RenderSettings.ambientLight       = _savedAmbientLight;
            RenderSettings.ambientIntensity   = _savedAmbientIntensity;
            RenderSettings.fog                = _savedFog;
            RenderSettings.fogColor           = _savedFogColor;
            RenderSettings.fogMode            = _savedFogMode;
            RenderSettings.fogDensity         = _savedFogDensity;
            RenderSettings.fogStartDistance   = _savedFogStart;
            RenderSettings.fogEndDistance     = _savedFogEnd;
            RenderSettings.skybox             = _savedSkybox;

            // Clean up any lights placed by tests
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (go.name.EndsWith("Light") || go.name == "TestSpot")
                    Object.DestroyImmediate(go);
        }

        // -----------------------------------------------------------------------
        // create_light — dry_run
        // -----------------------------------------------------------------------

        [Test]
        public void CreateLight_DryRun_DoesNotAddGameObject()
        {
            int before = Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length;

            var task = LightingHandler.CreateLightAsync(new CreateLightRequest
                { LightType = "Point", Position = new[] { 0f, 3f, 0f }, DryRun = true });

            MainThreadDispatcher.Tick();

            Assert.AreEqual(before, Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length);
            Assert.IsTrue(task.Result.Success);
            Assert.IsTrue(((CreatedLightData)task.Result.Data).DryRun);
        }

        [Test]
        public void CreateLight_DryRun_ReturnsExpectedLightType()
        {
            var task = LightingHandler.CreateLightAsync(new CreateLightRequest
                { LightType = "Spot", Position = new[] { 0f, 0f, 0f }, DryRun = true });

            MainThreadDispatcher.Tick();

            Assert.AreEqual("Spot", ((CreatedLightData)task.Result.Data).LightType);
        }

        // -----------------------------------------------------------------------
        // create_light — errors
        // -----------------------------------------------------------------------

        [Test]
        public void CreateLight_ReturnsError_OnInvalidLightType()
        {
            var task = LightingHandler.CreateLightAsync(new CreateLightRequest
                { LightType = "Laser", Position = new[] { 0f, 0f, 0f } });

            MainThreadDispatcher.Tick();

            Assert.IsFalse(task.Result.Success);
            Assert.AreEqual("INVALID_LIGHT_TYPE", task.Result.Error.Code);
        }

        [Test]
        public void CreateLight_ReturnsError_WhenParentNotFound()
        {
            var task = LightingHandler.CreateLightAsync(new CreateLightRequest
                { LightType = "Point", Position = new[] { 0f, 0f, 0f }, ParentPath = "GhostParent" });

            MainThreadDispatcher.Tick();

            Assert.IsFalse(task.Result.Success);
            Assert.AreEqual("PARENT_NOT_FOUND", task.Result.Error.Code);
        }

        // -----------------------------------------------------------------------
        // set_ambient_light — dry_run
        // -----------------------------------------------------------------------

        [Test]
        public void SetAmbientLight_DryRun_DoesNotChangeRenderSettings()
        {
            var originalMode = RenderSettings.ambientMode;

            var task = LightingHandler.SetAmbientLightAsync(new SetAmbientLightRequest
                { Mode = "Skybox", DryRun = true });

            MainThreadDispatcher.Tick();

            Assert.AreEqual(originalMode, RenderSettings.ambientMode);
            Assert.IsTrue(task.Result.Success);
        }

        // -----------------------------------------------------------------------
        // set_ambient_light — errors
        // -----------------------------------------------------------------------

        [Test]
        public void SetAmbientLight_ReturnsError_OnInvalidMode()
        {
            var task = LightingHandler.SetAmbientLightAsync(new SetAmbientLightRequest
                { Mode = "Rainbow" });

            MainThreadDispatcher.Tick();

            Assert.IsFalse(task.Result.Success);
            Assert.AreEqual("INVALID_MODE", task.Result.Error.Code);
        }

        // -----------------------------------------------------------------------
        // set_skybox — dry_run and errors
        // -----------------------------------------------------------------------

        [Test]
        public void SetSkybox_DryRun_DoesNotChangeSkybox()
        {
            var task = LightingHandler.SetSkyboxAsync(new SetSkyboxRequest
                { MaterialPath = "Assets/Does/Not/Exist.mat", DryRun = true });

            // dry_run validates path — but material doesn't exist, so expect error not dry success
            // Correct: dry_run should still validate the material exists
            MainThreadDispatcher.Tick();

            // Material doesn't exist → error even in dry_run
            Assert.IsFalse(task.Result.Success);
            Assert.AreEqual("MATERIAL_NOT_FOUND", task.Result.Error.Code);
            // Skybox unchanged
            Assert.AreEqual(_savedSkybox, RenderSettings.skybox);
        }

        [Test]
        public void SetSkybox_DryRun_WithNullPath_DoesNotClearSkybox()
        {
            var task = LightingHandler.SetSkyboxAsync(new SetSkyboxRequest
                { MaterialPath = null, DryRun = true });

            MainThreadDispatcher.Tick();

            Assert.IsTrue(task.Result.Success);
            Assert.AreEqual(_savedSkybox, RenderSettings.skybox);
        }

        [Test]
        public void SetSkybox_ReturnsError_WhenMaterialNotFound()
        {
            var task = LightingHandler.SetSkyboxAsync(new SetSkyboxRequest
                { MaterialPath = "Assets/Missing.mat" });

            MainThreadDispatcher.Tick();

            Assert.IsFalse(task.Result.Success);
            Assert.AreEqual("MATERIAL_NOT_FOUND", task.Result.Error.Code);
        }

        // -----------------------------------------------------------------------
        // set_fog — dry_run and errors
        // -----------------------------------------------------------------------

        [Test]
        public void SetFog_DryRun_DoesNotChangeFogSettings()
        {
            bool originalFog = RenderSettings.fog;

            var task = LightingHandler.SetFogAsync(new SetFogRequest
                { Enabled = !originalFog, DryRun = true });

            MainThreadDispatcher.Tick();

            Assert.AreEqual(originalFog, RenderSettings.fog);
            Assert.IsTrue(task.Result.Success);
        }

        [Test]
        public void SetFog_ReturnsError_OnInvalidMode()
        {
            var task = LightingHandler.SetFogAsync(new SetFogRequest
                { Enabled = true, Mode = "Volumetric" });

            MainThreadDispatcher.Tick();

            Assert.IsFalse(task.Result.Success);
            Assert.AreEqual("INVALID_FOG_MODE", task.Result.Error.Code);
        }

        [Test]
        public void SetFog_WithValidMode_UpdatesFogMode()
        {
            var task = LightingHandler.SetFogAsync(new SetFogRequest
                { Enabled = true, Mode = "Linear" });

            MainThreadDispatcher.Tick();

            Assert.IsTrue(task.Result.Success);
            Assert.AreEqual(FogMode.Linear, RenderSettings.fogMode);
            Assert.IsTrue(RenderSettings.fog);
        }
    }
}
