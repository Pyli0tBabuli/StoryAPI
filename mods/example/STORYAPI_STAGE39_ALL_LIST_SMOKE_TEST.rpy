# Stage 39 in-game smoke test for all(list).
# Start this label manually through the game's label/jump tooling.
define stage39_probe_truthy_values = [True, 1, "active", 2]
define stage39_probe_falsey_values = [True, 1, 0, "ignored"]

label storyapi_stage39_all_list_smoke:
    _ "STORYAPI_ALL_SMOKE_STARTED"

    $ stage39_every_truthy = all(stage39_probe_truthy_values)
    if stage39_every_truthy != True:
        _ "STORYAPI_ALL_TRUTHY_FAIL"
        return

    $ stage39_not_all = all(stage39_probe_falsey_values)
    if stage39_not_all != False:
        _ "STORYAPI_ALL_FALSEY_FAIL"
        return

    if stage39_probe_truthy_values[2] != "active":
        _ "STORYAPI_ALL_SOURCE_MUTATION_FAIL"
        return

    _ "STORYAPI_ALL_LIST_PASS"
    return
