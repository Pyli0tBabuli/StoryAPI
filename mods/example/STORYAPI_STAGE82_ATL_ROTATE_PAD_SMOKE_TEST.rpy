image stage82_background = classroom.png
image stage82_sprite = june.png

transform stage82_rotate_pad_enabled:
    rotate_pad True
    xanchor 0
    yanchor 0
    xpos 0.14
    ypos 0.22
    rotate 35

transform stage82_rotate_pad_disabled:
    rotate_pad False
    xanchor 0
    yanchor 0
    xpos 0.61
    ypos 0.22
    rotate 35

transform stage82_rotate_pad_toggle:
    rotate_pad True
    xanchor 0
    yanchor 0
    xpos 0.39
    ypos 0.68
    rotate 35
    pause 0.3
    linear 0.5 rotate_pad False

label storyapi_stage82_atl_rotate_pad_smoke:
    scene stage82_background
    show stage82_sprite as stage82_padded at stage82_rotate_pad_enabled
    show stage82_sprite as stage82_tight at stage82_rotate_pad_disabled
    show stage82_sprite as stage82_toggle at stage82_rotate_pad_toggle
    _ "STORYAPI_STAGE82_ROTATE_PAD_ENABLED_REACHED"
    _ "STORYAPI_STAGE82_ROTATE_PAD_DISABLED_REACHED"
    pause 1.0
    _ "STORYAPI_STAGE82_ROTATE_PAD_TOGGLE_REACHED"
    _ "STORYAPI_RENPY_ATL_ROTATE_PAD_PASS"
    return
