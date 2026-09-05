import type {ReactNode} from 'react';
import clsx from 'clsx';
import Link from '@docusaurus/Link';
import Heading from '@theme/Heading';
import styles from './styles.module.css';

type FeatureItem = {
  title: string;
  emoji: string;
  to: string;
  description: ReactNode;
};

const FeatureList: FeatureItem[] = [
  {
    title: 'Every Ecosystem',
    emoji: '📦',
    to: '/docs/usage',
    description: (
      <>
        Scan NuGet, NPM, PNPM and Yarn dependencies. Point it at a whole
        solution, a single project, or one <code>package.json</code>.
      </>
    ),
  },
  {
    title: 'Allow and Deny Lists',
    emoji: '🛡️',
    to: '/docs/configuration',
    description: (
      <>
        Decide which licenses, packages and versions are acceptable. Match on
        wildcards and NuGet version ranges, and keep pre-releases out. Deny
        rules always win over allow rules.
      </>
    ),
  },
  {
    title: 'Hierarchical Configuration',
    emoji: '🗂️',
    to: '/docs/configuration#hierarchical-configuration-discovery',
    description: (
      <>
        Put a repository-wide policy next to your solution and refine it per
        project. PackageGuard finds the configuration files and merges them for
        you.
      </>
    ),
  },
  {
    title: 'License Resolution',
    emoji: '🔍',
    to: '/docs/configuration#identifying-packages-and-licenses',
    description: (
      <>
        Resolve SPDX identifiers from NuGet and npm metadata, from GitHub
        repositories, and from the license text itself. Microsoft's proprietary
        library licenses are recognised by name.
      </>
    ),
  },
  {
    title: 'Private and Internal Feeds',
    emoji: '🔑',
    to: '/docs/configuration#about-feeds',
    description: (
      <>
        Uses the same feeds and credential providers as <code>dotnet</code>,
        your package manager and your IDE. Trust everything on your own feed, or
        skip a feed entirely.
      </>
    ),
  },
  {
    title: 'Risk Scores',
    emoji: '📊',
    to: '/docs/risk-metrics',
    description: (
      <>
        Score every package from 0 to 100, weighted across legal risk (20%),
        security risk (45%) and operational risk (35%). Missing evidence is
        scored conservatively rather than ignored.
      </>
    ),
  },
  {
    title: 'Vulnerabilities and Supply Chain',
    emoji: '🚨',
    to: '/docs/risk-metrics#security-risk',
    description: (
      <>
        Read known vulnerabilities from OSV, including transitive ones, their
        severity and how long they take to get fixed. Check package signing,
        publisher trust, build provenance, branch protection and the OpenSSF
        Scorecard.
      </>
    ),
  },
  {
    title: 'Project Health',
    emoji: '💓',
    to: '/docs/risk-metrics#operational-risk',
    description: (
      <>
        Measure release cadence, documentation quality, contributor
        concentration, issue and pull-request throughput, and CI reliability, so
        you can see which dependencies are quietly going unmaintained.
      </>
    ),
  },
  {
    title: 'Reports You Can Attach',
    emoji: '📄',
    to: '/docs/risk-metrics',
    description: (
      <>
        A colored console summary, a self-contained HTML report with the
        rationale behind every score, and a SARIF file your CI can upload as
        code-scanning results. The HTML has no scripts and no external assets.
      </>
    ),
  },
  {
    title: 'CycloneDX and SPDX SBOM',
    emoji: '🧾',
    to: '/docs/sbom',
    description: (
      <>
        Emit the resolved dependency graph as a bill of materials, with package
        URLs, direct versus transitive scope, declared versus concluded
        licenses, and vulnerabilities when risk reporting is on.
      </>
    ),
  },
  {
    title: 'Caching',
    emoji: '⚡',
    to: '/docs/caching-and-rate-limits',
    description: (
      <>
        Cache package, license and risk data on disk with a configurable
        lifetime. Commit the cache so your CI runs benefit from work that has
        already been done.
      </>
    ),
  },
  {
    title: 'Global Tool or Portable',
    emoji: '💻',
    to: '/docs/installation',
    description: (
      <>
        Install it as a .NET global tool, or download the portable zip and run
        it on Windows, Linux and macOS. The exit code tells your build whether
        the policy held.
      </>
    ),
  },
];

function Feature({title, emoji, description, to}: FeatureItem) {
  return (
    <div className={clsx('col col--4', styles.featureCol)}>
      <Link to={to} className={styles.featureCard}>
        <div className={styles.featureEmoji}>{emoji}</div>
        <Heading as="h3" className={styles.featureTitle}>{title}</Heading>
        <p className={styles.featureDescription}>{description}</p>
      </Link>
    </div>
  );
}

export default function HomepageFeatures(): ReactNode {
  return (
    <section className={styles.features}>
      <div className="container">
        <Heading as="h2" className={styles.featuresTitle}>
          Everything it does
        </Heading>
        <div className="row">
          {FeatureList.map((props, idx) => (
            <Feature key={idx} {...props} />
          ))}
        </div>
      </div>
    </section>
  );
}
