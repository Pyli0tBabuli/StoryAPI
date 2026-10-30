# Stage 64 in-game smoke test for dynamic dictionary method keys.
# Start this label manually through the game's label/jump tooling.
define stage64_probe_settings = {"theme": "dark", "accent": "blue", "remove_me": 42}
define stage64_probe_selected_key = "theme"
define stage64_probe_new_key = "highlight"
define stage64_probe_pop_key = "remove_me"
define stage64_probe_missing_key = "not_present"

label storyapi_stage64_dynamic_dictionary_method_keys_smoke:
    $ stage64_probe_theme = stage64_probe_settings.get(stage64_probe_selected_key)
    if stage64_probe_theme != "dark":
        _ "STORYAPI_DYNAMIC_DICT_GET_FAIL"
        return

    $ stage64_probe_default = stage64_probe_settings.get(stage64_probe_missing_key, "fallback")
    if stage64_probe_default != "fallback":
        _ "STORYAPI_DYNAMIC_DICT_GET_DEFAULT_FAIL"
        return

    $ stage64_probe_inserted = stage64_probe_settings.setdefault(stage64_probe_new_key, "green")
    if stage64_probe_inserted != "green" or stage64_probe_settings["highlight"] != "green":
        _ "STORYAPI_DYNAMIC_DICT_SETDEFAULT_INSERT_FAIL"
        return

    $ stage64_probe_existing = stage64_probe_settings.setdefault(stage64_probe_new_key, "red")
    if stage64_probe_existing != "green" or stage64_probe_settings["highlight"] != "green":
        _ "STORYAPI_DYNAMIC_DICT_SETDEFAULT_EXISTING_FAIL"
        return

    $ stage64_probe_removed = stage64_probe_settings.pop(stage64_probe_pop_key, None)
    if stage64_probe_removed != 42 or "remove_me" in stage64_probe_settings:
        _ "STORYAPI_DYNAMIC_DICT_POP_FAIL"
        return

    $ stage64_probe_missing_pop = stage64_probe_settings.pop(stage64_probe_missing_key, "fallback")
    if stage64_probe_missing_pop != "fallback" or stage64_probe_missing_key in stage64_probe_settings:
        _ "STORYAPI_DYNAMIC_DICT_POP_DEFAULT_FAIL"
        return

    _ "STORYAPI_DYNAMIC_DICTIONARY_METHOD_KEYS_PASS"
    return
