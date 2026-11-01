image stage79_background = classroom.png
image stage79_sprite = june.png

transform stage79_horizontal_anchor:
    xalign 0.30
    yalign 0.5
    xanchor 0.0
    yanchor 0.5
    linear 0.9 xanchor 1.0

transform stage79_vertical_anchor:
    xalign 0.70
    yalign 0.5
    xanchor 0.5
    yanchor 0.0
    ease 0.9 yanchor 1.0

transform stage79_pixel_anchor:
    xalign 0.5
    yalign 0.5
    xanchor 32
    yanchor 16

label storyapi_stage79_atl_anchor_smoke:
    scene stage79_background
    show stage79_sprite as stage79_horizontal at stage79_horizontal_anchor
    show stage79_sprite as stage79_vertical at stage79_vertical_anchor
    show stage79_sprite as stage79_pixel at stage79_pixel_anchor
    _ "STORYAPI_STAGE79_XANCHOR_REACHED"
    _ "STORYAPI_STAGE79_YANCHOR_REACHED"
    _ "STORYAPI_STAGE79_PIXEL_ANCHOR_REACHED"
    pause 1.1
    _ "STORYAPI_RENPY_ATL_ANCHOR_PASS"
    return
