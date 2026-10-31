using System;
using System.Collections.Generic;
using RenPyParser.Images;
using RenPyParser.Sprites;
using RenPyParser.Transforms;
using RenPyParser;
using RenpyParser;
using SimpleExpressionEngine;
using UnityEngine;

namespace StoryAPI.Runtime
{
/// <summary>Shows a mod-supplied sprite. Emitted instead of the game's <c>show</c>.</summary>
    internal sealed class CustomSpriteLine : Line, IApply
    {
        private readonly StoryAssets _assets;
        private readonly string _name;
        private readonly string _variant;
        private readonly string _slot;
        private readonly string _at;
        private readonly int _zOrder;
        private readonly string _layer;

        /// <summary>
        /// Shows a mod-supplied sprite.
        ///
        /// <paramref name="slot"/> is the name the sprite is filed under. When a script
        /// writes <c>show june as j2</c>, the art stays the same but the slot becomes
        /// "j2", so the same picture can be shown twice and then moved or faded one copy
        /// at a time.
        /// </summary>
        private readonly List<string> _behind;

        public CustomSpriteLine(StoryAssets assets, string name, string variant,
            string slot, string at, int zOrder, string layer = "master",
            IList<string> behind = null)
        {
            _assets = assets;
            _name = name;
            _variant = variant;
            _slot = string.IsNullOrEmpty(slot) ? name : slot;
            _at = at;
            _zOrder = zOrder;
            _layer = string.IsNullOrEmpty(layer) ? "master" : layer;
            _behind = behind == null ? new List<string>() : new List<string>(behind);
        }

        public void Apply(IContext context)
        {
            Log.Info("story: executing " + GetType().Name);
            ShowSprite(_assets, _name, _variant, _slot, _at, _zOrder, _layer, _behind);
        }

        /// <summary>
        /// Show a mod sprite. This is the one place that knows how such a picture is
        /// built, so drawing it again after a load is exactly the same work.
        /// </summary>
        internal static ActiveImage ShowSprite(StoryAssets assets, string name, string variant,
            string slot, string at, int zOrder, string layer = "master",
            IList<string> behind = null)
        {
            string showText = "show " + name +
                (string.IsNullOrEmpty(variant) ? "" : " " + variant) +
                (string.IsNullOrEmpty(slot) || slot == name ? "" : " as " + slot) +
                (!string.IsNullOrEmpty(layer) && layer != "master" ? " onlayer " + layer : "") +
                " zorder " + zOrder +
                (behind == null || behind.Count == 0 ? "" : " behind " + string.Join(", ", behind));
            var show = new Show(showText);
            show.ImageName = name;
            show.Variant = variant ?? string.Empty;
            layer = string.IsNullOrEmpty(layer) ? "master" : layer;
            show.Layer = layer;
            show.ZOrder = zOrder;
            show.HasZOrder = true;
            ArtInjector.ApplyBehind(show, behind);

            // The slot is the name the picture is filed under. When a script says
            // "show june as j2", the two differ, and the engine only reads the slot
            // from the "as" fields.
            if (!string.IsNullOrEmpty(slot) && slot != name)
            {
                show.As = slot;
                show.HasSynonym = true;
            }

            var image = ArtInjector.Draw(
                assets.GetImage(name) ?? assets.GetImage(variant),
                slot ?? name, show, null, HorizontalPosition(at));

            if (image == null || image.Object == null) return null;

            image.Object.name = slot ?? name;

            ArtInjector.SetRecipe(slot ?? name, new SavedArt
            {
                Kind = SavedArt.Sprite,
                Mod = assets.ModName,
                Source = name,
                Variant = variant,
                Slot = slot ?? name,
                At = at,
                Layer = layer,
                ZOrder = zOrder,
                Behind = behind == null ? new List<string>() : new List<string>(behind),
            });

            return image;
        }

