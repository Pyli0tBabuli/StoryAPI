using System;
using System.Collections.Generic;

using RenPyParser.Images;
using UnityEngine;

using StoryAPI.Game;

namespace StoryAPI.Runtime
{
    /// <summary>
    /// Moves a picture along a transform, under the mod's own control.
    ///
    /// The game has its own animation runner, but it does not run inside a mod label, so
    /// a named transform there would either do nothing or leave the script waiting for a
    /// picture that never finishes moving. This is a runner of the mod's own: it keeps the
    /// picture moving and tells the script when the move is over.
    /// </summary>
    internal sealed class AtlAnimation
    {
        /// <summary>The picture being moved.</summary>
        public ActiveImage Target { get; }

        /// <summary>
        /// The object that is actually moved.
        ///
        /// The game lays a picture out from its own display slot, but that work belongs to
        /// the animation runner the mod does not have, so nothing would read what was
        /// written there. The picture is therefore moved directly.
        /// </summary>
        public Transform Renderer { get; }

        /// <summary>The picture's own size in pixels.</summary>
        public int TextureWidth { get; }
        public int TextureHeight { get; }

        /// <summary>Where it starts.</summary>
        public GameBridge.TransformState Start { get; }

        /// <summary>Where it ends.</summary>
        public GameBridge.TransformState End { get; }

        public float Duration { get; }
        public string EaseName { get; }
        public float Elapsed { get; private set; }

        public bool Finished { get; private set; }
        public AtlAnimation(ActiveImage target, Transform renderer, int textureWidth,
            int textureHeight, GameBridge.TransformState start, GameBridge.TransformState end,
            float duration, string ease)
        {
            Target = target;
            Renderer = renderer;
            TextureWidth = textureWidth;
            TextureHeight = textureHeight;
            Start = start;
            End = end;
            Duration = duration;
            EaseName = ease;
        }

        /// <summary>Move the picture on a little.</summary>
        public void Advance(float delta)
        {
            if (Finished) return;
            Elapsed += delta;
            float t = Duration > 0f ? Elapsed / Duration : 1f;
            if (t >= 1f)
            {
                t = 1f;
                Finished = true;
            }

            Apply(Lerp(Start, End, EaseShape(t)));
        }

        /// <summary>
        /// Put the picture where a state says.
        ///
        /// A state that says nothing about a property leaves that property alone, so a
        /// transform that only moves a picture does not also resize it.
        /// </summary>
        public void Apply(GameBridge.TransformState state)
        {
            if (Renderer == null || state == null) return;

            float zoomX = state.HasZoom ? state.ZoomX : 1f;
            float zoomY = state.HasZoom ? state.ZoomY : 1f;

            float width = TextureWidth * zoomX;
            float height = TextureHeight * zoomY;

            float left, top;
            if (state.HasXAlign)
            {
                // Alignment places the picture inside the room that is left over, not
                // from the edge of the screen: at 0 its corner is at the screen's corner,
                // at 0.5 it is centred, at 1 its far edge reaches the screen's edge.
                left = state.AlignX * (ArtInjector.ScreenWidth - width);
            }
            else if (state.HasXPos)
            {
                left = state.PosX;
            }
            else
            {
                left = ArtInjector.ScreenWidth * 0.5f - width * 0.5f;
            }

            if (state.HasYAlign)
            {
                top = state.AlignY * (ArtInjector.ScreenHeight - height);
            }
            else if (state.HasYPos)
            {
                top = state.PosY;
            }
            else
            {
                top = ArtInjector.ScreenHeight * 0.5f - height * 0.5f;
            }

            if (state.HasXOffset) left += state.OffsetX;
            if (state.HasYOffset) top += state.OffsetY;

            Renderer.localScale = new Vector3(zoomX, zoomY, 1f);
            Renderer.localPosition = new Vector3(
                left + width * 0.5f - ArtInjector.ScreenWidth * 0.5f,
                -(top + height * 0.5f - ArtInjector.ScreenHeight * 0.5f),
                0f);

            if (state.HasAlpha)
            {
                var renderer = Renderer.GetComponent<SpriteRenderer>();
                if (renderer != null)
                {
                    var colour = renderer.color;
                    colour.a = state.Alpha;
                    renderer.color = colour;
                }
            }
        }

