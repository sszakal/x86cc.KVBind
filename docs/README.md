# KVBind documentation site

The KVBind documentation, built into a static site with [Docusaurus](https://docusaurus.io/) and the [Cosmos](https://sckyzo.github.io/cosmos-docusaurus-theme) theme, with Mermaid diagrams rendered inline. It is published to GitHub Pages at <https://kvbind.x86.cc/>.

The setup follows the `x86cc.Documentation` template; content lives in `docs/content/` (not `docs/docs/`).

## Local development

Node is pinned with [mise](https://mise.jdx.dev/) in `mise.toml` (Node 24); any Node 18+ works.

```bash
cd docs
mise install   # optional
npm install
npm start      # dev server with hot reload at http://localhost:3000/
```

Other commands:

```bash
npm run build   # production build -> build/ (fails on broken internal links)
npm run serve   # serve the production build locally
npm run clear   # clear the Docusaurus cache if nav/config changes don't show up
```

## Building with Docker

```bash
docker build -t x86cc-kvbind-docs docs
docker create --name extract x86cc-kvbind-docs
docker cp extract:/app/build ./docs/build
docker rm extract
```

This is the same build the deploy workflow runs.

## Writing content

- One Markdown file per page under `content/`, with `title` / `sidebar_position` frontmatter. `content/intro.md` is the site root (`slug: /`).
- **The sidebar is curated by hand**: a new page appears in the navigation only once it is listed in `sidebars.ts`.
- Pages are MDX: write `<id>` or `{id}` inside backticks or code blocks, or they are parsed as JSX.
- Fenced ` ```mermaid ` blocks render as diagrams.
- Link to other pages by their route (`/patching/operations`); the build fails on broken links.

## Deployment

`.github/workflows/docs.yml` builds the site with the Dockerfile in this folder and deploys it to GitHub Pages on every push to `main` that touches `docs/` (or manually from the Actions tab). Pull requests run the build only.

One-time setup: in the repository's **Settings → Pages**, set **Source** to **GitHub Actions**.

If the repository is renamed or moved, update `url`, `baseUrl` (the custom domain `kvbind.x86.cc` is set in Settings → Pages), `organizationName`, `projectName` and `repoUrl` in `docusaurus.config.ts`.
