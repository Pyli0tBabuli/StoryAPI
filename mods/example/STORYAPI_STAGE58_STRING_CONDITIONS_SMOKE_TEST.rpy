# Stage 58 in-game smoke test for len(string) and substring membership.
# Start this label manually through the game's label/jump tooling.
define stage58_probe_text = "banana"
define stage58_probe_empty = ""
default stage58_contains_count = 0

label storyapi_stage58_string_conditions_smoke:
    $ stage58_contains_count = 0

    if len(stage58_probe_text) != 6:
        _ "STORYAPI_STRING_LENGTH_FAIL"
        return

    if "ana" in stage58_probe_text:
        $ stage58_contains_count += 1
    else:
        _ "STORYAPI_STRING_SUBSTRING_PRESENT_FAIL"
        return

    if "pear" not in stage58_probe_text:
        $ stage58_contains_count += 1
    else:
        _ "STORYAPI_STRING_SUBSTRING_ABSENT_FAIL"
        return

    if "" in stage58_probe_text:
        $ stage58_contains_count += 1
    else:
        _ "STORYAPI_STRING_EMPTY_SUBSTRING_FAIL"
        return

    if len(stage58_probe_empty) != 0:
        _ "STORYAPI_EMPTY_STRING_LENGTH_FAIL"
        return

    if stage58_contains_count != 3:
        _ "STORYAPI_STRING_MEMBERSHIP_COUNT_FAIL"
        return

    _ "STORYAPI_STRING_CONDITIONS_PASS"
    return
