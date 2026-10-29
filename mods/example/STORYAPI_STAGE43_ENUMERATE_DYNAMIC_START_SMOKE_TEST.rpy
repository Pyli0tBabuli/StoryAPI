# Stage 43 in-game smoke test for a dynamic enumerate(list, start) expression.
# Start this label manually through the game's label/jump tooling.
define stage43_probe_values = ["alpha", "beta", "gamma"]
default stage43_start = 7
default stage43_index_sum = 0

label storyapi_stage43_enumerate_dynamic_start_smoke:
    $ stage43_start = 7
    $ stage43_index_sum = 0

    for stage43_index, stage43_item in enumerate(stage43_probe_values, stage43_start + 1):
        $ stage43_index_sum += stage43_index
        if stage43_index == 8:
            $ stage43_start = 100
            if stage43_item != "alpha":
                _ "STORYAPI_ENUMERATE_DYNAMIC_START_FIRST_VALUE_FAIL"
                return
        elif stage43_index == 9:
            if stage43_item != "beta":
                _ "STORYAPI_ENUMERATE_DYNAMIC_START_SECOND_VALUE_FAIL"
                return
        elif stage43_index == 10:
            if stage43_item != "gamma":
                _ "STORYAPI_ENUMERATE_DYNAMIC_START_THIRD_VALUE_FAIL"
                return

    if stage43_index_sum != 27:
        _ "STORYAPI_ENUMERATE_DYNAMIC_START_SUM_FAIL"
        return

    if stage43_start != 100:
        _ "STORYAPI_ENUMERATE_DYNAMIC_START_NOT_EVALUATED_ONCE_FAIL"
        return

    if stage43_probe_values[0] != "alpha" or stage43_probe_values[2] != "gamma":
        _ "STORYAPI_ENUMERATE_DYNAMIC_START_SOURCE_MUTATION_FAIL"
        return

    _ "STORYAPI_ENUMERATE_DYNAMIC_START_PASS"
    return
