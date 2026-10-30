# Stage 62 in-game smoke test for list slice assignment.
# Start this label manually through the game's label/jump tooling.
define stage62_probe_numbers = [1, 2, 3, 4, 5]
define stage62_probe_replacement = [20, 30, 40]
define stage62_probe_start = 1
define stage62_probe_stop = 3

define stage62_probe_insert_target = [1, 2, 3]
define stage62_probe_insert_values = [8, 9]
define stage62_probe_insert_at = 1

define stage62_probe_stride_target = [1, 2, 3, 4, 5, 6]
define stage62_probe_stride_values = [10, 30, 50]
define stage62_probe_stride_start = 0
define stage62_probe_stride_stop = 6
define stage62_probe_stride_step = 2

define stage62_probe_reverse_target = [1, 2, 3, 4, 5]
define stage62_probe_reverse_values = [50, 40, 30]
define stage62_probe_reverse_start = 4
define stage62_probe_reverse_stop = 1
define stage62_probe_reverse_step = -1

define stage62_probe_text_target = ["a", "b", "c"]
define stage62_probe_text_replacement = "😀X"

label storyapi_stage62_sequence_slice_assignment_smoke:
    $ stage62_probe_numbers[stage62_probe_start:stage62_probe_stop] = stage62_probe_replacement
    if len(stage62_probe_numbers) != 6:
        _ "STORYAPI_SLICE_ASSIGN_RESIZE_FAIL"
        return
    if stage62_probe_numbers[1] != 20 or stage62_probe_numbers[2] != 30 or stage62_probe_numbers[3] != 40:
        _ "STORYAPI_SLICE_ASSIGN_RESIZE_CONTENT_FAIL"
        return

    $ stage62_probe_insert_target[stage62_probe_insert_at:stage62_probe_insert_at] = stage62_probe_insert_values
    if len(stage62_probe_insert_target) != 5 or stage62_probe_insert_target[1] != 8 or stage62_probe_insert_target[2] != 9:
        _ "STORYAPI_SLICE_ASSIGN_INSERT_FAIL"
        return

    $ stage62_probe_stride_target[stage62_probe_stride_start:stage62_probe_stride_stop:stage62_probe_stride_step] = stage62_probe_stride_values
    if stage62_probe_stride_target[0] != 10 or stage62_probe_stride_target[2] != 30 or stage62_probe_stride_target[4] != 50:
        _ "STORYAPI_SLICE_ASSIGN_STRIDE_FAIL"
        return

    $ stage62_probe_reverse_target[stage62_probe_reverse_start:stage62_probe_reverse_stop:stage62_probe_reverse_step] = stage62_probe_reverse_values
    if stage62_probe_reverse_target[2] != 30 or stage62_probe_reverse_target[3] != 40 or stage62_probe_reverse_target[4] != 50:
        _ "STORYAPI_SLICE_ASSIGN_REVERSE_FAIL"
        return

    $ stage62_probe_text_target[1:2] = stage62_probe_text_replacement
    if len(stage62_probe_text_target) != 4 or stage62_probe_text_target[1] != "😀" or stage62_probe_text_target[2] != "X":
        _ "STORYAPI_SLICE_ASSIGN_STRING_FAIL"
        return

    _ "STORYAPI_SEQUENCE_SLICE_ASSIGNMENT_PASS"
    return
