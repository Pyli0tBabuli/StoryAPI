# Stage 37 in-game smoke test for max(list).
# Start this label manually through the game's label/jump tooling.
define stage37_probe_numbers = [2, -3, 4, 0]
define stage37_probe_strings = ["pear", "apple", "Apple", "banana"]

label storyapi_stage37_max_list_smoke:
    _ "STORYAPI_MAX_SMOKE_STARTED"

    $ stage37_highest_number = max(stage37_probe_numbers)
    if stage37_highest_number != 4:
        _ "STORYAPI_MAX_NUMBERS_FAIL"
        return

    if stage37_probe_numbers[0] != 2:
        _ "STORYAPI_MAX_SOURCE_MUTATION_FAIL"
        return

    $ stage37_last_string = max(stage37_probe_strings)
    if stage37_last_string != "pear":
        _ "STORYAPI_MAX_STRINGS_FAIL"
        return

    _ "STORYAPI_MAX_LIST_PASS"
    return
