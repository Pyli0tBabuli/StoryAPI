# Stage 47 in-game smoke test for zip(list_a, list_b).
# Start this label manually through the game's label/jump tooling.
define stage47_probe_names = ["alpha", "beta", "gamma"]
define stage47_probe_numbers = [10, 20]
default stage47_pair_count = 0
default stage47_number_sum = 0

label storyapi_stage47_zip_list_smoke:
    $ stage47_pair_count = 0
    $ stage47_number_sum = 0

    for stage47_name, stage47_number in zip(stage47_probe_names, stage47_probe_numbers):
        $ stage47_pair_count += 1
        $ stage47_number_sum += stage47_number
        if stage47_pair_count == 1:
            if stage47_name != "alpha" or stage47_number != 10:
                _ "STORYAPI_ZIP_LIST_FIRST_PAIR_FAIL"
                return
        elif stage47_pair_count == 2:
            if stage47_name != "beta" or stage47_number != 20:
                _ "STORYAPI_ZIP_LIST_SECOND_PAIR_FAIL"
                return

    if stage47_pair_count != 2:
        _ "STORYAPI_ZIP_LIST_COUNT_FAIL"
        return

    if stage47_number_sum != 30:
        _ "STORYAPI_ZIP_LIST_SUM_FAIL"
        return

    if len(stage47_probe_names) != 3 or len(stage47_probe_numbers) != 2:
        _ "STORYAPI_ZIP_LIST_SOURCE_MUTATION_FAIL"
        return

    _ "STORYAPI_ZIP_LIST_PASS"
    return
