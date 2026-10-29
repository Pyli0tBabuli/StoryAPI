# Stage 40 in-game smoke test for reversed(list) iteration.
# Start this label manually through the game's label/jump tooling.
define stage40_probe_values = [1, 2, 3]

label storyapi_stage40_reversed_list_smoke:
    $ stage40_accumulator = 0
    for stage40_item in reversed(stage40_probe_values):
        $ stage40_accumulator = stage40_accumulator * 10 + stage40_item

    if stage40_accumulator != 321:
        _ "STORYAPI_REVERSED_ORDER_FAIL"
        return

    if stage40_probe_values[0] != 1:
        _ "STORYAPI_REVERSED_SOURCE_MUTATION_FAIL"
        return

    _ "STORYAPI_REVERSED_LIST_PASS"
    return
