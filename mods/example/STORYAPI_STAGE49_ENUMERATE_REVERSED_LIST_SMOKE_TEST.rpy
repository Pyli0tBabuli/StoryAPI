# Stage 49 in-game smoke test for enumerate(reversed(list), start).
# Start this label manually through the game's label/jump tooling.
define stage49_probe_names = ["alpha", "beta", "gamma"]
define stage49_probe_start = 4
default stage49_reverse_count = 0
default stage49_index_sum = 0

label storyapi_stage49_enumerate_reversed_smoke:
    $ stage49_reverse_count = 0
    $ stage49_index_sum = 0

    for stage49_index, stage49_name in enumerate(reversed(stage49_probe_names), stage49_probe_start + 1):
        $ stage49_reverse_count += 1
        $ stage49_index_sum += stage49_index
        if stage49_reverse_count == 1:
            if stage49_index != 5 or stage49_name != "gamma":
                _ "STORYAPI_ENUMERATE_REVERSED_FIRST_FAIL"
                return
        elif stage49_reverse_count == 2:
            if stage49_index != 6 or stage49_name != "beta":
                _ "STORYAPI_ENUMERATE_REVERSED_SECOND_FAIL"
                return
        elif stage49_reverse_count == 3:
            if stage49_index != 7 or stage49_name != "alpha":
                _ "STORYAPI_ENUMERATE_REVERSED_THIRD_FAIL"
                return

    if stage49_reverse_count != 3:
        _ "STORYAPI_ENUMERATE_REVERSED_COUNT_FAIL"
        return

    if stage49_index_sum != 18:
        _ "STORYAPI_ENUMERATE_REVERSED_START_FAIL"
        return

    if len(stage49_probe_names) != 3 or stage49_probe_names[0] != "alpha":
        _ "STORYAPI_ENUMERATE_REVERSED_SOURCE_MUTATION_FAIL"
        return

    _ "STORYAPI_ENUMERATE_REVERSED_PASS"
    return
