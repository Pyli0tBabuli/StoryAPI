image stage70_background = classroom.png
image stage70_sprite = june.png

label storyapi_stage70_inline_image_transitions_smoke:
    scene stage70_background with dissolve
    _ "STORYAPI_STAGE70_SCENE_TRANSITION_REACHED"
    show stage70_sprite at left with dissolve
    _ "STORYAPI_STAGE70_SHOW_TRANSITION_REACHED"
    hide stage70_sprite with Dissolve(0.1)
    _ "STORYAPI_STAGE70_HIDE_TRANSITION_REACHED"
    scene black with Dissolve(0.1)
    _ "STORYAPI_INLINE_IMAGE_TRANSITIONS_PASS"
    return
