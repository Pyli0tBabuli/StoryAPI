# ---------------------------------------------------------------------------
# Moving, scaling and fading sprites smoothly.
#
# Animation is a tween the plugin runs itself rather than a game instruction, so a
# sprite can be slid, grown, turned or faded over a stated number of seconds. Each
# statement also pauses the script for that long, which is what makes it read as the
# character moving rather than jumping.
# ---------------------------------------------------------------------------

label storyapi_animate:
    scene modclassroom
    show june

    june "I'm going to slide across the room."
    move june xpos 0.75 over 1.2

    june "Now I'll grow."
    zoom june to 1.4 over 0.8

    june "And shrink back."
    zoom june to 1.0 over 0.8

    june "Slide and drop at the same time."
    move june xpos 0.3 ypos 0.35 over 1.0

    june "Now I'll fade out..."
    fade june to 0.0 over 1.0

    june "...and fade back in."
    fade june to 1.0 over 1.0

    june "One last slide to the middle."
    move june xpos 0.5 ypos 0.5 over 1.0

    june "Done. Nothing moved but the sprite."
    return