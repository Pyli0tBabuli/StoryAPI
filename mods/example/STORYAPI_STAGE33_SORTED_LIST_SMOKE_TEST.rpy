# Stage 33 in-game smoke test for sorted(list).
# Start this label manually through the game's label/jump tooling.
define stage33_probe_strings = ["pear", "apple", "banana", "Apple"]
define stage33_probe_numbers = [9, -2, 3, 0]

label storyapi_stage33_sorted_list_smoke:
    $ stage33_sorted_strings = sorted(stage33_probe_strings)
    if stage33_sorted_strings[0] != "Apple":
        _ "STORYAPI_SORTED_STRINGS_FIRST_FAIL"
        return
    if stage33_sorted_strings[1] != "apple":
        _ "STORYAPI_SORTED_STRINGS_CASE_FAIL"
        return
    if stage33_sorted_strings[2] != "banana":
        _ "STORYAPI_SORTED_STRINGS_MIDDLE_FAIL"
        return
    if stage33_sorted_strings[3] != "pear":
        _ "STORYAPI_SORTED_STRINGS_LAST_FAIL"
        return
    if stage33_probe_strings[0] != "pear":
        _ "STORYAPI_SORTED_SOURCE_MUTATION_FAIL"
        return

    $ stage33_sorted_numbers = sorted(stage33_probe_numbers)
    if stage33_sorted_numbers[0] != -2:
        _ "STORYAPI_SORTED_NUMBERS_FIRST_FAIL"
        return
    if stage33_sorted_numbers[1] != 0:
        _ "STORYAPI_SORTED_NUMBERS_ZERO_FAIL"
        return
    if stage33_sorted_numbers[2] != 3:
        _ "STORYAPI_SORTED_NUMBERS_MIDDLE_FAIL"
        return
    if stage33_sorted_numbers[3] != 9:
        _ "STORYAPI_SORTED_NUMBERS_LAST_FAIL"
        return
    if stage33_probe_numbers[0] != 9:
        _ "STORYAPI_SORTED_NUMBERS_SOURCE_MUTATION_FAIL"
        return

    _ "STORYAPI_SORTED_LIST_PASS"
    return
