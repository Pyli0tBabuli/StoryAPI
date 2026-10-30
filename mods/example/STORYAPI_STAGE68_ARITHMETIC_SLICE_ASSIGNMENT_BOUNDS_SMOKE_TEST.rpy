define stage68_probe_numbers = [10, 20, 30, 40, 50, 60]
define stage68_probe_replacement = [21, 41]
define stage68_probe_reverse_replacement = [66, 44]
define stage68_probe_start = 0
define stage68_probe_stop = 6
define stage68_probe_step = 1

label storyapi_stage68_arithmetic_sequence_slice_assignment_bounds_smoke:
    $ stage68_probe_numbers[stage68_probe_start + 1:stage68_probe_stop - 1:stage68_probe_step * 2] = stage68_probe_replacement
    $ stage68_probe_numbers[stage68_probe_stop - 1:stage68_probe_start + 1:-stage68_probe_step * 2] = stage68_probe_reverse_replacement
    if len(stage68_probe_numbers) == 6 and stage68_probe_numbers[0] == 10 and stage68_probe_numbers[1] == 21 and stage68_probe_numbers[2] == 30 and stage68_probe_numbers[3] == 44 and stage68_probe_numbers[4] == 50 and stage68_probe_numbers[5] == 66:
        _ "STORYAPI_ARITHMETIC_SLICE_ASSIGNMENT_BOUNDS_PASS"
    else:
        _ "STORYAPI_ARITHMETIC_SLICE_ASSIGNMENT_BOUNDS_FAIL"
    return
