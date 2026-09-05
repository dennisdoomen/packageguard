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
        Scan NuGet, NPM, PNPM and Yarn dependencies, using the same feeds and
        credential providers as <code>dotnet</code>, your package manager and
        your IDE. Private feeds included.
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
        Read known vulnerabilities from OSV, including transitive ones. Check
        package signing, publisher trust, build provenance, branch protection
        and the OpenSSF Scorecard.
      </>
    ),
  },
  {
    title: 'Reports and SBOM',
    emoji: '🧾',
    to: '/docs/sbom',
    description: (
      <>
        A console summary, an HTML report and a SARIF file for code scanning.
        Emit the dependency graph as CycloneDX or SPDX, with package URLs,
        license evidence and vulnerabilities.
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
