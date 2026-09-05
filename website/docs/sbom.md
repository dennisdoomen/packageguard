---
sidebar_position: 6
---

# Software Bill of Materials (SBOM)

PackageGuard can emit the resolved dependency graph as a standards-compliant SBOM, in either [CycloneDX](https://cyclonedx.org/) or [SPDX](https://spdx.dev/) JSON format, using the `--sbom` and `--sbom-output` flags:

```
packageguard --sbom cyclonedx --sbom-output bom.json <path-to-project>
packageguard --sbom spdx --sbom-output bom.spdx.json <path-to-project>
```

Both formats are built from the same resolved package data, so they always agree on what's included:

- **Package URLs (purl)** for every component, e.g. `pkg:nuget/Newtonsoft.Json@13.0.3` or `pkg:npm/lodash@4.17.21`.
- **One aggregate SBOM per run**, covering every project in the analyzed solution, with a synthetic root component representing the solution itself.
- **Direct vs. transitive dependencies**, reflected as CycloneDX `scope`/`dependsOn` entries and SPDX `DEPENDS_ON` relationships.
- **License evidence** - a license declared by the package's own metadata (NuGet/npm registry data) is recorded differently from one PackageGuard concluded from external evidence, such as a GitHub repository scan or a heuristic match against downloaded license text. CycloneDX records this as a license `acknowledgement` of `declared` or `concluded`; SPDX records it by populating either `licenseDeclared` or `licenseConcluded` (the other is `NOASSERTION`).
- **Vulnerabilities** - combine `--sbom` with `--report-risk` to also populate a CycloneDX `vulnerabilities` section (or, for SPDX, a per-package annotation) from the same OSV data used for risk scoring. Without `--report-risk`, no vulnerability data is fetched or included.

```
packageguard --sbom cyclonedx --sbom-output bom.json --report-risk <path-to-project>
```

**Known limitation:** PackageGuard currently only builds a real parent-child dependency graph for NuGet packages. npm, yarn, and pnpm packages are recorded as direct dependencies of the solution root rather than a fully nested tree, pending real dependency-graph parsing for those ecosystems. Both formats call this out explicitly - CycloneDX as a `metadata.properties` entry, SPDX as a document `comment` - so downstream consumers don't mistake a flat list for a complete graph.
