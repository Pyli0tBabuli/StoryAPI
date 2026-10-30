# Stage 59 in-game smoke test for Unicode-aware string indexing.
# Start this label manually through the game's label/jump tooling.
define stage59_probe_text = "A😀BC"
define stage59_probe_first = stage59_probe_text[0]
define stage59_probe_emoji = stage59_probe_text[1]
define stage59_probe_last = stage59_probe_text[-1]
define stage59_probe_index = 2
define stage59_probe_dynamic = stage59_probe_text[stage59_probe_index]
define stage59_probe_nested = stage59_probe_text[1][0]

label storyapi_stage59_string_index_smoke:
    if len(stage59_probe_text) != 4:
        _ "STORYAPI_STRING_INDEX_LENGTH_FAIL"
        return

    if stage59_probe_first != "A":
        _ "STORYAPI_STRING_INDEX_FIRST_FAIL"
        return

    if stage59_probe_emoji != "😀":
        _ "STORYAPI_STRING_INDEX_UNICODE_FAIL"
        return

    if stage59_probe_last != "C":
        _ "STORYAPI_STRING_INDEX_NEGATIVE_FAIL"
        return

    if stage59_probe_dynamic != "B":
        _ "STORYAPI_STRING_INDEX_DYNAMIC_FAIL"
        return

    if stage59_probe_nested != "😀":
        _ "STORYAPI_STRING_INDEX_NESTED_FAIL"
        return

    _ "STORYAPI_STRING_INDEX_PASS"
    return
