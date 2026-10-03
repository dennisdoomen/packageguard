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

## Warnings for tolerated copyleft

Copyleft licenses that the chosen profile tolerates (for example LGPL for SaaS software) are allowed, but
`init` also lists them in a [`warn` section](./configuration.md#warnings-instead-of-build-failures). They
never fail the build, but they show up as warnings so the obligations stay visible. When some packages
violate the suggested policy, `init` also reminds you that `--treat-deny-as-warning` lets you adopt the
policy gradually, reporting violations without failing the build yet.

## Risk gates

Optionally, `init` can also add [risk-based `deny` rules](./configuration.md#gating-on-risk-and-package-age)
to the generated file. You are asked about it interactively, or you can pass `--risk-gates`:

```json
"deny": {
    "maxOverallRisk": 60,
    "maxSecurityRisk": 7,
    "maxOsvSeverityScore": 7.0,
    "denyDeprecated": true,
    "minPackageAgeDays": { "npm": 14, "nuget": 3 }
}
```

This is off by default, because gating on risk makes every run slower: PackageGuard has to collect risk
data for each package, which works best with a GitHub API key. `init` itself doesn't collect risk data, so
these rules are only evaluated by your next `packageguard .` run. The file also contains a commented-out
`riskExceptions` example, for packages whose risk you have decided to accept.

## Non-interactive use

For scripted setup, CI, or project templates, skip the question with `--preset`:

```bash
packageguard init --preset permissive-only
```

The available presets are `permissive-only`, `no-network-copyleft`, and `oss-friendly`, matching the three
questions above.

## Options

- `--config-path <path>` - where to write the generated file. Defaults to `.packageguard/config.json` next to
  the solution (or the resolved project directory when no solution is found).
- `--risk-gates` - also add the risk-based `deny` rules described above. Without it, `--preset` skips the
  question and leaves them out.
- `--overwrite` - overwrite a configuration file that already exists at that path. Without it, `init` refuses to
  run rather than silently replacing your policy.
- `--npm`, `--npm-exe-path`, `--nuget`, `-i`/`-f`/`-s` - the same project-discovery and restore options
  `analyze` supports, since `init` scans the repository the same way.

The generated file is a normal [configuration](./configuration.md) file - a `settings.allow.licenses` list
with a comment explaining the chosen preset and what copyleft means for it. Edit it like any other
PackageGuard configuration from there.
