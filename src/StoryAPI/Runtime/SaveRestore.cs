using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using RenpyParser;

namespace StoryAPI.Runtime
{
    /// <summary>
    /// Keeps the mod's own pictures and music with the game's saves.
    ///
    /// A save game records the *name* of every picture on screen rather than the
    /// picture itself, and it records only the tracks the game started itself. So when
    /// a save is loaded, the game builds the display slots afresh and fills them with
    /// its own art, and a track that came with a mod is silent. A save also outlives
    /// the process, so remembering the screen only in memory would not help after a
    /// restart.
    ///
    /// The mod therefore writes what it has on screen at the moment the save is
    /// written, as a block appended after the game's own data. The game ignores those
    /// bytes, so the save stays playable, and the record travels with the file when it
    /// is copied or moved. A text copy is also kept beside the save, read only for
    /// saves written before the block existed.
    ///
    /// The game's lifecycle save (slot -1) is left alone. It is written on the way out
    /// and read before the main menu, where mod art has no business appearing.
    /// </summary>
    internal static class SaveRestore
    {
        private const string Id = "opencode.storyapi";

        /// <summary>What the mod had on screen, read while a save is being loaded.</summary>
        private static List<SavedArt> s_pendingArt;
        private static List<SavedMusic> s_pendingMusic;

        /// <summary>
        /// The record taken when a save was asked for, waiting for the game to write
        /// its file. The snapshot has to be taken at that moment, while the screen is
        /// still as the player sees it; the file itself is written a moment later.
        /// </summary>
        private static string s_nextRecord;

