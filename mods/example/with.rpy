# ---------------------------------------------------------------------------
# Фоны игры и переходы.
#
# `scene` ставит фон, `with` — переход. Переход пишется выражением, так же
# как в самой игре:
#
#     scene bg club_day with Dissolve(0.5)
#
# Короткое имя `dissolve` — это Dissolve(0.5), а `with None` — вовсе без
# перехода. Фон можно писать и коротко (`scene club_day`): слой добавится
# сам, если он нужен игре.
# ---------------------------------------------------------------------------

label storyapi_with:
    scene bg club_day with Dissolve(0.5)
    _ "Фон игры: клуб. Переход - растворение."
    pause 1

    scene bg residential_day with dissolve
    _ "Фон игры: жилой дом. Переход - dissolve."
    pause 1

    scene black with Dissolve(0.5)
    _ "Экран стал чёрным."
    pause 1
    return


label storyapi_with_none:
    scene bedroom with None
    _ "Фон игры: спальня. Короткое имя, перехода не было."
    pause 1

    scene black with None
    _ "Чёрный экран без перехода."
    pause 1
    return
