# Stage 55 in-game smoke test for sorted(dictionary.values()) iteration.
# Start this label manually through the game's label/jump tooling.
define stage55_probe_settings = {"third": 3, "first": 1, "second": 2}
define stage55_probe_string_settings = {"third": "gamma", "first": "alpha", "second": "beta"}
define stage55_probe_start = 7
default stage55_ascending_count = 0
default stage55_descending_count = 0
default stage55_enumerate_count = 0
default stage55_index_sum = 0

label storyapi_stage55_sorted_dictionary_values_smoke:
    $ stage55_ascending_count = 0
    $ stage55_descending_count = 0
    $ stage55_enumerate_count = 0
    $ stage55_index_sum = 0

    for stage55_value in sorted(stage55_probe_settings.values()):
        $ stage55_ascending_count += 1
        if stage55_ascending_count == 1:
            if stage55_value != 1:
                _ "STORYAPI_SORTED_DICTIONARY_VALUES_ASCENDING_FIRST_FAIL"
                return
        elif stage55_ascending_count == 2:
            if stage55_value != 2:
                _ "STORYAPI_SORTED_DICTIONARY_VALUES_ASCENDING_SECOND_FAIL"
                return
        elif stage55_ascending_count == 3:
            if stage55_value != 3:
                _ "STORYAPI_SORTED_DICTIONARY_VALUES_ASCENDING_THIRD_FAIL"
                return

    if stage55_ascending_count != 3:
        _ "STORYAPI_SORTED_DICTIONARY_VALUES_ASCENDING_COUNT_FAIL"
        return

    for stage55_value in sorted(stage55_probe_settings.values(), reverse=True):
        $ stage55_descending_count += 1
        if stage55_descending_count == 1:
            if stage55_value != 3:
                _ "STORYAPI_SORTED_DICTIONARY_VALUES_DESCENDING_FIRST_FAIL"
                return
        elif stage55_descending_count == 2:
            if stage55_value != 2:
                _ "STORYAPI_SORTED_DICTIONARY_VALUES_DESCENDING_SECOND_FAIL"
                return
        elif stage55_descending_count == 3:
            if stage55_value != 1:
                _ "STORYAPI_SORTED_DICTIONARY_VALUES_DESCENDING_THIRD_FAIL"
                return

    if stage55_descending_count != 3:
        _ "STORYAPI_SORTED_DICTIONARY_VALUES_DESCENDING_COUNT_FAIL"
        return

    for stage55_index, stage55_value in enumerate(sorted(stage55_probe_settings.values()), stage55_probe_start):
        $ stage55_enumerate_count += 1
        $ stage55_index_sum += stage55_index
        if stage55_enumerate_count == 1:
            if stage55_index != 7 or stage55_value != 1:
                _ "STORYAPI_ENUMERATE_SORTED_DICTIONARY_VALUES_FIRST_FAIL"
                return
        elif stage55_enumerate_count == 2:
            if stage55_index != 8 or stage55_value != 2:
                _ "STORYAPI_ENUMERATE_SORTED_DICTIONARY_VALUES_SECOND_FAIL"
                return
        elif stage55_enumerate_count == 3:
            if stage55_index != 9 or stage55_value != 3:
                _ "STORYAPI_ENUMERATE_SORTED_DICTIONARY_VALUES_THIRD_FAIL"
                return

    if stage55_enumerate_count != 3 or stage55_index_sum != 24:
        _ "STORYAPI_ENUMERATE_SORTED_DICTIONARY_VALUES_COUNT_FAIL"
        return

    if stage55_probe_settings["first"] != 1 or stage55_probe_settings["second"] != 2 or stage55_probe_settings["third"] != 3:
        _ "STORYAPI_SORTED_DICTIONARY_VALUES_SOURCE_MUTATION_FAIL"
        return
    if stage55_probe_string_settings["first"] != "alpha" or stage55_probe_string_settings["second"] != "beta" or stage55_probe_string_settings["third"] != "gamma":
        _ "STORYAPI_SORTED_DICTIONARY_STRING_VALUES_SOURCE_FAIL"
        return

    _ "STORYAPI_SORTED_DICTIONARY_VALUES_PASS"
    return
