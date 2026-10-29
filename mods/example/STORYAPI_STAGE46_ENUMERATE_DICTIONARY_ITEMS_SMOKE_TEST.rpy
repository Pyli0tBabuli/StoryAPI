# Stage 46 in-game smoke test for enumerate(dictionary.items(), start).
# Start this label manually through the game's label/jump tooling.
define stage46_probe_settings = {"alpha": 1, "beta": 2, "gamma": 3}
default stage46_index_sum = 0

label storyapi_stage46_enumerate_dictionary_items_smoke:
    $ stage46_index_sum = 0

    for stage46_index, stage46_key, stage46_value in enumerate(stage46_probe_settings.items(), 4):
        $ stage46_index_sum += stage46_index
        if stage46_index == 4:
            if stage46_key != "alpha" or stage46_value != 1:
                _ "STORYAPI_ENUMERATE_DICTIONARY_ITEMS_FIRST_FAIL"
                return
            $ stage46_probe_settings["delta"] = 4
        elif stage46_index == 5:
            if stage46_key != "beta" or stage46_value != 2:
                _ "STORYAPI_ENUMERATE_DICTIONARY_ITEMS_SECOND_FAIL"
                return
        elif stage46_index == 6:
            if stage46_key != "gamma" or stage46_value != 3:
                _ "STORYAPI_ENUMERATE_DICTIONARY_ITEMS_THIRD_FAIL"
                return

    if stage46_index_sum != 15:
        _ "STORYAPI_ENUMERATE_DICTIONARY_ITEMS_INDEX_SUM_FAIL"
        return

    if len(stage46_probe_settings) != 4:
        _ "STORYAPI_ENUMERATE_DICTIONARY_ITEMS_SOURCE_MUTATION_FAIL"
        return

    _ "STORYAPI_ENUMERATE_DICTIONARY_ITEMS_PASS"
    return
