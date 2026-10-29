# Stage 27 in-game smoke test for list.count(value).
# Start this label manually through the game's label/jump tooling.
define stage27_probe_list = ["key", "battery", "battery", "battery", 2, 2, True]
default stage27_count = 0

label storyapi_stage27_list_count_smoke:
    $ stage27_count = stage27_probe_list.count("battery")
    if stage27_count != 3:
        _ "STORYAPI_LIST_COUNT_STRING_FAIL"
        return

    $ stage27_count = stage27_probe_list.count(2)
    if stage27_count != 2:
        _ "STORYAPI_LIST_COUNT_NUMBER_FAIL"
        return

    $ stage27_count = stage27_probe_list.count("missing")
    if stage27_count != 0:
        _ "STORYAPI_LIST_COUNT_ZERO_FAIL"
        return

    _ "STORYAPI_LIST_COUNT_PASS"
    return
