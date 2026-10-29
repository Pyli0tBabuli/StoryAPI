# Stage 50 smoke test for reversed(dict) and enumerate(reversed(dict), start).
# Start this label manually through the game's label/jump tooling.
define stage50_probe_settings = {"alpha": 1, "beta": 2, "gamma": 3}
define stage50_probe_start = 4
default stage50_reverse_count = 0
default stage50_enumerate_count = 0
default stage50_index_sum = 0

label storyapi_stage50_reversed_dictionary_smoke:
    $ stage50_reverse_count = 0
    $ stage50_enumerate_count = 0
    $ stage50_index_sum = 0

    for stage50_key in reversed(stage50_probe_settings):
        $ stage50_reverse_count += 1
        if stage50_reverse_count == 1:
            if stage50_key != "gamma":
                _ "STORYAPI_REVERSED_DICTIONARY_FIRST_FAIL"
                return
        elif stage50_reverse_count == 2:
            if stage50_key != "beta":
                _ "STORYAPI_REVERSED_DICTIONARY_SECOND_FAIL"
                return
        elif stage50_reverse_count == 3:
            if stage50_key != "alpha":
                _ "STORYAPI_REVERSED_DICTIONARY_THIRD_FAIL"
                return

    if stage50_reverse_count != 3:
        _ "STORYAPI_REVERSED_DICTIONARY_COUNT_FAIL"
        return

    for stage50_index, stage50_key in enumerate(reversed(stage50_probe_settings), stage50_probe_start + 1):
        $ stage50_enumerate_count += 1
        $ stage50_index_sum += stage50_index
        if stage50_enumerate_count == 1:
            if stage50_index != 5 or stage50_key != "gamma":
                _ "STORYAPI_ENUMERATE_REVERSED_DICTIONARY_FIRST_FAIL"
                return
        elif stage50_enumerate_count == 2:
            if stage50_index != 6 or stage50_key != "beta":
                _ "STORYAPI_ENUMERATE_REVERSED_DICTIONARY_SECOND_FAIL"
                return
        elif stage50_enumerate_count == 3:
            if stage50_index != 7 or stage50_key != "alpha":
                _ "STORYAPI_ENUMERATE_REVERSED_DICTIONARY_THIRD_FAIL"
                return

    if stage50_enumerate_count != 3 or stage50_index_sum != 18:
        _ "STORYAPI_ENUMERATE_REVERSED_DICTIONARY_COUNT_FAIL"
        return

    if len(stage50_probe_settings) != 3 or stage50_probe_settings["gamma"] != 3:
        _ "STORYAPI_REVERSED_DICTIONARY_SOURCE_MUTATION_FAIL"
        return

    _ "STORYAPI_REVERSED_DICTIONARY_PASS"
    return
