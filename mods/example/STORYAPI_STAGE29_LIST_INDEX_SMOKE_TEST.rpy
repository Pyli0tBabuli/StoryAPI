# Stage 29 in-game smoke test for list.index(value).
# Start this label manually through the game's label/jump tooling.
define stage29_probe_list = ["key", "battery", "battery", "medkit", 2, 2, True]
default stage29_index = -1

label storyapi_stage29_list_index_smoke:
    $ stage29_index = stage29_probe_list.index("battery")
    if stage29_index != 1:
        _ "STORYAPI_LIST_INDEX_FIRST_MATCH_FAIL"
        return

    $ stage29_index = stage29_probe_list.index(2)
    if stage29_index != 4:
        _ "STORYAPI_LIST_INDEX_NUMBER_FAIL"
        return

    $ stage29_index = stage29_probe_list.index(True)
    if stage29_index != 6:
        _ "STORYAPI_LIST_INDEX_BOOLEAN_FAIL"
        return

    _ "STORYAPI_LIST_INDEX_PASS"
    return
