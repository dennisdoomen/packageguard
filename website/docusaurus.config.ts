import {themes as prismThemes} from 'prism-react-renderer';
import type {Config} from '@docusaurus/types';
import type * as Preset from '@docusaurus/preset-classic';

// This runs in Node.js - Don't use client-side code here (browser APIs, JSX...)

const config: Config = {
  title: 'PackageGuard',
  tagline: 'Get a grip on your open-source packages',
  favicon: 'img/favicon.png',

  // Future flags, see https://docusaurus.io/docs/api/docusaurus-config#future
  future: {
    v4: true, // Improve compatibility with the upcoming Docusaurus v4
  },

  // Set the production url of your site here
  url: 'https://packageguard.org',
  // Set the /<baseUrl>/ pathname under which your site is served
  // For GitHub pages deployment, it is often '/<projectName>/'
  baseUrl: '/',

  // GitHub pages deployment config.
  // If you aren't using GitHub pages, you don't need these.
  organizationName: 'dennisdoomen', // Usually your GitHub org/user name.
  projectName: 'packageguard', // Usually your repo name.

  onBrokenLinks: 'throw',

  headTags: [
    {
      tagName: 'link',
      attributes: {
        rel: 'preconnect',
        href: 'https://fonts.googleapis.com',
      },
    },
    {
      tagName: 'link',
      attributes: {
        rel: 'preconnect',
        href: 'https://fonts.gstatic.com',
        crossorigin: 'anonymous',
      },
    },
    {
      tagName: 'link',
      attributes: {
        rel: 'stylesheet',
        href: 'https://fonts.googleapis.com/css2?family=JetBrains+Mono:wght@400;500;600&family=Manrope:wght@400;500;600;700&family=Space+Grotesk:wght@500;600;700&display=swap',
      },
    },
  ],

  // Even if you don't use internationalization, you can use this field to set
  // useful metadata like html lang. For example, if your site is Chinese, you
  // may want to replace "en" with "zh-Hans".
  i18n: {
    defaultLocale: 'en',
    locales: ['en'],
  },

  presets: [
    [
      'classic',
      {
        docs: {
          sidebarPath: './sidebars.ts',
          editUrl:
            'https://github.com/dennisdoomen/packageguard/tree/main/website/',
        },
        blog: false,
        theme: {
          customCss: './src/css/custom.css',
        },
      } satisfies Preset.Options,
    ],
  ],

  themeConfig: {
    // Replace with your project's social card
    image: 'img/logo.png',
    colorMode: {
      respectPrefersColorScheme: true,
    },
    navbar: {
      title: 'PackageGuard',
      logo: {
        alt: 'PackageGuard Logo',
        src: 'img/logo.png',
      },
      items: [
        {
          type: 'docSidebar',
          sidebarId: 'tutorialSidebar',
          position: 'left',
          label: 'Documentation',
        },
        {
          href: 'https://github.com/dennisdoomen/packageguard',
          label: 'GitHub',
          position: 'right',
        },
        {
          href: 'https://www.nuget.org/packages/packageguard',
          label: 'NuGet',
          position: 'right',
        },
      ],
    },
    footer: {
      style: 'dark',
      links: [
        {
          title: 'Documentation',
          items: [
            {
              label: 'Installation',
              to: '/docs/installation',
            },
            {
              label: 'Configuration',
              to: '/docs/configuration',
            },
            {
              label: 'Risk Metrics',
              to: '/docs/risk-metrics',
            },
          ],
        },
        {
          title: 'Community',
          items: [
            {
              label: 'GitHub Issues',
              href: 'https://github.com/dennisdoomen/packageguard/issues',
            },
            {
              label: 'Discussions',
              href: 'https://github.com/dennisdoomen/packageguard/discussions',
            },
          ],
        },
        {
          title: 'More',
          items: [
            {
              label: 'NuGet',
              href: 'https://www.nuget.org/packages/packageguard',
            },
            {
              label: 'GitHub',
              href: 'https://github.com/dennisdoomen/packageguard',
            },
            {
              label: 'FluentAssertions',
              href: 'https://fluentassertions.com/',
            },
          ],
        },
      ],
      copyright: `Copyright © ${new Date().getFullYear()} Dennis Doomen. Built with Docusaurus.`,
    },
    prism: {
      theme: prismThemes.github,
      darkTheme: prismThemes.nightOwl,
      additionalLanguages: ['csharp', 'bash', 'powershell', 'json'],
    },
  } satisfies Preset.ThemeConfig,
};

export default config;
