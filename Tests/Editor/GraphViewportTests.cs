using NUnit.Framework;
using UnityEngine;

namespace GitVersionControl.Tests
{
    public sealed class GraphViewportTests
    {
        private const float Tolerance = 1e-3f;

        [TestCase(1f, 0f, 0f, 10f, 20f)]
        [TestCase(0.5f, 100f, -50f, 300f, 40f)]
        [TestCase(2f, -30f, 70f, -12.5f, 900f)]
        public void Test_ToCanvasAndToWorld_RoundTrip(float zoom, float panX, float panY, float worldX, float worldY)
        {
            GraphViewport viewport = new(zoom, new Vector2(panX, panY));
            Vector2 world = new(worldX, worldY);

            Vector2 canvas = viewport.ToCanvas(world);

            AssertVector(world * zoom - new Vector2(panX, panY), canvas);
            AssertVector(world, viewport.ToWorld(canvas));
        }

        [TestCase(1f, 2f)]
        [TestCase(1f, 0.5f)]
        [TestCase(0.75f, 1.6f)]
        public void Test_ZoomAround_KeepsWorldPointUnderCursor(float startZoom, float newZoom)
        {
            GraphViewport viewport = new(startZoom, new Vector2(120f, 40f));
            Vector2 mouse = new(250f, 180f);
            Vector2 worldBefore = viewport.ToWorld(mouse);

            viewport.ZoomAround(mouse, newZoom);

            Assert.AreEqual(newZoom, viewport.Zoom, Tolerance);
            AssertVector(worldBefore, viewport.ToWorld(mouse));
            AssertVector(mouse, viewport.ToCanvas(worldBefore));
        }

        [TestCase(10f, GraphViewport.MaxZoom)]
        [TestCase(0.01f, GraphViewport.MinZoom)]
        public void Test_ZoomAround_ClampsZoomAndStillKeepsPoint(float requested, float expected)
        {
            GraphViewport viewport = new(1f, Vector2.zero);
            Vector2 mouse = new(400f, 300f);
            Vector2 worldBefore = viewport.ToWorld(mouse);

            viewport.ZoomAround(mouse, requested);

            Assert.AreEqual(expected, viewport.Zoom, Tolerance);
            AssertVector(worldBefore, viewport.ToWorld(mouse));
        }

        [TestCase(5f, GraphViewport.MaxZoom)]
        [TestCase(0f, GraphViewport.MinZoom)]
        [TestCase(1.5f, 1.5f)]
        public void Test_Constructor_ClampsZoom(float requested, float expected)
        {
            Assert.AreEqual(expected, new GraphViewport(requested, Vector2.zero).Zoom, Tolerance);
        }

        [Test]
        public void Test_PanBy_MovesContentWithDrag()
        {
            GraphViewport viewport = new(1f, new Vector2(100f, 100f));
            Vector2 world = new(300f, 200f);
            Vector2 before = viewport.ToCanvas(world);

            viewport.PanBy(new Vector2(15f, -5f));

            AssertVector(new Vector2(85f, 105f), viewport.Pan);
            AssertVector(before + new Vector2(15f, -5f), viewport.ToCanvas(world));
        }

        [Test]
        public void Test_Focus_PlacesWorldPointAtRatio()
        {
            GraphViewport viewport = new(2f, Vector2.zero);
            Vector2 world = new(1000f, 500f);
            Vector2 canvasSize = new(800f, 600f);

            viewport.Focus(world, canvasSize);

            AssertVector(new Vector2(800f * 0.75f, 600f * 0.35f), viewport.ToCanvas(world));
        }

        [Test]
        public void Test_Focus_CustomRatio()
        {
            GraphViewport viewport = new(1f, Vector2.zero);

            viewport.Focus(new Vector2(500f, 400f), new Vector2(200f, 100f), 0.5f, 0.5f);

            AssertVector(new Vector2(100f, 50f), viewport.ToCanvas(new Vector2(500f, 400f)));
        }

        [Test]
        public void Test_Focus_DoesNotPanAboveTop()
        {
            GraphViewport viewport = new(1f, new Vector2(0f, 300f));

            viewport.Focus(new Vector2(1000f, 10f), new Vector2(800f, 600f));

            Assert.AreEqual(0f, viewport.Pan.y, Tolerance);
            Assert.AreEqual(1000f - 600f, viewport.Pan.x, Tolerance);
        }

        private static void AssertVector(Vector2 expected, Vector2 actual)
        {
            Assert.AreEqual(expected.x, actual.x, Tolerance, $"x: expected {expected}, actual {actual}");
            Assert.AreEqual(expected.y, actual.y, Tolerance, $"y: expected {expected}, actual {actual}");
        }
    }
}
