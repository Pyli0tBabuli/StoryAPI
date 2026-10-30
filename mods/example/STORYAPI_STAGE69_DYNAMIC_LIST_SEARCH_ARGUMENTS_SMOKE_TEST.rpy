define stage69_probe_inventory = ["battery", "key", "battery", "medkit", 2, 2, True, None]
define stage69_probe_item = "battery"
define stage69_probe_number = 2
define stage69_probe_flag = True
define stage69_probe_none = None
define stage69_probe_battery_count = -999
define stage69_probe_number_count = -999
define stage69_probe_flag_count = -999
define stage69_probe_none_count = -999
define stage69_probe_battery_index = -999
define stage69_probe_number_index = -999
define stage69_probe_flag_index = -999
define stage69_probe_none_index = -999

label storyapi_stage69_dynamic_list_search_arguments_smoke:
    $ stage69_probe_battery_count = stage69_probe_inventory.count(stage69_probe_item)
    $ stage69_probe_number_count = stage69_probe_inventory.count(stage69_probe_number)
    $ stage69_probe_flag_count = stage69_probe_inventory.count(stage69_probe_flag)
    $ stage69_probe_none_count = stage69_probe_inventory.count(stage69_probe_none)
    $ stage69_probe_battery_index = stage69_probe_inventory.index(stage69_probe_item)
    $ stage69_probe_number_index = stage69_probe_inventory.index(stage69_probe_number)
    $ stage69_probe_flag_index = stage69_probe_inventory.index(stage69_probe_flag)
    $ stage69_probe_none_index = stage69_probe_inventory.index(stage69_probe_none)
    _ "STORYAPI_STAGE69_CHECK_REACHED"
    if stage69_probe_battery_count == 2 and stage69_probe_number_count == 2 and stage69_probe_flag_count == 1 and stage69_probe_none_count == 1 and stage69_probe_battery_index == 0 and stage69_probe_number_index == 4 and stage69_probe_flag_index == 6 and stage69_probe_none_index == 7:
        _ "STORYAPI_DYNAMIC_LIST_SEARCH_ARGUMENTS_PASS"
    else:
        _ "STORYAPI_DYNAMIC_LIST_SEARCH_ARGUMENTS_FAIL"
    return
