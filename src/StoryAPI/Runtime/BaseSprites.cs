using System;
using System.Collections.Generic;
using System.Linq;
using RenPyParser;
using RenPyParser.Images;
using RenPyParser.Sprites;
using RenPyParser.Transforms;
using RenpyParser;
using SimpleExpressionEngine;
using UnityEngine;

using StoryAPI.Game;

namespace StoryAPI.Runtime
{
    /// <summary>
    /// A pose the game supplies, held open so that it can be moved and edited.
    ///
    /// A base-game pose is a prefab rather than one picture: the body, the eyes and the
    /// mouth are separate child renderers, and the game assembles them at runtime. Left
    /// to itself the game also rewrites the pose every frame from its own script data, so
    /// any change a mod makes would be thrown away. Taking the pose over means building
    /// it here instead, as the same container a mod sprite uses, which is what lets the
    /// existing animation code drive it without knowing the difference.
    /// </summary>
    public sealed class BasePose
    {
        /// <summary>Name the pose was requested under, used as its slot.</summary>
        public string Name { get; }

        /// <summary>The instantiated pose, parented under the container.</summary>
        public GameObject Root { get; }

        /// <summary>
        /// The pose's layers, keyed by name.
        ///
        /// Names come from the child objects themselves, so what a script writes is what
        /// the prefab actually contains.
        /// </summary>
        public Dictionary<string, SpriteRenderer> Layers { get; } =
            new Dictionary<string, SpriteRenderer>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Layers this pose had swapped, in order. A save has to carry this as well as
        /// the pose itself, or the picture comes back with its original face.
        /// </summary>
        internal List<SavedLayer> Overrides { get; } = new List<SavedLayer>();

        internal void RememberLayer(string layer, string asset)
        {
            for (int i = Overrides.Count - 1; i >= 0; i--)
            {
                if (Overrides[i].Layer == layer) Overrides.RemoveAt(i);
            }

            Overrides.Add(new SavedLayer { Layer = layer, Asset = asset });
        }

        public BasePose(string name, GameObject root)
        {
            Name = name;
            Root = root;
            Index(root);
        }

        private void Index(GameObject go)
        {
            foreach (var renderer in go.GetComponentsInChildren<SpriteRenderer>(includeInactive: true))
            {
                var key = LayerName(renderer);
                if (string.IsNullOrEmpty(key)) continue;
                if (!Layers.ContainsKey(key)) Layers[key] = renderer;
            }
        }

        /// <summary>
        /// Work out what to call a layer.
        ///
        /// The child objects are named after the file they show, which is the most
        /// descriptive name available, so that is preferred. When the name is only the
        /// word "Sprite", the part of the file name after the last folder is used instead,
        /// which is what makes the layers readable in a script.
        /// </summary>
        internal static string LayerName(SpriteRenderer renderer)
        {
            string name = renderer.gameObject.name;

            // Unity marks a copy with "(Clone)". An author should not have to type that,
            // and the pose name is what the layer is expected to be called.
            name = name.Replace("(Clone)", string.Empty).Trim();

            int slash = name.LastIndexOf('/');
            if (slash >= 0 && slash + 1 < name.Length) name = name.Substring(slash + 1);

            name = System.IO.Path.GetFileNameWithoutExtension(name);
            if (string.IsNullOrEmpty(name) || name == "Sprite") return null;
            return name;
        }

        /// <summary>Replace one layer's picture, keeping its placement.</summary>
        public bool SetLayer(string layer, Sprite sprite)
        {
            SpriteRenderer renderer;
            if (string.IsNullOrEmpty(layer) || sprite == null) return false;
            if (!Layers.TryGetValue(layer, out renderer) || renderer == null) return false;

            // Only the picture changes. The placement and size stay as the pose set them,
            // because that is what makes a replacement line up with the body.
            renderer.sprite = sprite;
            return true;
        }
    }

    /// <summary>Poses currently held open, by the name they were shown under.</summary>
    public static class BaseSprites
    {
        private static readonly Dictionary<string, BasePose> s_poses =
            new Dictionary<string, BasePose>(StringComparer.OrdinalIgnoreCase);

        /// <summary>True when the game ships a pose under this name.</summary>
        public static bool IsPose(string name)
        {
            return !string.IsNullOrEmpty(name) && GameBridge.TryLoadPrefab(name, out _);
        }

        public static bool Has(string name)
        {
            return !string.IsNullOrEmpty(name) && s_poses.ContainsKey(name);
        }

