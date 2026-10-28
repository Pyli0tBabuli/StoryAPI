# Stage 19 in-game smoke test for dictionary key/value iteration.
# Start this label manually through the game's label/jump tooling.
define stage19_probe_dict = {"alpha": 2, "beta": 3, "gamma": 4}
define stage19_empty_dict = {}
default stage19_probe_count = 0
default stage19_probe_total = 0
default stage19_seen_alpha = False
default stage19_seen_beta = False
default stage19_seen_gamma = False

label storyapi_stage19_for_dictionary_items_smoke:
    $ stage19_probe_count = 0
    $ stage19_probe_total = 0
    $ stage19_seen_alpha = False
    $ stage19_seen_beta = False
    $ stage19_seen_gamma = False

    for stage19_key, stage19_value in stage19_probe_dict.items():
        $ stage19_probe_count += 1
        $ stage19_probe_total += stage19_value
        if stage19_key == "alpha":
            $ stage19_seen_alpha = True
        elif stage19_key == "beta":
            $ stage19_seen_beta = True
        elif stage19_key == "gamma":
            $ stage19_seen_gamma = True

    if stage19_probe_count != 3:
        _ "STORYAPI_FOR_DICTIONARY_ITEMS_COUNT_FAIL"
        return
    if stage19_probe_total != 9:
        _ "STORYAPI_FOR_DICTIONARY_ITEMS_TOTAL_FAIL"
        return
    if not stage19_seen_alpha or not stage19_seen_beta or not stage19_seen_gamma:
        _ "STORYAPI_FOR_DICTIONARY_ITEMS_KEYS_FAIL"
        return

    $ stage19_probe_count = 0
    for stage19_key, stage19_value in stage19_empty_dict.items():
        $ stage19_probe_count += 1
    if stage19_probe_count != 0:
        _ "STORYAPI_FOR_DICTIONARY_ITEMS_EMPTY_FAIL"
        return

    _ "STORYAPI_FOR_DICTIONARY_ITEMS_PASS"
    return
