image stage84_background = classroom.png
image stage84_sprite = june.png

transform stage84_outer_anchor:
    transform_anchor False
    xanchor 0.0
    yanchor 0.0
    xpos 0.27
    ypos 0.38
    zoom 0.72
    rotate 35

transform stage84_child_anchor:
    transform_anchor True
    xanchor 0.0
    yanchor 0.0
    xpos 0.73
    ypos 0.38
    zoom 0.72
    rotate 35

transform stage84_anchor_toggle:
    transform_anchor False
    xanchor 0.0
    yanchor 0.0
    xpos 0.50
    ypos 0.73
    zoom 0.72
    rotate 35
    pause 0.4
    linear 0.5 transform_anchor True

label storyapi_stage84_atl_transform_anchor_smoke:
    scene stage84_background
    show stage84_sprite as stage84_outer at stage84_outer_anchor
    show stage84_sprite as stage84_child at stage84_child_anchor
    show stage84_sprite as stage84_toggle at stage84_anchor_toggle
    _ "STORYAPI_STAGE84_TRANSFORM_ANCHOR_FALSE_REACHED"
    _ "STORYAPI_STAGE84_TRANSFORM_ANCHOR_TRUE_REACHED"
    pause 1.0
    _ "STORYAPI_STAGE84_TRANSFORM_ANCHOR_TOGGLE_REACHED"
    _ "STORYAPI_RENPY_ATL_TRANSFORM_ANCHOR_PASS"
    return
