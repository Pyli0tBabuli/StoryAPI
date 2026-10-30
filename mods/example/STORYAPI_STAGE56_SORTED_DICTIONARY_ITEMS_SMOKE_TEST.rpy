# Stage 56 in-game smoke test for sorted(dictionary.items()) iteration.
# Start this label manually through the game's label/jump tooling.
define stage56_probe_settings = {"gamma": 3, "alpha": 1, "beta": 2}
define stage56_probe_start = 10
default stage56_ascending_count = 0
default stage56_descending_count = 0
default stage56_enumerate_count = 0
default stage56_index_sum = 0

label storyapi_stage56_sorted_dictionary_items_smoke:
    $ stage56_ascending_count = 0
    $ stage56_descending_count = 0
    $ stage56_enumerate_count = 0
    $ stage56_index_sum = 0

    for stage56_key, stage56_value in sorted(stage56_probe_settings.items()):
        $ stage56_ascending_count += 1
        if stage56_ascending_count == 1:
            if stage56_key != "alpha" or stage56_value != 1:
                _ "STORYAPI_SORTED_DICTIONARY_ITEMS_ASCENDING_FIRST_FAIL"
                return
        elif stage56_ascending_count == 2:
            if stage56_key != "beta" or stage56_value != 2:
                _ "STORYAPI_SORTED_DICTIONARY_ITEMS_ASCENDING_SECOND_FAIL"
                return
        elif stage56_ascending_count == 3:
            if stage56_key != "gamma" or stage56_value != 3:
                _ "STORYAPI_SORTED_DICTIONARY_ITEMS_ASCENDING_THIRD_FAIL"
                return

    if stage56_ascending_count != 3:
        _ "STORYAPI_SORTED_DICTIONARY_ITEMS_ASCENDING_COUNT_FAIL"
        return

    for stage56_key, stage56_value in sorted(stage56_probe_settings.items(), reverse=True):
        $ stage56_descending_count += 1
        if stage56_descending_count == 1:
            if stage56_key != "gamma" or stage56_value != 3:
                _ "STORYAPI_SORTED_DICTIONARY_ITEMS_DESCENDING_FIRST_FAIL"
                return
        elif stage56_descending_count == 2:
            if stage56_key != "beta" or stage56_value != 2:
                _ "STORYAPI_SORTED_DICTIONARY_ITEMS_DESCENDING_SECOND_FAIL"
                return
        elif stage56_descending_count == 3:
            if stage56_key != "alpha" or stage56_value != 1:
                _ "STORYAPI_SORTED_DICTIONARY_ITEMS_DESCENDING_THIRD_FAIL"
                return

    if stage56_descending_count != 3:
        _ "STORYAPI_SORTED_DICTIONARY_ITEMS_DESCENDING_COUNT_FAIL"
        return

    for stage56_index, stage56_key, stage56_value in enumerate(sorted(stage56_probe_settings.items()), stage56_probe_start):
        $ stage56_enumerate_count += 1
        $ stage56_index_sum += stage56_index
        if stage56_enumerate_count == 1:
            if stage56_index != 10 or stage56_key != "alpha" or stage56_value != 1:
                _ "STORYAPI_ENUMERATE_SORTED_DICTIONARY_ITEMS_FIRST_FAIL"
                return
        elif stage56_enumerate_count == 2:
            if stage56_index != 11 or stage56_key != "beta" or stage56_value != 2:
                _ "STORYAPI_ENUMERATE_SORTED_DICTIONARY_ITEMS_SECOND_FAIL"
                return
        elif stage56_enumerate_count == 3:
            if stage56_index != 12 or stage56_key != "gamma" or stage56_value != 3:
                _ "STORYAPI_ENUMERATE_SORTED_DICTIONARY_ITEMS_THIRD_FAIL"
                return

    if stage56_enumerate_count != 3 or stage56_index_sum != 33:
        _ "STORYAPI_ENUMERATE_SORTED_DICTIONARY_ITEMS_COUNT_FAIL"
        return

    if stage56_probe_settings["alpha"] != 1 or stage56_probe_settings["beta"] != 2 or stage56_probe_settings["gamma"] != 3:
        _ "STORYAPI_SORTED_DICTIONARY_ITEMS_SOURCE_MUTATION_FAIL"
        return

    _ "STORYAPI_SORTED_DICTIONARY_ITEMS_PASS"
    return
