# Stage 23 in-game smoke test for restricted dictionary.setdefault(key[, default]) assignment.
# Start this label manually through the game's label/jump tooling.
define stage23_probe_dict = {"theme": "dark", "count": 5}
default stage23_existing = ""
default stage23_inserted = ""
default stage23_repeat = ""
default stage23_none = "sentinel"

label storyapi_stage23_dictionary_setdefault_smoke:
    $ stage23_existing = stage23_probe_dict.setdefault("theme", "light")
    if stage23_existing != "dark":
        _ "STORYAPI_DICTIONARY_SETDEFAULT_EXISTING_FAIL"
        return

    if stage23_probe_dict["theme"] != "dark":
        _ "STORYAPI_DICTIONARY_SETDEFAULT_OVERWRITE_FAIL"
        return

    $ stage23_inserted = stage23_probe_dict.setdefault("accent", "blue")
    if stage23_inserted != "blue":
        _ "STORYAPI_DICTIONARY_SETDEFAULT_INSERT_VALUE_FAIL"
        return

    if stage23_probe_dict["accent"] != "blue":
        _ "STORYAPI_DICTIONARY_SETDEFAULT_INSERT_KEY_FAIL"
        return

    $ stage23_repeat = stage23_probe_dict.setdefault("accent", "red")
    if stage23_repeat != "blue" or stage23_probe_dict["accent"] != "blue":
        _ "STORYAPI_DICTIONARY_SETDEFAULT_REPEAT_FAIL"
        return

    $ stage23_none = stage23_probe_dict.setdefault("nullable")
    if stage23_none != None:
        _ "STORYAPI_DICTIONARY_SETDEFAULT_NONE_VALUE_FAIL"
        return

    if "nullable" not in stage23_probe_dict:
        _ "STORYAPI_DICTIONARY_SETDEFAULT_NONE_KEY_FAIL"
        return

    _ "STORYAPI_DICTIONARY_SETDEFAULT_PASS"
    return
