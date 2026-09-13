# Level 1: The Raised Way — player walkthrough

Companion to [first-node-story-levels.md](first-node-story-levels.md). That file is the
designer's view: story purpose, tone, failure conditions, rewards. This file is the
player's view, written in the register of the *From Dust* "The Breath" walkthrough —
what you are told, what you press, and what you see happen back.

Controls below are the live bindings in `Player/InputBindings.cs`. Mouse and keyboard
only; there is no gamepad scheme yet.

---

This first node is a basic tutorial mission. There are no real dangers here, and you can
play around with what little earth it gives you.

The God Hand has one hand for earth and water, and it chooses between them for you. Hold
it over dry ground and it takes earth; hold it over open water and it takes water. The
cursor ring tells you which before you commit — amber for earth, blue for water. Keys **1**
and **2** both return you to this hand from fire or lightning; there is no separate earth
key and water key to swap between. When prompted, use
**W, A, S and D** to move the view across the basin, **Q and E** to swing it around, and
the **mouse wheel** to pull back and see the whole crossing at once. **F** snaps the view
back to your people if you lose them in the mist.

Your tribe is already moving toward the First May Pike, as marked by the pale route line
laid along the ground. Follow them until they can go no further. The line turns red where
the old causeway has fallen away, and the survivors stop at the water's edge and start
calling back to you. You need to raise a way across so they can reach the pole.

The light brown material across the basin is earth, and the loose grey scatter along the
banks is stone small enough to shift. The large half-buried stones are not — you cannot
alter them at this point. The moving channels are not yours either. The hand is strong
enough to lift a little standing water, but not enough of it to stop a current or drain a
gap, so on this node the channels run where they want to run and you build around them.

Hold the **left mouse button** over the mud banks to scoop earth into the hand. Watch the
ring: over the banks it reads amber and takes earth, and if you drift out over the flood it
turns blue and starts taking water instead. Keep the brush on dry ground while you are
gathering fill. The ring fills as you gather, and when you have taken all the hand can
carry it closes to a solid band and the scoop stops biting, however long you hold it.

The hand carries one kind of matter at a time. Scoop water while you are holding earth and
you lose the earth, so empty out before you switch deliberately.

Now bring the loaded hand over the first gap and hold the **right mouse button** to pour
what you are carrying in a line running from your people to the far bank. One handful will
not do it. Expect to go back to the banks three or four times before the first gap carries
weight.

Once earth is down, the survivors come forward on their own, tread it flat and lay salvaged
timber over the top. You move the material; they make it a road. Wait for them to finish a
section before you start the next one — earth you drop under a working survivor just gets
walked off the edge.

## The three gaps

**First gap.** Small and slack, with no current through it. Drop stones in first to find the
bottom, then pack earth between them until the surface comes up dry. This is the whole loop
in miniature: mark, move, let them build, test it.

**Second gap.** Wider, with a live channel running through the middle. Fill the channel end
to end and the water backs up behind your new bank and floods the camp on the island — the
supplies go, and you spend the next few minutes digging them out. The channel has to stay
open. Lay a line of stones across the gap with a mouth left between two of them, pack earth
against the outside faces only, and let the survivors bridge the mouth with timber. Water
keeps running underneath and the road holds above it. The God Hand is not strong enough to
stop water at this stage, so you work with where it already wants to go.

**Third gap.** Close to the pole, with the fallen standing stone lying in the mud beside it.
The stone is far too heavy to lift. You can still move it: raise the ground under its
uphill side to start it turning, clear the mud from the line it will take, and drop a
couple of stones ahead of it to stop it running past the gap. The survivors get ropes and
a timber frame on it and walk it into place as a foundation. Large things need preparing,
not commanding.

## The willow

Near the last gap a survivor pulls a forked willow branch out of the reeds. An older one
remembers that willow grows where water sits near the surface, and holds it over the
ground until it dips. Follow where it dips — that is an underground stream, and earth
dropped straight onto it will slump within the hour. Set the final support to one side of
it instead, and raise the ground there. The branch gets planted beside the finished road.

## Crossing and the ritual

When the road is whole, the tribe tests it in order: a scout, then the cart, then the food
stores, then the children and the elderly, then everyone. It bends and settles, and holds.

Before the ceremony can start, the site has to be ready. Clear the debris out of the stone
ring, raise the three small stones in the ring to upright, and bring the fallen stone up to
the outer edge. All twelve survivors must be across the water and at the pole — the ritual
will not begin with the tribe split across the basin.

During the ritual you do not control the song. You keep the ground under it: push slumping
earth back into the ring, move stray stones back inside the circle, and keep the shallow
channel from cutting across the site. Then the bell rings untouched, the stones come up,
water starts moving through the channels, and a green line runs off across the flats toward
Avebury. The God Hand's water grip deepens: what was a splash it could barely hold becomes
enough to lift and shape. Take water from the channel — the ring reads blue over it — and
pour it between the two small standing stones until it stands up into a sheet, and the
Water Gate opens onto Glastonbury Tor.

## Before you go

Play around with the earth a bit first, to see how it behaves. Drop it into standing water
and watch how much of it washes; pile it high and watch the sides run. If you are carrying
a full hand and want rid of it all at once, hold **right mouse** over open ground and it
empties in one go. **Space** pauses the simulation and **N** steps it one tick at a time,
which is the clearest way to see how a bank fails. **R** resets the node if you want to
try the second gap a different way.

---

## Design notes

Two deliberate choices, flagged rather than buried:

- **One puzzle only.** From Dust's first level has none at all. The culvert at the second
  gap is the single thing here that can be got wrong, and getting it wrong costs supplies
  and time, not the level. The drone, the storm and the food clock from
  `first-node-story-levels.md` are held back — either move them to node two, or keep them
  as non-failing setbacks so this node stays a sandbox with a road in it.
- **Water is capped, not locked.** The build has a single auto-selecting matter hand
  (`WorldCursor.MatterTool.Matter`, wetness threshold 0.02) — it will take water off any wet
  cell from the first second of play, and keys 1 and 2 both just return to it. A hard "water
  is locked until the ritual" gate has no mechanism behind it and would mean special-casing
  the hand for one node. Both docs now instead treat the limit as
  *capacity*: the hand can lift a splash, not a current, which is already true of a small
  carry buffer and needs no new code. If you do want a hard lock, that is a `MatterFor`
  override gated on node state — say so and it is a few lines.
- **A hard number for the gate.** The design doc says "the whole tribe", which does not
  read on screen. Twelve survivors, all across, all at the pole. If the tribe size changes,
  change it here too.

Naming: **First May Pike** throughout, per the design doc's own explanation of the name.
`first-node-story-levels.md` still drifts between that and "first maypole node" in the
Primary objective and layout sections — worth a pass.
