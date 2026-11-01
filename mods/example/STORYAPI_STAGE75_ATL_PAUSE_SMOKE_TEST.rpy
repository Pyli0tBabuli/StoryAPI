image stage75_background = classroom.png
image stage75_sprite = june.png

transform stage75_keyword_pause:
    xalign 0.0
    yalign 0.5
    linear 0.6 xalign 1.0
    pause 0.55
    linear 0.6 xalign 0.0

transform stage75_number_pause:
    xalign 1.0
    yalign 0.5
    linear 0.6 xalign 0.0
    0.55
    linear 0.6 xalign 1.0

label storyapi_stage75_atl_pause_smoke:
    scene stage75_background
    show stage75_sprite as stage75_custom at stage75_keyword_pause
    _ "STORYAPI_STAGE75_EXPLICIT_PAUSE_REACHED"
    pause 1.75
    hide stage75_custom
    show stage75_sprite as stage75_custom at stage75_number_pause
    _ "STORYAPI_STAGE75_NUMBER_PAUSE_REACHED"
    pause 1.75
    _ "STORYAPI_RENPY_ATL_PAUSE_PASS"
    return
