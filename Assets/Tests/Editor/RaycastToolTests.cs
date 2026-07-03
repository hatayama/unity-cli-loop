#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace io.github.hatayama.uLoopMCP.Tests.Editor
{
    public class RaycastToolTests
    {
        private GameObject? _cameraObject;
        private GameObject? _cubeObject;
        private bool _originalAutoSyncTransforms;
        private readonly List<GameObject> _retaggedMainCameraObjects = new List<GameObject>();

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

            _retaggedMainCameraObjects.Clear();
            _cubeObject = null;
            _cameraObject = null;
        }

        [Test]
        public async Task ExecuteAsync_WhenCoordinateIntersectsCollider_ShouldReturnHitAndConversionMetadata()
        {
            CreateRaycastScene();
            Vector2 gameViewSize = GameViewCoordinateUtility.GetMainGameViewSize();
            Vector2 inputPosition = new Vector2(gameViewSize.x / 2f, gameViewSize.y / 2f);

            RaycastResponse response = await ExecuteRaycast(inputPosition);

            Assert.That(response.Success, Is.True);
            Assert.That(response.Hit, Is.True);
            Assert.That(response.HitGameObjectName, Is.EqualTo("RaycastToolTestsCube"));
            Assert.That(response.InputCoordinateSystem, Is.EqualTo(McpConstants.COORDINATE_SYSTEM_TOP_LEFT_GAME_VIEW));
            Assert.That(response.UnityCoordinateSystem, Is.EqualTo(McpConstants.COORDINATE_SYSTEM_BOTTOM_LEFT_GAME_VIEW));
            Assert.That(response.CoordinateConversionFormula, Is.EqualTo(McpConstants.COORDINATE_CONVERSION_FORMULA_GAME_VIEW_INPUT_TO_UNITY));
            Assert.That(response.InputPositionX, Is.EqualTo(inputPosition.x));
            Assert.That(response.InputPositionY, Is.EqualTo(inputPosition.y));
            Assert.That(response.InjectedUnityPositionX, Is.EqualTo(inputPosition.x));
            Assert.That(response.InjectedUnityPositionY, Is.EqualTo(gameViewSize.y - inputPosition.y));
        }

        [Test]
        public async Task ExecuteAsync_WhenCoordinateMissesCollider_ShouldReturnNoHit()
        {
            CreateRaycastScene();
            Vector2 inputPosition = new Vector2(0f, 0f);

            RaycastResponse response = await ExecuteRaycast(inputPosition);

            Assert.That(response.Success, Is.True);
            Assert.That(response.Hit, Is.False);
            Assert.That(response.HitGameObjectName, Is.Null);
        }

        [Test]
        public async Task ExecuteAsync_WhenCameraIsMissing_ShouldReturnConversionMetadata()
        {
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
            Assert.That(response.CoordinateConversionFormula, Is.EqualTo(McpConstants.COORDINATE_CONVERSION_FORMULA_GAME_VIEW_INPUT_TO_UNITY));
        }

        [Test]
        public async Task ExecuteAsync_WhenColliderLayerIsHiddenByCamera_ShouldReturnNoHit()
        {
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

            BaseToolResponse baseResponse = await tool.ExecuteAsync(parameters);
            return (RaycastResponse)baseResponse;
        }
    }
}
