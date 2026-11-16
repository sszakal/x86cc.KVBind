import type { Config } from '@docusaurus/types';
import type * as Preset from '@docusaurus/preset-classic';

const repoUrl = 'https://github.com/sszakal/x86cc.KVBind';

const config: Config = {
  title: 'KVBind',
  tagline: 'A Git-inspired runtime for versioned business object graphs',
  favicon: 'img/logo.svg',

  url: 'https://kvbind.x86.cc',
  baseUrl: '/',

  organizationName: 'sszakal',
  projectName: 'x86cc.KVBind',
  trailingSlash: false,

  onBrokenLinks: 'throw',

  i18n: {
    defaultLocale: 'en',
    locales: ['en'],
  },

  markdown: {
    mermaid: true,
    hooks: {
      onBrokenMarkdownLinks: 'throw',
    },
  },

  // injectFavicon: false — Cosmos otherwise injects its own atom favicon over ours (img/logo.svg).
  themes: [['cosmos-docusaurus-theme', { injectFavicon: false }], '@docusaurus/theme-mermaid'],

  presets: [
    [
      'classic',
      {
        docs: {
          path: 'content',
          routeBasePath: '/',
          sidebarPath: './sidebars.ts',
          editUrl: `${repoUrl}/tree/main/docs/`,
        },
        blog: false,
        theme: {
          customCss: './src/css/custom.css',
        },
      } satisfies Preset.Options,
    ],
  ],

  themeConfig: {
    colorMode: {
      defaultMode: 'dark',
      respectPrefersColorScheme: true,
    },
    navbar: {
      title: 'KVBind',
      logo: {
        alt: 'KVBind',
        src: 'img/logo.svg',
      },
      items: [
        {
          type: 'docSidebar',
          sidebarId: 'docs',
          position: 'left',
          label: 'Documentation',
        },
        {
          href: `${repoUrl}/blob/main/CHANGELOG.md`,
          label: 'Changelog',
          position: 'right',
        },
        {
          href: repoUrl,
          label: 'GitHub',
          position: 'right',
        },
      ],
    },
    footer: {
      style: 'dark',
      links: [
        {
          title: 'Docs',
          items: [
            { label: 'Getting started', to: '/getting-started' },
            { label: 'Patching', to: '/patching/operations' },
            { label: 'Schema migrations', to: '/workflows/migrations' },
          ],
        },
        {
          title: 'Project',
          items: [
            { label: 'GitHub', href: repoUrl },
            { label: 'Changelog', href: `${repoUrl}/blob/main/CHANGELOG.md` },
            { label: 'Samples', href: `${repoUrl}/tree/main/samples` },
          ],
        },
      ],
      copyright: `Copyright © ${new Date().getFullYear()} x86cc.`,
    },
    prism: {
      additionalLanguages: ['csharp', 'bash', 'json', 'http'],
    },
  } satisfies Preset.ThemeConfig,
};

export default config;
