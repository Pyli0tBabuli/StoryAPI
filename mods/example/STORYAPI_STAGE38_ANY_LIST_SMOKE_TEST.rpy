# Stage 38 in-game smoke test for any(list).
# Start this label manually through the game's label/jump tooling.
define stage38_probe_values = [False, 0, "", "active", 0]
define stage38_probe_false_values = [False, 0, "", 0]

label storyapi_stage38_any_list_smoke:
    _ "STORYAPI_ANY_SMOKE_STARTED"

    $ stage38_any_found = any(stage38_probe_values)
    if stage38_any_found != True:
        _ "STORYAPI_ANY_TRUTHY_FAIL"
        return

    $ stage38_any_none = any(stage38_probe_false_values)
    if stage38_any_none != False:
        _ "STORYAPI_ANY_FALSEY_FAIL"
        return

    if stage38_probe_values[3] != "active":
        _ "STORYAPI_ANY_SOURCE_MUTATION_FAIL"
        return

    _ "STORYAPI_ANY_LIST_PASS"
    return
