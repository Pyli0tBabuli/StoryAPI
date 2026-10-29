# Stage 42 in-game smoke test for enumerate(list, start).
# Start this label manually through the game's label/jump tooling.
define stage42_probe_values = ["alpha", "beta", "gamma"]
default stage42_index_sum = 0

label storyapi_stage42_enumerate_start_smoke:
    $ stage42_index_sum = 0

    for stage42_index, stage42_item in enumerate(stage42_probe_values, 5):
        $ stage42_index_sum += stage42_index
        if stage42_index == 5:
            if stage42_item != "alpha":
                _ "STORYAPI_ENUMERATE_START_FIRST_VALUE_FAIL"
                return
        elif stage42_index == 6:
            if stage42_item != "beta":
                _ "STORYAPI_ENUMERATE_START_SECOND_VALUE_FAIL"
                return
        elif stage42_index == 7:
            if stage42_item != "gamma":
                _ "STORYAPI_ENUMERATE_START_THIRD_VALUE_FAIL"
                return

    if stage42_index_sum != 18:
        _ "STORYAPI_ENUMERATE_START_INDEX_SUM_FAIL"
        return

    if stage42_probe_values[0] != "alpha" or stage42_probe_values[2] != "gamma":
        _ "STORYAPI_ENUMERATE_START_SOURCE_MUTATION_FAIL"
        return

    _ "STORYAPI_ENUMERATE_START_PASS"
    return
