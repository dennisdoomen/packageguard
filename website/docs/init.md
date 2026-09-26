---
sidebar_position: 2.5
---

# Getting started with `init`

Adopting PackageGuard usually starts with an empty configuration file and a question you can't answer yet:
which licenses should you allow? The `init` command scans your repository first, so the question comes with
an answer already in front of you: here's what's actually here, and here's a policy that fits it.

```bash
packageguard init <path-to-solution-file-or-project>
```

This scans the repository just like a normal run, then prints a breakdown of every license in use, with
copyleft licenses flagged:

```
Scanning MyProduct...
Found 247 packages across 12 projects.

Licenses in use:
  MIT              184 packages
  Apache-2.0        41 packages
  BSD-3-Clause       9 packages
  MS-PL              7 packages
  LGPL-2.1-only      4 packages   <- weak copyleft
  GPL-3.0-only       2 packages   <- strong copyleft
  (unknown)          1 package    <- SomeObscurePackage 1.2.0
```

It then asks what kind of software this is, since that decides how much copyleft exposure is reasonable to
allow:

- **Proprietary / commercial** - suggests a permissive-only policy (MIT, Apache-2.0, BSD, and similar).
- **SaaS / hosted** - suggests permissive plus weak copyleft (LGPL, MPL), but excludes GPL and AGPL, since
  AGPL's obligations are specifically triggered by offering software as a network service.
- **Open source** - suggests allowing every copyleft category, since an open-source project is typically
  already compatible with them.

The answer only decides which of the licenses *found in the scan* end up in the generated allow list -
`init` never invents a policy that allows a license your dependencies don't actually use, and it never
allows everything present just to guarantee a clean first run. If a copyleft license doesn't fit the chosen
profile, it's left out, and the packages using it show up as violations you can act on:

```
Written .packageguard/config.json
2 packages violate the suggested policy. Run `packageguard .` to see them.
```

## Non-interactive use

For scripted setup, CI, or project templates, skip the question with `--preset` and `--yes`:

```bash
packageguard init --preset permissive-only --yes
```

The available presets are `permissive-only`, `no-network-copyleft`, and `oss-friendly`, matching the three
questions above.

## Options

- `--config-path <path>` - where to write the generated file. Defaults to `.packageguard/config.json` next to
  the solution (or the resolved project directory when no solution is found).
- `--force` - overwrite a configuration file that already exists at that path. Without it, `init` refuses to
  run rather than silently replacing your policy.
- `--npm`, `--npm-exe-path`, `--nuget`, `-i`/`-f`/`-s` - the same project-discovery and restore options
  `analyze` supports, since `init` scans the repository the same way.

The generated file is a normal [configuration](./configuration.md) file - a `settings.allow.licenses` list
with a comment explaining the chosen preset and what copyleft means for it. Edit it like any other
PackageGuard configuration from there.
