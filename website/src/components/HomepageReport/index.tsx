import type {ReactNode} from 'react';
import Link from '@docusaurus/Link';
import useBaseUrl from '@docusaurus/useBaseUrl';
import Heading from '@theme/Heading';
import styles from './styles.module.css';

export default function HomepageReport(): ReactNode {
  return (
    <section className={styles.reportSection}>
      <div className="container">
        <Heading as="h2" className={styles.reportTitle}>
          Every score comes with its reasoning
        </Heading>
        <p className={styles.reportIntro}>
          <code>--report-risk</code> writes a self-contained HTML report and a
          matching SARIF file. The HTML has no scripts and no external assets,
          so you can publish it straight from a build. Every point a package
          loses is listed, with a link to the evidence behind it.
        </p>
        <figure className={styles.reportFigure}>
          <a
            className={styles.reportLink}
            href={useBaseUrl('/img/risk-report.png')}
            target="_blank"
            rel="noreferrer noopener">
            <img
              className={styles.reportImage}
              src={useBaseUrl('/img/risk-report.png')}
              alt="A PackageGuard risk report: package counts by risk zone, a status check summary, a package summary table, and a per-package breakdown into legal, security and operational scores with the reasoning behind each one"
              width={1280}
              height={2155}
              loading="lazy"
            />
          </a>
          <figcaption className={styles.reportCaption}>
            The top of a real report, with the per-package evidence lists left
            out to keep the picture short. Click it to see it full size.
          </figcaption>
        </figure>
        <div className={styles.reportLinks}>
          <Link className="button button--primary" to="/docs/risk-metrics">
            What gets measured
          </Link>
          <Link className="button button--secondary" to="/docs/sbom">
            SBOM output
          </Link>
        </div>
      </div>
    </section>
  );
}
