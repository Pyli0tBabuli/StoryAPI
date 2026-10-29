# Stage 48 smoke test for enumerate(zip(list_a, list_b), start).
# Start this label manually through the game's label/jump tooling.
define stage48_probe_names = ["alpha", "beta", "gamma"]
define stage48_probe_numbers = [10, 20]
define stage48_probe_start = 5
default stage48_pair_count = 0
default stage48_index_sum = 0

label storyapi_stage48_enumerate_zip_smoke:
    $ stage48_pair_count = 0
    $ stage48_index_sum = 0

    for stage48_index, stage48_name, stage48_number in enumerate(zip(stage48_probe_names, stage48_probe_numbers), stage48_probe_start + 0):
        $ stage48_pair_count += 1
        $ stage48_index_sum += stage48_index
        if stage48_pair_count == 1:
            if stage48_index != 5 or stage48_name != "alpha" or stage48_number != 10:
                _ "STORYAPI_ENUMERATE_ZIP_FIRST_PAIR_FAIL"
                return
        elif stage48_pair_count == 2:
            if stage48_index != 6 or stage48_name != "beta" or stage48_number != 20:
                _ "STORYAPI_ENUMERATE_ZIP_SECOND_PAIR_FAIL"
                return

    if stage48_pair_count != 2:
        _ "STORYAPI_ENUMERATE_ZIP_COUNT_FAIL"
        return

    if stage48_index_sum != 11:
        _ "STORYAPI_ENUMERATE_ZIP_START_FAIL"
        return

    if len(stage48_probe_names) != 3 or len(stage48_probe_numbers) != 2:
        _ "STORYAPI_ENUMERATE_ZIP_SOURCE_MUTATION_FAIL"
        return

    _ "STORYAPI_ENUMERATE_ZIP_PASS"
    return
