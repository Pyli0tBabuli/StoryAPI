# Stage 52 smoke test for reversed(dictionary.items()) and enumerate(reversed(...)).
# Start this label manually through the game's label/jump tooling.
define stage52_probe_settings = {"alpha": 10, "beta": 20, "gamma": 30}
define stage52_probe_start = 4
default stage52_reverse_count = 0
default stage52_enumerate_count = 0
default stage52_index_sum = 0
default stage52_value_sum = 0

label storyapi_stage52_reversed_dictionary_items_smoke:
    $ stage52_reverse_count = 0
    $ stage52_enumerate_count = 0
    $ stage52_index_sum = 0
    $ stage52_value_sum = 0

    for stage52_key, stage52_value in reversed(stage52_probe_settings.items()):
        $ stage52_reverse_count += 1
        if stage52_reverse_count == 1:
            if stage52_key != "gamma" or stage52_value != 30:
                _ "STORYAPI_REVERSED_DICTIONARY_ITEMS_FIRST_FAIL"
                return
        elif stage52_reverse_count == 2:
            if stage52_key != "beta" or stage52_value != 20:
                _ "STORYAPI_REVERSED_DICTIONARY_ITEMS_SECOND_FAIL"
                return
        elif stage52_reverse_count == 3:
            if stage52_key != "alpha" or stage52_value != 10:
                _ "STORYAPI_REVERSED_DICTIONARY_ITEMS_THIRD_FAIL"
                return

    if stage52_reverse_count != 3:
        _ "STORYAPI_REVERSED_DICTIONARY_ITEMS_COUNT_FAIL"
        return

    for stage52_index, stage52_key, stage52_value in enumerate(reversed(stage52_probe_settings.items()), stage52_probe_start + 1):
        $ stage52_enumerate_count += 1
        $ stage52_index_sum += stage52_index
        $ stage52_value_sum += stage52_value
        if stage52_enumerate_count == 1:
            if stage52_index != 5 or stage52_key != "gamma" or stage52_value != 30:
                _ "STORYAPI_ENUMERATE_REVERSED_DICTIONARY_ITEMS_FIRST_FAIL"
                return
        elif stage52_enumerate_count == 2:
            if stage52_index != 6 or stage52_key != "beta" or stage52_value != 20:
                _ "STORYAPI_ENUMERATE_REVERSED_DICTIONARY_ITEMS_SECOND_FAIL"
                return
        elif stage52_enumerate_count == 3:
            if stage52_index != 7 or stage52_key != "alpha" or stage52_value != 10:
                _ "STORYAPI_ENUMERATE_REVERSED_DICTIONARY_ITEMS_THIRD_FAIL"
                return

    if stage52_enumerate_count != 3 or stage52_index_sum != 18 or stage52_value_sum != 60:
        _ "STORYAPI_ENUMERATE_REVERSED_DICTIONARY_ITEMS_TOTAL_FAIL"
        return

    if len(stage52_probe_settings) != 3 or stage52_probe_settings["gamma"] != 30:
        _ "STORYAPI_REVERSED_DICTIONARY_ITEMS_MUTATION_FAIL"
        return

    _ "STORYAPI_REVERSED_DICTIONARY_ITEMS_PASS"
    return
