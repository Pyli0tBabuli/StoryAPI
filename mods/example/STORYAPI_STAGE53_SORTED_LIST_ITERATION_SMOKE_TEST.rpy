# Stage 53 in-game smoke test for sorted(list), sorted(list, reverse=True), and enumerate(sorted(list)).
# Start this label manually through the game's label/jump tooling.
define stage53_probe_numbers = [3, 1, 2]
define stage53_probe_names = ["gamma", "alpha", "beta"]
define stage53_probe_start = 3
default stage53_ascending_count = 0
default stage53_descending_count = 0
default stage53_enumerate_count = 0
default stage53_index_sum = 0

label storyapi_stage53_sorted_list_iteration_smoke:
    $ stage53_ascending_count = 0
    $ stage53_descending_count = 0
    $ stage53_enumerate_count = 0
    $ stage53_index_sum = 0

    for stage53_number in sorted(stage53_probe_numbers):
        $ stage53_ascending_count += 1
        if stage53_ascending_count == 1:
            if stage53_number != 1:
                _ "STORYAPI_SORTED_LIST_ASCENDING_FIRST_FAIL"
                return
        elif stage53_ascending_count == 2:
            if stage53_number != 2:
                _ "STORYAPI_SORTED_LIST_ASCENDING_SECOND_FAIL"
                return
        elif stage53_ascending_count == 3:
            if stage53_number != 3:
                _ "STORYAPI_SORTED_LIST_ASCENDING_THIRD_FAIL"
                return

    if stage53_ascending_count != 3:
        _ "STORYAPI_SORTED_LIST_ASCENDING_COUNT_FAIL"
        return

    for stage53_number in sorted(stage53_probe_numbers, reverse=True):
        $ stage53_descending_count += 1
        if stage53_descending_count == 1:
            if stage53_number != 3:
                _ "STORYAPI_SORTED_LIST_DESCENDING_FIRST_FAIL"
                return
        elif stage53_descending_count == 2:
            if stage53_number != 2:
                _ "STORYAPI_SORTED_LIST_DESCENDING_SECOND_FAIL"
                return
        elif stage53_descending_count == 3:
            if stage53_number != 1:
                _ "STORYAPI_SORTED_LIST_DESCENDING_THIRD_FAIL"
                return

    if stage53_descending_count != 3:
        _ "STORYAPI_SORTED_LIST_DESCENDING_COUNT_FAIL"
        return

    for stage53_index, stage53_name in enumerate(sorted(stage53_probe_names), stage53_probe_start + 1):
        $ stage53_enumerate_count += 1
        $ stage53_index_sum += stage53_index
        if stage53_enumerate_count == 1:
            if stage53_index != 4 or stage53_name != "alpha":
                _ "STORYAPI_ENUMERATE_SORTED_LIST_FIRST_FAIL"
                return
        elif stage53_enumerate_count == 2:
            if stage53_index != 5 or stage53_name != "beta":
                _ "STORYAPI_ENUMERATE_SORTED_LIST_SECOND_FAIL"
                return
        elif stage53_enumerate_count == 3:
            if stage53_index != 6 or stage53_name != "gamma":
                _ "STORYAPI_ENUMERATE_SORTED_LIST_THIRD_FAIL"
                return

    if stage53_enumerate_count != 3 or stage53_index_sum != 15:
        _ "STORYAPI_ENUMERATE_SORTED_LIST_COUNT_FAIL"
        return

    if stage53_probe_numbers[0] != 3 or stage53_probe_numbers[1] != 1 or stage53_probe_numbers[2] != 2:
        _ "STORYAPI_SORTED_LIST_SOURCE_MUTATION_FAIL"
        return
    if stage53_probe_names[0] != "gamma" or stage53_probe_names[1] != "alpha" or stage53_probe_names[2] != "beta":
        _ "STORYAPI_SORTED_LIST_STRING_MUTATION_FAIL"
        return

    _ "STORYAPI_SORTED_LIST_PASS"
    return
