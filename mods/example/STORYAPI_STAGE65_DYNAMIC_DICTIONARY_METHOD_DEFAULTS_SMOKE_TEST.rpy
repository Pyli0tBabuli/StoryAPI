# Stage 65 in-game smoke test for variable dictionary method defaults.
# Start this label manually through the game's label/jump tooling.
define stage65_probe_settings = {"theme": "dark", "count": 5}
define stage65_probe_missing_key = "not_present"
define stage65_probe_fallback = "first fallback"
define stage65_probe_number = 23

label storyapi_stage65_dynamic_dictionary_method_defaults_smoke:
    $ stage65_probe_get = stage65_probe_settings.get(stage65_probe_missing_key, stage65_probe_fallback)
    if stage65_probe_get != "first fallback":
        _ "STORYAPI_DYNAMIC_DICT_GET_VARIABLE_DEFAULT_FAIL"
        return

    $ stage65_probe_fallback = "updated fallback"
    $ stage65_probe_get_again = stage65_probe_settings.get(stage65_probe_missing_key, stage65_probe_fallback)
    if stage65_probe_get_again != "updated fallback":
        _ "STORYAPI_DYNAMIC_DICT_GET_REEVALUATION_FAIL"
        return

    $ stage65_probe_inserted = stage65_probe_settings.setdefault("accent", stage65_probe_fallback)
    if stage65_probe_inserted != "updated fallback" or stage65_probe_settings["accent"] != "updated fallback":
        _ "STORYAPI_DYNAMIC_DICT_SETDEFAULT_VARIABLE_FAIL"
        return

    $ stage65_probe_popped = stage65_probe_settings.pop("missing_pop", stage65_probe_number)
    if stage65_probe_popped != 23 or "missing_pop" in stage65_probe_settings:
        _ "STORYAPI_DYNAMIC_DICT_POP_VARIABLE_FAIL"
        return

    _ "STORYAPI_DYNAMIC_DICTIONARY_METHOD_DEFAULTS_PASS"
    return
