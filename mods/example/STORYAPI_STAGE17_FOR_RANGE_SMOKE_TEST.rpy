# Stage 17 in-game smoke test for range() loops.
# Start this label manually through the game's label/jump tooling.
default stage17_range_total = 0
default stage17_range_count = 0
default stage17_range_start = 0
default stage17_range_stop = 0
default stage17_range_step = 0

label storyapi_stage17_for_range_smoke:
    $ stage17_range_total = 0
    $ stage17_range_count = 0
    for stage17_i in range(5):
        $ stage17_range_total += stage17_i
        $ stage17_range_count += 1
    if stage17_range_total != 10 or stage17_range_count != 5:
        _ "STORYAPI_FOR_RANGE_ONE_ARG_FAIL"
        return

    $ stage17_range_total = 0
    $ stage17_range_count = 0
    for stage17_i in range(2, 5):
        $ stage17_range_total += stage17_i
        $ stage17_range_count += 1
    if stage17_range_total != 9 or stage17_range_count != 3:
        _ "STORYAPI_FOR_RANGE_TWO_ARGS_FAIL"
        return

    $ stage17_range_total = 0
    $ stage17_range_count = 0
    for stage17_i in range(6, 0, -2):
        $ stage17_range_total += stage17_i
        $ stage17_range_count += 1
    if stage17_range_total != 12 or stage17_range_count != 3:
        _ "STORYAPI_FOR_RANGE_NEGATIVE_STEP_FAIL"
        return

    $ stage17_range_count = 0
    for stage17_i in range(0):
        $ stage17_range_count += 1
    if stage17_range_count != 0:
        _ "STORYAPI_FOR_RANGE_EMPTY_FAIL"
        return

    $ stage17_range_total = 0
    $ stage17_range_count = 0
    for stage17_i in range(5):
        if stage17_i == 2:
            continue
        if stage17_i == 4:
            break
        $ stage17_range_total += stage17_i
        $ stage17_range_count += 1
    if stage17_range_total != 4 or stage17_range_count != 3:
        _ "STORYAPI_FOR_RANGE_LOOP_CONTROL_FAIL"
        return

    $ stage17_range_start = 2
    $ stage17_range_stop = 5
    $ stage17_range_step = 1
    $ stage17_range_total = 0
    $ stage17_range_count = 0
    for stage17_i in range(stage17_range_start, stage17_range_stop, stage17_range_step):
        $ stage17_range_total += stage17_i
        $ stage17_range_count += 1
    if stage17_range_total != 9 or stage17_range_count != 3:
        _ "STORYAPI_FOR_RANGE_VARIABLE_ARGS_FAIL"
        return

    $ stage17_range_count = 0
    for stage17_i in range(1, 5, 0):
        $ stage17_range_count += 1
    if stage17_range_count != 0:
        _ "STORYAPI_FOR_RANGE_ZERO_STEP_FAIL"
        return

    _ "STORYAPI_FOR_RANGE_PASS"
    return
