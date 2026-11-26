# Item art for Rune Reign

The game shows a picture for every item. Until a picture exists, it shows the item's symbol, so you can add art a few items at a time.

## Where the files go

Save each picture in `frontend/public/items/` (create the folder) and name it after the item's key:

    frontend/public/items/leather-jerkin.png

- Square PNG. 512 x 512 is plenty; 1024 x 1024 is fine too.
- Just refresh the page after adding files. Nothing needs rebuilding.
- To host a picture somewhere else (a CDN, say), put its full URL in the `Image` column of the `Items` table. An empty `Image` means `/items/<key>.png`.

## Shared style (paste this before every prompt)

> Hand-drawn cartoon fantasy game item, a single object floating slightly tilted, thick uneven black ink outlines, chunky exaggerated shapes, flat cel shading with two tones and a few sharp highlights, muted slate and gunmetal greys with small warm brass or leather accents, battered and chipped surfaces with scratches, a few small stone chips and specks floating around it, soft oval shadow on the ground below, plain light grey background, no text, no signature, no watermark, no hands, no character, square 1:1.

Then add the rarity line and the item line below.

**Rarity lines**
- Common: `plain worn iron, cloth and leather, no glow.`
- Rare: `finer work, cold blue glowing eye slits or runes.`
- Epic: `ornate, gold trim, a faint violet magical smoke or glow.`

Keep the same tool, the same style text and, if your tool allows it, the same seed for every item, so the set looks like one family.
Ask for a transparent background if your tool supports it; otherwise the plain grey background is fine, and Claude can cut it out.

## Items

| File | Rarity | Item prompt |
|---|---|---|
| runebound-mantle.png | common | a folded plain wool mantle with a single stitched rune on the clasp |
| frost-sigil.png | common | a round silver collar seal with a frosted snowflake rune, light frost on the edges |
| apprentice-charm.png | common | a small carved wooden token on a leather cord, a simple star rune |
| apprentice-hood.png | common | a simple brown cloth hood, slightly crumpled |
| padded-robe.png | common | a quilted cloth robe folded neatly, visible stitching |
| iron-circlet.png | common | a plain iron circlet band, slightly scratched |
| leather-jerkin.png | common | a thick hide leather jerkin with buckles |
| ember-ring.png | common | a bronze ring set with a glowing ember-orange stone, faint heat shimmer |
| bloodstone-sigil.png | common | a dark red bloodstone set in a bronze sigil mount |
| warding-charm.png | common | an endless-knot charm of braided cord and bone beads |
| seers-hood.png | rare | a deep indigo hood woven with faintly glowing silver threads |
| runeplate-vest.png | rare | a steel plate vest etched with the Algiz rune |
| duelist-ring.png | rare | a steel signet ring engraved with two crossed staves |
| stormglass-sigil.png | rare | a glass sigil with a tiny lightning bolt trapped inside |
| keen-circlet.png | rare | a thin silver circlet with a sharp blade-like point at the front |
| wanderers-cloak.png | rare | a travel cloak patched many times, sturdy and weathered |
| piercing-sigil.png | rare | a needle-thin silver rune sigil, sharp as a pin |
| seer-ring.png | rare | a silver ring with a pale moonstone shaped like an eye |
| thornspike-charm.png | rare | a charm of twisted black thorns bound with red thread |
| moonlit-cloak.png | rare | a deep blue cloak with a lining that shimmers like moonlight |
| aether-lantern.png | rare | a small brass lantern holding a floating pale blue flame |
| crown-of-duels.png | epic | a gold crown set with three rune stones, a champion's crown |
| shadowmail.png | epic | a shirt of dark chainmail links with smoky shadows curling off it |
| stormcaller-mantle.png | epic | a storm-grey mantle with crackling lightning along its hem |
| bladeweaver-ring.png | epic | two thin interlocked silver bands with blade-like edges |
| ruby-rune-frame.png | epic | a square ornate gold frame set with rubies, made to hold a rune die |

New items you add to the database follow the same rule: `<key>.png` in the same folder.
