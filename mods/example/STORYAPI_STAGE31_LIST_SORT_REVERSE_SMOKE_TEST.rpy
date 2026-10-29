# Stage 31 in-game smoke test for list.sort(reverse=True/False).
# Start this label manually through the game's label/jump tooling.
define stage31_probe_strings = ["pear", "apple", "banana"]
define stage31_probe_numbers = [9, -2, 3, 0]

label storyapi_stage31_list_sort_reverse_smoke:
    $ stage31_probe_strings.sort(reverse=True)
    if stage31_probe_strings[0] != "pear":
        _ "STORYAPI_LIST_SORT_REVERSE_STRINGS_FIRST_FAIL"
        return
    if stage31_probe_strings[1] != "banana":
        _ "STORYAPI_LIST_SORT_REVERSE_STRINGS_MIDDLE_FAIL"
        return
    if stage31_probe_strings[2] != "apple":
        _ "STORYAPI_LIST_SORT_REVERSE_STRINGS_LAST_FAIL"
        return

    $ stage31_probe_numbers.sort(reverse=True)
    if stage31_probe_numbers[0] != 9:
        _ "STORYAPI_LIST_SORT_REVERSE_NUMBERS_FIRST_FAIL"
        return
    if stage31_probe_numbers[1] != 3:
        _ "STORYAPI_LIST_SORT_REVERSE_NUMBERS_MIDDLE_FAIL"
        return
    if stage31_probe_numbers[3] != -2:
        _ "STORYAPI_LIST_SORT_REVERSE_NUMBERS_LAST_FAIL"
        return

    $ stage31_probe_numbers.sort(reverse=False)
    if stage31_probe_numbers[0] != -2:
        _ "STORYAPI_LIST_SORT_REVERSE_FALSE_FAIL"
        return

    _ "STORYAPI_LIST_SORT_REVERSE_PASS"
    return
