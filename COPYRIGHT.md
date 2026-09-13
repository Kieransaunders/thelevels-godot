# Licensing

## Code — GPL-3.0-or-later

Copyright (C) 2026 Kieran Saunders

All source in this repository is licensed under the GNU General Public License,
version 3 or later. The full text is in [LICENSE](LICENSE).

GPL-3.0 is a **strong copyleft** licence, chosen so this project stays open:
anyone who distributes this game, or a modified version of it, must make the
complete corresponding source available to their users under these same terms.
A fork cannot be closed.

Godot itself is MIT-licensed, which is GPL-compatible, so shipping a GPL game on
Godot is fine.

### One consequence worth knowing

GPL-3.0 is incompatible with the Apple App Store's terms of service (its DRM and
device restrictions conflict with GPL-3.0 section 6). If an iOS release ever
matters, that is the decision to revisit — GPL-2.0 or a dual-licence arrangement
are the usual routes, and both are much easier to choose now than after outside
contributors hold copyright in the code.

## Assets

Art, audio, models and other non-code assets are **not** covered by the GPL —
it is written for software and fits assets poorly. Until they are given their own
terms, treat everything under `Assets/` as all rights reserved.

Creative Commons Attribution-ShareAlike 4.0 (CC BY-SA 4.0) is the usual
copyleft pairing for game assets and keeps the same "must stay open" property.

## Third-party material

- `reference/` is technique-only reference material, excluded from the build and
  from version control (see `.gitignore` and `reference/README.md`). It is not
  distributed with this project.
- `docs/Game overview story/From Dust levels for reference.md` is a third-party
  walkthrough of *From Dust*, written by redapocalypse04. It is quoted here as
  design reference only, is not our work, and is not covered by this project's
  licence. See the note in that folder before redistributing it.
