image stage72_sprite = june.png

label storyapi_stage72_show_behind_smoke:
    scene classroom
    show monika 1a as stage72_native_anchor at center
    _ "STORYAPI_STAGE72_NATIVE_ANCHOR_REACHED"
    show stage72_sprite as stage72_custom_behind at center behind stage72_native_anchor
    _ "STORYAPI_STAGE72_CUSTOM_BEHIND_NATIVE_PASS"
    hide stage72_custom_behind
    show stage72_sprite as stage72_custom_anchor at center
    show monika 1a as stage72_pose_behind at center behind stage72_custom_anchor
    _ "STORYAPI_STAGE72_POSE_BEHIND_CUSTOM_PASS"
    hide stage72_pose_behind
    show monika 1a as stage72_anchor_one at left
    show stage72_sprite as stage72_anchor_two at right
    show monika 1a as stage72_multiple_behind at center behind stage72_anchor_one, stage72_anchor_two
    _ "STORYAPI_STAGE72_MULTIPLE_BEHIND_TAGS_PASS"
    hide stage72_multiple_behind
    _ "STORYAPI_RENPY_SHOW_BEHIND_PASS"
    return