        public static BasePose Find(string name)
        {
            BasePose pose;
            return name != null && s_poses.TryGetValue(name, out pose) ? pose : null;
        }

        /// <summary>
        /// Build a pose the game owns and hand it to the animation registry.
        ///
        /// The container is the game's own display slot, so ordering and fading behave the
        /// same way they do for a mod sprite.
        /// </summary>
        public static BasePose Show(string name, string at, int zOrder, float? x)
        {
            if (!GameBridge.TryLoadPrefab(name, out var prefab) || prefab == null)
            {
                Log.Warn("pose: the game has no pose called '" + name + "'");
                return null;
            }

            try
            {
                var show = new Show(string.Empty);
                show.ImageName = name;
                show.Variant = string.Empty;
                show.Layer = "master";
                show.ZOrder = zOrder;
                show.HasZOrder = true;

                var image = ArtInjector.Host(prefab, name, show, x);
                if (image == null || image.Object == null) return null;

                var instance = ArtInjector.Hosted(name);
                if (instance == null) return null;

                // The root keeps the name the game gave it. For a single layer pose the
                // renderer sits on the root, so renaming it would rename the layer too and
                // leave the author with a name they could never have guessed.

                var pose = new BasePose(name, instance);
                s_poses[name] = pose;

                ArtInjector.SetRecipe(name, new SavedArt
                {
                    Kind = SavedArt.Pose,
                    Source = name,
                    Slot = name,
                    At = at,
                    ZOrder = zOrder,
                });

                Log.Info("pose: built '" + name + "' with " + pose.Layers.Count +
                         " layer(s): " + string.Join(", ", pose.Layers.Keys.ToArray()));
                return pose;
            }
            catch (Exception e)
            {
                Log.Error("Could not build pose '" + name + "': " + e.Message);
                return null;
            }
        }

        /// <summary>Swap a layer of a held pose for other art from the game.</summary>
        public static bool SetLayer(string name, string layer, string asset)
        {
            var pose = Find(name);
            if (pose == null)
            {
                Log.Warn("layer: '" + name + "' is not being shown as a pose");
                return false;
            }

            if (!GameBridge.TryLoadSprite(asset, out var sprite) || sprite == null)
            {
                Log.Warn("layer: the game has no art called '" + asset + "'");
                return false;
            }

            if (!pose.SetLayer(layer, sprite))
            {
                Log.Warn("layer: '" + name + "' has no layer called '" + layer +
                         "'. It has: " + string.Join(", ", pose.Layers.Keys.ToArray()));
                return false;
            }

            pose.RememberLayer(layer, asset);
            Log.Info("layer: '" + name + "' layer '" + layer + "' now shows '" + asset + "'");
            return true;
        }

        public static void Clear()
        {
            s_poses.Clear();
        }
    }

    /// <summary>
    /// Shows a pose the game owns, built by the mod rather than by the game.
    ///
    /// Taking a pose over is what makes it animatable: the game normally drives its own
    /// poses from its own script data, and a mod cannot reach that.
    /// </summary>
    internal sealed class PoseLine : Line, IApply
    {
        private readonly string _name;
        private readonly string _at;
        private readonly int _zOrder;

        public PoseLine(string name, string at, int zOrder)
        {
            _name = name;
            _at = at;
            _zOrder = zOrder;
        }

        public void Apply(IContext context)
        {
            Log.Info("story: executing PoseLine");
            BaseSprites.Show(_name, _at, _zOrder, HorizontalPosition(_at));
        }

        internal static float? HorizontalPosition(string at)
        {
            switch (at)
            {
                case "left": return 0.25f;
                case "truecenter":
                case "center": return 0.5f;
                case "right": return 0.75f;
                case null:
                case "offscreenleft": return 0f;
                case "offscreenright": return 1f;
                default: return null;
            }
        }
    }

    /// <summary>Swaps one layer of a held pose, such as the eyes or the mouth.</summary>
    internal sealed class LayerLine : Line, IApply
    {
        private readonly string _pose;
        private readonly string _layer;
        private readonly string _asset;

        public LayerLine(string pose, string layer, string asset)
        {
            _pose = pose;
            _layer = layer;
            _asset = asset;
        }

        public void Apply(IContext context)
        {
            Log.Info("story: executing LayerLine");
            BaseSprites.SetLayer(_pose, _layer, _asset);
        }
    }
}