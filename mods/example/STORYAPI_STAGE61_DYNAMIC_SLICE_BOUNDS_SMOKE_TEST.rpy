# Stage 61 in-game smoke test for dynamic sequence slice bounds.
# Start this label manually through the game's label/jump tooling.
define stage61_probe_text = "A😀BC"
define stage61_probe_start = 1
define stage61_probe_stop = 4
define stage61_probe_step = 2
define stage61_probe_string_slice = stage61_probe_text[stage61_probe_start:stage61_probe_stop:stage61_probe_step]

define stage61_probe_numbers = [1, 2, 3, 4, 5]
define stage61_probe_list_slice = stage61_probe_numbers[stage61_probe_start:stage61_probe_stop:stage61_probe_step]

define stage61_probe_reverse_start = 4
define stage61_probe_reverse_stop = 1
define stage61_probe_reverse_step = -1
define stage61_probe_reverse_slice = stage61_probe_numbers[stage61_probe_reverse_start:stage61_probe_reverse_stop:stage61_probe_reverse_step]

label storyapi_stage61_dynamic_slice_bounds_smoke:
    if stage61_probe_string_slice != "😀C":
        _ "STORYAPI_DYNAMIC_SLICE_STRING_FAIL"
        return

    if len(stage61_probe_list_slice) != 2:
        _ "STORYAPI_DYNAMIC_SLICE_LIST_LENGTH_FAIL"
        return

    if stage61_probe_list_slice[0] != 2 or stage61_probe_list_slice[1] != 4:
        _ "STORYAPI_DYNAMIC_SLICE_LIST_CONTENT_FAIL"
        return

    if len(stage61_probe_reverse_slice) != 3:
        _ "STORYAPI_DYNAMIC_SLICE_REVERSE_LENGTH_FAIL"
        return

    if stage61_probe_reverse_slice[0] != 5 or stage61_probe_reverse_slice[1] != 4 or stage61_probe_reverse_slice[2] != 3:
        _ "STORYAPI_DYNAMIC_SLICE_REVERSE_CONTENT_FAIL"
        return

    _ "STORYAPI_DYNAMIC_SLICE_BOUNDS_PASS"
    return