        /// <summary>
        /// The state a picture is in part way along a move.
        ///
        /// Every property is eased from its value at the start to its value at the end.
        /// A property the end does not mention keeps the value it had at the start, which
        /// is how a transform that only moves a picture leaves its size alone.
        /// </summary>
        public static GameBridge.TransformState Lerp(GameBridge.TransformState from,
            GameBridge.TransformState to, float t)
        {
            var result = new GameBridge.TransformState();

            if (from == null && to == null) return result;

            var a = from ?? new GameBridge.TransformState();
            var b = to ?? new GameBridge.TransformState();

            if (a.HasXAlign || b.HasXAlign)
            {
                result.HasXAlign = true;
                result.AlignX = Ease(a.HasXAlign ? a.AlignX : b.AlignX,
                                     b.HasXAlign ? b.AlignX : a.AlignX, t);
            }
            if (a.HasYAlign || b.HasYAlign)
            {
                result.HasYAlign = true;
                result.AlignY = Ease(a.HasYAlign ? a.AlignY : b.AlignY,
                                     b.HasYAlign ? b.AlignY : a.AlignY, t);
            }

            if (a.HasXPos || b.HasXPos)
            {
                result.HasXPos = true;
                result.PosX = Ease(a.HasXPos ? a.PosX : b.PosX,
                                   b.HasXPos ? b.PosX : a.PosX, t);
            }
            if (a.HasYPos || b.HasYPos)
            {
                result.HasYPos = true;
                result.PosY = Ease(a.HasYPos ? a.PosY : b.PosY,
                                   b.HasYPos ? b.PosY : a.PosY, t);
            }

            if (a.HasZoom || b.HasZoom)
            {
                result.HasZoom = true;
                result.ZoomX = Ease(a.HasZoom ? a.ZoomX : b.ZoomX,
                                    b.HasZoom ? b.ZoomX : a.ZoomX, t);
                result.ZoomY = Ease(a.HasZoom ? a.ZoomY : b.ZoomY,
                                    b.HasZoom ? b.ZoomY : a.ZoomY, t);
            }

            if (a.HasAlpha || b.HasAlpha)
            {
                result.HasAlpha = true;
                result.Alpha = Ease(a.HasAlpha ? a.Alpha : b.Alpha,
                                    b.HasAlpha ? b.Alpha : a.Alpha, t);
            }

            if (a.HasRotate || b.HasRotate)
            {
                result.HasRotate = true;
                result.Rotate = Ease(a.HasRotate ? a.Rotate : b.Rotate,
                                     b.HasRotate ? b.Rotate : a.Rotate, t);
            }

            if (a.HasXOffset || b.HasXOffset)
            {
                result.HasXOffset = true;
                result.OffsetX = Ease(a.HasXOffset ? a.OffsetX : b.OffsetX,
                                      b.HasXOffset ? b.OffsetX : a.OffsetX, t);
            }
            if (a.HasYOffset || b.HasYOffset)
            {
                result.HasYOffset = true;
                result.OffsetY = Ease(a.HasYOffset ? a.OffsetY : b.OffsetY,
                                      b.HasYOffset ? b.OffsetY : a.OffsetY, t);
            }

            return result;
        }

        /// <summary>Move from one value to another as the move goes on.</summary>
        private static float Ease(float from, float to, float t)
        {
            return from + (to - from) * t;
        }

