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

            Apply(Lerp(Start, End, EaseShape(t), Finished));
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

            bool hasZoomX = state.HasZoom || state.HasXZoom;
            bool hasZoomY = state.HasZoom || state.HasYZoom;
            float zoomX = hasZoomX ? state.ZoomX : Renderer.localScale.x;
            float zoomY = hasZoomY ? state.ZoomY : Renderer.localScale.y;

            // Negative xzoom/yzoom flips the image, but does not make its layout
            // bounds negative.
            float width = TextureWidth * Mathf.Abs(zoomX);
            float height = TextureHeight * Mathf.Abs(zoomY);
            float rotationDegrees = state.HasRotate ? state.Rotate : -Renderer.localEulerAngles.z;
            bool hasRotation = state.HasRotate ||
                Mathf.Abs(Mathf.DeltaAngle(0f, rotationDegrees)) > 0.001f;
            ArtInjector.GetDisplayableDimensions(width, height, hasRotation,
                rotationDegrees, state.RotatePad, out width, out height);

            float anchorX = ResolveAnchorPixels(state, true, width);
            float anchorY = ResolveAnchorPixels(state, false, height);
            float left, top;
            if (state.HasXAlign)
            {
                // xalign sets xpos and xanchor to the same fraction unless an
                // explicit xanchor overrides it.
                left = state.AlignX * ArtInjector.ScreenWidth - anchorX;
            }
            else if (state.HasXPos)
            {
                float positionX = state.XPosIsRelative
                    ? state.PosX * ArtInjector.ScreenWidth : state.PosX;
                left = positionX - anchorX;
            }
            else
            {
                left = ArtInjector.ScreenWidth * 0.5f - width * 0.5f - anchorX;
            }

            if (state.HasYAlign)
            {
                top = state.AlignY * ArtInjector.ScreenHeight - anchorY;
            }
            else if (state.HasYPos)
            {
                float positionY = state.YPosIsRelative
                    ? state.PosY * ArtInjector.ScreenHeight : state.PosY;
                top = positionY - anchorY;
            }
            else
            {
                top = ArtInjector.ScreenHeight * 0.5f - height * 0.5f - anchorY;
            }

            if (state.HasXOffset) left += state.OffsetX;
            if (state.HasYOffset) top += state.OffsetY;

            // Ren'Py defaults to pixel-aligned placement. Subpixel mode keeps the
            // fractional top-left position, allowing the renderer to blend edge pixels
            // during movement.
            if (!state.Subpixel)
            {
                left = Mathf.Round(left);
                top = Mathf.Round(top);
            }

            if (hasZoomX || hasZoomY)
                Renderer.localScale = new Vector3(zoomX, zoomY, 1f);
            Renderer.localPosition = new Vector3(
                left + width * 0.5f - ArtInjector.ScreenWidth * 0.5f,
                -(top + height * 0.5f - ArtInjector.ScreenHeight * 0.5f),
                0f);
            if (state.HasRotate)
                Renderer.localRotation = Quaternion.Euler(0f, 0f, -state.Rotate);

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

        private float EffectiveWidth(GameBridge.TransformState state)
        {
            float width, height;
            EffectiveDimensions(state, out width, out height);
            return width;
        }

        private float EffectiveHeight(GameBridge.TransformState state)
        {
            float width, height;
            EffectiveDimensions(state, out width, out height);
            return height;
        }

        private void EffectiveDimensions(GameBridge.TransformState state,
            out float width, out float height)
        {
            float scaleX = state != null && (state.HasZoom || state.HasXZoom)
                ? Mathf.Abs(state.ZoomX) : 1f;
            float scaleY = state != null && (state.HasZoom || state.HasYZoom)
                ? Mathf.Abs(state.ZoomY) : 1f;
            width = TextureWidth * scaleX;
            height = TextureHeight * scaleY;
            float rotationDegrees = state != null && state.HasRotate
                ? state.Rotate : -Renderer.localEulerAngles.z;
            bool hasRotation = (state != null && state.HasRotate) ||
                Mathf.Abs(Mathf.DeltaAngle(0f, rotationDegrees)) > 0.001f;
            ArtInjector.GetDisplayableDimensions(width, height, hasRotation,
                rotationDegrees, state == null || state.RotatePad, out width, out height);
        }

        private static float EffectiveZoom(GameBridge.TransformState state, bool horizontal)
        {
            if (state == null) return 1f;
            if (horizontal)
                return state.HasZoom || state.HasXZoom ? Mathf.Abs(state.ZoomX) : 1f;
            return state.HasZoom || state.HasYZoom ? Mathf.Abs(state.ZoomY) : 1f;
        }

        private static float ResolveAnchorPixels(GameBridge.TransformState state,
            bool horizontal, float displayableSize)
        {
            if (state == null) return 0f;

            if (horizontal)
            {
                if (state.HasXAnchor)
                    return state.XAnchorIsRelative
                        ? state.AnchorX * displayableSize : state.AnchorX;
                return state.HasXAlign ? state.AlignX * displayableSize : 0f;
            }

            if (state.HasYAnchor)
                return state.YAnchorIsRelative
                    ? state.AnchorY * displayableSize : state.AnchorY;
            return state.HasYAlign ? state.AlignY * displayableSize : 0f;
        }

        /// <summary>
        /// The state a picture is in part way along a move.
        ///
        /// Every property is eased from its value at the start to its value at the end.
        /// A property the end does not mention keeps the value it had at the start, which
        /// is how a transform that only moves a picture leaves its size alone.
        /// </summary>
        public GameBridge.TransformState Lerp(GameBridge.TransformState from,
            GameBridge.TransformState to, float t, bool complete = false)
        {
            var result = new GameBridge.TransformState();

            if (from == null && to == null) return result;

            var a = from ?? new GameBridge.TransformState();
            var b = to ?? new GameBridge.TransformState();

            bool aHasXPosition = a.HasXAlign || a.HasXPos;
            bool bHasXPosition = b.HasXAlign || b.HasXPos;
            if (aHasXPosition || bHasXPosition)
            {
                var startX = aHasXPosition ? a : b;
                var endX = bHasXPosition ? b : a;
                result.HasXPos = true;
                result.PosX = Ease(ResolvePositionPixels(startX, true),
                                   ResolvePositionPixels(endX, true), t);
                result.XPosIsRelative = false;
            }

            bool aHasYPosition = a.HasYAlign || a.HasYPos;
            bool bHasYPosition = b.HasYAlign || b.HasYPos;
            if (aHasYPosition || bHasYPosition)
            {
                var startY = aHasYPosition ? a : b;
                var endY = bHasYPosition ? b : a;
                result.HasYPos = true;
                result.PosY = Ease(ResolvePositionPixels(startY, false),
                                   ResolvePositionPixels(endY, false), t);
                result.YPosIsRelative = false;
            }

            bool aHasAnchorX = a.HasXAnchor || a.HasXAlign;
            bool bHasAnchorX = b.HasXAnchor || b.HasXAlign;
            if (aHasAnchorX || bHasAnchorX)
            {
                result.HasXAnchor = true;
                var anchorStartX = aHasAnchorX ? a : b;
                var anchorEndX = bHasAnchorX ? b : a;
                result.AnchorX = Ease(
                    ResolveAnchorPixels(anchorStartX, true, EffectiveWidth(anchorStartX)),
                    ResolveAnchorPixels(anchorEndX, true, EffectiveWidth(anchorEndX)), t);
                result.XAnchorIsRelative = false;
            }

            bool aHasAnchorY = a.HasYAnchor || a.HasYAlign;
            bool bHasAnchorY = b.HasYAnchor || b.HasYAlign;
            if (aHasAnchorY || bHasAnchorY)
            {
                result.HasYAnchor = true;
                var anchorStartY = aHasAnchorY ? a : b;
                var anchorEndY = bHasAnchorY ? b : a;
                result.AnchorY = Ease(
                    ResolveAnchorPixels(anchorStartY, false, EffectiveHeight(anchorStartY)),
                    ResolveAnchorPixels(anchorEndY, false, EffectiveHeight(anchorEndY)), t);
                result.YAnchorIsRelative = false;
            }

            bool aHasZoomX = a.HasZoom || a.HasXZoom;
            bool bHasZoomX = b.HasZoom || b.HasXZoom;
            if (aHasZoomX || bHasZoomX)
            {
                result.HasXZoom = true;
                result.ZoomX = Ease(aHasZoomX ? a.ZoomX : b.ZoomX,
                                    bHasZoomX ? b.ZoomX : a.ZoomX, t);
            }

            bool aHasZoomY = a.HasZoom || a.HasYZoom;
            bool bHasZoomY = b.HasZoom || b.HasYZoom;
            if (aHasZoomY || bHasZoomY)
            {
                result.HasYZoom = true;
                result.ZoomY = Ease(aHasZoomY ? a.ZoomY : b.ZoomY,
                                    bHasZoomY ? b.ZoomY : a.ZoomY, t);
            }

            if (a.HasAlpha || b.HasAlpha)
            {
                result.HasAlpha = true;
                result.Alpha = Ease(a.HasAlpha ? a.Alpha : b.Alpha,
                                    b.HasAlpha ? b.Alpha : a.Alpha, t);
            }

            if (a.HasSubpixel || b.HasSubpixel)
            {
                result.HasSubpixel = true;
                bool startSubpixel = a.HasSubpixel && a.Subpixel;
                bool endSubpixel = b.HasSubpixel ? b.Subpixel : startSubpixel;
                result.Subpixel = complete ? endSubpixel : startSubpixel;
            }

            if (a.HasRotate || b.HasRotate)
            {
                result.HasRotate = true;
                result.Rotate = Ease(a.HasRotate ? a.Rotate : b.Rotate,
                                     b.HasRotate ? b.Rotate : a.Rotate, t);
            }

            if (a.HasRotatePad || b.HasRotatePad)
            {
                result.HasRotatePad = true;
                bool startRotatePad = a.HasRotatePad ? a.RotatePad : true;
                bool endRotatePad = b.HasRotatePad ? b.RotatePad : startRotatePad;
                result.RotatePad = complete ? endRotatePad : startRotatePad;
            }

            if (a.HasTransformAnchor || b.HasTransformAnchor)
            {
                result.HasTransformAnchor = true;
                bool startTransformAnchor = a.HasTransformAnchor && a.TransformAnchor;
                bool endTransformAnchor = b.HasTransformAnchor
                    ? b.TransformAnchor : startTransformAnchor;
                result.TransformAnchor = complete ? endTransformAnchor : startTransformAnchor;
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

        private static float ResolvePositionPixels(GameBridge.TransformState state,
            bool horizontal)
        {
            if (state == null) return 0f;
            if (horizontal)
            {
                if (state.HasXPos)
                    return state.XPosIsRelative
                        ? state.PosX * ArtInjector.ScreenWidth : state.PosX;
                if (state.HasXAlign)
                    return state.AlignX * ArtInjector.ScreenWidth;
            }
            else
            {
                if (state.HasYPos)
                    return state.YPosIsRelative
                        ? state.PosY * ArtInjector.ScreenHeight : state.PosY;
                if (state.HasYAlign)
                    return state.AlignY * ArtInjector.ScreenHeight;
            }
            return 0f;
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
        public bool IsPause { get; set; }
    }

    /// <summary>Runs sequential ATL stages over a StoryAPI-owned image.</summary>
    internal sealed class AtlScriptAnimation
    {
        private readonly List<AtlScriptSegment> _segments;
        private readonly GameBridge.TransformState _initial;
        private readonly int _repeatCount;
        private int _completedCycles = 1;
        private GameBridge.TransformState _current;
        private int _segmentIndex;
        private float _elapsed;
        private bool _waitingForZeroPauseFrame;
        private readonly float _fps;
        private float _wallClockElapsed;
        private float _timebaseElapsed;

        public string Container { get; }
        public string Layer { get; }
        public ActiveImage Target { get; }
        public bool Finished { get; private set; }

        public AtlScriptAnimation(string container, string layer, ActiveImage target,
            GameBridge.TransformState initial, IList<AtlScriptSegment> segments, int repeatCount)
        {
            Container = container;
            Layer = string.IsNullOrEmpty(layer) ? "master" : layer;
            Target = target;
            _initial = Copy(initial);
            _current = Copy(initial);
            _segments = new List<AtlScriptSegment>(segments);
            _repeatCount = repeatCount;
            _fps = initial != null && initial.HasFps ? initial.Fps : 0f;
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
            if (_fps > 0f)
            {
                // Ren'Py rounds transform time down to the nearest frame interval.
                // Accumulating wall-clock time keeps the frame grid continuous across
                // interpolation, pause, and repeat boundaries.
                _wallClockElapsed += remaining;
                float quantizedTime = Mathf.Floor(_wallClockElapsed * _fps + 0.00001f) / _fps;
                remaining = Mathf.Max(0f, quantizedTime - _timebaseElapsed);
                _timebaseElapsed = quantizedTime;
            }

            while (true)
            {
                if (_segmentIndex >= _segments.Count)
                {
                    bool shouldRepeat = _repeatCount < 0 ||
                        (_repeatCount > 0 && _completedCycles < _repeatCount);
                    if (!shouldRepeat)
                    {
                        Finished = true;
                        return;
                    }

                    // Repeat restarts the complete transform timeline from its
                    // original properties. Defer the next cycle to the next frame,
                    // which also prevents an all-zero-duration loop from spinning.
                    _completedCycles++;
                    _segmentIndex = 0;
                    _elapsed = 0f;
                    _waitingForZeroPauseFrame = false;
                    _current = Copy(_initial);
                    ArtInjector.ApplyStaticTransform(Container, Layer, _current, false);
                    return;
                }

                AtlScriptSegment segment = _segments[_segmentIndex];
                if (segment == null || segment.Target == null)
                {
                    _segmentIndex++;
                    _elapsed = 0f;
                    continue;
                }

                float duration = Mathf.Max(0f, segment.Duration);
                if (segment.IsPause && duration <= 0f)
                {
                    // Ren'Py's pause 0 is special: it guarantees at least one frame.
                    // StartScript calls Advance(0) immediately after show, so defer
                    // completion until the next scheduler tick.
                    if (!_waitingForZeroPauseFrame)
                    {
                        _waitingForZeroPauseFrame = true;
                        ArtInjector.ApplyStaticTransform(Container, Layer, _current, false);
                        return;
                    }

                    _waitingForZeroPauseFrame = false;
                    _segmentIndex++;
                    _elapsed = 0f;
                    continue;
                }

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

                if (segment.IsPause)
                {
                    // Holding a pause segment means leaving the current transform intact.
                    ArtInjector.ApplyStaticTransform(Container, Layer, _current, false);
                }
                else
                {
                    float t = Mathf.Clamp01(_elapsed / duration);
                    var frame = Copy(_current);
                    float targetWidth, targetHeight;
                    GetTargetDimensions(out targetWidth, out targetHeight);
                    Interpolate(frame, _current, segment.Target,
                        Warp(segment.EaseName, t), targetWidth, targetHeight,
                        _elapsed >= duration);
                    ArtInjector.ApplyStaticTransform(Container, Layer, frame, false);
                }

                if (_elapsed < duration) return;

                if (!segment.IsPause)
                    _current = Merge(_current, segment.Target);
                _segmentIndex++;
                _elapsed = 0f;

                if (remaining <= 0f && _segmentIndex < _segments.Count)
                {
                    AtlScriptSegment next = _segments[_segmentIndex];
                    if (next != null && next.Duration > 0f) return;
                }
            }
        }

        private static float Warp(string easeName, float t)
        {
            string name = (easeName ?? "linear").ToLowerInvariant();
            switch (name)
            {
                case "ease":
                    return 0.5f - Mathf.Cos(Mathf.PI * t) * 0.5f;
                case "easein":
                    return Mathf.Cos((1f - t) * Mathf.PI * 0.5f);
                case "easeout":
                    return 1f - Mathf.Cos(t * Mathf.PI * 0.5f);
                default:
                    // Ren'Py intentionally uses the opposite naming convention from
                    // the familiar Penner "in"/"out" labels: easein_* maps to
                    // easings.net easeOut_*, while easeout_* maps to easeIn_*.
                    if (name.StartsWith("easein_", StringComparison.Ordinal))
                        return 1f - EaseOutPenner(name.Substring(7), 1f - t);
                    if (name.StartsWith("easeout_", StringComparison.Ordinal))
                        return EaseOutPenner(name.Substring(8), t);
                    if (name.StartsWith("ease_", StringComparison.Ordinal))
                        return EaseInOutPenner(name.Substring(5), t);
                    return t;
            }
        }

        /// <summary>
        /// Ren'Py's easeout_* curves correspond to the standard Penner ease-in
        /// curves. The mirroring for easein_* and the symmetric ease_* forms is
        /// applied by the callers above and below.
        /// </summary>
        private static float EaseOutPenner(string family, float t)
        {
            switch (family)
            {
                case "quad": return t * t;
                case "cubic": return t * t * t;
                case "quart": return t * t * t * t;
                case "quint": return t * t * t * t * t;
                case "expo": return Mathf.Pow(2f, 10f * (t - 1f));
                case "circ": return 1f - Mathf.Sqrt(1f - t * t);
                case "back":
                {
                    const float overshoot = 1.7015f;
                    return t * t * ((overshoot + 1f) * t - overshoot);
                }
                case "elastic":
                    return 1f - EaseOutElasticPenner(1f - t);
                case "bounce":
                    return 1f - EaseOutBouncePenner(1f - t);
                default:
                    return t;
            }
        }

        private static float EaseInOutPenner(string family, float t)
        {
            if (t < 0.5f)
                return EaseOutPenner(family, t * 2f) * 0.5f;

            return 1f - EaseOutPenner(family, (1f - t) * 2f) * 0.5f;
        }

        // This is the Penner "ease-out" elastic curve used as Ren'Py easein_elastic.
        private static float EaseOutElasticPenner(float t)
        {
            const float period = 0.3f;
            return 1f + Mathf.Pow(2f, -10f * t) *
                Mathf.Sin((t - period * 0.25f) * (2f * Mathf.PI) / period);
        }

        // This is the Penner "ease-out" bounce curve used as Ren'Py easein_bounce.
        private static float EaseOutBouncePenner(float t)
        {
            const float period = 2.75f;
            float scale = period * period;

            if (t < 1f / period)
                return scale * t * t;
            if (t < 2f / period)
                return 1f + scale * (
                    Mathf.Pow(t - 1.5f / period, 2f) -
                    Mathf.Pow(-0.5f / period, 2f));
            if (t < 2.5f / period)
                return 1f + scale * (
                    Mathf.Pow(t - 2.25f / period, 2f) -
                    Mathf.Pow(-0.25f / period, 2f));

            return 1f + scale * (
                Mathf.Pow(t - 2.625f / period, 2f) -
                Mathf.Pow(-0.125f / period, 2f));
        }

        private void GetTargetDimensions(out float width, out float height)
        {
            width = 1f;
            height = 1f;
            Transform renderer = ArtInjector.RendererFor(Container, Layer);
            if (renderer == null) return;

            var renderers = renderer.GetComponentsInChildren<SpriteRenderer>(true);
            foreach (var spriteRenderer in renderers)
            {
                if (spriteRenderer == null || spriteRenderer.sprite == null) continue;
                float candidateWidth = Mathf.Max(1f, spriteRenderer.sprite.rect.width);
                float candidateHeight = Mathf.Max(1f, spriteRenderer.sprite.rect.height);
                if (candidateWidth * candidateHeight > width * height)
                {
                    width = candidateWidth;
                    height = candidateHeight;
                }
            }
        }

        private static float ResolvePositionPixels(GameBridge.TransformState state,
            bool horizontal)
        {
            if (state == null) return 0f;
            if (horizontal)
            {
                if (state.HasXPos)
                    return state.XPosIsRelative
                        ? state.PosX * ArtInjector.ScreenWidth : state.PosX;
                if (state.HasXAlign)
                    return state.AlignX * ArtInjector.ScreenWidth;
            }
            else
            {
                if (state.HasYPos)
                    return state.YPosIsRelative
                        ? state.PosY * ArtInjector.ScreenHeight : state.PosY;
                if (state.HasYAlign)
                    return state.AlignY * ArtInjector.ScreenHeight;
            }
            return 0f;
        }

        private static float EffectiveZoom(GameBridge.TransformState state, bool horizontal)
        {
            if (state == null) return 1f;
            if (horizontal)
                return state.HasZoom || state.HasXZoom ? Mathf.Abs(state.ZoomX) : 1f;
            return state.HasZoom || state.HasYZoom ? Mathf.Abs(state.ZoomY) : 1f;
        }

        private static void ResolveAnchorPixels(GameBridge.TransformState state,
            float displayableWidth, float displayableHeight, bool childCoordinates,
            out float anchorX, out float anchorY)
        {
            state = state ?? new GameBridge.TransformState();
            if (childCoordinates)
            {
                // While transform_anchor is active on the current frame, the anchor
                // value is expressed in the source child's coordinate system.
                anchorX = state.HasXAnchor
                    ? (state.XAnchorIsRelative ? state.AnchorX * displayableWidth : state.AnchorX)
                    : (state.HasXAlign ? state.AlignX * displayableWidth : 0f);
                anchorY = state.HasYAnchor
                    ? (state.YAnchorIsRelative ? state.AnchorY * displayableHeight : state.AnchorY)
                    : (state.HasYAlign ? state.AlignY * displayableHeight : 0f);
                return;
            }

            float zoomX = state.HasZoom || state.HasXZoom ? state.ZoomX : 1f;
            float zoomY = state.HasZoom || state.HasYZoom ? state.ZoomY : 1f;
            float rotation = state.HasRotate ? state.Rotate : 0f;
            bool hasRotation = Mathf.Abs(Mathf.DeltaAngle(0f, rotation)) > 0.001f;

            ArtInjector.GetTransformAnchorPixels(
                state, displayableWidth, displayableHeight,
                zoomX, zoomY, rotation, hasRotation,
                out anchorX, out anchorY);
        }

        private static void Interpolate(GameBridge.TransformState frame,
            GameBridge.TransformState from, GameBridge.TransformState to, float t,
            float displayableWidth, float displayableHeight, bool complete)
        {
            // Back, elastic, and bounce warpers intentionally overshoot [0, 1].
            // Unclamped interpolation preserves those values instead of clipping them.
            bool startTransformAnchor = from.HasTransformAnchor && from.TransformAnchor;
            bool endTransformAnchor = to.HasTransformAnchor
                ? to.TransformAnchor : startTransformAnchor;
            float fromAnchorX, fromAnchorY, toAnchorX, toAnchorY;
            ResolveAnchorPixels(from, displayableWidth, displayableHeight,
                startTransformAnchor, out fromAnchorX, out fromAnchorY);
            ResolveAnchorPixels(to, displayableWidth, displayableHeight,
                startTransformAnchor, out toAnchorX, out toAnchorY);
            if (to.HasXAlign || to.HasXPos)
            {
                frame.HasXAlign = false;
                frame.HasXPos = true;
                frame.PosX = Mathf.LerpUnclamped(
                    ResolvePositionPixels(from, true),
                    ResolvePositionPixels(to, true), t);
                frame.XPosIsRelative = false;
            }
            if (to.HasYAlign || to.HasYPos)
            {
                frame.HasYAlign = false;
                frame.HasYPos = true;
                frame.PosY = Mathf.LerpUnclamped(
                    ResolvePositionPixels(from, false),
                    ResolvePositionPixels(to, false), t);
                frame.YPosIsRelative = false;
            }
            if (to.HasXAnchor)
            {
                frame.HasXAnchor = true;
                frame.AnchorX = Mathf.LerpUnclamped(fromAnchorX, toAnchorX, t);
                if (complete && endTransformAnchor != startTransformAnchor)
                {
                    ResolveAnchorPixels(to, displayableWidth, displayableHeight,
                        endTransformAnchor, out frame.AnchorX, out var ignoredY);
                }
                frame.XAnchorIsRelative = false;
            }
            if (to.HasYAnchor)
            {
                frame.HasYAnchor = true;
                frame.AnchorY = Mathf.LerpUnclamped(fromAnchorY, toAnchorY, t);
                if (complete && endTransformAnchor != startTransformAnchor)
                {
                    ResolveAnchorPixels(to, displayableWidth, displayableHeight,
                        endTransformAnchor, out var ignoredX, out frame.AnchorY);
                }
                frame.YAnchorIsRelative = false;
            }
            if (to.HasXOffset) { frame.HasXOffset = true; frame.OffsetX = Mathf.LerpUnclamped(from.OffsetX, to.OffsetX, t); }
            if (to.HasYOffset) { frame.HasYOffset = true; frame.OffsetY = Mathf.LerpUnclamped(from.OffsetY, to.OffsetY, t); }
            if (to.HasZoom || to.HasXZoom)
            {
                frame.HasXZoom = true;
                frame.ZoomX = Mathf.LerpUnclamped(from.ZoomX, to.ZoomX, t);
            }
            if (to.HasZoom || to.HasYZoom)
            {
                frame.HasYZoom = true;
                frame.ZoomY = Mathf.LerpUnclamped(from.ZoomY, to.ZoomY, t);
            }
            if (to.HasAlpha) { frame.HasAlpha = true; frame.Alpha = Mathf.LerpUnclamped(from.Alpha, to.Alpha, t); }
            if (to.HasRotate) { frame.HasRotate = true; frame.Rotate = Mathf.LerpUnclamped(from.Rotate, to.Rotate, t); }
            if (to.HasRotatePad)
            {
                frame.HasRotatePad = true;
                bool startRotatePad = from.HasRotatePad ? from.RotatePad : true;
                frame.RotatePad = complete ? to.RotatePad : startRotatePad;
            }
            if (to.HasTransformAnchor)
            {
                frame.HasTransformAnchor = true;
                bool startTransformAnchor = from.HasTransformAnchor && from.TransformAnchor;
                frame.TransformAnchor = complete ? to.TransformAnchor : startTransformAnchor;
            }
        }

        private static GameBridge.TransformState Merge(GameBridge.TransformState current,
            GameBridge.TransformState target)
        {
            var result = Copy(current);
            if (target.HasXAlign)
            {
                result.HasXAlign = true; result.AlignX = target.AlignX;
                result.HasXPos = false;
            }
            if (target.HasYAlign)
            {
                result.HasYAlign = true; result.AlignY = target.AlignY;
                result.HasYPos = false;
            }
            if (target.HasXPos)
            {
                result.HasXPos = true; result.PosX = target.PosX;
                result.XPosIsRelative = target.XPosIsRelative;
                result.HasXAlign = false;
            }
            if (target.HasYPos)
            {
                result.HasYPos = true; result.PosY = target.PosY;
                result.YPosIsRelative = target.YPosIsRelative;
                result.HasYAlign = false;
            }
            if (target.HasXOffset) { result.HasXOffset = true; result.OffsetX = target.OffsetX; }
            if (target.HasYOffset) { result.HasYOffset = true; result.OffsetY = target.OffsetY; }
            if (target.HasXAnchor)
            {
                result.HasXAnchor = true;
                result.AnchorX = target.AnchorX;
                result.XAnchorIsRelative = target.XAnchorIsRelative;
            }
            if (target.HasYAnchor)
            {
                result.HasYAnchor = true;
                result.AnchorY = target.AnchorY;
                result.YAnchorIsRelative = target.YAnchorIsRelative;
            }
            if (target.HasZoom || target.HasXZoom)
            {
                result.HasXZoom = true;
                result.ZoomX = target.ZoomX;
            }
            if (target.HasZoom || target.HasYZoom)
            {
                result.HasYZoom = true;
                result.ZoomY = target.ZoomY;
            }
            if (target.HasAlpha) { result.HasAlpha = true; result.Alpha = target.Alpha; }
            if (target.HasRotate) { result.HasRotate = true; result.Rotate = target.Rotate; }
            if (target.HasRotatePad)
            {
                result.HasRotatePad = true;
                result.RotatePad = target.RotatePad;
            }
            if (target.HasTransformAnchor)
            {
                result.HasTransformAnchor = true;
                result.TransformAnchor = target.TransformAnchor;
            }
            if (target.HasSubpixel)
            {
                result.HasSubpixel = true;
                result.Subpixel = target.Subpixel;
            }
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
                XPosIsRelative = source.XPosIsRelative,
                HasYPos = source.HasYPos, PosY = source.PosY,
                YPosIsRelative = source.YPosIsRelative,
                HasXAnchor = source.HasXAnchor, AnchorX = source.AnchorX,
                HasYAnchor = source.HasYAnchor, AnchorY = source.AnchorY,
                XAnchorIsRelative = source.XAnchorIsRelative,
                YAnchorIsRelative = source.YAnchorIsRelative,
                HasXOffset = source.HasXOffset, OffsetX = source.OffsetX,
                HasYOffset = source.HasYOffset, OffsetY = source.OffsetY,
                HasZoom = source.HasZoom,
                HasXZoom = source.HasXZoom, HasYZoom = source.HasYZoom,
                ZoomX = source.ZoomX, ZoomY = source.ZoomY,
                HasAlpha = source.HasAlpha, Alpha = source.Alpha,
                HasRotate = source.HasRotate, Rotate = source.Rotate,
                HasRotatePad = source.HasRotatePad, RotatePad = source.RotatePad,
                HasTransformAnchor = source.HasTransformAnchor,
                TransformAnchor = source.TransformAnchor,
                HasSubpixel = source.HasSubpixel, Subpixel = source.Subpixel,
                HasFps = source.HasFps, Fps = source.Fps,
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
            GameBridge.TransformState initial, IList<AtlScriptSegment> segments, int repeatCount)
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

            var animation = new AtlScriptAnimation(container, layer, target, initial, segments, repeatCount);
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
}
