image stage74_background = classroom.png
image stage74_sprite = june.png

transform stage74_linear_motion:
    xalign 0.0
    yalign 0.5
    linear 1.25 xalign 1.0

transform stage74_eased_motion:
    xalign 0.5
    yalign 0.5
    zoom 0.65
    alpha 0.35
    rotate 0
    ease 0.8:
        zoom 1.0
        alpha 1.0
    easein 0.4 rotate -12.0
    easeout 0.4:
        rotate 12.0

label storyapi_stage74_atl_interpolation_smoke:
    scene stage74_background
    show stage74_sprite as stage74_custom at stage74_linear_motion
    _ "STORYAPI_STAGE74_CUSTOM_LINEAR_REACHED"
    show monika 1a at stage74_eased_motion
    _ "STORYAPI_STAGE74_NATIVE_EASING_REACHED"
    pause 2.25
    _ "STORYAPI_RENPY_ATL_INTERPOLATION_PASS"
    return
