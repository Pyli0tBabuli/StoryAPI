using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using RenpyParser;
using UnityEngine;

namespace StoryAPI.Runtime
{
    /// <summary>
    /// One picture the mod had on screen when a save was made.
    ///
    /// The game's own save records the *name* of each picture but not the mod's art,
    /// and it forgets everything about a mod's own display slots once the process
    /// ends. What the mod needs in order to draw its art again is therefore written
    /// down with the save: what kind of picture it was, which asset it came from,
    /// which slot it occupied and where it had been moved to.
    /// </summary>
    internal sealed class SavedArt
    {
        public const string Sprite = "sprite";
        public const string Scene = "scene";
        public const string Native = "native";
        public const string Pose = "pose";

        /// <summary>One of <see cref="Sprite"/>, <see cref="Scene"/>, <see cref="Native"/> or <see cref="Pose"/>.</summary>
        public string Kind;

        /// <summary>The mod the art came from, empty for the game's own art.</summary>
        public string Mod;

        /// <summary>Name the art is registered under, or the game's own name for native art and poses.</summary>
        public string Source;

        /// <summary>Pose of a mod sprite, such as "happy".</summary>
        public string Variant;

        /// <summary>The display slot the picture occupies.</summary>
        public string Slot;

        /// <summary>The transform a native picture was shown with.</summary>
        public string Transform;

        /// <summary>The position word the script used, such as "left".</summary>
        public string At;

        public int ZOrder;

        public float X = 0.5f;
        public float Y = 0.5f;
        public float ScaleX = 1f;
        public float ScaleY = 1f;
        public float Rotation;
        public float Alpha = 1f;

        /// <summary>Layers of a game pose that were swapped, in the order they were swapped.</summary>
        public List<SavedLayer> Layers = new List<SavedLayer>();

        public SavedArt Clone()
        {
            var copy = new SavedArt
            {
                Kind = Kind,
                Mod = Mod,
                Source = Source,
                Variant = Variant,
                Slot = Slot,
                Transform = Transform,
                At = At,
                ZOrder = ZOrder,
                X = X,
                Y = Y,
                ScaleX = ScaleX,
                ScaleY = ScaleY,
                Rotation = Rotation,
                Alpha = Alpha,
            };

            if (Layers != null)
            {
                foreach (var layer in Layers)
                {
                    copy.Layers.Add(new SavedLayer { Layer = layer.Layer, Asset = layer.Asset });
                }
            }

            return copy;
        }
    }

    /// <summary>One layer of a game pose that the script replaced.</summary>
    internal sealed class SavedLayer
    {
        public string Layer;
        public string Asset;
    }

    /// <summary>One track the mod started and that was still playing when the save was made.</summary>
    internal sealed class SavedMusic
    {
        public string Mod;
        public string Channel;
        public string Name;
        public bool Loop;
    }

    /// <summary>Everything the mod had on screen at the moment a save was written.</summary>
    internal sealed class SavedScreen
    {
        public readonly List<SavedArt> Art = new List<SavedArt>();
        public readonly List<SavedMusic> Music = new List<SavedMusic>();
    }

    /// <summary>
    /// Reads and writes the mod's screen state in a save file.
    ///
    /// The record is appended after everything the game writes, so the game never reads
    /// it, but it still travels with the save when the file is copied or moved. A small
    /// block at the end carries its own length and marker, which is enough to find it
    /// again without understanding the game's format. A separate text file beside the
    /// save is kept as a copy for saves the game wrote before this block existed.
    /// </summary>
    internal static class SaveStateFile
    {
        private const string Header = "storyapi-save 1";
        private const char Separator = '\t';

        /// <summary>
        /// Marks the mod's block at the very end of a save file. Sixteen bytes, so the
        /// block can be found from the end of the file without understanding the game's
        /// own data at all.
        /// </summary>
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("STORYAPI-SAVE-1!");

        /// <summary>Where the game keeps the save for a slot. Slot -1 is the lifecycle save.</summary>
        public static string SavePathFor(int slot)
        {
            string name = slot == -1 ? "save_lifecycle.sav" : "save_" + slot + ".sav";
            try
            {
                return Path.Combine(Renpy.FileSystem.PersistentDataPath, name);
            }
            catch
            {
                return Path.Combine(Application.persistentDataPath, name);
            }
        }