        private static float? HorizontalPosition(string at)
        {
            switch (at)
            {
                case "left": return 0.25f;
                case "truecenter": return 0.5f;
                case "center": return 0.5f;
                case "right": return 0.75f;
                case null:
                case "offscreenleft": return 0f;
                case "offscreenright": return 1f;
                default: return null;   // an explicit xpos the author gave
            }
        }
    }

    /// <summary>Sets a mod-supplied background. Emitted instead of the game's <c>scene</c>.</summary>
    internal sealed class CustomBackgroundLine : Line, IApply
    {
        private readonly StoryAssets _assets;
        private readonly string _name;
        private readonly string _layer;

        public CustomBackgroundLine(StoryAssets assets, string name, string layer = "master")
        {
            _assets = assets;
            _name = name;
            _layer = string.IsNullOrEmpty(layer) ? "master" : layer;
        }

        public void Apply(IContext context)
        {
            Log.Info("story: executing " + GetType().Name);
            ShowScene(_assets, _name, _layer);
        }

        /// <summary>Set a mod background, in the same way whether it is first shown or drawn again.</summary>
        internal static ActiveImage ShowScene(StoryAssets assets, string name,
            string layer = "master")
        {
            layer = string.IsNullOrEmpty(layer) ? "master" : layer;
            var scene = new Scene(name);
            if (!ArtInjector.TrySetLayer(scene, layer))
            {
                Log.Warn("scene: the DDLC+ Scene instruction cannot select layer '" + layer + "'");
                return null;
            }
            var image = ArtInjector.Draw(assets.GetImage(name), "bg", null, scene);
            if (image == null || image.Object == null) return null;

            image.Object.name = "bg " + name;
            image.SetZOrder(0);

            ArtInjector.SetRecipe("bg", new SavedArt
            {
                Kind = SavedArt.Scene,
                Mod = assets.ModName,
                Source = name,
                Slot = "bg",
                Layer = layer,
                ZOrder = 0,
            });

            return image;
        }
    }

    /// <summary>Plays a mod-supplied track.</summary>
        /// <summary>
    /// Shows a picture the game owns, placed by one of the game's own transforms.
    ///
    /// The game's animation runner does not work inside a mod label, so a named transform
    /// would otherwise be ignored and the picture would land at a default position. The
    /// transform's starting state is only data, though, so it is read straight from the
    /// transform block and applied to the display slot. That puts the picture where the
    /// game would have put it; only the movement between states is lost.
    /// </summary>
    internal sealed class NativeSpriteLine : Line, IApply
    {
        private readonly string _image;
        private readonly string _slot;
        private readonly string _transform;
        private readonly int _zOrder;
        private readonly string _layer;
        private readonly List<string> _behind;

        public NativeSpriteLine(string image, string slot, string transform, int zOrder,
            string layer = "master", IList<string> behind = null)
        {
            _image = image;
            _slot = string.IsNullOrEmpty(slot) ? image : slot;
            _transform = transform;
            _zOrder = zOrder;
            _layer = string.IsNullOrEmpty(layer) ? "master" : layer;
            _behind = behind == null ? new List<string>() : new List<string>(behind);
        }

        public void Apply(IContext context)
        {
            Log.Info("story: executing " + GetType().Name);
            ShowNative(_image, _slot, _transform, _zOrder, _layer, _behind);
        }

