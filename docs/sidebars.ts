import type { SidebarsConfig } from '@docusaurus/plugin-content-docs';

const sidebars: SidebarsConfig = {
  docs: [
    'intro',
    'getting-started',
    {
      type: 'category',
      label: 'Concepts',
      items: ['concepts/mental-model', 'concepts/paths', 'concepts/source-generation'],
    },
    {
      type: 'category',
      label: 'Definition DSL',
      items: [
        'dsl/fields-and-groups',
        'dsl/allowed-values',
        'dsl/collections',
        'dsl/nested-nodes',
        'dsl/defaults-and-reset',
        'dsl/patch-read-only',
      ],
    },
    {
      type: 'category',
      label: 'Behavior',
      items: ['behavior/change-reactions', 'behavior/validation', 'behavior/domain-events'],
    },
    {
      type: 'category',
      label: 'Patching',
      items: ['patching/operations', 'patching/custom-operations'],
    },
    {
      type: 'category',
      label: 'Workflows',
      items: ['workflows/drafts-and-rebase', 'workflows/inheritance', 'workflows/migrations'],
    },
    {
      type: 'category',
      label: 'Integration',
      items: ['integration/definitions', 'integration/persistence', 'integration/web-api'],
    },
    'schema',
    {
      type: 'category',
      label: 'Advanced',
      items: ['advanced/definitions'],
    },
    'samples',
  ],
};

export default sidebars;
