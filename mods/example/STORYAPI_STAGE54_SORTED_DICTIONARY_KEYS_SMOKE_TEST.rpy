# Stage 54 in-game smoke test for sorted(dictionary) key iteration.
# Start this label manually through the game's label/jump tooling.
define stage54_probe_settings = {"gamma": 3, "alpha": 1, "beta": 2}
define stage54_probe_start = 5
default stage54_ascending_count = 0
default stage54_descending_count = 0
default stage54_enumerate_count = 0
default stage54_index_sum = 0

label storyapi_stage54_sorted_dictionary_keys_smoke:
    $ stage54_ascending_count = 0
    $ stage54_descending_count = 0
    $ stage54_enumerate_count = 0
    $ stage54_index_sum = 0

    for stage54_key in sorted(stage54_probe_settings):
        $ stage54_ascending_count += 1
        if stage54_ascending_count == 1:
            if stage54_key != "alpha":
                _ "STORYAPI_SORTED_DICTIONARY_KEYS_ASCENDING_FIRST_FAIL"
                return
        elif stage54_ascending_count == 2:
            if stage54_key != "beta":
                _ "STORYAPI_SORTED_DICTIONARY_KEYS_ASCENDING_SECOND_FAIL"
                return
        elif stage54_ascending_count == 3:
            if stage54_key != "gamma":
                _ "STORYAPI_SORTED_DICTIONARY_KEYS_ASCENDING_THIRD_FAIL"
                return

    if stage54_ascending_count != 3:
        _ "STORYAPI_SORTED_DICTIONARY_KEYS_ASCENDING_COUNT_FAIL"
        return

    for stage54_key in sorted(stage54_probe_settings, reverse=True):
        $ stage54_descending_count += 1
        if stage54_descending_count == 1:
            if stage54_key != "gamma":
                _ "STORYAPI_SORTED_DICTIONARY_KEYS_DESCENDING_FIRST_FAIL"
                return
        elif stage54_descending_count == 2:
            if stage54_key != "beta":
                _ "STORYAPI_SORTED_DICTIONARY_KEYS_DESCENDING_SECOND_FAIL"
                return
        elif stage54_descending_count == 3:
            if stage54_key != "alpha":
                _ "STORYAPI_SORTED_DICTIONARY_KEYS_DESCENDING_THIRD_FAIL"
                return

    if stage54_descending_count != 3:
        _ "STORYAPI_SORTED_DICTIONARY_KEYS_DESCENDING_COUNT_FAIL"
        return

    for stage54_index, stage54_key in enumerate(sorted(stage54_probe_settings), stage54_probe_start):
        $ stage54_enumerate_count += 1
        $ stage54_index_sum += stage54_index
        if stage54_enumerate_count == 1:
            if stage54_index != 5 or stage54_key != "alpha":
                _ "STORYAPI_ENUMERATE_SORTED_DICTIONARY_KEYS_FIRST_FAIL"
                return
        elif stage54_enumerate_count == 2:
            if stage54_index != 6 or stage54_key != "beta":
                _ "STORYAPI_ENUMERATE_SORTED_DICTIONARY_KEYS_SECOND_FAIL"
                return
        elif stage54_enumerate_count == 3:
            if stage54_index != 7 or stage54_key != "gamma":
                _ "STORYAPI_ENUMERATE_SORTED_DICTIONARY_KEYS_THIRD_FAIL"
                return

    if stage54_enumerate_count != 3 or stage54_index_sum != 18:
        _ "STORYAPI_ENUMERATE_SORTED_DICTIONARY_KEYS_COUNT_FAIL"
        return

    if stage54_probe_settings["alpha"] != 1 or stage54_probe_settings["beta"] != 2 or stage54_probe_settings["gamma"] != 3:
        _ "STORYAPI_SORTED_DICTIONARY_KEYS_SOURCE_MUTATION_FAIL"
        return

    _ "STORYAPI_SORTED_DICTIONARY_KEYS_PASS"
    return
