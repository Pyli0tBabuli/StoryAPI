image stage81_background = classroom.png
image stage81_sprite = june.png

transform stage81_pixel_aligned:
    xpos 0.251
    ypos 0.30
    linear 1.4 xpos 0.749

transform stage81_subpixel_enabled:
    subpixel True
    xpos 0.251
    ypos 0.50
    linear 1.4 xpos 0.749

transform stage81_subpixel_toggle:
    subpixel False
    xpos 0.251
    ypos 0.70
    pause 0.2
    linear 0.4 subpixel True
    linear 1.0 xpos 0.749

label storyapi_stage81_atl_subpixel_smoke:
    scene stage81_background
    show stage81_sprite as stage81_pixel at stage81_pixel_aligned
    show stage81_sprite as stage81_smooth at stage81_subpixel_enabled
    show stage81_sprite as stage81_toggle at stage81_subpixel_toggle
    _ "STORYAPI_STAGE81_DEFAULT_SUBPIXEL_REACHED"
    _ "STORYAPI_STAGE81_ENABLED_SUBPIXEL_REACHED"
    pause 1.8
    _ "STORYAPI_STAGE81_INTERPOLATION_TOGGLE_REACHED"
    _ "STORYAPI_RENPY_ATL_SUBPIXEL_PASS"
    return
