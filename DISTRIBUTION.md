# Distribution review, 22 November 2026

PPSSPP GPL-2.0-or-later; frontend GPL-2.0; FFmpeg LGPL-2.1-or-later and vendored dependencies under their retained notices.

This source release does not constitute blanket legal clearance. Individual source notices are retained. No private TPK, compiled emulator core, retail BIOS, commercial ROM, downloaded cover scan, signing credential or pairing file is included. Do not describe the mixed tree as entirely covered by its top-level LICENSE.

Before a binary release:

- Establish the exact historical source corresponding to the retained interpreter binary, not just the newer JIT source/patch stack.
- Complete build/dependency and native runtime correspondence audit before publishing compiled core binaries.

GPL source distribution reference: https://www.gnu.org/licenses/old-licenses/gpl-2.0.html (section 3). Core-license overview: https://docs.libretro.com/development/licenses/. Local pinned license texts take precedence over this summary.

## Publication corrections

The audit now checks executable magic bytes as well as filenames. Versioned and extensionless compiled files missed by the original audit are excluded. Additional prebuilt runtime/tool/media assets are omitted; PUBLIC-SOURCE.json lists their exact paths and hashes. Upstream source test vectors may remain and are not commercial games.
