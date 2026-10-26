# Stage 1 smoke test for declarations, branching and loop control.
# Run this label using StoryAPI's test/jump-target mechanism.
define storyapi_test_character = Character("StoryAPI Test")
define storyapi_initial_points = 3
default storyapi_points = 0
default storyapi_has_key = False

label storyapi_stage1_smoke:
    $ storyapi_points += 1
    while storyapi_points < 3:
        $ storyapi_points += 1

    if storyapi_has_key:
        storyapi_test_character "The key flag is set."
    else:
        storyapi_test_character "The key flag is not set."

    return
