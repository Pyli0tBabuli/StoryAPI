# Stage 25 in-game smoke test for dictionary.pop(key, default).
# Start this label manually through the game's label/jump tooling.
define stage25_probe_dict = {"theme": "dark", "count": 5}
default stage25_removed = ""

label storyapi_stage25_dictionary_pop_default_smoke:
    $ stage25_removed = stage25_probe_dict.pop("theme", "fallback")
    if stage25_removed != "dark":
        _ "STORYAPI_DICTIONARY_POP_DEFAULT_EXISTING_VALUE_FAIL"
        return

    if "theme" in stage25_probe_dict:
        _ "STORYAPI_DICTIONARY_POP_DEFAULT_EXISTING_REMOVE_FAIL"
        return

    $ stage25_removed = stage25_probe_dict.pop("missing", "fallback")
    if stage25_removed != "fallback":
        _ "STORYAPI_DICTIONARY_POP_DEFAULT_FALLBACK_FAIL"
        return

    if "missing" in stage25_probe_dict:
        _ "STORYAPI_DICTIONARY_POP_DEFAULT_INSERTED_KEY_FAIL"
        return

    $ stage25_removed = stage25_probe_dict.pop("missing_count", 7)
    if stage25_removed != 7:
        _ "STORYAPI_DICTIONARY_POP_DEFAULT_NUMBER_FAIL"
        return

    _ "STORYAPI_DICTIONARY_POP_DEFAULT_PASS"
    return
