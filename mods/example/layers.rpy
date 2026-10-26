label storyapi_layers:

    # No `scene` on purpose: it does not return inside a mod label, and that is a
    # separate pre-existing problem. This label only exercises the pose work.
    pause 0.5

    pose monika 1 at left
    pause 1.5

    layer monika 1 1l 1r
    pause 1.5

    move monika 1 xpos 0.75 over 1.0
    pause 1.5

    zoom monika 1 to 1.2 over 0.8
    pause 1.2

    turn monika 1 to 10 over 0.6
    pause 1.0

    fade monika 1 to 0.0 over 1.0
    pause 1.5

    pose natsuki 0 at center
    pause 1.5

    move natsuki 0 xpos 0.25 over 1.0
    pause 1.5

    fade natsuki 0 to 0.0 over 1.0
    pause 1.5
    return