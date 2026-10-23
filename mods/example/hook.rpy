label storyapi_hook:

    _ "A normal mod label, so the mod is known to work at all."
    return


# The game says Monika's opening lines, then the mod takes over.
#
# The id comes from docs/data/script-reference.txt:
#   [0] tag=m  id=838645429   Hi, [player]!
#   [1] tag=m  id=1271520166  Having a good time so far?
hook ch1_m_start after 1271520166:
    _ "Monika has said her two opening lines. This is the mod speaking."
    return