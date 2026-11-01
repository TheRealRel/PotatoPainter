## 0.4.10 (test build, not released)
- Etched glow a little softer, so every colour keeps its colour in more places, day and night.
- New setting: Rune light shadows (off by default). When on, the light of nearby etched runes stops at walls instead of shining through; costs some frame time.
- The glow test is gone.

## 0.4.9 (test build, not released)
- Etched runes dim a little in sunlight, both their colour and their glow, so they keep their colour instead of washing out. They are as before at night and in shade.
- Test build: by day the glow test now checks the sunlight dimming (about half a minute, hold still).

## 0.4.8 (test build, not released)
- Less stutter when painting over walls made of many pieces.
- Etched glow limits set a little lower, so every colour keeps its colour in every case measured.
- Test build: the game log times each step of painting, and the glow and shadow tests stay in.

## 0.4.7 (test build, not released)
- Test build: Right Ctrl + L switches shadows on rune lights on and off, to measure what they cost.

## 0.4.6 (test build, not released)
- Etched glow limits remeasured for every colour, and lighter (less saturated) colours now have their own.
- Test build: placing an etched rune runs a glow test (about a minute, hold still): at night it checks the new limits, by day it measures how much a sunlit rune needs to dim to keep its colour.

## 0.4.5 (test build, not released)
- Test build: placing an etched rune now runs a finer glow test, every colour in small steps, by day and at night (about a minute, hold still).

## 0.4.4 (test build, not released)
- Etched glow no longer goes flat: the Glow slider now runs from 25% to 100% of the most each colour can take before it washes out to white or fills up to a solid block, measured on screen for every colour. With bloom on the limit is lower than with it off, so each player sees the glow at its best for their own bloom setting, and runes change with it when bloom is switched.
- Etched runes from earlier builds keep their glow, held to the new limit.
- Test build: placing an etched rune now checks the new limits (about 20 seconds, hold still).

## 0.4.3 (test build, not released)
- Etched runes give off light again, like the early glow tests: a soft light in the rune's colour that lights the surface around it, so it reads as glowing with bloom on or off. The Etched panel has a Light button (on by default); each rune keeps the setting it was etched with. Etched runes from earlier builds have it on.
- Test build: every etched rune you place starts a glow test that reads the screen for about half a minute (hold still), to find where each colour stops clipping.
- Symbol hiding no longer looks up a build menu list by a name another mod also uses.

## 0.4.2 (test build, not released)
- Large paints no longer drag the frame rate down: tracing a paint onto surfaces is much faster, and paint on the ground is only reshaped when the ground's drawn shape can have changed (not on every step you take near it).
- A slow preview waits a little longer before tracing again, so it can't take over the frame.
- Still writes the timing lines to the log, to compare with 0.4.1.

## 0.4.1 (test build, not released)
- Test build: writes how long painting takes to the log every 5 seconds, to find the lag with large paints.

## 0.4.0 (test build, not released)
- Symbols, painted and etched, under "Symbols: Painted" and "Symbols: Etched": a left and a right hand print from the start; Vegvísir, Huginn and Muninn, Sleipnir and Gungnir after Eikthyr; Yggdrasil, the Web of Wyrd, the troll cross and a knotwork Mjölnir after The Elder; the Valknut, Mjölnir, the triple horn and a bloody hand print after Bonemass.
- A boss's symbols show up once the boss is down in the world, or once your character has defeated it.

## 0.3.3 (test build, not released)
- One set of runes instead of a set per rune row: every rune shape once, 51 in all, under "Runes: Painted" and "Runes: Etched". Shapes the rows share list the other names in their description. The short-twig runes are ordinary runes in the set, so the Style button is gone.

## 0.3.2 (test build, not released)
- The Younger Futhark (16 runes) and the Anglo-Saxon futhorc (28 runes and its 6 later additions), painted and etched, each in its own tabs.
- A Style button on the panel for the Younger Futhark switches between long-branch and short-twig runes.

## 0.3.1 (test build, not released)
- No support colours on built pieces while painting runes: paint doesn't hold anything up.

