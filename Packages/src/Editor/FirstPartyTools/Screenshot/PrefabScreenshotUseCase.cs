using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Renders a prefab asset alone in a temporary preview scene and saves it as a PNG,
    /// leaving open scenes, Prefab Mode, and the asset untouched.
    /// </summary>
    internal sealed class PrefabScreenshotUseCase
    {
        private readonly IPrefabPreviewRenderer _renderer;
        private readonly Func<Vector2> _gameViewSizeReader;

        internal PrefabScreenshotUseCase(IPrefabPreviewRenderer renderer = null, Func<Vector2> gameViewSizeReader = null)
        {
            _renderer = renderer ?? new PrefabPreviewRenderer();
            _gameViewSizeReader = gameViewSizeReader ?? Handles.GetMainGameViewSize;
        }

        /// <summary>
        /// Completes synchronously; invalid input is rejected before a preview scene is created.
        /// </summary>
        internal Task<ScreenshotResponse> CaptureAsync(ScreenshotSchema request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            ScreenshotParameterValidator.ValidatePrefab(request);
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(request.PrefabPath);
            if (asset == null)
            {
                throw new UnityCliLoopToolParameterValidationException(
                    $"PrefabPath does not point to a prefab or model asset: {request.PrefabPath}");
            }

            Color background = PrefabBackgroundColorParser.Resolve(request.PrefabBackgroundColor);
            Vector2 rawSize = _gameViewSizeReader();
            Vector2Int gameViewSize = new(
                Mathf.Max(1, Mathf.RoundToInt(rawSize.x)),
                Mathf.Max(1, Mathf.RoundToInt(rawSize.y)));
            string correlationId = UnityCliLoopConstants.GenerateCorrelationId();
            VibeLogger.LogInfo(
                "screenshot_prefab_start",
                "Prefab screenshot started",
                new { request.PrefabPath, GameViewWidth = gameViewSize.x, GameViewHeight = gameViewSize.y },
                correlationId: correlationId);

            Scene scene = EditorSceneManager.NewPreviewScene();
            Texture2D texture = null;
            try
            {
                Camera camera = CreateCamera(scene, background);
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
                PrefabCaptureFrame frame = PrefabCaptureFraming.Arrange(instance, camera, gameViewSize);
                // Only an opaque background is composited, so only then is the extra probe render worth it.
                // The probe runs before the real render so the decision rests on the pipeline, never on the image content.
                bool pipelineClears = background.a >= 1f && _renderer.ClearsBackground(camera, frame.ImageSize);
                texture = _renderer.Render(camera, frame.ImageSize);
                CheckAndFillBackground(texture, background, request.PrefabPath, pipelineClears);
                if (!Mathf.Approximately(request.ResolutionScale, 1f))
                {
                    // ApplyResolutionScaling destroys the original only after it succeeds, so finally destroys exactly one texture.
                    texture = EditorWindowCaptureUtility.ApplyResolutionScaling(texture, request.ResolutionScale);
                }

                ScreenshotInfo info = SavePng(texture, asset.name, request);
                VibeLogger.LogInfo(
                    "screenshot_prefab_complete",
                    $"Rendered prefab ({info.Width}x{info.Height})",
                    new { Kind = frame.Kind.ToString(), info.Width, info.Height },
                    correlationId: correlationId);
                return Task.FromResult(new ScreenshotResponse
                {
                    Success = true,
                    ResolvedCaptureMode = UnityCliLoopConstants.SCREENSHOT_RESOLVED_CAPTURE_MODE_PREFAB,
                    Message = DescribeFrame(request.PrefabPath, frame),
                    Screenshots = new List<ScreenshotInfo> { info }
                });
            }
            finally
            {
                if (texture != null)
                {
                    UnityEngine.Object.DestroyImmediate(texture);
                }

                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static void CheckAndFillBackground(Texture2D texture, Color background, string prefabPath, bool pipelineClears)
        {
            Color32[] pixels = texture.GetPixels32();
            if (!PrefabImagePixels.HasDrawnPixel(pixels, background))
            {
                throw new UnityCliLoopToolParameterValidationException(
                    $"Nothing was drawn for {prefabPath}; the project's render pipeline did not draw this content. If the prefab is a single flat color, pass a different --prefab-background-color.");
            }

            // Some pipelines do not clear game cameras, so an opaque background is filled in afterwards;
            // a translucent background is left to the pipeline because compositing it again would double it.
            if (background.a < 1f)
            {
                return;
            }

            if (pipelineClears)
            {
                // The GPU already blended every pixel over the background; only alpha can be off,
                // because shaders without a separate alpha blend leave it below 255.
                PrefabImagePixels.MakeOpaque(pixels);
            }
            else
            {
                PrefabImagePixels.FillBackground(pixels, background, QualitySettings.activeColorSpace == ColorSpace.Linear);
            }

            texture.SetPixels32(pixels);
            texture.Apply();
        }

        private static Camera CreateCamera(Scene scene, Color background)
        {
            GameObject cameraObject = new("PrefabCaptureCamera");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.scene = scene;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = background;
            return camera;
        }

        private static ScreenshotInfo SavePng(Texture2D texture, string assetName, ScreenshotSchema request)
        {
            string outputDirectory = ScreenshotFileWriter.EnsureOutputDirectoryExists(request.OutputDirectory);
            string fileName = $"Prefab_{ScreenshotFileWriter.SanitizeFileName(assetName)}_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png";
            string path = Path.Combine(outputDirectory, fileName);
            ScreenshotFileWriter.SaveTextureAsPng(texture, path);
            // Only the package default folder is pruned; files in a user-specified OutputDirectory are never deleted.
            if (string.IsNullOrEmpty(request.OutputDirectory))
            {
                OutputFileRetention.DeleteOldestBeyondLimit(outputDirectory, "*.png");
            }

            return new ScreenshotInfo
            {
                ImagePath = path,
                FileSizeBytes = new FileInfo(path).Length,
                Width = texture.width,
                Height = texture.height,
                ResolutionScale = request.ResolutionScale,
                ImageCoordinateSystem = UnityCliLoopConstants.COORDINATE_SYSTEM_TOP_LEFT_PREFAB_IMAGE,
                ScreenshotToInputFormula = UnityCliLoopConstants.SCREENSHOT_PREFAB_TO_INPUT_FORMULA_UNAVAILABLE
            };
        }

        private static string DescribeFrame(string path, PrefabCaptureFrame frame)
        {
            return frame.Kind switch
            {
                PrefabCaptureKind.UiPart => $"Rendered {path} as a UI part, sized to the visible area of its RectTransforms (masks and a World Space root Canvas cut off what they hide).",
                PrefabCaptureKind.ScreenUi => $"Rendered {path} as screen UI at the Game View size.",
                PrefabCaptureKind.Object3D => $"Rendered {path} as a 3D object framed from the front-left at the Game View size.",
                PrefabCaptureKind.Flat2D => $"Rendered {path} as a flat object framed straight on at the Game View size.",
                _ => throw new ArgumentOutOfRangeException(nameof(frame), frame.Kind, "Unknown prefab capture kind")
            };
        }
    }
}
