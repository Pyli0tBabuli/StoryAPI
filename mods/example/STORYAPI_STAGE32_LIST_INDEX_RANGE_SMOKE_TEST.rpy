# Stage 32 in-game smoke test for list.index(value, start, stop).
# Start this label manually through the game's label/jump tooling.
define stage32_probe_list = ["key", "battery", "battery", "medkit", 2, 2, True, None]
default stage32_index = -1

label storyapi_stage32_list_index_range_smoke:
    $ stage32_index = stage32_probe_list.index("battery", 2)
    if stage32_index != 2:
        _ "STORYAPI_LIST_INDEX_START_FAIL"
        return

    $ stage32_index = stage32_probe_list.index("battery", 0, 2)
    if stage32_index != 1:
        _ "STORYAPI_LIST_INDEX_STOP_FAIL"
        return

    $ stage32_index = stage32_probe_list.index("battery", -6)
    if stage32_index != 2:
        _ "STORYAPI_LIST_INDEX_NEGATIVE_START_FAIL"
        return

    $ stage32_index = stage32_probe_list.index("battery", 0, -6)
    if stage32_index != 1:
        _ "STORYAPI_LIST_INDEX_NEGATIVE_STOP_FAIL"
        return

    _ "STORYAPI_LIST_INDEX_RANGE_PASS"
    return
