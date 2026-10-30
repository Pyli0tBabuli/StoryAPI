using System;
using System.Collections.Generic;
using System.Reflection;
using RenPyParser.Images;
using RenPyParser.Sprites;
using RenPyParser.Transforms;
using RenpyParser;
using SimpleExpressionEngine;
using UnityEngine;

namespace StoryAPI.Runtime
{
    /// <summary>
    /// Poses mod sprites: position, size, rotation and transparency.
    ///
    /// The game re-derives every sprite's placement, scale and opacity from scratch on
    /// each frame, so nothing can be set once and left alone. Two different handles are
    /// needed, and the split is forced by the engine:
    ///
    /// * Geometry -- where the sprite is, how big, how turned -- goes on the child
    ///   renderer. Nothing else writes there, so it sticks.
    /// * Opacity has to travel through the container's Ren'Py transform, because the
    ///   game overwrites the colour of every child renderer from it each frame, and a
    ///   value set on the renderer would simply be discarded.
    /// </summary>
    public static class ArtInjector
    {
        /// <summary>
        /// The game lays everything out against a fixed reference resolution, so the same
        /// numbers are used here to turn normalised positions into world units.
        /// </summary>
        internal const float ScreenWidth = 1280f;
        internal const float ScreenHeight = 720f;

        private static FieldInfo s_musicSourcesField;

        private static readonly Dictionary<string, SpriteHandle> s_sprites =
            new Dictionary<string, SpriteHandle>(StringComparer.OrdinalIgnoreCase);

        private const string SlotKeySeparator = "\u001f";

        /// <summary>
        /// The same image tag can exist on different Ren'Py layers. Keep StoryAPI's
        /// renderer/animation/save bookkeeping scoped by layer while still asking the
        /// game's image manager for the original tag within the selected layer.
        /// </summary>
        internal static string SlotKey(string container, string layer)
        {
            if (string.IsNullOrEmpty(container)) return container;
            if (string.IsNullOrEmpty(layer) ||
                string.Equals(layer, "master", StringComparison.OrdinalIgnoreCase))
                return container;
            return layer + SlotKeySeparator + container;
        }

