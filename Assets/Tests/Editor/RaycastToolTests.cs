#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies Raycast Tool behavior.
    /// </summary>
    public class RaycastToolTests
    {
        private GameObject? _cameraObject;
        private GameObject? _cubeObject;
        private bool _originalAutoSyncTransforms;
        private readonly List<GameObject> _retaggedMainCameraObjects = new List<GameObject>();
        private readonly List<Collider> _disabledAmbientColliders = new List<Collider>();

        [SetUp]
        public void SetUp()
        {
            _originalAutoSyncTransforms = Physics.autoSyncTransforms;
        }

        [TearDown]
        public void TearDown()
        {
            Physics.autoSyncTransforms = _originalAutoSyncTransforms;

            if (_cubeObject != null)
            {
                Object.DestroyImmediate(_cubeObject);
            }

            if (_cameraObject != null)
            {
                Object.DestroyImmediate(_cameraObject);
            }

            foreach (GameObject retaggedObject in _retaggedMainCameraObjects)
            {
                if (retaggedObject != null)
                {
                    retaggedObject.tag = "MainCamera";
                }
            }

            foreach (Collider ambientCollider in _disabledAmbientColliders)
            {
                if (ambientCollider != null)
                {
                    ambientCollider.enabled = true;
                }
            }

            _retaggedMainCameraObjects.Clear();
            _disabledAmbientColliders.Clear();
            _cubeObject = null;
            _cameraObject = null;
        }

        [Test]
        public async Task ExecuteAsync_WhenCoordinateIntersectsCollider_ShouldReturnHitAndConversionMetadata()
        {
            // Tests that a Game View coordinate landing on a collider reports the hit and coordinate conversion metadata.
            CreateRaycastScene();
            Vector2 gameViewSize = GameViewCoordinateUtility.GetMainGameViewSize();
            Vector2 inputPosition = new Vector2(gameViewSize.x / 2f, gameViewSize.y / 2f);

            RaycastResponse response = await ExecuteRaycast(inputPosition);

            Assert.That(response.Success, Is.True);
            Assert.That(response.Hit, Is.True);
            Assert.That(response.HitGameObjectName, Is.EqualTo("RaycastToolTestsCube"));
            Assert.That(response.CameraName, Is.EqualTo("RaycastToolTestsCamera"));
            Assert.That(response.CameraPath, Does.Contain("RaycastToolTestsCamera"));
            Assert.That(response.InputCoordinateSystem, Is.EqualTo(UnityCliLoopConstants.COORDINATE_SYSTEM_TOP_LEFT_GAME_VIEW));
            Assert.That(response.UnityCoordinateSystem, Is.EqualTo(UnityCliLoopConstants.COORDINATE_SYSTEM_BOTTOM_LEFT_GAME_VIEW));
            Assert.That(response.CoordinateConversionFormula, Is.EqualTo(UnityCliLoopConstants.COORDINATE_CONVERSION_FORMULA_GAME_VIEW_INPUT_TO_UNITY));
            Assert.That(response.InputPositionX, Is.EqualTo(inputPosition.x));
            Assert.That(response.InputPositionY, Is.EqualTo(inputPosition.y));
            Assert.That(response.InjectedUnityPositionX, Is.EqualTo(inputPosition.x));
            Assert.That(response.InjectedUnityPositionY, Is.EqualTo(gameViewSize.y - inputPosition.y));
        }

        [Test]
        public async Task ExecuteAsync_WhenCoordinateMissesCollider_ShouldReturnNoHit()
        {
            // Tests that a Game View coordinate that misses every collider still succeeds with Hit=false.
            CreateRaycastScene();
            Vector2 inputPosition = new Vector2(0f, 0f);

            RaycastResponse response = await ExecuteRaycast(inputPosition);

            Assert.That(response.Success, Is.True);
            Assert.That(response.Hit, Is.False);
            Assert.That(response.HitGameObjectName, Is.Null);
        }

        [Test]
        public async Task ExecuteAsync_WhenCoordinateMissesCollider_ShouldStillReportResolvedCamera()
        {
            // Tests that the resolved Camera.main is reported even on a "No physics hit" response, so an
            // agent can tell which camera the ray actually came from instead of assuming Camera.main.
            CreateRaycastScene();
            Vector2 inputPosition = new Vector2(0f, 0f);

            RaycastResponse response = await ExecuteRaycast(inputPosition);

            Assert.That(response.Hit, Is.False);
            Assert.That(response.CameraName, Is.EqualTo("RaycastToolTestsCamera"));
            Assert.That(response.CameraPath, Does.Contain("RaycastToolTestsCamera"));
        }

        [Test]
        public async Task ExecuteAsync_WhenCoordinateIntersectsCollider_EmitsRaycastExecutedVibeLog()
        {
            // Verifies a raycast records a raycast_executed observability event with camera/hit context.
            CreateRaycastScene();
            Vector2 gameViewSize = GameViewCoordinateUtility.GetMainGameViewSize();
            Vector2 inputPosition = new Vector2(gameViewSize.x / 2f, gameViewSize.y / 2f);
            VibeLogger.ClearMemoryLogs();

            await ExecuteRaycast(inputPosition);

            string logs = VibeLogger.GetLogsForAi("raycast_executed");
            Assert.That(logs, Does.Contain("raycast_executed"));
            Assert.That(logs, Does.Contain("\"CameraName\": \"RaycastToolTestsCamera\""));
            Assert.That(logs, Does.Contain("\"Hit\": true"));
            Assert.That(logs, Does.Contain("\"HitGameObjectName\": \"RaycastToolTestsCube\""));
        }

        [Test]
        public async Task ExecuteAsync_WhenCameraIsMissing_ShouldReturnConversionMetadata()
        {
            // Tests that a missing Camera.main fails the raycast but still returns coordinate conversion metadata.
            RetagExistingMainCameras();
            Vector2 gameViewSize = GameViewCoordinateUtility.GetMainGameViewSize();
            Vector2 inputPosition = new Vector2(gameViewSize.x / 2f, gameViewSize.y / 2f);

            RaycastResponse response = await ExecuteRaycast(inputPosition);

            Assert.That(response.Success, Is.False);
            Assert.That(response.Message, Does.Contain("Camera.main"));
            Assert.That(response.InputPositionX, Is.EqualTo(inputPosition.x));
            Assert.That(response.InputPositionY, Is.EqualTo(inputPosition.y));
            Assert.That(response.InjectedUnityPositionX, Is.EqualTo(inputPosition.x));
            Assert.That(response.InjectedUnityPositionY, Is.EqualTo(gameViewSize.y - inputPosition.y));
            Assert.That(response.CoordinateConversionFormula, Is.EqualTo(UnityCliLoopConstants.COORDINATE_CONVERSION_FORMULA_GAME_VIEW_INPUT_TO_UNITY));
        }

        [Test]
        public async Task ExecuteAsync_WhenColliderLayerIsHiddenByCamera_ShouldReturnNoHit()
        {
            // Tests that a collider on a layer excluded from the camera's culling mask is not hit.
            CreateRaycastScene();
            if (_cameraObject == null)
            {
                Assert.Fail("Camera should be created.");
                return;
            }

            Camera camera = _cameraObject.GetComponent<Camera>();
            camera.cullingMask = 0;
            Vector2 gameViewSize = GameViewCoordinateUtility.GetMainGameViewSize();
            Vector2 inputPosition = new Vector2(gameViewSize.x / 2f, gameViewSize.y / 2f);

            RaycastResponse response = await ExecuteRaycast(inputPosition);

            Assert.That(response.Success, Is.True);
            Assert.That(response.Hit, Is.False);
        }

        [Test]
        public async Task ExecuteAsync_WhenAutoSyncTransformsIsDisabled_ShouldRaycastAgainstLatestTransform()
        {
            // Tests that the raycast syncs physics transforms itself even when Physics.autoSyncTransforms is disabled.
            Physics.autoSyncTransforms = false;
            CreateRaycastScene();
            if (_cubeObject == null)
            {
                Assert.Fail("Cube should be created.");
                return;
            }

            _cubeObject.transform.position = new Vector3(100f, 0f, 0f);
            Vector2 gameViewSize = GameViewCoordinateUtility.GetMainGameViewSize();
            Vector2 inputPosition = new Vector2(gameViewSize.x / 2f, gameViewSize.y / 2f);

            RaycastResponse response = await ExecuteRaycast(inputPosition);

            Assert.That(response.Success, Is.True);
            Assert.That(response.Hit, Is.False);
        }

        private void CreateRaycastScene()
        {
            RetagExistingMainCameras();
            DisableAmbientColliders();

            _cameraObject = new GameObject("RaycastToolTestsCamera");
            Camera camera = _cameraObject.AddComponent<Camera>();
            _cameraObject.tag = "MainCamera";
            _cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            _cameraObject.transform.rotation = Quaternion.identity;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;

            _cubeObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _cubeObject.name = "RaycastToolTestsCube";
            _cubeObject.transform.position = Vector3.zero;
        }

        // Other fixtures can leave colliders in the shared scene between test runs;
        // this test asserts on the nearest raycast hit, so ambient colliders must not compete with our own.
        private void DisableAmbientColliders()
        {
            Collider[] colliders = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None);
            foreach (Collider ambientCollider in colliders)
            {
                if (!ambientCollider.enabled)
                {
                    continue;
                }

                _disabledAmbientColliders.Add(ambientCollider);
                ambientCollider.enabled = false;
            }
        }

        private void RetagExistingMainCameras()
        {
            Camera[] cameras = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
            foreach (Camera camera in cameras)
            {
                if (!camera.CompareTag("MainCamera"))
                {
                    continue;
                }

                _retaggedMainCameraObjects.Add(camera.gameObject);
                camera.gameObject.tag = "Untagged";
            }
        }

        private static async Task<RaycastResponse> ExecuteRaycast(Vector2 inputPosition)
        {
            RaycastTool tool = new RaycastTool();
            JObject parameters = new JObject
            {
                ["x"] = inputPosition.x,
                ["y"] = inputPosition.y
            };

            UnityCliLoopToolResponse baseResponse = await tool.ExecuteAsync(parameters, CancellationToken.None);
            return (RaycastResponse)baseResponse;
        }
    }
}