        /// <summary>
        /// Show a picture the game owns, placed by one of its transforms. Restoring one
        /// after a load passes no transform, because the movement it describes has
        /// already finished and the saved placement is the truthful one.
        /// </summary>
        internal static bool ShowNative(string image, string slot, string transform, int zOrder,
            string layer = "master", IList<string> behind = null)
        {
            if (!global::StoryAPI.Game.GameBridge.TryLoadNativeSprite(image, out var sprite) || sprite == null)
            {
                Log.Warn("show: the game has no picture called '" + image + "'");
                return false;
            }

            string showText = "show " + image +
                (string.IsNullOrEmpty(slot) || slot == image ? "" : " as " + slot) +
                (!string.IsNullOrEmpty(layer) && layer != "master" ? " onlayer " + layer : "") +
                " zorder " + zOrder +
                (behind == null || behind.Count == 0 ? "" : " behind " + string.Join(", ", behind));
            var show = new Show(showText);
            show.ImageName = image;
            show.Variant = string.Empty;
            layer = string.IsNullOrEmpty(layer) ? "master" : layer;
            show.Layer = layer;
            show.ZOrder = zOrder;
            show.HasZOrder = true;
            ArtInjector.ApplyBehind(show, behind);

            if (!string.IsNullOrEmpty(slot) && slot != image)
            {
                show.As = slot;
                show.HasSynonym = true;
            }

            var active = ArtInjector.DrawNative(sprite, slot ?? image, show, transform);
            if (active == null) return false;

            ArtInjector.SetRecipe(slot ?? image, new SavedArt
            {
                Kind = SavedArt.Native,
                Source = image,
                Slot = slot ?? image,
                Layer = layer,
                Transform = transform,
                ZOrder = zOrder,
                Behind = behind == null ? new List<string>() : new List<string>(behind),
            });

            return true;
        }
    }

    /// <summary>Applies a user-defined static ATL transform to an image already shown.</summary>
    internal sealed class StaticTransformLine : Line, IApply
    {
        private readonly string _container;
        private readonly string _layer;
        private readonly global::StoryAPI.Game.GameBridge.TransformState _state;

        public StaticTransformLine(string container, string layer,
            global::StoryAPI.Game.GameBridge.TransformState state)
        {
            _container = container;
            _layer = string.IsNullOrEmpty(layer) ? "master" : layer;
            _state = state;
        }

        public void Apply(IContext context)
        {
            Log.Info("story: executing StaticTransformLine");
            ArtInjector.ApplyStaticTransform(_container, _layer, _state);
        }
    }

    /// <summary>Starts timed interpolation statements in a custom ATL transform.</summary>
    internal sealed class AtlTransformLine : Line, IApply
    {
        private readonly string _container;
        private readonly string _layer;
        private readonly global::StoryAPI.Game.GameBridge.TransformState _initial;
        private readonly List<AtlScriptSegment> _segments;

        public AtlTransformLine(string container, string layer,
            global::StoryAPI.Game.GameBridge.TransformState initial, IList<AtlScriptSegment> segments)
        {
            _container = container;
            _layer = string.IsNullOrEmpty(layer) ? "master" : layer;
            _initial = initial;
            _segments = segments == null
                ? new List<AtlScriptSegment>() : new List<AtlScriptSegment>(segments);
        }

        public void Apply(IContext context)
        {
            Log.Info("story: executing AtlTransformLine");
            AtlScheduler.StartScript(_container, _layer, _initial, _segments);
        }
    }

    /// <summary>Clears one Ren'Py display layer without showing a new background.</summary>
    internal sealed class LayeredSceneClearLine : Line, IApply
    {
        private readonly string _layer;

        public LayeredSceneClearLine(string layer)
        {
            _layer = string.IsNullOrEmpty(layer) ? "master" : layer;
        }

        public void Apply(IContext context)
        {
            Log.Info("story: executing LayeredSceneClearLine");
            ArtInjector.ClearLayer(_layer);
        }
    }

    /// <summary>Hides a StoryAPI-managed image from one non-default display layer.</summary>
    internal sealed class LayeredHideLine : Line, IApply
    {
        private readonly string _imageTag;
        private readonly string _layer;

        public LayeredHideLine(string imageTag, string layer)
        {
            _imageTag = imageTag;
            _layer = string.IsNullOrEmpty(layer) ? "master" : layer;
        }

