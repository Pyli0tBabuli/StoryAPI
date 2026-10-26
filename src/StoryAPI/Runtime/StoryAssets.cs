using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace StoryAPI.Runtime
{
    /// <summary>A mod-provided image and the file it was loaded from.</summary>
    public sealed class StoryImage
    {
        public string Name { get; }
        public string Path { get; }
        public Sprite Sprite { get; private set; }

        /// <summary>The mod this picture belongs to, used to find it again after a restart.</summary>
        public string Mod { get; internal set; }

        public StoryImage(string name, string path)
        {
            Name = name;
            Path = path;
        }

        public bool TryLoad()
        {
            if (Sprite != null) return true;

            try
            {
                byte[] bytes = File.ReadAllBytes(Path);
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                texture.LoadImage(bytes);
                texture.name = "StoryAPI_" + Name;

                // Pixels-per-unit has to be 1. The game does all of its layout in raw
                // pixels against a fixed 1280x720 reference: it measures an image with
                // sprite.rect.size, which is in pixels, and offsets it by multiples of
                // 1280 and 720. Any other value would scale mod art differently from
                // the game's own.
                Sprite = Sprite.Create(
                    texture,
                    new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f),
                    1f);
                Sprite.name = "StoryAPI_" + Name;
                return true;
            }
            catch (Exception e)
            {
                Log.Error("Could not load image '" + Path + "': " + e.Message);
                return false;
            }
        }
    }

    /// <summary>A mod-provided audio file.</summary>
    public sealed class StoryAudio
    {
        private UnityWebRequest s_request;

        public string Name { get; }
        public string Path { get; }
        public AudioClip Clip { get; private set; }
        public bool Failed { get; private set; }

        public StoryAudio(string name, string path)
        {
            Name = name;
            Path = path;
        }

        /// <summary>
        /// Start fetching the clip. This is deliberately non-blocking.
        ///
        /// Unity completes web requests on its own update loop, so waiting for one by
        /// spinning on the main thread deadlocks the game outright. The request is
        /// started here and collected later by <see cref="Poll"/>.
        /// </summary>
        public void BeginLoad()
        {
            if (Clip != null || s_request != null || Failed) return;

            if (!File.Exists(Path))
            {
                Failed = true;
                Log.Error("Audio file missing: " + Path);
                return;
            }

            try
            {
                s_request = UnityWebRequestMultimedia.GetAudioClip(new Uri(Path), AudioTypeFor(Path));
                s_request.SendWebRequest();
            }
            catch (Exception e)
            {
                Failed = true;
                Log.Error("Could not start loading audio '" + Path + "': " + e.Message);
            }
        }

        /// <summary>Collect the clip once Unity has finished decoding it.</summary>
        public bool Poll()
        {
            if (Clip != null || s_request == null) return Clip != null;
            if (!s_request.isDone) return false;

            try
            {
                if (s_request.isNetworkError || s_request.isHttpError)
                {
                    Failed = true;
                    Log.Error("Could not load audio '" + Path + "': " + s_request.error);
                    return false;
                }

                Clip = DownloadHandlerAudioClip.GetContent(s_request);
                if (Clip != null) Clip.name = "StoryAPI_" + Name;
                else Failed = true;
            }
            catch (Exception e)
            {
                Failed = true;
                Log.Error("Could not decode audio '" + Path + "': " + e.Message);
            }
            finally
            {
                s_request.Dispose();
                s_request = null;
            }

            return Clip != null;
        }

        /// <summary>Nothing left to wait for, whether it worked or not.</summary>
        public bool IsDone => Clip != null || Failed;

        private static AudioType AudioTypeFor(string path)
        {
            switch (Path2Extension(path))
            {
                case ".ogg": return AudioType.OGGVORBIS;
                case ".wav": return AudioType.WAV;
                case ".mp3": return AudioType.MPEG;
                default: return AudioType.UNKNOWN;
            }
        }

        private static string Path2Extension(string path)
        {
            return System.IO.Path.GetExtension(path).ToLowerInvariant();
        }
    }

    /// <summary>
    /// Every asset a mod brings with it, keyed by the name its script refers to.
    /// </summary>
    public sealed class StoryAssets
    {
        private readonly Dictionary<string, StoryImage> _images = new Dictionary<string, StoryImage>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, StoryAudio> _audio = new Dictionary<string, StoryAudio>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The mod these assets came from, set once the folder has been read.</summary>
        public string ModName { get; internal set; }

        public IEnumerable<string> ImageNames => _images.Keys;
        public IEnumerable<string> AudioNames => _audio.Keys;
        public int ImageCount => _images.Count;
        public int AudioCount => _audio.Count;

        public void AddImage(string name, string path)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path)) return;
            _images[name] = new StoryImage(name, path) { Mod = ModName };
        }

        public void AddAudio(string name, string path)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path)) return;
            _audio[name] = new StoryAudio(name, path);
        }

        public void Clear()
        {
            _images.Clear();
            _audio.Clear();
        }

        /// <summary>Begin loading every declared track. Called once mods are registered.</summary>
        public void BeginAudioLoads()
        {
            foreach (var entry in _audio.Values) entry.BeginLoad();
        }

        /// <summary>
        /// Advance pending audio decodes. Safe to call every frame; this is what keeps
        /// the game responsive while a track loads.
        /// </summary>
        public void Pump()
        {
            foreach (var entry in _audio.Values)
            {
                if (entry.IsDone) continue;
                if (entry.Poll())
                {
                    Log.Info("Loaded track '" + entry.Name + "'.");
                }
            }
        }

        /// <summary>True when the script is naming an image this mod supplies.</summary>
        public bool IsCustomImage(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;

            // `show june happy` names a sprite plus a pose; either part may be what
            // the mod registered, so both spellings are accepted.
            if (_images.ContainsKey(name)) return true;

            int space = name.IndexOf(' ');
            if (space <= 0) return false;

            string head = name.Substring(0, space);
            string tail = name.Substring(space + 1);

            return _images.ContainsKey(head) || _images.ContainsKey(tail) ||
                   _images.ContainsKey(head + " " + tail);
        }

        public bool IsCustomAudio(string name)
        {
            return !string.IsNullOrEmpty(name) && _audio.ContainsKey(name);
        }

        /// <summary>Resolves a possibly-posed name to the registered image and pose.</summary>
        public bool ResolveImage(string name, out StoryImage image, out string variant)
        {
            image = null;
            variant = null;
            if (string.IsNullOrEmpty(name)) return false;

            if (_images.TryGetValue(name, out image)) return true;

            int space = name.IndexOf(' ');
            if (space > 0)
            {
                string head = name.Substring(0, space);
                string tail = name.Substring(space + 1);
                if (_images.TryGetValue(head, out image))
                {
                    variant = tail;
                    return true;
                }
                if (_images.TryGetValue(tail, out image)) return true;
            }

            return false;
        }

        public StoryImage GetImage(string name)
        {
            return name != null && _images.TryGetValue(name, out var image) ? image : null;
        }

        public StoryAudio GetAudio(string name)
        {
            return name != null && _audio.TryGetValue(name, out var audio) ? audio : null;
        }
    }
}