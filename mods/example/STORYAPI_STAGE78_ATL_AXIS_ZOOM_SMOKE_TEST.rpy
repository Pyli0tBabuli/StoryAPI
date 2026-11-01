image stage78_background = classroom.png
image stage78_sprite = june.png

transform stage78_flip_horizontal:
    xalign 0.28
    yalign 0.5
    xzoom 1.0
    linear 0.9 xzoom -1.0

transform stage78_stretch_vertical:
    xalign 0.72
    yalign 0.5
    yzoom 1.0
    linear 0.9 yzoom 1.6

label storyapi_stage78_atl_axis_zoom_smoke:
    scene stage78_background
    show stage78_sprite as stage78_horizontal at stage78_flip_horizontal
    show stage78_sprite as stage78_vertical at stage78_stretch_vertical
    _ "STORYAPI_STAGE78_XZOOM_FLIP_REACHED"
    _ "STORYAPI_STAGE78_YZOOM_STRETCH_REACHED"
    pause 1.1
    _ "STORYAPI_RENPY_ATL_AXIS_ZOOM_PASS"
    return
