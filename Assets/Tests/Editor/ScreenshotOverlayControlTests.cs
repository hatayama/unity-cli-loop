using System;
using System.Collections.Generic;
using System.Threading;

using NUnit.Framework;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

using Object = UnityEngine.Object;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies when the screenshot overlay helper creates an annotation overlay, and that restoring the input
    /// overlay and destroying the annotation overlay run inline on the editor context but are posted from any
    /// other context. A recording synchronization context stands in for the editor context; the previous
    /// context is put back after each test.
    /// </summary>
    public sealed class ScreenshotOverlayControlTests
    {
        private readonly List<GameObject> _createdObjects = new List<GameObject>();
        private SynchronizationContext _previousContext;

        [SetUp]
        public void SetUp()
        {
            _previousContext = SynchronizationContext.Current;
        }

        [TearDown]
        public void TearDown()
        {
            SynchronizationContext.SetSynchronizationContext(_previousContext);
            foreach (GameObject createdObject in _createdObjects)
            {
                if (createdObject != null)
                {
                    Object.DestroyImmediate(createdObject);
                }
            }

            _createdObjects.Clear();
        }

        /// <summary>
        /// Verifies no overlay is created when neither element nor raycast annotation was requested.
        /// </summary>
        [Test]
        public void CreateAnnotationOverlayIfNeeded_WithoutAnnotations_ReturnsNull()
        {
            GameObject overlay = ScreenshotOverlayControl.CreateAnnotationOverlayIfNeeded(
                new ScreenshotSchema(),
                CreateCapture());

            Assert.That(overlay, Is.Null);
        }

        /// <summary>
        /// Verifies an overlay is created when element annotation is requested.
        /// </summary>
        [Test]
        public void CreateAnnotationOverlayIfNeeded_WithElementAnnotation_CreatesTheOverlay()
        {
            GameObject overlay = Track(ScreenshotOverlayControl.CreateAnnotationOverlayIfNeeded(
                new ScreenshotSchema { AnnotateElements = true },
                CreateCapture()));

            Assert.That(overlay, Is.Not.Null);
            Assert.That(overlay.name, Is.EqualTo("__UIAnnotation__"));
        }

        /// <summary>
        /// Verifies an overlay is created when only raycast annotation is requested.
        /// </summary>
        [Test]
        public void CreateAnnotationOverlayIfNeeded_WithRaycastAnnotation_CreatesTheOverlay()
        {
            GameObject overlay = Track(ScreenshotOverlayControl.CreateAnnotationOverlayIfNeeded(
                new ScreenshotSchema { AnnotateRaycastGrid = true },
                new ScreenshotUseCase.RenderingAnnotationCapture()));

            Assert.That(overlay, Is.Not.Null);
        }

        /// <summary>
        /// Verifies an overlay that was not active before capture stays hidden.
        /// </summary>
        [Test]
        public void RestoreInputVisualizationOverlay_WhenItWasNotActive_LeavesItHidden()
        {
            RecordingSynchronizationContext editorContext = UseAsCurrent(new RecordingSynchronizationContext());
            GameObject overlay = CreateHiddenOverlay();

            ScreenshotOverlayControl.RestoreInputVisualizationOverlay(overlay, false, editorContext);

            Assert.That(overlay.activeSelf, Is.False);
            Assert.That(overlay.GetComponent<Canvas>().enabled, Is.False);
        }

        /// <summary>
        /// Verifies a missing overlay is ignored.
        /// </summary>
        [Test]
        public void RestoreInputVisualizationOverlay_WithoutAnOverlay_DoesNothing()
        {
            RecordingSynchronizationContext editorContext = new RecordingSynchronizationContext();

            Assert.DoesNotThrow(() => ScreenshotOverlayControl.RestoreInputVisualizationOverlay(null, true, editorContext));
            Assert.That(editorContext.PostedCallbacks, Is.Empty);
        }

        /// <summary>
        /// Verifies the overlay and its canvas are re-enabled inline when already on the editor context.
        /// </summary>
        [Test]
        public void RestoreInputVisualizationOverlay_OnTheEditorContext_RestoresInline()
        {
            RecordingSynchronizationContext editorContext = UseAsCurrent(new RecordingSynchronizationContext());
            GameObject overlay = CreateHiddenOverlay();

            ScreenshotOverlayControl.RestoreInputVisualizationOverlay(overlay, true, editorContext);

            Assert.That(editorContext.PostedCallbacks, Is.Empty);
            Assert.That(overlay.activeSelf, Is.True);
            Assert.That(overlay.GetComponent<Canvas>().enabled, Is.True);
        }

        /// <summary>
        /// Verifies the restore is posted to the editor context from another context and runs when that callback runs.
        /// </summary>
        [Test]
        public void RestoreInputVisualizationOverlay_FromAnotherContext_PostsTheRestore()
        {
            UseAsCurrent(new RecordingSynchronizationContext());
            RecordingSynchronizationContext editorContext = new RecordingSynchronizationContext();
            GameObject overlay = CreateHiddenOverlay();

            ScreenshotOverlayControl.RestoreInputVisualizationOverlay(overlay, true, editorContext);

            Assert.That(overlay.activeSelf, Is.False);
            Assert.That(editorContext.PostedCallbacks.Count, Is.EqualTo(1));
            editorContext.RunPosted();
            Assert.That(overlay.activeSelf, Is.True);
            Assert.That(overlay.GetComponent<Canvas>().enabled, Is.True);
        }

        /// <summary>
        /// Verifies a posted restore does nothing when the overlay was destroyed before the callback ran.
        /// </summary>
        [Test]
        public void RestoreInputVisualizationOverlay_WhenTheOverlayIsDestroyedBeforeThePost_DoesNothing()
        {
            UseAsCurrent(new RecordingSynchronizationContext());
            RecordingSynchronizationContext editorContext = new RecordingSynchronizationContext();
            GameObject overlay = CreateHiddenOverlay();
            ScreenshotOverlayControl.RestoreInputVisualizationOverlay(overlay, true, editorContext);
            Object.DestroyImmediate(overlay);

            Assert.DoesNotThrow(() => editorContext.RunPosted());
        }

        /// <summary>
        /// Verifies a missing annotation overlay is ignored.
        /// </summary>
        [Test]
        public void DestroyAnnotationOverlay_WithoutAnOverlay_DoesNothing()
        {
            RecordingSynchronizationContext editorContext = new RecordingSynchronizationContext();

            ScreenshotOverlayControl.DestroyAnnotationOverlay(null, editorContext);

            Assert.That(editorContext.PostedCallbacks, Is.Empty);
        }

        /// <summary>
        /// Verifies the annotation overlay is destroyed inline when already on the editor context.
        /// </summary>
        [Test]
        public void DestroyAnnotationOverlay_OnTheEditorContext_DestroysInline()
        {
            RecordingSynchronizationContext editorContext = UseAsCurrent(new RecordingSynchronizationContext());
            GameObject overlay = Track(new GameObject("ScreenshotOverlayControlTests_Annotation"));

            ScreenshotOverlayControl.DestroyAnnotationOverlay(overlay, editorContext);

            Assert.That(overlay == null, Is.True);
            Assert.That(editorContext.PostedCallbacks, Is.Empty);
        }

        /// <summary>
        /// Verifies the destroy is posted to the editor context from another context.
        /// </summary>
        [Test]
        public void DestroyAnnotationOverlay_FromAnotherContext_PostsTheDestroy()
        {
            UseAsCurrent(new RecordingSynchronizationContext());
            RecordingSynchronizationContext editorContext = new RecordingSynchronizationContext();
            GameObject overlay = Track(new GameObject("ScreenshotOverlayControlTests_Annotation"));

            ScreenshotOverlayControl.DestroyAnnotationOverlay(overlay, editorContext);

            Assert.That(overlay != null, Is.True);
            editorContext.RunPosted();
            Assert.That(overlay == null, Is.True);
        }

        private static ScreenshotUseCase.RenderingAnnotationCapture CreateCapture()
        {
            ScreenshotUseCase.RenderingAnnotationCapture capture = new ScreenshotUseCase.RenderingAnnotationCapture();
            capture.AnnotatedElements.Add(new UIElementInfo
            {
                Label = "A",
                Type = "Button",
                Interaction = "Click",
                SimX = 20f,
                SimY = 20f,
                BoundsMinX = 10f,
                BoundsMinY = 10f,
                BoundsMaxX = 30f,
                BoundsMaxY = 30f
            });
            return capture;
        }

        private GameObject CreateHiddenOverlay()
        {
            GameObject overlay = Track(new GameObject("ScreenshotOverlayControlTests_InputOverlay"));
            overlay.AddComponent<Canvas>().enabled = false;
            overlay.SetActive(false);
            return overlay;
        }

        private GameObject Track(GameObject gameObject)
        {
            _createdObjects.Add(gameObject);
            return gameObject;
        }

        private static RecordingSynchronizationContext UseAsCurrent(RecordingSynchronizationContext context)
        {
            SynchronizationContext.SetSynchronizationContext(context);
            return context;
        }

        // Records posted callbacks so a test decides when they run; nothing runs on another thread.
        private sealed class RecordingSynchronizationContext : SynchronizationContext
        {
            public List<Action> PostedCallbacks { get; } = new List<Action>();

            public override void Post(SendOrPostCallback callback, object state)
            {
                PostedCallbacks.Add(() => callback(state));
            }

            public void RunPosted()
            {
                foreach (Action callback in PostedCallbacks)
                {
                    callback();
                }
            }
        }
    }
}
