# ---------------------------------------------------------------------------
# StoryAPI example mod
#
# A story mod is just a folder of .rpy files plus the art they mention.
# Drop this folder into BepInEx/plugins/StoryAPI/Mods/ and press F10 in game.
# ---------------------------------------------------------------------------

# A character of your own. The tag is what you type before a line of dialogue.
character june "June" image june

# Art that ships with the mod. Paths are relative to this folder; images/ and
# audio/ are searched automatically, so `image june = june.png` finds
# either ./june.png or ./images/june.png.
image june = june.png
image modclassroom = classroom.png
audio june_theme = theme.ogg


# The smallest useful label: one line, no art. Useful for checking that a mod
# is loading at all.
label storyapi_smoke:
    _ "StoryAPI smoke test. If you can read this, custom dialogue works."
    june "And this is a character the mod defined."
    return


label storyapi_demo:
    # --- a background from the mod -------------------------------------------
    # `scene` alone swaps instantly; add `with Dissolve(0.5)` for a transition.
    scene modclassroom
    play june_theme fadein 1.0

    show june at left

    # --- dialogue ------------------------------------------------------------
    # `june` is a mod character, `m` is the real Monika. Both use the game's own
    # name box, so custom and original characters share a scene.
    june "Hi. I'm June, and this line comes from a text file."
    m "And I'm the real Monika. Custom and original characters sit side by side."
    _ "Narration is written with an underscore in front of the text."

    # --- game variables ------------------------------------------------------
    $ demo_points = 3
    $ demo_greeted = True

    # --- branching -----------------------------------------------------------
    if demo_points > 5:
        june "You have plenty of points."
    elif demo_points > 0:
        june "You have [demo_points] points. Text in square brackets is interpolated."
    else:
        june "No points at all."

    # --- a choice ------------------------------------------------------------
    menu "What should we do?":
        "Ask about the club":
            m "Ask away."
            june "We write poems. It's very serious."
        "Say nothing":
            _ "You say nothing. The club respects that."
            jump storyapi_farewell
        "Leave":
            jump storyapi_farewell

    # --- reusing variables across labels -------------------------------------
    $ demo_farewell = True

    june "Goodbye for now."
    jump storyapi_farewell


label storyapi_farewell:
    stop music
    hide june
    scene black

    if demo_farewell:
        june "Thanks for reading. Press F10 to reload this script without restarting."
    _ "..."
    return


# ---------------------------------------------------------------------------
# Replacing a line the original game already contains.
#
# `override` needs the label and the numeric line id the game uses internally.
# Find them in the game's script data, then uncomment and fill them in:
#
# override ch1_main = 12345
#     "A completely different line."
# ---------------------------------------------------------------------------