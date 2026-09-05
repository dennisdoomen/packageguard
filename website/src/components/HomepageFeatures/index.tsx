import type {ReactNode} from 'react';
import clsx from 'clsx';
import Heading from '@theme/Heading';
import styles from './styles.module.css';

type FeatureItem = {
  title: string;
  emoji: string;
  description: ReactNode;
};

const FeatureList: FeatureItem[] = [
  {
    title: 'Every Ecosystem',
    emoji: '📦',
    description: (
      <>
        Scan NuGet, NPM, PNPM and Yarn dependencies across your whole solution
        or codebase, using the same feeds and credentials your IDE already uses.
      </>
    ),
  },
  {
    title: 'Allow and Deny Lists',
    emoji: '🛡️',
    description: (
      <>
        Enforce which open-source licenses, packages and version ranges are
        acceptable. Deny rules always win, so a mistake in an allow list can't
        let something through.
      </>
    ),
  },
  {
    title: 'Hierarchical Configuration',
    emoji: '🗂️',
    description: (
      <>
        Set a repository-wide policy next to your solution and refine it per
        project. PackageGuard discovers and merges the configuration files for
        you.
      </>
    ),
  },
  {
    title: 'Risk Scores',
    emoji: '📊',
    description: (
      <>
        Score every package on legal, security and operational risk, built from
        license data, OSV vulnerabilities, signing checks and repository health
        signals.
      </>
    ),
  },
  {
    title: 'Reports You Can Attach',
    emoji: '📄',
    description: (
      <>
        A colored console summary, a self-contained HTML report with the
        rationale behind every score, and a SARIF file your CI can upload as
        code-scanning results.
      </>
    ),
  },
  {
    title: 'Standards-Compliant SBOM',
    emoji: '🧾',
    description: (
      <>
        Emit the resolved dependency graph as CycloneDX or SPDX JSON, with
        package URLs, license evidence and — when combined with risk reporting —
        known vulnerabilities.
      </>
    ),
  },
];

function Feature({title, emoji, description}: FeatureItem) {
  return (
    <div className={clsx('col col--4', styles.featureCol)}>
      <div className={styles.featureCard}>
        <div className={styles.featureEmoji}>{emoji}</div>
        <Heading as="h3" className={styles.featureTitle}>{title}</Heading>
        <p className={styles.featureDescription}>{description}</p>
      </div>
    </div>
  );
}

export default function HomepageFeatures(): ReactNode {
  return (
    <section className={styles.features}>
      <div className="container">
        <div className="row">
          {FeatureList.map((props, idx) => (
            <Feature key={idx} {...props} />
          ))}
        </div>
      </div>
    </section>
  );
}