        /// <summary>Where the separate copy of the record lives, beside the save.</summary>
        public static string PathFor(int slot)
        {
            return SavePathFor(slot) + ".storyapi";
        }

        /// <summary>The record as text, ready to go into a save or a file.</summary>
        public static string Serialize(SavedScreen screen)
        {
            if (screen == null) return null;

            var text = new StringBuilder();
            text.Append(Header).Append('\n');

            foreach (var art in screen.Art)
            {
                text.Append(Join("art", art.Kind, art.Mod, art.Source, art.Variant, art.Slot,
                    art.Transform, art.At, art.ZOrder.ToString(CultureInfo.InvariantCulture),
                    Number(art.X), Number(art.Y), Number(art.ScaleX), Number(art.ScaleY),
                    Number(art.Rotation), Number(art.Alpha))).Append('\n');

                foreach (var layer in art.Layers)
                {
                    text.Append(Join("layer", art.Slot, layer.Layer, layer.Asset)).Append('\n');
                }
            }

            foreach (var music in screen.Music)
            {
                text.Append(Join("music", music.Mod, music.Channel, music.Name,
                    music.Loop ? "1" : "0")).Append('\n');
            }

            return text.ToString();
        }

        /// <summary>Read a record written by <see cref="Serialize"/>.</summary>
        public static SavedScreen Parse(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;

            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            if (lines.Length == 0 || !lines[0].StartsWith("storyapi-save")) return null;

            var screen = new SavedScreen();
            SavedArt current = null;

            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrEmpty(lines[i])) continue;
                string[] fields = Split(lines[i]);
                if (fields.Length == 0) continue;

                switch (fields[0])
                {
                    case "art" when fields.Length >= 15:
                        current = new SavedArt
                        {
                            Kind = At(fields, 1),
                            Mod = At(fields, 2),
                            Source = At(fields, 3),
                            Variant = At(fields, 4),
                            Slot = At(fields, 5),
                            Transform = At(fields, 6),
                            At = At(fields, 7),
                            ZOrder = Int(fields, 8),
                            X = Float(fields, 9, 0.5f),
                            Y = Float(fields, 10, 0.5f),
                            ScaleX = Float(fields, 11, 1f),
                            ScaleY = Float(fields, 12, 1f),
                            Rotation = Float(fields, 13, 0f),
                            Alpha = Float(fields, 14, 1f),
                        };
                        screen.Art.Add(current);
                        break;

                    case "layer" when fields.Length >= 4:
                        if (current != null)
                        {
                            current.Layers.Add(new SavedLayer
                            {
                                Layer = At(fields, 2),
                                Asset = At(fields, 3),
                            });
                        }
                        break;

                    case "music" when fields.Length >= 5:
                        screen.Music.Add(new SavedMusic
                        {
                            Mod = At(fields, 1),
                            Channel = At(fields, 2),
                            Name = At(fields, 3),
                            Loop = At(fields, 4) == "1",
                        });
                        break;
                }
            }

