# Working instructions

- Keep repository content in English. Conversation with the owner may be Russian.
- Follow the workspace working baseline at https://github.com/Xaaalera/sts2-workshop/blob/01a2e121e0a86d969a3540fa52d12994f32e88e0/AGENTS.md and the installed relevant marketplace skills.
- Search existing research and primary sources before changing an approach. Record evidence and failures in the workspace journal.
- This mod displays probability distributions and state graphs, never seeded future selections. Do not read, clone, advance or replace RNG or execute game transition/weight/condition callbacks during inspection.
- Keep probability logic independent of game APIs and SDK internals. Share behavior between native player and SDK adapters.
- Own game UI, timer and subscriptions in the resident adapter; collectible logic exchanges BCL/JSON data only. Preserve unloadability after real invocation.
- Use pinned compatible BaseLib/RitsuLib dependencies and native game loading. Finished player packages have no SDK dependency.
- Keep exactly one owned test game; the owner's separate Steam game may coexist and must never be controlled or lose focus/input to our work. Revalidate owned PID/path/UTC creation/session before control.
- Game screenshots require explicit owner request. Use diagnostics and programmatic checks; do not claim physical/rendering acceptance from background commands.
- Local game binaries, decompiled content, profiles, saves, raw logs and evidence stay in ignored .local directories.
- Install the pinned executable review gates before substantive implementation. Use actual configured independent reviewers and never bypass a gate or invent results.
