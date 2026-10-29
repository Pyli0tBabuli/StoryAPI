# Stage 41 in-game smoke test for enumerate(list) iteration.
# Start this label manually through the game's label/jump tooling.
define stage41_probe_values = ["alpha", "beta", "gamma"]
default stage41_index_sum = 0
default stage41_seen_values = ""

label storyapi_stage41_enumerate_list_smoke:
    $ stage41_index_sum = 0
    $ stage41_seen_values = ""

    for stage41_index, stage41_item in enumerate(stage41_probe_values):
        $ stage41_index_sum += stage41_index
        if stage41_index == 0:
            if stage41_item != "alpha":
                _ "STORYAPI_ENUMERATE_FIRST_VALUE_FAIL"
                return
        elif stage41_index == 1:
            if stage41_item != "beta":
                _ "STORYAPI_ENUMERATE_SECOND_VALUE_FAIL"
                return
        elif stage41_index == 2:
            if stage41_item != "gamma":
                _ "STORYAPI_ENUMERATE_THIRD_VALUE_FAIL"
                return

    if stage41_index_sum != 3:
        _ "STORYAPI_ENUMERATE_INDEX_SUM_FAIL"
        return

    if stage41_probe_values[0] != "alpha" or stage41_probe_values[2] != "gamma":
        _ "STORYAPI_ENUMERATE_SOURCE_MUTATION_FAIL"
        return

    _ "STORYAPI_ENUMERATE_LIST_PASS"
    return
