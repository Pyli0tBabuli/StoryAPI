# ---------------------------------------------------------------------------
# Using the base game's own art from a mod.
#
# Nothing here is declared with `image`: every name below is one the game
# already ships. This label exercises backgrounds, character sprites and music
# that live inside the game's own asset bundles.
# ---------------------------------------------------------------------------

label storyapi_baseart:
    # A background the game ships.
    scene bedroom

    # A character the game ships, with one of its own poses.
    show monika 1 at left

    # The real Monika speaking. The speaker table is part of the script, not the
    # asset bundles, so this works regardless of how the label was entered.
    m "This line is mine, but the room and the sprite are the game's."

    # Music the game ships, resolved by name.
    play "bgm/9.ogg"

    # A base-game background we did not declare either.
    scene bedroom

    m "Second background. No `image` statement was needed for any of this."

    stop music
    hide monika
    return