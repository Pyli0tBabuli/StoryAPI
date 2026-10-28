# Stage 24 in-game smoke test for dictionary.copy() assignment.
# Start this label manually through the game's label/jump tooling.
define stage24_probe_dict = {"theme": "dark", "count": 5}
default stage24_copy = {}

label storyapi_stage24_dictionary_copy_smoke:
    $ stage24_copy = stage24_probe_dict.copy()
    if stage24_copy["theme"] != "dark" or stage24_copy["count"] != 5:
        _ "STORYAPI_DICTIONARY_COPY_CONTENT_FAIL"
        return

    $ stage24_copy["theme"] = "light"
    if stage24_copy["theme"] != "light":
        _ "STORYAPI_DICTIONARY_COPY_MUTATION_FAIL"
        return

    if stage24_probe_dict["theme"] != "dark":
        _ "STORYAPI_DICTIONARY_COPY_SOURCE_FAIL"
        return

    $ stage24_copy["accent"] = "blue"
    if "accent" in stage24_probe_dict:
        _ "STORYAPI_DICTIONARY_COPY_KEY_ISOLATION_FAIL"
        return

    _ "STORYAPI_DICTIONARY_COPY_PASS"
    return
