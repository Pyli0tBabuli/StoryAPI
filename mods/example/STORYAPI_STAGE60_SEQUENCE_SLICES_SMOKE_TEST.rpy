# Stage 60 in-game smoke test for list and string slices.
# Start this label manually through the game's label/jump tooling.
define stage60_probe_text = "A😀BC"
define stage60_probe_forward = stage60_probe_text[1:3]
define stage60_probe_prefix = stage60_probe_text[:2]
define stage60_probe_reverse = stage60_probe_text[::-1]
define stage60_probe_stride = stage60_probe_text[::2]
define stage60_probe_numbers = [1, 2, 3, 4, 5]
define stage60_probe_middle = stage60_probe_numbers[1:4]
define stage60_probe_list_reverse = stage60_probe_numbers[::-1]
define stage60_probe_empty = stage60_probe_numbers[99:101]
define stage60_probe_chained = stage60_probe_numbers[1:4][1]

label storyapi_stage60_sequence_slices_smoke:
    if len(stage60_probe_forward) != 2:
        _ "STORYAPI_SLICE_STRING_LENGTH_FAIL"
        return

    if stage60_probe_forward != "😀B":
        _ "STORYAPI_SLICE_STRING_FORWARD_FAIL"
        return

    if stage60_probe_prefix != "A😀":
        _ "STORYAPI_SLICE_STRING_PREFIX_FAIL"
        return

    if stage60_probe_reverse != "CB😀A":
        _ "STORYAPI_SLICE_STRING_REVERSE_FAIL"
        return

    if stage60_probe_stride != "AB":
        _ "STORYAPI_SLICE_STRING_STEP_FAIL"
        return

    if len(stage60_probe_middle) != 3:
        _ "STORYAPI_SLICE_LIST_LENGTH_FAIL"
        return

    if stage60_probe_middle[0] != 2 or stage60_probe_middle[-1] != 4:
        _ "STORYAPI_SLICE_LIST_CONTENT_FAIL"
        return

    if stage60_probe_list_reverse[0] != 5 or stage60_probe_list_reverse[-1] != 1:
        _ "STORYAPI_SLICE_LIST_REVERSE_FAIL"
        return

    if len(stage60_probe_empty) != 0:
        _ "STORYAPI_SLICE_EMPTY_FAIL"
        return

    if stage60_probe_chained != 3:
        _ "STORYAPI_SLICE_CHAINED_INDEX_FAIL"
        return

    _ "STORYAPI_SEQUENCE_SLICES_PASS"
    return
