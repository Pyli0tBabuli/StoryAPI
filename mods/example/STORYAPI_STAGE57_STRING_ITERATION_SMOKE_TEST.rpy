# Stage 57 in-game smoke test for string iteration, reversed(string), and enumerate(string).
# Start this label manually through the game's label/jump tooling.
define stage57_probe_text = "abc"
define stage57_probe_start = 5
default stage57_direct_count = 0
default stage57_reverse_count = 0
default stage57_enumerate_count = 0
default stage57_reverse_enumerate_count = 0
default stage57_index_sum = 0

label storyapi_stage57_string_iteration_smoke:
    $ stage57_direct_count = 0
    $ stage57_reverse_count = 0
    $ stage57_enumerate_count = 0
    $ stage57_reverse_enumerate_count = 0
    $ stage57_index_sum = 0

    for stage57_character in stage57_probe_text:
        $ stage57_direct_count += 1
        if stage57_direct_count == 1:
            if stage57_character != "a":
                _ "STORYAPI_STRING_DIRECT_FIRST_FAIL"
                return
        elif stage57_direct_count == 2:
            if stage57_character != "b":
                _ "STORYAPI_STRING_DIRECT_SECOND_FAIL"
                return
        elif stage57_direct_count == 3:
            if stage57_character != "c":
                _ "STORYAPI_STRING_DIRECT_THIRD_FAIL"
                return

    if stage57_direct_count != 3:
        _ "STORYAPI_STRING_DIRECT_COUNT_FAIL"
        return

    for stage57_character in reversed(stage57_probe_text):
        $ stage57_reverse_count += 1
        if stage57_reverse_count == 1:
            if stage57_character != "c":
                _ "STORYAPI_STRING_REVERSED_FIRST_FAIL"
                return
        elif stage57_reverse_count == 2:
            if stage57_character != "b":
                _ "STORYAPI_STRING_REVERSED_SECOND_FAIL"
                return
        elif stage57_reverse_count == 3:
            if stage57_character != "a":
                _ "STORYAPI_STRING_REVERSED_THIRD_FAIL"
                return

    if stage57_reverse_count != 3:
        _ "STORYAPI_STRING_REVERSED_COUNT_FAIL"
        return

    for stage57_index, stage57_character in enumerate(stage57_probe_text, stage57_probe_start):
        $ stage57_enumerate_count += 1
        $ stage57_index_sum += stage57_index
        if stage57_enumerate_count == 1:
            if stage57_index != 5 or stage57_character != "a":
                _ "STORYAPI_ENUMERATE_STRING_FIRST_FAIL"
                return
        elif stage57_enumerate_count == 2:
            if stage57_index != 6 or stage57_character != "b":
                _ "STORYAPI_ENUMERATE_STRING_SECOND_FAIL"
                return
        elif stage57_enumerate_count == 3:
            if stage57_index != 7 or stage57_character != "c":
                _ "STORYAPI_ENUMERATE_STRING_THIRD_FAIL"
                return

    if stage57_enumerate_count != 3 or stage57_index_sum != 18:
        _ "STORYAPI_ENUMERATE_STRING_COUNT_FAIL"
        return

    for stage57_index, stage57_character in enumerate(reversed(stage57_probe_text), stage57_probe_start + 5):
        $ stage57_reverse_enumerate_count += 1
        if stage57_reverse_enumerate_count == 1:
            if stage57_index != 10 or stage57_character != "c":
                _ "STORYAPI_ENUMERATE_REVERSED_STRING_FIRST_FAIL"
                return
        elif stage57_reverse_enumerate_count == 2:
            if stage57_index != 11 or stage57_character != "b":
                _ "STORYAPI_ENUMERATE_REVERSED_STRING_SECOND_FAIL"
                return
        elif stage57_reverse_enumerate_count == 3:
            if stage57_index != 12 or stage57_character != "a":
                _ "STORYAPI_ENUMERATE_REVERSED_STRING_THIRD_FAIL"
                return

    if stage57_reverse_enumerate_count != 3:
        _ "STORYAPI_ENUMERATE_REVERSED_STRING_COUNT_FAIL"
        return

    _ "STORYAPI_STRING_ITERATION_PASS"
    return
