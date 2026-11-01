image stage76_background = classroom.png
image stage76_sprite = june.png

transform stage76_repeat_forever:
    xalign 0.0
    yalign 0.32
    linear 0.4 xalign 1.0
    linear 0.4 xalign 0.0
    repeat

transform stage76_repeat_twice:
    xalign 0.0
    yalign 0.68
    linear 0.25 xalign 1.0
    linear 0.25 xalign 0.0
    repeat 2

label storyapi_stage76_atl_repeat_smoke:
    scene stage76_background
    show stage76_sprite as stage76_forever at stage76_repeat_forever
    show stage76_sprite as stage76_twice at stage76_repeat_twice
    _ "STORYAPI_STAGE76_REPEAT_FOREVER_REACHED"
    _ "STORYAPI_STAGE76_REPEAT_COUNT_REACHED"
    pause 1.5
    _ "STORYAPI_RENPY_ATL_REPEAT_PASS"
    return
