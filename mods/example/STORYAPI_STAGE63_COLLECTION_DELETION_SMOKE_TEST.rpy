# Stage 63 in-game smoke test for list item and slice deletion.
# Start this label manually through the game's label/jump tooling.
define stage63_probe_inventory = ["key", "flashlight", "battery", "map", "food"]
define stage63_probe_remove_index = 1
define stage63_probe_start = 1
define stage63_probe_stop = 3

define stage63_probe_extended = [1, 2, 3, 4, 5, 6]
define stage63_probe_extended_start = 0
define stage63_probe_extended_stop = 6
define stage63_probe_extended_step = 2

define stage63_probe_reverse = [1, 2, 3, 4, 5]
define stage63_probe_reverse_start = 4
define stage63_probe_reverse_stop = 0
define stage63_probe_reverse_step = -2

define stage63_probe_settings = {"theme": "dark", "sound": "on"}

label storyapi_stage63_collection_deletion_smoke:
    $ del stage63_probe_inventory[stage63_probe_remove_index]
    if len(stage63_probe_inventory) != 4 or stage63_probe_inventory[1] != "battery":
        _ "STORYAPI_DELETE_INDEX_FAIL"
        return

    $ del stage63_probe_inventory[stage63_probe_start:stage63_probe_stop]
    if len(stage63_probe_inventory) != 2 or stage63_probe_inventory[0] != "key" or stage63_probe_inventory[1] != "food":
        _ "STORYAPI_DELETE_SLICE_FAIL"
        return

    $ del stage63_probe_extended[stage63_probe_extended_start:stage63_probe_extended_stop:stage63_probe_extended_step]
    if len(stage63_probe_extended) != 3 or stage63_probe_extended[0] != 2 or stage63_probe_extended[1] != 4 or stage63_probe_extended[2] != 6:
        _ "STORYAPI_DELETE_EXTENDED_SLICE_FAIL"
        return

    $ del stage63_probe_reverse[stage63_probe_reverse_start:stage63_probe_reverse_stop:stage63_probe_reverse_step]
    if len(stage63_probe_reverse) != 3 or stage63_probe_reverse[0] != 1 or stage63_probe_reverse[1] != 2 or stage63_probe_reverse[2] != 4:
        _ "STORYAPI_DELETE_REVERSE_SLICE_FAIL"
        return

    $ del stage63_probe_settings["theme"]
    if len(stage63_probe_settings) != 1 or "theme" in stage63_probe_settings:
        _ "STORYAPI_DELETE_DICTIONARY_KEY_FAIL"
        return

    _ "STORYAPI_COLLECTION_DELETION_PASS"
    return
