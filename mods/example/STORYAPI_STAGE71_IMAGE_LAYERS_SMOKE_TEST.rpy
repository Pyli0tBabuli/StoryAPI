image stage71_background = classroom.png
image stage71_sprite = june.png

label storyapi_stage71_image_layers_smoke:
    scene stage71_background onlayer master with dissolve
    _ "STORYAPI_STAGE71_SCENE_MASTER_REACHED"
    show monika 1a as stage71_native_pose at left onlayer screens
    _ "STORYAPI_STAGE71_NATIVE_SHOW_SCREENS_REACHED"
    hide stage71_native_pose onlayer screens
    _ "STORYAPI_STAGE71_NATIVE_HIDE_SCREENS_REACHED"
    show stage71_sprite as stage71_custom_sprite at right onlayer screens
    _ "STORYAPI_STAGE71_CUSTOM_SHOW_SCREENS_REACHED"
    hide stage71_custom_sprite onlayer screens
    _ "STORYAPI_STAGE71_CUSTOM_HIDE_SCREENS_REACHED"
    scene onlayer overlay
    _ "STORYAPI_RENPY_IMAGE_LAYERS_PASS"
    return
