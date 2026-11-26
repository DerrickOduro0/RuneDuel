Character art for Rune Reign
============================

Put one picture per class in this folder, named exactly:

  flamecaller.png
  frostwarden.png
  fateweaver.png
  gravebound.png

These are used on the Character (Build) screen and in combat. Any class without a
picture keeps the drawn mage, so you can add them one at a time. Refresh the page
after adding a file.

Rules for each picture
- PNG with a transparent background (no scenery behind the character).
  If your generator can't do transparency, ask for a plain flat light-grey background
  and send the images to Claude to cut out.
- Portrait shape 5:7, for example 1000 x 1400 pixels.
- Full body, feet touching the bottom edge, head near the top with a little space above.
- Character faces to the RIGHT (three-quarter view). The game mirrors it for the opponent.

Style (paste before every prompt, so all four match the home screen art)
-----------------------------------------------------------------------
Anime fantasy illustration in the same style as a painted JRPG key visual, clean ink
linework, soft painterly cel shading, muted warm colours with one strong accent colour,
full-body young mage standing in a confident ready-to-duel pose, three-quarter view facing
right, detailed layered clothing with leather straps and metal fittings, flowing cloak,
plain flat light-grey background, no text, no frame, no scenery.

Class prompts
-------------
flamecaller.png
  A fierce young fire mage, dark red hooded cloak with torn hem and a glowing rune
  embroidered on it, dark leather armour, holding a short blackened staff with an ember
  crystal at the tip, small flames curling around one hand, amber eyes.

frostwarden.png
  A calm young ice mage, long pale blue and white coat with silver trim, fur-lined
  mantle, holding a tall staff topped with a frost crystal, a thin rune shield of ice
  floating beside the free hand, frosty breath, silver hair.

fateweaver.png
  A mysterious young seer, deep purple hooded robe with gold thread and star patterns,
  holding a staff with a ring of floating rune stones around its head, faint golden
  threads of light spun between the fingers, calm knowing expression.

gravebound.png
  A grim young necromancer, black and dark green layered robes, bone and iron
  ornaments, tattered cloak, holding a staff of dark wood topped with a cracked skull
  lantern glowing green, pale skin, sharp eyes.

Home background (optional, replaces the temporary one)
-------------------------------------------------------
The home screen and every menu use frontend/public/scenes/home.jpg. It is currently cut
from your design mock-up, so it is a little soft on large screens. For a sharper one,
generate the same scene without any interface on it, wide 16:9 at 1920 x 1080 or larger,
and save it as scenes/home.jpg:

  Anime fantasy landscape in a painted JRPG key-visual style, a young swordsman-mage seen
  from behind in a dark red hooded cloak with a white rune on the back, standing on a
  grassy cliff edge on the left third, looking at a vast white gothic castle city built on
  floating rock islands in the centre, smaller floating islands in a soft blue cloudy sky,
  waterfalls and mist below, warm soft light, no text, no interface, no frame.
