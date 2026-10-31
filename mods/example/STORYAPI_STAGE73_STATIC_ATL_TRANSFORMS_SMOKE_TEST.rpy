image stage73_background = classroom.png
image stage73_sprite = june.png

transform stage73_bottom_right:
    xalign 1.0
    yalign 1.0
    zoom 0.65
    alpha 0.8

transform stage73_offset:
    xpos 140
    yoffset -40
    zoom 0.8

transform stage73_center_tilt:
    xalign 0.5
    yalign 0.5
    zoom 0.9
    rotate -8

label storyapi_stage73_static_atl_transforms_smoke:
    scene stage73_background
    show stage73_sprite as stage73_custom at stage73_bottom_right
    _ "STORYAPI_STAGE73_CUSTOM_STATIC_TRANSFORM_REACHED"
    show monika 1a at stage73_center_tilt
    _ "STORYAPI_STAGE73_NATIVE_POSE_STATIC_TRANSFORM_REACHED"
    show stage73_sprite as stage73_positioned at stage73_offset
    _ "STORYAPI_RENPY_STATIC_ATL_TRANSFORMS_PASS"
    return