        /// <summary>
        /// The shape of a move.
        ///
        /// The game names many shapes; this follows the smooth one the game's own
        /// transformations mostly use, so a picture eases into place rather than sliding
        /// at a fixed rate.
        /// </summary>
        public static float EaseShape(float t)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            return t * t * (3f - 2f * t);
        }
    }

    /// <summary>One timed stage of a user-authored ATL transform.</summary>
    internal sealed class AtlScriptSegment
    {
        public GameBridge.TransformState Target { get; set; }
        public float Duration { get; set; }
        public string EaseName { get; set; }
    }

    /// <summary>Runs sequential ATL stages over a StoryAPI-owned image.</summary>
    internal sealed class AtlScriptAnimation
    {
        private readonly List<AtlScriptSegment> _segments;
        private GameBridge.TransformState _current;
        private int _segmentIndex;
        private float _elapsed;

        public string Container { get; }
        public string Layer { get; }
        public ActiveImage Target { get; }
        public bool Finished { get; private set; }

        public AtlScriptAnimation(string container, string layer, ActiveImage target,
            GameBridge.TransformState initial, IList<AtlScriptSegment> segments)
        {
            Container = container;
            Layer = string.IsNullOrEmpty(layer) ? "master" : layer;
            Target = target;
            _current = Copy(initial);
            _segments = new List<AtlScriptSegment>(segments);
        }

        public void Advance(float delta)
        {
            if (Finished) return;
            if (Target == null || Target.Object == null || !ArtInjector.Has(Container, Layer))
            {
                Finished = true;
                return;
            }

            float remaining = Mathf.Max(0f, delta);
            while (_segmentIndex < _segments.Count)
            {
                AtlScriptSegment segment = _segments[_segmentIndex];
                if (segment == null || segment.Target == null)
                {
                    _segmentIndex++;
                    _elapsed = 0f;
                    continue;
                }

                float duration = Mathf.Max(0f, segment.Duration);
                if (duration <= 0f)
                {
                    _current = Merge(_current, segment.Target);
                    ArtInjector.ApplyStaticTransform(Container, Layer, _current, false);
                    _segmentIndex++;
                    _elapsed = 0f;
                    continue;
                }

                float step = Mathf.Min(remaining, duration - _elapsed);
                _elapsed += step;
                remaining -= step;

                float t = Mathf.Clamp01(_elapsed / duration);
                var frame = Copy(_current);
                Interpolate(frame, _current, segment.Target, Warp(segment.EaseName, t));
                ArtInjector.ApplyStaticTransform(Container, Layer, frame, false);

                if (_elapsed < duration) return;

                _current = Merge(_current, segment.Target);
                _segmentIndex++;
                _elapsed = 0f;

                if (remaining <= 0f && _segmentIndex < _segments.Count)
                {
                    AtlScriptSegment next = _segments[_segmentIndex];
                    if (next != null && next.Duration > 0f) return;
                }
            }

            Finished = true;
        }

        private static float Warp(string easeName, float t)
        {
            switch ((easeName ?? "linear").ToLowerInvariant())
            {
                case "ease":
                    return 0.5f - Mathf.Cos(Mathf.PI * t) * 0.5f;
                case "easein":
                    return Mathf.Cos((1f - t) * Mathf.PI * 0.5f);
                case "easeout":
                    return 1f - Mathf.Cos(t * Mathf.PI * 0.5f);
                default:
                    return t;
            }
        }

        private static void Interpolate(GameBridge.TransformState frame,
            GameBridge.TransformState from, GameBridge.TransformState to, float t)
        {
            if (to.HasXAlign) { frame.HasXAlign = true; frame.AlignX = Mathf.Lerp(from.AlignX, to.AlignX, t); }
            if (to.HasYAlign) { frame.HasYAlign = true; frame.AlignY = Mathf.Lerp(from.AlignY, to.AlignY, t); }
            if (to.HasXPos) { frame.HasXPos = true; frame.PosX = Mathf.Lerp(from.PosX, to.PosX, t); }
            if (to.HasYPos) { frame.HasYPos = true; frame.PosY = Mathf.Lerp(from.PosY, to.PosY, t); }
            if (to.HasXOffset) { frame.HasXOffset = true; frame.OffsetX = Mathf.Lerp(from.OffsetX, to.OffsetX, t); }
            if (to.HasYOffset) { frame.HasYOffset = true; frame.OffsetY = Mathf.Lerp(from.OffsetY, to.OffsetY, t); }
            if (to.HasZoom)
            {
                frame.HasZoom = true;
                frame.ZoomX = Mathf.Lerp(from.ZoomX, to.ZoomX, t);
                frame.ZoomY = Mathf.Lerp(from.ZoomY, to.ZoomY, t);
            }
            if (to.HasAlpha) { frame.HasAlpha = true; frame.Alpha = Mathf.Lerp(from.Alpha, to.Alpha, t); }
            if (to.HasRotate) { frame.HasRotate = true; frame.Rotate = Mathf.Lerp(from.Rotate, to.Rotate, t); }
        }

        private static GameBridge.TransformState Merge(GameBridge.TransformState current,
            GameBridge.TransformState target)
        {
            var result = Copy(current);
            if (target.HasXAlign) { result.HasXAlign = true; result.AlignX = target.AlignX; }
            if (target.HasYAlign) { result.HasYAlign = true; result.AlignY = target.AlignY; }
            if (target.HasXPos) { result.HasXPos = true; result.PosX = target.PosX; }
            if (target.HasYPos) { result.HasYPos = true; result.PosY = target.PosY; }
            if (target.HasXOffset) { result.HasXOffset = true; result.OffsetX = target.OffsetX; }
            if (target.HasYOffset) { result.HasYOffset = true; result.OffsetY = target.OffsetY; }
            if (target.HasZoom)
            {
                result.HasZoom = true;
                result.ZoomX = target.ZoomX;
                result.ZoomY = target.ZoomY;
            }
            if (target.HasAlpha) { result.HasAlpha = true; result.Alpha = target.Alpha; }
            if (target.HasRotate) { result.HasRotate = true; result.Rotate = target.Rotate; }
            return result;
        }

        private static GameBridge.TransformState Copy(GameBridge.TransformState source)
        {
            source = source ?? new GameBridge.TransformState();
            return new GameBridge.TransformState
            {
                HasXAlign = source.HasXAlign, AlignX = source.AlignX,
                HasYAlign = source.HasYAlign, AlignY = source.AlignY,
                HasXPos = source.HasXPos, PosX = source.PosX,
                HasYPos = source.HasYPos, PosY = source.PosY,
                HasXOffset = source.HasXOffset, OffsetX = source.OffsetX,
                HasYOffset = source.HasYOffset, OffsetY = source.OffsetY,
                HasZoom = source.HasZoom, ZoomX = source.ZoomX, ZoomY = source.ZoomY,
                HasAlpha = source.HasAlpha, Alpha = source.Alpha,
                HasRotate = source.HasRotate, Rotate = source.Rotate,
            };
        }
    }

    /// <summary>
    /// The mod's own animation runner.
    ///
    /// It keeps a list of pictures that are moving and steps them once per frame. The
    /// game's own runner is not available inside a mod label, and this is what fills that
    /// gap: transforms work there, and a script waiting for a picture to arrive is let go
    /// once it has.
    /// </summary>
    public static class AtlScheduler
    {
        private static readonly List<AtlAnimation> s_running = new List<AtlAnimation>();
        private static readonly List<AtlScriptAnimation> s_scriptRunning =
            new List<AtlScriptAnimation>();

        /// <summary>How many pictures are moving at the moment.</summary>
        public static int RunningCount => s_running.Count + s_scriptRunning.Count;

        /// <summary>
        /// Start a picture moving along a transform.
        ///
        /// The picture is put where the transform starts straight away, so it never jumps
        /// from wherever it was, and then it is eased along to where the transform ends.
        /// </summary>
        public static bool Start(ActiveImage target, Transform renderer, int textureWidth,
            int textureHeight, GameBridge.TransformAnimation animation)
        {
            if (target == null || renderer == null || animation == null) return false;

            // A newer transform replaces any older animation for this image.
            CancelTarget(target);

            var move = new AtlAnimation(target, renderer, textureWidth, textureHeight,
                                        animation.Start, animation.End,
                                        animation.Duration, animation.EaseType);

            if (animation.Start != null) move.Apply(animation.Start);

            if (animation.IsStill || animation.End == null || animation.Duration <= 0f)
            {
                if (animation.End != null) move.Apply(animation.End);
                return false;
            }

            s_running.Add(move);
            return true;
        }

        /// <summary>Start a user-authored ATL transform after its image has been shown.</summary>
        internal static bool StartScript(string container, string layer,
            GameBridge.TransformState initial, IList<AtlScriptSegment> segments)
        {
            if (string.IsNullOrEmpty(container) || initial == null || segments == null)
                return false;

            layer = string.IsNullOrEmpty(layer) ? "master" : layer;
            Cancel(container, layer);

            ActiveImage target = ArtInjector.ImageFor(container, layer);
            if (target == null || !ArtInjector.Has(container, layer))
            {
                Log.Warn("atl: no image named '" + container + "' on layer '" + layer + "'.");
                return false;
            }

            if (!ArtInjector.ApplyStaticTransform(container, layer, initial, false))
                return false;

            if (segments.Count == 0) return true;

            var animation = new AtlScriptAnimation(container, layer, target, initial, segments);
            animation.Advance(0f);
            if (!animation.Finished) s_scriptRunning.Add(animation);
            return true;
        }

        internal static void Cancel(string container, string layer = "master")
        {
            ActiveImage target = ArtInjector.ImageFor(container, layer);
            if (target != null) CancelTarget(target);

            string effectiveLayer = string.IsNullOrEmpty(layer) ? "master" : layer;
            s_scriptRunning.RemoveAll(move =>
                string.Equals(move.Container, container, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(move.Layer, effectiveLayer, StringComparison.OrdinalIgnoreCase));
        }

        private static void CancelTarget(ActiveImage target)
        {
            if (target == null) return;
            s_running.RemoveAll(move => move.Target == target);
            s_scriptRunning.RemoveAll(move => move.Target == target);
        }

        /// <summary>Step every moving picture along.</summary>
        public static void Tick(float delta)
        {
            for (int i = s_running.Count - 1; i >= 0; i--)
            {
                var move = s_running[i];
                move.Advance(delta);

                if (!move.Finished) continue;

                s_running.RemoveAt(i);
                Log.Info("atl: '" + move.Target.Name + "' finished moving");
            }

            for (int i = s_scriptRunning.Count - 1; i >= 0; i--)
            {
                AtlScriptAnimation animation = s_scriptRunning[i];
                animation.Advance(delta);
                if (!animation.Finished) continue;

                s_scriptRunning.RemoveAt(i);
                Log.Info("atl: '" + animation.Container + "' finished its custom transform");
            }
        }

        /// <summary>Forget every move, used when the mod is reloaded.</summary>
        public static void Clear()
        {
            s_running.Clear();
            s_scriptRunning.Clear();
        }
    }
}    }
}
