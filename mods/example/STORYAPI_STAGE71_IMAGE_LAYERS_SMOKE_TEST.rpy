image stage71_background = classroom.png
image stage71_sprite = june.png

label storyapi_stage71_image_layers_smoke:
    scene stage71_background onlayer master with dissolve
    _ "STORYAPI_STAGE71_SCENE_MASTER_REACHED"
    show monika 1a at left onlayer screens
    _ "STORYAPI_STAGE71_NATIVE_SHOW_SCREENS_REACHED"
    hide monika onlayer screens
    _ "STORYAPI_STAGE71_NATIVE_HIDE_SCREENS_REACHED"
    show stage71_sprite as stage71_custom_sprite at right onlayer screens
    _ "STORYAPI_STAGE71_CUSTOM_SHOW_SCREENS_REACHED"
    hide stage71_custom_sprite onlayer screens
    _ "STORYAPI_STAGE71_CUSTOM_HIDE_SCREENS_REACHED"
    scene stage71_background onlayer overlay
    _ "STORYAPI_STAGE71_CUSTOM_SCENE_OVERLAY_REACHED"
    scene onlayer overlay
    _ "STORYAPI_STAGE71_OVERLAY_CLEAR_REACHED"
    show stage71_sprite as stage71_shared at left onlayer master
    _ "STORYAPI_STAGE71_SHARED_MASTER_REACHED"
    show stage71_sprite as stage71_shared at right onlayer screens
    _ "STORYAPI_STAGE71_SHARED_SCREENS_REACHED"
    hide stage71_shared onlayer screens
    _ "STORYAPI_STAGE71_LAYER_SCOPED_HIDE_REACHED"
    hide stage71_shared onlayer master
    _ "STORYAPI_RENPY_IMAGE_LAYERS_PASS"
    return
