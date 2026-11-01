image stage77_background = classroom.png
image stage77_sprite = june.png

transform stage77_ease_back:
    xalign 0.0
    yalign 0.28
    ease_back 0.9 xalign 1.0

transform stage77_easein_bounce:
    xalign 0.0
    yalign 0.50
    easein_bounce 0.9 xalign 1.0

transform stage77_easeout_elastic:
    xalign 0.0
    yalign 0.72
    easeout_elastic 0.9 xalign 1.0

label storyapi_stage77_atl_penner_warpers_smoke:
    scene stage77_background
    show stage77_sprite as stage77_back at stage77_ease_back
    show stage77_sprite as stage77_bounce at stage77_easein_bounce
    show stage77_sprite as stage77_elastic at stage77_easeout_elastic
    _ "STORYAPI_STAGE77_PENNER_INOUT_REACHED"
    _ "STORYAPI_STAGE77_PENNER_IN_REACHED"
    _ "STORYAPI_STAGE77_PENNER_OUT_REACHED"
    pause 1.2
    _ "STORYAPI_RENPY_ATL_PENNER_WARPERS_PASS"
    return
