# Stage 51 smoke test for reversed(dictionary.values()) and enumerate(reversed(...)).
# Start this label manually through the game's label/jump tooling.
define stage51_probe_settings = {"alpha": 10, "beta": 20, "gamma": 30}
define stage51_probe_start = 4
default stage51_reverse_count = 0
default stage51_enumerate_count = 0
default stage51_index_sum = 0
default stage51_value_sum = 0

label storyapi_stage51_reversed_dictionary_values_smoke:
    $ stage51_reverse_count = 0
    $ stage51_enumerate_count = 0
    $ stage51_index_sum = 0
    $ stage51_value_sum = 0

    for stage51_value in reversed(stage51_probe_settings.values()):
        $ stage51_reverse_count += 1
        if stage51_reverse_count == 1:
            if stage51_value != 30:
                _ "STORYAPI_REVERSED_DICTIONARY_VALUES_FIRST_FAIL"
                return
        elif stage51_reverse_count == 2:
            if stage51_value != 20:
                _ "STORYAPI_REVERSED_DICTIONARY_VALUES_SECOND_FAIL"
                return
        elif stage51_reverse_count == 3:
            if stage51_value != 10:
                _ "STORYAPI_REVERSED_DICTIONARY_VALUES_THIRD_FAIL"
                return

    if stage51_reverse_count != 3:
        _ "STORYAPI_REVERSED_DICTIONARY_VALUES_COUNT_FAIL"
        return

    for stage51_index, stage51_value in enumerate(reversed(stage51_probe_settings.values()), stage51_probe_start + 1):
        $ stage51_enumerate_count += 1
        $ stage51_index_sum += stage51_index
        $ stage51_value_sum += stage51_value
        if stage51_enumerate_count == 1:
            if stage51_index != 5 or stage51_value != 30:
                _ "STORYAPI_ENUMERATE_REVERSED_DICTIONARY_VALUES_FIRST_FAIL"
                return
        elif stage51_enumerate_count == 2:
            if stage51_index != 6 or stage51_value != 20:
                _ "STORYAPI_ENUMERATE_REVERSED_DICTIONARY_VALUES_SECOND_FAIL"
                return
        elif stage51_enumerate_count == 3:
            if stage51_index != 7 or stage51_value != 10:
                _ "STORYAPI_ENUMERATE_REVERSED_DICTIONARY_VALUES_THIRD_FAIL"
                return

    if stage51_enumerate_count != 3 or stage51_index_sum != 18 or stage51_value_sum != 60:
        _ "STORYAPI_ENUMERATE_REVERSED_DICTIONARY_VALUES_TOTAL_FAIL"
        return

    if len(stage51_probe_settings) != 3 or stage51_probe_settings["gamma"] != 30:
        _ "STORYAPI_REVERSED_DICTIONARY_VALUES_MUTATION_FAIL"
        return

    _ "STORYAPI_REVERSED_DICTIONARY_VALUES_PASS"
    return
