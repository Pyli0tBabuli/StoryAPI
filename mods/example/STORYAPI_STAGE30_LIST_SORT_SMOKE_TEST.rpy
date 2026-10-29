# Stage 30 in-game smoke test for list.sort().
# Start this label manually through the game's label/jump tooling.
define stage30_probe_strings = ["pear", "apple", "banana", "Apple"]
define stage30_probe_numbers = [9, -2, 3, 0]

label storyapi_stage30_list_sort_smoke:
    $ stage30_probe_strings.sort()
    if stage30_probe_strings[0] != "Apple":
        _ "STORYAPI_LIST_SORT_STRINGS_FAIL"
        return
    if stage30_probe_strings[1] != "apple":
        _ "STORYAPI_LIST_SORT_CASE_FAIL"
        return
    if stage30_probe_strings[2] != "banana":
        _ "STORYAPI_LIST_SORT_ORDER_FAIL"
        return
    if stage30_probe_strings[3] != "pear":
        _ "STORYAPI_LIST_SORT_LAST_FAIL"
        return

    $ stage30_probe_numbers.sort()
    if stage30_probe_numbers[0] != -2:
        _ "STORYAPI_LIST_SORT_NEGATIVE_FAIL"
        return
    if stage30_probe_numbers[1] != 0:
        _ "STORYAPI_LIST_SORT_ZERO_FAIL"
        return
    if stage30_probe_numbers[2] != 3:
        _ "STORYAPI_LIST_SORT_MIDDLE_FAIL"
        return
    if stage30_probe_numbers[3] != 9:
        _ "STORYAPI_LIST_SORT_NUMBER_ORDER_FAIL"
        return

    _ "STORYAPI_LIST_SORT_PASS"
    return