        /// <summary>Watch the game for saves being written, loaded and deleted.</summary>
        public static void Install()
        {
            try
            {
                Patch(typeof(SaveManager), "Save",
                    new[] { typeof(int), typeof(bool) }, "BeforeSave");
                Patch(typeof(SaveManager), "Load",
                    new[] { typeof(int), typeof(bool) }, "BeforeLoad");
                Patch(typeof(SaveManager), "Delete",
                    new[] { typeof(int) }, "BeforeDelete");

                // The record is written into the game's own save file. The game writes
                // its stream through the one it is given, so that stream is wrapped for
                // the length of the write and the block is added right after it.
                var save = AccessTools.Method(typeof(RenpyMainBase), "Save",
                    new[] { typeof(Stream), typeof(IFormatter) });
                if (save == null)
                {
                    Log.Warn("The game's save could not be found; what the mod has on " +
                             "screen will not be written with a save.");
                }
                else
                {
                    var prefix = typeof(SaveRestore).GetMethod("BeforeGameSave",
                        BindingFlags.Static | BindingFlags.NonPublic);
                    if (prefix != null)
                    {
                        new Harmony(Id).Patch(save, prefix: new HarmonyMethod(prefix));
                    }
                }

                // The game's load is where the screen is rebuilt, so this is the moment
                // the mod's own pictures can be put back on top of it. Every other part
                // of loading, including the jump into "after_load", has already run.
                var target = AccessTools.Method(typeof(RenpyMainBase), "Load",
                    new[] { typeof(Stream), typeof(IFormatter), typeof(bool) });
                if (target == null)
                {
                    Log.Warn("The game's load could not be found; the mod's pictures will " +
                             "not come back after a save is loaded.");
                    return;
                }

                var postfix = typeof(SaveRestore).GetMethod("AfterLoad",
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (postfix == null) return;

                new Harmony(Id).Patch(target, postfix: new HarmonyMethod(postfix));
            }
            catch (Exception e)
            {
                Log.Warn("Could not watch save games: " + e.Message);
            }
        }

        private static void Patch(Type type, string name, Type[] arguments, string prefixName)
        {
            try
            {
                var target = AccessTools.Method(type, name, arguments);
                if (target == null)
                {
                    Log.Warn("The game's " + type.Name + "." + name + " could not be found.");
                    return;
                }

                var prefix = typeof(SaveRestore).GetMethod(prefixName,
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (prefix == null) return;

                new Harmony(Id).Patch(target, prefix: new HarmonyMethod(prefix));
            }
            catch (Exception e)
            {
                Log.Warn("Could not watch " + type.Name + "." + name + ": " + e.Message);
            }
        }

        /// <summary>
        /// A save is about to be written: take down what is on screen at this moment.
        ///
        /// The snapshot belongs here rather than at file-writing time: the game takes
        /// its save picture afterwards and the script may have moved on by then, so the
        /// only truthful moment is the one the player asked to save.
        /// </summary>
        private static void BeforeSave(int slot)
        {
            s_nextRecord = null;
            if (slot < 0) return;

            try
            {
                var screen = new SavedScreen();
                screen.Art.AddRange(ArtInjector.Snapshot());
                screen.Music.AddRange(ModAudio.Snapshot());

                string record = SaveStateFile.Serialize(screen);
                s_nextRecord = record;

                // A second copy beside the save costs nothing and is read when a save
                // written by an older version has no block of its own.
                SaveStateFile.Write(slot, screen);

                Log.Info("Saving " + screen.Art.Count + " of the mod's picture(s) and " +
                         screen.Music.Count + " track(s) with the save.");
            }
            catch (Exception e)
            {
                Log.Warn("Could not take down the mod's screen state: " + e.Message);
            }
        }

        /// <summary>
        /// The game is about to serialise its save: give it a stream that adds the
        /// mod's block after the game's own data.
        /// </summary>
        private static void BeforeGameSave(ref Stream fileStream)
        {
            string record = s_nextRecord;
            s_nextRecord = null;

            if (record == null || fileStream == null) return;

            try
            {
                fileStream = new BlockStream(fileStream, SaveStateFile.MakeBlock(record));
            }
            catch (Exception e)
            {
                Log.Warn("Could not write the mod's screen state into the save: " + e.Message);
            }
        }

        /// <summary>
        /// A save is about to be read: read what the mod had on screen then. The block
        /// inside the save is the real record; the copy beside it is only for saves
        /// written by an older version.
        /// </summary>
        private static void BeforeLoad(int slot)
        {
            s_pendingArt = null;
            s_pendingMusic = null;
            if (slot < 0) return;

            try
            {
                string record = SaveStateFile.ReadBlock(slot);
                string source = "in the save";
                if (string.IsNullOrEmpty(record))
                {
                    record = SaveStateFile.ReadText(slot);
                    source = "beside the save";
                }

                var screen = SaveStateFile.Parse(record);
                if (screen == null) return;

                s_pendingArt = screen.Art;
                s_pendingMusic = screen.Music;
                Log.Info("The save has " + screen.Art.Count + " of the mod's picture(s) and " +
                         screen.Music.Count + " track(s) to put back (" + source + ").");
            }
            catch (Exception e)
            {
                Log.Warn("Could not read the mod's screen state: " + e.Message);
            }
        }

        /// <summary>A save is about to be deleted: its record has no further use.</summary>
        private static void BeforeDelete(int slot)
        {
            if (slot < 0) return;
            SaveStateFile.Delete(slot);
        }

        /// <summary>
        /// The game has loaded a save: put the mod's own pictures and music back.
        ///
        /// The game has just destroyed every display slot and built them again from the
        /// save, so anything the mod remembered is stale; it is dropped first and then
        /// rebuilt from the record written with the save.
        /// </summary>
        private static void AfterLoad()
        {
            var art = s_pendingArt;
            var music = s_pendingMusic;
            s_pendingArt = null;
            s_pendingMusic = null;

            try
            {
                ArtInjector.ForgetAll();
                BaseSprites.Clear();
                ModAudio.ForgetAll();

                if (art == null && music == null) return;

                int pictures = 0;
                if (art != null)
                {
                    foreach (var saved in art)
                    {
                        if (ArtInjector.Apply(saved)) pictures++;
                    }
                }

                if (music != null)
                {
                    foreach (var saved in music) ModAudio.Restore(saved);
                }

                Log.Info("Put back " + pictures + " of the mod's picture(s) and " +
                         (music?.Count ?? 0) + " track(s) after a load.");
            }
            catch (Exception e)
            {
                Log.Warn("Could not put the mod's pictures back after a load: " + e.Message);
            }
        }

        /// <summary>
        /// Passes every write through to the game's file, then adds the mod's block once.
        ///
        /// The game saves by writing one compressed buffer; the block simply follows it.
        /// The file is cut at that point, because the game opens saves without truncating
        /// them, and a longer file from before would otherwise leave bytes after the block.
        /// </summary>
        private sealed class BlockStream : Stream
        {
            private readonly Stream _inner;
            private readonly byte[] _block;
            private bool _written;

            public BlockStream(Stream inner, byte[] block)
            {
                _inner = inner;
                _block = block;
            }

            public override bool CanRead => _inner.CanRead;
            public override bool CanSeek => _inner.CanSeek;
            public override bool CanWrite => _inner.CanWrite;
            public override long Length => _inner.Length;

            public override long Position
            {
                get => _inner.Position;
                set => _inner.Position = value;
            }

            public override void Flush() => _inner.Flush();

            public override int Read(byte[] buffer, int offset, int count) =>
                _inner.Read(buffer, offset, count);

            public override long Seek(long offset, SeekOrigin origin) =>
                _inner.Seek(offset, origin);

            public override void SetLength(long value) => _inner.SetLength(value);

            public override void Write(byte[] buffer, int offset, int count)
            {
                _inner.Write(buffer, offset, count);
                WriteBlock();
            }

            public override async Task WriteAsync(byte[] buffer, int offset, int count,
                CancellationToken cancellationToken)
            {
                await _inner.WriteAsync(buffer, offset, count, cancellationToken);
                WriteBlock();
            }

            private void WriteBlock()
            {
                if (_written) return;
                _written = true;

                try
                {
                    _inner.Write(_block, 0, _block.Length);
                    _inner.Flush();
                    if (_inner.CanSeek) _inner.SetLength(_inner.Position);
                }
                catch (Exception e)
                {
                    Log.Warn("Could not add the mod's screen state to the save: " + e.Message);
                }
            }

            // The game owns the file and closes it itself; this wrapper only decorates
            // the write, so it must not close anything.
            protected override void Dispose(bool disposing)
            {
                if (disposing) _inner.Flush();
                base.Dispose(disposing);
            }
        }
    }
}
