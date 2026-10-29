# Stage 45 in-game smoke test for enumerate(dictionary.values(), start).
# Start this label manually through the game's label/jump tooling.
define stage45_probe_settings = {"red": 10, "blue": 20, "green": 30}
default stage45_index_sum = 0

label storyapi_stage45_enumerate_dictionary_values_smoke:
    $ stage45_index_sum = 0

    for stage45_index, stage45_value in enumerate(stage45_probe_settings.values(), 2):
        $ stage45_index_sum += stage45_index
        if stage45_index == 2:
            if stage45_value != 10:
                _ "STORYAPI_ENUMERATE_DICTIONARY_VALUES_FIRST_FAIL"
                return
            $ stage45_probe_settings["yellow"] = 40
        elif stage45_index == 3:
            if stage45_value != 20:
                _ "STORYAPI_ENUMERATE_DICTIONARY_VALUES_SECOND_FAIL"
                return
        elif stage45_index == 4:
            if stage45_value != 30:
                _ "STORYAPI_ENUMERATE_DICTIONARY_VALUES_THIRD_FAIL"
                return

    if stage45_index_sum != 9:
        _ "STORYAPI_ENUMERATE_DICTIONARY_VALUES_SUM_FAIL"
        return

    if len(stage45_probe_settings) != 4:
        _ "STORYAPI_ENUMERATE_DICTIONARY_VALUES_MUTATION_FAIL"
        return

    _ "STORYAPI_ENUMERATE_DICTIONARY_VALUES_PASS"
    return
