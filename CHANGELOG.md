# Changelog

## 0.1.0 — 2026-10-23

First public release.

* Load `.rpy` mods from `BepInEx/plugins/StoryAPI/Mods/` and register them as normal
  Ren'Py labels.
* Custom characters, images, music and sound, with the game's own dialogue box,
  sprite layers and audio channel.
* Base-game backgrounds, poses, layers, cut scenes and transitions by name.
* `show`/`hide`, `at`, `zorder`, aliases, `pose`, `layer`, `override` and `hook`.
* Tweened `move`/`zoom`/`fade`/`turn` animations with a built-in scheduler.
* `if`/`elif`/`else`, `menu`, variables, interpolation and `$` expressions.
* Saving and loading: mod pictures and music are restored with the save, including
  after restarting the game.
* Hotkeys: F10 reload mods, F11 jump to a label, F12/Shift+F12 save and load the
  test slot.
* Example mod (`Mods/example`) showing the language and the game's own art.
