import React from 'react';
import CodeBlock from '@theme/CodeBlock';
import Heading from '@theme/Heading';
import styles from './styles.module.css';

const codeExample = `{
    "settings": {
        "allow": {
            // 1. Only these SPDX licenses are acceptable
            "licenses": [ "MIT", "Apache-2.0" ],

            // 2. Wildcards and NuGet version ranges are supported
            "packages": [
                "Microsoft.Extensions.*",
                "MyPackage/[7.0.0,8.0.0)"
            ],

            // 3. Trust everything coming from your own feed
            "feeds": [ "*dev.azure.com*" ],

            // 4. Keep pre-release packages out of production
            "prerelease": false
        },
        "deny": {
            // 5. Deny always wins over allow
            "packages": [ "ProhibitedPackage", "Legacy.*" ]
        },

        // 6. Never even try to reach this feed
        "ignoredFeeds": [
            "https://pkgs.dev.azure.com/company/project/_packaging/myfeed/nuget/v3/index.json"
        ]
    }
}`;

const usageExample = `# Scan a solution, a project, or a package.json against the policy above
packageguard .

# Add legal, security and operational risk scores, plus HTML and SARIF reports
packageguard . --report-risk

# Emit a CycloneDX or SPDX bill of materials, vulnerabilities included
packageguard . --sbom cyclonedx --sbom-output bom.json --report-risk`;

export default function HomepageExample(): JSX.Element {
  return (
    <section className={styles.exampleSection}>
      <div className="container">
        <div className="row">
          <div className="col col--12">
            <Heading as="h2" className="text--center margin-bottom--lg">
              One Policy, Every Ecosystem
            </Heading>
            <div className={styles.codeWrapper}>
              <CodeBlock language="json" title="packageguard.config.json">
                {codeExample}
              </CodeBlock>
            </div>
            <div className={styles.codeWrapper}>
              <CodeBlock language="bash">
                {usageExample}
              </CodeBlock>
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}
