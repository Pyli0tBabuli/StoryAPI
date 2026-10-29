# Stage 44 in-game smoke test for enumerate(dictionary, start).
# Start this label manually through the game's label/jump tooling.
define stage44_probe_settings = {"alpha": 1, "beta": 2, "gamma": 3}
default stage44_index_sum = 0

label storyapi_stage44_enumerate_dictionary_smoke:
    $ stage44_index_sum = 0

    for stage44_index, stage44_key in enumerate(stage44_probe_settings, 4):
        $ stage44_index_sum += stage44_index
        if stage44_index == 4:
            if stage44_key != "alpha":
                _ "STORYAPI_ENUMERATE_DICTIONARY_FIRST_KEY_FAIL"
                return
            $ stage44_probe_settings["delta"] = 4
        elif stage44_index == 5:
            if stage44_key != "beta":
                _ "STORYAPI_ENUMERATE_DICTIONARY_SECOND_KEY_FAIL"
                return
        elif stage44_index == 6:
            if stage44_key != "gamma":
                _ "STORYAPI_ENUMERATE_DICTIONARY_THIRD_KEY_FAIL"
                return

    if stage44_index_sum != 15:
        _ "STORYAPI_ENUMERATE_DICTIONARY_INDEX_SUM_FAIL"
        return

    if len(stage44_probe_settings) != 4:
        _ "STORYAPI_ENUMERATE_DICTIONARY_SOURCE_MUTATION_FAIL"
        return

    _ "STORYAPI_ENUMERATE_DICTIONARY_PASS"
    return