        private static string ResolveSlotKey(string container, string layer)
        {
            if (string.IsNullOrEmpty(container)) return null;
            if (s_sprites.ContainsKey(container)) return container;

            string key = SlotKey(container, layer);
            if (s_sprites.ContainsKey(key)) return key;

            // Existing move/zoom/fade commands do not take an onlayer property. If a
            // tag exists on exactly one non-master layer, resolve that unique target;
            // when the same tag exists on multiple layers, refuse to guess.
            if (string.IsNullOrEmpty(layer) ||
                string.Equals(layer, "master", StringComparison.OrdinalIgnoreCase))
            {
                string match = null;
                foreach (var pair in s_sprites)
                {
                    if (pair.Value?.Recipe == null ||
                        !string.Equals(pair.Value.Recipe.Slot, container,
                            StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (match != null) return null;
                    match = pair.Key;
                }
                return match;
            }

            return null;
        }

        private static string LayerOf(Show show)
        {
            return string.IsNullOrEmpty(show?.Layer) ? "master" : show.Layer;
        }

        private static string LayerOf(Scene scene)
        {
            if (scene == null) return "master";
            try
            {
                var property = scene.GetType().GetProperty("Layer",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object value = property == null ? null : property.GetValue(scene, null);
                if (value == null)
                {
                    var field = scene.GetType().GetField("Layer",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    value = field == null ? null : field.GetValue(scene);
                }
                return string.IsNullOrEmpty(value as string) ? "master" : (string)value;
            }
            catch
            {
                return "master";
            }
        }

        internal static bool TrySetLayer(object target, string layer)
        {
            if (target == null || string.IsNullOrEmpty(layer)) return false;
            Type type = target.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            try
            {
                var property = type.GetProperty("Layer", flags);
                if (property != null)
                {
                    if (property.CanWrite)
                    {
                        property.SetValue(target, layer, null);
                        return true;
                    }

                    var backing = type.GetField("<Layer>k__BackingField", flags);
                    if (backing != null)
                    {
                        backing.SetValue(target, layer);
                        return true;
                    }
                }

                var field = type.GetField("Layer", flags);
                if (field != null && field.FieldType == typeof(string))
                {
                    field.SetValue(target, layer);
                    return true;
                }
            }
            catch (Exception e)
            {
                Log.Warn("Could not set image layer '" + layer + "' on " +
                    type.FullName + ": " + e.GetBaseException().Message);
                return false;
            }

            return false;
        }

        private sealed class SpriteHandle
        {
            public Transform Renderer;
            public ActiveImage Image;

            /// <summary>
            /// What this picture is and how it was asked for.
            ///
            /// A save records the name of every picture on screen rather than the
            /// picture itself, and a mod's own art is gone once the process ends. The
            /// recipe is written down with the save so the picture can be drawn again
            /// later, even in a new run of the game.
            /// </summary>
            public SavedArt Recipe;
        }

        // ------------------------------------------------------------- placement

        /// <summary>Find the slot the game uses for a named image, creating it if needed.</summary>
        private static ActiveImage EnsureImage(string container, Show show, Scene scene)
        {
            try
            {
                if (scene != null) Renpy.Images.OnScene(scene);
                else Renpy.Images.OnShow(show);

                return Renpy.Images.TryGetImage(container, out var handle) ? handle.Ref : null;
            }
            catch (Exception e)
            {
                Log.Error("Could not open display slot '" + container + "': " + e.Message);
                return null;
            }
        }

        /// <summary>Swap in the mod's sprite, replacing whatever was drawn before.</summary>
        internal static ActiveImage Draw(StoryImage source, string container, Show show, Scene scene, float? x = null)
        {
            if (source == null || !source.TryLoad()) return null;

            string layer = scene != null ? LayerOf(scene) : LayerOf(show);
            string slotKey = SlotKey(container, layer);
            ActiveImage image = EnsureImage(container, show, scene);
            if (image == null || image.Object == null) return null;

            try
            {
                // Showing the same tag twice replaces the picture rather than stacking
                // another copy in the slot, which is what the game does too. Everything
                // already in the slot goes, including the copy the game restores from a
                // save: only one picture may occupy the slot.
                var slot = image.Object.transform;
                for (int i = slot.childCount - 1; i >= 0; i--)
                {
                    SafeDestroy(slot.GetChild(i).gameObject);
                }

                var renderer = new GameObject("StoryAPI_" + source.Name);
                var spriteRenderer = renderer.AddComponent<SpriteRenderer>();
                spriteRenderer.sprite = source.Sprite;

                // Use the game's own sprite material so mod art is composited and faded
                // exactly like the shipped art.
                var materials = Renpy.Materials;
                if (materials != null && materials.Sprite != null)
                {
                    spriteRenderer.sharedMaterial = materials.Sprite;
                }

                renderer.transform.SetParent(image.Object.transform, worldPositionStays: false);

                Remember(slotKey, renderer.transform, image, new SavedArt
                {
                    Kind = SavedArt.Sprite,
                    Mod = source.Mod,
                    Source = source.Name,
                    Slot = container,
                    Layer = layer,
                });
                Place(image);
                SetPosition(slotKey, x ?? 0.5f, 0.5f);
                return image;
            }
            catch (Exception e)
            {
                Log.Error("Could not draw '" + source.Name + "': " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// Put a pose the game owns into a display slot, built by the mod.
        ///
        /// This is the same path a mod sprite takes, so anything that can move or fade a
        /// mod sprite can do the same to a game pose. The difference is only where the
        /// picture comes from: here the game's own prefab, with all of its layers.
        /// </summary>
        internal static ActiveImage Host(GameObject prefab, string container, Show show, float? x)
        {
            if (prefab == null) return null;

            string layer = LayerOf(show);
            string slotKey = SlotKey(container, layer);
            ActiveImage image = EnsureImage(container, show, null);
            if (image == null || image.Object == null) return null;

            try
            {
                // Any earlier version of this pose in the same layer is replaced.
                DestroyHosted(container, layer);

                var instance = UnityEngine.Object.Instantiate(prefab);

                var materials = Renpy.Materials;
                if (materials != null && materials.Sprite != null)
                {
                    foreach (var renderer in instance.GetComponentsInChildren<SpriteRenderer>(true))
                    {
                        renderer.sharedMaterial = materials.Sprite;
                    }
                }

                instance.transform.SetParent(image.Object.transform, worldPositionStays: false);
                // Some generated character prefabs are saved inactive in the asset bundle.
                // The game normally activates them as part of its own Show pipeline, but
                // StoryAPI hosts the prefab directly, so it must explicitly enable the root.
                instance.SetActive(true);

                Remember(slotKey, instance.transform, image, new SavedArt
                {
                    Kind = SavedArt.Pose,
                    Source = container,
                    Slot = container,
                    Layer = layer,
                });
                s_hosted[slotKey] = instance;

                Place(image);
                SetPosition(slotKey, x ?? 0.5f, 0.5f);
                return image;
            }
            catch (Exception e)
            {
                Log.Error("Could not host '" + container + "': " + e.Message);
                return null;
            }
        }

        private static readonly Dictionary<string, GameObject> s_hosted =
            new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The object instantiated for a hosted pose, if there is one.</summary>
        internal static GameObject Hosted(string container, GameObject prefab = null,
            string layer = "master")
        {
            GameObject instance;
            string key = ResolveSlotKey(container, layer);
            if (key == null) key = SlotKey(container, layer);
            if (key != null && s_hosted.TryGetValue(key, out instance) && instance != null)
                return instance;
            return prefab;
        }

        /// <summary>Tear down a hosted pose and forget it.</summary>
        private static void DestroyHosted(string container, string layer)
        {
            GameObject instance;
            string key = SlotKey(container, layer);
            if (container == null || !s_hosted.TryGetValue(key, out instance)) return;

            s_hosted.Remove(key);
            if (instance == null) return;

            try { UnityEngine.Object.Destroy(instance); }
            catch (Exception) { /* already gone */ }
        }

        /// <summary>
        /// Put a picture the game owns into a slot, placed by a transform of the game's.
        ///
        /// The transform's starting state is written onto the slot rather than onto the
        /// renderer, because the game reads the slot's own transform when it lays the sprite
        /// out. Applying it there is what makes the picture land exactly where the shipped
        /// scene would have put it.
        /// </summary>
        internal static ActiveImage DrawNative(Sprite sprite, string container, Show show,
            string transformName)
        {
            if (sprite == null) return null;

            string layer = LayerOf(show);
            string slotKey = SlotKey(container, layer);
            ActiveImage image = EnsureImage(container, show, null);
            if (image == null || image.Object == null) return null;

            try
            {
                // A second show of the same picture replaces the first, rather than
                // stacking another copy in the same slot. Everything already in the
                // slot goes, including the copy the game itself restores from a save:
                // the mod's placement is what the player saw, and two copies of the
                // same picture at different positions is not.
                var slot = image.Object.transform;
                for (int i = slot.childCount - 1; i >= 0; i--)
                {
                    SafeDestroy(slot.GetChild(i).gameObject);
                }

                var renderer = new GameObject("StoryAPI_native");
                var spriteRenderer = renderer.AddComponent<SpriteRenderer>();
                spriteRenderer.sprite = sprite;

                var materials = Renpy.Materials;
                if (materials != null && materials.Sprite != null)
                {
                    spriteRenderer.sharedMaterial = materials.Sprite;
                }

                renderer.transform.SetParent(image.Object.transform, worldPositionStays: false);
                renderer.transform.localPosition = Vector3.zero;
                renderer.transform.localScale = Vector3.one;

                // The picture is placed by writing the transform's state into the slot and
                // letting the game lay it out. The game knows how alignment, anchor and
                // zoom fit together, and doing it here is what used to put pictures in
                // slightly the wrong place.
                // The picture is placed by the mod's own animation runner, which is what
                // stands in for the game's runner inside a mod label. The game lays a
                // picture out from its own slot, but that work belongs to the runner the
                // mod does not have, so nothing would read what was written there.
                bool placed = false;
                if (!string.IsNullOrEmpty(transformName))
                {
                    if (StoryAPI.Game.GameBridge.TryGetTransformAnimation(
                            transformName, out var animation))
                    {
                        // The picture starts where the transform says, and the runner
                        // eases it along to where the transform ends.
                        AtlScheduler.Start(image, renderer.transform,
                            sprite.texture.width, sprite.texture.height, animation);
                        placed = true;

                        Log.Info("show: '" + container + "' with transform '" +
                                 transformName + "' (over " + animation.Duration + "s, " +
                                 animation.EaseType +
                                 (animation.IsStill ? ", places only" : ", moving") + ")");
                    }
                    else
                    {
                        Log.Warn("show: the game has no transform called '" + transformName +
                                 "'; '" + container + "' is shown at the default position");
                    }
                }

                // A transform's own zoom already contains the factor that brings a picture
                // into the game's picture space, so it is the one that decides the size.
                // The size recorded in the picture's own block is only a stand-in for a
                // picture shown without a transform.
                int sizeX = 0, sizeY = 0;
                if (!placed && StoryAPI.Game.GameBridge.TryGetImageSize(
                        container, out sizeX, out sizeY) && sizeX > 0 && sizeY > 0)
                {
                    renderer.transform.localScale = new Vector3(
                        sizeX / (float)sprite.texture.width,
                        sizeY / (float)sprite.texture.height,
                        1f);
                    Log.Info("show: '" + container + "' sized " + sizeX + "x" + sizeY +
                             " by the game (from " + sprite.rect.width + "x" +
                             sprite.rect.height + " pixels)");
                }
                Remember(slotKey, renderer.transform, image, new SavedArt
                {
                    Kind = SavedArt.Native,
                    Source = container,
                    Slot = container,
                    Layer = layer,
                    Transform = transformName,
                });
                return image;
            }
            catch (Exception e)
            {
                Log.Error("Could not show '" + container + "': " + e.Message);
                return null;
            }
        }

        /// <summary>Copy a transform's state onto a display slot's own transform.</summary>
        private static void Apply(RenpyTransformData target, RenpyTransformData source)
        {
            try
            {
                target.HasXPos = true; target.XPos = source.XPos;
                target.HasYPos = true; target.YPos = source.YPos;
                target.HasXAnchor = true; target.XAnchor = source.XAnchor;
                target.HasYAnchor = true; target.YAnchor = source.YAnchor;
                target.HasXZoom = true; target.XZoom = source.XZoom;
                target.HasYZoom = true; target.YZoom = source.YZoom;
                target.HasXOffset = true; target.XOffset = source.XOffset;
                target.HasYOffset = true; target.YOffset = source.YOffset;
                target.HasAlpha = true; target.Alpha = source.Alpha;
                target.HasRotate = true; target.Rotate = source.Rotate;
            }
            catch (Exception e)
            {
                Log.Warn("Could not apply transform state: " + e.Message);
            }
        }

        private static void SafeDestroy(GameObject go)
        {
            try { UnityEngine.Object.Destroy(go); }
            catch (Exception) { /* already gone */ }
        }

        /// <summary>
        /// Pin the container to the neutral centre.
        ///
        /// The game derives the container's position from its Ren'Py transform every
        /// frame. Were placement to use that transform as well it would be added on top
        /// of the renderer offset and the sprite would land at twice the intended
        /// distance, so the container stays neutral and the renderer carries placement.
        /// </summary>
        private static void Place(ActiveImage image)
        {
            var data = image.Transform.Transform;
            data.HasXAnchor = true; data.XAnchor = 0.5f;
            data.HasYAnchor = true; data.YAnchor = 0.5f;
            data.HasXPos = true; data.XPos = 0.5f;
            data.HasYPos = true; data.YPos = 0.5f;
        }

        // ---------------------------------------------------------- bookkeeping

        internal static void Remember(string container, Transform renderer, ActiveImage image,
            SavedArt recipe = null)
        {
            if (string.IsNullOrEmpty(container) || renderer == null) return;
            s_sprites[container] = new SpriteHandle
            {
                Renderer = renderer,
                Image = image,
                Recipe = recipe,
            };
        }

        /// <summary>Say what a picture is, once the caller knows the whole command.</summary>
        internal static void SetRecipe(string container, SavedArt recipe)
        {
            if (string.IsNullOrEmpty(container) || recipe == null) return;
            string key = SlotKey(container,
                string.IsNullOrEmpty(recipe.Layer) ? "master" : recipe.Layer);
            if (s_sprites.TryGetValue(key, out var handle) && handle != null)
            {
                handle.Recipe = recipe;
            }
        }

        /// <summary>
        /// Write down every picture the mod has on screen at this moment.
        ///
        /// A slot whose child has already been destroyed no longer shows mod art: the
        /// game replaced it, hid it, or the picture was never drawn. Those entries are
        /// skipped so that a save does not carry pictures that are not really there.
        /// </summary>
        internal static List<SavedArt> Snapshot()
        {
            var pictures = new List<SavedArt>();

            foreach (var pair in s_sprites)
            {
                var handle = pair.Value;
                if (handle?.Recipe == null) continue;
                if (handle.Renderer == null || handle.Renderer.gameObject == null) continue;
                if (handle.Image == null || handle.Image.Object == null) continue;
                if (!handle.Renderer.gameObject.activeInHierarchy) continue;

                var saved = handle.Recipe.Clone();

                saved.X = handle.Renderer.localPosition.x / ScreenWidth + 0.5f;
                saved.Y = handle.Renderer.localPosition.y / ScreenHeight + 0.5f;
                saved.ScaleX = handle.Renderer.localScale.x;
                saved.ScaleY = handle.Renderer.localScale.y;
                saved.Rotation = -Mathf.DeltaAngle(0f, handle.Renderer.localRotation.eulerAngles.z);
                saved.Alpha = Mathf.Clamp01(handle.Image.Transform.Transform.Alpha);
                saved.ZOrder = handle.Image.GetZOrder();

                // A pose's layers are swapped by a separate command, so they are not
                // part of the recipe that was written when the pose first appeared.
                if (saved.Kind == SavedArt.Pose)
                {
                    for (int i = saved.Layers.Count - 1; i >= 0; i--) saved.Layers.RemoveAt(i);
                    var pose = BaseSprites.Find(saved.Slot, saved.Layer);
                    if (pose != null)
                    {
                        foreach (var layer in pose.Overrides)
                        {
                            saved.Layers.Add(new SavedLayer
                            {
                                Layer = layer.Layer,
                                Asset = layer.Asset,
                            });
                        }
                    }
                }

                pictures.Add(saved);
            }

            return pictures;
        }

        /// <summary>
        /// Draw one picture from a save again.
        ///
        /// The identity comes from the record, but the placement is taken from the
        /// values written down at save time: a sprite may have been moved or faded long
        /// after the command that showed it.
        /// </summary>
        internal static bool Apply(SavedArt saved)
        {
            if (saved == null || string.IsNullOrEmpty(saved.Slot)) return false;

            try
            {
                switch (saved.Kind)
                {
                    case SavedArt.Sprite:
                    {
                        var assets = ModManager.FindAssets(saved.Mod);
                        if (assets == null)
                        {
                            Log.Warn("The mod that supplied '" + saved.Source +
                                     "' is not loaded; its picture was not drawn again.");
                            return false;
                        }

                        if (CustomSpriteLine.ShowSprite(assets, saved.Source, saved.Variant,
                                saved.Slot, saved.At, saved.ZOrder, saved.Layer) == null) return false;
                        break;
                    }

                    case SavedArt.Scene:
                    {
                        var assets = ModManager.FindAssets(saved.Mod);
                        if (assets == null)
                        {
                            Log.Warn("The mod that supplied the background '" + saved.Source +
                                     "' is not loaded; it was not drawn again.");
                            return false;
                        }

                        if (CustomBackgroundLine.ShowScene(assets, saved.Source,
                                saved.Layer) == null) return false;
                        break;
                    }

                    case SavedArt.Native:
                    {
                        // The transform is deliberately not replayed. It may have been a
                        // movement that had already finished, and starting it over would
                        // make the picture jump back to where it began.
                        if (!NativeSpriteLine.ShowNative(saved.Source, saved.Slot, null,
                                saved.ZOrder, saved.Layer)) return false;
                        break;
                    }

                    case SavedArt.Pose:
                    {
                        if (BaseSprites.Show(saved.Source, saved.At, saved.ZOrder,
                                saved.X, saved.Layer) == null)
                        {
                            return false;
                        }

                        foreach (var layer in saved.Layers)
                        {
                            BaseSprites.SetLayer(saved.Source, layer.Layer, layer.Asset, saved.Layer);
                        }
                        break;
                    }

                    default:
                        return false;
                }

                ApplyPose(saved);
                return true;
            }
            catch (Exception e)
            {
                Log.Warn("Could not draw '" + saved.Slot + "' again: " + e.Message);
                return false;
            }
        }

        /// <summary>Put a picture back where the save says it was.</summary>
        private static void ApplyPose(SavedArt saved)
        {
            var renderer = RendererFor(saved.Slot, saved.Layer);
            if (renderer != null)
            {
                renderer.localPosition = new Vector3(
                    (saved.X - 0.5f) * ScreenWidth, (saved.Y - 0.5f) * ScreenHeight, 0f);
                renderer.localScale = new Vector3(saved.ScaleX, saved.ScaleY, 1f);
                renderer.localRotation = Quaternion.Euler(0f, 0f, -saved.Rotation);
            }

            var image = ImageFor(saved.Slot, saved.Layer);
            if (image == null) return;

            image.SetZOrder(saved.ZOrder);

            var data = image.Transform.Transform;
            data.HasAlpha = true;
            data.FixedAlpha = 1f;
            data.Alpha = Mathf.Clamp01(saved.Alpha);
        }

        public static void Forget(string container, string layer = "master")
        {
            string key = ResolveSlotKey(container, layer);
            if (key != null) s_sprites.Remove(key);

            string hostedKey = SlotKey(container, layer);
            if (s_hosted.TryGetValue(hostedKey, out var hosted))
            {
                s_hosted.Remove(hostedKey);
                if (hosted != null)
                {
                    try { UnityEngine.Object.Destroy(hosted); } catch (Exception) { }
                }
            }
        }

        public static void ForgetAll()
        {
            s_sprites.Clear();
            foreach (var instance in new List<GameObject>(s_hosted.Values))
            {
                if (instance == null) continue;
                try { UnityEngine.Object.Destroy(instance); } catch (Exception) { }
            }
            s_hosted.Clear();
        }

        public static bool Has(string container, string layer = "master")
        {
            return ResolveSlotKey(container, layer) != null;
        }

        internal static Transform RendererFor(string container, string layer = "master")
        {
            string key = ResolveSlotKey(container, layer);
            return key != null && s_sprites.TryGetValue(key, out var h) ? h.Renderer : null;
        }

        internal static ActiveImage ImageFor(string container, string layer = "master")
        {
            string key = ResolveSlotKey(container, layer);
            return key != null && s_sprites.TryGetValue(key, out var h) ? h.Image : null;
        }

        // ------------------------------------------------------------- the pose

        /// <summary>Move a sprite, in the game's normalised screen coordinates.</summary>
        public static bool SetPosition(string container, float x, float y)
        {
            var renderer = RendererFor(container);
            if (renderer == null) return false;

            renderer.localPosition =
                new Vector3((x - 0.5f) * ScreenWidth, (y - 0.5f) * ScreenHeight, 0f);
            return true;
        }

        public static bool SetScale(string container, float scale)
        {
            var renderer = RendererFor(container);
            if (renderer == null) return false;

            renderer.localScale = new Vector3(scale, scale, 1f);
            return true;
        }

        public static bool SetRotation(string container, float degrees)
        {
            var renderer = RendererFor(container);
            if (renderer == null) return false;

            renderer.localRotation = Quaternion.Euler(0f, 0f, -degrees);
            return true;
        }

        /// <summary>Fade a sprite. This has to travel through the container transform.</summary>
        public static bool SetAlpha(string container, float alpha)
        {
            var image = ImageFor(container);
            if (image == null) return false;

            var data = image.Transform.Transform;
            data.HasAlpha = true;
            data.FixedAlpha = 1f;
            data.Alpha = Mathf.Clamp01(alpha);
            return true;
        }

        public static bool GetPosition(string container, out float x, out float y)
        {
            x = 0.5f;
            y = 0.5f;
            var renderer = RendererFor(container);
            if (renderer == null) return false;

            x = renderer.localPosition.x / ScreenWidth + 0.5f;
            y = renderer.localPosition.y / ScreenHeight + 0.5f;
            return true;
        }

        public static bool GetScale(string container, out float scale)
        {
            scale = 1f;
            var renderer = RendererFor(container);
            if (renderer == null) return false;

            scale = renderer.localScale.x;
            return true;
        }

        public static bool GetAlpha(string container, out float alpha)
        {
            alpha = 1f;
            var image = ImageFor(container);
            if (image == null) return false;

            alpha = image.Transform.Transform.Alpha;
            return true;
        }

        // --------------------------------------------------------------- audio

        /// <summary>
        /// The game keeps its music players in a private array. Music is driven directly
        /// because <c>MusicSource</c> only ever resolves clips out of a bundle, with no
        /// way to hand it a clip from disk.
        /// </summary>
        internal static AudioSource MusicPlayer(MusicSource music)
        {
            if (music == null) return null;
            try
            {
                if (s_musicSourcesField == null)
                {
                    s_musicSourcesField = typeof(MusicSource)
                        .GetField("musicSources", BindingFlags.Instance | BindingFlags.NonPublic);
                }

                var players = s_musicSourcesField?.GetValue(music) as AudioSource[];
                return players != null && players.Length > 0 ? players[0] : null;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>What a tween is changing.</summary>
    public enum TweenTarget
    {
        Position,
        Scale,
        Rotation,
        Alpha,
    }

    /// <summary>One running change to a sprite.</summary>
    public sealed class SpriteTween
    {
        public string Container;
        public TweenTarget Target;
        public float FromA;
        public float ToA;
        public float FromB;
        public float ToB;
        public float Duration;
        public float Elapsed;
        public int Ticks;

        public void Apply(float t)
        {
            float a = Mathf.Lerp(FromA, ToA, t);
            float b = Mathf.Lerp(FromB, ToB, t);

            switch (Target)
            {
                case TweenTarget.Position: ArtInjector.SetPosition(Container, a, b); break;
                case TweenTarget.Scale: ArtInjector.SetScale(Container, a); break;
                case TweenTarget.Rotation: ArtInjector.SetRotation(Container, a); break;
                case TweenTarget.Alpha: ArtInjector.SetAlpha(Container, a); break;
            }
        }
    }

    /// <summary>
    /// Starts one tween when the script actually reaches it.
    ///
    /// This has to be deferred like every other instruction. A mod is compiled long
    /// before the game has drawn anything, so reading the sprite's current pose at
    /// compile time would always fail: the sprite does not exist yet. A pause of the
    /// same length follows, holding the script while the tween plays.
    /// </summary>
    internal sealed class SpriteTweenLine : Line, IApply
    {
        private readonly string _name;
        private readonly TweenTarget _target;
        private readonly float _toA;

        /// <summary>NaN means "leave this axis where it is".</summary>
        private readonly float _toB;
        private readonly float _seconds;

        public SpriteTweenLine(string name, TweenTarget target, float toA, float toB, float seconds)
        {
            _name = name;
            _target = target;
            _toA = toA;
            _toB = toB;
            _seconds = seconds;
        }

        public void Apply(IContext context)
        {
            if (!ArtInjector.Has(_name))
            {
                Log.Warn("No sprite named '" + _name + "' to animate; ignored.");
                return;
            }

            float fromA = 0f;
            float fromB = _toB;

            switch (_target)
            {
                case TweenTarget.Position:
                    if (!ArtInjector.GetPosition(_name, out fromA, out fromB)) return;
                    break;
                case TweenTarget.Scale:
                    if (!ArtInjector.GetScale(_name, out fromA)) return;
                    break;
                case TweenTarget.Alpha:
                    if (!ArtInjector.GetAlpha(_name, out fromA)) return;
                    break;
                case TweenTarget.Rotation:
                    fromA = 0f;
                    break;
            }

            SpriteAnimator.Start(new SpriteTween
            {
                Container = _name,
                Target = _target,
                FromA = fromA,
                ToA = _toA,
                FromB = fromB,
                ToB = _target == TweenTarget.Position && float.IsNaN(_toB) ? fromB : _toB,
                Duration = _seconds,
            });
        }
    }

    /// <summary>
    /// Drives sprite tweens.
    ///
    /// Time is measured with the unscaled clock so an animation still finishes while the
    /// game is paused: a script that says "move over one second" means one second of
    /// wall-clock, which is what an author expects while testing.
    /// </summary>
    public static class SpriteAnimator
    {
        private static readonly List<SpriteTween> s_tweens = new List<SpriteTween>();

        /// <summary>True while anything is still moving.</summary>
        public static bool Busy => s_tweens.Count > 0;

        public static int ActiveCount => s_tweens.Count;

        /// <summary>Start a change. A zero duration applies the value immediately.</summary>
        public static bool Start(SpriteTween tween)
        {
            if (tween == null || string.IsNullOrEmpty(tween.Container)) return false;

            if (tween.Duration <= 0f)
            {
                tween.Apply(1f);
                return true;
            }

            Log.Info("tween " + tween.Container + " " + tween.Target +
                     " " + tween.FromA + "->" + tween.ToA + " over " + tween.Duration + "s");

            s_tweens.RemoveAll(t => t.Container == tween.Container && t.Target == tween.Target);
            s_tweens.Add(tween);
            return true;
        }

        /// <summary>Apply the end state of any pending change without animating to it.</summary>
        public static void FinishAll()
        {
            foreach (var tween in s_tweens) tween.Apply(1f);
            s_tweens.Clear();
        }

        /// <summary>Advance every running change. Called once per frame.</summary>
        public static void Tick(float deltaTime)
        {
            if (s_tweens.Count == 0) return;

            for (int i = s_tweens.Count - 1; i >= 0; i--)
            {
                var tween = s_tweens[i];
                tween.Elapsed += deltaTime;
                tween.Ticks++;

                float t = tween.Duration <= 0f ? 1f : Mathf.Clamp01(tween.Elapsed / tween.Duration);
                tween.Apply(Ease(t));

                if (t < 1f) continue;

                var renderer = ArtInjector.RendererFor(tween.Container);
                Log.Info("tween done " + tween.Container + " " + tween.Target +
                         " frames=" + tween.Ticks +
                         " pos=" + (renderer == null ? "?" : renderer.localPosition.ToString("F1")) +
                         " scale=" + (renderer == null ? "?" : renderer.localScale.x.ToString("F2")));
                s_tweens.RemoveAt(i);
            }
        }

        /// <summary>
        /// Smooth the pacing so a change eases in and out instead of starting and
        /// stopping abruptly, which is what makes it read as movement rather than a jump.
        /// </summary>
        private static float Ease(float t)
        {
            return t * t * (3f - 2f * t);
        }

        /// <summary>Forget everything, e.g. when mods are reloaded.</summary>
        public static void Reset()
        {
            s_tweens.Clear();
            ArtInjector.ForgetAll();
        }
    }
}
