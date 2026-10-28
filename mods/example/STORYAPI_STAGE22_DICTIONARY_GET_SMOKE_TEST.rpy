# Stage 22 in-game smoke test for dictionary.get(key[, default]) assignments.
# Start this label manually through the game's label/jump tooling.
define stage22_probe_dict = {"theme": "dark", "count": 5}
default stage22_theme = ""
default stage22_fallback = ""
default stage22_missing = "sentinel"

label storyapi_stage22_dictionary_get_smoke:
    $ stage22_theme = stage22_probe_dict.get("theme")
    if stage22_theme != "dark":
        _ "STORYAPI_DICTIONARY_GET_EXISTING_FAIL"
        return

    $ stage22_fallback = stage22_probe_dict.get("missing", "fallback")
    if stage22_fallback != "fallback":
        _ "STORYAPI_DICTIONARY_GET_DEFAULT_FAIL"
        return

    $ stage22_missing = stage22_probe_dict.get("missing")
    if stage22_missing != None:
        _ "STORYAPI_DICTIONARY_GET_NONE_FAIL"
        return

    if stage22_probe_dict["theme"] != "dark" or stage22_probe_dict["count"] != 5:
        _ "STORYAPI_DICTIONARY_GET_MUTATION_FAIL"
        return

    _ "STORYAPI_DICTIONARY_GET_PASS"
    return
