# CI

Workflows run in Docker Hub's `node:24-bookworm` image. [mise](https://mise.jdx.dev) installs the tools from `mise.toml` and, with `MISE_ENV=ci`, from `mise.ci.toml`: the .NET SDK, changie, jq and yq. Locally only changie gets installed, the plugin builds in the .NET SDK container (`just build`).

| Workflow | Runs on | Runs what |
|---|---|---|
| `build.yml` | push to `main`, every PR | builds the plugin and dry-runs a batch of the pending release notes |
| `release.yml` | manual dispatch on `main` | batches the release notes, bumps the version, packages the plugin, adds it to `manifest.json`, commits and tags, then publishes the Forgejo release |

**Writing release notes:** run `changie new` ([changie](https://changie.dev)) and commit the fragment it writes to `.changes/unreleased/` with the change it describes. `Added`, `Changed`, `Deprecated` and `Removed` bump the minor version, `Fixed` and `Security` bump the patch. Nothing bumps major; 1.0 is a deliberate `major` dispatch.

**Cutting a release:** dispatch `release.yml`. The default `auto` bump picks the version from the pending fragments and fails if there are none; `patch`, `minor` or `major` force a bump. The job:

1. batches the fragments into `.changes/vX.Y.Z.md`, regenerates `CHANGELOG.md` and sets `X.Y.Z.0` in `Directory.Build.props` and `build.yaml` (changie's `replacements`),
2. builds the plugin and packages `time-is-up_X.Y.Z.0.zip`: the files listed under `artifacts` in `build.yaml` and a `meta.json`,
3. adds the version to `manifest.json` with the zip's download URL and MD5 (`scripts/package.sh`),
4. commits `chore(release): vX.Y.Z` and pushes tag `vX.Y.Z`,
5. publishes the Forgejo release with the notes as the body and the zip attached (`scripts/publish-release.sh`).

`changie latest` and the version in `Directory.Build.props` must agree, or the job stops before writing anything.

**Tokens:** the release commit, the tag and the release itself all use the workflow's automatic token (`FORGEJO_TOKEN`), which has write access to this repository. The only secret is optional: `MISE_TOKEN`, a GitHub token so mise's downloads aren't rate limited.

## Plugin repository

Jellyfin installs and updates the plugin from `manifest.json` on `main`:

```
https://code.hosaka.cc/hosaka/jellyfin-plugin-timeisup/raw/branch/main/manifest.json
```

Each version in it points at the zip attached to its Forgejo release.