## 0.3.0 (test build, not released)
- All 24 Elder Futhark runes, painted and etched. Jera's two hooks sit side by side.
- Rune names and descriptions are spelled without the long-vowel marks the game's font can't show (Isaz, Raido, Tiwaz).
- Etched runes are lit evenly; painted runes keep their slight texture.

## 0.2.4 (test build, not released)
- Runes can be painted on mountain firs, Black Forest pines, swamp trees and oaks (their trunks and thick branches, never needles or leaves), and move with the tree in the wind.

## 0.2.3 (test build, not released)
- Test build: writes why a spot can't be painted to the log.

## 0.2.2 (test build, not released)
- The slider panel shows beside the build menu (it was missing).
- Eight runes have new lines on what they stood for, so none just repeats its meaning.

## 0.2.1 (test build, not released)
- A panel beside the Rúnakefli's build menu sets the next rune's colour (hue, saturation, brightness) and size (0.25 to 2 m); for etched runes, the glow's colour and strength. It follows the open tab, and your settings stay with your character until you change them. Default puts them back.
- The preview shows the rune in the colour, size and glow it will have.
- A different colour or size of the same rune can go over an old one.

## 0.2.0 (test build, not released)
- Every rune comes two ways, each in its own tab of the Rúnakefli's menu: Elder Futhark: Painted, and Elder Futhark: Etched, which glows ember with a faint halo.
- A painted rune costs 1 Coal; an etched one 1 Coal and 1 Resin. Taking a rune away with the Rúnakefli gives them back.
- A click where nothing can be painted, or on the same rune already there, does nothing and costs nothing.

## 0.1.9 (test build, not released)
- The preview does less work while you aim, and PotatoPainter no longer fills the game's log with test lines.

## 0.1.8 (test build, not released)
- Runes on the ground follow the ground's bumps as the game draws them (with the Tessellation setting on, the ground has small hills its collision doesn't), so they no longer sink into it.
- Aiming a rune at the ground no longer makes the grass around it flicker in and out.

## 0.1.7 (test build, not released)
- The mouse wheel turns the rune before you paint it, like it turns a building piece.
- Less grass is cleared around a rune on the ground (about 1.5 m across).

## 0.1.6 (test build, not released)
- Īsaz and Perþō get a line on what they stood for too.
- The grass cleared around a rune on the ground is about 2 m across; the tallest grass may still lean in at the edges.

## 0.1.5 (test build, not released)
- Each rune's tooltip adds a short line on what it stood for.

## 0.1.4 (test build, not released)
- The preview and the paint follow the crosshair, the same aim the game uses for building, so snapping no longer sends it off the surface.
- Pieces from mods whose models can't be read are painted on their collision shape.

## 0.1.3 (test build, not released)
- Grass clears far enough around a rune on the ground that no blades lean over it.
- Painting the same rune on the same spot again does nothing ("Already painted there"). Piles of the same rune already painted on one spot are cut down to one.
- Each rune's tooltip shows its meaning.

## 0.1.2 (test build, not released)
- Runes on the ground can be seen now: grass and other ground clutter are left out under them, and come back when the paint is removed. The ground itself isn't changed.
- The runes on the Rúnakefli glow ember, sit in the middle of the plain wood and are a little larger.

## 0.1.1 (test build, not released)
- The Rúnakefli has its own look: an antler-tipped rune stick with ᚱᚢᚾᚨᚲᛖᚠᛚᛁ painted along the shaft, and its own icon.
- Nine more runes to paint: Raidō, Ūruz, Naudiz, Ansuz, Kauną, Ehwaz, Fehu, Laguz and Īsaz.
- No placement marker while painting.
- Painting twice on the same spot stacks the new paint on top instead of flickering into the one below.

## 0.1.0 (test build, not released)
- The Rúnakefli, crafted at the workbench, with its own build menu.
- Paint the Algiz rune onto walls, floors, rocks, trees and the ground; it stays for everyone and comes back after a restart.
- Middle mouse with the Rúnakefli takes a paint away again. A paint goes when what it's painted on is destroyed.
