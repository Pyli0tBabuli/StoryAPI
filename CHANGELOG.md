# Changelog

## 0.2.0 — Draft (proposed)

This draft collects the changes merged after **StoryAPI 0.1.1**. It expands the
Ren'Py-style scripting subset and adds a substantial first slice of Ren'Py image
statement and ATL transform compatibility for Doki Doki Literature Club Plus.

### Scripting language and variables

* Expand top-level `define` and `default` declarations beyond scalar literals:
  support booleans, `None`, arithmetic/comparison/logical scalar expressions,
  nested list and dictionary literals, and the common `Character("Name")`
  declaration.
* Initialize declarations when a mod label begins executing rather than during
  early mod registration. Preserve `define`'s once-only behavior and `default`'s
  behavior of leaving an existing value unchanged.
* Add `while`, `break`, `continue`, and `pass`, plus `for` loops over
  supported collections and generated iterables.
* Add `range()` with one, two, or three integer arguments, including negative
  steps, and support dictionary key, value, and key/value iteration.
* Improve parsing of DDLC+ pose attributes containing adjacent digits and letters
  (for example, `monika 2a`) and of layer replacement asset names containing
  spaces or digits.

### Lists, dictionaries, strings, and iteration

* Add list and dictionary reads, nested indexing, supported dynamic indexes and
  keys, and indexed assignments.
* Add scalar and indexed compound assignments: `+=`, `-=`, `*=`, `/=`,
  and `%=`.
* Add list mutation operations: `append()`, `remove()`, `clear()`,
  `extend()`, `insert()`, `pop()`, and in-place `reverse()`.
* Add dictionary operations: `get()`, `setdefault()`, `copy()`, `pop()`,
  and `update()`, including the documented literal or variable defaults for
  the supported forms.
* Add list queries and ordering: `count()`, `index()` (including start/stop
  bounds and supported variable search values), `sort()`, and `sorted()`
  with reverse ordering. Sorting is limited to supported homogeneous string or
  numeric collections.
* Add the collection helpers `len()`, `sum()`, `min()`, `max()`,
  `any()`, and `all()`, together with `in` and `not in` membership checks.
* Add iteration with `enumerate()`, `zip()`, `reversed()`, and `sorted()`
  over the supported list and dictionary forms, including dictionary keys,
  values, and items.
* Add string iteration, Unicode-aware index reads, length and membership checks.
* Add list and string slice reads with variable and supported arithmetic bounds;
  add list slice assignment and item/slice deletion.

### Ren'Py image statements

* Support inline `with` transitions on `scene`, `show`, and `hide`,
  including explicit `with None` and transition expressions. Preserve standalone
  `with` statements for grouping several image operations into one transition.
* Add `onlayer` to `scene`, `show`, and `hide`, plus `scene onlayer`
  layer clearing. Preserve StoryAPI-managed image layers when saving and loading.
* Add `show ... behind` with one or more image tags, and preserve that ordering
  when restoring saved StoryAPI-managed images.
* Fix the rendering path for built-in DDLC+ character poses used with named
  transforms, so the complete character pose is transformed as a unit.

### ATL transforms and animation

* Add reusable top-level `transform <name>:` declarations usable with
  `show ... at <name>`.
* Add static transform properties: `xalign`, `yalign`, `xpos`, `ypos`,
  `xanchor`, `yanchor`, `xcenter`, `ycenter`, `xoffset`, `yoffset`,
  `alpha`, `zoom`, `xzoom`, `yzoom`, and `rotate`.
* Add sequential timed interpolation with `linear`, `ease`, `easein`,
  and `easeout`, accepting inline targets and indented property blocks.
* Add all 27 built-in Penner easing variants (`ease_*`, `easein_*`, and
  `easeout_*`), including back, bounce, circ, cubic, elastic, expo, quad,
  quart, and quint curves.
* Add ATL pauses using both `pause 0.5` and the numeric statement form `0.5`.
  Add finite and infinite `repeat` timelines, with Ren'Py-style total-run
  counting for `repeat N`.
* Add `subpixel`, `rotate_pad`, `fps`, and `transform_anchor`:
  * `subpixel` controls fractional pixel positioning (default: `False`).
  * `rotate_pad` controls the padded rotation layout bounds (default: `True`).
  * `fps` accepts a positive frame rate or `None` and quantizes the ATL
    timeline's clock; it is an initial transform property, not a timed target.
  * `transform_anchor` controls whether the anchor is resolved on the source
    child and transformed with it (default: `False`).
* Carry transform state through static transforms and timed animation paths,
  including discrete completion-time changes for boolean properties.

### Tests and documentation

* Add the standalone `tools/ScriptTests` regression-test project and a
  GitHub Actions workflow for parser regression tests and checked-in script
  samples.
* Add staged in-game smoke-test scripts under `mods/example/` for the new
  language, collection, image statement, and ATL features.
* Expand the README with syntax examples, supported behavior, and limitations.

### Compatibility notes

StoryAPI still implements a **restricted Ren'Py-style and Python-like subset**,
not the complete Python language or the full Ren'Py engine. Arbitrary Python,
general function calls and attribute access, and unsupported expression forms
are not enabled by these changes. ATL features such as `crop`, `block`,
and `parallel` are still outside the implemented subset. Image layers must
already exist in DDLC+'s runtime; StoryAPI does not create new game layers.
DDLC+'s native `Hide` instruction does not expose a layer field, so hiding
unrelated game-owned images on non-default layers is not generally available;
StoryAPI can remove images it manages on those layers.

### Validation

* The parser regression suite passes **91 tests with 0 failures** on the
  Stage 84 merge commit (`ba0db39a3a08561ce8622c9e96161cbfc3e6b1e7`).
* The workflow validates parser and checked-in script samples; it does not build
  the game-facing runtime against a local DDLC+ installation. Runtime build and
  in-game smoke tests were also performed incrementally against the target game
  during implementation.

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