        public void Apply(IContext context)
        {
            Log.Info("story: executing LayeredHideLine");
            ArtInjector.HideOwned(_imageTag, _layer);
        }
    }

    internal sealed class CustomAudioLine : Line, IApply
    {
        private readonly StoryAssets _assets;
        private readonly string _channel;
        private readonly string _name;
        private readonly bool _loop;

        public CustomAudioLine(StoryAssets assets, string channel, string name, bool loop)
        {
            _assets = assets;
            _channel = channel;
            _name = name;
            _loop = loop;
        }

        public void Apply(IContext context)
        {
            Log.Info("story: executing " + GetType().Name);
            Play(_assets, _channel, _name, _loop);
        }

        /// <summary>
        /// Start a track of the mod's.
        ///
        /// The track is played on the game's own sound source, but the game only
        /// remembers the tracks it started itself, so a track of the mod's is gone
        /// when a save is loaded. What was started is therefore remembered, and
        /// started again after a load.
        /// </summary>
        internal static void Play(StoryAssets assets, string channel, string name, bool loop)
        {
            var entry = assets?.GetAudio(name);
            if (entry == null)
            {
                Log.Warn("No such track: " + name);
                return;
            }

            if (entry.Clip == null)
            {
                // Loading happens in the background; playing before it finishes would
                // either fail or stall. Either way the script carries on.
                if (!entry.IsDone)
                {
                    entry.BeginLoad();
                    Log.Warn("Track '" + name + "' is still loading; skipped for now.");
                    return;
                }
                Log.Error("Track '" + name + "' failed to load.");
                return;
            }

            var main = Game.GameBridge.Main;
            if (main == null) return;

            try
            {
                AudioSource player = ModAudio.PlayerFor(channel);
                if (player == null)
                {
                    Log.Warn("[StoryAPI] no player available for channel '" + channel + "'.");
                    return;
                }

                player.clip = entry.Clip;
                player.loop = loop;
                player.Play();

                ModAudio.Remember(assets, channel, name, loop);
            }
            catch (Exception e)
            {
                Log.Error("[StoryAPI] could not play '" + name + "': " + e.Message);
            }
        }
    }

    /// <summary>
    /// The tracks of the mod's that are playing, and how to start them again.
    ///
    /// A save records the tracks the game itself started. A track that came with a
    /// mod is played on the game's own sound source, but the game does not know about
    /// it, so it is silent after a load. The mod keeps what it started, so it can
    /// start it again -- including in a new run of the game, because the record is
    /// written with the save.
    /// </summary>
    public static class ModAudio
    {
        private sealed class Track
        {
            public string Mod;
            public string Channel;
            public string Name;
            public bool Loop;
        }

        private static readonly Dictionary<string, Track> s_playing =
            new Dictionary<string, Track>(StringComparer.OrdinalIgnoreCase);

        private static readonly List<SavedMusic> s_pending = new List<SavedMusic>();

        /// <summary>Remember a track the mod has just started.</summary>
        internal static void Remember(StoryAssets assets, string channel, string name, bool loop)
        {
            if (string.IsNullOrEmpty(channel) || string.IsNullOrEmpty(name)) return;

            s_playing[channel] = new Track
            {
                Mod = assets?.ModName ?? string.Empty,
                Channel = channel,
                Name = name,
                Loop = loop,
            };
        }

        /// <summary>The game's own player for a channel.</summary>
        internal static AudioSource PlayerFor(string channel)
        {
            var main = Game.GameBridge.Main;
            if (main == null) return null;

            if (channel == "music" || channel == "music_poem")
            {
                return ArtInjector.MusicPlayer(
                    channel == "music" ? main.musicSource : main.musicPoemSource);
            }

            return main.soundSource != null ? main.soundSource.m_gameAudioSource : null;
        }

