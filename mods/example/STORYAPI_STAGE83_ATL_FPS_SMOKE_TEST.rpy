image stage83_background = classroom.png
image stage83_sprite = june.png

transform stage83_low_fps:
    fps 4
    subpixel True
    zoom 0.25
    xalign 0.0
    yalign 0.22
    linear 1.2 xalign 1.0

transform stage83_high_fps:
    fps 12
    subpixel True
    zoom 0.25
    xalign 0.0
    yalign 0.50
    linear 1.2 xalign 1.0

transform stage83_default_fps:
    subpixel True
    zoom 0.25
    xalign 0.0
    yalign 0.78
    linear 1.2 xalign 1.0

label storyapi_stage83_atl_fps_smoke:
    scene stage83_background
    show stage83_sprite as stage83_low at stage83_low_fps
    show stage83_sprite as stage83_high at stage83_high_fps
    show stage83_sprite as stage83_default at stage83_default_fps
    _ "STORYAPI_STAGE83_FPS_4_REACHED"
    _ "STORYAPI_STAGE83_FPS_12_REACHED"
    _ "STORYAPI_STAGE83_FPS_DEFAULT_REACHED"
    pause 1.5
    _ "STORYAPI_RENPY_ATL_FPS_PASS"
    return
