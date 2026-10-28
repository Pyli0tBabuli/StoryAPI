# Stage 20 in-game smoke test for dictionary value iteration.
# Start this label manually through the game's label/jump tooling.
define stage20_probe_dict = {"alpha": 2, "beta": 3, "gamma": 4}
define stage20_empty_dict = {}
default stage20_probe_count = 0
default stage20_probe_total = 0
default stage20_seen_two = False
default stage20_seen_three = False
default stage20_seen_four = False

label storyapi_stage20_for_dictionary_values_smoke:
    $ stage20_probe_count = 0
    $ stage20_probe_total = 0
    $ stage20_seen_two = False
    $ stage20_seen_three = False
    $ stage20_seen_four = False

    for stage20_value in stage20_probe_dict.values():
        $ stage20_probe_count += 1
        $ stage20_probe_total += stage20_value
        if stage20_value == 2:
            $ stage20_seen_two = True
        elif stage20_value == 3:
            $ stage20_seen_three = True
        elif stage20_value == 4:
            $ stage20_seen_four = True

    if stage20_probe_count != 3:
        _ "STORYAPI_FOR_DICTIONARY_VALUES_COUNT_FAIL"
        return
    if stage20_probe_total != 9:
        _ "STORYAPI_FOR_DICTIONARY_VALUES_TOTAL_FAIL"
        return
    if not stage20_seen_two or not stage20_seen_three or not stage20_seen_four:
        _ "STORYAPI_FOR_DICTIONARY_VALUES_CONTENT_FAIL"
        return

    $ stage20_probe_count = 0
    for stage20_value in stage20_empty_dict.values():
        $ stage20_probe_count += 1
    if stage20_probe_count != 0:
        _ "STORYAPI_FOR_DICTIONARY_VALUES_EMPTY_FAIL"
        return

    _ "STORYAPI_FOR_DICTIONARY_VALUES_PASS"
    return