        /// <summary>Every mod track that is really playing at this moment.</summary>
        internal static List<SavedMusic> Snapshot()
        {
            var tracks = new List<SavedMusic>();

            foreach (var pair in s_playing)
            {
                var track = pair.Value;
                if (track == null) continue;

                var assets = ModManager.FindAssets(track.Mod);
                var entry = assets?.GetAudio(track.Name);
                var player = PlayerFor(track.Channel);
                if (entry?.Clip == null || player == null || player.clip != entry.Clip) continue;

                tracks.Add(new SavedMusic
                {
                    Mod = track.Mod,
                    Channel = track.Channel,
                    Name = track.Name,
                    Loop = track.Loop,
                });
            }

            return tracks;
        }

        /// <summary>Start a track a save says was playing when it was written.</summary>
        internal static void Restore(SavedMusic saved)
        {
            if (saved == null || string.IsNullOrEmpty(saved.Name)) return;

            var assets = ModManager.FindAssets(saved.Mod);
            if (assets == null)
            {
                Log.Warn("The mod that supplied '" + saved.Name +
                         "' is not loaded; its track was not started again.");
                return;
            }

            var entry = assets.GetAudio(saved.Name);
            if (entry == null)
            {
                Log.Warn("The game has no track called '" + saved.Name + "' any more.");
                return;
            }

            if (entry.Clip == null)
            {
                if (!entry.IsDone)
                {
                    // Decoding an audio file takes a moment, and a save can be loaded
                    // while it is still running. Try again once it has finished.
                    entry.BeginLoad();
                    s_pending.Add(saved);
                    return;
                }

                Log.Error("Track '" + saved.Name + "' failed to load.");
                return;
            }

            CustomAudioLine.Play(assets, saved.Channel, saved.Name, saved.Loop);
            Log.Info("Started the mod's own track again after a load.");
        }

        /// <summary>Start any track whose file was still being decoded at load time.</summary>
        public static void Tick()
        {
            if (s_pending.Count == 0) return;

            for (int i = s_pending.Count - 1; i >= 0; i--)
            {
                SavedMusic saved = s_pending[i];
                var assets = ModManager.FindAssets(saved.Mod);
                var entry = assets?.GetAudio(saved.Name);
                if (entry == null)
                {
                    s_pending.RemoveAt(i);
                    continue;
                }

                if (!entry.IsDone)
                {
                    entry.BeginLoad();
                    continue;
                }

                s_pending.RemoveAt(i);
                if (entry.Clip == null)
                {
                    Log.Error("Track '" + saved.Name + "' failed to load.");
                    continue;
                }

                CustomAudioLine.Play(assets, saved.Channel, saved.Name, saved.Loop);
                Log.Info("Started the mod's own track again after a load.");
            }
        }

        /// <summary>Forget a channel: the script stopped it.</summary>
        internal static void Forget(string channel)
        {
            if (!string.IsNullOrEmpty(channel)) s_playing.Remove(channel);
        }

        /// <summary>Forget everything, e.g. when mods are reloaded.</summary>
        public static void ForgetAll()
        {
            s_playing.Clear();
            s_pending.Clear();
        }
    }


    /// <summary>Stops a channel.</summary>
    internal sealed class CustomAudioStopLine : Line, IApply
    {
        private readonly string _channel;

        public CustomAudioStopLine(string channel)
        {
            _channel = channel;
        }

        public void Apply(IContext context)
        {
            Log.Info("story: executing " + GetType().Name);
            var main = Game.GameBridge.Main;
            if (main == null) return;

            try
            {
                switch (_channel)
                {
                    case "music":
                        main.musicSource?.OnStop(0.5f, true);
                        break;
                    case "music_poem":
                        main.musicPoemSource?.OnStop(0.5f, true);
                        break;
                    default:
                        main.soundSource?.OnStop();
                        break;
                }

                ModAudio.Forget(_channel);
            }
            catch (Exception e)
            {
                Log.Warn("[StoryAPI] could not stop '" + _channel + "': " + e.Message);
            }
        }
    }
}