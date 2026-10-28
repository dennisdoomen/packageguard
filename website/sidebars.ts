import type {SidebarsConfig} from '@docusaurus/plugin-content-docs';

// This runs in Node.js - Don't use client-side code here (browser APIs, JSX...)

const sidebars: SidebarsConfig = {
  tutorialSidebar: [
    'intro',
    {
      type: 'category',
      label: 'Getting started',
      collapsed: false,
      items: ['installation', 'quick-start', 'init'],
    },
    {
      type: 'category',
      label: 'Guides',
      collapsed: false,
      items: [
        'usage',
        'explain',
        'dependencies',
        'risk-metrics',
        'sbom',
        'ci-integration',
        'caching-and-rate-limits',
      ],
    },
    {
      type: 'category',
      label: 'Reference',
      collapsed: false,
      items: ['configuration', 'risk-gates', 'risk-signals', 'cli-reference'],
    },
    'roadmap',
  ],
};

export default sidebars;
