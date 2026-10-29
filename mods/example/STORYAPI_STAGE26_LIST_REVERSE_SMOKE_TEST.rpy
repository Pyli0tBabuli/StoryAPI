# Stage 26 in-game smoke test for list.reverse().
# Start this label manually through the game's label/jump tooling.
define stage26_probe_list = ["alpha", "beta", "gamma", "delta"]

label storyapi_stage26_list_reverse_smoke:
    $ stage26_probe_list.reverse()
    if stage26_probe_list[0] != "delta":
        _ "STORYAPI_LIST_REVERSE_FIRST_ITEM_FAIL"
        return
    if stage26_probe_list[1] != "gamma":
        _ "STORYAPI_LIST_REVERSE_SECOND_ITEM_FAIL"
        return
    if stage26_probe_list[2] != "beta":
        _ "STORYAPI_LIST_REVERSE_THIRD_ITEM_FAIL"
        return
    if stage26_probe_list[3] != "alpha":
        _ "STORYAPI_LIST_REVERSE_LAST_ITEM_FAIL"
        return

    $ stage26_probe_list.reverse()
    if stage26_probe_list[0] != "alpha" or stage26_probe_list[3] != "delta":
        _ "STORYAPI_LIST_REVERSE_TOGGLE_FAIL"
        return

    _ "STORYAPI_LIST_REVERSE_PASS"
    return
