image stage80_background = classroom.png
image stage80_sprite = june.png

transform stage80_horizontal_center:
    xcenter 0.25
    ycenter 0.5
    linear 0.9 xcenter 0.75

transform stage80_vertical_center:
    xcenter 0.5
    ycenter 0.20
    ease 0.9 ycenter 0.80

transform stage80_pixel_center:
    xcenter 640
    ycenter 360

transform stage80_mixed_position_units:
    xpos 0.25
    xanchor 0.5
    ypos 360
    yanchor 0.5
    linear 0.9 xpos 960
    ease 0.9 ypos 0.25

label storyapi_stage80_atl_center_smoke:
    scene stage80_background
    show stage80_sprite as stage80_horizontal at stage80_horizontal_center
    show stage80_sprite as stage80_vertical at stage80_vertical_center
    show stage80_sprite as stage80_pixel at stage80_pixel_center
    show stage80_sprite as stage80_mixed at stage80_mixed_position_units
    _ "STORYAPI_STAGE80_XCENTER_REACHED"
    _ "STORYAPI_STAGE80_YCENTER_REACHED"
    _ "STORYAPI_STAGE80_PIXEL_CENTER_REACHED"
    pause 1.1
    _ "STORYAPI_STAGE80_MIXED_POSITION_UNITS_REACHED"
    _ "STORYAPI_RENPY_ATL_CENTER_PASS"
    return