            return screen;
        }

        /// <summary>Write the separate copy beside a save.</summary>
        public static void Write(int slot, SavedScreen screen)
        {
            string text = Serialize(screen);
            if (text == null) return;

            string path = PathFor(slot);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, text, new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Log.Warn("Could not write the mod's screen state for slot " + slot + ": " + e.Message);
            }
        }

        public static SavedScreen Read(int slot)
        {
            return Parse(ReadText(slot));
        }

        /// <summary>The separate copy as text, or null when there is none.</summary>
        public static string ReadText(int slot)
        {
            string path = PathFor(slot);
            if (!File.Exists(path)) return null;

            try
            {
                return File.ReadAllText(path);
            }
            catch (Exception e)
            {
                Log.Warn("Could not read the mod's screen state for slot " + slot + ": " + e.Message);
                return null;
            }
        }

        public static void Delete(int slot)
        {
            try
            {
                string path = PathFor(slot);
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception e)
            {
                Log.Warn("Could not delete the mod's screen state for slot " + slot + ": " + e.Message);
            }
        }

        // ------------------------------------------------- the block inside a save

        /// <summary>
        /// The bytes appended to a save: the record, its length, and the marker.
        ///
        /// The game writes one compressed stream and never looks past its own last
        /// field, so bytes after it are ignored on load. Putting the marker last means
        /// the block can be found from the end of the file, with no need to understand
        /// the game's own data.
        /// </summary>
        public static byte[] MakeBlock(string text)
        {
            byte[] payload = Encoding.UTF8.GetBytes(text ?? string.Empty);
            var block = new byte[payload.Length + 4 + Magic.Length];

            Buffer.BlockCopy(payload, 0, block, 0, payload.Length);
            Buffer.BlockCopy(BitConverter.GetBytes(payload.Length), 0, block, payload.Length, 4);
            Buffer.BlockCopy(Magic, 0, block, payload.Length + 4, Magic.Length);
            return block;
        }

        /// <summary>
        /// Read the record from inside a save file. Null for a save written without one:
        /// an older save, or a slot that was written before the mod was installed.
        /// </summary>
        public static string ReadBlock(int slot)
        {
            string path = SavePathFor(slot);

            try
            {
                if (!File.Exists(path)) return null;

                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                           FileShare.ReadWrite))
                {
                    if (stream.Length < 4 + Magic.Length) return null;

                    stream.Seek(-Magic.Length, SeekOrigin.End);
                    var marker = new byte[Magic.Length];
                    if (ReadExact(stream, marker, marker.Length) != marker.Length) return null;
                    if (!SameBytes(marker, Magic)) return null;

                    stream.Seek(-(Magic.Length + 4), SeekOrigin.End);
                    var lengthBytes = new byte[4];
                    if (ReadExact(stream, lengthBytes, 4) != 4) return null;

                    int length = BitConverter.ToInt32(lengthBytes, 0);
                    if (length < 0 || length > stream.Length - 4 - Magic.Length) return null;

                    stream.Seek(-(Magic.Length + 4 + length), SeekOrigin.End);
                    var payload = new byte[length];
                    if (ReadExact(stream, payload, length) != length) return null;

                    return Encoding.UTF8.GetString(payload);
                }
            }
            catch (Exception e)
            {
                Log.Warn("Could not read the mod's screen state from the save: " + e.Message);
                return null;
            }
        }

        private static int ReadExact(Stream stream, byte[] buffer, int count)
        {
            int total = 0;
            while (total < count)
            {
                int read = stream.Read(buffer, total, count - total);
                if (read <= 0) break;
                total += read;
            }

            return total;
        }

        private static bool SameBytes(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }

            return true;
        }

        private static string Join(params string[] fields)
        {
            for (int i = 0; i < fields.Length; i++) fields[i] = Escape(fields[i]);
            return string.Join(Separator.ToString(), fields);
        }

        private static string[] Split(string line)
        {
            string[] parts = line.Split(Separator);
            for (int i = 0; i < parts.Length; i++) parts[i] = Unescape(parts[i]);
            return parts;
        }

        private static string At(string[] fields, int index)
        {
            return index < fields.Length ? fields[index] : string.Empty;
        }

        private static int Int(string[] fields, int index)
        {
            return int.TryParse(At(fields, index), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int value) ? value : 0;
        }

        private static float Float(string[] fields, int index, float fallback)
        {
            return float.TryParse(At(fields, index), NumberStyles.Float,
                CultureInfo.InvariantCulture, out float value) ? value : fallback;
        }

        private static string Number(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Keep a field on one line. Names come from scripts and folders, and a tab or a
        /// newline in one of them must not be able to break the file apart.
        /// </summary>
        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            return value
                .Replace("\\", "\\\\")
                .Replace("\t", "\\t")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r");
        }

        private static string Unescape(string value)
        {
            if (string.IsNullOrEmpty(value) || value.IndexOf('\\') < 0) return value;

            var text = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c != '\\' || i + 1 >= value.Length)
                {
                    text.Append(c);
                    continue;
                }

                i++;
                switch (value[i])
                {
                    case 't': text.Append('\t'); break;
                    case 'n': text.Append('\n'); break;
                    case 'r': text.Append('\r'); break;
                    default: text.Append(value[i]); break;
                }
            }

            return text.ToString();
        }
    }
}
