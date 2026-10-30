define stage66_probe_numbers = [10, 20, 30, 40, 50]
define stage66_probe_start = 0
define stage66_probe_stop = 5
define stage66_probe_step = 2
define stage66_probe_slice = stage66_probe_numbers[stage66_probe_start + 1:stage66_probe_stop - 1:stage66_probe_step]

label storyapi_stage66_arithmetic_sequence_slice_read_bounds_smoke:
    if stage66_probe_slice[0] == 20 and stage66_probe_slice[1] == 40:
        _ "STORYAPI_ARITHMETIC_SLICE_READ_BOUNDS_PASS"
    else:
        _ "STORYAPI_ARITHMETIC_SLICE_READ_BOUNDS_FAIL"
    return
