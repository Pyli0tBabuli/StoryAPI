define stage67_probe_numbers = [10, 20, 30, 40, 50, 60]
define stage67_probe_start = 1
define stage67_probe_stop = 6
define stage67_probe_step = 1

label storyapi_stage67_arithmetic_sequence_slice_delete_bounds_smoke:
    $ del stage67_probe_numbers[stage67_probe_start - 1:stage67_probe_stop - 1:stage67_probe_step + 1]
    $ del stage67_probe_numbers[stage67_probe_stop - 1:stage67_probe_start - 1:-stage67_probe_step * 2]
    if len(stage67_probe_numbers) == 2 and stage67_probe_numbers[0] == 20 and stage67_probe_numbers[1] == 40:
        _ "STORYAPI_ARITHMETIC_SLICE_DELETE_BOUNDS_PASS"
    else:
        _ "STORYAPI_ARITHMETIC_SLICE_DELETE_BOUNDS_FAIL"
    return
