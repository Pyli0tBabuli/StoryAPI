# Stage 28 in-game smoke test for list.copy().
# Start this label manually through the game's label/jump tooling.
define stage28_probe_list = ["alpha", "beta", "gamma"]
default stage28_copy = []

label storyapi_stage28_list_copy_smoke:
    $ stage28_copy = stage28_probe_list.copy()
    if stage28_copy[0] != "alpha" or stage28_copy[2] != "gamma":
        _ "STORYAPI_LIST_COPY_CONTENT_FAIL"
        return

    $ stage28_copy[0] = "changed"
    if stage28_copy[0] != "changed":
        _ "STORYAPI_LIST_COPY_MUTATION_FAIL"
        return

    if stage28_probe_list[0] != "alpha":
        _ "STORYAPI_LIST_COPY_SOURCE_FAIL"
        return

    $ stage28_copy.append("delta")
    if len(stage28_copy) != 4 or len(stage28_probe_list) != 3:
        _ "STORYAPI_LIST_COPY_LENGTH_ISOLATION_FAIL"
        return

    _ "STORYAPI_LIST_COPY_PASS"
    return
