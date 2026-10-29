# Stage 35 in-game smoke test for sum(list).
# Start this label manually through the game's label/jump tooling.
define stage35_probe_numbers = [2, 3, -1, 4]
define stage35_probe_empty = []

label storyapi_stage35_sum_list_smoke:
    $ stage35_total = sum(stage35_probe_numbers)
    if stage35_total != 8:
        _ "STORYAPI_SUM_NUMBERS_FAIL"
        return

    if stage35_probe_numbers[0] != 2:
        _ "STORYAPI_SUM_SOURCE_MUTATION_FAIL"
        return

    $ stage35_empty_total = sum(stage35_probe_empty)
    if stage35_empty_total != 0:
        _ "STORYAPI_SUM_EMPTY_FAIL"
        return

    _ "STORYAPI_SUM_LIST_PASS"
    return
