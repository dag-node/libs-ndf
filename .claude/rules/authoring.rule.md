---
paths:
  - "**/*.rule.md"
---

# Writing `.claude/rules/*.rule.md` files

Conventions for the path-scoped component rules in this directory. This guideline is
itself scoped to `**/*.rule.md`, so it loads whenever a rule file is open.

## Code is the source of truth, and doubt is resolved by asking

A rule and a source file header *describe* behaviour the code already has. Where either
disagrees with the code, the code is correct by definition and the prose is the stale side.
Verify a claim against the code before writing it, not against another document that may
repeat the same stale reading.

`CLAUDE.md` is different in kind: it states project-wide invariants the code is required to
uphold. Code contradicting one is a defect in the code, not licence to rewrite the
invariant. Resolve such a disagreement by consulting the code together with the rule that
scopes it, and surface it — an invariant is relaxed deliberately, never as a side effect of
documenting what the code happens to do.

Where the correct behaviour is not clear from the code, or two readings of it are equally
plausible, ask instead of guessing. Reference prose carries no hedging, so a guess committed
here reads as verified fact in every later session.

## Domains are fluid, shaped by the code

A rule is a lens onto a *domain* — a cluster of files that share one mechanism or story.
Domains emerge from how the code is organised; they are not a fixed taxonomy and not a
one-file-per-rule mapping.

- **A rule's shape mirrors its domain.** `paths:` is a precise named-file set where the
  domain is a few specific files (the bash process, its parser, and the scripts it sources),
  a recursive tree where the domain is a directory (`tests/**`), or a single file where the
  domain is one component (`FunctionParser.cs`). There is no uniform template to force.
- **`paths:` matches what the rule actually describes** — neither over-claiming (a broad
  glob that also sweeps in unrelated files: `bin/`, `obj/`, generated output, sample
  scripts) nor pointing at a file that does not exist in the repo.
- **A file may belong to several domains.** Its path then appears in several rules, and
  opening it loads them all — that overlap is a feature (richer context at a boundary),
  not duplication to remove.
- **Not every file needs a rule.** A file fully explained by its own header — a DTO, a
  settings POCO, a self-contained extension class — can stay uncovered. A rule exists only
  where there are domain-common principles or a cross-file story worth stating beyond the
  per-file headers. New code can grow a new domain; add a rule when that story appears.

## Naming

- One file per component: `<topic>.rule.md`. The `.rule.md` suffix lets tooling and this
  guideline target every rule with the `*.rule.md` glob.
- **Avoid a stem that matches a secret pattern.** The agent integration quarantines
  secret-named files: a file whose basename matches `~/.config/ai-tools/secret-patterns` is
  chowned to the operator `600` and becomes unreadable to the agent — which silently
  disables the rule. Steer clear of stems like `secret`, `secrets`, `credential(s)`, `env`,
  `private`, or any `*.key`/`*.pem`-style name.

## Frontmatter

- Give each rule a `paths:` list of globs over the `src/**` (and `tests/**`) files it
  describes, so it loads only when one of those files is open. Paths match the source
  files, not the rule filename.
- A rule with **no** `paths:` loads at launch every session and costs context
  unconditionally. Use that only for a guideline scoped to `**/*.rule.md` like this one.

## Content

- **What goes where (three tiers).** Root `CLAUDE.md` holds global invariants + the
  router. A rule holds the principles common to its whole domain (those not already in
  `CLAUDE.md`) plus the cross-component overview. The source file's header holds that
  file's local mechanism.
- **Register: reference-docs — the same skill as `CLAUDE.md` and source file/module
  headers.** Present-tense spec of current behaviour; no history ("removed", "now", "used
  to"). The register is applied independently of where content is split — always write to
  it, whatever tier you are editing. (Method/class XML doc comments use the `doc-comments`
  skill instead; a file/module *header block* is reference-docs.)
- **Rule and source header are bidirectionally coupled — reconcile at write time.** A rule
  and its header are paired by the rule's `paths:` frontmatter. When you touch either and
  the other disagrees, resolve it then and there against the actual code behaviour and make
  both sides match: do not write a known inconsistency, do not default to one side, and do
  not guess which is current (the stale side is not always the rule). When the correct
  behaviour is genuinely unclear, ask immediately rather than committing a guess.
- **A rule that has to narrate code line by line is reporting a refactor.** Each fact is
  single-sourced at the tier that owns it and linked from the others, so prose grows only
  where the domain does. Where covering a component takes prose out of proportion to the
  code — restating what descriptive identifiers would say — record that the component is a
  refactor candidate instead of documenting around it.
- **Keep load-bearing protocol invariants in the root `CLAUDE.md`, not here.** Path-scoped
  rules do not load unless a matching file is open and do not survive `/compact`; an
  invariant that must always hold — the one-line stdin protocol, the marker field offsets —
  belongs in the always-loaded root file.
- Cross-link sibling rules as `[topic](topic.rule.md)`, and register each rule in the
  component map in the root `CLAUDE.md`.

## Sections

A rule stays free-form prose (overview + mechanism), and may add any of these sections when
the domain has that content — none mandatory, rules stay fluid:

- **`## Design notes`** — why the domain is shaped this way (rationale as present-tense
  guarantees).
- **`## Quirks`** — domain-specific surprising behaviour / foot-guns (e.g. sourcing the
  signal traps in a test host kills the test host's own process group).
- **`## Why not`** — rejected alternatives + the reason (e.g. `echo -e` transport → rewrites
  `\t`, `\0nnn` and `\\` in the payload; flattening to one line → cannot express here-docs).
- **`## Deferred`** — domain-scoped, not-yet-built proposals, marked as such.
