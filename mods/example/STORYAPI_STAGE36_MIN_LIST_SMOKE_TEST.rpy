# Stage 36 in-game smoke test for min(list).
# Start this label manually through the game's label/jump tooling.
define stage36_probe_numbers = [2, -3, 4, 0]
define stage36_probe_strings = ["pear", "apple", "Apple", "banana"]

label storyapi_stage36_min_list_smoke:
    _ "STORYAPI_MIN_SMOKE_STARTED"

    $ stage36_lowest_number = min(stage36_probe_numbers)
    if stage36_lowest_number != -3:
        _ "STORYAPI_MIN_NUMBERS_FAIL"
        return

    if stage36_probe_numbers[0] != 2:
        _ "STORYAPI_MIN_SOURCE_MUTATION_FAIL"
        return

    $ stage36_first_string = min(stage36_probe_strings)
    if stage36_first_string != "Apple":
        _ "STORYAPI_MIN_STRINGS_FAIL"
        return

    _ "STORYAPI_MIN_LIST_PASS"
    return
