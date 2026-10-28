# Stage 18 in-game smoke test for dictionary-key iteration.
# Start this label manually through the game's label/jump tooling.
define stage18_probe_dict = {"alpha": 1, "beta": 2, "gamma": 3}
define stage18_empty_dict = {}
default stage18_probe_count = 0
default stage18_probe_total = 0
default stage18_seen_alpha = False
default stage18_seen_beta = False
default stage18_seen_gamma = False

label storyapi_stage18_for_dictionary_smoke:
    $ stage18_probe_count = 0
    $ stage18_probe_total = 0
    $ stage18_seen_alpha = False
    $ stage18_seen_beta = False
    $ stage18_seen_gamma = False

    for stage18_key in stage18_probe_dict:
        $ stage18_probe_count += 1
        $ stage18_probe_total += stage18_probe_dict[stage18_key]
        if stage18_key == "alpha":
            $ stage18_seen_alpha = True
        elif stage18_key == "beta":
            $ stage18_seen_beta = True
        elif stage18_key == "gamma":
            $ stage18_seen_gamma = True

    if stage18_probe_count != 3 or stage18_probe_total != 6:
        _ "STORYAPI_FOR_DICTIONARY_ITERATION_TOTAL_FAIL"
        return
    if not stage18_seen_alpha or not stage18_seen_beta or not stage18_seen_gamma:
        _ "STORYAPI_FOR_DICTIONARY_ITERATION_KEYS_FAIL"
        return

    $ stage18_probe_count = 0
    for stage18_key in stage18_empty_dict:
        $ stage18_probe_count += 1
    if stage18_probe_count != 0:
        _ "STORYAPI_FOR_DICTIONARY_ITERATION_EMPTY_FAIL"
        return

    _ "STORYAPI_FOR_DICTIONARY_ITERATION_PASS"
    return
