# Stage 21 in-game smoke test for explicit dictionary keys() iteration.
# Start this label manually through the game's label/jump tooling.
define stage21_probe_dict = {"alpha": 2, "beta": 3, "gamma": 4}
define stage21_empty_dict = {}
default stage21_probe_count = 0
default stage21_seen_alpha = False
default stage21_seen_beta = False
default stage21_seen_gamma = False

label storyapi_stage21_for_dictionary_keys_smoke:
    $ stage21_probe_count = 0
    $ stage21_seen_alpha = False
    $ stage21_seen_beta = False
    $ stage21_seen_gamma = False

    for stage21_key in stage21_probe_dict.keys():
        $ stage21_probe_count += 1
        if stage21_key == "alpha":
            $ stage21_seen_alpha = True
        elif stage21_key == "beta":
            $ stage21_seen_beta = True
        elif stage21_key == "gamma":
            $ stage21_seen_gamma = True

    if stage21_probe_count != 3:
        _ "STORYAPI_FOR_DICTIONARY_KEYS_COUNT_FAIL"
        return
    if not stage21_seen_alpha or not stage21_seen_beta or not stage21_seen_gamma:
        _ "STORYAPI_FOR_DICTIONARY_KEYS_CONTENT_FAIL"
        return

    $ stage21_probe_count = 0
    for stage21_key in stage21_empty_dict.keys():
        $ stage21_probe_count += 1
    if stage21_probe_count != 0:
        _ "STORYAPI_FOR_DICTIONARY_KEYS_EMPTY_FAIL"
        return

    _ "STORYAPI_FOR_DICTIONARY_KEYS_PASS"
    return
