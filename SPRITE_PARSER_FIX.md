# Sprite parser fix

## Fixed: attributes such as `2a` and `1f`

The lexer emits a digit-led pose attribute such as `2a` as two tokens (`2` and `a`).
The parser now rejoins them when they were contiguous in the source. These forms are
accepted by the updated parser:

```renpy
show monika 2a at left
show monika 1f
pose monika 2a at right
```

## Important: `layer` wants a single sprite, not a full pose

`layer monika 1 a monika 5a` is not a valid layer replacement. `monika 5a` names a whole
pose/prefab, while the final argument of `layer` must resolve to an individual Unity
Sprite. Use the specific sprite name recorded for an individual layer in the generated
pose reference. The exact names depend on the assets present in the installed DDLC+ build.

The parser fix does not make whole-pose names into single sprite assets; that would need
a distinct feature to copy one named component from another pose.
