# Stage 34 in-game smoke test for sorted(list, reverse=True/False).
# Start this label manually through the game's label/jump tooling.
define stage34_probe_strings = ["pear", "apple", "banana", "Apple"]
define stage34_probe_numbers = [9, -2, 3, 0]

label storyapi_stage34_sorted_reverse_smoke:
    $ stage34_sorted_strings = sorted(stage34_probe_strings, reverse=True)
    if stage34_sorted_strings[0] != "pear":
        _ "STORYAPI_SORTED_REVERSE_STRINGS_FIRST_FAIL"
        return
    if stage34_sorted_strings[1] != "banana":
        _ "STORYAPI_SORTED_REVERSE_STRINGS_MIDDLE_FAIL"
        return
    if stage34_sorted_strings[2] != "apple":
        _ "STORYAPI_SORTED_REVERSE_STRINGS_LOWERCASE_FAIL"
        return
    if stage34_sorted_strings[3] != "Apple":
        _ "STORYAPI_SORTED_REVERSE_STRINGS_LAST_FAIL"
        return
    if stage34_probe_strings[0] != "pear":
        _ "STORYAPI_SORTED_REVERSE_STRING_SOURCE_MUTATION_FAIL"
        return

    $ stage34_sorted_numbers = sorted(stage34_probe_numbers, reverse=True)
    if stage34_sorted_numbers[0] != 9:
        _ "STORYAPI_SORTED_REVERSE_NUMBERS_FIRST_FAIL"
        return
    if stage34_sorted_numbers[1] != 3:
        _ "STORYAPI_SORTED_REVERSE_NUMBERS_MIDDLE_FAIL"
        return
    if stage34_sorted_numbers[3] != -2:
        _ "STORYAPI_SORTED_REVERSE_NUMBERS_LAST_FAIL"
        return
    if stage34_probe_numbers[0] != 9:
        _ "STORYAPI_SORTED_REVERSE_NUMBER_SOURCE_MUTATION_FAIL"
        return

    $ stage34_sorted_numbers = sorted(stage34_probe_numbers, reverse=False)
    if stage34_sorted_numbers[0] != -2:
        _ "STORYAPI_SORTED_REVERSE_FALSE_FAIL"
        return

    _ "STORYAPI_SORTED_REVERSE_PASS"
    return
